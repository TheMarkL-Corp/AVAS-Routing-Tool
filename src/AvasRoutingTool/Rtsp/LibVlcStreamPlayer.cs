using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LibVLCSharp.Shared;

namespace AvasRoutingTool.Rtsp
{
    /// <summary>
    /// LibVLC adapter for <see cref="IRtspStreamPlayer"/>. Restartable: every Start builds a fresh media and media player.
    /// Frames are decoded to RV32, copied into an unmanaged buffer, then to the shared <see cref="RtspFrameSink"/> on the UI thread.
    /// A failure before the first frame is classified by <see cref="RtspProbe"/> against THIS player's own URL, so several
    /// players can fail independently. Decoder log lines are never stored or written to AppLogger; the masked line reaches only
    /// the test hook <see cref="MaskedLogSink"/>.
    /// </summary>
    public sealed class LibVlcStreamPlayer : IRtspStreamPlayer
    {
        private static readonly object InitGate = new();
        private static LibVLC? _shared;
        private static readonly ConcurrentDictionary<Session, byte> ActiveSessions = new();

        private static LibVLC Shared()
        {
            lock (InitGate)
            {
                if (_shared == null)
                {
                    Core.Initialize();
                    var vlc = new LibVLC("--no-video-title-show");
                    vlc.Log += OnLog;
                    _shared = vlc;
                }
                return _shared;
            }
        }

        private static void OnLog(object? sender, LogEventArgs e)
        {
            string masked;
            try { masked = Scrub(RtspUrl.MaskInText(e.Message ?? "")); }
            catch { return; }
            foreach (var s in ActiveSessions.Keys) s.Classify(masked);
        }

        // The decoder also logs scheme-less forms such as "user:secret@host:554/path"; mask any user:password@ it prints.
        private static readonly System.Text.RegularExpressions.Regex UserInfo =
            new(@"[^\s'`""/\\@:]+:[^\s'`""@]*@", System.Text.RegularExpressions.RegexOptions.Compiled);

        internal static string Scrub(string line) => UserInfo.Replace(line, "***:***@");

        private readonly Dispatcher _dispatcher;
        private readonly RtspFrameSink _sink = new();
        private readonly object _gate = new();
        private Session? _session;
        private bool _disposed;

        public LibVlcStreamPlayer() : this(Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher) { }

        public LibVlcStreamPlayer(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            _sink.BitmapReplaced += () => BitmapChanged?.Invoke();
        }

        public event Action? BitmapChanged;

        public event Action<RtspPlayerEvent>? Event;

        /// <summary>Test hook: receives each decoder log line AFTER masking. Never wire this to a logger.</summary>
        internal Action<string>? MaskedLogSink { get; set; }

        public WriteableBitmap? Bitmap => _sink.Bitmap;

