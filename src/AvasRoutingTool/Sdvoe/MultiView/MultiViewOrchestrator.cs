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
    /// Outcome of copying a display's picture onto another. <paramref name="Error"/> is the hardware's (or the
    /// planner's) reason when it failed; <paramref name="Now"/> is what the target shows after a successful copy.
    /// </summary>
    /// <param name="LeftAsItWas">
    /// For a failed copy: true when the target was not touched or was put back to what it showed; false when the
    /// copy had changed it and putting it back failed too (what it shows is then unknown).
    /// </param>
    public sealed record CopyResult(bool Ok, string? Error, ScreenState? Now = null, bool LeftAsItWas = true)
    {
        public static CopyResult Fail(string error) => new(false, error);
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

            /// <summary>The output format <see cref="Previous"/> ran at, and whether it was a copied display's (Undo goes back to it).</summary>
            public MultiViewCanvas? PreviousCanvas { get; set; }

            public bool PreviousCanvasIsCopy { get; set; }

            public MultiViewCanvas Canvas { get; set; }

            /// <summary>
            /// <see cref="Canvas"/> is a copied display's output format, not this display's EDID choice. A Take of a
            /// layout drawn at that size keeps it (while the EDID lists it), so a copy stays usable afterwards.
            /// </summary>
            public bool CanvasIsCopy { get; set; }

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
        private sealed record Sent(MultiViewCanvas Canvas, IReadOnlySet<string> DividerRefused, IReadOnlySet<int> Joined, bool CanvasIsCopy = false);

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

        /// <summary>Which encoder each externally adopted layout shows at what window size (by receiver key): one scaler size per encoder holds for these too.</summary>
        private readonly Dictionary<string, IReadOnlyList<OtherDisplayUse>> _externalUses = new();

        /// <summary>
        /// One operation at a time per display (by receiver key): a Take, Undo, Off, return to Single View, or a
        /// caller's <see cref="RunExclusiveAsync{T}"/> section (a Single View route, a restore) never interleave on
        /// the same display. Taken before <see cref="_gate"/>, never while holding it.
        /// </summary>
        private readonly Dictionary<string, SemaphoreSlim> _displayLocks = new();

        /// <summary>The display keys whose lock the current async flow already holds (makes the lock re-entrant per flow).</summary>
        private readonly AsyncLocal<HashSet<string>?> _held = new();

        /// <summary>A Single View copy routes exactly as a normal Single View route does.</summary>
        private readonly RoutingOrchestrator _routing;

        /// <summary>What each copy target showed just before its last copy, and when that copy happened (by receiver key).</summary>
        private readonly Dictionary<string, (ScreenState Screen, DateTime At)> _previousScreens = new();

        /// <summary>A copy target's previous picture can be put back for this long (spec 14.3).</summary>
        public static readonly TimeSpan CopyUndoWindow = TimeSpan.FromSeconds(10);

        /// <param name="fallback">Output format used when a display's EDID cannot be read.</param>
        public MultiViewOrchestrator(ISdvoeClient client, MultiViewCanvas? fallback = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _fallback = fallback ?? MultiViewCanvasPolicy.DefaultFallback;
            _routing = new RoutingOrchestrator(_client);
        }

        /// <summary>
        /// Time source for the copy Undo window. <c>MainViewModel</c> sets its own clock here, so the overview has one
        /// time source; tests may set one.
        /// </summary>
        public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

        /// <summary>True when the display has a live multiview (taken or adopted).</summary>
        public bool IsMultiView(AvasDevice rx) => _sessions.ContainsKey(Key(rx));

        public MultiViewLayoutPlan? GetPlan(AvasDevice rx) => Find(rx)?.Live.Plan;

        public MultiViewCanvas? GetCanvas(AvasDevice rx) => Find(rx)?.Canvas;

        /// <summary>The display's live multiview runs at a copied display's output format (not its own EDID choice).</summary>
        public bool CanvasIsCopy(AvasDevice rx) => Find(rx)?.CanvasIsCopy == true;

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
            var sessionUses = _sessions
                .Where(pair => pair.Key != key)
                .SelectMany(pair => pair.Value.Live.Plan.Slots
                    .Where(slot => pair.Value.Live.Placements.ContainsKey(slot.Index))
                    .Select(slot => new OtherDisplayUse(
                        pair.Value.DisplayName, pair.Value.Live.Placements[slot.Index].SourceMac, slot.Width, slot.Height)));
            var externalUses = _externalUses
                .Where(pair => pair.Key != key)
                .SelectMany(pair => pair.Value);
            return sessionUses.Concat(externalUses).ToList();
        }

        /// <summary>
        /// Registers a layout found already running on a decoder (restored from the hardware). Changes no device.
        /// Dividers already on are not ours and are never switched back.
        /// </summary>
        /// <param name="joinedSubscriptions">The HDMI subscription indexes read back as routed on the decoder.</param>
        public void Adopt(AvasDevice rx, MultiViewDraft live, MultiViewCanvas canvas, IEnumerable<int> joinedSubscriptions)
        {
            DropExternal(rx);
            _sessions[Key(rx)] = new Session(rx.DeviceName, live.Clone(), canvas) { Joined = joinedSubscriptions.ToHashSet() };
        }

        /// <summary>
        /// Registers a multiview layout this tool did not create, found running on a decoder (read-only: there is no
        /// live draft to drive). Changes no device. The first Take on the display leaves the given subscriptions that
        /// its own layout does not subscribe again, so no stream of the old layout stays joined.
        /// </summary>
        /// <param name="uses">
        /// The encoders its windows show, at the window sizes (<see cref="MultiViewRestore.ExternalUses"/>): they count
        /// for the other displays like a live layout's blocks do (one scaler size per encoder).
        /// </param>
        public void AdoptExternal(AvasDevice rx, IEnumerable<int> joinedSubscriptions, IEnumerable<OtherDisplayUse>? uses = null)
        {
            _sessions.Remove(Key(rx));
            _external[Key(rx)] = joinedSubscriptions.ToHashSet();
            SetExternalUses(rx, uses);
        }

        /// <summary>Drops what we believe about a decoder (the hardware says otherwise). Changes no device.</summary>
        public void Forget(AvasDevice rx)
        {
            _sessions.Remove(Key(rx));
            DropExternal(rx);
        }

        /// <summary>Forgets the external layout recorded for the display: its subscriptions and its encoder uses.</summary>
        private void DropExternal(AvasDevice rx)
        {
            _external.Remove(Key(rx));
            _externalUses.Remove(Key(rx));
        }

        private void SetExternalUses(AvasDevice rx, IEnumerable<OtherDisplayUse>? uses)
        {
            var list = uses?.ToList();
            if (list == null || list.Count == 0) _externalUses.Remove(Key(rx));
            else _externalUses[Key(rx)] = list;
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
            return ExclusiveAsync(rx, () => TakeCoreAsync(rx, draft, null));
        }

        /// <param name="copiedCanvas">
        /// A copied output format the draft may keep (an Undo back to a copy); null = the session's own when it is a copy.
        /// </param>
        private async Task<TakeResult> TakeCoreAsync(AvasDevice rx, MultiViewDraft draft, MultiViewCanvas? copiedCanvas)
        {
            await _gate.WaitAsync();
            try
            {
                if (draft.Placements.Count == 0) return TakeResult.Reject(NothingPlacedText);

                var session = Find(rx);
                if (session != null && SameLayout(draft, session.Live)) return new TakeResult(TakeOutcome.NothingToTake);

                var taken = draft.Clone();
                var (result, sent) = await SendAsync(rx, taken, copiedCanvas ?? CopiedCanvasOf(session));
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
                    DropExternal(rx);
                }
                else
                {
                    session.Previous = session.Live;
                    session.PreviousCanvas = session.Canvas;
                    session.PreviousCanvasIsCopy = session.CanvasIsCopy;
                }
                await CommitAsync(session, rx, taken, sent);
                return result;
            }
            finally
            {
                await ReleaseGateAsync();
            }
        }

        /// <summary>
        /// Ends an operation under <see cref="_gate"/>: switches unused frame dividers back, then releases the gate. The
        /// release always happens, whatever the restore does (a stuck gate would block every later Take).
        /// </summary>
        private async Task ReleaseGateAsync()
        {
            try
            {
                await RestoreUnusedDividersAsync();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("MULTIVIEW", $"Restoring frame dividers failed: {ex.Message}");
            }
            finally
            {
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
                // The picture before the last Take may have run at a copied format that the Take left behind.
                var copied = session.PreviousCanvasIsCopy ? session.PreviousCanvas : null;
                var (result, sent) = await SendAsync(rx, previous, copied);
                if (sent == null) return result;

                session.Previous = null;
                session.PreviousCanvas = null;
                session.PreviousCanvasIsCopy = false;
                await CommitAsync(session, rx, previous, sent);
                return result;
            }
            finally
            {
                await ReleaseGateAsync();
            }
        }

        /// <summary>
        /// Leaves a multiview this tool did not start (an external layout): optionally leaves the given
        /// subscriptions, then returns the decoder to full-screen genlock.
        /// </summary>
        /// <param name="leaveIndexZero">
        /// Also leaves subscription 0 when the external layout had it joined (no Single View source follows to reuse
        /// it). Default false: the subscriptions given are all that are left.
        /// </param>
        public Task<bool> ExitExternalAsync(AvasDevice rx, IEnumerable<int> subscriptionIndexesToLeave, bool leaveIndexZero = false)
        {
            var toLeave = subscriptionIndexesToLeave.ToList();
            return ExclusiveAsync(rx, async () =>
            {
                await _gate.WaitAsync();
                try
                {
                    if (leaveIndexZero && !toLeave.Contains(0) && _external.TryGetValue(Key(rx), out var joined) && joined.Contains(0))
                        toLeave.Insert(0, 0);
                    foreach (int index in toLeave)
                        await LeaveAsync(rx, index);
                    bool ok = await _client.SetGenlockedAsync(rx.DeviceId);
                    if (ok) DropExternal(rx);
                    return ok;
                }
                finally
                {
                    _gate.Release();
                }
            });
        }

        /// <summary>
        /// Leaves multiview: the multiview's stream-1 subscriptions are left (best effort; index 0 is kept, a Single
        /// View join reuses it), then the decoder goes back to full-screen genlock, ready for a normal join.
        /// </summary>
        /// <param name="leaveIndexZero">
        /// Leaves subscription 0 as well, for a return to Single View with no source to follow (nothing reuses it).
        /// </param>
        public Task<bool> ReturnToSingleViewAsync(AvasDevice rx, bool leaveIndexZero = false) => ExclusiveAsync(rx, async () =>
        {
            await _gate.WaitAsync();
            try
            {
                var session = Find(rx);
                if (session == null) return true;
                var left = new HashSet<int>();
                foreach (int index in session.Joined.Where(i => leaveIndexZero || i != 0).OrderBy(i => i))
                {
                    if (await LeaveAsync(rx, index)) left.Add(index);
                }

                bool ok = await GenlockAndEndAsync(rx);
                // Refused genlock: the display stays in multiview, but the leaves did happen, so they no longer
                // count as joined (a later Off or Take does not leave them again).
                if (!ok && left.Count > 0) session.Joined = session.Joined.Except(left).ToHashSet();
                return ok;
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

        // ---- copy a display onto another (spec section 15) ----

        /// <summary>
        /// Drops the target's copy Undo: the display was changed deliberately since (a route, Off, a Take, a re-read),
        /// so putting back the picture from before the copy would overwrite that change. Changes no device.
        /// </summary>
        public void ForgetPreviousScreen(AvasDevice target)
        {
            ArgumentNullException.ThrowIfNull(target);
            lock (_previousScreens) _previousScreens.Remove(Key(target));
        }

        /// <summary>
        /// What the target showed just before its last copy, while that copy is less than <see cref="CopyUndoWindow"/>
        /// old (evaluated on read: an expired snapshot is dropped). Null when there is nothing to put back. The same
        /// object is returned for as long as that snapshot is kept, so callers can tell whether it is still current.
        /// </summary>
        public ScreenState? PreviousScreen(AvasDevice target)
        {
            ArgumentNullException.ThrowIfNull(target);
            string key = Key(target);
            lock (_previousScreens)
            {
                if (!_previousScreens.TryGetValue(key, out var entry)) return null;
                if (Clock() - entry.At < CopyUndoWindow) return entry.Screen;
                _previousScreens.Remove(key);
                return null;
            }
        }

        /// <summary>
        /// Puts <paramref name="source"/> (another display's picture on screen now) on the target, as the only
        /// operation on the target (the source's lock is never taken: its state comes from the snapshot).
        /// Single View: the target leaves multiview (genlock) if needed, then is routed to the same encoder.
        /// MultiView this tool made: the same preset at the source's output format, named per target
        /// (<c>avas_&lt;mac&gt;_&lt;preset&gt;_&lt;W&gt;x&lt;H&gt;</c>), same surfaces and subscriptions. External layout: its
        /// windows as they are, named <c>avas_&lt;mac&gt;_copy_&lt;W&gt;x&lt;H&gt;</c>, recorded read-only like an adopted
        /// external layout. No EDID canvas re-pick, no scaler and no frame-divider change: the encoders already run
        /// for the source. On success the target's previous picture is kept for <see cref="CopyUndoWindow"/>; on a
        /// failure after the target was touched, that picture is put back (best effort).
        /// </summary>
        /// <param name="before">
        /// The target's picture now, captured by the caller under the target's lock (the overview knows its external
        /// layout and Single View format). When null, what this orchestrator knows is used.
        /// </param>
        public Task<CopyResult> CopyAsync(AvasDevice target, ScreenState source, ScreenState? before = null)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(source);
            return ExclusiveAsync(target, async () =>
            {
                var snapshot = before ?? CaptureScreen(target);
                var (result, touched) = await ApplyScreenAsync(target, source);
                if (result.Ok)
                {
                    lock (_previousScreens) _previousScreens[Key(target)] = (snapshot, Clock());
                    AppLogger.Info("COPY", $"'{target.DeviceName}' now shows {source.Describe()}.");
                    return result;
                }

                AppLogger.Warn("COPY", $"Copy to '{target.DeviceName}' failed: {result.Error}");
                if (!touched) return result;

                bool putBack = false;
                try
                {
                    putBack = await RestoreCoreAsync(target, snapshot);
                    if (!putBack) AppLogger.Warn("COPY", $"'{target.DeviceName}' could not be put back to {snapshot.Describe()}.");
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("COPY", $"Putting '{target.DeviceName}' back failed: {ex.Message}");
                }
                return result with { LeftAsItWas = putBack };
            });
        }

        /// <summary>
        /// Puts back a copy target's previous picture (<see cref="PreviousScreen"/>): Nothing = leave every
        /// subscription and genlock; Single View = route that encoder; MultiView = Take that draft again (its own EDID
        /// canvas); External = load that layout again. Afterwards the snapshot is gone. False when the device refused.
        /// </summary>
        public Task<bool> RestoreScreenAsync(AvasDevice target, ScreenState previous)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(previous);
            return ExclusiveAsync(target, async () =>
            {
                bool ok = await RestoreCoreAsync(target, previous);
                if (ok)
                {
                    lock (_previousScreens) _previousScreens.Remove(Key(target));
                }
                return ok;
            });
        }

        /// <summary>
        /// The picture an external layout <paramref name="source"/> becomes on <paramref name="targetMac"/> when copied:
        /// the same windows (unshown surfaces black = 255) under the target's copy layout name, with the routed
        /// subscriptions its windows show.
        /// </summary>
        public static ScreenState ExternalCopyOf(ScreenState source, string targetMac)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (source.External == null) throw new ArgumentException("Not an external layout.", nameof(source));

            var surfaces = SurfacePlanner.Plan(source.External, source.Subscriptions, source.Width, source.Height, targetMac);
            var windows = surfaces.Windows.Select(w => new RxWindow(w.Slot.Index, w.Surface ?? 255,
                w.Slot.X, w.Slot.Y, w.Slot.Width, w.Slot.Height, w.HOffset, w.VOffset)).ToList();
            var used = surfaces.Sources
                .Select(s => source.Subscriptions.First(x => x.IsRouted && x.Index == s.Surface))
                .ToList();
            return new ScreenState(ScreenKind.External, source.Width, source.Height, source.Fps, null, null, null,
                new RxLayout(surfaces.LayoutName, source.Width, source.Height, windows), used);
        }

        /// <summary>What this orchestrator alone knows the display shows (no Single View format, no external geometry).</summary>
        private ScreenState CaptureScreen(AvasDevice rx)
        {
            var none = Array.Empty<RxSubscription>();
            var session = Find(rx);
            if (session != null)
                return new ScreenState(ScreenKind.MultiView, session.Canvas.Width, session.Canvas.Height, session.Canvas.Fps,
                    null, null, session.Live.Clone(), null, none) { CanvasIsCopy = session.CanvasIsCopy };
            if (_external.ContainsKey(Key(rx)))
                return new ScreenState(ScreenKind.External, 0, 0, 0, null, null, null, null, none);
            if (!string.IsNullOrEmpty(rx.ActiveSourceId))
                return new ScreenState(ScreenKind.Single, 0, 0, 0, rx.ActiveSourceId, rx.ActiveSourceName, null, null, none);
            return new ScreenState(ScreenKind.Nothing, 0, 0, 0, null, null, null, null, none);
        }

        /// <summary>Sends the picture. <c>Touched</c>: the target may have changed (so a failure needs putting back).</summary>
        private Task<(CopyResult Result, bool Touched)> ApplyScreenAsync(AvasDevice rx, ScreenState screen) =>
            screen.Kind switch
            {
                ScreenKind.Single => ApplySingleAsync(rx, screen),
                ScreenKind.MultiView when screen.Draft != null => ApplyDraftAsync(rx, screen),
                ScreenKind.External when screen.External != null => ApplyExternalAsync(rx, screen),
                ScreenKind.MultiView or ScreenKind.External =>
                    Task.FromResult((CopyResult.Fail("The layout on screen could not be read, so there is nothing to copy."), false)),
                _ => Task.FromResult((CopyResult.Fail("Nothing to copy."), false)),
            };

        private async Task<(CopyResult, bool)> ApplySingleAsync(AvasDevice rx, ScreenState screen)
        {
            if (string.IsNullOrEmpty(screen.SourceMac)) return (CopyResult.Fail("Nothing to copy."), false);

            bool touched = false;
            if (Find(rx) != null)
            {
                if (!await ReturnToSingleViewAsync(rx))
                    return (CopyResult.Fail($"'{rx.DeviceName}' did not accept the switch back to Single View."), false);
                touched = true;
            }
            else if (_external.TryGetValue(Key(rx), out var external))
            {
                if (!await ExitExternalAsync(rx, StreamOneJoins(external)))
                    return (CopyResult.Fail($"'{rx.DeviceName}' did not accept the switch back to Single View."), false);
                touched = true;
            }

            var tx = SourceDevice(screen);
            if (!await _routing.RouteSingleViewVideoAsync(tx, rx))
                return (CopyResult.Fail($"'{rx.DeviceName}' did not accept the route to '{tx.DeviceName}'."), touched);

            return (new CopyResult(true, null, screen), true);
        }

        private async Task<(CopyResult, bool)> ApplyDraftAsync(AvasDevice rx, ScreenState screen)
        {
            var source = screen.Draft!;
            MultiViewDraft draft;
            SurfacePlan surfaces;
            try
            {
                // The same preset rebuilt at the source's output format: identical blocks (deterministic).
                draft = new MultiViewDraft(MultiViewPresets.Build(source.Plan.Id, screen.Width, screen.Height));
                foreach (var slot in source.Plan.Slots)
                {
                    if (!source.Placements.TryGetValue(slot.Index, out var p)) continue;
                    var placed = draft.Place(slot.Index, p.SourceMac, p.SourceName);
                    if (!placed.Ok) return (CopyResult.Fail(placed.Reason ?? "The layout could not be copied."), false);
                }
                surfaces = SurfacePlanner.Plan(draft, rx.DeviceId);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return (CopyResult.Fail(ex.Message), false);
            }

            var canvas = new MultiViewCanvas(screen.Width, screen.Height, screen.Fps, false, $"{screen.Width}x{screen.Height}@{screen.Fps} (copy)");
            await _gate.WaitAsync();
            try
            {
                // One encoder has one scaler size: a third display using one of these encoders at another size
                // (possible when the source was adopted as found) refuses the copy before anything is sent.
                var conflict = DraftValidator.Validate(draft, UsesExcept(rx), _ => true)
                    .FirstOrDefault(i => i.Kind == BlockIssueKind.SizeConflictOtherDisplay);
                if (conflict != null) return (CopyResult.Fail(conflict.Message), false);

                var (error, touched, joined) = await LoadAsync(rx, surfaces, draft.Plan, screen);
                if (error != null) return (CopyResult.Fail(error), touched);

                var dropped = JoinedOn(rx).Except(joined).OrderBy(i => i).ToList();
                DropExternal(rx);
                // A new session: the Composer's Undo does not reach back past a copy (the copy has its own).
                _sessions[Key(rx)] = new Session(rx.DeviceName, draft.Clone(), canvas) { Joined = joined, CanvasIsCopy = true };
                foreach (int index in dropped) await LeaveAsync(rx, index);
                await RestoreUnusedDividersAsync();

                var subscriptions = surfaces.Sources
                    .Select(s => new RxSubscription(s.Surface, true, "STREAMING", s.Mac, NameOf(draft, s.Mac), s.Stream))
                    .ToList();
                return (new CopyResult(true, null, screen with
                {
                    Draft = draft.Clone(), External = null, Subscriptions = subscriptions, CanvasIsCopy = true,
                }), true);
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<(CopyResult, bool)> ApplyExternalAsync(AvasDevice rx, ScreenState screen)
        {
            var surfaces = SurfacePlanner.Plan(screen.External!, screen.Subscriptions, screen.Width, screen.Height, rx.DeviceId);
            if (surfaces.Windows.Count == 0)
                return (CopyResult.Fail("The layout on screen has no windows to copy."), false);
            var plan = new MultiViewLayoutPlan("copy", surfaces.LayoutName, screen.Width, screen.Height,
                surfaces.Windows.Select(w => w.Slot).ToList());

            await _gate.WaitAsync();
            try
            {
                var (error, touched, joined) = await LoadAsync(rx, surfaces, plan, screen);
                if (error != null) return (CopyResult.Fail(error), touched);

                var dropped = JoinedOn(rx).Except(joined).OrderBy(i => i).ToList();
                // Recorded like an adopted external layout: read-only here, and Off / a Take leave what it joined.
                _sessions.Remove(Key(rx));
                _external[Key(rx)] = joined;
                SetExternalUses(rx, MultiViewRestore.ExternalUses(rx.DeviceName, screen.External!, screen.Subscriptions));
                foreach (int index in dropped) await LeaveAsync(rx, index);
                await RestoreUnusedDividersAsync();

                return (new CopyResult(true, null, ExternalCopyOf(screen, rx.DeviceId)), true);
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Writes the layout and loads it at the source's format. Error is null on success.</summary>
        private async Task<(string? Error, bool Touched, IReadOnlySet<int> Joined)> LoadAsync(
            AvasDevice rx, SurfacePlan surfaces, MultiViewLayoutPlan plan, ScreenState screen)
        {
            var none = new HashSet<int>();
            if (!await _client.CreateMultiViewLayoutAsync(surfaces, plan))
                return ($"Could not create layout '{surfaces.LayoutName}'.", false, none);

            var subscriptions = surfaces.Sources.Select(s => new MultiViewSubscription(s.Mac, s.Surface, s.Stream)).ToList();
            if (!await _client.SetMultiViewAsync(rx.DeviceId, surfaces.LayoutName, screen.Width, screen.Height, screen.Fps, subscriptions))
                return ($"'{rx.DeviceName}' rejected layout '{surfaces.LayoutName}'.", true, none);

            AppLogger.Info("COPY", $"'{rx.DeviceName}' loaded '{surfaces.LayoutName}' with {subscriptions.Count} source(s) at {screen.Width}x{screen.Height}@{screen.Fps}");
            return (null, true, subscriptions.Select(s => s.SubscriptionIndex).ToHashSet());
        }

        private async Task<bool> RestoreCoreAsync(AvasDevice rx, ScreenState previous)
        {
            string key = Key(rx);
            switch (previous.Kind)
            {
                case ScreenKind.Nothing:
                    if (Find(rx) != null) return await TurnOffAsync(rx);
                    if (_external.TryGetValue(key, out var external)) return await ExitExternalAsync(rx, external.OrderBy(i => i));
                    if (!string.IsNullOrEmpty(rx.ActiveSourceId) && !await _routing.UnrouteDisplayAsync(rx)) return false;
                    return await _client.SetGenlockedAsync(rx.DeviceId);

                case ScreenKind.Single:
                    if (string.IsNullOrEmpty(previous.SourceMac)) return false;
                    if (Find(rx) != null && !await ReturnToSingleViewAsync(rx)) return false;
                    if (_external.TryGetValue(key, out var joinedExternal) && !await ExitExternalAsync(rx, StreamOneJoins(joinedExternal))) return false;
                    return await _routing.RouteSingleViewVideoAsync(SourceDevice(previous), rx);

                case ScreenKind.MultiView:
                    if (previous.Draft == null) return false;
                    // A previous picture that was itself a copy goes back at its copied format (not an EDID re-pick).
                    var copied = previous.CanvasIsCopy
                        ? new MultiViewCanvas(previous.Width, previous.Height, previous.Fps, false, $"{previous.Width}x{previous.Height}@{previous.Fps} (copy)")
                        : null;
                    var taken = await ExclusiveAsync(rx, () => TakeCoreAsync(rx, previous.Draft, copied));
                    return taken.Outcome is TakeOutcome.Applied or TakeOutcome.NothingToTake;

                case ScreenKind.External:
                    if (previous.External == null) return false;
                    var (result, _) = await ApplyExternalAsync(rx, previous);
                    return result.Ok;

                default:
                    return false;
            }
        }

        /// <summary>
        /// The subscriptions to leave when a display returns to Single View (Ruling R18): every joined index but 0,
        /// which the Single View join reuses.
        /// </summary>
        public static IReadOnlyList<int> StreamOneJoins(IEnumerable<int> joined) => joined.Where(i => i != 0).OrderBy(i => i).ToList();

        /// <summary>The session's canvas when it is a copied format (a Take may keep it), else null.</summary>
        private static MultiViewCanvas? CopiedCanvasOf(Session? session) => session?.CanvasIsCopy == true ? session.Canvas : null;

        /// <summary>The subscription indexes this orchestrator knows are joined on the decoder in multiview.</summary>
        private IReadOnlySet<int> JoinedOn(AvasDevice rx) =>
            Find(rx)?.Joined ?? (_external.TryGetValue(Key(rx), out var external) ? external : new HashSet<int>());

        private static AvasDevice SourceDevice(ScreenState screen) => new()
        {
            DeviceId = screen.SourceMac ?? string.Empty,
            DeviceName = string.IsNullOrEmpty(screen.SourceName) ? screen.SourceMac ?? string.Empty : screen.SourceName,
            IsTransmitter = true,
        };

        private static string NameOf(MultiViewDraft draft, string mac) =>
            draft.Placements.Values.FirstOrDefault(p => SdvoeClient.NormalizeMac(p.SourceMac) == mac)?.SourceName ?? mac;

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
        /// <param name="copied">
        /// A copied output format the display runs (or ran) at: a layout drawn at exactly that size keeps it while the
        /// display's EDID still lists that mode, so a copied picture stays independent and editable afterwards.
        /// </param>
        private async Task<(TakeResult Result, Sent? Sent)> SendAsync(AvasDevice rx, MultiViewDraft draft, MultiViewCanvas? copied = null)
        {
            var plan = draft.Plan;

            // The display may have been swapped since the layout was drawn: never send a layout for another canvas.
            var edid = await ReadEdidAsync(rx);
            var canvas = MultiViewCanvasPolicy.Choose(edid, _fallback);
            bool drawnForCopy = copied != null && plan.Width == copied.Width && plan.Height == copied.Height;
            bool keepCopy = drawnForCopy && edid != null && edid.Supports(copied!.Width, copied.Height, copied.Fps);
            if (keepCopy) canvas = copied!;
            AppLogger.Info("MULTIVIEW", $"'{rx.DeviceName}' output: {canvas.Description}");

            // An unreadable EDID is not a 1920x1080 display: a layout drawn for another size must not be redrawn
            // for the fallback (and the operator's staged draft replaced) because of a transient read failure.
            if (edid == null && (canvas.Width != plan.Width || canvas.Height != plan.Height))
            {
                AppLogger.Warn("MULTIVIEW", $"'{rx.DeviceName}': EDID could not be read; the {plan.Width}x{plan.Height} layout is not sent.");
                return (TakeResult.Reject($"Can't read {rx.DeviceName}'s resolution - nothing changed on {rx.DeviceName}"), null);
            }

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
                string why = drawnForCopy
                    ? $"'{rx.DeviceName}' runs a copied {copied!.Width}x{copied.Height} picture; the layout was redrawn for its own {canvas.Width}x{canvas.Height}."
                    : $"The display of '{rx.DeviceName}' changed to {canvas.Description}; the layout was redrawn for it.";
                return (new TakeResult(TakeOutcome.DisplayChanged, why, redrawn), null);
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

            // Every scaler this attempt resizes: a refused or failed Take must put them back (the live picture would
            // otherwise run cropped or stretched while the card says nothing changed).
            var scalers = new List<ScalerAttempt>();
            try
            {
                foreach (var source in surfaces.Sources)
                {
                    string name = draft.Placements.Values.First(p => SdvoeClient.NormalizeMac(p.SourceMac) == source.Mac).SourceName;
                    var attempt = new ScalerAttempt(source.Mac, source.Width, source.Height);
                    scalers.Add(attempt);
                    attempt.Accepted = await _client.PrepareSourceScalerAsync(source.Mac, source.Width, source.Height);
                    if (!attempt.Accepted)
                    {
                        await RestoreScalersAsync(rx, scalers);
                        return (TakeResult.Reject($"Could not prepare '{name}' for a {source.Width}x{source.Height} window."), null);
                    }

                    if (halve && !await HalveAsync(source.Mac, name))
                        refused.Add(name);
                }

                if (!await _client.CreateMultiViewLayoutAsync(surfaces, plan))
                {
                    await RestoreScalersAsync(rx, scalers);
                    return (TakeResult.Reject($"Could not create layout '{surfaces.LayoutName}'."), null);
                }

                var subscriptions = surfaces.Sources.Select(s => new MultiViewSubscription(s.Mac, s.Surface)).ToList();
                if (!await _client.SetMultiViewAsync(rx.DeviceId, surfaces.LayoutName, plan.Width, plan.Height, canvas.Fps, subscriptions))
                {
                    await RestoreScalersAsync(rx, scalers);
                    return (TakeResult.Reject($"'{rx.DeviceName}' rejected layout '{surfaces.LayoutName}'."), null);
                }

                AppLogger.Info("MULTIVIEW", $"'{rx.DeviceName}' now shows {plan.Id} with {subscriptions.Count} source(s) ({canvas.Description})");
                return (new TakeResult(TakeOutcome.Applied),
                    new Sent(canvas, refused, subscriptions.Select(s => s.SubscriptionIndex).ToHashSet(), keepCopy));
            }
            catch
            {
                await RestoreScalersAsync(rx, scalers);
                throw;
            }
        }

        /// <summary>One scaler resize a Take attempt made (or tried to; a refused one may have changed the encoder in part).</summary>
        private sealed class ScalerAttempt
        {
            public ScalerAttempt(string mac, int width, int height)
            {
                Mac = mac;
                Width = width;
                Height = height;
            }

            public string Mac { get; }
            public int Width { get; }
            public int Height { get; }
            public bool Accepted { get; set; }
        }

        /// <summary>
        /// Best effort, never throws: puts each encoder this attempt resized, and the display's live picture still
        /// uses, back at the size that picture needs. Encoders the live picture does not use are left as they are.
        /// </summary>
        private async Task RestoreScalersAsync(AvasDevice rx, IReadOnlyList<ScalerAttempt> attempts)
        {
            try
            {
                var session = Find(rx);
                if (session == null || attempts.Count == 0) return;

                foreach (var live in SurfacePlanner.Plan(session.Live, rx.DeviceId).Sources)
                {
                    var last = attempts.LastOrDefault(a => a.Mac == live.Mac);
                    if (last == null) continue;
                    if (last.Accepted && last.Width == live.Width && last.Height == live.Height) continue; // already there

                    try
                    {
                        bool ok = await _client.PrepareSourceScalerAsync(live.Mac, live.Width, live.Height);
                        AppLogger.Warn("MULTIVIEW", ok
                            ? $"Take refused: {live.Mac} stream 1 put back at {live.Width}x{live.Height} for '{rx.DeviceName}'."
                            : $"Take refused: {live.Mac} stream 1 could not be put back at {live.Width}x{live.Height}; '{rx.DeviceName}' may show it cropped.");
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn("MULTIVIEW", $"Putting {live.Mac} back at {live.Width}x{live.Height} failed: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("MULTIVIEW", $"Putting the scalers back for '{rx.DeviceName}' failed: {ex.Message}");
            }
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
            session.CanvasIsCopy = sent.CanvasIsCopy;
            session.DividerRefused = sent.DividerRefused;
            session.Joined = sent.Joined;

            foreach (int index in droppedOut)
                await LeaveAsync(rx, index);
        }

        /// <summary>Best effort: a leave that is refused or throws is logged, never fatal. True when it was accepted.</summary>
        private async Task<bool> LeaveAsync(AvasDevice rx, int subscriptionIndex)
        {
            try
            {
                if (await _client.LeaveStreamAsync(rx.DeviceId, "HDMI", subscriptionIndex)) return true;
                AppLogger.Warn("MULTIVIEW", $"Leave of subscription {subscriptionIndex} on '{rx.DeviceName}' was not accepted.");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("MULTIVIEW", $"Leave of subscription {subscriptionIndex} on '{rx.DeviceName}' failed: {ex.Message}");
            }
            return false;
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

                try
                {
                    if (await _client.SetStream1FrameDividerAsync(mac, false))
                        _halvedByUs.Remove(mac);
                    else
                        AppLogger.Warn("MULTIVIEW", $"Could not restore full frame rate on {mac}; will retry later.");
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("MULTIVIEW", $"Restoring full frame rate on {mac} failed ({ex.Message}); will retry later.");
                }
            }
        }

        /// <summary>Reads the display's EDID and picks the output format; falls back to the default when it can't.</summary>
        private async Task<MultiViewCanvas> ResolveCanvasAsync(AvasDevice rx)
        {
            var canvas = MultiViewCanvasPolicy.Choose(await ReadEdidAsync(rx), _fallback);
            AppLogger.Info("MULTIVIEW", $"'{rx.DeviceName}' output: {canvas.Description}");
            return canvas;
        }

        /// <summary>The display's EDID, or null when there is none or it cannot be read or parsed.</summary>
        private async Task<EdidInfo?> ReadEdidAsync(AvasDevice rx)
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
            return EdidInfo.Parse(hex);
        }

        /// <summary>Same layout and the same encoder in every block.</summary>
        private static bool SameLayout(MultiViewDraft a, MultiViewDraft b) =>
            a.Plan.Name == b.Plan.Name && a.DiffFrom(b).Count == 0;

        private Session? Find(AvasDevice rx) => _sessions.TryGetValue(Key(rx), out var s) ? s : null;

        private static string Key(AvasDevice d) => SdvoeClient.NormalizeMac(d.DeviceId);
    }
}
