using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvasRoutingTool.Logging;

namespace AvasRoutingTool.Sdvoe.MultiView
{
    public enum TakeOutcome { Applied, NothingToTake, DisplayChanged, Rejected }

    /// <param name="Error">Operator-readable reason for <see cref="TakeOutcome.Rejected"/> or <see cref="TakeOutcome.DisplayChanged"/>.</param>
    /// <param name="NewPlan">For <see cref="TakeOutcome.DisplayChanged"/>: the same layout rebuilt for the display now attached.</param>
    public sealed record TakeResult(TakeOutcome Outcome, string? Error = null, MultiViewLayoutPlan? NewPlan = null)
    {
        public static TakeResult Reject(string error) => new(TakeOutcome.Rejected, error);
    }

    /// <summary>
    /// Owns what each decoder shows in multiview and the hardware sequence that puts it there.
    /// A whole staged draft goes live at once (<see cref="TakeAsync"/>): the display's EDID is read again, every
    /// distinct encoder is scaled (and halved when the layout is heavy), the layout is written and loaded with one
    /// <c>set:multiview</c>. A refused Take leaves what is on screen unchanged; the previous live draft is kept for
    /// a single-level <see cref="UndoAsync"/>.
    /// </summary>
    public sealed class MultiViewOrchestrator
    {
        private const string NothingPlacedText = "Place at least one source (Step 2).";

        /// <summary>One display that has a live multiview. Exists only while <see cref="Live"/> is on its screen.</summary>
        private sealed class Session
        {
            public Session(string displayName, MultiViewDraft live, MultiViewCanvas canvas)
            {
                DisplayName = displayName;
                Live = live;
                Canvas = canvas;
            }

            public string DisplayName { get; set; }

            /// <summary>What is on the screen now.</summary>
            public MultiViewDraft Live { get; set; }

            /// <summary>What was on the screen before the last Take, for Undo.</summary>
            public MultiViewDraft? Previous { get; set; }

            public MultiViewCanvas Canvas { get; set; }

            /// <summary>Names of sources whose frame divider was refused (they run at full rate).</summary>
            public IReadOnlySet<string> DividerRefused { get; set; } = new HashSet<string>();

            /// <summary>
            /// HDMI subscription indexes really joined on the decoder: what the last <c>set:multiview</c> sent, or what
            /// was read back when the layout was adopted. Off leaves all of them.
            /// </summary>
            public IReadOnlySet<int> Joined { get; set; } = new HashSet<int>();

            /// <summary>Stream 1 would exceed the bandwidth guideline at full rate, so sources are halved.</summary>
            public bool NeedsHalfRate => MultiViewCanvasPolicy.NeedsFrameDivider(Live.Plan, Canvas.Fps);
        }

        /// <summary>What a successful send leaves behind, to be committed to the session.</summary>
        private sealed record Sent(MultiViewCanvas Canvas, IReadOnlySet<string> DividerRefused, IReadOnlySet<int> Joined);

        private readonly ISdvoeClient _client;
        private readonly MultiViewCanvas _fallback;
        private readonly Dictionary<string, Session> _sessions = new();

        /// <summary>Sources whose frame divider this tool switched on (so only those are switched back).</summary>
        private readonly HashSet<string> _halvedByUs = new();
        private readonly SemaphoreSlim _gate = new(1, 1);

        /// <summary>
        /// Subscription indexes of a layout found running that this tool did not create (adopted read-only, by
        /// receiver key). The first Take on that display leaves those its new layout does not subscribe again.
        /// </summary>
        private readonly Dictionary<string, IReadOnlySet<int>> _external = new();

        /// <summary>
        /// One operation at a time per display (by receiver key): a Take, Undo, Off, return to Single View, or a
        /// caller's <see cref="RunExclusiveAsync{T}"/> section (a Single View route, a restore) never interleave on
        /// the same display. Taken before <see cref="_gate"/>, never while holding it.
        /// </summary>
        private readonly Dictionary<string, SemaphoreSlim> _displayLocks = new();

        /// <summary>The display keys whose lock the current async flow already holds (makes the lock re-entrant per flow).</summary>
        private readonly AsyncLocal<HashSet<string>?> _held = new();

