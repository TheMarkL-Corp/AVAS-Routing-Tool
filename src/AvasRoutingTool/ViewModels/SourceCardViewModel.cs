using System;
using System.ComponentModel;
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
        public bool IsDualLink => string.Equals(_device.LinkMode, "Dual Link", StringComparison.OrdinalIgnoreCase);
        public bool IsPreviewSupported => _device.IsPreviewSupported;
        public string PreviewMulticastIp => !string.IsNullOrEmpty(_device.MulticastIp)
            ? $"{_device.MulticastIp}:{_device.MulticastPort}"
            : "N/A";

        public double Fps
        {
            get => _fps;
            set { _fps = value; OnPropertyChanged(); }
        }

        public bool IsStreaming
        {
            get => _isStreaming;
            set { _isStreaming = value; OnPropertyChanged(); }
        }

        public bool IsRefreshing
        {
            get => _isRefreshing;
            set { _isRefreshing = value; OnPropertyChanged(); }
        }

        public WriteableBitmap? PreviewBitmap
        {
            get => _previewBitmap;
            set { _previewBitmap = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Tapped to place: the next tap on a display routes it (Single View), inside the Composer it fills blocks.
        /// At most one source is armed at a time; set by <see cref="MainViewModel"/>.
        /// </summary>
        public bool IsArmed
        {
            get => _isArmed;
            set { if (_isArmed == value) return; _isArmed = value; OnPropertyChanged(); }
        }

        /// <summary>How many blocks of the Composer's staged picture show this encoder ("in 2 blocks"); 0 outside the Composer.</summary>
        public int UsedInBlocks
        {
            get => _usedInBlocks;
            set { if (_usedInBlocks == value) return; _usedInBlocks = value; OnPropertyChanged(); }
        }

        public ICommand RefreshPreviewCommand { get; }
        public ICommand StartPreviewCommand { get; }
        public ICommand StopPreviewCommand { get; }

        public SourceCardViewModel(AvasDevice device)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));

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
                _channel = UnifiedRtpDemuxReceiver.Instance.GetOrCreateChannel(_device.IpAddress, multicastIp, _device.NodeIps);
                _channel.BitmapUpdated += OnBitmapUpdated;
                _channel.FpsUpdated += OnFpsUpdated;

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
