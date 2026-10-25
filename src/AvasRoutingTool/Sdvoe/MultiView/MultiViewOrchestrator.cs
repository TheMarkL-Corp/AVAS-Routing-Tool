using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvasRoutingTool.Logging;

namespace AvasRoutingTool.Sdvoe.MultiView
{
    /// <param name="Plan">The layout in force after the call (it can change if the display changed).</param>
    /// <param name="PlanChanged">True when the display's EDID gave a different canvas than the one the card was showing.</param>
    public sealed record MultiViewResult(
        bool Success,
        string? Error = null,
        IReadOnlyList<int>? ClearedSlots = null,
        MultiViewLayoutPlan? Plan = null,
        bool PlanChanged = false)
    {
        public static MultiViewResult Fail(string error, MultiViewLayoutPlan? plan = null, bool planChanged = false) =>
            new(false, error, null, plan, planChanged);
    }

    /// <summary>
    /// Owns the multiview state and hardware sequence of every decoder.
    /// The output format always comes from the decoder's display: the EDID is read when a layout is
    /// applied and again right before the first hardware apply (the display may have been swapped).
    /// Layouts are applied lazily - <see cref="BeginLayoutAsync"/> changes no device; the first
    /// <see cref="AssignAsync"/> prepares each source's scaled stream, creates the layout and loads it
    /// (the API rejects an empty subscription list).
    /// </summary>
    public sealed class MultiViewOrchestrator
    {
        private sealed class Session
        {
            public Session(MultiViewLayoutPlan plan, MultiViewCanvas canvas)
            {
                Plan = plan;
                Canvas = canvas;
            }

            public MultiViewLayoutPlan Plan { get; set; }
            public MultiViewCanvas Canvas { get; set; }
            public Dictionary<int, AvasDevice> Assignments { get; } = new();
            public bool LayoutCreated { get; set; }

            /// <summary>Names of sources whose frame divider was refused (they run at full rate).</summary>
            public HashSet<string> DividerRefused { get; } = new();

            /// <summary>Stream 1 would exceed the bandwidth guideline at full rate, so sources are halved.</summary>
            public bool NeedsHalfRate => MultiViewCanvasPolicy.NeedsFrameDivider(Plan, Canvas.Fps);
        }

        private readonly ISdvoeClient _client;
        private readonly MultiViewCanvas _fallback;
        private readonly Dictionary<string, Session> _sessions = new();

        /// <summary>Sources whose frame divider this tool switched on (so only those are switched back).</summary>
        private readonly HashSet<string> _halvedByUs = new();
        private readonly SemaphoreSlim _gate = new(1, 1);

        /// <param name="fallback">Output format used when a display's EDID cannot be read.</param>
        public MultiViewOrchestrator(ISdvoeClient client, MultiViewCanvas? fallback = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _fallback = fallback ?? MultiViewCanvasPolicy.DefaultFallback;
        }

        public bool IsMultiView(AvasDevice rx) => _sessions.ContainsKey(Key(rx));

        public MultiViewLayoutPlan? GetPlan(AvasDevice rx) =>
            _sessions.TryGetValue(Key(rx), out var s) ? s.Plan : null;

        public MultiViewCanvas? GetCanvas(AvasDevice rx) =>
            _sessions.TryGetValue(Key(rx), out var s) ? s.Canvas : null;

        /// <summary>One line describing the output format (and a bandwidth note when high), for the card.</summary>
        public string DescribeCanvas(AvasDevice rx)
        {
            if (!_sessions.TryGetValue(Key(rx), out var s)) return string.Empty;

            string text = MultiViewCanvasPolicy.Summarize(s.Canvas, s.Plan);
            if (s.DividerRefused.Count > 0)
                text += $" · frame divider refused by {string.Join(", ", s.DividerRefused.OrderBy(n => n))}; running at full rate";
            return text;
        }

        public IReadOnlyDictionary<int, AvasDevice> GetAssignments(AvasDevice rx) =>
            _sessions.TryGetValue(Key(rx), out var s)
                ? new Dictionary<int, AvasDevice>(s.Assignments)
                : new Dictionary<int, AvasDevice>();

