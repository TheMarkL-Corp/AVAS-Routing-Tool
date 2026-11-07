using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using AvasRoutingTool.Rtsp;

namespace AvasRoutingTool.ViewModels
{
    public enum RtspTileState { Stopped, Connecting, Live, Lost, Error }

    /// <summary>
    /// State machine of one RTSP view-only stream. Exposes only the masked address and fixed status texts;
    /// the full URL goes to the player and nowhere else. Player events are marshalled to the context captured
    /// at construction (inline when there is none, as in tests); all state changes then happen on that one context.
    /// </summary>
    public sealed class RtspSourceViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly RtspUrl _url;
        private readonly Func<IRtspStreamPlayer> _playerFactory;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly SynchronizationContext? _uiContext;
        private IRtspStreamPlayer? _player;
        private CancellationTokenSource? _retryCts;
        private RtspTileState _state = RtspTileState.Stopped;
        private string _statusText = "Stopped";
        private int _attempt;
        private bool _disposed;

        public RtspSourceViewModel(RtspUrl url, Func<IRtspStreamPlayer> playerFactory, Func<TimeSpan, CancellationToken, Task> delay)
        {
            _url = url ?? throw new ArgumentNullException(nameof(url));
            _playerFactory = playerFactory ?? throw new ArgumentNullException(nameof(playerFactory));
            _delay = delay ?? throw new ArgumentNullException(nameof(delay));
            _uiContext = SynchronizationContext.Current;
            MaskedAddress = url.ToMasked();

            StartCommand = new RelayCommand(_ => Start(), _ => !_disposed && _state == RtspTileState.Stopped);
            StopCommand = new RelayCommand(_ => Stop(), _ => !_disposed && _state != RtspTileState.Stopped);
            RetryCommand = new RelayCommand(_ => Retry(), _ => !_disposed && (_state == RtspTileState.Error || _state == RtspTileState.Lost));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string MaskedAddress { get; }
        public RelayCommand StartCommand { get; }
        public RelayCommand StopCommand { get; }
        public RelayCommand RetryCommand { get; }

        public RtspTileState State => _state;
        public string StatusText => _statusText;
        public bool IsLive => _state == RtspTileState.Live;
        public WriteableBitmap? PreviewBitmap => _player?.Bitmap;

        private void Start()
        {
            if (_disposed || _state != RtspTileState.Stopped)
                return;
            _attempt = 0;
            BeginAttempt();
        }

        private void Retry()
        {
            if (_disposed || (_state != RtspTileState.Error && _state != RtspTileState.Lost))
                return;
            CancelRetry();
            _attempt = 0;
            BeginAttempt();
        }

        /// <summary>Halts everything: pending retry cancelled, player stopped. Events that arrive later are ignored.</summary>
        public void Stop()
        {
            if (_disposed)
                return;
            CancelRetry();
            SetState(RtspTileState.Stopped, "Stopped");
            StopPlayerQuietly();
            RaisePreview();
        }

        private void BeginAttempt()
        {
            SetState(RtspTileState.Connecting, "Connecting...");
            try
            {
                if (_player == null)
                {
                    _player = _playerFactory();
                    _player.Event += OnPlayerEvent;
                    _player.BitmapChanged += OnBitmapChanged;
                }
                _player.Start(_url.ToFullUrl());
            }
            catch (Exception)
            {
                // Exception text can carry the URL: classify, never show it.
                Fail(RtspFailure.Other);
            }
            RaisePreview();
        }

        private void OnPlayerEvent(RtspPlayerEvent e) => RunOnUi(() => Handle(e));

        private void OnBitmapChanged() => RunOnUi(RaisePreview);

        private void Handle(RtspPlayerEvent e)
        {
            // Stopped = user halted (late events must not revive the tile); Error = terminal until Retry.
            if (_disposed || _state == RtspTileState.Stopped || _state == RtspTileState.Error)
                return;

            switch (e.Kind)
            {
                case RtspPlayerEventKind.Playing:
                    _attempt = 0;
                    SetState(RtspTileState.Live, "Live");
                    RaisePreview();
                    break;
                case RtspPlayerEventKind.Dropped:
                    ScheduleRetry();
                    break;
                case RtspPlayerEventKind.Failed:
                    if (e.Failure == RtspFailure.Unreachable)
                        ScheduleRetry();
                    else
                        Fail(e.Failure);
                    break;
            }
        }

        private void Fail(RtspFailure kind)
        {
            CancelRetry();
            StopPlayerQuietly();
            SetState(RtspTileState.Error, "Cannot open stream: " + Sentence(kind));
            RaisePreview();
        }

        private static string Sentence(RtspFailure kind) => kind switch
        {
            RtspFailure.AuthFailed => "the user name or password was rejected.",
            RtspFailure.NotFound => "the camera has no stream at this address.",
            RtspFailure.Unreachable => "the camera cannot be reached.",
            _ => "the stream could not be opened.",
        };

        private void ScheduleRetry()
        {
            CancelRetry();
            _attempt++;
            var wait = RtspRetryPolicy.NextDelay(_attempt);
            var seconds = (int)Math.Ceiling(wait.TotalSeconds);
            SetState(RtspTileState.Lost, "Connection lost - retrying in " + seconds + " s");
            StopPlayerQuietly();
            var cts = new CancellationTokenSource();
            _retryCts = cts;
            _ = RetryAfterAsync(wait, cts);
        }

        private async Task RetryAfterAsync(TimeSpan wait, CancellationTokenSource cts)
        {
            var token = cts.Token;
            try
            {
                await _delay(wait, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                return;
            }
            if (token.IsCancellationRequested)
                return;
            RunOnUi(() =>
            {
                if (_disposed || token.IsCancellationRequested || !ReferenceEquals(_retryCts, cts) || _state != RtspTileState.Lost)
                    return;
                _retryCts = null;
                BeginAttempt();
            });
        }

        private void CancelRetry()
        {
            var cts = _retryCts;
            _retryCts = null;
            if (cts == null)
                return;
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
        }

        private void StopPlayerQuietly()
        {
            try { _player?.Stop(); } catch (Exception) { }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            CancelRetry();
            StopPlayerQuietly();
            _disposed = true;
            var p = _player;
            _player = null;
            if (p != null)
            {
                p.Event -= OnPlayerEvent;
                p.BitmapChanged -= OnBitmapChanged;
                try { p.Dispose(); } catch (Exception) { }
            }
            _state = RtspTileState.Stopped;
            _statusText = "Stopped";
        }

        private void SetState(RtspTileState state, string text)
        {
            var stateChanged = _state != state;
            var textChanged = _statusText != text;
            _state = state;
            _statusText = text;
            if (stateChanged) { OnPropertyChanged(nameof(State)); OnPropertyChanged(nameof(IsLive)); }
            if (textChanged) OnPropertyChanged(nameof(StatusText));
            if (stateChanged) CommandManagerRequery();
        }

        private static void CommandManagerRequery()
        {
            try { System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } catch (Exception) { }
        }

        private void RaisePreview() => OnPropertyChanged(nameof(PreviewBitmap));

        private void RunOnUi(Action action)
        {
            if (_disposed)
                return;
            if (_uiContext == null || SynchronizationContext.Current == _uiContext)
                action();
            else
                _uiContext.Post(_ => { if (!_disposed) action(); }, null);
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