        /// <param name="fallback">Output format used when a display's EDID cannot be read.</param>
        public MultiViewOrchestrator(ISdvoeClient client, MultiViewCanvas? fallback = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _fallback = fallback ?? MultiViewCanvasPolicy.DefaultFallback;
        }

        /// <summary>True when the display has a live multiview (taken or adopted).</summary>
        public bool IsMultiView(AvasDevice rx) => _sessions.ContainsKey(Key(rx));

        public MultiViewLayoutPlan? GetPlan(AvasDevice rx) => Find(rx)?.Live.Plan;

        public MultiViewCanvas? GetCanvas(AvasDevice rx) => Find(rx)?.Canvas;

        /// <summary>A copy of what is on the display's screen, or null when it has no live multiview.</summary>
        public MultiViewDraft? GetLive(AvasDevice rx) => Find(rx)?.Live.Clone();

        public bool CanUndo(AvasDevice rx) => Find(rx)?.Previous != null;

        /// <summary>One line describing the output format (and a bandwidth note when high), for the card.</summary>
        public string DescribeCanvas(AvasDevice rx)
        {
            var s = Find(rx);
            if (s == null) return string.Empty;

            string text = MultiViewCanvasPolicy.Summarize(s.Canvas, s.Live.Plan);
            if (s.DividerRefused.Count > 0)
                text += $" · frame divider refused by {string.Join(", ", s.DividerRefused.OrderBy(n => n))}; running at full rate";
            return text;
        }

        /// <summary>
        /// Every block of every other display's live layout: which encoder it shows at what size. One encoder has one
        /// scaler size, so these constrain what this display may take (<see cref="DraftValidator"/>).
        /// </summary>
        public IEnumerable<OtherDisplayUse> UsesExcept(AvasDevice rx)
        {
            string key = Key(rx);
            return _sessions
                .Where(pair => pair.Key != key)
                .SelectMany(pair => pair.Value.Live.Plan.Slots
                    .Where(slot => pair.Value.Live.Placements.ContainsKey(slot.Index))
                    .Select(slot => new OtherDisplayUse(
                        pair.Value.DisplayName, pair.Value.Live.Placements[slot.Index].SourceMac, slot.Width, slot.Height)))
                .ToList();
        }

        /// <summary>
        /// Registers a layout found already running on a decoder (restored from the hardware). Changes no device.
        /// Dividers already on are not ours and are never switched back.
        /// </summary>
        /// <param name="joinedSubscriptions">The HDMI subscription indexes read back as routed on the decoder.</param>
        public void Adopt(AvasDevice rx, MultiViewDraft live, MultiViewCanvas canvas, IEnumerable<int> joinedSubscriptions)
        {
            _external.Remove(Key(rx));
            _sessions[Key(rx)] = new Session(rx.DeviceName, live.Clone(), canvas) { Joined = joinedSubscriptions.ToHashSet() };
        }

        /// <summary>
        /// Registers a multiview layout this tool did not create, found running on a decoder (read-only: there is no
        /// live draft to drive). Changes no device. The first Take on the display leaves the given subscriptions that
        /// its own layout does not subscribe again, so no stream of the old layout stays joined.
        /// </summary>
        public void AdoptExternal(AvasDevice rx, IEnumerable<int> joinedSubscriptions)
        {
            _sessions.Remove(Key(rx));
            _external[Key(rx)] = joinedSubscriptions.ToHashSet();
        }

        /// <summary>Drops what we believe about a decoder (the hardware says otherwise). Changes no device.</summary>
        public void Forget(AvasDevice rx)
        {
            _sessions.Remove(Key(rx));
            _external.Remove(Key(rx));
        }

        /// <summary>
        /// Runs <paramref name="action"/> as the only operation on this display: a Take, Undo, Off or return to Single
        /// View issued meanwhile (here or from the Composer) waits until it ends. Calls to this orchestrator made from
        /// inside the section for the same display do not wait for the lock again (no deadlock). Other displays are
        /// not held. Work started inside the section but left running after it ends is not covered.
        /// </summary>
        public Task<T> RunExclusiveAsync<T>(AvasDevice rx, Func<Task<T>> action)
        {
            ArgumentNullException.ThrowIfNull(rx);
            ArgumentNullException.ThrowIfNull(action);
            return ExclusiveAsync(rx, action);
        }

