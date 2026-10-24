using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AvasRoutingTool.Sdvoe
{
    public class SdvoeClient : ISdvoeClient
    {
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private readonly SemaphoreSlim _commandLock = new(1, 1);
        private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(3) };
        private CancellationTokenSource? _readCts;
        private string _host = "127.0.0.1";
        private int _telnetPort = 6970;
        private int _restPort = 8080;
        private bool _isDisposed;

        public bool IsConnected => _tcpClient != null && _tcpClient.Connected;
        public event Action<string, bool>? DeviceStateChanged;

        public async Task<bool> ConnectAsync(string host, int telnetPort = 6970, int restPort = 8080)
        {
            await DisconnectAsync();

            _host = host;
            _telnetPort = telnetPort;
            _restPort = restPort;

            try
            {
                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(_host, _telnetPort);
                _stream = _tcpClient.GetStream();
                _reader = new StreamReader(_stream, Encoding.ASCII);
                _writer = new StreamWriter(_stream, Encoding.ASCII) { AutoFlush = true };

                // Handshake with SDVoE Control Server
                await SendCommandAsync("require api 3.0.0.0");

                _readCts = new CancellationTokenSource();
                _ = Task.Run(() => ListenServerEvents(_readCts.Token));

                return true;
            }
            catch (Exception)
            {
                await DisconnectAsync();
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            _readCts?.Cancel();
            _readCts?.Dispose();
            _readCts = null;

            await _commandLock.WaitAsync();
            try
            {
                _reader?.Dispose();
                _reader = null;

                _writer?.Dispose();
                _writer = null;

                _stream?.Dispose();
                _stream = null;

                _tcpClient?.Dispose();
                _tcpClient = null;
            }
            finally
            {
                _commandLock.Release();
            }
        }

        public async Task<string> SendCommandAsync(string command)
        {
            if (!IsConnected || _writer == null || _reader == null)
            {
                return string.Empty;
            }

            await _commandLock.WaitAsync();
            try
            {
                await _writer.WriteLineAsync(command);
                // Control server returns response terminated with newline
                string? response = await _reader.ReadLineAsync();
                return response ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
            finally
            {
                _commandLock.Release();
            }
        }

        public async Task<bool> JoinStreamAsync(string rxMac, string txMac, string streamType = "HDMI", int streamIndex = 0)
        {
            if (string.IsNullOrWhiteSpace(rxMac) || string.IsNullOrWhiteSpace(txMac)) return false;

            string cmd = $"join {rxMac}:{streamType}:{streamIndex} {txMac}:{streamType}:{streamIndex}";
            string response = await SendCommandAsync(cmd);
            return !response.Contains("ERROR", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<bool> LeaveStreamAsync(string rxMac, string streamType = "HDMI", int streamIndex = 0)
        {
            if (string.IsNullOrWhiteSpace(rxMac)) return false;

            string cmd = $"leave {rxMac}:{streamType}:{streamIndex}";
            string response = await SendCommandAsync(cmd);
            return !response.Contains("ERROR", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<bool> StartStream1ScalerAsync(string txMac, string multicastIp, int port = 5004)
        {
            if (string.IsNullOrWhiteSpace(txMac) || string.IsNullOrWhiteSpace(multicastIp)) return false;

            string cmd = $"set {txMac} stream HDMI 1 enable true address {multicastIp} port {port} auto_stop true remove_audio true";
            string response = await SendCommandAsync(cmd);
            return !response.Contains("ERROR", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<bool> StopStream1ScalerAsync(string txMac)
        {
            if (string.IsNullOrWhiteSpace(txMac)) return false;

            string cmd = $"set {txMac} stream HDMI 1 enable false address 0.0.0.0";
            string response = await SendCommandAsync(cmd);
            return !response.Contains("ERROR", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<List<AvasDevice>> DiscoverDevicesAsync()
        {
            var list = new List<AvasDevice>();
            try
            {
                string url = $"http://{_host}:{_restPort}/devices";
                var response = await _httpClient.GetStringAsync(url);
                using var doc = JsonDocument.Parse(response);

                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in doc.RootElement.EnumerateArray())
                    {
                        var dev = ParseDeviceElement(elem);
                        if (dev != null) list.Add(dev);
                    }
                }
            }
            catch (Exception)
            {
                // Fallback to empty list if REST service isn't reachable
            }
            return list;
        }

        private static AvasDevice? ParseDeviceElement(JsonElement elem)
        {
            try
            {
                string mac = elem.TryGetProperty("device_id", out var idProp) ? idProp.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(mac) && elem.TryGetProperty("mac", out var macProp))
                {
                    mac = macProp.GetString() ?? "";
                }

                if (string.IsNullOrEmpty(mac)) return null;

                string name = elem.TryGetProperty("device_name", out var nameProp) ? nameProp.GetString() ?? mac : mac;
                string ip = elem.TryGetProperty("ip_address", out var ipProp) ? ipProp.GetString() ?? "" : "";
                string mode = elem.TryGetProperty("mode", out var modeProp) ? modeProp.GetString() ?? "" : "";
                bool isTx = mode.Equals("TRANSMITTER", StringComparison.OrdinalIgnoreCase) || mode.Equals("TX", StringComparison.OrdinalIgnoreCase);
                bool isRx = mode.Equals("RECEIVER", StringComparison.OrdinalIgnoreCase) || mode.Equals("RX", StringComparison.OrdinalIgnoreCase);
                bool isActive = elem.TryGetProperty("is_active", out var actProp) ? actProp.GetBoolean() : true;

                return new AvasDevice
                {
                    DeviceId = mac,
                    DeviceName = name,
                    IpAddress = ip,
                    IsTransmitter = isTx,
                    IsReceiver = isRx,
                    IsActive = isActive
                };
            }
            catch
            {
                return null;
            }
        }

        private async Task ListenServerEvents(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && IsConnected && _reader != null)
            {
                try
                {
                    string? line = await _reader.ReadLineAsync(ct);
                    if (line == null) break;

                    // Handle event notifications like DEVICE_STATUS <mac> ONLINE/OFFLINE
                    if (line.StartsWith("EVENT DEVICE_STATUS", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 4)
                        {
                            string mac = parts[2];
                            bool online = parts[3].Equals("ONLINE", StringComparison.OrdinalIgnoreCase);
                            DeviceStateChanged?.Invoke(mac, online);
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

        public void Dispose()
        {
            if (_isDisposed) return;
            DisconnectAsync().GetAwaiter().GetResult();
            _httpClient.Dispose();
            _commandLock.Dispose();
            _isDisposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
