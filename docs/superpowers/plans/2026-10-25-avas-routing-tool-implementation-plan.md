# AVAS Routing Tool Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Recreate the BlueRiver AV Manager (BAVM) with a superior visual drag-and-drop routing UI/UX and live video previews for Advantech AVAS-223 transceivers, while preserving 100% of BAVM functionality intact via an embedded WebView2 console.

**Architecture:** A native C# .NET 8 WPF desktop application featuring a 3-tier visual canvas (Sources top row, Displays middle row, Layouts bottom row) powered by a concurrent UDP 5000/5004 RFC 4175 RTP demux receiver, alongside an isolated Microsoft WebView2 tab embedding the official BAVM web management interface.

**Tech Stack:** C# .NET 8 (`net8.0-windows`), WPF, Microsoft.Web.WebView2, System.Net.Sockets, xUnit, FluentAssertions, Moq.

**Spec:** [`docs/superpowers/specs/2026-10-25-avas-routing-tool-design.md`](file:///c:/Users/POC-615/Documents/GitHub/AVAS-Routing-Tool/docs/superpowers/specs/2026-10-25-avas-routing-tool-design.md)

---

## Global Constraints

* **Platform Floor**: Windows 10/11 x64, .NET 8.0 SDK installed.
* **Target Hardware**: Advantech AVAS-223 (Vendor ID: 105, Product ID: 81) with dual AVP2000 chips.
* **Control Server Interface**: TCP 6970 JSON-RPC / Telnet (`require api 3.0.0.0`) and REST API on port 8080/9200.
* **Multicast Pool**: Strictly dynamic Class D allocation between `225.0.0.1` and `225.0.1.254`.
* **Zero Memory Leaks**: Direct `WriteableBitmap.BackBuffer` updating without GDI+ allocations; deterministic socket teardown via `0.0.0.0` control nullification and `DropMulticastGroup`.
* **Memory Integrity**: All changes must be tracked in `memory/active_context.md` and `memory/progress.md`.

## Review Focus

1. **WSAEADDRINUSE (Error 10048)**: Rapidly cycling preview streams must never throw port collision exceptions because all UDP sockets explicitly set `SocketOptionName.ReuseAddress = true`.
2. **Switch Multicast Flooding**: Stopping or unrouting a stream must issue both the control nullification (`set <mac> stream HDMI 1 enable false address 0.0.0.0`) and the host IGMP drop (`DropMulticastGroup`).
3. **UI Thread Affinity**: All socket and notification callbacks must marshal property changes to the UI thread via `Dispatcher` to prevent `InvalidOperationException`.
4. **Drag-and-Drop Reliability**: Dropping a Source TX onto a Display RX must execute Video, Audio, and USB HID routes atomically and reflect active connection state visually.
5. **BAVM WebView2 Isolation**: The embedded BAVM browser tab must utilize an isolated local `WebView2_UserData` folder and gracefully handle offline states.

---

## File Structure

```
AVAS-Routing-Tool/
├── AvasRoutingTool.sln
├── src/
│   └── AvasRoutingTool/
│       ├── AvasRoutingTool.csproj
│       ├── App.xaml / App.xaml.cs
│       ├── MainWindow.xaml / MainWindow.xaml.cs
│       ├── Configuration/
│       │   ├── AppConfig.cs
│       │   └── ConfigService.cs
│       ├── Sdvoe/
│       │   ├── AvasDevice.cs
│       │   ├── MulticastIpManager.cs
│       │   ├── SdvoeClient.cs
│       │   └── RoutingOrchestrator.cs
│       ├── Rtp/
│       │   ├── Yuv422Rasterizer.cs
│       │   └── UnifiedRtpDemuxReceiver.cs
│       ├── ViewModels/
│       │   ├── ViewModelBase.cs
│       │   ├── MainViewModel.cs
│       │   ├── SourceCardViewModel.cs
│       │   ├── DisplayCardViewModel.cs
│       │   └── LayoutItemViewModel.cs
│       ├── Views/
│       │   ├── VisualRoutingView.xaml / VisualRoutingView.xaml.cs
│       │   ├── SourceCardView.xaml / SourceCardView.xaml.cs
│       │   ├── DisplayCardView.xaml / DisplayCardView.xaml.cs
│       │   ├── LayoutsBarView.xaml / LayoutsBarView.xaml.cs
│       │   └── SettingsDialog.xaml / SettingsDialog.xaml.cs
│       └── Resources/
│           └── Icons.xaml
└── tests/
    └── AvasRoutingTool.Tests/
        ├── AvasRoutingTool.Tests.csproj
        ├── ConfigServiceTests.cs
        ├── MulticastIpManagerTests.cs
        ├── SdvoeClientTests.cs
        ├── RoutingOrchestratorTests.cs
        ├── Yuv422RasterizerTests.cs
        └── UnifiedRtpDemuxReceiverTests.cs
```

---

### Task 1: Scaffold Solution & Test Infrastructure

**Files:**
- Create: `src/AvasRoutingTool/AvasRoutingTool.csproj`
- Create: `tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj`
- Create: `AvasRoutingTool.sln`
- Create: `src/AvasRoutingTool/Configuration/AppConfig.cs`
- Create: `src/AvasRoutingTool/Configuration/ConfigService.cs`
- Test: `tests/AvasRoutingTool.Tests/ConfigServiceTests.cs`

**Interfaces:**
- Produces: `AppConfig`, `IConfigService` (methods: `Load()`, `Save(AppConfig)`)

- [ ] **Step 1: Write the failing test for ConfigService**

```csharp
// tests/AvasRoutingTool.Tests/ConfigServiceTests.cs
using System.IO;
using AvasRoutingTool.Configuration;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class ConfigServiceTests
    {
        [Fact]
        public void Load_WhenFileDoesNotExist_ReturnsDefaultConfig()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"cfg_test_{System.Guid.NewGuid()}.json");
            try
            {
                var service = new ConfigService(tempFile);
                var config = service.Load();

                Assert.NotNull(config);
                Assert.Equal("127.0.0.1", config.ControlServerIp);
                Assert.Equal(6970, config.TelnetPort);
                Assert.Equal(8080, config.RestPort);
                Assert.Equal("225.0.0.1", config.MulticastStartIp);
                Assert.Equal("225.0.1.254", config.MulticastEndIp);
                Assert.Equal("http://localhost:80", config.BavmUrl);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void Save_PersistsChangesAndLoadsBack()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"cfg_test_{System.Guid.NewGuid()}.json");
            try
            {
                var service = new ConfigService(tempFile);
                var config = new AppConfig { ControlServerIp = "10.32.32.100", RestPort = 9200 };
                service.Save(config);

                var loaded = service.Load();
                Assert.Equal("10.32.32.100", loaded.ControlServerIp);
                Assert.Equal(9200, loaded.RestPort);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }
    }
}
```

- [ ] **Step 2: Scaffold .NET solution, projects, and run test to verify failure**

Run:
```powershell
dotnet new sln -n AvasRoutingTool
dotnet new wpf -n AvasRoutingTool -o src/AvasRoutingTool -f net8.0-windows
dotnet new xunit -n AvasRoutingTool.Tests -o tests/AvasRoutingTool.Tests -f net8.0-windows
dotnet sln add src/AvasRoutingTool/AvasRoutingTool.csproj
dotnet sln add tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj
dotnet add tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj reference src/AvasRoutingTool/AvasRoutingTool.csproj
dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj
```
Expected: FAIL with missing `ConfigService` / `AppConfig`.

- [ ] **Step 3: Implement AppConfig and ConfigService**

```csharp
// src/AvasRoutingTool/Configuration/AppConfig.cs
namespace AvasRoutingTool.Configuration
{
    public class AppConfig
    {
        public string ControlServerIp { get; set; } = "127.0.0.1";
        public int TelnetPort { get; set; } = 6970;
        public int RestPort { get; set; } = 8080;
        public string MulticastStartIp { get; set; } = "225.0.0.1";
        public string MulticastEndIp { get; set; } = "225.0.1.254";
        public int BasePort { get; set; } = 5004;
        public string LocalNetworkInterfaceIp { get; set; } = "";
        public string BavmUrl { get; set; } = "http://localhost:80";
        public string Theme { get; set; } = "Dark";
    }
}
```

```csharp
// src/AvasRoutingTool/Configuration/ConfigService.cs
using System;
using System.IO;
using System.Text.Json;

namespace AvasRoutingTool.Configuration
{
    public interface IConfigService
    {
        AppConfig Load();
        void Save(AppConfig config);
    }

    public class ConfigService : IConfigService
    {
        private readonly string _filePath;
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public ConfigService(string? filePath = null)
        {
            _filePath = filePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
        }

        public AppConfig Load()
        {
            if (!File.Exists(_filePath))
            {
                var defaults = new AppConfig();
                Save(defaults);
                return defaults;
            }

            try
            {
                string json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
            }
            catch
            {
                return new AppConfig();
            }
        }

        public void Save(AppConfig config)
        {
            string json = JsonSerializer.Serialize(config, JsonOptions);
            string? dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(_filePath, json);
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj`  
Expected: PASS (2 passed).

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat(scaffold): initialize .NET 8 solution and implement ConfigService"
```

---

### Task 2: Multicast IP Pool Manager & SDVoE Device Model

**Files:**
- Create: `src/AvasRoutingTool/Sdvoe/AvasDevice.cs`
- Create: `src/AvasRoutingTool/Sdvoe/MulticastIpManager.cs`
- Test: `tests/AvasRoutingTool.Tests/MulticastIpManagerTests.cs`

**Interfaces:**
- Produces: `AvasDevice`, `MulticastIpManager` (`AllocateIp(string mac)`, `ReleaseIp(string mac)`)

- [ ] **Step 1: Write failing tests for MulticastIpManager**

```csharp
// tests/AvasRoutingTool.Tests/MulticastIpManagerTests.cs
using AvasRoutingTool.Sdvoe;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class MulticastIpManagerTests
    {
        [Fact]
        public void AllocateIp_AssignsSequentialUniqueIps()
        {
            var manager = new MulticastIpManager("225.0.0.1", "225.0.0.3");
            string ip1 = manager.AllocateIp("00:11:22:33:44:01");
            string ip2 = manager.AllocateIp("00:11:22:33:44:02");
            string ip3 = manager.AllocateIp("00:11:22:33:44:03");

            Assert.Equal("225.0.0.1", ip1);
            Assert.Equal("225.0.0.2", ip2);
            Assert.Equal("225.0.0.3", ip3);
        }

        [Fact]
        public void AllocateIp_WhenSameMacReAllocates_ReturnsSameIp()
        {
            var manager = new MulticastIpManager("225.0.0.1", "225.0.0.5");
            string ip1 = manager.AllocateIp("00:11:22:33:44:01");
            string ip2 = manager.AllocateIp("00:11:22:33:44:01");

            Assert.Equal(ip1, ip2);
        }

        [Fact]
        public void ReleaseIp_RecyclesIpForNextDevice()
        {
            var manager = new MulticastIpManager("225.0.0.1", "225.0.0.1");
            string ip1 = manager.AllocateIp("00:11:22:33:44:01");
            manager.ReleaseIp("00:11:22:33:44:01");

            string ip2 = manager.AllocateIp("00:11:22:33:44:02");
            Assert.Equal(ip1, ip2);
        }
    }
}
```

- [ ] **Step 2: Run test to verify failure**

Run: `dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj`  
Expected: FAIL with "MulticastIpManager does not exist".

- [ ] **Step 3: Implement AvasDevice and MulticastIpManager**

```csharp
// src/AvasRoutingTool/Sdvoe/AvasDevice.cs
using System;

