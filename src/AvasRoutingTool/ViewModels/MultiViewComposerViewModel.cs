using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AvasRoutingTool.Logging;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Sdvoe.MultiView;

namespace AvasRoutingTool.ViewModels
{
    public enum BannerKind { None, Info, Success, Warning, Error }

    /// <summary>An arrow key on the canvas: the keyboard moves from block to block by where they lie on the screen.</summary>
    public enum FocusDirection { Left, Right, Up, Down }

    /// <summary>One line of feedback under the Composer header or the Take button.</summary>
    public sealed record ComposerBanner(string Text, BannerKind Kind)
    {
        public static ComposerBanner None { get; } = new(string.Empty, BannerKind.None);
    }

    /// <summary>
    /// The MultiView Composer for one display: a layout and sources are staged in a <see cref="MultiViewDraft"/>
    /// (nothing reaches the device) and go on screen together with <see cref="TakeAsync"/>. Tap-first: arm a
    /// source and tap blocks, or tap a block and pick. No XAML here - the view binds to this.
    /// </summary>
    public sealed class MultiViewComposerViewModel : INotifyPropertyChanged, IDisposable
    {
        /// <summary>How long Undo is offered after a Take.</summary>
        public static readonly TimeSpan UndoWindow = TimeSpan.FromSeconds(10);

        private const string NothingPlacedText = "Place at least one source (Step 2).";
        private const string NothingToChangeText = "Nothing to change since what is on screen.";
        private const string DisplayChangedText = "The display changed - review the new layout, then save and apply.";

        /// <summary>How long a Success banner ("On screen") stays; <see cref="Tick"/> clears it after this.</summary>
        public static readonly TimeSpan SuccessBannerLifetime = TimeSpan.FromSeconds(4);

        private readonly DisplayCardViewModel _display;
        private readonly MultiViewOrchestrator _mv;
        private readonly Func<IEnumerable<SourceCardViewModel>> _sources;
        private readonly Func<DateTime> _clock;
        private readonly HashSet<SourceCardViewModel> _watched = new();

        private MultiViewDraft? _draft;
        private MultiViewDraft? _live;
        private MultiViewCanvas? _canvas;
        private MultiViewLayoutPlan? _blocksPlan;
        private IReadOnlyList<DraftChange> _changes = Array.Empty<DraftChange>();
        private IReadOnlyList<BlockIssue> _issues = Array.Empty<BlockIssue>();
        private SourceCardViewModel? _armed;
        private int? _pickerBlock;
        private int? _movingFrom;
        private int? _selectedBlock;
        private string? _selectedLayoutId;
        private bool _stepLayoutDone;
        private bool _busy;
        private ComposerBanner _banner = ComposerBanner.None;
        private DateTime? _takenAt;
        private DateTime? _successShownAt;
        private bool _lastCanUndo;
        private int _selectSeq;
        private bool _selecting;
        private string? _pendingLayoutId;
        private bool _disposed;
        private string? _hintText;
        private int? _focusedBlock;
        private string? _takeHint;
        private readonly MotionPolicy _motion;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Raised after Take or Undo put a new picture on screen; the argument is the new live draft. For the overview card.</summary>
        public event Action<MultiViewDraft?>? LiveChanged;

        /// <param name="resumeDraft">An unsent, copied or restored draft to start from (starts past Step 1); null starts at Step 1.</param>
        /// <param name="clock">Time source for the Undo window; tests inject one.</param>
        /// <param name="animationsEnabled">Whether Windows animations are on (default <see cref="System.Windows.SystemParameters.ClientAreaAnimation"/>); off skips every transition.</param>
        public MultiViewComposerViewModel(
            DisplayCardViewModel display,
            MultiViewOrchestrator mv,
            Func<IEnumerable<SourceCardViewModel>> sources,
            IReadOnlyList<LayoutItemViewModel> layouts,
            MultiViewDraft? resumeDraft,
            Func<DateTime>? clock = null,
            Func<bool>? animationsEnabled = null)
        {
            _display = display ?? throw new ArgumentNullException(nameof(display));
            _motion = new MotionPolicy(animationsEnabled);
            _mv = mv ?? throw new ArgumentNullException(nameof(mv));
            _sources = sources ?? throw new ArgumentNullException(nameof(sources));
            _clock = clock ?? (() => DateTime.UtcNow);
            LayoutOptions = layouts
                .Where(l => l.IsAvailable && MultiViewPresets.IsMultiView(l.Id))
                .Select(l => new ComposerLayoutOptionViewModel(l.Id, l.Title, l.Subtitle, l.IsAvailable))
                .ToList();

            _live = _mv.GetLive(_display.Device);
            _canvas = _mv.GetCanvas(_display.Device);
            RebuildLiveBlocks();

            if (resumeDraft != null)
            {
                _draft = resumeDraft.Clone();
                _selectedLayoutId = _draft.Plan.Id;
                _stepLayoutDone = true;
            }
            Refresh();
        }

        // ---- what the view binds to ----

        /// <summary>The layout tiles of Step 1, owned by this Composer (selected / pending follow <see cref="SelectedLayoutId"/> / <see cref="PendingLayoutId"/>).</summary>
        public IReadOnlyList<ComposerLayoutOptionViewModel> LayoutOptions { get; }

        /// <summary>The layout staged now; changes only when a selection has finished.</summary>
        public string? SelectedLayoutId
        {
            get => _selectedLayoutId;
            private set { if (Set(ref _selectedLayoutId, value)) SyncOptions(); }
        }

        /// <summary>The tile tapped last while the display is still being read; null otherwise.</summary>
        public string? PendingLayoutId
        {
            get => _pendingLayoutId;
            private set { if (Set(ref _pendingLayoutId, value)) SyncOptions(); }
        }