        /// <summary>
        /// Selects a layout for a decoder with every window empty, sized to the display's EDID.
        /// Only reads from the device; nothing is changed until the first source is assigned.
        /// </summary>
        public async Task<MultiViewLayoutPlan> BeginLayoutAsync(AvasDevice rx, string layoutId)
        {
            var canvas = await ResolveCanvasAsync(rx);
            var plan = MultiViewPresets.Build(layoutId, canvas.Width, canvas.Height);
            _sessions[Key(rx)] = new Session(plan, canvas);
            return plan;
        }

        public async Task<MultiViewResult> AssignAsync(AvasDevice rx, int slotIndex, AvasDevice tx)
        {
            await _gate.WaitAsync();
            try
            {
                if (!_sessions.TryGetValue(Key(rx), out var session))
                    return MultiViewResult.Fail($"'{rx.DeviceName}' has no multiview layout selected.");

                // Before the first hardware apply, check the display again: it may have been swapped.
                bool planChanged = false;
                if (!session.LayoutCreated)
                {
                    var canvas = await ResolveCanvasAsync(rx);
                    if (canvas.Width != session.Canvas.Width || canvas.Height != session.Canvas.Height || canvas.Fps != session.Canvas.Fps)
                    {
                        AppLogger.Info("MULTIVIEW", $"'{rx.DeviceName}' display changed: {session.Canvas.Description} -> {canvas.Description}");
                        session.Plan = MultiViewPresets.Build(session.Plan.Id, canvas.Width, canvas.Height);
                        planChanged = true;
                    }
                    session.Canvas = canvas;
                }

                var plan = session.Plan;
                MultiViewResult Fail(string error) => MultiViewResult.Fail(error, plan, planChanged);

                var slot = plan.Slots.FirstOrDefault(s => s.Index == slotIndex);
                if (slot == null)
                    return Fail($"Layout '{plan.Id}' has no window {slotIndex}.");

                if (!tx.IsTransmitter)
                    return Fail($"'{tx.DeviceName}' is not a transmitter.");
                if (!tx.IsActive)
                    return Fail($"'{tx.DeviceName}' is offline.");

                // A source can only appear once per layout (duplicate streams are ILLEGAL_ARGUMENT), so a
                // source already in another window of this layout is moved rather than duplicated.
                var movedFrom = session.Assignments
                    .Where(a => a.Key != slotIndex && SameDevice(a.Value, tx))
                    .Select(a => a.Key).ToList();

                string? conflict = FindScalerConflict(session, slot, tx, movedFrom);
                if (conflict != null) return Fail(conflict);

                if (!await _client.PrepareSourceScalerAsync(tx.DeviceId, slot.Width, slot.Height))
                    return Fail($"Could not prepare '{tx.DeviceName}' for a {slot.Width}x{slot.Height} window.");

                // Stream 1 at half frame rate when the layout would otherwise exceed the bandwidth guideline.
                // A source without the divider simply keeps running at full rate; that is not fatal.
                if (session.NeedsHalfRate)
                {
                    // Remember whether the divider was already on, so we only ever undo our own change.
                    bool alreadyHalved = await _client.ReadStream1FrameDividerAsync(tx.DeviceId) == true;
                    if (await _client.SetStream1FrameDividerAsync(tx.DeviceId, true))
                    {
                        if (!alreadyHalved) _halvedByUs.Add(Key(tx));
                        session.DividerRefused.Remove(tx.DeviceName);
                    }
                    else
                    {
                        AppLogger.Warn("MULTIVIEW", $"'{tx.DeviceName}' did not accept the frame divider; stream 1 stays at full rate.");
                        session.DividerRefused.Add(tx.DeviceName);
                    }
                }

                if (!session.LayoutCreated)
                {
                    if (!await _client.CreateMultiViewLayoutAsync(plan))
                        return Fail($"Could not create layout '{plan.Name}'.");
                    session.LayoutCreated = true;
                }

                var next = new Dictionary<int, AvasDevice>(session.Assignments);
                foreach (int old in movedFrom) next.Remove(old);
                next[slotIndex] = tx;

                var subscriptions = next
                    .Select(a => new MultiViewSubscription(a.Value.DeviceId,
                        plan.Slots.First(s => s.Index == a.Key).TargetSurface))
                    .ToList();

                if (!await _client.SetMultiViewAsync(rx.DeviceId, plan, session.Canvas.Fps, subscriptions))
                    return Fail($"'{rx.DeviceName}' rejected layout '{plan.Name}'.");

                session.Assignments.Clear();
                foreach (var a in next) session.Assignments[a.Key] = a.Value;

                AppLogger.Info("MULTIVIEW", $"'{tx.DeviceName}' -> window {slot.Label} of '{rx.DeviceName}' ({plan.Id}, {session.Canvas.Description})");
                return new MultiViewResult(true, null, movedFrom, plan, planChanged);
            }
            finally
            {
                await RestoreUnusedDividersAsync();
                _gate.Release();
            }
        }

