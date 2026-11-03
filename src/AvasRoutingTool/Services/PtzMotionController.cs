using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AvasRoutingTool.Services.Visca;

namespace AvasRoutingTool.Services
{
    public enum MotionState { Idle, Moving, Stopping, StopNotConfirmed }

    /// <summary>
    /// Safety rules for press-and-hold PTZ motion. Pan/tilt and zoom are two independent axes.
    /// Owns no timers or threads: <see cref="Tick"/> drives the watchdog and Stop retries against
    /// the injected clock. Camera calls are fire-and-forget; their results are tracked by
    /// generation so a late answer for a superseded command is ignored.
    /// Thread-safe: state is guarded by a lock, camera calls and events happen outside it.
    /// </summary>
    public sealed class PtzMotionController : IDisposable
    {
        private static readonly TimeSpan MoveLimit = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan StopAttemptTimeout = TimeSpan.FromSeconds(2);
        private const int MaxStopAttempts = 3;
        private const string WatchdogText = "Stopped (10 s limit)";
        private const string NotConnectedText = "Camera not connected";
        private const string MoveNotAckedText = "Move not acknowledged";
        private const string StopNotConfirmedText = "Stop not confirmed";

        private sealed class Axis
        {
            public Axis(Func<Task<bool>> stop)
            {
                Stop = stop;
            }

            public Func<Task<bool>> Stop { get; }
            public bool Active;
            public string Label = "";
            public DateTime BeganAt;
            public bool StopPending;      // a Stop is owed and not yet confirmed
            public bool StopExhausted;    // MaxStopAttempts failed; cleared by a later Stop that succeeds
            public bool StopInFlight;
            public int StopAttempts;
            public DateTime AttemptStartedAt;
            /// <summary>Bumped on every command; results carrying an older value are ignored.</summary>
            public int Generation;
        }

        private readonly object _gate = new();
        private readonly IPtzCamera _camera;
        private readonly Func<DateTime> _now;
        private readonly Axis _panTilt;
        private readonly Axis _zoom;
        private PtzSpeed _speed = new(0x0C, 0x0A, 4);   // Medium
        private string _idleText = "";
        private bool _disposed;

        public PtzMotionController(IPtzCamera camera, Func<DateTime> now)
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
            _now = now ?? throw new ArgumentNullException(nameof(now));
            _panTilt = new Axis(() => _camera.StopMoveAsync(CancellationToken.None));
            _zoom = new Axis(() => _camera.StopZoomAsync(CancellationToken.None));
        }

        public event Action? Changed;

        public PtzSpeed Speed
        {
            get { lock (_gate) return _speed; }
            set { lock (_gate) _speed = value; }
        }

        public MotionState State
        {
            get { lock (_gate) return ComputeState(); }
        }

        public string MovingText
        {
            get { lock (_gate) return ComputeText(); }
        }

        public void BeginMove(PanTiltDirection d)
        {
            Run(() =>
            {
                var sends = new List<Action>();
                if (_disposed || !CanStart(_panTilt, sends))
                    return sends;
                var speed = _speed;
                Begin(_panTilt, $"Moving {Words(d)}", () => _camera.MoveAsync(d, speed, CancellationToken.None), sends);
                return sends;
            });
        }

        public void BeginZoom(ZoomDirection d)
        {
            Run(() =>
            {
                var sends = new List<Action>();
                if (_disposed || !CanStart(_zoom, sends))
                    return sends;
                var speed = _speed;
                var label = d == ZoomDirection.Tele ? "Zooming in" : "Zooming out";
                Begin(_zoom, label, () => _camera.ZoomAsync(d, speed, CancellationToken.None), sends);
                return sends;
            });
        }

        /// <summary>Stops whatever is active. Does nothing when idle.</summary>
        public void End()
        {
            Run(() =>
            {
                var sends = new List<Action>();
                if (!_disposed)
                {
                    EndAxis(_panTilt, sends);
                    EndAxis(_zoom, sends);
                }
                return sends;
            });
        }

        /// <summary>Ends both axes (pan/tilt and zoom). Used on tab switch, deactivate and view unload.</summary>
        public void StopAll() => End();

        /// <summary>Drives the watchdog and Stop retries. Call from the view model's timer.</summary>
        public void Tick()
        {
            Run(() =>
            {
                var sends = new List<Action>();
                if (_disposed)
                    return sends;
                var now = _now();
                foreach (var axis in new[] { _panTilt, _zoom })
                {
                    if (axis.Active && now - axis.BeganAt > MoveLimit)
                    {
                        axis.Active = false;
                        _idleText = WatchdogText;
                        StartStop(axis, sends);
                    }

                    if (axis.StopPending && axis.StopInFlight && now - axis.AttemptStartedAt > StopAttemptTimeout)
                    {
                        // No answer in time: count the attempt as failed and ignore any late reply.
                        axis.Generation++;
                        axis.StopInFlight = false;
                        if (axis.StopAttempts >= MaxStopAttempts)
                        {
                            axis.StopPending = false;
                            axis.StopExhausted = true;
                        }
                    }

                    if (axis.StopPending && !axis.StopInFlight)
                        SendStopAttempt(axis, sends);
                }
                return sends;
            });
        }

        /// <summary>Sends one Stop if a move is still running on either axis. Stop-once semantics; no retries.</summary>
        public void Dispose()
        {
            Run(() =>
            {
                var sends = new List<Action>();
                if (_disposed)
                    return sends;
                _disposed = true;
                EndAxis(_panTilt, sends);
                EndAxis(_zoom, sends);
                return sends;
            });
        }

