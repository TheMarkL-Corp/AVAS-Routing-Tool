using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Rtsp;
using AvasRoutingTool.Services;
using AvasRoutingTool.Services.Visca;

namespace AvasRoutingTool.ViewModels
{
    /// <summary>Speed step for pan, tilt and zoom (R12).</summary>
    public enum SpeedStep { Slow, Medium, Fast }

    /// <summary>What the picture area of a camera shows (plan, "Preview states").</summary>
    public enum CameraPreviewState { NoSourceBound, SourceOffline, Standby, Starting, Live, Reconnecting, Failed }

    /// <summary>
    /// One room camera as the Room Cameras tab shows it: link and power state, motion commands,
    /// zoom and focus sliders, power with inline confirmation, and the Lock switch (R13).
    /// Does not own the camera: the owner (RoomCamerasViewModel) disposes it.
    /// Camera events and poll results are marshalled to the context captured at construction.
    /// </summary>
    public class RoomCameraViewModel : INotifyPropertyChanged, IDisposable
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan PowerOffPromptLimit = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan SendInterval = TimeSpan.FromMilliseconds(200);   // at most 5 per second

        private readonly Func<DateTime> _now;
        private readonly SynchronizationContext? _uiContext;
        private readonly AbsoluteSlider _zoom;
        private readonly AbsoluteSlider _focus;
        private CameraPower _power = CameraPower.Unknown;
        private bool _autoFocus;
        private bool _isLocked;
        private bool _powerOffPending;
        private DateTime _powerOffAt;
        private DateTime _lastPollAt = DateTime.MinValue;
        private bool _pollInFlight;
        private SpeedStep _speedStep = SpeedStep.Medium;
        private bool _disposed;

        // Preview binding. The card is re-resolved by RoomCamerasViewModel on every change of the sources
        // collection; _card is never trusted across a rebuild (a Refresh replaces every card).
        private SourceCardViewModel? _card;
        private string _lastSourceName = string.Empty;
        private bool _startedByTab;
        private bool _startRequested;

        // RTSP preview (per-camera choice): created on Start, disposed on Stop and Dispose.
        private readonly Func<IRtspStreamPlayer> _rtspPlayerFactory;
        private readonly Func<TimeSpan, CancellationToken, Task> _rtspDelay;
        private RtspSourceViewModel? _rtsp;

        public RoomCameraViewModel(
            RoomCameraConfig config,
            IPtzCamera camera,
            Func<DateTime> now,
            Func<IRtspStreamPlayer>? rtspPlayerFactory = null,
            Func<TimeSpan, CancellationToken, Task>? rtspDelay = null)
        {
            _rtspPlayerFactory = rtspPlayerFactory ?? (() => new LibVlcStreamPlayer());
            _rtspDelay = rtspDelay ?? ((t, ct) => Task.Delay(t, ct));
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Camera = camera ?? throw new ArgumentNullException(nameof(camera));
            _now = now ?? throw new ArgumentNullException(nameof(now));
            _uiContext = SynchronizationContext.Current;

            Motion = new PtzMotionController(camera, now);
            Motion.Speed = SpeedFor(_speedStep);
            Motion.Changed += OnMotionChanged;

            _zoom = new AbsoluteSlider(config.ZoomMax, v => Camera.SetZoomPositionAsync(v, CancellationToken.None));
            _focus = new AbsoluteSlider(config.FocusMax, v => Camera.SetFocusPositionAsync(v, CancellationToken.None));
            Camera.LinkStateChanged += OnLinkStateChanged;

            PowerCommand = new RelayCommand(_ => OnPowerCommand(), _ => PowerEnabled);
            ConfirmPowerOffCommand = new RelayCommand(_ => ConfirmPowerOff(), _ => IsPowerOffPending && !IsLocked);
            CancelPowerOffCommand = new RelayCommand(_ => CancelPowerOff(), _ => IsPowerOffPending);
            HomeCommand = new RelayCommand(_ => Fire(() => Camera.HomeAsync(CancellationToken.None)), _ => MotionEnabled);
            MoveStartCommand = new RelayCommand(
                p => { if (p is PanTiltDirection d) BeginMove(d); },
                p => p is PanTiltDirection && MotionEnabled);
            MoveEndCommand = new RelayCommand(_ => EndMove());
            ZoomStartCommand = new RelayCommand(
                p => { if (p is ZoomDirection d) BeginZoom(d); },
                p => p is ZoomDirection && MotionEnabled);
            ZoomEndCommand = new RelayCommand(_ => EndZoom());

            StartPreviewCommand = new RelayCommand(_ => StartPreview(), _ => CanStartPreview);
            StopPreviewCommand = new RelayCommand(_ => StopPreview(), _ => ShowStopPreview);
            RetryPreviewCommand = new RelayCommand(_ => RetryPreview(), _ => CanRetryPreview);
        }

