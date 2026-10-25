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
        private bool _isExternalLayout;
        private int _externalSourceCount;
        private string _externalLayoutText = string.Empty;
        private double _canvasAspect = 16.0 / 9.0;
        private List<int> _logicalOrder = new();

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

        /// <summary>What to tell the operator about an external layout.</summary>
        public string ExternalLayoutText => _externalLayoutText;

        /// <summary>Layout currently on this display: <c>single</c> or a multiview preset id.</summary>
        public string LayoutId => _layoutId;

        public bool IsMultiView => MultiViewPresets.IsMultiView(_layoutId);

        /// <summary>Drop zones of the active multiview layout, painted back to front. Empty in Single View.</summary>
        public ObservableCollection<WindowSlotViewModel> Slots { get; } = new();

        /// <summary>Header pill text: layout name in multiview, otherwise routed state.</summary>
        public string ModeBadge => IsMultiView ? _layoutTitle : _isExternalLayout ? "EXTERNAL LAYOUT" : IsRouted ? "SINGLE VIEW" : "UNROUTED";

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
        public string CanvasInfo => _canvasInfo;

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

        public DisplayCardViewModel(AvasDevice device, Func<AvasDevice, System.Threading.Tasks.Task>? unrouteAction = null)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _unrouteAction = unrouteAction;

            UnrouteCommand = new RelayCommand(async _ =>
            {
                if (_unrouteAction != null)
                {
                    await _unrouteAction(_device);
                    SetPreviewSource(null);
                    NotifyRoutingChanged();
                }
            }, _ => IsRouted || IsMultiView);

            Slots.CollectionChanged += (_, _) => NotifyLayoutChanged();
        }

        /// <summary>Switches the card to a multiview layout with every window empty.</summary>
        public void ApplyLayout(MultiViewLayoutPlan plan, string title, string canvasInfo = "")
        {
            DetachSlotSources();
            SetPreviewSource(null);

            ResetExternalLayout();
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
            _layoutId = "single";
            _layoutTitle = string.Empty;
            _logicalOrder = new List<int>();
            _canvasAspect = 16.0 / 9.0;
            LastError = null;

            _isExternalLayout = true;
            _externalSourceCount = sourceNames.Count;
            string what = string.IsNullOrEmpty(layoutName)
                ? "Running a multiview layout set elsewhere (read-only)"
                : $"Running layout '{layoutName}' set elsewhere (read-only)";
            _externalLayoutText = sourceNames.Count == 0 ? what : $"{what}: {string.Join(", ", sourceNames)}";
            _canvasInfo = canvasInfo;
            OnPropertyChanged(nameof(IsExternalLayout));
            OnPropertyChanged(nameof(ExternalLayoutText));
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
            OnPropertyChanged(nameof(IsExternalLayout));
            OnPropertyChanged(nameof(ExternalLayoutText));
        }

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
            OnPropertyChanged(nameof(LayoutId));
            OnPropertyChanged(nameof(IsMultiView));
            OnPropertyChanged(nameof(ModeBadge));
            OnPropertyChanged(nameof(CanvasInfo));
            OnPropertyChanged(nameof(CanvasAspect));
            OnPropertyChanged(nameof(SourceLabel));
            OnPropertyChanged(nameof(IsRouted));
            OnPropertyChanged(nameof(FirstEmptySlotIndex));
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
        }

        public void NotifyRoutingChanged()
        {
            OnPropertyChanged(nameof(ActiveSourceId));
            OnPropertyChanged(nameof(ActiveSourceName));
            OnPropertyChanged(nameof(IsRouted));
            OnPropertyChanged(nameof(SourceLabel));
            OnPropertyChanged(nameof(ModeBadge));
            OnPropertyChanged(nameof(PreviewBitmap));
            OnPropertyChanged(nameof(IsPreviewSupported));
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
