using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AvasRoutingTool.Rtp;
using AvasRoutingTool.Sdvoe;

namespace AvasRoutingTool.ViewModels
{
    public class SourceCardViewModel : INotifyPropertyChanged
    {
        private readonly AvasDevice _device;
        private WriteableBitmap? _previewBitmap;
        private double _fps;
        private bool _isStreaming;
        private int _frameCount;
        private DateTime _lastFpsUpdate = DateTime.UtcNow;
        private byte[]? _frameBuffer;
        private const int FrameWidth = 320;
        private const int FrameHeight = 180;
        private const int Stride = FrameWidth * 4;

        public event PropertyChangedEventHandler? PropertyChanged;

        public AvasDevice Device => _device;
        public string DeviceName => _device.DeviceName;
        public string IpAddress => _device.IpAddress;
        public string MacAddress => _device.DeviceId;
        public bool IsActive => _device.IsActive;

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

        public WriteableBitmap? PreviewBitmap
        {
            get => _previewBitmap;
            set { _previewBitmap = value; OnPropertyChanged(); }
        }

        public SourceCardViewModel(AvasDevice device)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _frameBuffer = new byte[FrameHeight * Stride];

            // Initialize WriteableBitmap on UI thread if possible
            if (Application.Current != null && Application.Current.Dispatcher != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    PreviewBitmap = new WriteableBitmap(FrameWidth, FrameHeight, 96, 96, PixelFormats.Bgra32, null);
                });
            }
        }

        public async Task StartPreviewAsync(IRtpReceiver rtpReceiver, ISdvoeClient sdvoeClient, string multicastIp, int port = 5004)
        {
            if (IsStreaming || !IsActive) return;

            _device.MulticastIp = multicastIp;
            _device.MulticastPort = port;

            rtpReceiver.RegisterChannel(multicastIp, OnRtpPacketReceived);
            await sdvoeClient.StartStream1ScalerAsync(MacAddress, multicastIp, port);
            IsStreaming = true;
        }

        public async Task StopPreviewAsync(IRtpReceiver rtpReceiver, ISdvoeClient sdvoeClient)
        {
            if (!IsStreaming) return;

            rtpReceiver.UnregisterChannel(_device.MulticastIp);
            await sdvoeClient.StopStream1ScalerAsync(MacAddress);
            IsStreaming = false;
            Fps = 0;
        }

        private void OnRtpPacketReceived(byte[] data, int offset, int length)
        {
            if (_previewBitmap == null || _frameBuffer == null) return;

            // In RFC 4175 or raw YUV422 payload, skip 12-byte RTP header
            int payloadOffset = offset + 12;
            int payloadLength = length - 12;
            if (payloadLength <= 0) return;

            // Convert slice to BGRA
            int copyLen = Math.Min(payloadLength, _frameBuffer.Length);
            Yuv422Rasterizer.ConvertYuv422SliceToBgra(data, payloadOffset, copyLen, _frameBuffer, 0);

            _frameCount++;
            var now = DateTime.UtcNow;
            if ((now - _lastFpsUpdate).TotalSeconds >= 1.0)
            {
                Fps = Math.Round(_frameCount / (now - _lastFpsUpdate).TotalSeconds, 1);
                _frameCount = 0;
                _lastFpsUpdate = now;
            }

            // Update WriteableBitmap on UI thread
            if (Application.Current != null && Application.Current.Dispatcher != null)
            {
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        _previewBitmap.Lock();
                        System.Runtime.InteropServices.Marshal.Copy(_frameBuffer, 0, _previewBitmap.BackBuffer, _frameBuffer.Length);
                        _previewBitmap.AddDirtyRect(new Int32Rect(0, 0, FrameWidth, FrameHeight));
                        _previewBitmap.Unlock();
                    }
                    catch { }
                });
            }
        }

        public void SetOnlineState(bool isOnline)
        {
            _device.IsActive = isOnline;
            OnPropertyChanged(nameof(IsActive));
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