        /// <summary>Seam: how the card's start command is reached. Tests replace it; the default is the card's own command.</summary>
        internal Func<SourceCardViewModel, ICommand> StartCommandOf { get; set; } = c => c.StartPreviewCommand;

        /// <summary>Seam: how the card's stop command is reached. Tests replace it; the default is the card's own command.</summary>
        internal Func<SourceCardViewModel, ICommand> StopCommandOf { get; set; } = c => c.StopPreviewCommand;

        public RelayCommand StartPreviewCommand { get; }
        public RelayCommand StopPreviewCommand { get; }
        /// <summary>Retry the RTSP stream; enabled only while it is Failed or Reconnecting.</summary>
        public RelayCommand RetryPreviewCommand { get; }

        public RoomCameraPreviewMode PreviewMode => Config.PreviewMode;
        public bool IsRtsp => Config.PreviewMode == RoomCameraPreviewMode.Rtsp;

        public event PropertyChangedEventHandler? PropertyChanged;

        public RoomCameraConfig Config { get; }
        public IPtzCamera Camera { get; }
        public PtzMotionController Motion { get; }

        public string Id => Config.Id;
        public string Name => Config.Name;
        public string Endpoint => $"{Config.Host}:{Config.Port}";

        public int ZoomMaximum => Config.ZoomMax;
        public int FocusMaximum => Config.FocusMax;

        public RelayCommand PowerCommand { get; }
        public RelayCommand ConfirmPowerOffCommand { get; }
        public RelayCommand CancelPowerOffCommand { get; }
        public RelayCommand HomeCommand { get; }
        /// <summary>Press a pan/tilt key (parameter: <see cref="PanTiltDirection"/>).</summary>
        public RelayCommand MoveStartCommand { get; }
        /// <summary>Release a pan/tilt key. Always enabled: Stop is never blocked.</summary>
        public RelayCommand MoveEndCommand { get; }
        /// <summary>Press a zoom key (parameter: <see cref="ZoomDirection"/>).</summary>
        public RelayCommand ZoomStartCommand { get; }
        /// <summary>Release a zoom key. Always enabled: Stop is never blocked.</summary>
        public RelayCommand ZoomEndCommand { get; }

        public string LinkText => Camera.LinkState switch
        {
            CameraLinkState.Connected => "Connected",
            CameraLinkState.Connecting => "Connecting...",
            CameraLinkState.Unreachable => $"Cannot reach {Endpoint}",
            _ => "Not connected - retrying",
        };

        public bool LinkIsGood => Camera.LinkState == CameraLinkState.Connected;

        /// <summary>True once the camera is unreachable (3 failed attempts, R16): the link dots turn red.</summary>
        public bool LinkIsUnreachable => Camera.LinkState == CameraLinkState.Unreachable;

        public string PowerText => _power switch
        {
            CameraPower.On => "Camera on",
            CameraPower.Off => "Camera off",
            CameraPower.Fault => "Camera fault",
            _ => "Power unknown",
        };

        public string PowerOffPrompt => $"Turn off {Name}?";

