using System;
using System.Collections.Generic;
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
        private int _stopImmediatelyCount;
        private int _tickCount;

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

        /// <summary>Status returned by PollAsync; null simulates no answer.</summary>
        public CameraStatus? PollResult { get; set; } = new(CameraPower.On, 0, 0, true);

        /// <summary>The next n StopMoveAsync / StopZoomAsync calls return false (recorded, but not confirmed).</summary>
        public void FailNextStops(int n)
        {
            lock (_gate) _failStops = n;
        }

        public Task ConnectAsync(CancellationToken ct) => Record("Connect");

        public Task<bool> MoveAsync(PanTiltDirection d, PtzSpeed s, CancellationToken ct) => Ok($"Move:{d}:{s.Pan}:{s.Tilt}");

        public Task<bool> StopMoveAsync(CancellationToken ct) => Stop("StopMove");

        public Task<bool> HomeAsync(CancellationToken ct) => Ok("Home");

        public Task<bool> ZoomAsync(ZoomDirection d, PtzSpeed s, CancellationToken ct) => Ok($"Zoom:{d}:{s.Zoom}");

        public Task<bool> StopZoomAsync(CancellationToken ct) => Stop("StopZoom");

        public Task<bool> SetZoomPositionAsync(int pos, CancellationToken ct) => Ok($"ZoomPos:{pos}");

        public Task<bool> SetFocusAutoAsync(bool auto, CancellationToken ct) => Ok($"FocusAuto:{auto}");

        public Task<bool> SetFocusPositionAsync(int pos, CancellationToken ct) => Ok($"FocusPos:{pos}");

        public Task<bool> SetPowerAsync(bool on, CancellationToken ct) => Ok($"Power:{on}");

        public Task<CameraStatus?> PollAsync(CancellationToken ct)
        {
            Add("Poll");
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

        public IPtzCamera Create(RoomCameraConfig config)
        {
            var camera = _camera ?? new FakePtzCamera();
            Created.Add(camera);
            return camera;
        }
    }
}
