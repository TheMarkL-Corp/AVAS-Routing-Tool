using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using AvasRoutingTool.Logging;
using AvasRoutingTool.Rtp;
using AvasRoutingTool.Sdvoe;

namespace AvasRoutingTool.ViewModels
{
    public class SourceCardViewModel : INotifyPropertyChanged
    {
        private readonly AvasDevice _device;
        private ISdvoeClient? _sdvoeClient;
        private IRtpReceiver? _rtpReceiver;
        private IRtpStreamReceiver? _channel;
        private WriteableBitmap? _previewBitmap;
        private double _fps;
        private bool _isStreaming;
        private bool _isRefreshing;
        private bool _isArmed;
        private int _usedInBlocks;

        public event PropertyChangedEventHandler? PropertyChanged;

        public AvasDevice Device => _device;
        public string DeviceName => _device.DeviceName;
        public string IpAddress => _device.IpAddress;
        public string MacAddress => _device.DeviceId;
        public bool IsActive => _device.IsActive;

        public string LinkMode => _device.LinkMode;

        /// <summary>What a screen reader says for the source: its name and state ("TX2, online, placing - press Enter to stop, in 2 blocks").</summary>
        public string AutomationName
        {
            get
            {
                string name = $"{DeviceName}, {(IsActive ? "online" : "offline")}";
                if (_isArmed) name += ", placing - press Enter to stop";
                if (_usedInBlocks > 0) name += $", in {_usedInBlocks} block{(_usedInBlocks == 1 ? string.Empty : "s")}";
                return name;
            }
        }
        public bool IsDualLink => string.Equals(_device.LinkMode, "Dual Link", StringComparison.OrdinalIgnoreCase);
        public bool IsPreviewSupported => _device.IsPreviewSupported;
        public string PreviewMulticastIp => !string.IsNullOrEmpty(_device.MulticastIp)
            ? $"{_device.MulticastIp}:{_device.MulticastPort}"
            : "N/A";

        public double Fps
        {
            get => _fps;
            set { _fps = value; OnPropertyChanged(); OnPropertyChanged(nameof(StreamStatusText)); }
        }