        // ---- internals: all of these run under _gate and return camera calls to make afterwards ----

        private bool CanStart(Axis axis, List<Action> sends)
        {
            if (_camera.LinkState == CameraLinkState.Connected)
                return true;
            EndAxis(axis, sends);   // never leave a running motion behind a dropped link
            _idleText = NotConnectedText;
            return false;
        }

        private void Begin(Axis axis, string label, Func<Task<bool>> move, List<Action> sends)
        {
            axis.Active = true;
            axis.Label = label;
            axis.BeganAt = _now();
            axis.StopPending = false;
            axis.StopExhausted = false;
            axis.StopInFlight = false;
            axis.StopAttempts = 0;
            axis.Generation++;
            _idleText = "";

            int gen = axis.Generation;
            sends.Add(() => Observe(move, ok => OnMoveResult(axis, gen, ok)));
        }

        private void EndAxis(Axis axis, List<Action> sends)
        {
            if (axis.Active)
            {
                axis.Active = false;
                axis.Label = "";
                _idleText = "";
                StartStop(axis, sends);
            }
            else if (axis.StopExhausted)
            {
                // The operator asked again: a fresh Stop cycle supersedes the unconfirmed one.
                StartStop(axis, sends);
            }
            // Otherwise: idle, or a Stop is already pending and Tick is driving it.
        }

        private void StartStop(Axis axis, List<Action> sends)
        {
            axis.Generation++;
            axis.StopPending = true;
            axis.StopExhausted = false;
            axis.StopAttempts = 0;
            SendStopAttempt(axis, sends);
        }

        private void SendStopAttempt(Axis axis, List<Action> sends)
        {
            axis.StopAttempts++;
            axis.StopInFlight = true;
            axis.AttemptStartedAt = _now();
            int gen = axis.Generation;
            sends.Add(() => Observe(axis.Stop, ok => OnStopResult(axis, gen, ok)));
        }

        private void OnMoveResult(Axis axis, int gen, bool ok) => Run(() =>
        {
            var sends = new List<Action>();
            if (ok || axis.Generation != gen || !axis.Active)
                return sends;
            // Not acknowledged: the camera may still be moving, so end this axis with a Stop.
            axis.Active = false;
            axis.Label = "";
            _idleText = MoveNotAckedText;
            StartStop(axis, sends);
            return sends;
        });

        private void OnStopResult(Axis axis, int gen, bool ok) => Run(() =>
        {
            var sends = new List<Action>();
            if (axis.Generation != gen)
                return sends;
            axis.StopInFlight = false;
            if (ok)
            {
                axis.StopPending = false;
                axis.StopExhausted = false;
                axis.StopAttempts = 0;
            }
            else if (axis.StopAttempts >= MaxStopAttempts)
            {
                axis.StopPending = false;
                axis.StopExhausted = true;
            }
            return sends;
        });

        private MotionState ComputeState()
        {
            if (_panTilt.Active || _zoom.Active) return MotionState.Moving;
            if (_panTilt.StopPending || _zoom.StopPending) return MotionState.Stopping;
            if (_panTilt.StopExhausted || _zoom.StopExhausted) return MotionState.StopNotConfirmed;
            return MotionState.Idle;
        }

        private string ComputeText()
        {
            switch (ComputeState())
            {
                case MotionState.Moving:
                    var parts = new List<string>();
                    if (_panTilt.Active) parts.Add(_panTilt.Label);
                    if (_zoom.Active) parts.Add(_zoom.Label);
                    return string.Join(" and ", parts);
                case MotionState.Stopping:
                    return _idleText.Length > 0 ? _idleText : "Stopping";
                case MotionState.StopNotConfirmed:
                    return StopNotConfirmedText;
                default:
                    return _idleText;
            }
        }

        /// <summary>Runs a state change under the lock, raises Changed if the state or text moved, then makes the camera calls.</summary>
        private void Run(Func<List<Action>> plan)
        {
            List<Action> sends;
            bool changed;
            lock (_gate)
            {
                var before = (ComputeState(), ComputeText());
                sends = plan();
                changed = before != (ComputeState(), ComputeText());
            }

            if (changed)
                Changed?.Invoke();
            foreach (var send in sends)
                send();
        }

        /// <summary>Calls the camera and reports its result. A synchronous completion is reported inline.</summary>
        private static void Observe(Func<Task<bool>> call, Action<bool> onResult)
        {
            Task<bool> task;
            try
            {
                task = call();
            }
            catch (Exception)
            {
                onResult(false);
                return;
            }

            if (task.IsCompleted)
            {
                onResult(Outcome(task));
                return;
            }
            task.ContinueWith(t => onResult(Outcome(t)), TaskContinuationOptions.ExecuteSynchronously);
        }

        private static bool Outcome(Task<bool> t)
        {
            if (t.IsFaulted)
            {
                _ = t.Exception;   // mark observed
                return false;
            }
            return !t.IsCanceled && t.Result;
        }

        private static string Words(PanTiltDirection d) => d switch
        {
            PanTiltDirection.Up => "up",
            PanTiltDirection.Down => "down",
            PanTiltDirection.Left => "left",
            PanTiltDirection.Right => "right",
            PanTiltDirection.UpLeft => "up-left",
            PanTiltDirection.UpRight => "up-right",
            PanTiltDirection.DownLeft => "down-left",
            PanTiltDirection.DownRight => "down-right",
            _ => throw new ArgumentOutOfRangeException(nameof(d)),
        };
    }
}