        /// <summary>
        /// The layout sized to the display's EDID, and the output format it will run at. Only reads from the device:
        /// no session is created and nothing is changed.
        /// </summary>
        public async Task<(MultiViewLayoutPlan Plan, MultiViewCanvas Canvas)> PlanForDisplayAsync(AvasDevice rx, string layoutId)
        {
            var canvas = await ResolveCanvasAsync(rx);
            return (MultiViewPresets.Build(layoutId, canvas.Width, canvas.Height), canvas);
        }

        /// <summary>Puts the whole draft on the display's screen, or changes nothing.</summary>
        public Task<TakeResult> TakeAsync(AvasDevice rx, MultiViewDraft draft)
        {
            ArgumentNullException.ThrowIfNull(draft);
            return ExclusiveAsync(rx, () => TakeCoreAsync(rx, draft));
        }

        private async Task<TakeResult> TakeCoreAsync(AvasDevice rx, MultiViewDraft draft)
        {
            await _gate.WaitAsync();
            try
            {
                if (draft.Placements.Count == 0) return TakeResult.Reject(NothingPlacedText);

                var session = Find(rx);
                if (session != null && SameLayout(draft, session.Live)) return new TakeResult(TakeOutcome.NothingToTake);

                var taken = draft.Clone();
                var (result, sent) = await SendAsync(rx, taken);
                if (sent == null) return result;

                if (session == null)
                {
                    // Taking over a layout made elsewhere: its subscriptions count as joined, so the commit leaves
                    // those the new layout does not use.
                    session = new Session(rx.DeviceName, taken, sent.Canvas)
                    {
                        Joined = _external.TryGetValue(Key(rx), out var external) ? external : new HashSet<int>()
                    };
                    _sessions[Key(rx)] = session;
                    _external.Remove(Key(rx));
                }
                else
                {
                    session.Previous = session.Live;
                }
                await CommitAsync(session, rx, taken, sent);
                return result;
            }
            finally
            {
                await RestoreUnusedDividersAsync();
                _gate.Release();
            }
        }

        /// <summary>Takes the previous live draft again (single level: afterwards there is nothing left to undo).</summary>
        public Task<TakeResult> UndoAsync(AvasDevice rx) => ExclusiveAsync(rx, () => UndoCoreAsync(rx));

        private async Task<TakeResult> UndoCoreAsync(AvasDevice rx)
        {
            await _gate.WaitAsync();
            try
            {
                var session = Find(rx);
                if (session?.Previous == null) return new TakeResult(TakeOutcome.NothingToTake);

                var previous = session.Previous;
                var (result, sent) = await SendAsync(rx, previous);
                if (sent == null) return result;

                session.Previous = null;
                await CommitAsync(session, rx, previous, sent);
                return result;
            }
            finally
            {
                await RestoreUnusedDividersAsync();
                _gate.Release();
            }
        }

        /// <summary>
        /// Leaves a multiview this tool did not start (an external layout): optionally leaves the given
        /// subscriptions, then returns the decoder to full-screen genlock.
        /// </summary>
        public Task<bool> ExitExternalAsync(AvasDevice rx, IEnumerable<int> subscriptionIndexesToLeave)
        {
            var toLeave = subscriptionIndexesToLeave.ToList();
            return ExclusiveAsync(rx, async () =>
            {
                await _gate.WaitAsync();
                try
                {
                    foreach (int index in toLeave)
                        await LeaveAsync(rx, index);
                    bool ok = await _client.SetGenlockedAsync(rx.DeviceId);
                    if (ok) _external.Remove(Key(rx));
                    return ok;
                }
                finally
                {
                    _gate.Release();
                }
            });
        }

        /// <summary>Leaves multiview: the decoder goes back to full-screen genlock, ready for a normal join.</summary>
        public Task<bool> ReturnToSingleViewAsync(AvasDevice rx) => ExclusiveAsync(rx, async () =>
        {
            await _gate.WaitAsync();
            try
            {
                if (Find(rx) == null) return true;
                return await GenlockAndEndAsync(rx);
            }
            finally
            {
                _gate.Release();
            }
        });

