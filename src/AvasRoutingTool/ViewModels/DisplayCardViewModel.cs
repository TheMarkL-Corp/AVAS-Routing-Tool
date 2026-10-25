using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using AvasRoutingTool.Sdvoe;

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

        public event PropertyChangedEventHandler? PropertyChanged;

        public AvasDevice Device => _device;
        public string DeviceName => _device.DeviceName;
        public string IpAddress => _device.IpAddress;
        public string MacAddress => _device.DeviceId;
        public bool IsActive => _device.IsActive;

        public string ActiveSourceId => _device.ActiveSourceId;
        public string ActiveSourceName => _device.ActiveSourceName;
        public bool IsRouted => !string.IsNullOrEmpty(_device.ActiveSourceId);

        /// <summary>Footer label: the routed source's BAVM name, or "NONE" when unrouted.</summary>
        public string SourceLabel => IsRouted && !string.IsNullOrEmpty(ActiveSourceName) ? ActiveSourceName : "NONE";

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
            }, _ => IsRouted);
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
            OnPropertyChanged(nameof(PreviewBitmap));
            OnPropertyChanged(nameof(IsPreviewSupported));
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
