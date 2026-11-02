using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace AvasRoutingTool.Services.Visca
{
    /// <summary>
    /// Raw VISCA over one TCP connection (no 8-byte header). Owns one TcpClient per
    /// connection and a single receive loop; DisposeAsync closes both and waits briefly
    /// for the loop to exit. A failed or cancelled write closes the connection, because a
    /// half-sent frame would corrupt the stream.
    /// </summary>
    public sealed class TcpViscaTransport : IViscaTransport
    {
        public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);
        public static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan LoopExitWait = TimeSpan.FromSeconds(2);
        private const int ReadBufferSize = 1024;

        private readonly string _host;
        private readonly int _port;
        private readonly object _gate = new();
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private Connection? _connection;   // guarded by _gate
        private bool _disposed;            // guarded by _gate

        public TcpViscaTransport(string host, int port)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(host);
            _host = host;
            _port = port;
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

            var client = new TcpClient { NoDelay = true };
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(ConnectTimeout);
                await client.ConnectAsync(_host, _port, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
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

            await _sendLock.WaitAsync(ct).ConfigureAwait(false);
            Exception? failure = null;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(SendTimeout);
                await connection.Stream.WriteAsync(visca, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failure = ex is OperationCanceledException && !ct.IsCancellationRequested
                    ? new TimeoutException("Sending the VISCA command timed out.", ex)
                    : ex;
            }
            finally
            {
                _sendLock.Release();
            }

            if (failure == null)
                return;

            if (Teardown(connection) && !IsDisposed())
                Disconnected?.Invoke(failure);
            throw failure;
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
            var reader = new ViscaStreamReader();
            var buffer = new byte[ReadBufferSize];
            try
            {
                while (true)
                {
                    int read = await connection.Stream.ReadAsync(buffer).ConfigureAwait(false);
                    if (read == 0)
                    {
                        // The camera closed the connection.
                        LoseConnection(connection, null);
                        return;
                    }

                    foreach (byte[] message in reader.Feed(buffer.AsSpan(0, read)))
                        MessageReceived?.Invoke(message);
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

            public Connection(TcpClient client)
            {
                Client = client;
                Stream = client.GetStream();
            }

            public TcpClient Client { get; }
            public NetworkStream Stream { get; }
            public Task? Loop { get; set; }

            public bool TryClose() => Interlocked.Exchange(ref _closed, 1) == 0;
        }
    }
}