        /// <summary>Leaves every subscription joined on the decoder, then returns it to genlock.</summary>
        public Task<bool> TurnOffAsync(AvasDevice rx) => ExclusiveAsync(rx, async () =>
        {
            await _gate.WaitAsync();
            try
            {
                var session = Find(rx);
                if (session == null) return true;

                foreach (int index in session.Joined.OrderBy(i => i))
                    await LeaveAsync(rx, index);

                return await GenlockAndEndAsync(rx);
            }
            finally
            {
                _gate.Release();
            }
        });

        /// <summary>Runs <paramref name="action"/> holding the display's lock, unless this async flow already holds it.</summary>
        private async Task<T> ExclusiveAsync<T>(AvasDevice rx, Func<Task<T>> action)
        {
            string key = Key(rx);
            var held = _held.Value;
            if (held != null && held.Contains(key)) return await action();

            SemaphoreSlim gate;
            lock (_displayLocks)
            {
                if (!_displayLocks.TryGetValue(key, out gate!))
                    _displayLocks[key] = gate = new SemaphoreSlim(1, 1);
            }

            await gate.WaitAsync();
            // A copy: the parent flow's set is shared by reference and must not see this key.
            _held.Value = held == null ? new HashSet<string> { key } : new HashSet<string>(held) { key };
            try
            {
                return await action();
            }
            finally
            {
                _held.Value = held;
                gate.Release();
            }
        }

        /// <summary>
        /// The hardware part of a Take: EDID check, validation against the other displays, scalers (and dividers),
        /// layout, load. Returns <c>sent</c> only when the draft is now on the screen.
        /// </summary>
        private async Task<(TakeResult Result, Sent? Sent)> SendAsync(AvasDevice rx, MultiViewDraft draft)
        {
            var plan = draft.Plan;

            // The display may have been swapped since the layout was drawn: never send a layout for another canvas.
            var canvas = await ResolveCanvasAsync(rx);
            if (canvas.Width != plan.Width || canvas.Height != plan.Height)
            {
                AppLogger.Info("MULTIVIEW", $"'{rx.DeviceName}' display changed: {plan.Width}x{plan.Height} -> {canvas.Description}");
                MultiViewLayoutPlan redrawn;
                try
                {
                    redrawn = MultiViewPresets.Build(plan.Id, canvas.Width, canvas.Height);
                }
                catch (ArgumentException ex)
                {
                    return (TakeResult.Reject(ex.Message), null);
                }
                return (new TakeResult(TakeOutcome.DisplayChanged,
                    $"The display of '{rx.DeviceName}' changed to {canvas.Description}; the layout was redrawn for it.", redrawn), null);
            }

            var conflict = DraftValidator.Validate(draft, UsesExcept(rx), isOnline: _ => true)
                .FirstOrDefault(i => i.Kind == BlockIssueKind.SizeConflictOtherDisplay);
            if (conflict != null) return (TakeResult.Reject(conflict.Message), null);

            SurfacePlan surfaces;
            try
            {
                surfaces = SurfacePlanner.Plan(draft, rx.DeviceId);
            }
            catch (InvalidOperationException ex)
            {
                return (TakeResult.Reject(ex.Message), null);
            }

            // Stream 1 at half frame rate when the layout would otherwise exceed the bandwidth guideline.
            // A source without the divider simply keeps running at full rate; that is not fatal.
            bool halve = MultiViewCanvasPolicy.NeedsFrameDivider(plan, canvas.Fps);
            var refused = new HashSet<string>();
            foreach (var source in surfaces.Sources)
            {
                string name = draft.Placements.Values.First(p => SdvoeClient.NormalizeMac(p.SourceMac) == source.Mac).SourceName;
                if (!await _client.PrepareSourceScalerAsync(source.Mac, source.Width, source.Height))
                    return (TakeResult.Reject($"Could not prepare '{name}' for a {source.Width}x{source.Height} window."), null);

                if (halve && !await HalveAsync(source.Mac, name))
                    refused.Add(name);
            }

            if (!await _client.CreateMultiViewLayoutAsync(surfaces, plan))
                return (TakeResult.Reject($"Could not create layout '{surfaces.LayoutName}'."), null);

            var subscriptions = surfaces.Sources.Select(s => new MultiViewSubscription(s.Mac, s.Surface)).ToList();
            if (!await _client.SetMultiViewAsync(rx.DeviceId, surfaces.LayoutName, plan.Width, plan.Height, canvas.Fps, subscriptions))
                return (TakeResult.Reject($"'{rx.DeviceName}' rejected layout '{surfaces.LayoutName}'."), null);

            AppLogger.Info("MULTIVIEW", $"'{rx.DeviceName}' now shows {plan.Id} with {subscriptions.Count} source(s) ({canvas.Description})");
            return (new TakeResult(TakeOutcome.Applied), new Sent(canvas, refused, subscriptions.Select(s => s.SubscriptionIndex).ToHashSet()));
        }

