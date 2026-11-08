using System;
using System.Windows.Media.Imaging;

namespace AvasRoutingTool.Rtsp
{
    public enum RtspFailure { AuthFailed, NotFound, Unreachable, Other }

    public enum RtspPlayerEventKind { Playing, Dropped, Failed }

    /// <summary>What a player reports. Detail is free text from the decoder and may contain a URL: never show it unmasked.</summary>
    public sealed record RtspPlayerEvent(RtspPlayerEventKind Kind, RtspFailure Failure = RtspFailure.Other, string? Detail = null)
    {
        public static readonly RtspPlayerEvent Playing = new(RtspPlayerEventKind.Playing);
        public static readonly RtspPlayerEvent Dropped = new(RtspPlayerEventKind.Dropped);
        public static RtspPlayerEvent Failed(RtspFailure kind, string? detail = null) => new(RtspPlayerEventKind.Failed, kind, detail);
    }

    /// <summary>Decoder seam. Events may be raised on any thread.</summary>
    public interface IRtspStreamPlayer : IDisposable
    {
        event Action<RtspPlayerEvent> Event;
        /// <summary>Raised (any thread) when <see cref="Bitmap"/> is replaced by a new instance (frame size change).</summary>
        event Action BitmapChanged;
        WriteableBitmap? Bitmap { get; }
        void Start(string fullUrl);
        void Stop();
    }
}