        public bool IsPowerOffPending
        {
            get => _powerOffPending;
            private set
            {
                if (_powerOffPending == value)
                    return;
                _powerOffPending = value;
                OnPropertyChanged();
            }
        }

        public bool IsLocked
        {
            get => _isLocked;
            set
            {
                if (_isLocked == value)
                    return;
                _isLocked = value;
                if (value)
                {
                    Motion.StopAll();
                    _zoom.CancelPending();
                    _focus.CancelPending();
                    IsPowerOffPending = false;
                }
                RaiseLockDependents();
            }
        }

        public bool MotionEnabled => !IsLocked;
        public bool ZoomEnabled => !IsLocked;
        public bool AutoFocusEnabled => !IsLocked;
        public bool PowerEnabled => !IsLocked;

        /// <summary>Focus slider is usable only when auto focus is off and the camera is not locked.</summary>
        public bool FocusEnabled => !_autoFocus && !IsLocked;

        public SpeedStep SpeedStep
        {
            get => _speedStep;
            set
            {
                if (_speedStep == value)
                    return;
                _speedStep = value;
                Motion.Speed = SpeedFor(value);
                OnPropertyChanged();
            }
        }

        public string MotionText => Motion.MovingText;

        public MotionState MotionState => Motion.State;

        /// <summary>Zoom position in camera units, 0 to <see cref="ZoomMaximum"/>. User changes are sent throttled.</summary>
        public int ZoomValue
        {
            get => _zoom.Value;
            set
            {
                if (!ZoomEnabled)
                    return;
                if (_zoom.SetFromUser(value, _now()))
                    OnPropertyChanged();
            }
        }

        /// <summary>Focus position in camera units, 0 to <see cref="FocusMaximum"/>. User changes are sent throttled.</summary>
        public int FocusValue
        {
            get => _focus.Value;
            set
            {
                if (!FocusEnabled)
                    return;
                if (_focus.SetFromUser(value, _now()))
                    OnPropertyChanged();
            }
        }