        /// <summary>Leaves multiview: the decoder goes back to full-screen genlock, ready for a normal join.</summary>
        public async Task<bool> ReturnToSingleViewAsync(AvasDevice rx)
        {
            await _gate.WaitAsync();
            try
            {
                if (!_sessions.ContainsKey(Key(rx))) return true;

                bool ok = await _client.SetGenlockedAsync(rx.DeviceId);
                if (ok)
                {
                    _sessions.Remove(Key(rx));
                    await RestoreUnusedDividersAsync();
                }
                return ok;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Drops every window's subscription, then returns the decoder to genlock.</summary>
        public async Task<bool> TurnOffAsync(AvasDevice rx)
        {
            await _gate.WaitAsync();
            try
            {
                if (!_sessions.TryGetValue(Key(rx), out var session)) return true;

                foreach (var assignment in session.Assignments)
                {
                    int surface = session.Plan.Slots.First(s => s.Index == assignment.Key).TargetSurface;
                    if (!await _client.LeaveStreamAsync(rx.DeviceId, "HDMI", surface))
                        AppLogger.Warn("MULTIVIEW", $"Leave of subscription {surface} on '{rx.DeviceName}' was not accepted.");
                }

                bool ok = await _client.SetGenlockedAsync(rx.DeviceId);
                if (ok)
                {
                    _sessions.Remove(Key(rx));
                    await RestoreUnusedDividersAsync();
                }
                return ok;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Switches stream 1 back to full frame rate for every source this tool halved and no heavy layout
        /// uses any more. Sources whose divider was already on before we started are never touched.
        /// </summary>
        private async Task RestoreUnusedDividersAsync()
        {
            foreach (string mac in _halvedByUs.ToList())
            {
                bool stillNeeded = _sessions.Values.Any(s => s.NeedsHalfRate && s.Assignments.Values.Any(a => Key(a) == mac));
                if (stillNeeded) continue;

                if (await _client.SetStream1FrameDividerAsync(mac, false))
                    _halvedByUs.Remove(mac);
                else
                    AppLogger.Warn("MULTIVIEW", $"Could not restore full frame rate on {mac}; will retry later.");
            }
        }

        /// <summary>Reads the display's EDID and picks the output format; falls back to the default when it can't.</summary>
        private async Task<MultiViewCanvas> ResolveCanvasAsync(AvasDevice rx)
        {
            string? hex = null;
            try
            {
                hex = await _client.ReadDisplayEdidAsync(rx.DeviceId);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("EDID", $"EDID read for '{rx.DeviceName}' threw: {ex.Message}");
            }

            var edid = EdidInfo.Parse(hex);
            var canvas = MultiViewCanvasPolicy.Choose(edid, _fallback);
            AppLogger.Info("MULTIVIEW", $"'{rx.DeviceName}' output: {canvas.Description}");
            return canvas;
        }

        /// <summary>
        /// One TX has one scaled stream, so every window that shows it must want the same size,
        /// on every decoder.
        /// </summary>
        private string? FindScalerConflict(Session session, WindowSlot slot, AvasDevice tx, List<int> movedFrom)
        {
            foreach (var other in _sessions.Values)
            {
                foreach (var (index, device) in other.Assignments)
                {
                    if (!SameDevice(device, tx)) continue;
                    if (ReferenceEquals(other, session) && (index == slot.Index || movedFrom.Contains(index))) continue;

                    var existing = other.Plan.Slots.First(s => s.Index == index);
                    if (existing.Width != slot.Width || existing.Height != slot.Height)
                    {
                        return $"'{tx.DeviceName}' is already scaled to {existing.Width}x{existing.Height} for another multiview window; " +
                               $"this window needs {slot.Width}x{slot.Height}. A source can only have one scaled size.";
                    }
                }
            }
            return null;
        }

        private static bool SameDevice(AvasDevice a, AvasDevice b) => Key(a) == Key(b);

        private static string Key(AvasDevice d) => SdvoeClient.NormalizeMac(d.DeviceId);
    }
}
