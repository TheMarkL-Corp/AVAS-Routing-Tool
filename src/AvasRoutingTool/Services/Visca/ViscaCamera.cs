using System;
using System.Threading;
using System.Threading.Tasks;
using AvasRoutingTool.Configuration;

namespace AvasRoutingTool.Services.Visca
{
    /// <summary>
    /// One PTZ camera over an IViscaTransport. This file holds construction, the link
    /// state machine, reconnect backoff (driven by Tick and the injected clock, no
    /// internal timers) and disposal. Commands are in ViscaCamera.Commands.cs and the
    /// reply-matching pipeline in ViscaCamera.Pipeline.cs.
    /// </summary>
    public sealed partial class ViscaCamera : IPtzCamera
    {
        private const int UnreachableAfter = 3;
        private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan DisposeWait = TimeSpan.FromSeconds(3);

        private readonly RoomCameraConfig _config;
        private readonly Func<DateTime> _now;
        private readonly Func<RoomCameraConfig, IViscaTransport> _transportFactory;
        private readonly int _address;
        private readonly CancellationTokenSource _lifetime = new();
        private readonly object _gate = new();   // guards the fields below
        private IViscaTransport? _transport;     // set only while connected
        private CameraLinkState _linkState = CameraLinkState.Disconnected;
        private bool _connecting;
        private bool _wantConnected;
        private bool _disposed;
        private int _connectFailures;
        private int _pollTimeouts;
        private DateTime _nextAttempt;
        private Task _connectTask = Task.CompletedTask;

        public ViscaCamera(RoomCameraConfig config, Func<DateTime> now, Func<RoomCameraConfig, IViscaTransport>? transportFactory = null)
        {
            ArgumentNullException.ThrowIfNull(config);
            ArgumentNullException.ThrowIfNull(now);
            _config = config;
            _now = now;
            _transportFactory = transportFactory ?? PtzCameraFactory.CreateTransport;
            _address = config.ViscaAddress;
        }

        public event Action<CameraLinkState>? LinkStateChanged;

        public CameraLinkState LinkState
        {
            get { lock (_gate) return _linkState; }
        }

        /// <summary>When the next reconnect attempt may start (test hook).</summary>
        internal DateTime NextConnectAttempt
        {
            get { lock (_gate) return _nextAttempt; }
        }

        public async Task ConnectAsync(CancellationToken ct)
        {
            lock (_gate)
            {
                if (_disposed)
                    return;
                _wantConnected = true;
            }
            if (!TryBeginConnect(requireDue: false))
                return;
            await ConnectCoreAsync(ct).ConfigureAwait(false);
        }

        public void Tick()
        {
            try
            {
                if (!TryBeginConnect(requireDue: true))
                    return;
                var token = _lifetime.Token;
                lock (_gate)
                    _connectTask = Task.Run(async () =>
                    {
                        try { await ConnectCoreAsync(token).ConfigureAwait(false); }
                        catch (Exception) { /* cancelled by dispose, or already recorded as a failed attempt */ }
                    });
            }
            catch (Exception)
            {
                // Tick never throws.
            }
        }

        public async ValueTask DisposeAsync()
        {
            IViscaTransport? transport;
            Task connectTask;
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
                transport = _transport;
                _transport = null;
                connectTask = _connectTask;
            }

            try { _lifetime.Cancel(); } catch (ObjectDisposedException) { }
            FailAllPending();
            SetState(CameraLinkState.Disconnected);

            if (transport != null)
                await DisposeTransportAsync(transport).ConfigureAwait(false);

            try { await connectTask.WaitAsync(DisposeWait).ConfigureAwait(false); }
            catch (Exception) { /* the attempt cleans up after itself */ }

            _lifetime.Dispose();
        }

        // ---- link state machine ----

        /// <summary>Claims the single connect slot. With requireDue it also checks the reconnect schedule.</summary>
        private bool TryBeginConnect(bool requireDue)
        {
            lock (_gate)
            {
                if (_disposed || _connecting || _transport != null || !_wantConnected)
                    return false;
                if (requireDue && _now() < _nextAttempt)
                    return false;
                _connecting = true;
                return true;
            }
        }