        /// <summary>Switches a source's stream 1 to half rate; false when the device refused.</summary>
        private async Task<bool> HalveAsync(string mac, string name)
        {
            // Remember whether the divider was already on, so we only ever undo our own change.
            bool alreadyHalved = await _client.ReadStream1FrameDividerAsync(mac) == true;
            if (await _client.SetStream1FrameDividerAsync(mac, true))
            {
                if (!alreadyHalved) _halvedByUs.Add(SdvoeClient.NormalizeMac(mac));
                return true;
            }

            AppLogger.Warn("MULTIVIEW", $"'{name}' did not accept the frame divider; stream 1 stays at full rate.");
            return false;
        }

        /// <summary>
        /// Records what is now on the screen, then leaves (best effort) the subscriptions the new layout no longer
        /// uses, so no stream stays joined after it dropped out of the layout.
        /// </summary>
        private async Task CommitAsync(Session session, AvasDevice rx, MultiViewDraft live, Sent sent)
        {
            var droppedOut = session.Joined.Except(sent.Joined).OrderBy(i => i).ToList();

            session.DisplayName = rx.DeviceName;
            session.Live = live;
            session.Canvas = sent.Canvas;
            session.DividerRefused = sent.DividerRefused;
            session.Joined = sent.Joined;

            foreach (int index in droppedOut)
                await LeaveAsync(rx, index);
        }

        /// <summary>Best effort: a leave that is refused or throws is logged, never fatal.</summary>
        private async Task LeaveAsync(AvasDevice rx, int subscriptionIndex)
        {
            try
            {
                if (!await _client.LeaveStreamAsync(rx.DeviceId, "HDMI", subscriptionIndex))
                    AppLogger.Warn("MULTIVIEW", $"Leave of subscription {subscriptionIndex} on '{rx.DeviceName}' was not accepted.");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("MULTIVIEW", $"Leave of subscription {subscriptionIndex} on '{rx.DeviceName}' failed: {ex.Message}");
            }
        }

        private async Task<bool> GenlockAndEndAsync(AvasDevice rx)
        {
            bool ok = await _client.SetGenlockedAsync(rx.DeviceId);
            if (ok)
            {
                _sessions.Remove(Key(rx));
                await RestoreUnusedDividersAsync();
            }
            return ok;
        }

        /// <summary>
        /// Switches stream 1 back to full frame rate for every source this tool halved and no live heavy layout
        /// uses any more. Sources whose divider was already on before we started are never touched.
        /// </summary>
        private async Task RestoreUnusedDividersAsync()
        {
            foreach (string mac in _halvedByUs.ToList())
            {
                bool stillNeeded = _sessions.Values.Any(s =>
                    s.NeedsHalfRate && s.Live.Placements.Values.Any(p => SdvoeClient.NormalizeMac(p.SourceMac) == mac));
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

        /// <summary>Same layout and the same encoder in every block.</summary>
        private static bool SameLayout(MultiViewDraft a, MultiViewDraft b) =>
            a.Plan.Name == b.Plan.Name && a.DiffFrom(b).Count == 0;

        private Session? Find(AvasDevice rx) => _sessions.TryGetValue(Key(rx), out var s) ? s : null;

        private static string Key(AvasDevice d) => SdvoeClient.NormalizeMac(d.DeviceId);
    }
}
