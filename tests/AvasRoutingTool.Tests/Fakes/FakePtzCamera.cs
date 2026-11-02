using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Services.Visca;

namespace AvasRoutingTool.Tests.Fakes
{
    /// <summary>
    /// In-memory IPtzCamera for view-model tests. Records every call in order (as a short
    /// string such as "Move:Left:5:4" or "StopMove"), lets a test set LinkState, and can fail the next Stops.
    /// </summary>
    public sealed class FakePtzCamera : IPtzCamera
    {
        private readonly object _gate = new();
        private readonly List<string> _calls = new();
        private CameraLinkState _linkState = CameraLinkState.Connected;
        private int _failStops;
        private int _failMoves;
        private bool _holdStops;
        private readonly List<TaskCompletionSource<bool>> _heldStops = new();
        private int _stopImmediatelyCount;
        private int _tickCount;
        private bool _holdPolls;
        private readonly List<TaskCompletionSource<CameraStatus?>> _heldPolls = new();

        public event Action<CameraLinkState>? LinkStateChanged;

        public CameraLinkState LinkState
        {
            get { lock (_gate) return _linkState; }
            set
            {
                lock (_gate)
                {
                    if (_linkState == value)
                        return;
                    _linkState = value;
                }
                LinkStateChanged?.Invoke(value);
            }
        }

        /// <summary>Every call so far, in order.</summary>
        public IReadOnlyList<string> Calls
        {
            get { lock (_gate) return _calls.ToArray(); }
        }

        public int StopImmediatelyCount => Volatile.Read(ref _stopImmediatelyCount);

        public int TickCount => Volatile.Read(ref _tickCount);

        public bool Disposed { get; private set; }

        private int _connectCount;
        private int _disposeCount;

        /// <summary>How many times ConnectAsync was called.</summary>
        public int ConnectCount => Volatile.Read(ref _connectCount);

        /// <summary>How many times DisposeAsync was called.</summary>
        public int DisposeCount => Volatile.Read(ref _disposeCount);

        /// <summary>Status returned by PollAsync; null simulates no answer.</summary>
        public CameraStatus? PollResult { get; set; } = new(CameraPower.On, 0, 0, true);

        /// <summary>The next n StopMoveAsync / StopZoomAsync calls return false (recorded, but not confirmed).</summary>
        public void FailNextStops(int n)
        {
            lock (_gate) _failStops = n;
        }

        /// <summary>The next n MoveAsync / ZoomAsync calls return false (recorded, but not acknowledged).</summary>
        public void FailNextMoves(int n)
        {
            lock (_gate) _failMoves = n;
        }

        /// <summary>
        /// While true, Stop calls are recorded and return a task that stays pending until
        /// <see cref="ReleaseHeldStops"/> is called (a camera that has not answered yet).
        /// </summary>
        public bool HoldStops
        {
            get { lock (_gate) return _holdStops; }
            set { lock (_gate) _holdStops = value; }
        }

        /// <summary>
        /// While true, PollAsync calls are recorded and return a task that stays pending until
        /// <see cref="ReleaseHeldPolls"/> is called (a camera that has not answered yet).
        /// </summary>
        public bool HoldPolls
        {
            get { lock (_gate) return _holdPolls; }
            set { lock (_gate) _holdPolls = value; }
        }

        /// <summary>Completes every held poll with the given status (null = no answer).</summary>
        public void ReleaseHeldPolls(CameraStatus? status)
        {
            TaskCompletionSource<CameraStatus?>[] held;
            lock (_gate)
            {
                held = _heldPolls.ToArray();
                _heldPolls.Clear();
            }
            foreach (var tcs in held)
                tcs.SetResult(status);
        }

        /// <summary>Completes every held Stop with the given result.</summary>
        public void ReleaseHeldStops(bool ok)
        {
            TaskCompletionSource<bool>[] held;
            lock (_gate)
            {
                held = _heldStops.ToArray();
                _heldStops.Clear();
            }
            foreach (var tcs in held)
                tcs.SetResult(ok);
        }

        public Task ConnectAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _connectCount);
            return Record("Connect");
        }

        public Task<bool> MoveAsync(PanTiltDirection d, PtzSpeed s, CancellationToken ct) => Move($"Move:{d}:{s.Pan}:{s.Tilt}");

        public Task<bool> StopMoveAsync(CancellationToken ct) => Stop("StopMove");

        public Task<bool> HomeAsync(CancellationToken ct) => Ok("Home");

        public Task<bool> ZoomAsync(ZoomDirection d, PtzSpeed s, CancellationToken ct) => Move($"Zoom:{d}:{s.Zoom}");

        public Task<bool> StopZoomAsync(CancellationToken ct) => Stop("StopZoom");

        public Task<bool> SetZoomPositionAsync(int pos, CancellationToken ct) => Ok($"ZoomPos:{pos}");

        public Task<bool> SetFocusAutoAsync(bool auto, CancellationToken ct) => Ok($"FocusAuto:{auto}");

        public Task<bool> SetFocusPositionAsync(int pos, CancellationToken ct) => Ok($"FocusPos:{pos}");

        public Task<bool> SetPowerAsync(bool on, CancellationToken ct) => Ok($"Power:{on}");

        public Task<CameraStatus?> PollAsync(CancellationToken ct)
        {
            Add("Poll");
            lock (_gate)
            {
                if (_holdPolls)
                {
                    var held = new TaskCompletionSource<CameraStatus?>();
                    _heldPolls.Add(held);
                    return held.Task;
                }
            }
            return Task.FromResult(PollResult);
        }

        public void StopImmediately()
        {
            Interlocked.Increment(ref _stopImmediatelyCount);
            Add("StopImmediately");
        }

        public void Tick() => Interlocked.Increment(ref _tickCount);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            Interlocked.Increment(ref _disposeCount);
            Add("Dispose");
            return ValueTask.CompletedTask;
        }

        private void Add(string call)
        {
            lock (_gate) _calls.Add(call);
        }

        private Task Record(string call)
        {
            Add(call);
            return Task.CompletedTask;
        }

        private Task<bool> Ok(string call)
        {
            Add(call);
            return Task.FromResult(true);
        }

        private Task<bool> Move(string call)
        {
            Add(call);
            lock (_gate)
            {
                if (_failMoves > 0)
                {
                    _failMoves--;
                    return Task.FromResult(false);
                }
            }
            return Task.FromResult(true);
        }

        private Task<bool> Stop(string call)
        {
            Add(call);
            lock (_gate)
            {
                if (_failStops > 0)
                {
                    _failStops--;
                    return Task.FromResult(false);
                }
                if (_holdStops)
                {
                    var held = new TaskCompletionSource<bool>();
                    _heldStops.Add(held);
                    return held.Task;
                }
            }
            return Task.FromResult(true);
        }
    }

    /// <summary>Factory returning a fixed FakePtzCamera (or a new one per call when none is given).</summary>
    public sealed class FakePtzCameraFactory : IPtzCameraFactory
    {
        private readonly FakePtzCamera? _camera;

        public FakePtzCameraFactory(FakePtzCamera? camera = null)
        {
            _camera = camera;
        }

        public List<FakePtzCamera> Created { get; } = new();

        /// <summary>How many times Create was called.</summary>
        public int CreateCount => Created.Count;

        /// <summary>How many created cameras have been disposed.</summary>
        public int DisposedCount => Created.Count(c => c.Disposed);

        public IPtzCamera Create(RoomCameraConfig config)
        {
            var camera = _camera ?? new FakePtzCamera();
            Created.Add(camera);
            return camera;
        }
    }
}
