using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace AvasRoutingTool.Services.Visca
{
    /// <summary>
    /// VISCA-over-IP on one connected UDP socket. Each datagram carries an 8-byte
    /// ViscaFrame header; the transport strips it and raises the VISCA bytes. The socket
    /// is connected to the camera, so the OS drops datagrams from other endpoints.
    /// Sequence numbers start at 1 after a sequence-reset control message and increase
    /// per message. Lost datagrams are not retried here: the camera layer owns that.
    /// </summary>
    public sealed class UdpViscaTransport : IViscaTransport
    {
        public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);
        public static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan LoopExitWait = TimeSpan.FromSeconds(2);

        private readonly string _host;
        private readonly int _port;
        private readonly int _localPort;
        private readonly object _gate = new();
        private Connection? _connection;   // guarded by _gate
        private bool _disposed;            // guarded by _gate

        /// <param name="localPort">Local port to bind; 0 lets the OS choose.</param>
        public UdpViscaTransport(string host, int port, int localPort = 0)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(host);
            ArgumentOutOfRangeException.ThrowIfNegative(localPort);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(localPort, ushort.MaxValue);
            _host = host;
            _port = port;
            _localPort = localPort;
        }

        public event Action<byte[]>? MessageReceived;
        public event Action<Exception?>? Disconnected;

        public bool IsConnected
        {
            get { lock (_gate) return _connection != null; }
        }

        public async Task ConnectAsync(CancellationToken ct)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_connection != null)
                    throw new InvalidOperationException("The transport is already connected.");
            }

            var client = new UdpClient(_localPort);
            try
            {
                // UDP connect only fixes the peer (and may resolve a host name), so it runs off-thread with a bound.
                await Task.Run(() => client.Connect(_host, _port), ct).WaitAsync(ConnectTimeout, ct).ConfigureAwait(false);
                byte[] reset = ViscaFrame.SequenceReset();
                await client.SendAsync(reset, reset.Length).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                client.Dispose();
                throw new TimeoutException($"Connecting to {_host}:{_port} timed out.", ex);
            }
            catch
            {
                client.Dispose();
                throw;
            }

            lock (_gate)
            {
                if (_disposed || _connection != null)
                {
                    client.Dispose();
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    throw new InvalidOperationException("The transport is already connected.");
                }

                var connection = new Connection(client);
                _connection = connection;
                connection.Loop = Task.Run(() => ReceiveLoopAsync(connection));
            }
        }

        public async Task SendAsync(byte[] visca, bool isInquiry, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(visca);
            Connection connection = CurrentOrThrow();

            ViscaPayloadType type = isInquiry ? ViscaPayloadType.Inquiry : ViscaPayloadType.Command;
            byte[] frame = ViscaFrame.Encode(type, connection.NextSequence(), visca);

            try
            {
                await connection.Client.SendAsync(frame, frame.Length).WaitAsync(SendTimeout, ct).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                throw new TimeoutException("Sending the VISCA command timed out.", ex);
            }
            catch (SocketException ex)
            {
                // The socket itself failed; a cancelled send leaves the socket usable.
                if (Teardown(connection) && !IsDisposed())
                    Disconnected?.Invoke(ex);
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Connection? connection;
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
                connection = _connection;
            }

            if (connection == null)
                return;

            Teardown(connection);
            if (connection.Loop != null)
            {
                try
                {
                    await connection.Loop.WaitAsync(LoopExitWait).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    // The loop is still unwinding (for example it is inside a subscriber); its socket is already closed.
                }
            }
        }

        private async Task ReceiveLoopAsync(Connection connection)
        {
            var buffer = new byte[ViscaFrame.HeaderLength + ushort.MaxValue];
            try
            {
                while (true)
                {
                    int read = await connection.Client.Client.ReceiveAsync(buffer.AsMemory(), SocketFlags.None).ConfigureAwait(false);
                    if (!ViscaFrame.TryDecode(buffer.AsSpan(0, read), out ViscaPayloadType type, out _, out byte[] visca))
                        continue;   // Malformed datagram: dropped silently.

                    // Replies are delivered whatever their sequence number; control replies are not VISCA.
                    if (type == ViscaPayloadType.Reply)
                        MessageReceived?.Invoke(visca);
                }
            }
            catch (Exception ex)
            {
                // Also covers a socket closed by DisposeAsync or by a failed send (Teardown already won).
                LoseConnection(connection, ex);
            }
        }

        private void LoseConnection(Connection connection, Exception? cause)
        {
            if (Teardown(connection) && !IsDisposed())
                Disconnected?.Invoke(cause);
        }

        private Connection CurrentOrThrow()
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _connection ?? throw new InvalidOperationException("The transport is not connected.");
            }
        }

        private bool IsDisposed()
        {
            lock (_gate) return _disposed;
        }

        /// <summary>Closes the connection once. Returns true only for the caller that closed it.</summary>
        private bool Teardown(Connection connection)
        {
            if (!connection.TryClose())
                return false;

            lock (_gate)
            {
                if (ReferenceEquals(_connection, connection))
                    _connection = null;
            }

            connection.Client.Dispose();
            return true;
        }

        private sealed class Connection
        {
            private int _closed;
            private uint _sequence;

            public Connection(UdpClient client)
            {
                Client = client;
            }

            public UdpClient Client { get; }
            public Task? Loop { get; set; }

            /// <summary>Sequence numbers start at 1; the sequence-reset message is sent as 0.</summary>
            public uint NextSequence() => Interlocked.Increment(ref _sequence);

            public bool TryClose() => Interlocked.Exchange(ref _closed, 1) == 0;
        }
    }
}
