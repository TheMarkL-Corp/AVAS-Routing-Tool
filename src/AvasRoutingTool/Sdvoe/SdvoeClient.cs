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
        private readonly HttpClient _httpClient;
        private CancellationTokenSource? _readCts;
        private TaskCompletionSource<string>? _pendingCommandTcs;
        private string _host = "127.0.0.1";
        private int _telnetPort = 6970;
        private int _restPort = 8090;
        private bool _isDisposed;

        public SdvoeClient() : this(null)
        {
        }

        /// <summary>Test seam: lets tests observe REST traffic without a Control Server.</summary>
        public SdvoeClient(HttpMessageHandler? handler)
        {
            _httpClient = handler == null ? new HttpClient() : new HttpClient(handler);
            _httpClient.Timeout = TimeSpan.FromSeconds(5);
        }

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
                    return RestResponse.IsAccepted(body);
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

        public async Task<bool> CreateMultiViewLayoutAsync(MultiView.MultiViewLayoutPlan plan)
        {
            AppLogger.Info("MULTIVIEW", $"Creating layout '{plan.Name}' ({plan.Width}x{plan.Height}, {plan.Slots.Count} windows)");

            if (!await SendRestCallAsync(MultiView.MultiViewRequests.CreateLayout(plan))) return false;
            foreach (var slot in plan.Slots)
            {
                if (!await SendRestCallAsync(MultiView.MultiViewRequests.CreateWindow(plan, slot))) return false;
            }
            return true;
        }

        public async Task<bool> PrepareSourceScalerAsync(string txMac, int width, int height)
        {
            AppLogger.Info("MULTIVIEW", $"Preparing {NormalizeMac(txMac)} stream 1 at {width}x{height}");

            if (!await SendRestCallAsync(MultiView.MultiViewRequests.SetScaler(txMac, width, height))) return false;
            return await SendRestCallAsync(MultiView.MultiViewRequests.StartScaledStream(txMac));
        }

        public Task<bool> SetMultiViewAsync(string rxMac, MultiView.MultiViewLayoutPlan plan, int fps,
            IReadOnlyList<MultiView.MultiViewSubscription> subscriptions)
        {
            AppLogger.Info("MULTIVIEW", $"Loading '{plan.Name}' on {NormalizeMac(rxMac)} with {subscriptions.Count} subscription(s)");
            return SendRestCallAsync(MultiView.MultiViewRequests.SetMultiView(rxMac, plan.Name, plan.Width, plan.Height, fps, subscriptions));
        }

        public async Task<RxState?> ReadReceiverStateAsync(string rxMac)
        {
            string norm = NormalizeMac(rxMac);
            var settings = await RequestAsync("POST", $"/api/device/{norm}", new { op = "get", subset = "settings" });
            if (!settings.Ok || settings.Body == null)
            {
                AppLogger.Warn("RESTORE", $"Could not read the state of {norm}: {settings.Failure ?? "no reply"}");
                return null;
            }

            var state = RxStateReader.Parse(settings.Body);
            if (state == null || !state.IsMultiView) return state;

            // The layout name is only available through the multiview subset; if it is refused the state is still useful.
            var layout = await RequestAsync("POST", $"/api/device/{norm}", new { op = "get", subset = "multiview" });
            return layout.Body == null ? state : RxStateReader.Parse(settings.Body, layout.Body);
        }

        public async Task<bool?> ReadStream1FrameDividerAsync(string txMac)
        {
            string norm = NormalizeMac(txMac);
            var result = await RequestAsync("POST", $"/api/device/{norm}", new { op = "get", subset = "settings" });
            if (!result.Ok || result.Body == null) return null;

            try
            {
                using var doc = JsonDocument.Parse(result.Body);
                foreach (var device in doc.RootElement.GetProperty("result").GetProperty("devices").EnumerateArray())
                {
                    if (!device.TryGetProperty("nodes", out var nodes)) continue;
                    foreach (var node in nodes.EnumerateArray())
                    {
                        if (!node.TryGetProperty("type", out var type) || type.GetString() != "SCALER") continue;
                        if (!node.TryGetProperty("inputs", out var inputs)) continue;
                        foreach (var input in inputs.EnumerateArray())
                        {
                            if (input.TryGetProperty("configuration", out var cfg) &&
                                cfg.TryGetProperty("source", out var src) &&
                                src.TryGetProperty("value", out var value) &&
                                value.ValueKind == JsonValueKind.Number)
                            {
                                return value.GetInt32() == 1;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                AppLogger.Warn("MULTIVIEW", $"Unexpected settings reply from {norm}: {ex.Message}");
            }
            return null;
        }

        public Task<bool> SetStream1FrameDividerAsync(string txMac, bool halve)
        {
            AppLogger.Info("MULTIVIEW", $"{NormalizeMac(txMac)} stream 1 frame divider {(halve ? "ON (half rate)" : "OFF (full rate)")}");
            return SendRestCallAsync(MultiView.MultiViewRequests.SetScalerFrameDivider(txMac, halve));
        }

        public Task<bool> SetGenlockedAsync(string rxMac)
        {
            AppLogger.Info("MULTIVIEW", $"Returning {NormalizeMac(rxMac)} to genlocked single view");
            return SendRestCallAsync(MultiView.MultiViewRequests.SetGenlocked(rxMac));
        }

        /// <summary>How long a background request may stay PROCESSING before we give up on it.</summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(8);

        public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(200);

        public async Task<string?> ReadDisplayEdidAsync(string rxMac)
        {
            string norm = NormalizeMac(rxMac);
            var result = await RequestAsync("POST", $"/api/device/{norm}", new { op = "get", subset = "edid" });
            if (!result.Ok || result.Body == null)
            {
                AppLogger.Warn("EDID", $"Could not read the display EDID from {norm}: {result.Failure ?? "no reply"}");
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(result.Body);
                foreach (var device in doc.RootElement.GetProperty("result").GetProperty("devices").EnumerateArray())
                {
                    if (!device.TryGetProperty("nodes", out var nodes)) continue;
                    foreach (var node in nodes.EnumerateArray())
                    {
                        if (!node.TryGetProperty("type", out var type) || type.GetString() != "HDMI_MONITOR") continue;
                        if (!node.TryGetProperty("status", out var status)) continue;

                        bool connected = status.TryGetProperty("connected", out var c) && c.ValueKind == JsonValueKind.True;
                        if (!connected)
                        {
                            AppLogger.Info("EDID", $"{norm}: no display connected.");
                            return null;
                        }
                        return status.TryGetProperty("edid", out var edid) ? edid.GetString() : null;
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                AppLogger.Warn("EDID", $"Unexpected EDID reply from {norm}: {ex.Message}");
            }
            return null;
        }

        private async Task<bool> SendRestCallAsync(MultiView.RestCall call)
        {
            var result = await RequestAsync(call.Method, call.Path, call.Body);
            if (!result.Ok)
                AppLogger.Warn("MULTIVIEW", $"{call.Method} {call.Path} failed: {result.Failure ?? "no reply"}");
            return result.Ok;
        }

        private sealed record RequestResult(bool Ok, string? Body, string? Failure);

        /// <summary>
        /// Sends a call and, if the server answers PROCESSING, follows /api/request/{id} until the request
        /// really finishes, so a device that refuses the command later is reported as a failure.
        /// </summary>
        private async Task<RequestResult> RequestAsync(string method, string path, object? payload)
        {
            try
            {
                var (httpOk, body) = await SendRawAsync(method, path, payload);
                var inspection = RestResponse.Inspect(body);
                if (inspection == null) return new RequestResult(false, body, httpOk ? "unreadable reply" : "HTTP error");

                if (string.Equals(inspection.Status, "PROCESSING", StringComparison.OrdinalIgnoreCase) && inspection.RequestId is long id)
                {
                    var deadline = DateTime.UtcNow + RequestTimeout;
                    while (string.Equals(inspection.Status, "PROCESSING", StringComparison.OrdinalIgnoreCase))
                    {
                        if (DateTime.UtcNow >= deadline)
                            return new RequestResult(false, body, $"request {id} still PROCESSING after {RequestTimeout.TotalSeconds:0.#}s");

                        await Task.Delay(PollInterval);
                        (httpOk, body) = await SendRawAsync("GET", $"/api/request/{id}", null);
                        inspection = RestResponse.Inspect(body);
                        if (inspection == null) return new RequestResult(false, body, "unreadable request reply");
                    }
                }

                bool ok = string.Equals(inspection.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase) && inspection.Failure == null;
                return new RequestResult(ok, body, inspection.Failure ?? (ok ? null : inspection.Status));
            }
            catch (Exception ex)
            {
                return new RequestResult(false, null, ex.Message);
            }
        }

        private async Task<(bool HttpOk, string Body)> SendRawAsync(string method, string path, object? payload)
        {
            string url = $"http://{_host}:{_restPort}{path}";
            using var request = new HttpRequestMessage(new HttpMethod(method), url);
            if (payload != null)
            {
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            }
            using var resp = await _httpClient.SendAsync(request);
            string body = await resp.Content.ReadAsStringAsync();
            AppLogger.Debug("REST", $"{method} {path} -> HTTP {(int)resp.StatusCode}");
            return (resp.IsSuccessStatusCode, body);
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