        private async Task ConnectCoreAsync(CancellationToken ct)
        {
            IViscaTransport? transport = null;
            bool connected = false;
            try
            {
                lock (_gate)
                {
                    if (_linkState == CameraLinkState.Disconnected)
                        SetStateLocked(CameraLinkState.Connecting);
                }
                FlushState();

                transport = _transportFactory(_config);
                IViscaTransport created = transport;
                transport.MessageReceived += OnMessage;
                transport.Disconnected += cause => OnTransportDisconnected(created, cause);
                await transport.ConnectAsync(ct).ConfigureAwait(false);

                lock (_gate)
                {
                    if (!_disposed && transport.IsConnected)
                    {
                        _transport = transport;
                        _connectFailures = 0;
                        _pollTimeouts = 0;
                        SetStateLocked(CameraLinkState.Connected);
                        connected = true;
                    }
                }
                FlushState();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                await DisposeTransportAsync(transport).ConfigureAwait(false);
                lock (_gate)
                {
                    _connecting = false;
                    if (_linkState == CameraLinkState.Connecting)
                        SetStateLocked(CameraLinkState.Disconnected);
                }
                FlushState();
                throw;
            }
            catch (Exception)
            {
                await DisposeTransportAsync(transport).ConfigureAwait(false);
                transport = null;
                lock (_gate)
                {
                    if (!_disposed)
                    {
                        _connectFailures++;
                        _nextAttempt = _now() + Backoff(_connectFailures);
                        SetStateLocked(_connectFailures >= UnreachableAfter ? CameraLinkState.Unreachable : CameraLinkState.Connecting);
                    }
                }
                FlushState();
            }
            finally
            {
                lock (_gate) _connecting = false;
            }

            if (!connected && transport != null)
                await DisposeTransportAsync(transport).ConfigureAwait(false);
        }

        /// <summary>1, 2, 4, ... seconds after the 1st, 2nd, 3rd ... failure, capped at 30.</summary>
        internal static TimeSpan Backoff(int failures)
        {
            double seconds = Math.Pow(2, Math.Max(0, failures - 1));
            return seconds >= MaxBackoff.TotalSeconds ? MaxBackoff : TimeSpan.FromSeconds(seconds);
        }

        private void OnTransportDisconnected(IViscaTransport transport, Exception? cause)
        {
            lock (_gate)
            {
                if (_disposed || !ReferenceEquals(_transport, transport))
                    return;
                _transport = null;
                _nextAttempt = _now();   // first reconnect is due on the next Tick
                SetStateLocked(CameraLinkState.Disconnected);
            }
            FailAllPending();
            FlushState();
            _ = DisposeTransportAsync(transport);
        }

        private void ReportSuccess()
        {
            lock (_gate)
            {
                _pollTimeouts = 0;
                _connectFailures = 0;
                if (_linkState == CameraLinkState.Unreachable && _transport != null)
                    SetStateLocked(CameraLinkState.Connected);
            }
            FlushState();
        }

        private void ReportPollTimeout()
        {
            lock (_gate)
            {
                if (_disposed || _transport == null)
                    return;
                _pollTimeouts++;
                if (_pollTimeouts >= UnreachableAfter)
                    SetStateLocked(CameraLinkState.Unreachable);
            }
            FlushState();
        }

        // State changes are recorded under the gate and raised outside it, in order.
        private readonly System.Collections.Generic.Queue<CameraLinkState> _changes = new();
        private int _flushing;

        private void SetState(CameraLinkState state)
        {
            lock (_gate) SetStateLocked(state);
            FlushState();
        }

        private void SetStateLocked(CameraLinkState state)
        {
            if (_linkState == state)
                return;
            _linkState = state;
            _changes.Enqueue(state);
        }

        private void FlushState()
        {
            // One thread at a time raises events, so subscribers see changes in order.
            if (Interlocked.CompareExchange(ref _flushing, 1, 0) != 0)
                return;
            try
            {
                while (true)
                {
                    CameraLinkState next;
                    lock (_gate)
                    {
                        if (_changes.Count == 0)
                            break;
                        next = _changes.Dequeue();
                    }
                    try { LinkStateChanged?.Invoke(next); }
                    catch (Exception) { /* a subscriber must not break the link */ }
                }
            }
            finally
            {
                Volatile.Write(ref _flushing, 0);
            }
            bool more;
            lock (_gate) more = _changes.Count > 0;
            if (more)
                FlushState();
        }

        private IViscaTransport? CurrentTransport()
        {
            lock (_gate) return _disposed ? null : _transport;
        }

        private static async Task DisposeTransportAsync(IViscaTransport? transport)
        {
            if (transport == null)
                return;
            try { await transport.DisposeAsync().ConfigureAwait(false); }
            catch (Exception) { /* best effort */ }
        }
    }
}