        /// <summary>A layout selection is in flight: Take and Undo are refused until it ends.</summary>
        public bool IsSelecting
        {
            get => _selecting;
            private set { if (Set(ref _selecting, value)) RaiseDerived(); }
        }

        /// <summary>The staged picture, one block per window of the layout, in logical order.</summary>
        public ObservableCollection<ComposerBlockViewModel> Blocks { get; } = new();

        /// <summary>What is on the screen now ("On screen now" strip); read-only, empty when nothing is live.</summary>
        public ObservableCollection<ComposerBlockViewModel> LiveBlocks { get; } = new();

        /// <summary>The staged draft; null until a layout is chosen (Step 1).</summary>
        public MultiViewDraft? Draft => _draft;

        public SourceCardViewModel? ArmedSource => _armed;

        /// <summary>The block whose source picker is open.</summary>
        public int? PickerBlockIndex => _pickerBlock;

        /// <summary>The block being moved: the next tap on a block is its destination.</summary>
        public int? MovingFromIndex => _movingFrom;

        /// <summary>
        /// The block the keyboard is on, by API window index (<see cref="ComposerBlockViewModel.Index"/>, never Number);
        /// null until an arrow key or a focus puts it on one. Arrow keys move it, Enter places, Delete clears.
        /// </summary>
        public int? FocusedBlockIndex => _focusedBlock;

        // ---- reduced motion ----

        /// <summary>Windows animations are on. Off (Show animations unchecked): every transition is skipped.</summary>
        public bool AnimationsEnabled => _motion.AnimationsEnabled;

        /// <summary>How long the staged frame takes to turn into the live frame after a Take (400 ms; zero when animations are off).</summary>
        public TimeSpan FrameTransitionDuration => _motion.FrameTransition;

        /// <summary>How long a banner or toast fades in (150 ms; zero when animations are off).</summary>
        public TimeSpan FadeDuration => _motion.Fade;

        /// <summary><paramref name="normal"/> when animations are on, <see cref="TimeSpan.Zero"/> when they are off.</summary>
        public TimeSpan TransitionDuration(TimeSpan normal) => _motion.Resolve(normal);

        /// <summary>Blocks that differ from the screen. <see cref="DraftChange.SlotIndex"/> is the API window index; see <see cref="ChangeLines"/> for display numbers.</summary>
        public IReadOnlyList<DraftChange> Changes => _changes;

        /// <summary>The change list as the operator reads it, by display number: "Block 2: empty -> TX1", "Block 4: TX2 -> empty", "Block 2: TX1 -> TX2".</summary>
        public IReadOnlyList<string> ChangeLines =>
            _changes.Select(c => $"Block {NumberOf(c.SlotIndex)}: {c.From ?? "empty"} -> {c.To ?? "empty"}").ToList();

        /// <summary>How many blocks differ from the screen ("CHANGES (n)").</summary>
        public int ChangeCount => _changes.Count;

        /// <summary>The staged picture differs from the screen: "STAGED - not on screen yet", dashed amber frame.</summary>
        public bool IsStaged => _draft != null && HasChanges;

        /// <summary>The staged picture is what the screen shows now: "ON SCREEN", solid blue frame.</summary>
        public bool IsOnScreen => _draft != null && _live != null && !HasChanges;

        public bool StepLayoutDone => _stepLayoutDone;

        public bool StepPlaceDone => _draft != null && _draft.Placements.Count > 0;

        // The step markers show a number or a check; these say the same in words for a screen reader.
        public string StepLayoutText => $"Step 1, Layout: {(StepLayoutDone ? "done" : "to do")}";

        public string StepPlaceText => $"Step 2, Place sources: {(StepPlaceDone ? "done" : "to do")}";

        public string StepTakeText => $"Step 3, Check, then save and apply: {(CanTake ? "ready" : "not ready")}";

        /// <summary>Width / height of the plan that is on screen now (the ON SCREEN NOW picture follows it); 16:9 when nothing is live.</summary>
        public double LiveAspectRatio =>
            _live is { Plan: { Width: > 0, Height: > 0 } plan } ? (double)plan.Width / plan.Height : 16.0 / 9.0;

        /// <summary>The display's name: the name plate on the empty ON SCREEN NOW picture (it never changes).</summary>
        public string DisplayName => _display.DeviceName;

        /// <summary>What the ON SCREEN NOW picture shows, in words (the thumbnail itself has no text for a screen reader).</summary>
        public string OnScreenSummary
        {
            get
            {
                if (LiveBlocks.Count == 0) return "On screen now: no MultiView on this screen";
                var blocks = LiveBlocks.Select(b => b.IsPlaced ? $"{(b.Number == 1 ? "Block" : "block")} {b.Number} {b.SourceName}" : $"block {b.Number} empty");
                return $"On screen now: {LiveBlocks.Count} blocks. {string.Join("; ", blocks)}";
            }
        }

