using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace AvasRoutingTool.Rtp
{
    public interface IRtpReceiver : IDisposable
    {
        void StartListening(int[]? ports = null, string? localIp = null);
        void StopListening();
        void JoinGroup(string multicastIp);
        void DropGroup(string multicastIp);
        void RegisterChannel(string multicastIp, Action<byte[], int, int> packetHandler);
        void UnregisterChannel(string multicastIp);
        bool IsListening { get; }
    }

    public class UnifiedRtpDemuxReceiver : IRtpReceiver
    {
        private readonly ConcurrentDictionary<string, Action<byte[], int, int>> _channelHandlers = new();
        private readonly ConcurrentDictionary<int, UdpClient> _listeners = new();
        private readonly ConcurrentBag<string> _joinedGroups = new();
        private CancellationTokenSource? _cts;
        private IPAddress? _localIpAddress;
        private bool _isDisposed;

        public bool IsListening { get; private set; }

        public void StartListening(int[]? ports = null, string? localIp = null)
        {
            if (IsListening) return;

            ports ??= new[] { 5004, 5000 };
            if (!string.IsNullOrEmpty(localIp) && IPAddress.TryParse(localIp, out var parsedIp))
            {
                _localIpAddress = parsedIp;
            }

            _cts = new CancellationTokenSource();

            foreach (var port in ports)
            {
                try
                {
                    var client = CreateSocket(port, _localIpAddress);
                    _listeners[port] = client;
                    Task.Run(() => ReceiveLoop(client, _cts.Token));
                }
                catch (Exception)
                {
                    // Ignore port bind failure (e.g. port already bound by external test/service)
                }
            }

            IsListening = true;
        }

        private static UdpClient CreateSocket(int port, IPAddress? localIp)
        {
            var client = new UdpClient();
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            client.ExclusiveAddressUse = false;
            client.Client.ReceiveBufferSize = 8 * 1024 * 1024; // 8MB buffer

            var endpoint = new IPEndPoint(localIp ?? IPAddress.Any, port);
            client.Client.Bind(endpoint);
            return client;
        }

        public void JoinGroup(string multicastIp)
        {
            if (!IPAddress.TryParse(multicastIp, out var ip)) return;

            foreach (var kvp in _listeners)
            {
                try
                {
                    if (_localIpAddress != null)
                    {
                        kvp.Value.JoinMulticastGroup(ip, _localIpAddress);
                    }
                    else
                    {
                        kvp.Value.JoinMulticastGroup(ip);
                    }
                }
                catch { }
            }

            _joinedGroups.Add(multicastIp);
        }

        public void DropGroup(string multicastIp)
        {
            if (!IPAddress.TryParse(multicastIp, out var ip)) return;

            foreach (var kvp in _listeners)
            {
                try
                {
                    kvp.Value.DropMulticastGroup(ip);
                }
                catch { }
            }
        }

        public void RegisterChannel(string multicastIp, Action<byte[], int, int> packetHandler)
        {
            _channelHandlers[multicastIp] = packetHandler;
            JoinGroup(multicastIp);
        }

        public void UnregisterChannel(string multicastIp)
        {
            _channelHandlers.TryRemove(multicastIp, out _);
            DropGroup(multicastIp);
        }

        private async Task ReceiveLoop(UdpClient client, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var result = await client.ReceiveAsync(ct);
                    var data = result.Buffer;
                    if (data.Length >= 12)
                    {
                        foreach (var kvp in _channelHandlers)
                        {
                            kvp.Value?.Invoke(data, 0, data.Length);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    if (ct.IsCancellationRequested) break;
                }
            }
        }

        public void StopListening()
        {
            if (!IsListening) return;

            _cts?.Cancel();
            foreach (var group in _joinedGroups)
            {
                DropGroup(group);
            }

            foreach (var kvp in _listeners)
            {
                try
                {
                    kvp.Value.Dispose();
                }
                catch { }
            }
            _listeners.Clear();
            IsListening = false;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            StopListening();
            _cts?.Dispose();
            _isDisposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
