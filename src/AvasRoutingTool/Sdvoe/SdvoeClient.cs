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
using AvasRoutingTool.Rtp;

namespace AvasRoutingTool.Sdvoe
{
    public class SdvoeClient : ISdvoeClient
    {
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private readonly SemaphoreSlim _commandLock = new(1, 1);
        private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };
        private CancellationTokenSource? _readCts;
        private TaskCompletionSource<string>? _pendingCommandTcs;
        private string _host = "127.0.0.1";
        private int _telnetPort = 6970;
        private int _restPort = 8090;
        private bool _isDisposed;

        public bool IsConnected => _tcpClient != null && _tcpClient.Connected;
        public event Action<string, bool>? DeviceStateChanged;

        public static string NormalizeMac(string mac)
        {
            if (string.IsNullOrWhiteSpace(mac)) return string.Empty;
            return mac.Replace(":", "").Replace("-", "").Trim().ToLowerInvariant();
        }

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

        private async Task<bool> SendRestPostAsync(string path, object payload)
        {
            try
            {
                string url = $"http://{_host}:{_restPort}{path}";
                string json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var resp = await _httpClient.PostAsync(url, content);
                if (resp.IsSuccessStatusCode)
                {
                    string body = await resp.Content.ReadAsStringAsync();
                    AppLogger.Info("REST", $"POST {path} response: {body}");
                    return body.Contains("\"status\":\"SUCCESS\"", StringComparison.OrdinalIgnoreCase) ||
                           body.Contains("\"status\":\"PROCESSING\"", StringComparison.OrdinalIgnoreCase) ||
                           body.Contains("\"send_rs232\"", StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    AppLogger.Warn("REST", $"POST {path} returned HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("REST", $"Failed POST {path}: {ex.Message}");
            }
            return false;
        }

        public async Task<bool> EnsureUartApiModeAsync(string mac)
        {
            string normMac = NormalizeMac(mac);
            var payload = new
            {
                stream_type = "rs232",
                stream_index = 1,
                op = "switch:api"
            };
            return await SendRestPostAsync($"/api/device/{normMac}", payload);
        }

        public async Task<bool> StartAvasPreviewAsync(string mac, string multicastIp)
        {
            string normMac = NormalizeMac(mac);
            AppLogger.Info("PREVIEW", $"Starting AVAS preview stream on {normMac} -> {multicastIp}:5000");

            await EnsureUartApiModeAsync(normMac);

            var igmpPayload = new
            {
                op = "send:rs232",
                port_index = 1,
                data_string = $"set rtp igmp {multicastIp}\r\n"
            };
            await SendRestPostAsync($"/api/device/{normMac}", igmpPayload);

            var onPayload = new
            {
                op = "send:rs232",
                port_index = 1,
                data_string = "set rtp ON\r\n"
            };
            return await SendRestPostAsync($"/api/device/{normMac}", onPayload);
        }

        public async Task<bool> StopAvasPreviewAsync(string mac)
        {
            string normMac = NormalizeMac(mac);
            AppLogger.Info("PREVIEW", $"Stopping AVAS preview stream on {normMac}");

            var offPayload = new
            {
                op = "send:rs232",
                port_index = 1,
                data_string = "set rtp OFF\r\n"
            };
            return await SendRestPostAsync($"/api/device/{normMac}", offPayload);
        }

        public Task<bool> StartStream1ScalerAsync(string txMac, string multicastIp, int port = 5004)
        {
            return StartAvasPreviewAsync(txMac, multicastIp);
        }

        public Task<bool> StopStream1ScalerAsync(string txMac)
        {
            return StopAvasPreviewAsync(txMac);
        }

        public async Task<bool> JoinStreamAsync(string rxMac, string txMac, string streamType = "HDMI", int streamIndex = 0)
        {
            if (string.IsNullOrWhiteSpace(rxMac) || string.IsNullOrWhiteSpace(txMac)) return false;

            string normRx = NormalizeMac(rxMac);
            string normTx = NormalizeMac(txMac);

            AppLogger.Info("ROUTING", $"Executing video route: {normTx} -> {normRx} ({streamType}:{streamIndex})");

            // 1. Ensure transmitter stream is running
            var startPayload = new
            {
                op = "start",
                stream_type = streamType,
                stream_index = streamIndex
            };
            await SendRestPostAsync($"/api/device/{normTx}", startPayload);

            // 2. Route receiver to transmitter
            var joinPayload = new
            {
                op = "join",
                source_device = normTx,
                stream_type = streamType,
                stream_index = streamIndex,
                subscription_index = 0
            };
            bool success = await SendRestPostAsync($"/api/device/{normRx}", joinPayload);

            if (!success && IsConnected)
            {
                // Telnet fallback
                string cmd = $"join {normRx}:{streamType}:{streamIndex} {normTx}:{streamType}:{streamIndex}";
                string response = await SendCommandAsync(cmd);
                success = response.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase);
            }

            return success;
        }

        public async Task<bool> LeaveStreamAsync(string rxMac, string streamType = "HDMI", int streamIndex = 0)
        {
            if (string.IsNullOrWhiteSpace(rxMac)) return false;

            string normRx = NormalizeMac(rxMac);
            AppLogger.Info("ROUTING", $"Unrouting receiver {normRx} ({streamType}:{streamIndex})");

            var leavePayload = new
            {
                op = "leave",
                subscription_type = streamType,
                subscription_index = streamIndex
            };
            bool success = await SendRestPostAsync($"/api/device/{normRx}", leavePayload);

            if (!success && IsConnected)
            {
                string cmd = $"leave {normRx}:{streamType}:{streamIndex}";
                string response = await SendCommandAsync(cmd);
                success = response.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase);
            }

            return success;
        }

        public async Task<List<AvasDevice>> DiscoverDevicesAsync()
        {
            var list = new List<AvasDevice>();

            // 1. Query via Telnet
            if (IsConnected)
            {
                AppLogger.Info("DISCOVERY", "Querying devices via Telnet: 'get all identity'...");
                string telnetJson = await SendCommandAsync("get all identity");
                if (!string.IsNullOrWhiteSpace(telnetJson))
                {
                    list = ParseDevicesJson(telnetJson);
                    if (list.Count > 0)
                    {
                        RegisterTransmitterAliases(list);
                        AppLogger.Info("DISCOVERY", $"Telnet discovery found {list.Count} devices: {string.Join(", ", list.ConvertAll(d => $"{d.DeviceName} ({d.DeviceId}, IP:{d.IpAddress}, Link:{d.LinkMode})"))}");
                        return list;
                    }
                }
            }

            // 2. Query via REST
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
                    RegisterTransmitterAliases(list);
                    AppLogger.Info("DISCOVERY", $"REST discovery found {list.Count} devices: {string.Join(", ", list.ConvertAll(d => $"{d.DeviceName} ({d.DeviceId}, IP:{d.IpAddress}, Link:{d.LinkMode})"))}");
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

        private static void RegisterTransmitterAliases(List<AvasDevice> devices)
        {
            foreach (var dev in devices)
            {
                if (dev.IsTransmitter && !string.IsNullOrWhiteSpace(dev.IpAddress))
                {
                    foreach (var nodeIp in dev.NodeIps)
                    {
                        if (!nodeIp.Equals(dev.IpAddress, StringComparison.OrdinalIgnoreCase))
                        {
                            UnifiedRtpDemuxReceiver.Instance.RegisterAlias(nodeIp, dev.IpAddress);
                            AppLogger.Info("DISCOVERY", $"Registered RTP alias: {nodeIp} -> {dev.IpAddress} ({dev.DeviceName})");
                        }
                    }
                }
            }
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

                    string name = d.TryGetProperty("device_name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(name) && d.TryGetProperty("configuration", out var confElem) &&
                        confElem.TryGetProperty("device_name", out var confName))
                    {
                        name = confName.GetString() ?? "";
                    }
                    if (string.IsNullOrEmpty(name)) name = mac;

                    bool isActive = true;
                    if (d.TryGetProperty("status", out var statusElem) && statusElem.TryGetProperty("active", out var activeProp))
                    {
                        isActive = activeProp.GetBoolean();
                    }

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

                    string primaryIp = "";
                    var nodeIps = new List<string>();
                    string detectedLinkMode = "Single Link";

                    if (d.TryGetProperty("nodes", out var nodesElem))
                    {
                        foreach (var node in nodesElem.EnumerateArray())
                        {
                            if (node.TryGetProperty("status", out var nodeStatus) &&
                                nodeStatus.TryGetProperty("ip", out var ipObj) &&
                                ipObj.TryGetProperty("address", out var ipAddr))
                            {
                                string addr = ipAddr.GetString() ?? "";
                                if (!string.IsNullOrEmpty(addr))
                                {
                                    if (string.IsNullOrEmpty(primaryIp)) primaryIp = addr;
                                    if (!nodeIps.Contains(addr)) nodeIps.Add(addr);
                                }
                            }

                            if (node.TryGetProperty("type", out var typeProp) &&
                                string.Equals(typeProp.GetString(), "MULTI_LINK_TRANSMITTER", StringComparison.OrdinalIgnoreCase))
                            {
                                if (node.TryGetProperty("configuration", out var cfgElem) &&
                                    cfgElem.TryGetProperty("link_mode", out var lmProp))
                                {
                                    string lm = lmProp.GetString() ?? "";
                                    if (lm.Equals("DUAL", StringComparison.OrdinalIgnoreCase))
                                    {
                                        detectedLinkMode = "Dual Link";
                                    }
                                }
                            }
                        }
                    }

                    if (name.Contains("DUAL", StringComparison.OrdinalIgnoreCase))
                    {
                        detectedLinkMode = "Dual Link";
                    }

                    if (!isTx || vid != 105)
                    {
                        detectedLinkMode = "N/A";
                    }

                    list.Add(new AvasDevice
                    {
                        DeviceId = mac,
                        DeviceName = name,
                        IpAddress = primaryIp,
                        VendorId = vid,
                        ProductId = pid,
                        ChipId = chip,
                        IsTransmitter = isTx,
                        IsReceiver = isRx,
                        IsActive = isActive,
                        LinkMode = detectedLinkMode,
                        NodeIps = nodeIps
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