        public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) RaiseDerived(); } }

        public ComposerBanner Banner
        {
            get => _banner;
            private set
            {
                _successShownAt = value.Kind == BannerKind.Success ? _clock() : null; // restarts for every Success shown
                Set(ref _banner, value);
            }
        }

        /// <summary>Sources a layout change left without a block, one per encoder, minus any placed again since.</summary>
        public IReadOnlyList<DraftPlacement> UnplacedSources
        {
            get
            {
                if (_draft == null) return Array.Empty<DraftPlacement>();
                var placed = _draft.Placements.Values.Select(p => SdvoeClient.NormalizeMac(p.SourceMac)).ToHashSet();
                return _draft.Unplaced
                    .Where(u => !placed.Contains(SdvoeClient.NormalizeMac(u.SourceMac)))
                    .GroupBy(u => SdvoeClient.NormalizeMac(u.SourceMac))
                    .Select(g => g.First())
                    .ToList();
            }
        }

        public string OutputText
        {
            get
            {
                if (_canvas != null) return MultiViewCanvasPolicy.OutputLabel(_canvas);
                if (_draft == null) return string.Empty;

                // A resumed draft on a display with nothing live yet: reuse what the card already shows, else the plan's size.
                string info = _display.LayoutCanvasInfo;
                if (info.StartsWith("OUT ", StringComparison.Ordinal))
                {
                    int cut = info.IndexOf(" · ", StringComparison.Ordinal);
                    return cut < 0 ? info : info.Substring(0, cut);
                }
                return $"OUT {_draft.Plan.Width}x{_draft.Plan.Height}";
            }
        }

        /// <summary>"Sources at 30 fps on this layout" when the layout is heavy enough that sources are halved; else empty.</summary>
        public string RateNote =>
            _canvas != null && _draft != null && MultiViewCanvasPolicy.NeedsFrameDivider(_draft.Plan, _canvas.Fps)
                ? $"Sources at {_canvas.Fps / 2} fps on this layout"
                : string.Empty;

        /// <summary>The header line: display name and the layout being arranged.</summary>
        public string HeaderNotice
        {
            get
            {
                string head = $"{_display.DeviceName} - MultiView";
                if (_draft == null) return head;
                string title = LayoutOptions.FirstOrDefault(l => l.Id == _draft.Plan.Id)?.Title ?? _draft.Plan.Id;
                return $"{head} · {title}";
            }
        }

        // ---- Take / Undo state ----

        public bool CanTake =>
            !_busy && !_selecting && _draft != null && _draft.Placements.Count > 0 && _issues.Count == 0 && HasChanges;

        public string TakeLabel
        {
            get
            {
                if (_busy) return $"Applying to {_display.DeviceName}...";
                int n = _changes.Count;
                if (n == 0 && LayoutDiffers) return "SAVE & APPLY NEW LAYOUT";
                return $"SAVE & APPLY {n} CHANGE{(n == 1 ? string.Empty : "S")}";
            }
        }

        /// <summary>Why Take is unavailable (first reason), or null when it is available (or while sending).</summary>
        public string? TakeBlockedReason
        {
            get
            {
                if (_draft == null) return "Choose a layout (Step 1).";
                if (_busy || _selecting) return null;
                if (_draft.Placements.Count == 0) return NothingPlacedText;

                var issue = _issues.FirstOrDefault();
                if (issue != null)
                {
                    return issue.Kind == BlockIssueKind.SourceOffline
                        ? $"{_draft.Placements[issue.SlotIndex].SourceName} is offline - clear or replace block {NumberOf(issue.SlotIndex)}"
                        : issue.Message;
                }
                return HasChanges ? null : NothingToChangeText;
            }
        }

        /// <summary>
        /// Ctrl+Enter with Take unavailable: says why on the banner (Info) instead of doing nothing silently. The line goes
        /// by itself as soon as the reason no longer holds. Nothing to say while Take is available or a Take is running.
        /// </summary>
        public void ExplainTake()
        {
            if (_disposed || CanTake || TakeBlockedReason is not { Length: > 0 } reason) return;
            _takeHint = reason;
            Banner = new ComposerBanner(reason, BannerKind.Info);
        }

        /// <summary>The Take button's name for a screen reader: its label, plus why it is disabled while it is.</summary>
        public string TakeAutomationName =>
            !CanTake && TakeBlockedReason is { Length: > 0 } reason ? $"{TakeLabel}. {reason}" : TakeLabel;

        /// <summary>Undo is offered for <see cref="UndoWindow"/> after a Take, and only while the orchestrator still holds the previous picture.</summary>
        public bool CanUndo =>
            !_busy && !_selecting && _takenAt.HasValue && _clock() - _takenAt.Value < UndoWindow && _mv.CanUndo(_display.Device);

        private bool LayoutDiffers => _draft != null && _live != null && _live.Plan.Name != _draft.Plan.Name;

        private bool HasChanges => _changes.Count > 0 || LayoutDiffers;

        // ---- Step 1: layout ----

        /// <summary>
        /// Stages a layout sized to the display's EDID. Reads the display only: no device change. Sources already
        /// staged follow by block order (<see cref="MultiViewDraft.WithLayout"/>).
        /// </summary>
        /// <remarks>
        /// Overlapping selects are sequenced: only the last tap's result is adopted, an older answer arriving late is
        /// dropped. While one is in flight (<see cref="IsSelecting"/>) Take and Undo are refused, so a Take can never
        /// send a draft that the select is about to swap. <see cref="SelectedLayoutId"/> changes only when the select
        /// succeeds (so it "reverts" on failure by never moving); the tapped tile shows as <see cref="PendingLayoutId"/>.
        /// </remarks>
        public async Task SelectLayoutAsync(string id)
        {
            if (_disposed || _busy || !MultiViewPresets.IsMultiView(id)) return;

            int token = ++_selectSeq;
            IsSelecting = true; // synchronously, before the first await
            PendingLayoutId = id;

            MultiViewLayoutPlan plan;
            MultiViewCanvas canvas;
            try
            {
                (plan, canvas) = await _mv.PlanForDisplayAsync(_display.Device, id);
            }
            catch (Exception ex)
            {
                if (token != _selectSeq) return; // a newer tap owns the outcome
                PendingLayoutId = null;
                IsSelecting = false;
                if (ex is not ArgumentException) throw;
                Banner = new ComposerBanner(ex.Message, BannerKind.Error);
                return;
            }

            if (token != _selectSeq || _disposed) return; // stale: a newer tap is (or was) in charge
            PendingLayoutId = null;
            IsSelecting = false;

            int unplacedBefore = UnplacedSources.Count;
            _canvas = canvas;
            _draft = _draft?.WithLayout(plan) ?? new MultiViewDraft(plan);
            SelectedLayoutId = plan.Id;
            _stepLayoutDone = true;
            Banner = ComposerBanner.None;
            ResetTransient();
            Refresh();

            int spilled = UnplacedSources.Count - unplacedBefore;
            if (spilled > 0)
                Banner = new ComposerBanner(
                    $"{spilled} source{(spilled == 1 ? " has" : "s have")} no block in this layout - tap one to place it again.", BannerKind.Info);
        }

        // ---- Step 2: placing ----

        /// <summary>Arms the source (tap again to stop). While armed, tapping a block places it and it stays armed.</summary>
        public void Arm(SourceCardViewModel s)
        {
            if (_busy || s == null) return;
            if (ReferenceEquals(_armed, s))
            {
                Disarm();
                return;
            }

            ResetTransient();
            _armed = s;
            SetHint($"Placing {s.DeviceName} - tap a block. Tap {s.DeviceName} again to stop.");
            Refresh();
        }

        /// <summary>Arms an encoder that a layout change left without a block (its chip was tapped).</summary>
        public void ArmUnplaced(DraftPlacement unplaced)
        {
            var card = FindSource(unplaced.SourceMac);
            if (card != null) Arm(card);
        }

        /// <summary>Stops placing, closes the picker and cancels a move.</summary>
        public void Disarm()
        {
            bool wasPlacing = _armed != null;
            ResetTransient(); // also withdraws the "Placing..." / "Moving..." hint
            if (wasPlacing && Banner.Kind == BannerKind.Warning) Banner = ComposerBanner.None;
            Refresh();
        }

        /// <summary>
        /// A tap on a block. <paramref name="i"/> is the API window index (<see cref="ComposerBlockViewModel.Index"/>),
        /// never the display <see cref="ComposerBlockViewModel.Number"/> / Label.
        /// Armed: place (stays armed). Moving: move / swap to this block. Unarmed and empty: open the picker.
        /// Unarmed and placed: open or close the Replace / Move strip.
        /// </summary>
        public void TapBlock(int i)
        {
            if (_busy || _draft == null || BlockAt(i) == null) return;

            if (_movingFrom is int from)
            {
                _movingFrom = null;
                ClearHint();
                if (from != i) MoveBlock(from, i);
                ClearSelection();
                Refresh();
                return;
            }

            if (_armed != null)
            {
                TryPlace(i, _armed);
                return;
            }

            if (!_draft.Placements.ContainsKey(i))
            {
                ClearSelection();
                _pickerBlock = i;
                Refresh();
                return;
            }

            // A placed block: open (or close) its Replace / Move strip.
            _pickerBlock = null;
            _selectedBlock = _selectedBlock == i ? null : i;
            Refresh();
        }

        /// <summary>Opens the picker on a block (the strip's Replace). <paramref name="block"/> is the API window index (<see cref="ComposerBlockViewModel.Index"/>), never Number/Label.</summary>
        public void OpenPicker(int block)
        {
            if (_busy || _draft == null || BlockAt(block) == null) return;
            _armed = null;
            _movingFrom = null;
            ClearHint();
            ClearSelection();
            _pickerBlock = block;
            Refresh();
        }

        /// <summary>What placing the source in the block would answer, for the picker to show tiles enabled or with the reason. <paramref name="block"/> is the API window index (<see cref="ComposerBlockViewModel.Index"/>), never Number/Label.</summary>
        public DraftResult CheckPlace(int block, SourceCardViewModel s)
        {
            if (_draft == null) return DraftResult.Refuse("Choose a layout (Step 1).");
            return DraftValidator.CanPlace(_draft, block, s.MacAddress, s.DeviceName, _mv.UsesExcept(_display.Device), IsOnline);
        }

        /// <summary>Places the picked source in the block and closes the picker. <paramref name="block"/> is the API window index (<see cref="ComposerBlockViewModel.Index"/>), never Number/Label.</summary>
        public void Pick(int block, SourceCardViewModel s)
        {
            if (_busy || _draft == null || s == null) return;
            if (TryPlace(block, s, keepPicker: true))
            {
                _pickerBlock = null;
                Refresh();
            }
        }

        /// <summary>Starts moving the block's source: the next tap on a block is its destination (occupied = swap). <paramref name="i"/> is the API window index (<see cref="ComposerBlockViewModel.Index"/>), never Number/Label.</summary>
        public void BeginMove(int i)
        {
            if (_busy || _draft == null || !_draft.Placements.ContainsKey(i)) return;
            _armed = null;
            _pickerBlock = null;
            _selectedBlock = null;
            _movingFrom = i;
            SetHint($"Moving block {NumberOf(i)} - tap the block to move it to.");
            Refresh();
        }

        /// <summary>The corner clear: removes the placement. Staged only - nothing reaches the device. <paramref name="i"/> is the API window index (<see cref="ComposerBlockViewModel.Index"/>), never Number/Label.</summary>
        public void Clear(int i)
        {
            if (_busy || _draft == null) return;
            _draft.Clear(i);
            ClearSuccessBanner();
            if (_selectedBlock == i) _selectedBlock = null;
            if (_movingFrom == i)
            {
                _movingFrom = null;
                ClearHint();
            }
            Refresh();
        }

        public void ClearAll()
        {
            if (_busy || _draft == null) return;
            _draft.ClearAll();
            ClearSuccessBanner();
            _selectedBlock = null;
            if (_movingFrom != null)
            {
                _movingFrom = null;
                ClearHint();
            }
            Refresh();
        }

        // ---- keyboard (spec 10): every drag and tap has a keyboard path; no WPF here, the view only maps keys ----

        /// <summary>
        /// Puts the keyboard on a block (a click or Tab landed there). <paramref name="index"/> is the API window index
        /// (<see cref="ComposerBlockViewModel.Index"/>), never Number / Label. An index that is not a block is ignored.
        /// </summary>
        public void FocusBlock(int index)
        {
            if (_disposed || _draft == null || Blocks.All(b => b.Index != index) || _focusedBlock == index) return;
            _focusedBlock = index;
            ApplyFocus();
            Raise(nameof(FocusedBlockIndex));
        }

        /// <summary>
        /// An arrow key: the keyboard moves to the nearest block that lies in that direction on the screen (it must
        /// overlap the current block's rows, or columns, so 2x2 and 1+3 walk like a grid and a PIP inset to its
        /// background and back). It never wraps: at an edge it stays. With no block focused yet the first press focuses
        /// the lowest block (it does not move).
        /// </summary>
        public void MoveFocus(FocusDirection direction)
        {
            if (_disposed || _draft == null || Blocks.Count == 0) return;
            var from = Blocks.FirstOrDefault(b => b.Index == _focusedBlock);
            if (from == null)
            {
                FocusBlock(Blocks.Min(b => b.Index));
                return;
            }

            const double eps = 1e-6; // blocks that only touch (2x2 neighbours on the diagonal) do not overlap
            ComposerBlockViewModel? best = null;
            double bestPrimary = 0, bestSide = 0;
            foreach (var b in Blocks)
            {
                if (b.Index == from.Index) continue;

                bool horizontal = direction is FocusDirection.Left or FocusDirection.Right;
                double dx = Centre(b.NormalizedX, b.NormalizedWidth) - Centre(from.NormalizedX, from.NormalizedWidth);
                double dy = Centre(b.NormalizedY, b.NormalizedHeight) - Centre(from.NormalizedY, from.NormalizedHeight);
                double primary = direction switch
                {
                    FocusDirection.Right => dx,
                    FocusDirection.Left => -dx,
                    FocusDirection.Down => dy,
                    _ => -dy,
                };
                if (primary <= eps) continue; // not in that direction

                double overlap = horizontal
                    ? Overlap(from.NormalizedY, from.NormalizedHeight, b.NormalizedY, b.NormalizedHeight)
                    : Overlap(from.NormalizedX, from.NormalizedWidth, b.NormalizedX, b.NormalizedWidth);
                if (overlap <= eps) continue; // beside, not across, from it

                double side = Math.Abs(horizontal ? dy : dx);
                bool better = best == null || primary < bestPrimary - eps ||
                              (Math.Abs(primary - bestPrimary) <= eps && (side < bestSide - eps || (Math.Abs(side - bestSide) <= eps && b.Index < best.Index)));
                if (!better) continue;
                best = b;
                bestPrimary = primary;
                bestSide = side;
            }

            if (best != null) FocusBlock(best.Index);
        }

        private static double Centre(double start, double length) => start + length / 2;

        private static double Overlap(double aStart, double aLength, double bStart, double bLength) =>
            Math.Min(aStart + aLength, bStart + bLength) - Math.Max(aStart, bStart);

        /// <summary>
        /// "Arm" on the focused block: with no source armed it opens the block's source picker (Esc closes it, the
        /// tiles are reached by keyboard). A source already armed (Enter on its card) makes this a no-op: Enter places it.
        /// </summary>
        public void ArmFocused()
        {
            if (_busy || _draft == null || _armed != null || _focusedBlock is not int i) return;
            OpenPicker(i);
        }

        /// <summary>
        /// Enter on the focused block, as a tap on it: an armed source is placed (and stays armed), a move ends here,
        /// an empty block opens its picker, a placed one opens its Replace / Move strip. Does nothing with no block focused.
        /// </summary>
        public void PlaceAtFocus()
        {
            if (_focusedBlock is int i) TapBlock(i);
        }

        /// <summary>Delete on the focused block: the corner clear (staged only). Does nothing with no block focused, never "the first block".</summary>
        public void ClearAtFocus()
        {
            if (_draft != null && _focusedBlock is int i && _draft.Placements.ContainsKey(i)) Clear(i);
        }

        /// <summary>
        /// Esc, one step at a time: close the picker, else cancel a move, else disarm the source, else close an open
        /// Replace / Move strip, else nothing. Never closes the Composer or discards the draft. True when it did something.
        /// </summary>
        public bool Escape()
        {
            if (_disposed || _busy) return false;

            if (_pickerBlock != null)
            {
                _pickerBlock = null;
            }
            else if (_movingFrom != null)
            {
                _movingFrom = null;
                ClearHint();
            }
            else if (_armed != null)
            {
                Disarm();
                return true;
            }
            else if (_selectedBlock != null)
            {
                _selectedBlock = null;
            }
            else
            {
                return false;
            }

            Refresh();
            return true;
        }

        /// <summary>The keyboard block and the one Tab stop follow the blocks that exist (roving focus).</summary>
        private void ApplyFocus()
        {
            int? tabStop = _focusedBlock ?? (Blocks.Count == 0 ? null : Blocks.Min(b => b.Index));
            foreach (var b in Blocks)
            {
                b.IsFocusedBlock = _focusedBlock == b.Index;
                b.IsTabStop = tabStop == b.Index;
            }
        }

        // ---- Take / Undo / Discard ----

        /// <summary>Puts the whole staged picture on screen, or changes nothing. Ignored while a Take or Undo is in flight.</summary>
        public async Task TakeAsync()
        {
            if (_disposed || _busy || _selecting || !CanTake) return;

            IsBusy = true; // synchronously, before the first await: a second tap finds the gate shut
            var device = _display.Device;
            var staged = _draft!;
            bool liveChanged = false;
            try
            {
                var result = await _mv.TakeAsync(device, staged);
                switch (result.Outcome)
                {
                    case TakeOutcome.Applied:
                        _takenAt = _clock();
                        CommitLive();
                        liveChanged = true;
                        Banner = new ComposerBanner("Saved & applied - on screen now.", BannerKind.Success);
                        break;

                    case TakeOutcome.NothingToTake:
                        // The screen already shows this picture (it changed without us hearing): converge on it.
                        _live = _mv.GetLive(device);
                        RebuildLiveBlocks();
                        Banner = new ComposerBanner(NothingToChangeText, BannerKind.Info);
                        break;

                    case TakeOutcome.DisplayChanged:
                        await AdoptRedrawnLayoutAsync(result.NewPlan!);
                        // The orchestrator says which display changed to what; the fixed line is the fallback.
                        Banner = new ComposerBanner(
                            string.IsNullOrWhiteSpace(result.Error) ? DisplayChangedText : result.Error, BannerKind.Warning);
                        break;

                    default:
                        Banner = new ComposerBanner($"Nothing changed on screen. {result.Error}".TrimEnd(), BannerKind.Error);
                        break;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("COMPOSER", $"Take on '{device.DeviceName}' failed", ex);
                Banner = new ComposerBanner($"Could not apply: {ex.Message} Check what is on screen.", BannerKind.Error);
            }
            finally
            {
                IsBusy = false;
                Refresh();
            }

            // Outside the try: a subscriber that throws must not read as a failed Take.
            if (liveChanged) RaiseLiveChanged();
        }

        /// <summary>Takes the previous picture again (only while <see cref="CanUndo"/>).</summary>
        public async Task UndoAsync()
        {
            if (_disposed || _busy || _selecting || !CanUndo) return;

            IsBusy = true;
            var device = _display.Device;
            bool liveChanged = false;
            try
            {
                var result = await _mv.UndoAsync(device);
                if (result.Outcome == TakeOutcome.Applied)
                {
                    _takenAt = null;
                    _draft = _mv.GetLive(device) ?? _draft;
                    SelectedLayoutId = _draft?.Plan.Id;
                    CommitLive();
                    liveChanged = true;
                    Banner = new ComposerBanner("Back to the previous picture.", BannerKind.Success);
                }
                else if (result.Outcome == TakeOutcome.Rejected)
                {
                    Banner = new ComposerBanner($"Nothing changed on screen. {result.Error}".TrimEnd(), BannerKind.Error);
                }
                else if (result.Outcome == TakeOutcome.DisplayChanged)
                {
                    Banner = new ComposerBanner(
                        result.Error ?? "The display changed - the previous picture no longer fits it.", BannerKind.Warning);
                }
                else
                {
                    Banner = new ComposerBanner("Nothing to undo.", BannerKind.Info);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("COMPOSER", $"Undo on '{device.DeviceName}' failed", ex);
                Banner = new ComposerBanner($"Undo failed: {ex.Message} Check what is on screen.", BannerKind.Error);
            }
            finally
            {
                IsBusy = false;
                Refresh();
            }

            if (liveChanged) RaiseLiveChanged();
        }

        /// <summary>Back to what is on screen (an empty draft on the current layout when nothing is live).</summary>
        public void Discard()
        {
            if (_busy || _selecting || _draft == null) return;

            var live = _mv.GetLive(_display.Device);
            _live = live;
            if (live != null)
            {
                _draft = live.Clone(); // staging must never alias what is on screen
                _canvas = _mv.GetCanvas(_display.Device) ?? _canvas;
                SelectedLayoutId = live.Plan.Id;
            }
            else
            {
                _draft = new MultiViewDraft(_draft.Plan);
            }
            ResetTransient();
            Banner = ComposerBanner.None;
            RebuildLiveBlocks();
            Refresh();
        }

        /// <summary>
        /// The decoder was changed by someone else (state re-read from the hardware). ON SCREEN NOW and the change
        /// list follow; the draft is kept unless it had nothing of its own to lose.
        /// </summary>
        /// <param name="notice">
        /// The warning to show when the draft is kept (default "... was changed elsewhere - your draft is kept."). Given
        /// by the overview when a copy replaced the screen; it is shown even when the new screen matches the old one.
        /// </param>
        public void OnLiveChangedElsewhere(MultiViewDraft? live, string? notice = null)
        {
            // Idempotent: the overview re-announces the Composer's own Take / Undo too. Nothing differs, so the Undo
            // window and the banner must stay as they are.
            if (_disposed || (notice == null && IsSameAsCurrentLive(live))) return;

            _live = live?.Clone();
            _takenAt = null;
            if (live != null) _canvas = _mv.GetCanvas(_display.Device) ?? _canvas;
            RebuildLiveBlocks();

            bool followScreen = _draft != null && _live != null &&
                                (_draft.Placements.Count == 0 ||
                                 (_draft.Plan.Name == _live.Plan.Name && _draft.DiffFrom(_live).Count == 0));
            if (followScreen)
            {
                _draft = _live!.Clone();
                SelectedLayoutId = _draft.Plan.Id;
                ResetTransient();
            }

            Refresh();

            if (!followScreen && _draft != null && HasChanges)
                Banner = new ComposerBanner(notice ?? $"{_display.DeviceName} was changed elsewhere - your draft is kept.", BannerKind.Warning);
        }

        /// <summary>Re-evaluates the time-limited Undo; the view calls it from a timer so the offer is withdrawn on time.</summary>
        public void Tick()
        {
            if (_successShownAt is DateTime shown && _clock() - shown >= SuccessBannerLifetime)
                ClearSuccessBanner();

            bool now = CanUndo;
            if (now == _lastCanUndo) return;
            _lastCanUndo = now;
            Raise(nameof(CanUndo));
        }

        /// <summary>Re-reads source availability (a source went offline or came back).</summary>
        public void Refresh()
        {
            if (_disposed) return;
            WatchSources();
            SyncOptions();
            _issues = _draft == null
                ? Array.Empty<BlockIssue>()
                : DraftValidator.Validate(_draft, _mv.UsesExcept(_display.Device), IsOnline);
            _changes = _draft == null ? Array.Empty<DraftChange>() : _draft.DiffFrom(_live);

            if (_draft == null)
            {
                DisposeBlocks(Blocks);
                _blocksPlan = null;
            }
            else
            {
                EnsureBlocks();
                UpdateBlocks();
            }

            _lastCanUndo = CanUndo;

            // The "why Take is off" line (Ctrl+Enter) is withdrawn once that reason is gone.
            if (_takeHint != null && TakeBlockedReason != _takeHint)
            {
                if (Banner.Kind == BannerKind.Info && Banner.Text == _takeHint) Banner = ComposerBanner.None;
                _takeHint = null;
            }
            RaiseDerived();
        }

        /// <summary>Terminal: stops watching the source cards and releases the blocks' preview mirrors. Later calls do nothing.</summary>
        public void Dispose()
        {
            _disposed = true;
            foreach (var s in _watched) s.PropertyChanged -= OnSourceChanged;
            _watched.Clear();
            DisposeBlocks(Blocks);
            DisposeBlocks(LiveBlocks);
        }

        // ---- internals ----

        private bool TryPlace(int slot, SourceCardViewModel s, bool keepPicker = false)
        {
            var allowed = CheckPlace(slot, s);
            if (!allowed.Ok)
            {
                Banner = new ComposerBanner(allowed.Reason ?? "This block cannot take that source.", BannerKind.Warning);
                return false;
            }

            _draft!.Place(slot, s.MacAddress, s.DeviceName);
            if (!keepPicker) _pickerBlock = null;
            _selectedBlock = null;
            if (Banner.Kind == BannerKind.Warning) Banner = ComposerBanner.None;
            ClearSuccessBanner();
            Refresh();
            return true;
        }

        private void MoveBlock(int from, int to)
        {
            var result = _draft!.Move(from, to);
            if (result.Ok)
            {
                if (Banner.Kind == BannerKind.Warning) Banner = ComposerBanner.None;
                ClearSuccessBanner();
            }
            else
            {
                Banner = new ComposerBanner(result.Reason ?? "That move is not possible.", BannerKind.Warning);
            }
        }

        private async Task AdoptRedrawnLayoutAsync(MultiViewLayoutPlan newPlan)
        {
            _draft = _draft!.WithLayout(newPlan);
            SelectedLayoutId = newPlan.Id;
            ResetTransient();
            try
            {
                // Only reads the display: gives the new output format for the header.
                _canvas = (await _mv.PlanForDisplayAsync(_display.Device, newPlan.Id)).Canvas;
            }
            catch (ArgumentException)
            {
                _canvas = null;
            }
        }

        /// <summary>The staged picture is now on screen: it becomes the live strip and Changes empties.</summary>
        private void CommitLive()
        {
            _live = _mv.GetLive(_display.Device);
            _canvas = _mv.GetCanvas(_display.Device) ?? _canvas; // the Take read the display: output format and rate note follow
            RebuildLiveBlocks();
            ResetTransient();
        }

        /// <summary>Tells the overview the picture on screen changed; a subscriber's failure is logged, never propagated.</summary>
        private void RaiseLiveChanged()
        {
            var handlers = LiveChanged;
            if (handlers == null) return;

            foreach (var handler in handlers.GetInvocationList().Cast<Action<MultiViewDraft?>>())
            {
                try
                {
                    handler(_live?.Clone());
                }
                catch (Exception ex)
                {
                    AppLogger.Error("COMPOSER", "A LiveChanged subscriber failed", ex);
                }
            }
        }

        private bool IsSameAsCurrentLive(MultiViewDraft? live) =>
            live == null
                ? _live == null
                : _live != null && live.Plan.Name == _live.Plan.Name && live.DiffFrom(_live).Count == 0;

        private void ResetTransient()
        {
            _armed = null;
            _pickerBlock = null;
            _movingFrom = null;
            _selectedBlock = null;
            ClearHint();
        }

        private void SetHint(string text)
        {
            _hintText = text;
            Banner = new ComposerBanner(text, BannerKind.Info);
        }

        /// <summary>Withdraws the "On screen" line (it expires, or the first edit after it makes it stale); any other banner stays.</summary>
        private void ClearSuccessBanner()
        {
            if (Banner.Kind == BannerKind.Success) Banner = ComposerBanner.None;
        }

        /// <summary>Withdraws the "Placing..." / "Moving..." line once its mode has ended; any other banner stays.</summary>
        private void ClearHint()
        {
            if (_hintText != null && Banner.Kind == BannerKind.Info && Banner.Text == _hintText)
                Banner = ComposerBanner.None;
            _hintText = null;
        }

        private void SyncOptions()
        {
            foreach (var option in LayoutOptions)
            {
                option.IsSelected = option.Id == _selectedLayoutId;
                option.IsPending = _pendingLayoutId != null && option.Id == _pendingLayoutId;
            }
        }

        private void ClearSelection() => _selectedBlock = null;

        private WindowSlot? BlockAt(int index) => _draft?.Plan.Slots.FirstOrDefault(s => s.Index == index);

        /// <summary>
        /// The display number (1-based position in logical order) of the block with API window index
        /// <paramref name="slotIndex"/>; a block only the screen's layout has is numbered by that layout.
        /// </summary>
        private int NumberOf(int slotIndex)
        {
            int n = _draft == null ? -1 : _draft.Plan.Slots.ToList().FindIndex(s => s.Index == slotIndex);
            if (n < 0 && _live != null) n = _live.Plan.Slots.ToList().FindIndex(s => s.Index == slotIndex);
            return n < 0 ? slotIndex + 1 : n + 1;
        }

        private bool IsOnline(string mac) => FindSource(mac)?.IsActive ?? false;

        private SourceCardViewModel? FindSource(string mac)
        {
            string norm = SdvoeClient.NormalizeMac(mac);
            return _sources().FirstOrDefault(s => SdvoeClient.NormalizeMac(s.MacAddress) == norm);
        }

        /// <summary>Rebuilds <see cref="Blocks"/> when the layout changed; otherwise the blocks are updated in place.</summary>
        private void EnsureBlocks()
        {
            var plan = _draft!.Plan;
            if (ReferenceEquals(_blocksPlan, plan)) return;

            DisposeBlocks(Blocks);
            int number = 0;
            foreach (var slot in plan.Slots)
                Blocks.Add(new ComposerBlockViewModel(slot, ++number, Clear));
            _blocksPlan = plan;
            ResetTransientIfStale();
        }

        private void ResetTransientIfStale()
        {
            if (_pickerBlock is int p && BlockAt(p) == null) _pickerBlock = null;
            if (_movingFrom is int m && BlockAt(m) == null) _movingFrom = null;
            if (_selectedBlock is int s && BlockAt(s) == null) _selectedBlock = null;
            if (_focusedBlock is int f && BlockAt(f) == null)
            {
                _focusedBlock = null; // the layout has no such block now: the keyboard starts over at the first one
                Raise(nameof(FocusedBlockIndex));
            }
        }

        private void UpdateBlocks()
        {
            var draft = _draft!;
            var others = _mv.UsesExcept(_display.Device).ToList();
            bool sameLayout = _live != null && _live.Plan.Name == draft.Plan.Name;

            foreach (var block in Blocks)
            {
                draft.Placements.TryGetValue(block.Index, out var placement);
                var card = placement == null ? null : FindSource(placement.SourceMac);
                block.SetSource(placement?.SourceMac, placement?.SourceName, card);

                var blockIssues = _issues.Where(i => i.SlotIndex == block.Index).Select(i => i.Message).ToList();
                string? issueText = blockIssues.Count == 0 ? null : string.Join("; ", blockIssues);

                DraftPlacement? onScreen = null;
                if (sameLayout) _live!.Placements.TryGetValue(block.Index, out onScreen);

                BlockState state;
                if (issueText != null) state = BlockState.Issue;
                else if (placement != null)
                    state = onScreen != null && SameSource(onScreen, placement) ? BlockState.Live : BlockState.Staged;
                else if (!sameLayout) state = BlockState.Empty;
                else state = onScreen != null ? BlockState.Staged : BlockState.Live; // cleared = goes black; else already black

                string sameAs = string.Empty;
                int sameAsBlock = 0;
                if (placement != null)
                {
                    int first = draft.Plan.Slots.TakeWhile(s => !SameSource(draft.Placements.GetValueOrDefault(s.Index), placement)).Count() + 1;
                    if (first < block.Number)
                    {
                        sameAs = $"Same as {first}";
                        sameAsBlock = first;
                    }
                }

                bool available = true;
                string? reason = null;
                if (_armed != null)
                {
                    var check = DraftValidator.CanPlace(draft, block.Index, _armed.MacAddress, _armed.DeviceName, others, IsOnline);
                    available = check.Ok;
                    reason = check.Ok ? null : check.Reason;
                }

                block.SetStatus(state, issueText, sameAs, available, reason, sameAsBlock);
                block.IsSelected = _selectedBlock == block.Index;
                block.IsMoveSource = _movingFrom == block.Index;
            }
            ApplyFocus();
        }

        private static bool SameSource(DraftPlacement? a, DraftPlacement b) =>
            a != null && SdvoeClient.NormalizeMac(a.SourceMac) == SdvoeClient.NormalizeMac(b.SourceMac);

        private void RebuildLiveBlocks()
        {
            DisposeBlocks(LiveBlocks);
            if (_live == null || _disposed) return;

            int number = 0;
            foreach (var slot in _live.Plan.Slots)
            {
                var block = new ComposerBlockViewModel(slot, ++number, null);
                if (_live.Placements.TryGetValue(slot.Index, out var p))
                    block.SetSource(p.SourceMac, p.SourceName, FindSource(p.SourceMac));
                block.SetStatus(BlockState.Live, null, string.Empty, true, null);
                LiveBlocks.Add(block);
            }
        }

        private static void DisposeBlocks(ObservableCollection<ComposerBlockViewModel> blocks)
        {
            foreach (var b in blocks) b.Dispose();
            blocks.Clear();
        }

        /// <summary>Follows source availability: a card going offline or online re-validates the draft.</summary>
        private void WatchSources()
        {
            if (_disposed) return;

            var current = _sources().ToHashSet();
            foreach (var gone in _watched.Where(s => !current.Contains(s)).ToList())
            {
                gone.PropertyChanged -= OnSourceChanged;
                _watched.Remove(gone);
            }
            foreach (var s in current)
                if (_watched.Add(s)) s.PropertyChanged += OnSourceChanged;
        }

        private void OnSourceChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!_disposed && e.PropertyName == nameof(SourceCardViewModel.IsActive)) Refresh();
        }

        private void RaiseDerived()
        {
            foreach (var name in new[]
            {
                nameof(Draft), nameof(ArmedSource), nameof(PickerBlockIndex), nameof(MovingFromIndex), nameof(Changes), nameof(ChangeLines),
                nameof(ChangeCount), nameof(IsStaged), nameof(IsOnScreen),
                nameof(StepLayoutDone), nameof(StepPlaceDone), nameof(StepLayoutText), nameof(StepPlaceText), nameof(StepTakeText), nameof(OnScreenSummary), nameof(LiveAspectRatio), nameof(CanTake), nameof(TakeLabel), nameof(TakeBlockedReason), nameof(TakeAutomationName),
                nameof(CanUndo), nameof(UnplacedSources), nameof(OutputText), nameof(RateNote), nameof(HeaderNotice),
            })
                Raise(name);
        }

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            Raise(name);
            return true;
        }

        private void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
