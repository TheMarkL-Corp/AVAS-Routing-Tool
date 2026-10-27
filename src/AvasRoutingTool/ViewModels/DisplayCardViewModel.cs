using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Sdvoe.MultiView;

namespace AvasRoutingTool.ViewModels
{
    public class DisplayCardViewModel : INotifyPropertyChanged
    {
        private readonly AvasDevice _device;
        private readonly Func<AvasDevice, System.Threading.Tasks.Task>? _unrouteAction;
        private SourceCardViewModel? _currentSourceVm;
        private WriteableBitmap? _previewBitmap;
        private bool _isPreviewSupported;
        private bool _isDragOver;
        private string _layoutId = "single";
        private string _layoutTitle = string.Empty;
        private string? _lastError;
        private string _canvasInfo = string.Empty;
        private string _singleViewOutput = string.Empty;
        private bool _isExternalLayout;
        private int _externalSourceCount;
        private string _externalLayoutText = string.Empty;
        private string _externalLayoutName = string.Empty;
        private double _canvasAspect = 16.0 / 9.0;
        private List<int> _logicalOrder = new();
        private bool _isLayoutMenuOpen;
        private bool _hasDraft;
        private SourceCardViewModel? _dropChoiceSource;
        private CopyDecision? _copyTarget;
        private bool _isCopySource;
        private string? _undoToastText;
        private string? _draftNotice;
        private Func<DisplayCardViewModel, System.Threading.Tasks.Task>? _copyLayoutAction;
        private Func<DisplayCardViewModel, System.Threading.Tasks.Task>? _undoScreenAction;

        public event PropertyChangedEventHandler? PropertyChanged;

        public AvasDevice Device => _device;
        public string DeviceName => _device.DeviceName;
        public string IpAddress => _device.IpAddress;
        public string MacAddress => _device.DeviceId;
        public bool IsActive => _device.IsActive;

        public string ActiveSourceId => _device.ActiveSourceId;
        public string ActiveSourceName => _device.ActiveSourceName;
        public bool IsRouted => !string.IsNullOrEmpty(_device.ActiveSourceId) || Slots.Any(s => s.IsAssigned) || _isExternalLayout;

        /// <summary>The decoder is running a multiview layout this tool did not create (found when restoring state). Read-only.</summary>
        public bool IsExternalLayout => _isExternalLayout;

        /// <summary>
        /// What to tell the operator about an external layout. A layout this tool copied here (<c>avas_&lt;mac&gt;_copy_&lt;W&gt;x&lt;H&gt;</c>)
        /// reads "Copied layout (read-only)"; its technical name stays in <see cref="ExternalLayoutName"/> (the tooltip).
        /// </summary>
        public string ExternalLayoutText => _externalLayoutText;

        /// <summary>The external layout's name on the Control Server (shown as a tooltip); empty when there is none.</summary>
        public string ExternalLayoutName => _externalLayoutName;

        /// <summary>Layout currently on this display: <c>single</c> or a multiview preset id.</summary>
        public string LayoutId => _layoutId;

        public bool IsMultiView => MultiViewPresets.IsMultiView(_layoutId);

        /// <summary>Drop zones of the active multiview layout, painted back to front. Empty in Single View.</summary>
        public ObservableCollection<WindowSlotViewModel> Slots { get; } = new();

        /// <summary>
        /// Header pill text. In multiview: the layout and how many of its blocks carry a source on screen
        /// ("MATRIX 2X2 · 3/4"); otherwise the routed state.
        /// </summary>
        public string ModeBadge => IsMultiView
            ? $"{_layoutTitle.ToUpperInvariant()} · {Slots.Count(s => s.IsAssigned)}/{Slots.Count}"
            : _isExternalLayout ? "EXTERNAL LAYOUT" : IsRouted ? "SINGLE VIEW" : "UNROUTED";

        /// <summary>The header "Layout" menu: Single View, the four MultiView presets, Off. The layout on screen is selected.</summary>
        public IReadOnlyList<LayoutItemViewModel> LayoutMenu { get; }

        /// <summary>The header "Layout" menu is open.</summary>
        public bool IsLayoutMenuOpen
        {
            get => _isLayoutMenuOpen;
            set { if (_isLayoutMenuOpen == value) return; _isLayoutMenuOpen = value; OnPropertyChanged(); }
        }

        /// <summary>The Composer was closed with changes that are not on screen ("DRAFT - not on screen", Resume). Set by <see cref="MainViewModel"/>.</summary>
        public bool HasDraft
        {
            get => _hasDraft;
            set { if (_hasDraft == value) return; _hasDraft = value; OnPropertyChanged(); }
        }

