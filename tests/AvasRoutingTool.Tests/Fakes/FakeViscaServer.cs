using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Services.Visca;

namespace AvasRoutingTool.Tests.Fakes
{
    /// <summary>
    /// Loopback stand-in for a VISCA camera. Binds 127.0.0.1 on an ephemeral port.
    /// Reply logic lives in ProcessMessage, which is transport-agnostic: the TCP side
    /// frames replies as raw bytes; UDP (RC5) will wrap them in ViscaFrame headers.
    /// </summary>
    public sealed class FakeViscaServer : IDisposable
    {
        private static readonly byte[] Ack = { 0x90, 0x41, 0xFF };
        private static readonly byte[] Completion = { 0x90, 0x51, 0xFF };

        private readonly object _gate = new();
        private readonly List<TcpClient> _clients = new();
        private TcpListener? _listener;
        private int _dropRemaining;   // guarded by _gate
        private bool _disposed;       // guarded by _gate

        public FakeViscaServer(ViscaTransportKind kind)
        {
            Kind = kind;
            Responder = DefaultResponder;

            switch (kind)
            {
                case ViscaTransportKind.Tcp:
                    _listener = new TcpListener(IPAddress.Loopback, 0);
                    _listener.Start();
                    Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                    _ = AcceptLoopAsync(_listener);
                    break;

                case ViscaTransportKind.Udp:
                    throw new NotSupportedException("UDP mode of the fake camera arrives with RC5.");

                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        public ViscaTransportKind Kind { get; }

        public int Port { get; }

        /// <summary>Every command the server answered or recorded, in arrival order (dropped commands excluded).</summary>
        public List<byte[]> Received { get; } = new();

        /// <summary>Maps one command or inquiry to the replies to send, in order. Defaults to the camera's behaviour.</summary>
        public Func<byte[], IEnumerable<byte[]>> Responder { get; set; }

        // Settable camera state, read by the default responder and updated by commands.
        public bool PowerOn { get; set; } = true;
        public int ZoomPosition { get; set; }
        public int FocusPosition { get; set; }
        public bool AutoFocus { get; set; }

        public int OpenClientCount
        {
            get { lock (_gate) return _clients.Count; }
        }

        /// <summary>Raised after a command is recorded, on the server's thread.</summary>
        public event Action<byte[]>? CommandReceived;

        /// <summary>Raised when a client connection ends, however it ends.</summary>
        public event Action? ClientDisconnected;

        /// <summary>The next n incoming messages are lost: neither recorded nor answered.</summary>
        public void DropNext(int n)
        {
            lock (_gate) _dropRemaining = n;
        }

        /// <summary>Closes every open client connection from the camera side (sends FIN). The server keeps listening.</summary>
        public void Disconnect()
        {
            List<TcpClient> clients;
            lock (_gate) clients = _clients.ToList();

            foreach (var client in clients)
            {
                try
                {
                    client.Client.Shutdown(SocketShutdown.Send);
                }
                catch (SocketException)
                {
                    // Already gone.
                }
            }
        }

        public void Dispose()
        {
            List<TcpClient> clients;
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
                clients = _clients.ToList();
            }

            _listener?.Stop();
            foreach (var client in clients)
                client.Close();
        }

        /// <summary>Runs the responder for one complete VISCA message and returns the replies.</summary>
        internal IReadOnlyList<byte[]> ProcessMessage(byte[] message)
        {
            lock (_gate)
            {
                if (_dropRemaining > 0)
                {
                    _dropRemaining--;
                    return Array.Empty<byte[]>();
                }
                Received.Add(message);
            }

            CommandReceived?.Invoke(message);
            return Responder(message).ToList();
        }

        private IEnumerable<byte[]> DefaultResponder(byte[] message)
        {
            if (message.Length < 3)
                return Array.Empty<byte[]>();

            if (message[1] == 0x09)
                return InquiryReply(message);

            if (message[1] == 0x01)
            {
                ApplyCommand(message);
                return new[] { Ack, Completion };
            }

            return Array.Empty<byte[]>();
        }

        private IEnumerable<byte[]> InquiryReply(byte[] message)
        {
            // Inquiry bodies: 04 00 power, 04 47 zoom, 04 48 focus position, 04 38 focus mode.
            if (message.Length != 5 || message[2] != 0x04)
                return Array.Empty<byte[]>();

            switch (message[3])
            {
                case 0x00:
                    return new[] { BoolReply(PowerOn) };
                case 0x47:
                    return new[] { PositionReply(ZoomPosition) };
                case 0x48:
                    return new[] { PositionReply(FocusPosition) };
                case 0x38:
                    return new[] { FocusModeReply(AutoFocus) };
                default:
                    return Array.Empty<byte[]>();
            }
        }

        private void ApplyCommand(byte[] message)
        {
            if (message.Length < 4 || message[2] != 0x04)
                return;

            switch (message[3])
            {
                case 0x00 when message.Length == 6:
                    PowerOn = message[4] == 0x02;
                    break;
                case 0x47 when message.Length == 9:
                    ZoomPosition = ReadPosition(message);
                    break;
                case 0x48 when message.Length == 9:
                    FocusPosition = ReadPosition(message);
                    break;
                case 0x38 when message.Length == 6:
                    AutoFocus = message[4] == 0x02;
                    break;
            }
        }

        private static int ReadPosition(byte[] message) =>
            (message[4] << 12) | (message[5] << 8) | (message[6] << 4) | message[7];

        // 90 50 02 FF (on) or 90 50 03 FF (standby / off).
        private static byte[] BoolReply(bool on) => new byte[] { 0x90, 0x50, on ? (byte)0x02 : (byte)0x03, 0xFF };

        // 90 50 02 FF (auto) or 90 50 03 FF (manual).
        private static byte[] FocusModeReply(bool auto) => BoolReply(auto);

        // 90 50 0p 0q 0r 0s FF: one nibble per byte, most significant first.
        private static byte[] PositionReply(int value) => new byte[]
        {
            0x90, 0x50,
            (byte)((value >> 12) & 0x0F),
            (byte)((value >> 8) & 0x0F),
            (byte)((value >> 4) & 0x0F),
            (byte)(value & 0x0F),
            0xFF
        };

        private async Task AcceptLoopAsync(TcpListener listener)
        {
            try
            {
                while (true)
                {
                    TcpClient client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    client.NoDelay = true;

                    bool keep;
                    lock (_gate)
                    {
                        keep = !_disposed;
                        if (keep)
                            _clients.Add(client);
                    }

                    if (!keep)
                    {
                        client.Close();
                        return;
                    }

                    _ = ServeTcpClientAsync(client);
                }
            }
            catch (Exception)
            {
                // The listener was stopped by Dispose.
            }
        }

        private async Task ServeTcpClientAsync(TcpClient client)
        {
            var reader = new ViscaStreamReader();
            var buffer = new byte[1024];
            try
            {
                NetworkStream stream = client.GetStream();
                while (true)
                {
                    int read = await stream.ReadAsync(buffer).ConfigureAwait(false);
                    if (read == 0)
                        break;

                    var messages = reader.Feed(buffer.AsSpan(0, read)).ToList();
                    foreach (byte[] message in messages)
                    {
                        foreach (byte[] reply in ProcessMessage(message))
                            await stream.WriteAsync(reply).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception)
            {
                // The client reset the connection, or the server closed it.
            }
            finally
            {
                bool removed;
                lock (_gate) removed = _clients.Remove(client);
                client.Close();
                if (removed)
                    ClientDisconnected?.Invoke();
            }
        }
    }
}