        public bool AutoFocus
        {
            get => _autoFocus;
            set
            {
                if (IsLocked || _autoFocus == value)
                    return;
                _autoFocus = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FocusEnabled));
                Fire(() => Camera.SetFocusAutoAsync(value, CancellationToken.None));
            }
        }

        /// <summary>Screen reader text: name, link state and power state, always all three (R16, plan Review Focus 5).</summary>
        public string AutomationName
        {
            get
            {
                var link = Camera.LinkState switch
                {
                    CameraLinkState.Connected => "connected",
                    CameraLinkState.Connecting => "connecting",
                    CameraLinkState.Unreachable => "unreachable",
                    _ => "not connected",
                };
                var name = $"{Name}, {link}, {PowerText.ToLowerInvariant()}";
                return IsLocked ? $"{name}, locked" : name;
            }
        }

        public void BeginZoomDrag() => _zoom.Dragging = true;

        /// <summary>Ends a zoom drag and sends the final value at once (unless locked).</summary>
        public void EndZoomDrag() => _zoom.EndDrag(_now(), send: !IsLocked);

        public void BeginFocusDrag() => _focus.Dragging = true;

        /// <summary>Ends a focus drag and sends the final value at once (unless locked).</summary>
        public void EndFocusDrag() => _focus.EndDrag(_now(), send: !IsLocked);

        /// <summary>Starts a pan/tilt move. Ignored while locked.</summary>
        public void BeginMove(PanTiltDirection d)
        {
            if (_disposed || IsLocked)
                return;
            Motion.BeginMove(d);
        }

        /// <summary>Ends pan/tilt or zoom motion (Stop). Always allowed, even while locked.</summary>
        public void EndMove()
        {
            if (!_disposed)
                Motion.End();
        }

        /// <summary>Starts a zoom move. Ignored while locked.</summary>
        public void BeginZoom(ZoomDirection d)
        {
            if (_disposed || IsLocked)
                return;
            Motion.BeginZoom(d);
        }

        /// <summary>Ends zoom motion (Stop). Always allowed, even while locked.</summary>
        public void EndZoom()
        {
            if (!_disposed)
                Motion.End();
        }

        public void StopAll()
        {
            if (!_disposed)
                Motion.StopAll();
        }

        /// <summary>
        /// Applies a status read from the camera. Updates the controls without sending anything;
        /// zoom and focus are left alone while the operator is dragging them.
        /// </summary>
        public void ApplyStatus(CameraStatus status)
        {
            if (_disposed)
                return;

            if (_power != status.Power)
            {
                _power = status.Power;
                OnPropertyChanged(nameof(PowerText));
                OnPropertyChanged(nameof(AutomationName));
            }

            if (_autoFocus != status.AutoFocus)
            {
                _autoFocus = status.AutoFocus;
                OnPropertyChanged(nameof(AutoFocus));
                OnPropertyChanged(nameof(FocusEnabled));
            }

            if (_zoom.SetFromReadback(status.ZoomPosition))
                OnPropertyChanged(nameof(ZoomValue));
            if (_focus.SetFromReadback(status.FocusPosition))
                OnPropertyChanged(nameof(FocusValue));
        }

        /// <summary>Drives the watchdog, the throttled slider sends, the power prompt timeout and the 5 s power poll.</summary>
        public void Tick()
        {
            if (_disposed)
                return;

            Camera.Tick();
            Motion.Tick();

            var now = _now();
            if (!IsLocked)
            {
                _zoom.Flush(now);
                _focus.Flush(now);
            }

            if (IsPowerOffPending && now - _powerOffAt >= PowerOffPromptLimit)
                IsPowerOffPending = false;

            if (!_pollInFlight && now - _lastPollAt >= PollInterval)
                StartPoll(now);
        }

        // ---- Preview (R1, R5) ----

        public CameraPreviewState Preview
        {
            get
            {
                if (IsRtsp)
                    return RtspPreview;
                if (_card == null)
                    return string.IsNullOrEmpty(Config.BoundTxMac) ? CameraPreviewState.NoSourceBound : CameraPreviewState.SourceOffline;
                if (!_card.IsActive)
                    return CameraPreviewState.SourceOffline;
                if (_card.IsStreaming)
                    return CameraPreviewState.Live;
                if (_card.IsRefreshing || _startRequested)
                    return CameraPreviewState.Starting;
                return CameraPreviewState.Standby;
            }
        }

        private bool TryParseRtsp(out RtspUrl url, out string reason)
        {
            if (string.IsNullOrWhiteSpace(Config.RtspUrl))
            {
                url = null!;
                reason = "No RTSP address";
                return false;
            }
            return RtspUrl.TryParse(Config.RtspUrl, out url, out reason);
        }

        private CameraPreviewState RtspPreview
        {
            get
            {
                if (_rtsp == null)
                    return TryParseRtsp(out _, out _) ? CameraPreviewState.Standby : CameraPreviewState.NoSourceBound;
                return _rtsp.State switch
                {
                    RtspTileState.Connecting => CameraPreviewState.Starting,
                    RtspTileState.Live => CameraPreviewState.Live,
                    RtspTileState.Lost => CameraPreviewState.Reconnecting,
                    RtspTileState.Error => CameraPreviewState.Failed,
                    _ => CameraPreviewState.Standby,
                };
            }
        }

        public string SourceName => IsRtsp ? string.Empty : _card?.DeviceName ?? _lastSourceName;

        public WriteableBitmap? PreviewBitmap =>
            Preview == CameraPreviewState.Live ? (IsRtsp ? _rtsp?.PreviewBitmap : _card?.PreviewBitmap) : null;

        public string PreviewMessage => Preview switch
        {
            CameraPreviewState.NoSourceBound when IsRtsp => TryParseRtsp(out _, out var reason) ? string.Empty : reason,
            CameraPreviewState.Reconnecting or CameraPreviewState.Failed => _rtsp?.StatusText ?? string.Empty,
            CameraPreviewState.NoSourceBound => $"No source bound to {Name}",
            CameraPreviewState.SourceOffline => $"{SourceName} is offline",
            CameraPreviewState.Standby => "Preview standby",
            CameraPreviewState.Starting => "Starting preview...",
            _ => string.Empty,
        };

        public string CaptionText
        {
            get
            {
                var text = Name;
                if (IsRtsp)
                    return TryParseRtsp(out var url, out _) ? text + " \u00B7 via RTSP " + url.ToMasked() : text;
                if (!string.IsNullOrEmpty(SourceName) && Preview != CameraPreviewState.NoSourceBound)
                    text += $" \u00B7 via {SourceName}";
                if (Preview == CameraPreviewState.Live && _card != null)
                    text += string.Format(System.Globalization.CultureInfo.InvariantCulture, " \u00B7 {0:F1} fps", _card.Fps);
                return text;
            }
        }

        /// <summary>Why Start is unavailable even in standby; null when it is available.</summary>
        public string? StartDisabledReason
        {
            get
            {
                if (IsRtsp)
                    return TryParseRtsp(out _, out var reason) ? null : reason;
                if (_card == null)
                    return null;
                if (!_card.IsPreviewSupported)
                    return "Preview not supported";
                if (_card.PreviewMulticastIp == "N/A")
                    return "No multicast address";
                return null;
            }
        }

        public bool CanStartPreview => IsRtsp
            ? !_disposed && Preview == CameraPreviewState.Standby && StartDisabledReason == null
            : !_disposed && _card != null && Preview == CameraPreviewState.Standby
            && StartDisabledReason == null && StartCommandOf(_card).CanExecute(null);

        /// <summary>"Stop preview" is offered only for a stream this tab started.</summary>
        public bool ShowStopPreview => IsRtsp
            ? !_disposed && _rtsp != null
            : !_disposed && _startedByTab && Preview == CameraPreviewState.Live;

        private bool CanRetryPreview => !_disposed && IsRtsp
            && (Preview == CameraPreviewState.Failed || Preview == CameraPreviewState.Reconnecting);

        /// <summary>Starts the bound card's preview. Only in standby; never touches a stream that is already running.</summary>
        public void StartPreview()
        {
            if (IsRtsp)
            {
                StartRtsp();
                return;
            }
            if (!CanStartPreview || _card == null)
                return;
            _startedByTab = true;
            _startRequested = true;
            StartCommandOf(_card).Execute(null);
            SettleFlags();
            RaisePreview();
        }

        /// <summary>Stops the preview, but only if this tab started it.</summary>
        public void StopPreview()
        {
            if (IsRtsp)
            {
                StopRtsp();
                return;
            }
            if (!_startedByTab)
                return;
            _startedByTab = false;
            _startRequested = false;
            if (_card != null)
            {
                var stop = StopCommandOf(_card);
                if (stop.CanExecute(null))
                    stop.Execute(null);
            }
            RaisePreview();
        }

        /// <summary>
        /// Points the binding at the card now registered for the bound MAC (null when none). Called on every
        /// change of the sources collection; unsubscribes from the previous card first.
        /// </summary>
        internal void ResolveSource(SourceCardViewModel? card)
        {
            if (_disposed)
                return;
            if (!ReferenceEquals(_card, card))
            {
                if (_card != null)
                    _card.PropertyChanged -= OnCardPropertyChanged;
                _card = card;
                if (card != null)
                {
                    card.PropertyChanged += OnCardPropertyChanged;
                    _lastSourceName = card.DeviceName;
                }
            }
            SettleFlags();
            RaisePreview();
        }

        private void StartRtsp()
        {
            if (!CanStartPreview || !TryParseRtsp(out var url, out _))
                return;
            var source = new RtspSourceViewModel(url, _rtspPlayerFactory, _rtspDelay);
            source.PropertyChanged += OnRtspPropertyChanged;
            _rtsp = source;
            source.StartCommand.Execute(null);
            RaisePreview();
        }

        private void StopRtsp()
        {
            var source = _rtsp;
            if (source == null)
                return;
            _rtsp = null;
            source.PropertyChanged -= OnRtspPropertyChanged;
            source.Stop();
            source.Dispose();
            RaisePreview();
        }

        private void RetryPreview()
        {
            if (CanRetryPreview)
                _rtsp?.RetryCommand.Execute(null);
        }

        private void OnRtspPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_disposed || !ReferenceEquals(sender, _rtsp))
                return;
            RaisePreview();
        }

        private void OnCardPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_disposed || !ReferenceEquals(sender, _card))
                return;
            if (e.PropertyName == nameof(SourceCardViewModel.IsStreaming) && _card != null
                && !_card.IsStreaming && !_card.IsRefreshing)
            {
                // The stream ended or the start failed: nothing is running, so nothing is ours to stop.
                _startRequested = false;
                _startedByTab = false;
            }
            SettleFlags();
            RaisePreview();
        }

        private void SettleFlags()
        {
            if (_card != null && _card.IsStreaming)
                _startRequested = false;
            // A rebuilt card in standby with no start pending carries no stream of ours.
            if (_card != null && _card.IsActive && !_card.IsStreaming && !_card.IsRefreshing && !_startRequested)
                _startedByTab = false;
        }

        private void RaisePreview()
        {
            OnPropertyChanged(nameof(Preview));
            OnPropertyChanged(nameof(SourceName));
            OnPropertyChanged(nameof(PreviewBitmap));
            OnPropertyChanged(nameof(PreviewMessage));
            OnPropertyChanged(nameof(CaptionText));
            OnPropertyChanged(nameof(StartDisabledReason));
            OnPropertyChanged(nameof(CanStartPreview));
            OnPropertyChanged(nameof(ShowStopPreview));
            OnPropertyChanged(nameof(IsRtsp));
            OnPropertyChanged(nameof(PreviewMode));
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            StopPreview();   // TX stream this tab started, or the RTSP source
            _disposed = true;

            if (_card != null)
                _card.PropertyChanged -= OnCardPropertyChanged;
            _card = null;
            Camera.LinkStateChanged -= OnLinkStateChanged;
            Motion.Changed -= OnMotionChanged;
            Motion.Dispose();
        }

        private void OnPowerCommand()
        {
            if (!PowerEnabled)
                return;
            if (_power == CameraPower.On)
            {
                _powerOffAt = _now();
                IsPowerOffPending = true;
                return;
            }
            Fire(() => Camera.SetPowerAsync(true, CancellationToken.None));
        }

        private void ConfirmPowerOff()
        {
            if (!IsPowerOffPending || IsLocked)
                return;
            IsPowerOffPending = false;
            Fire(() => Camera.SetPowerAsync(false, CancellationToken.None));
        }

        private void CancelPowerOff() => IsPowerOffPending = false;

        private void OnLinkStateChanged(CameraLinkState state) => RunOnUi(() =>
        {
            OnPropertyChanged(nameof(LinkText));
            OnPropertyChanged(nameof(LinkIsGood));
            OnPropertyChanged(nameof(LinkIsUnreachable));
            OnPropertyChanged(nameof(AutomationName));
            // A dropped link cannot be left moving: end any motion now (Stop is retried by the controller).
            if (state != CameraLinkState.Connected)
                Motion.End();
        });

        private void OnMotionChanged() => RunOnUi(() =>
        {
            OnPropertyChanged(nameof(MotionText));
            OnPropertyChanged(nameof(MotionState));
        });

        private void StartPoll(DateTime now)
        {
            _lastPollAt = now;
            _pollInFlight = true;

            Task<CameraStatus?> task;
            try
            {
                task = Camera.PollAsync(CancellationToken.None);
            }
            catch (Exception)
            {
                _pollInFlight = false;
                return;
            }

            if (task.IsCompleted)
            {
                CompletePoll(task);
                return;
            }
            task.ContinueWith(t => RunOnUi(() => CompletePoll(t)), TaskContinuationOptions.ExecuteSynchronously);
        }

        private void CompletePoll(Task<CameraStatus?> task)
        {
            _pollInFlight = false;
            if (task.IsFaulted)
            {
                _ = task.Exception;   // mark observed
                return;
            }
            if (task.IsCanceled)
                return;
            var status = task.Result;
            if (status != null)
                ApplyStatus(status);
        }

        private void RaiseLockDependents()
        {
            OnPropertyChanged(nameof(IsLocked));
            OnPropertyChanged(nameof(MotionEnabled));
            OnPropertyChanged(nameof(ZoomEnabled));
            OnPropertyChanged(nameof(FocusEnabled));
            OnPropertyChanged(nameof(AutoFocusEnabled));
            OnPropertyChanged(nameof(PowerEnabled));
            OnPropertyChanged(nameof(AutomationName));
        }

        /// <summary>Runs on the captured UI context when one exists, else inline.</summary>
        private void RunOnUi(Action action)
        {
            if (_disposed)
                return;
            if (_uiContext == null || SynchronizationContext.Current == _uiContext)
                action();
            else
                _uiContext.Post(_ => { if (!_disposed) action(); }, null);
        }

        private static PtzSpeed SpeedFor(SpeedStep step) => step switch
        {
            SpeedStep.Slow => new PtzSpeed(0x05, 0x04, 2),
            SpeedStep.Medium => new PtzSpeed(0x0C, 0x0A, 4),
            SpeedStep.Fast => new PtzSpeed(0x14, 0x10, 6),
            _ => throw new ArgumentOutOfRangeException(nameof(step)),
        };

        /// <summary>Starts a camera call whose result the view does not show. Failures surface through the link state.</summary>
        private static void Fire(Func<Task<bool>> call)
        {
            try
            {
                var task = call();
                if (!task.IsCompleted)
                    task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            }
            catch (Exception)
            {
                // A synchronous failure is reported by the camera's link state, not by a dialog.
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        /// <summary>
        /// One slider bound to an absolute camera position. A user change is sent at once when the
        /// throttle allows, otherwise by <see cref="Flush"/> from the timer; <see cref="EndDrag"/> sends the final value.
        /// </summary>
        private sealed class AbsoluteSlider
        {
            private readonly Func<int, Task<bool>> _send;
            private DateTime _lastSendAt = DateTime.MinValue;

            public AbsoluteSlider(int maximum, Func<int, Task<bool>> send)
            {
                Maximum = maximum;
                _send = send;
            }

            public int Maximum { get; }
            public int Value { get; private set; }
            public bool Dragging { get; set; }
            public bool Pending { get; private set; }

            /// <summary>Records a user change. Returns true when the value changed.</summary>
            public bool SetFromUser(int value, DateTime now)
            {
                int clamped = Clamp(value);
                if (clamped == Value)
                    return false;
                Value = clamped;
                Pending = true;
                Flush(now);
                return true;
            }

            /// <summary>Applies a camera readback. Ignored while dragging or while a user change is still unsent.</summary>
            public bool SetFromReadback(int value)
            {
                if (Dragging || Pending)
                    return false;
                int clamped = Clamp(value);
                if (clamped == Value)
                    return false;
                Value = clamped;
                return true;
            }

            /// <summary>Sends the pending value if the throttle interval has passed.</summary>
            public void Flush(DateTime now)
            {
                if (Pending && now - _lastSendAt >= SendInterval)
                    SendNow(now);
            }

            public void CancelPending() => Pending = false;

            public void EndDrag(DateTime now, bool send)
            {
                Dragging = false;
                if (send)
                    SendNow(now);
                else
                    Pending = false;
            }

            private void SendNow(DateTime now)
            {
                Pending = false;
                _lastSendAt = now;
                int value = Value;
                Fire(() => _send(value));
            }

            private int Clamp(int value) => Math.Clamp(value, 0, Maximum);
        }
    }
}