        public void Start(string fullUrl)
        {
            Session session;
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(LibVlcStreamPlayer));
                Retire(_session);
                session = new Session(this);
                _session = session;
            }
            session.Begin(fullUrl);
        }

        public void Stop()
        {
            lock (_gate)
            {
                Retire(_session);
                _session = null;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                Retire(_session);
                _session = null;
            }
        }

        private static void Retire(Session? s) => s?.Shutdown();

        private void Raise(Session s, RtspPlayerEvent e)
        {
            lock (_gate) { if (!ReferenceEquals(_session, s) || _disposed) return; }
            Event?.Invoke(e);
        }

        private sealed class Session
        {
            private readonly LibVlcStreamPlayer _owner;
            private readonly object _gate = new();
            private Media? _media;
            private MediaPlayer? _player;
            private IntPtr _buffer;
            private int _w, _h;
            private volatile bool _closed;
            private bool _played;
            private bool _ended;
            private int _uiPending;
            private RtspUrl? _url;
            private readonly CancellationTokenSource _probeCts = new();

            // delegates kept in fields so the GC cannot collect them while native code holds them
            private MediaPlayer.LibVLCVideoFormatCb? _format;
            private MediaPlayer.LibVLCVideoCleanupCb? _cleanup;
            private MediaPlayer.LibVLCVideoLockCb? _lock;
            private MediaPlayer.LibVLCVideoUnlockCb? _unlock;
            private MediaPlayer.LibVLCVideoDisplayCb? _display;

            public Session(LibVlcStreamPlayer owner) { _owner = owner; }

            public void Begin(string url)
            {
                ActiveSessions[this] = 0;
                RtspUrl.TryParse(url, out var parsed, out _);
                _url = parsed;
                try
                {
                    var vlc = Shared();
                    var media = new Media(vlc, url, FromType.FromLocation);
                    media.AddOption(":rtsp-tcp");
                    media.AddOption(":network-caching=150");
                    var player = new MediaPlayer(media);
                    _format = Format; _cleanup = Cleanup; _lock = Lock; _unlock = Unlock; _display = Display;
                    player.SetVideoFormatCallbacks(_format, _cleanup);
                    player.SetVideoCallbacks(_lock, _unlock, _display);
                    player.EncounteredError += (_, _) => Ended(error: true);
                    player.EndReached += (_, _) => Ended(error: false);
                    lock (_gate)
                    {
                        if (_closed) { media.Dispose(); player.Dispose(); return; }
                        _media = media;
                        _player = player;
                    }
                    if (!player.Play()) Ended(error: true);
                }
                catch (Exception)
                {
                    Ended(error: true);
                }
            }

            public void Classify(string maskedLine)
            {
                if (_closed) return;
                try { _owner.MaskedLogSink?.Invoke(maskedLine); } catch { }
            }

            private async void Ended(bool error)
            {
                bool played;
                lock (_gate)
                {
                    if (_closed || _ended) return;
                    _ended = true;
                    played = _played;
                }
                if (played) { _owner.Raise(this, RtspPlayerEvent.Dropped); return; }
                var kind = RtspFailure.Other;
                try { if (_url != null) kind = await RtspProbe.ClassifyAsync(_url, _probeCts.Token).ConfigureAwait(false); }
                catch { kind = RtspFailure.Other; }
                _owner.Raise(this, RtspPlayerEvent.Failed(kind));
            }

            public void Shutdown()
            {
                MediaPlayer? p; Media? m;
                lock (_gate)
                {
                    if (_closed) return;
                    _closed = true;
                    try { _probeCts.Cancel(); } catch { }
                    p = _player; m = _media;
                    _player = null; _media = null;
                }
                ActiveSessions.TryRemove(this, out _);
                // Stop/Dispose can block and must not run inside a LibVLC callback or under a lock.
                Task.Run(() =>
                {
                    try { p?.Stop(); } catch { }
                    try { p?.Dispose(); } catch { }
                    try { m?.Dispose(); } catch { }
                });
            }

            // ---- video callbacks (decoder thread) ----

            private uint Format(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
            {
                Marshal.Copy(new byte[] { (byte)'R', (byte)'V', (byte)'3', (byte)'2' }, 0, chroma, 4);
                lock (_gate)
                {
                    FreeBuffer();
                    _w = (int)width; _h = (int)height;
                    _buffer = Marshal.AllocHGlobal(_w * _h * 4);
                }
                pitches = (uint)(_w * 4);
                lines = (uint)_h;
                return 1;
            }

            private void Cleanup(ref IntPtr opaque)
            {
                lock (_gate) FreeBuffer();
            }

            private void FreeBuffer()
            {
                if (_buffer != IntPtr.Zero) { Marshal.FreeHGlobal(_buffer); _buffer = IntPtr.Zero; }
            }

            private IntPtr Lock(IntPtr opaque, IntPtr planes)
            {
                Marshal.WriteIntPtr(planes, _buffer);
                return IntPtr.Zero;
            }

            private void Unlock(IntPtr opaque, IntPtr picture, IntPtr planes) { }

            private void Display(IntPtr opaque, IntPtr picture)
            {
                if (_closed || Interlocked.Exchange(ref _uiPending, 1) == 1) return;   // drop frames while the UI is behind
                try { _owner._dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(PresentOnUi)); }
                catch { Interlocked.Exchange(ref _uiPending, 0); }
            }

            private void PresentOnUi()
            {
                Interlocked.Exchange(ref _uiPending, 0);
                if (_closed) return;
                bool shown;
                lock (_gate)
                {
                    shown = _buffer != IntPtr.Zero && _owner._sink.Present(_w, _h, _buffer, _w * 4);
                }
                if (!shown) return;
                bool first;
                lock (_gate) { first = !_played; _played = true; }
                if (first) _owner.Raise(this, RtspPlayerEvent.Playing);
            }
        }
    }
}


