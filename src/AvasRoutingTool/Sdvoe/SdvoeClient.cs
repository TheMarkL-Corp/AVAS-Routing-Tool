using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AvasRoutingTool.Logging;

namespace AvasRoutingTool.Sdvoe
{
    public class SdvoeClient : ISdvoeClient
    {
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private readonly SemaphoreSlim _commandLock = new(1, 1);
        private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(4) };
        private CancellationTokenSource? _readCts;
        private TaskCompletionSource<string>? _pendingCommandTcs;
        private string _host = "127.0.0.1";
        private int _telnetPort = 6970;
        private int _restPort = 8090;
        private bool _isDisposed;

        public bool IsConnected => _tcpClient != null && _tcpClient.Connected;
        public event Action<string, bool>? DeviceStateChanged;

        public async Task<bool> ConnectAsync(string host, int telnetPort = 6970, int restPort = 8090)
        {
            await DisconnectAsync();

            _host = string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ? "127.0.0.1" : host;
            _telnetPort = telnetPort;
            _restPort = restPort;

            AppLogger.Info("SDVOE", $"Connecting to Control Server Telnet at {_host}:{_telnetPort} and REST at {_host}:{_restPort}...");

            try
            {
                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(_host, _telnetPort);
                _stream = _tcpClient.GetStream();
                _reader = new StreamReader(_stream, Encoding.ASCII);
                _writer = new StreamWriter(_stream, Encoding.ASCII) { AutoFlush = true, NewLine = "\r\n" };

                _readCts = new CancellationTokenSource();
                _ = Task.Run(() => SocketReadLoop(_readCts.Token));

                // Handshake with SDVoE Control Server
                string handshakeRes = await SendCommandAsync("require api 3.0.0.0");
                AppLogger.Info("SDVOE", $"Handshake require api 3.0.0.0 result: {handshakeRes}");

                bool ok = handshakeRes.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase);
                if (ok)
                {
                    AppLogger.Info("SDVOE", "Successfully connected and authenticated with SDVoE Control Server.");
                }
                else
                {
                    AppLogger.Warn("SDVOE", $"Handshake did not return SUCCESS. Response: {handshakeRes}");
                }

                return ok;
            }
            catch (Exception ex)
            {
                AppLogger.Error("SDVOE", $"Failed to connect to {_host}:{_telnetPort}", ex);
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

                _pendingCommandTcs?.TrySetCanceled();
                _pendingCommandTcs = null;
            }
            finally
            {
                _commandLock.Release();
            }
        }

        public async Task<string> SendCommandAsync(string command)
        {
            if (!IsConnected || _writer == null)
            {
                AppLogger.Warn("SDVOE", $"Cannot send '{command}': not connected.");
                return string.Empty;
            }

            await _commandLock.WaitAsync();
            try
            {
                _pendingCommandTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                await _writer.WriteLineAsync(command);

                // Wait up to 3.5 seconds for response
                var timeoutTask = Task.Delay(3500);
                var completedTask = await Task.WhenAny(_pendingCommandTcs.Task, timeoutTask);

                if (completedTask == _pendingCommandTcs.Task)
                {
                    return await _pendingCommandTcs.Task;
                }
                else
                {
                    AppLogger.Warn("SDVOE", $"Command timeout waiting for response to: {command}");
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("SDVOE", $"Error sending command '{command}'", ex);
                return string.Empty;
            }
            finally
            {
                _pendingCommandTcs = null;
                _commandLock.Release();
            }
        }

        private async Task SocketReadLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _reader != null)
            {
                try
                {
                    string? line = await _reader.ReadLineAsync(ct);
                    if (line == null) break;

                    if (string.IsNullOrWhiteSpace(line)) continue;

                    // Check for unsolicited notifications
                    if (line.StartsWith("EVENT DEVICE_STATUS", StringComparison.OrdinalIgnoreCase))
                    {
                        AppLogger.Info("SDVOE-EVENT", line);
                        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 4)
                        {
                            string mac = parts[2];
                            bool online = parts[3].Equals("ONLINE", StringComparison.OrdinalIgnoreCase);
                            DeviceStateChanged?.Invoke(mac, online);
                        }
                    }
                    else if (line.StartsWith("EVENT", StringComparison.OrdinalIgnoreCase))
                    {
                        AppLogger.Debug("SDVOE-EVENT", line);
                    }
                    else
                    {
                        // Command response line
                        _pendingCommandTcs?.TrySetResult(line);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested) break;
                    AppLogger.Debug("SDVOE", $"Read loop exception: {ex.Message}");
                }
            }
        }

        public async Task<bool> JoinStreamAsync(string rxMac, string txMac, string streamType = "HDMI", int streamIndex = 0)
        {
            if (string.IsNullOrWhiteSpace(rxMac) || string.IsNullOrWhiteSpace(txMac)) return false;

            string cmd = $"join {rxMac}:{streamType}:{streamIndex} {txMac}:{streamType}:{streamIndex}";
            AppLogger.Info("ROUTING", $"Executing video route: {cmd}");
            string response = await SendCommandAsync(cmd);
            AppLogger.Info("ROUTING", $"Route response: {response}");
            return response.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase) || !response.Contains("ERROR", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<bool> LeaveStreamAsync(string rxMac, string streamType = "HDMI", int streamIndex = 0)
        {
            if (string.IsNullOrWhiteSpace(rxMac)) return false;

            string cmd = $"leave {rxMac}:{streamType}:{streamIndex}";
            AppLogger.Info("ROUTING", $"Executing video unroute: {cmd}");
            string response = await SendCommandAsync(cmd);
            AppLogger.Info("ROUTING", $"Unroute response: {response}");
            return response.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase) || !response.Contains("ERROR", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<bool> StartStream1ScalerAsync(string txMac, string multicastIp, int port = 5004)
        {
            if (string.IsNullOrWhiteSpace(txMac) || string.IsNullOrWhiteSpace(multicastIp)) return false;

            string cmd = $"set {txMac} stream HDMI 1 enable true address {multicastIp} port {port} auto_stop true remove_audio true";
            AppLogger.Info("RTP", $"Starting preview Stream 1 Scaler: {cmd}");
            string response = await SendCommandAsync(cmd);
            return response.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase) || !response.Contains("ERROR", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<bool> StopStream1ScalerAsync(string txMac)
        {
            if (string.IsNullOrWhiteSpace(txMac)) return false;

            string cmd = $"set {txMac} stream HDMI 1 enable false address 0.0.0.0";
            AppLogger.Info("RTP", $"Stopping preview Stream 1 Scaler: {cmd}");
            string response = await SendCommandAsync(cmd);
            return response.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase) || !response.Contains("ERROR", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<List<AvasDevice>> DiscoverDevicesAsync()
        {
            var list = new List<AvasDevice>();

            // 1. Try Telnet first: 'get all identity'
            if (IsConnected)
            {
                AppLogger.Info("DISCOVERY", "Querying devices via Telnet: 'get all identity'...");
                string telnetJson = await SendCommandAsync("get all identity");
                if (!string.IsNullOrWhiteSpace(telnetJson))
                {
                    list = ParseDevicesJson(telnetJson);
                    if (list.Count > 0)
                    {
                        AppLogger.Info("DISCOVERY", $"Telnet discovery found {list.Count} devices: {string.Join(", ", list.ConvertAll(d => $"{d.DeviceName} ({d.DeviceId}, IP:{d.IpAddress}, TX:{d.IsTransmitter}, RX:{d.IsReceiver})"))}");
                        return list;
                    }
                }
            }

            // 2. Fallback to REST API: POST http://{_host}:{_restPort}/api/device/ALL with body {"op":"get","subset":"identity"}
            try
            {
                string url = $"http://{_host}:{_restPort}/api/device/ALL";
                AppLogger.Info("DISCOVERY", $"Querying devices via REST: POST {url} with subset=identity...");

                var content = new StringContent("{\"op\":\"get\",\"subset\":\"identity\"}", Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    string restJson = await response.Content.ReadAsStringAsync();
                    list = ParseDevicesJson(restJson);
                    AppLogger.Info("DISCOVERY", $"REST discovery found {list.Count} devices: {string.Join(", ", list.ConvertAll(d => $"{d.DeviceName} ({d.DeviceId}, IP:{d.IpAddress}, TX:{d.IsTransmitter}, RX:{d.IsReceiver})"))}");
                    return list;
                }
                else
                {
                    AppLogger.Warn("DISCOVERY", $"REST request to {url} returned status {(int)response.StatusCode} {response.ReasonPhrase}");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("DISCOVERY", $"REST device discovery failed: {ex.Message}");
            }

            return list;
        }

        public static List<AvasDevice> ParseDevicesJson(string json)
        {
            var list = new List<AvasDevice>();
            if (string.IsNullOrWhiteSpace(json)) return list;

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("result", out var resultElem)) return list;
                if (!resultElem.TryGetProperty("devices", out var devicesElem)) return list;

                foreach (var d in devicesElem.EnumerateArray())
                {
                    string mac = d.TryGetProperty("device_id", out var macProp) ? macProp.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(mac) && d.TryGetProperty("mac", out var altMac))
                    {
                        mac = altMac.GetString() ?? "";
                    }

                    if (string.IsNullOrEmpty(mac)) continue;

                    // Device Name (prefer device_name root, fallback to configuration.device_name, fallback to MAC)
                    string name = d.TryGetProperty("device_name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(name) && d.TryGetProperty("configuration", out var confElem) &&
                        confElem.TryGetProperty("device_name", out var confName))
                    {
                        name = confName.GetString() ?? "";
                    }
                    if (string.IsNullOrEmpty(name)) name = mac;

                    // Active status
                    bool isActive = true;
                    if (d.TryGetProperty("status", out var statusElem) && statusElem.TryGetProperty("active", out var activeProp))
                    {
                        isActive = activeProp.GetBoolean();
                    }

                    // Identity (TX, RX, Vendor, Product, Chip)
                    int vid = 105, pid = 80, chip = 0;
                    bool isTx = false, isRx = false;

                    if (d.TryGetProperty("identity", out var idElem))
                    {
                        if (idElem.TryGetProperty("vendor_id", out var vidProp)) vid = vidProp.GetInt32();
                        if (idElem.TryGetProperty("product_id", out var pidProp)) pid = pidProp.GetInt32();
                        if (idElem.TryGetProperty("chip_id", out var chipProp)) chip = chipProp.GetInt32();
                        if (idElem.TryGetProperty("is_transmitter", out var txProp)) isTx = txProp.GetBoolean();
                        if (idElem.TryGetProperty("is_receiver", out var rxProp)) isRx = rxProp.GetBoolean();
                    }

                    // IP Address from nodes
                    string ip = "";
                    if (d.TryGetProperty("nodes", out var nodesElem))
                    {
                        foreach (var node in nodesElem.EnumerateArray())
                        {
                            if (node.TryGetProperty("status", out var nodeStatus) &&
                                nodeStatus.TryGetProperty("ip", out var ipObj) &&
                                ipObj.TryGetProperty("address", out var ipAddr))
                            {
                                ip = ipAddr.GetString() ?? "";
                                if (!string.IsNullOrEmpty(ip)) break;
                            }
                        }
                    }

                    list.Add(new AvasDevice
                    {
                        DeviceId = mac,
                        DeviceName = name,
                        IpAddress = ip,
                        VendorId = vid,
                        ProductId = pid,
                        ChipId = chip,
                        IsTransmitter = isTx,
                        IsReceiver = isRx,
                        IsActive = isActive
                    });
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("PARSER", "Error parsing Semtech devices JSON", ex);
            }

            return list;
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