        /// <summary>A source was dropped or tapped on this MultiView card: "Full screen (Single View)" or "Into a block...".</summary>
        public bool IsDropChoiceOpen => _dropChoiceSource != null;

        /// <summary>The source the drop choice is about; null when the choice is closed.</summary>
        public SourceCardViewModel? DropChoiceSource => _dropChoiceSource;

        public void OpenDropChoice(SourceCardViewModel source)
        {
            _dropChoiceSource = source ?? throw new ArgumentNullException(nameof(source));
            OnPropertyChanged(nameof(DropChoiceSource));
            OnPropertyChanged(nameof(FullScreenAutomationName));
            OnPropertyChanged(nameof(IntoBlockAutomationName));
            OnPropertyChanged(nameof(IsDropChoiceOpen));
        }

        public void CloseDropChoice()
        {
            if (_dropChoiceSource == null) return;
            _dropChoiceSource = null;
            OnPropertyChanged(nameof(DropChoiceSource));
            OnPropertyChanged(nameof(FullScreenAutomationName));
            OnPropertyChanged(nameof(IntoBlockAutomationName));
            OnPropertyChanged(nameof(IsDropChoiceOpen));
        }

        /// <summary>Lowest-numbered window that has no source yet, or null when all are filled / not multiview.</summary>
        public int? FirstEmptySlotIndex
        {
            get
            {
                foreach (int index in _logicalOrder)
                {
                    var slot = Slots.FirstOrDefault(s => s.Index == index);
                    if (slot != null && !slot.IsAssigned) return index;
                }
                return null;
            }
        }

        /// <summary>The decoder output format read from the display EDID (and a bandwidth note), shown on the card.</summary>
        public string CanvasInfo => !string.IsNullOrEmpty(_canvasInfo) ? _canvasInfo : IsRouted ? _singleViewOutput : string.Empty;

        /// <summary>The output format of a multiview or external layout only (what the Composer reuses); empty in Single View.</summary>
        public string LayoutCanvasInfo => _canvasInfo;

        /// <summary>The Single View output format ("OUT 1920x1080@60 (display EDID unavailable)") shown while this card is routed.</summary>
        public void SetSingleViewOutput(string info)
        {
            _singleViewOutput = info ?? string.Empty;
            OnPropertyChanged(nameof(CanvasInfo));
        }

        /// <summary>Width / height of the multiview canvas, so the window surface is letterboxed like the real output.</summary>
        public double CanvasAspect => _canvasAspect;

