using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using AvasRoutingTool.Rtsp;

namespace AvasRoutingTool.Tests.Fakes
{
    public sealed class FakeRtspPlayer : IRtspStreamPlayer
    {
        public event Action<RtspPlayerEvent>? Event;
        public event Action? BitmapChanged;
        public WriteableBitmap? Bitmap { get; set; }
        public List<string> Started { get; } = new();
        public int StopCount { get; private set; }
        public bool Disposed { get; private set; }

        public Action<string>? OnStart { get; set; }
        public void Start(string fullUrl) { Started.Add(fullUrl); OnStart?.Invoke(fullUrl); }
        public void Stop() => StopCount++;
        public void Dispose() { Disposed = true; }

        public void Raise(RtspPlayerEvent e) => Event?.Invoke(e);
        public void SwapBitmap(WriteableBitmap bitmap) { Bitmap = bitmap; BitmapChanged?.Invoke(); }
    }
}