        public bool IsStreaming
        {
            get => _isStreaming;
            set
            {
                _isStreaming = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowStop));
                OnPropertyChanged(nameof(ShowStart));
                OnPropertyChanged(nameof(ShowStandby));
                OnPropertyChanged(nameof(ShowConnecting));
                OnPropertyChanged(nameof(StreamStatusText));
            }
        }

        public bool IsRefreshing
        {
            get => _isRefreshing;
            set { _isRefreshing = value; OnPropertyChanged(); }
        }

        public WriteableBitmap? PreviewBitmap
        {
            get => _previewBitmap;
            set { _previewBitmap = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowConnecting)); }
        }

        /// <summary>
        /// Tapped to place: the next tap on a display routes it (Single View), inside the Composer it fills blocks.
        /// At most one source is armed at a time; set by <see cref="MainViewModel"/>.
        /// </summary>
        public bool IsArmed
        {
            get => _isArmed;
            set { if (_isArmed == value) return; _isArmed = value; OnPropertyChanged(); OnPropertyChanged(nameof(AutomationName)); }
        }

        /// <summary>How many blocks of the Composer's staged picture show this encoder ("in 2 blocks"); 0 outside the Composer.</summary>
        public int UsedInBlocks
        {
            get => _usedInBlocks;
            set
            {
                if (_usedInBlocks == value) return;
                _usedInBlocks = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AutomationName));
                OnPropertyChanged(nameof(UsedInText));
                OnPropertyChanged(nameof(HasUsage));
            }
        }

        // ---- View state: read-only, derived from the flags above; the card view binds these instead of stacking XAML triggers. ----

        /// <summary>"in 1 block" / "in 3 blocks" for the Composer's staged picture; empty while the source is in no block.</summary>
        public string UsedInText => _usedInBlocks <= 0 ? string.Empty : _usedInBlocks == 1 ? "in 1 block" : $"in {_usedInBlocks} blocks";

        /// <summary>The source is in at least one block of the Composer's staged picture.</summary>
        public bool HasUsage => _usedInBlocks > 0;

        /// <summary>Refresh is offered for every source that has a preview at all.</summary>
        public bool ShowRefresh => IsPreviewSupported;

        public bool ShowStop => _isStreaming;

        public bool ShowStart => IsPreviewSupported && !_isStreaming;

        /// <summary>"Preview Standby" placeholder: an AVAS source that is not streaming.</summary>
        public bool ShowStandby => IsPreviewSupported && !_isStreaming;

        /// <summary>"Connecting preview" placeholder: streaming, but no frame has arrived yet.</summary>
        public bool ShowConnecting => _isStreaming && _previewBitmap == null;

        /// <summary>"Preview not supported" placeholder: a non-AVAS source.</summary>
        public bool ShowUnsupported => !IsPreviewSupported;

        /// <summary>Footer line: the frame rate while streaming, else why there is no picture.</summary>
        public string StreamStatusText => _isStreaming
            ? (_fps <= 0 ? "Waiting for signal" : string.Format(CultureInfo.InvariantCulture, "{0:F1} FPS", _fps))
            : IsPreviewSupported ? "Preview paused" : "External source";

        public ICommand RefreshPreviewCommand { get; }
        public ICommand StartPreviewCommand { get; }
        public ICommand StopPreviewCommand { get; }

        /// <summary>The pencil: opens the rename dialog for this device (runs <see cref="RenameHandler"/>).</summary>
        public ICommand RenameCommand { get; }

        /// <summary>Set by <see cref="MainViewModel"/> when discovery builds the card; without it the command is disabled.</summary>
        public Func<Task>? RenameHandler { get; set; }

        /// <summary>What a screen reader says for the pencil button ("Rename TX2").</summary>
        public string RenameAutomationName => $"Rename {DeviceName}";

        public string RenameToolTip => "Rename";

        public SourceCardViewModel(AvasDevice device)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));

            RenameCommand = new RelayCommand(async _ => await RunRenameAsync(), _ => RenameHandler != null);
            RefreshPreviewCommand = new RelayCommand(async _ => await RefreshPreviewAsync(), _ => IsActive && IsPreviewSupported && !IsRefreshing);
            StartPreviewCommand = new RelayCommand(async _ => await StartManualPreviewAsync(), _ => IsActive && IsPreviewSupported && !IsStreaming && !IsRefreshing);
            StopPreviewCommand = new RelayCommand(async _ => await StopPreviewAsync(), _ => IsStreaming && !IsRefreshing);
        }

        public async Task StartPreviewAsync(IRtpReceiver rtpReceiver, ISdvoeClient sdvoeClient, string multicastIp, int port = 5000)
        {
            if (!IsActive) return;

            _rtpReceiver = rtpReceiver;
            _sdvoeClient = sdvoeClient;

            if (!IsPreviewSupported)
            {
                AppLogger.Info("PREVIEW", $"Preview skipped for non-AVAS device: {DeviceName} (Vendor: {_device.VendorId})");
                return;
            }

            _device.MulticastIp = multicastIp;
            _device.MulticastPort = port;
            OnPropertyChanged(nameof(PreviewMulticastIp));

            try
            {
                // Connect or retrieve demux channel
                DetachChannel();
                _channel = UnifiedRtpDemuxReceiver.Instance.GetOrCreateChannel(_device.IpAddress, multicastIp, _device.NodeIps);
                _channel.BitmapUpdated += OnBitmapUpdated;
                _channel.FpsUpdated += OnFpsUpdated;

                // The channel outlives this card (a refresh rebuilds the cards) and only announces a bitmap when it creates one:
                // pick up the one it already has, or this card stays black.
                if (_channel.Bitmap != null) PreviewBitmap = _channel.Bitmap;

                _channel.StartListening(multicastIp, port);

                bool success = await sdvoeClient.StartAvasPreviewAsync(MacAddress, multicastIp);
                IsStreaming = success;
                AppLogger.Info("PREVIEW", $"Started preview for {DeviceName} on {multicastIp}:{port} (Status: {success})");
            }
            catch (Exception ex)
            {
                AppLogger.Error("PREVIEW", $"Failed to start preview for {DeviceName}", ex);
                IsStreaming = false;
            }
        }

        /// <summary>Stops listening to the RTP channel without touching the device; called when this card is replaced by a rebuilt one.</summary>
        public void Detach() => DetachChannel();

        private void DetachChannel()
        {
            if (_channel == null) return;
            _channel.BitmapUpdated -= OnBitmapUpdated;
            _channel.FpsUpdated -= OnFpsUpdated;
        }

        public async Task StopPreviewAsync()
        {
            if (_sdvoeClient == null) return;

            try
            {
                if (_channel != null)
                {
                    _channel.BitmapUpdated -= OnBitmapUpdated;
                    _channel.FpsUpdated -= OnFpsUpdated;
                    _channel.StopListening();
                }

                await _sdvoeClient.StopAvasPreviewAsync(MacAddress);
                IsStreaming = false;
                Fps = 0.0;
                AppLogger.Info("PREVIEW", $"Stopped preview for {DeviceName}");
            }
            catch (Exception ex)
            {
                AppLogger.Error("PREVIEW", $"Failed to stop preview for {DeviceName}", ex);
            }
        }

        public async Task RefreshPreviewAsync()
        {
            if (IsRefreshing || !IsPreviewSupported) return;

            IsRefreshing = true;
            try
            {
                AppLogger.Info("PREVIEW", $"Refreshing preview stream for {DeviceName}...");
                await StopPreviewAsync();
                await Task.Delay(300);

                if (_rtpReceiver != null && _sdvoeClient != null && !string.IsNullOrEmpty(_device.MulticastIp))
                {
                    await StartPreviewAsync(_rtpReceiver, _sdvoeClient, _device.MulticastIp, _device.MulticastPort);
                }
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        private async Task RunRenameAsync()
        {
            var handler = RenameHandler;
            if (handler == null) return;
            try
            {
                await handler();
            }
            catch (Exception ex)
            {
                AppLogger.Error("RENAME", $"Renaming {DeviceName} failed", ex);
            }
        }

        private async Task StartManualPreviewAsync()
        {
            if (_rtpReceiver != null && _sdvoeClient != null && !string.IsNullOrEmpty(_device.MulticastIp))
            {
                await StartPreviewAsync(_rtpReceiver, _sdvoeClient, _device.MulticastIp, _device.MulticastPort);
            }
        }

        private void OnBitmapUpdated(WriteableBitmap bmp)
        {
            if (PreviewBitmap != bmp)
            {
                PreviewBitmap = bmp;
            }
        }

        private void OnFpsUpdated(double fps)
        {
            Fps = Math.Round(fps, 1);
        }

        public void SetOnlineState(bool isOnline)
        {
            _device.IsActive = isOnline;
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(AutomationName));
            if (!isOnline && IsStreaming)
            {
                _ = StopPreviewAsync();
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