        /// <summary>Why the last multiview action was refused, shown on the card. Null when fine.</summary>
        public string? LastError
        {
            get => _lastError;
            set { _lastError = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasError)); }
        }

        public bool HasError => !string.IsNullOrEmpty(_lastError);

        /// <summary>Footer label: the routed source's BAVM name ("N IN LAYOUT" in multiview), or "NONE" when unrouted.</summary>
        public string SourceLabel => IsMultiView
            ? $"{Slots.Count(s => s.IsAssigned)} IN LAYOUT"
            : _isExternalLayout
                ? $"{_externalSourceCount} IN LAYOUT"
                : IsRouted && !string.IsNullOrEmpty(ActiveSourceName) ? ActiveSourceName : "NONE";

        public WriteableBitmap? PreviewBitmap
        {
            get => _previewBitmap;
            set { _previewBitmap = value; OnPropertyChanged(); }
        }

        public bool IsPreviewSupported
        {
            get => _isPreviewSupported;
            set { _isPreviewSupported = value; OnPropertyChanged(); }
        }

        public bool IsDragOver
        {
            get => _isDragOver;
            set { _isDragOver = value; OnPropertyChanged(); }
        }

        public ICommand UnrouteCommand { get; }

        // ---- copy this display onto another (spec 15) ----

        /// <summary>The display shows something that can be copied (a Single View route, a multiview, an external layout).</summary>
        public bool CanCopyFrom => IsActive && (IsMultiView || _isExternalLayout || !string.IsNullOrEmpty(ActiveSourceId));

        /// <summary>Why the Copy layout handle is disabled ("RX1 shows nothing to copy"); empty when it is enabled.</summary>
        public string CopyFromBlockedText => CanCopyFrom ? string.Empty : $"{DeviceName} shows nothing to copy";

        /// <summary>This display's Copy layout is armed (or being dragged): it is the copy source.</summary>
        public bool IsCopySource => _isCopySource;

        /// <summary>A display is armed for copying and this card may receive it (cyan ring, "Release to copy here").</summary>
        public bool IsCopyTargetAllowed => _copyTarget?.Allowed == true;

        /// <summary>A display is armed for copying and this card may not receive it (lock + <see cref="CopyTargetText"/>).</summary>
        public bool IsCopyTargetBlocked => _copyTarget != null && !_copyTarget.Allowed;

        /// <summary>While a copy is armed: the note or summary when allowed, the reason when blocked; else empty.</summary>
        public string CopyTargetText =>
            _copyTarget == null ? string.Empty
            : _copyTarget.Allowed ? _copyTarget.Note ?? _copyTarget.Summary
            : _copyTarget.Reason ?? string.Empty;

        /// <summary>
        /// What a screen reader says for the card's picture. While a copy is armed the card is a target and says so with
        /// <see cref="CopyTargetText"/> (what will be copied, or why it cannot be); otherwise the display and what it shows.
        /// </summary>
        public string PictureAutomationName
        {
            get
            {
                if (IsCopyTargetAllowed) return $"{DeviceName}, copy target. {CopyTargetText}";
                if (IsCopyTargetBlocked) return $"{DeviceName}, cannot copy here. {CopyTargetText}";
                if (_isCopySource) return $"{DeviceName}, copying from here";
                return IsRouted ? $"{DeviceName}, picture on screen, {ModeBadge}" : $"{DeviceName}, nothing on screen";
            }
        }

        /// <summary>What a screen reader says for the drop choice's "Full screen" button: which source it puts on screen.</summary>
        public string FullScreenAutomationName =>
            _dropChoiceSource == null
                ? "Full screen, Single View, goes on screen now"
                : $"Full screen: {_dropChoiceSource.DeviceName}, Single View, goes on screen now";

        /// <summary>What a screen reader says for the drop choice's "Into a block" button: which source it will place.</summary>
        public string IntoBlockAutomationName =>
            _dropChoiceSource == null
                ? "Into a block, opens the Composer"
                : $"Into a block: {_dropChoiceSource.DeviceName}, opens the Composer";

        /// <summary>What a screen reader says for the Copy layout handle: what it copies from, why it is off, or that it is armed.</summary>
        public string CopyHandleAutomationName =>
            !CanCopyFrom ? $"Copy layout from {DeviceName}. {CopyFromBlockedText}"
            : _isCopySource ? $"Copy layout from {DeviceName}, armed. Press again to stop."
            : $"Copy layout from {DeviceName}";

        /// <summary>"PAX355S now shows RX1's Matrix 2x2" for 10 s after a copy onto this display; null otherwise.</summary>
        public string? UndoToastText => _undoToastText;

        /// <summary>The toast's Undo is offered (10 s after a copy onto this display).</summary>
        public bool CanUndoScreen => _undoToastText != null;

        /// <summary>The kept draft is based on a picture that is no longer on screen (a copy replaced it); null otherwise.</summary>
        public string? DraftNotice
        {
            get => _draftNotice;
            set { if (_draftNotice == value) return; _draftNotice = value; OnPropertyChanged(); }
        }

        /// <summary>The toast's Undo: puts back what this display showed before the copy.</summary>
        public ICommand UndoScreenCommand { get; }

        /// <summary>The "Copy layout" handle tapped: arms this display as the copy source (tap again to stop).</summary>
        public ICommand CopyLayoutCommand { get; }

        /// <summary>Connects the copy commands to the overview (<see cref="MainViewModel"/> does this for every card it shows).</summary>
        public void UseCopyActions(Func<DisplayCardViewModel, System.Threading.Tasks.Task> copyLayout,
            Func<DisplayCardViewModel, System.Threading.Tasks.Task> undoScreen)
        {
            _copyLayoutAction = copyLayout;
            _undoScreenAction = undoScreen;
        }

        /// <summary>This card's state as a copy target; null when no copy is armed.</summary>
        public void SetCopyTarget(CopyDecision? decision)
        {
            if (_copyTarget == null && decision == null) return;
            _copyTarget = decision;
            OnPropertyChanged(nameof(IsCopyTargetAllowed));
            OnPropertyChanged(nameof(IsCopyTargetBlocked));
            OnPropertyChanged(nameof(CopyTargetText));
            OnPropertyChanged(nameof(PictureAutomationName));
        }

        public void SetCopySource(bool isSource)
        {
            if (_isCopySource == isSource) return;
            _isCopySource = isSource;
            OnPropertyChanged(nameof(IsCopySource));
            OnPropertyChanged(nameof(PictureAutomationName));
            OnPropertyChanged(nameof(CopyHandleAutomationName));
        }

        public void ShowUndoScreen(string text)
        {
            _undoToastText = text;
            OnPropertyChanged(nameof(UndoToastText));
            OnPropertyChanged(nameof(CanUndoScreen));
        }

        public void ClearUndoScreen()
        {
            if (_undoToastText == null) return;
            _undoToastText = null;
            OnPropertyChanged(nameof(UndoToastText));
            OnPropertyChanged(nameof(CanUndoScreen));
        }

        private void NotifyCopySourceChanged()
        {
            OnPropertyChanged(nameof(CanCopyFrom));
            OnPropertyChanged(nameof(CopyFromBlockedText));
            OnPropertyChanged(nameof(CopyHandleAutomationName));
            OnPropertyChanged(nameof(PictureAutomationName));
        }

        public DisplayCardViewModel(AvasDevice device, Func<AvasDevice, System.Threading.Tasks.Task>? unrouteAction = null)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _unrouteAction = unrouteAction;

            CopyLayoutCommand = new RelayCommand(async _ =>
            {
                if (_copyLayoutAction != null) await _copyLayoutAction(this);
            }, _ => CanCopyFrom);
            UndoScreenCommand = new RelayCommand(async _ =>
            {
                if (_undoScreenAction != null) await _undoScreenAction(this);
            }, _ => CanUndoScreen);

            UnrouteCommand = new RelayCommand(async _ =>
            {
                if (_unrouteAction != null)
                {
                    await _unrouteAction(_device);
                    SetPreviewSource(null);
                    NotifyRoutingChanged();
                }
            }, _ => IsRouted || IsMultiView);

            var catalog = LayoutItemViewModel.CreateCatalog();
            LayoutMenu = catalog.Where(l => l.Id != "off").Concat(catalog.Where(l => l.Id == "off")).ToList();
            SyncLayoutMenu();

            Slots.CollectionChanged += (_, _) => NotifyLayoutChanged();
        }

        private void SyncLayoutMenu()
        {
            foreach (var item in LayoutMenu)
            {
                bool selected = !_isExternalLayout && item.Id == _layoutId;
                if (item.IsSelected != selected) item.IsSelected = selected;
            }
        }

        /// <summary>Switches the card to a multiview layout with every window empty.</summary>
        public void ApplyLayout(MultiViewLayoutPlan plan, string title, string canvasInfo = "")
        {
            DetachSlotSources();
            SetPreviewSource(null);

            ResetExternalLayout();
            _singleViewOutput = string.Empty;
            _layoutId = plan.Id;
            _layoutTitle = title;
            _canvasInfo = canvasInfo;
            _canvasAspect = (double)plan.Width / plan.Height;
            _logicalOrder = plan.Slots.Select(s => s.Index).ToList();
            LastError = null;

            Slots.Clear();
            foreach (var slot in plan.PaintOrder) Slots.Add(new WindowSlotViewModel(slot));

            NotifyLayoutChanged();
        }

        /// <summary>Back to Single View: drops every window.</summary>
        public void ClearLayout()
        {
            DetachSlotSources();
            ResetExternalLayout();
            _layoutId = "single";
            _layoutTitle = string.Empty;
            _canvasInfo = string.Empty;
            _canvasAspect = 16.0 / 9.0;
            _logicalOrder = new List<int>();
            LastError = null;
            Slots.Clear();
            NotifyLayoutChanged();
        }

        /// <summary>
        /// Shows that the decoder runs a layout made elsewhere (BAVM, the built-in 4K 2x2): its sources are listed but
        /// there are no drop zones. Plain routing, Off and Single View still work (they leave multiview first).
        /// </summary>
        public void SetExternalLayout(string layoutName, IReadOnlyList<string> sourceNames, string canvasInfo)
        {
            DetachSlotSources();
            Slots.Clear();
            SetPreviewSource(null);
            _singleViewOutput = string.Empty;
            _layoutId = "single";
            _layoutTitle = string.Empty;
            _logicalOrder = new List<int>();
            _canvasAspect = 16.0 / 9.0;
            LastError = null;

            _isExternalLayout = true;
            _externalSourceCount = sourceNames.Count;
            _externalLayoutName = layoutName ?? string.Empty;
            string what = string.IsNullOrEmpty(layoutName)
                ? "Running a multiview layout set elsewhere (read-only)"
                : IsCopiedLayoutName(layoutName)
                    ? "Copied layout (read-only)"
                    : $"Running layout '{layoutName}' set elsewhere (read-only)";
            _externalLayoutText = sourceNames.Count == 0 ? what : $"{what}: {string.Join(", ", sourceNames)}";
            _canvasInfo = canvasInfo;
            OnPropertyChanged(nameof(IsExternalLayout));
            OnPropertyChanged(nameof(ExternalLayoutText));
            OnPropertyChanged(nameof(ExternalLayoutName));
            NotifyLayoutChanged();
        }

        public void ClearExternalLayout()
        {
            if (!_isExternalLayout) return;
            ResetExternalLayout();
            _canvasInfo = string.Empty;
            NotifyLayoutChanged();
        }

        private void ResetExternalLayout()
        {
            if (!_isExternalLayout) return;
            _isExternalLayout = false;
            _externalSourceCount = 0;
            _externalLayoutText = string.Empty;
            _externalLayoutName = string.Empty;
            OnPropertyChanged(nameof(IsExternalLayout));
            OnPropertyChanged(nameof(ExternalLayoutText));
            OnPropertyChanged(nameof(ExternalLayoutName));
        }

        /// <summary>A layout this tool copied onto a display (<see cref="MultiViewPresets.CopyLayoutName"/>): <c>avas_..._copy_...</c>.</summary>
        private static bool IsCopiedLayoutName(string name) =>
            name.StartsWith("avas_", StringComparison.Ordinal) && name.Contains("_copy_", StringComparison.Ordinal);

        public void AssignSlot(int slotIndex, SourceCardViewModel? source)
        {
            Slots.FirstOrDefault(s => s.Index == slotIndex)?.SetSource(source);
            NotifyLayoutChanged();
        }

        public void ClearSlot(int slotIndex) => AssignSlot(slotIndex, null);

        private void DetachSlotSources()
        {
            foreach (var slot in Slots) slot.SetSource(null);
        }

        private void NotifyLayoutChanged()
        {
            SyncLayoutMenu();
            OnPropertyChanged(nameof(LayoutId));
            OnPropertyChanged(nameof(IsMultiView));
            OnPropertyChanged(nameof(ModeBadge));
            OnPropertyChanged(nameof(CanvasInfo));
            OnPropertyChanged(nameof(CanvasAspect));
            OnPropertyChanged(nameof(SourceLabel));
            OnPropertyChanged(nameof(IsRouted));
            OnPropertyChanged(nameof(FirstEmptySlotIndex));
            NotifyCopySourceChanged();
        }

        public void SetPreviewSource(SourceCardViewModel? sourceVm)
        {
            if (_currentSourceVm != null)
            {
                _currentSourceVm.PropertyChanged -= OnSourcePropertyChanged;
            }

            _currentSourceVm = sourceVm;

            if (_currentSourceVm != null)
            {
                _currentSourceVm.PropertyChanged += OnSourcePropertyChanged;
                IsPreviewSupported = _currentSourceVm.IsPreviewSupported;
                PreviewBitmap = _currentSourceVm.PreviewBitmap;
            }
            else
            {
                IsPreviewSupported = false;
                PreviewBitmap = null;
            }
        }

        private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SourceCardViewModel.PreviewBitmap) && _currentSourceVm != null)
            {
                PreviewBitmap = _currentSourceVm.PreviewBitmap;
            }
            else if (e.PropertyName == nameof(SourceCardViewModel.IsPreviewSupported) && _currentSourceVm != null)
            {
                IsPreviewSupported = _currentSourceVm.IsPreviewSupported;
            }
        }

        public void SetOnlineState(bool isOnline)
        {
            _device.IsActive = isOnline;
            OnPropertyChanged(nameof(IsActive));
            NotifyCopySourceChanged();
        }

        public void NotifyRoutingChanged()
        {
            // The route changed: a Single View format shown for the earlier one is not known to hold. The caller that knows it sets it again.
            _singleViewOutput = string.Empty;
            OnPropertyChanged(nameof(CanvasInfo));
            OnPropertyChanged(nameof(ActiveSourceId));
            OnPropertyChanged(nameof(ActiveSourceName));
            OnPropertyChanged(nameof(IsRouted));
            OnPropertyChanged(nameof(SourceLabel));
            OnPropertyChanged(nameof(ModeBadge));
            OnPropertyChanged(nameof(PreviewBitmap));
            OnPropertyChanged(nameof(IsPreviewSupported));
            NotifyCopySourceChanged();
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