namespace AvasRoutingTool.Sdvoe
{
    public class AvasDevice
    {
        public string DeviceId { get; set; } = string.Empty; // MAC address
        public string DeviceName { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string Model { get; set; } = "AVAS-223";
        public int VendorId { get; set; } = 105;
        public int ProductId { get; set; } = 81;
        public int ChipId { get; set; } = 0;
        public bool IsTransmitter { get; set; }
        public bool IsReceiver { get; set; }
        public bool IsActive { get; set; } = true;
        public string ActiveSourceId { get; set; } = string.Empty; // For RX: currently routed TX MAC
        public string ActiveSourceName { get; set; } = string.Empty;
        public string MulticastIp { get; set; } = string.Empty;
        public int MulticastPort { get; set; } = 5004;
        public double FramesPerSecond { get; set; }
        public string Resolution { get; set; } = "1080p60";
        public bool HasAudio { get; set; } = true;
        public bool HasUsb { get; set; } = true;
    }
}
```

```csharp
// src/AvasRoutingTool/Sdvoe/MulticastIpManager.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;

namespace AvasRoutingTool.Sdvoe
{
    public class MulticastIpManager
    {
        private readonly uint _startIp;
        private readonly uint _endIp;
        private readonly ConcurrentDictionary<string, uint> _assignedIps = new(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<uint> _freeIps = new();
        private readonly object _lock = new();
        private uint _nextIp;

        public MulticastIpManager(string startIp = "225.0.0.1", string endIp = "225.0.1.254")
        {
            _startIp = IpToUint(IPAddress.Parse(startIp));
            _endIp = IpToUint(IPAddress.Parse(endIp));
            _nextIp = _startIp;
        }

        public string AllocateIp(string macAddress)
        {
            lock (_lock)
            {
                if (_assignedIps.TryGetValue(macAddress, out uint existing))
                {
                    return UintToIp(existing);
                }

                uint allocated;
                if (_freeIps.Count > 0)
                {
                    allocated = _freeIps.Dequeue();
                }
                else if (_nextIp <= _endIp)
                {
                    allocated = _nextIp++;
                }
                else
                {
                    throw new InvalidOperationException("Multicast IP pool exhausted.");
                }

                _assignedIps[macAddress] = allocated;
                return UintToIp(allocated);
            }
        }

        public void ReleaseIp(string macAddress)
        {
            lock (_lock)
            {
                if (_assignedIps.TryRemove(macAddress, out uint ip))
                {
                    _freeIps.Enqueue(ip);
                }
            }
        }

        private static uint IpToUint(IPAddress ip)
        {
            byte[] bytes = ip.GetAddressBytes();
            Array.Reverse(bytes);
            return BitConverter.ToUInt32(bytes, 0);
        }

        private static string UintToIp(uint value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Array.Reverse(bytes);
            return new IPAddress(bytes).ToString();
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj`  
Expected: PASS (all tests pass).

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat(sdvoe): implement AvasDevice model and MulticastIpManager"
```

---

### Task 3: Color-Space Rasterizer & RTP Packet Demuxer

**Files:**
- Create: `src/AvasRoutingTool/Rtp/Yuv422Rasterizer.cs`
- Create: `src/AvasRoutingTool/Rtp/UnifiedRtpDemuxReceiver.cs`
- Test: `tests/AvasRoutingTool.Tests/Yuv422RasterizerTests.cs`

**Interfaces:**
- Produces: `Yuv422Rasterizer.ConvertYuv422SliceToBgra(...)`, `UnifiedRtpDemuxReceiver` (`RegisterChannel`, `UnregisterChannel`, `StartListening`, `StopListening`)

- [ ] **Step 1: Write failing test for Yuv422Rasterizer**

```csharp
// tests/AvasRoutingTool.Tests/Yuv422RasterizerTests.cs
using AvasRoutingTool.Rtp;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class Yuv422RasterizerTests
    {
        [Fact]
        public void ConvertYuv422SliceToBgra_DecodesBlackMacroPixelCorrectly()
        {
            // Y0=16 (black), U=128 (neutral), Y1=16 (black), V=128 (neutral)
            byte[] src = new byte[] { 16, 128, 16, 128 };
            byte[] dest = new byte[8]; // 2 pixels in BGRA (4 bytes each)

            Yuv422Rasterizer.ConvertYuv422SliceToBgra(src, 0, 4, dest, 0);

            // Black in BGRA: B=0, G=0, R=0, A=255
            Assert.Equal(0, dest[0]); // B
            Assert.Equal(0, dest[1]); // G
            Assert.Equal(0, dest[2]); // R
            Assert.Equal(255, dest[3]); // A

            Assert.Equal(0, dest[4]); // B
            Assert.Equal(0, dest[5]); // G
            Assert.Equal(0, dest[6]); // R
            Assert.Equal(255, dest[7]); // A
        }
    }
}
```

- [ ] **Step 2: Run test to verify failure**

Run: `dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj`  
Expected: FAIL with "Yuv422Rasterizer does not exist".

- [ ] **Step 3: Implement Yuv422Rasterizer and UnifiedRtpDemuxReceiver**

```csharp
// src/AvasRoutingTool/Rtp/Yuv422Rasterizer.cs
using System;

namespace AvasRoutingTool.Rtp
{
    public static class Yuv422Rasterizer
    {
        public static void ConvertYuv422SliceToBgra(byte[] src, int srcOffset, int srcLength, byte[] dest, int destOffset)
        {
            for (int i = 0; i <= srcLength - 4; i += 4)
            {
                byte y0 = src[srcOffset + i];
                byte u  = src[srcOffset + i + 1];
                byte y1 = src[srcOffset + i + 2];
                byte v  = src[srcOffset + i + 3];

                int c0 = y0 - 16;
                int d  = u - 128;
                int c1 = y1 - 16;
                int e  = v - 128;

                // Pixel 0
                int r0 = Math.Clamp((298 * c0 + 409 * e + 128) >> 8, 0, 255);
                int g0 = Math.Clamp((298 * c0 - 100 * d - 208 * e + 128) >> 8, 0, 255);
                int b0 = Math.Clamp((298 * c0 + 516 * d + 128) >> 8, 0, 255);

                int p0 = destOffset + (i * 2);
                if (p0 + 7 < dest.Length)
                {
                    dest[p0]     = (byte)b0;
                    dest[p0 + 1] = (byte)g0;
                    dest[p0 + 2] = (byte)r0;
                    dest[p0 + 3] = 255;

                    // Pixel 1
                    int r1 = Math.Clamp((298 * c1 + 409 * e + 128) >> 8, 0, 255);
                    int g1 = Math.Clamp((298 * c1 - 100 * d - 208 * e + 128) >> 8, 0, 255);
                    int b1 = Math.Clamp((298 * c1 + 516 * d + 128) >> 8, 0, 255);

                    dest[p0 + 4] = (byte)b1;
                    dest[p0 + 5] = (byte)g1;
                    dest[p0 + 6] = (byte)r1;
                    dest[p0 + 7] = 255;
                }
            }
        }
    }
}
```

```csharp
// src/AvasRoutingTool/Rtp/UnifiedRtpDemuxReceiver.cs
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace AvasRoutingTool.Rtp
{
    public class UnifiedRtpDemuxReceiver : IDisposable
    {
        private static readonly Lazy<UnifiedRtpDemuxReceiver> Lazy = new(() => new UnifiedRtpDemuxReceiver());
        public static UnifiedRtpDemuxReceiver Instance => Lazy.Value;

        private readonly ConcurrentDictionary<string, Action<byte[], int, int>> _channels = new(StringComparer.OrdinalIgnoreCase);
        private UdpClient? _udpClient;
        private CancellationTokenSource? _cts;
        private bool _isListening;

        public void StartListening(string localAdapterIp, int port = 5004)
        {
            if (_isListening) return;

            _cts = new CancellationTokenSource();
            var localEp = string.IsNullOrEmpty(localAdapterIp) 
                ? new IPEndPoint(IPAddress.Any, port) 
                : new IPEndPoint(IPAddress.Parse(localAdapterIp), port);

            _udpClient = new UdpClient();
            _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpClient.Client.ReceiveBufferSize = 8 * 1024 * 1024; // 8MB buffer
            _udpClient.Client.Bind(localEp);

            _isListening = true;
            _ = Task.Run(() => IngestionLoopAsync(_udpClient, _cts.Token));
        }

        public void JoinGroup(string multicastIp, string localAdapterIp)
        {
            if (_udpClient != null)
            {
                var mcast = IPAddress.Parse(multicastIp);
                var local = string.IsNullOrEmpty(localAdapterIp) ? IPAddress.Any : IPAddress.Parse(localAdapterIp);
                _udpClient.JoinMulticastGroup(mcast, local);
            }
        }

        public void DropGroup(string multicastIp)
        {
            if (_udpClient != null)
            {
                try
                {
                    _udpClient.DropMulticastGroup(IPAddress.Parse(multicastIp));
                }
                catch { /* Ignore drop exceptions during cleanup */ }
            }
        }

        public void RegisterChannel(string sourceIp, Action<byte[], int, int> packetHandler)
        {
            _channels[sourceIp] = packetHandler;
        }

        public void UnregisterChannel(string sourceIp)
        {
            _channels.TryRemove(sourceIp, out _);
        }

        private async Task IngestionLoopAsync(UdpClient client, CancellationToken ct)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(65535);
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var result = await client.ReceiveAsync(ct);
                    string senderIp = result.RemoteEndPoint.Address.ToString();

                    if (_channels.TryGetValue(senderIp, out var handler))
                    {
                        handler(result.Buffer, 0, result.Buffer.Length);
                    }
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public void StopListening()
        {
            _isListening = false;
            _cts?.Cancel();
            _udpClient?.Close();
            _udpClient?.Dispose();
            _udpClient = null;
        }

        public void Dispose() => StopListening();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj`  
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat(rtp): implement Yuv422Rasterizer and UnifiedRtpDemuxReceiver"
```

---

### Task 4: SDVoE Client & Single View Routing Orchestrator

**Files:**
- Create: `src/AvasRoutingTool/Sdvoe/SdvoeClient.cs`
- Create: `src/AvasRoutingTool/Sdvoe/RoutingOrchestrator.cs`
- Test: `tests/AvasRoutingTool.Tests/RoutingOrchestratorTests.cs`

**Interfaces:**
- Produces: `ISdvoeClient`, `RoutingOrchestrator` (`RouteSingleViewAsync(AvasDevice source, AvasDevice display)`, `UnrouteDisplayAsync(AvasDevice display)`)

- [ ] **Step 1: Write failing test for RoutingOrchestrator**

```csharp
// tests/AvasRoutingTool.Tests/RoutingOrchestratorTests.cs
using System.Threading.Tasks;
using AvasRoutingTool.Sdvoe;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class MockSdvoeClient : ISdvoeClient
    {
        public string? LastVideoRx;
        public string? LastVideoTx;
        public string? LastAudioRx;
        public string? LastAudioTx;
        public string? LastUsbRx;
        public string? LastUsbTx;
        public string? LastUnroutedRx;

        public Task<bool> JoinStreamAsync(string rxMac, string txMac, string streamType, int streamIndex)
        {
            if (streamType == "HDMI") { LastVideoRx = rxMac; LastVideoTx = txMac; }
            else if (streamType == "AUDIO") { LastAudioRx = rxMac; LastAudioTx = txMac; }
            else if (streamType == "USB") { LastUsbRx = rxMac; LastUsbTx = txMac; }
            return Task.FromResult(true);
        }

        public Task<bool> LeaveStreamAsync(string rxMac, string streamType, int streamIndex)
        {
            LastUnroutedRx = rxMac;
            return Task.FromResult(true);
        }
    }

    public class RoutingOrchestratorTests
    {
        [Fact]
        public async Task RouteSingleViewAsync_AtomicallyRoutesVideoAudioAndUsb()
        {
            var mockClient = new MockSdvoeClient();
            var orchestrator = new RoutingOrchestrator(mockClient);

            var tx = new AvasDevice { DeviceId = "00:11:22:33:44:AA", DeviceName = "Bron 1", IsTransmitter = true };
            var rx = new AvasDevice { DeviceId = "00:11:22:33:44:BB", DeviceName = "Dell Display", IsReceiver = true };

            bool success = await orchestrator.RouteSingleViewAsync(tx, rx);

            Assert.True(success);
            Assert.Equal("00:11:22:33:44:BB", mockClient.LastVideoRx);
            Assert.Equal("00:11:22:33:44:AA", mockClient.LastVideoTx);
            Assert.Equal("00:11:22:33:44:BB", mockClient.LastAudioRx);
            Assert.Equal("00:11:22:33:44:AA", mockClient.LastAudioTx);
            Assert.Equal("00:11:22:33:44:BB", mockClient.LastUsbRx);
            Assert.Equal("00:11:22:33:44:AA", mockClient.LastUsbTx);

            Assert.Equal("00:11:22:33:44:AA", rx.ActiveSourceId);
            Assert.Equal("Bron 1", rx.ActiveSourceName);
        }

        [Fact]
        public async Task UnrouteDisplayAsync_ClearsSubscriptions()
        {
            var mockClient = new MockSdvoeClient();
            var orchestrator = new RoutingOrchestrator(mockClient);

            var rx = new AvasDevice { DeviceId = "00:11:22:33:44:BB", ActiveSourceId = "00:11:22:33:44:AA", ActiveSourceName = "Bron 1" };
            bool success = await orchestrator.UnrouteDisplayAsync(rx);

            Assert.True(success);
            Assert.Equal("00:11:22:33:44:BB", mockClient.LastUnroutedRx);
            Assert.Empty(rx.ActiveSourceId);
            Assert.Empty(rx.ActiveSourceName);
        }
    }
}
```

- [ ] **Step 2: Run test to verify failure**

Run: `dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj`  
Expected: FAIL with missing interfaces.

- [ ] **Step 3: Implement ISdvoeClient and RoutingOrchestrator**

```csharp
// src/AvasRoutingTool/Sdvoe/SdvoeClient.cs
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
    public interface ISdvoeClient
    {
        Task<bool> JoinStreamAsync(string rxMac, string txMac, string streamType, int streamIndex);
        Task<bool> LeaveStreamAsync(string rxMac, string streamType, int streamIndex);
    }

    public class SdvoeClient : ISdvoeClient, IDisposable
    {
        private readonly string _serverIp;
        private readonly int _telnetPort;
        private readonly int _restPort;
        private TcpClient? _tcpClient;
        private StreamWriter? _writer;
        private StreamReader? _reader;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public SdvoeClient(string serverIp = "127.0.0.1", int telnetPort = 6970, int restPort = 8080)
        {
            _serverIp = serverIp;
            _telnetPort = telnetPort;
            _restPort = restPort;
        }

        public async Task<bool> ConnectAsync(CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct);
            try
            {
                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(_serverIp, _telnetPort, ct);
                var stream = _tcpClient.GetStream();
                _writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true, NewLine = "\r\n" };
                _reader = new StreamReader(stream, Encoding.ASCII);

                // Handshake
                await _writer.WriteLineAsync("require api 3.0.0.0");
                string? response = await _reader.ReadLineAsync(ct);
                return response != null && response.Contains("SUCCESS");
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<bool> JoinStreamAsync(string rxMac, string txMac, string streamType, int streamIndex)
        {
            if (_writer == null) return false;
            await _lock.WaitAsync();
            try
            {
                // SDVoE Join command syntax: join <rx>:<type>:<idx> <tx>:<type>:<idx>
                string cmd = $"join {rxMac}:{streamType}:{streamIndex} {txMac}:{streamType}:{streamIndex}";
                await _writer.WriteLineAsync(cmd);
                string? resp = await _reader?.ReadLineAsync();
                return resp != null && resp.Contains("SUCCESS");
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task<bool> LeaveStreamAsync(string rxMac, string streamType, int streamIndex)
        {
            if (_writer == null) return false;
            await _lock.WaitAsync();
            try
            {
                string cmd = $"leave {rxMac}:{streamType}:{streamIndex}";
                await _writer.WriteLineAsync(cmd);
                string? resp = await _reader?.ReadLineAsync();
                return resp != null && resp.Contains("SUCCESS");
            }
            finally
            {
                _lock.Release();
            }
        }

        public void Dispose()
        {
            _writer?.Dispose();
            _reader?.Dispose();
            _tcpClient?.Dispose();
        }
    }
}
```

```csharp
// src/AvasRoutingTool/Sdvoe/RoutingOrchestrator.cs
using System;
using System.Threading.Tasks;

namespace AvasRoutingTool.Sdvoe
{
    public class RoutingOrchestrator
    {
        private readonly ISdvoeClient _sdvoeClient;

        public RoutingOrchestrator(ISdvoeClient sdvoeClient)
        {
            _sdvoeClient = sdvoeClient ?? throw new ArgumentNullException(nameof(sdvoeClient));
        }

        public async Task<bool> RouteSingleViewAsync(AvasDevice sourceTx, AvasDevice displayRx)
        {
            if (sourceTx == null || displayRx == null) return false;

            // 1. Route Video (HDMI:0)
            bool videoOk = await _sdvoeClient.JoinStreamAsync(displayRx.DeviceId, sourceTx.DeviceId, "HDMI", 0);
            
            // 2. Route Audio (HDMI:0 or AUDIO:0)
            bool audioOk = await _sdvoeClient.JoinStreamAsync(displayRx.DeviceId, sourceTx.DeviceId, "AUDIO", 0);

            // 3. Route USB Mouse/Touch Thru (USB:0)
            bool usbOk = await _sdvoeClient.JoinStreamAsync(displayRx.DeviceId, sourceTx.DeviceId, "USB", 0);

            if (videoOk)
            {
                displayRx.ActiveSourceId = sourceTx.DeviceId;
                displayRx.ActiveSourceName = sourceTx.DeviceName;
            }

            return videoOk;
        }

        public async Task<bool> UnrouteDisplayAsync(AvasDevice displayRx)
        {
            if (displayRx == null) return false;

            bool leaveOk = await _sdvoeClient.LeaveStreamAsync(displayRx.DeviceId, "HDMI", 0);
            displayRx.ActiveSourceId = string.Empty;
            displayRx.ActiveSourceName = string.Empty;
            return leaveOk;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj`  
Expected: PASS (all tests pass).

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat(routing): implement SdvoeClient and RoutingOrchestrator"
```

---

### Task 5: WPF ViewModels & Dual-Mode Main Window

**Files:**
- Create: `src/AvasRoutingTool/ViewModels/ViewModelBase.cs`
- Create: `src/AvasRoutingTool/ViewModels/SourceCardViewModel.cs`
- Create: `src/AvasRoutingTool/ViewModels/DisplayCardViewModel.cs`
- Create: `src/AvasRoutingTool/ViewModels/MainViewModel.cs`
- Modify: `src/AvasRoutingTool/MainWindow.xaml`
- Modify: `src/AvasRoutingTool/MainWindow.xaml.cs`

**Interfaces:**
- Produces: `MainViewModel` (observable collections: `Sources`, `Displays`, `Layouts`), `SelectedTab` (`VisualRouting` vs `BavmConsole`)

- [ ] **Step 1: Write ViewModel classes with ObservableCollection properties**

Implement `ViewModelBase`, `SourceCardViewModel`, `DisplayCardViewModel`, `MainViewModel`.

- [ ] **Step 2: Install WebView2 NuGet package to AvasRoutingTool**

Run:
```powershell
dotnet add src/AvasRoutingTool/AvasRoutingTool.csproj package Microsoft.Web.WebView2
```

- [ ] **Step 3: Implement MainWindow.xaml with dual-mode navigation tabs**

Add TabControl containing:
* Tab 0: Visual Routing Canvas (Sources top row, Displays middle row, Layouts bottom row).
* Tab 1: Microsoft WebView2 Browser pointing to `config.BavmUrl` (`http://localhost:80`).
* Tab 2: Settings & Diagnostics.

- [ ] **Step 4: Build solution to verify compilation**

Run: `dotnet build AvasRoutingTool.sln`  
Expected: Build succeeded with 0 errors.

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat(ui): implement MainViewModel and dual-mode MainWindow with WebView2"
```

---

### Task 6: Visual Drag-and-Drop Routing Interaction

**Files:**
- Create: `src/AvasRoutingTool/Views/VisualRoutingView.xaml` / `.xaml.cs`
- Create: `src/AvasRoutingTool/Views/SourceCardView.xaml` / `.xaml.cs`
- Create: `src/AvasRoutingTool/Views/DisplayCardView.xaml` / `.xaml.cs`
- Create: `src/AvasRoutingTool/Views/LayoutsBarView.xaml` / `.xaml.cs`

**Interfaces:**
- Handles: `DragDrop.DoDragDrop`, `UIElement.Drop` on DisplayCard. Calls `RoutingOrchestrator.RouteSingleViewAsync`.

- [ ] **Step 1: Implement SourceCardView with drag gesture initiator**

Implement `PreviewMouseLeftButtonDown` and `MouseMove` to initiate `DragDrop.DoDragDrop(sourceCard, sourceDevice, DragDropEffects.Link)`.

- [ ] **Step 2: Implement DisplayCardView with Drop handler**

Set `AllowDrop="True"` on DisplayCard. On `Drop` event, extract dropped `AvasDevice` source and trigger ViewModel `RouteCommand.Execute(droppedSource)`.

- [ ] **Step 3: Implement LayoutsBarView**

Display cards for `Off`, `Single View`, `Matrix 2x2` (staged), `PIP` (staged), `Side-by-Side` (staged), and `1+3` (staged).

- [ ] **Step 4: Build and test interaction handlers**

Run: `dotnet build AvasRoutingTool.sln`  
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat(ui): implement drag-and-drop visual routing views"
```

---

### Task 7: Comprehensive Verification & Packaging

**Files:**
- Create: `scripts/build_package.ps1`
- Modify: `memory/active_context.md`
- Modify: `memory/progress.md`

- [ ] **Step 1: Run all automated unit and integration tests**

Run: `dotnet test AvasRoutingTool.sln -v normal`  
Expected: 100% tests passing.

- [ ] **Step 2: Publish standalone portable application**

Run:
```powershell
dotnet publish src/AvasRoutingTool/AvasRoutingTool.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/AvasRoutingTool
```
Expected: Single self-contained `AvasRoutingTool.exe` generated in `publish/AvasRoutingTool/`.

- [ ] **Step 3: Synchronize memory state**

Update `memory/active_context.md` and `memory/progress.md` marking Phase 1 deliverables as complete.

- [ ] **Step 4: Final commit**

```bash
git add .
git commit -m "chore: complete Phase 1 implementation and packaging"
```
