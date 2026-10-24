# AVAS Routing Tool: Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the AVAS Routing Tool desktop application (Approach A: Native Visual Router as primary interface + embedded full BAVM web console in WebView2), featuring live TX video previews, 3-tier visual canvas, and drag-and-drop Single View video routing for Advantech AVAS-223 transceivers.

**Architecture:** A native C# .NET 8 WPF application with a dual-mode shell. Mode 1 is an interactive 3-tier Visual Routing Canvas (Sources top row, Displays middle row, Layouts bottom row) backed by a high-throughput UDP 5004/5000 RFC 4175 RTP demux receiver rendering directly to GPU WriteableBitmaps. Mode 2 is an embedded Microsoft WebView2 browser control hosting the official BAVM web application with persistent configurable URL.

**Tech Stack:** C# .NET 8 (`net8.0-windows`), WPF, Microsoft.Web.WebView2, System.Net.Sockets, xUnit, FluentAssertions, Moq.

**Spec:** [`docs/superpowers/specs/2026-10-25-avas-routing-tool-design.md`](file:///c:/Users/POC-615/Documents/GitHub/AVAS-Routing-Tool/docs/superpowers/specs/2026-10-25-avas-routing-tool-design.md)

---

## Global Constraints

* **Platform Floor**: Windows 10/11 x64, .NET 8.0 SDK.
* **BAVM URL Persistence**: Configurable BAVM URL (defaults to `http://localhost:80`), persisted to `appsettings.json` and remembered across sessions.
* **Synchronized Control Server IP**: Control Server IP is derived automatically from the BAVM URL host; Telnet port (default 6970) and REST port (default 8080) are configurable.
* **Strict Device Naming**: Displays (RX) and Sources (TX) strictly display the **Device Name** as set in BAVM (no raw EDID sink names).
* **Phase 1 Routing Scope**: **Video Routing ONLY** (`join <rx>:HDMI:0 <tx>:HDMI:0`). Audio and USB-KVM routing are left to the original BAVM console.
* **Zero GDI+ Leaks**: Live video frames are written directly to `WriteableBitmap.BackBuffer` without GDI+ bitmap instantiation.
* **Multicast Range**: Class D pool strictly allocated between `225.0.0.1` and `225.0.1.254`.
* **Memory Integrity**: All changes tracked in `memory/active_context.md` and `memory/progress.md`.

## Review Focus

1. **WSAEADDRINUSE (Error 10048)**: All UDP sockets must set `SocketOptionName.ReuseAddress = true` before calling `Bind()` to allow rapid start/stop preview cycles.
2. **Switch Multicast Flooding**: Teardown must issue both control nullification (`set <mac> stream HDMI 1 enable false address 0.0.0.0`) and host IGMP drop (`DropMulticastGroup`).
3. **UI Thread Marshalling**: All socket callbacks and asynchronous notifications must dispatch property changes to the WPF `DispatcherQueue`.
4. **Drag-and-Drop Reliability**: Dropping a Source TX onto a Display RX must execute `join <rx>:HDMI:0 <tx>:HDMI:0` and immediately reflect active routing state on the Display card.
5. **BAVM Web Isolation**: WebView2 must use an isolated portable `WebView2_UserData` directory and provide an offline banner with retry options if BAVM is unreachable.

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
│           ├── Theme.xaml
│           └── Icons.xaml
└── tests/
    └── AvasRoutingTool.Tests/
        ├── AvasRoutingTool.Tests.csproj
        ├── ConfigServiceTests.cs
        ├── MulticastIpManagerTests.cs
        ├── Yuv422RasterizerTests.cs
        ├── SdvoeClientTests.cs
        └── RoutingOrchestratorTests.cs
```

---

### Task 1: Scaffolding, Models & Persistent Configuration

**Files:**
- Create: `AvasRoutingTool.sln`
- Create: `src/AvasRoutingTool/AvasRoutingTool.csproj`
- Create: `src/AvasRoutingTool/Configuration/AppConfig.cs`
- Create: `src/AvasRoutingTool/Configuration/ConfigService.cs`
- Create: `src/AvasRoutingTool/Sdvoe/AvasDevice.cs`
- Create: `tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj`
- Test: `tests/AvasRoutingTool.Tests/ConfigServiceTests.cs`

**Interfaces:**
- Produces: `AppConfig` (`BavmUrl`, `DerivedControlServerIp`, `TelnetPort`, `RestPort`, `MulticastStartIp`, `MulticastEndIp`, `BasePort`), `ConfigService`, `AvasDevice`

- [ ] **Step 1: Write failing test for ConfigService and DerivedControlServerIp**

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
        public void AppConfig_DerivedControlServerIp_ExtractsHostFromBavmUrl()
        {
            var config = new AppConfig { BavmUrl = "http://192.168.1.200:80" };
            Assert.Equal("192.168.1.200", config.DerivedControlServerIp);

            config.BavmUrl = "http://avas-server:8080/spa";
            Assert.Equal("avas-server", config.DerivedControlServerIp);
        }

        [Fact]
        public void Load_WhenFileDoesNotExist_ReturnsDefaultsAndPersists()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"cfg_{System.Guid.NewGuid()}.json");
            try
            {
                var service = new ConfigService(tempFile);
                var config = service.Load();

                Assert.NotNull(config);
                Assert.Equal("http://localhost:80", config.BavmUrl);
                Assert.Equal("localhost", config.DerivedControlServerIp);
                Assert.Equal(6970, config.TelnetPort);
                Assert.Equal(8080, config.RestPort);
                Assert.True(File.Exists(tempFile));
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }
    }
}
```

- [ ] **Step 2: Scaffold .NET solution and verify test failure**

```powershell
dotnet new sln -n AvasRoutingTool
dotnet new wpf -n AvasRoutingTool -o src/AvasRoutingTool -f net8.0-windows
dotnet new xunit -n AvasRoutingTool.Tests -o tests/AvasRoutingTool.Tests -f net8.0-windows
dotnet sln add src/AvasRoutingTool/AvasRoutingTool.csproj
dotnet sln add tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj
dotnet add tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj reference src/AvasRoutingTool/AvasRoutingTool.csproj
dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj
```
Expected: FAIL with missing classes.

- [ ] **Step 3: Implement AppConfig, ConfigService, and AvasDevice**

```csharp
// src/AvasRoutingTool/Configuration/AppConfig.cs
using System;

namespace AvasRoutingTool.Configuration
{
    public class AppConfig
    {
        public string BavmUrl { get; set; } = "http://localhost:80";
        public int TelnetPort { get; set; } = 6970;
        public int RestPort { get; set; } = 8080;
        public string MulticastStartIp { get; set; } = "225.0.0.1";
        public string MulticastEndIp { get; set; } = "225.0.1.254";
        public int BasePort { get; set; } = 5004;
        public string LocalNetworkInterfaceIp { get; set; } = "";
        public string Theme { get; set; } = "Dark";

        public string DerivedControlServerIp
        {
            get
            {
                try
                {
                    if (Uri.TryCreate(BavmUrl, UriKind.Absolute, out var uri))
                    {
                        return uri.Host;
                    }
                }
                catch { }
                return "127.0.0.1";
            }
        }
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

```csharp
// src/AvasRoutingTool/Sdvoe/AvasDevice.cs
namespace AvasRoutingTool.Sdvoe
{
    public class AvasDevice
    {
        public string DeviceId { get; set; } = string.Empty; // MAC Address
        public string DeviceName { get; set; } = string.Empty; // Strict BAVM Device Name
        public string IpAddress { get; set; } = string.Empty;
        public string Model { get; set; } = "AVAS-223";
        public int VendorId { get; set; } = 105;
        public int ProductId { get; set; } = 81;
        public int ChipId { get; set; } = 0;
        public bool IsTransmitter { get; set; }
        public bool IsReceiver { get; set; }
        public bool IsActive { get; set; } = true;
        public string ActiveSourceId { get; set; } = string.Empty; // For RX: currently routed TX MAC
        public string ActiveSourceName { get; set; } = string.Empty; // For RX: currently routed TX DeviceName
        public string MulticastIp { get; set; } = string.Empty;
        public int MulticastPort { get; set; } = 5004;
        public double FramesPerSecond { get; set; }
        public string Resolution { get; set; } = "1080p60";
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj`  
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat(scaffold): initialize .NET 8 solution and implement AppConfig with derived host IP"
```

---

### Task 2: Multicast IP Pool Manager

**Files:**
- Create: `src/AvasRoutingTool/Sdvoe/MulticastIpManager.cs`
- Test: `tests/AvasRoutingTool.Tests/MulticastIpManagerTests.cs`

**Interfaces:**
- Produces: `MulticastIpManager` (`AllocateIp(string mac)`, `ReleaseIp(string mac)`)

- [ ] **Step 1: Write failing test for MulticastIpManager**

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
Expected: FAIL with missing `MulticastIpManager`.

- [ ] **Step 3: Implement MulticastIpManager**

Implement `MulticastIpManager.cs` supporting thread-safe allocation and reuse across Class D range `225.0.0.1` to `225.0.1.254`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj`  
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat(sdvoe): implement MulticastIpManager for Class D pool"
```

---

### Task 3: Color-Space Rasterizer & Unified RTP Demux Receiver

**Files:**
- Create: `src/AvasRoutingTool/Rtp/Yuv422Rasterizer.cs`
- Create: `src/AvasRoutingTool/Rtp/UnifiedRtpDemuxReceiver.cs`
- Test: `tests/AvasRoutingTool.Tests/Yuv422RasterizerTests.cs`

**Interfaces:**
- Produces: `Yuv422Rasterizer.ConvertYuv422SliceToBgra(...)`, `UnifiedRtpDemuxReceiver` (`StartListening`, `JoinGroup`, `DropGroup`, `RegisterChannel`, `UnregisterChannel`, `StopListening`)

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
            byte[] src = new byte[] { 16, 128, 16, 128 }; // Black in YUV422
            byte[] dest = new byte[8];

            Yuv422Rasterizer.ConvertYuv422SliceToBgra(src, 0, 4, dest, 0);

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
Expected: FAIL.

- [ ] **Step 3: Implement Yuv422Rasterizer and UnifiedRtpDemuxReceiver**

Implement fast fixed-point BT.601 conversion in `Yuv422Rasterizer` and dual-port socket demultiplexing in `UnifiedRtpDemuxReceiver` (with 8MB buffer and `SocketOptionName.ReuseAddress = true`).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj`  
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat(rtp): implement Yuv422Rasterizer and UnifiedRtpDemuxReceiver"
```

---

### Task 4: SDVoE Client & Video Routing Orchestrator

**Files:**
- Create: `src/AvasRoutingTool/Sdvoe/SdvoeClient.cs`
- Create: `src/AvasRoutingTool/Sdvoe/RoutingOrchestrator.cs`
- Test: `tests/AvasRoutingTool.Tests/RoutingOrchestratorTests.cs`

**Interfaces:**
- Produces: `ISdvoeClient`, `RoutingOrchestrator` (`RouteSingleViewVideoAsync(AvasDevice source, AvasDevice display)`, `UnrouteDisplayAsync(AvasDevice display)`)

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
        public string? LastUnroutedRx;

        public Task<bool> JoinStreamAsync(string rxMac, string txMac, string streamType, int streamIndex)
        {
            if (streamType == "HDMI") { LastVideoRx = rxMac; LastVideoTx = txMac; }
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
        public async Task RouteSingleViewVideoAsync_RoutesVideoAndUpdatesDeviceName()
        {
            var mock = new MockSdvoeClient();
            var orchestrator = new RoutingOrchestrator(mock);

            var tx = new AvasDevice { DeviceId = "00:11:22:33:44:AA", DeviceName = "Bron 1", IsTransmitter = true };
            var rx = new AvasDevice { DeviceId = "00:11:22:33:44:BB", DeviceName = "Samsung 1", IsReceiver = true };

            bool success = await orchestrator.RouteSingleViewVideoAsync(tx, rx);

            Assert.True(success);
            Assert.Equal("00:11:22:33:44:BB", mock.LastVideoRx);
            Assert.Equal("00:11:22:33:44:AA", mock.LastVideoTx);
            Assert.Equal("00:11:22:33:44:AA", rx.ActiveSourceId);
            Assert.Equal("Bron 1", rx.ActiveSourceName);
        }

        [Fact]
        public async Task UnrouteDisplayAsync_ClearsSubscriptions()
        {
            var mock = new MockSdvoeClient();
            var orchestrator = new RoutingOrchestrator(mock);

            var rx = new AvasDevice { DeviceId = "00:11:22:33:44:BB", DeviceName = "Samsung 1", ActiveSourceId = "00:11:22:33:44:AA", ActiveSourceName = "Bron 1" };
            bool success = await orchestrator.UnrouteDisplayAsync(rx);

            Assert.True(success);
            Assert.Equal("00:11:22:33:44:BB", mock.LastUnroutedRx);
            Assert.Empty(rx.ActiveSourceId);
            Assert.Empty(rx.ActiveSourceName);
        }
    }
}
```

- [ ] **Step 2: Run test to verify failure**

Run: `dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj`  
Expected: FAIL.

- [ ] **Step 3: Implement ISdvoeClient and RoutingOrchestrator**

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

        public async Task<bool> RouteSingleViewVideoAsync(AvasDevice sourceTx, AvasDevice displayRx)
        {
            if (sourceTx == null || displayRx == null) return false;

            // Phase 1: Video Routing Only (HDMI:0)
            bool videoOk = await _sdvoeClient.JoinStreamAsync(displayRx.DeviceId, sourceTx.DeviceId, "HDMI", 0);

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

Implement `SdvoeClient.cs` connecting to TCP port 6970 and REST port 8080.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj`  
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat(routing): implement SdvoeClient and RoutingOrchestrator for video-only Single View"
```

---

### Task 5: Modern Slate Dark Theme & Dual-Mode Shell with WebView2

**Files:**
- Create: `src/AvasRoutingTool/Resources/Theme.xaml`
- Create: `src/AvasRoutingTool/Resources/Icons.xaml`
- Create: `src/AvasRoutingTool/Views/SettingsDialog.xaml` / `.xaml.cs`
- Modify: `src/AvasRoutingTool/App.xaml`
- Modify: `src/AvasRoutingTool/MainWindow.xaml` / `.xaml.cs`
- Modify: `src/AvasRoutingTool/ViewModels/MainViewModel.cs`

**Interfaces:**
- Produces: `MainWindow` with tabs `[ 🔀 Visual Routing ]`, `[ 🎛 BAVM Console ]`, and `[ ⚙ Settings ]` dialog.

- [ ] **Step 1: Install Microsoft.Web.WebView2 package**

```powershell
dotnet add src/AvasRoutingTool/AvasRoutingTool.csproj package Microsoft.Web.WebView2
```

- [ ] **Step 2: Create Slate Dark Medical Theme and Vector Icons**

Define Slate 900/800 dark theme brushes (`#0F172A`, `#1E293B`, `#334155`), Blue accent (`#3B82F6`), Emerald online (`#10B981`), and SVG vector geometries in `Theme.xaml` and `Icons.xaml`.

- [ ] **Step 3: Implement SettingsDialog for BAVM URL and Ports**

Provide editable fields for:
* BAVM Web URL (default `http://localhost:80`)
* Auto-computed Control Server IP (derived from URL host)
* Control Server Telnet Port (default 6970) and REST Port (default 8080)
* Local Network Adapter Selector
* Save button calling `ConfigService.Save()` and triggering live reconnect.

- [ ] **Step 4: Implement MainWindow with Dual-Mode Tab Navigation**

Host `VisualRoutingView` on Tab 0, and `WebView2` browser on Tab 1 with isolated `WebView2_UserData` folder.

- [ ] **Step 5: Build solution to verify compilation**

Run: `dotnet build AvasRoutingTool.sln`  
Expected: Build succeeded with 0 errors.

- [ ] **Step 6: Commit**

```bash
git add .
git commit -m "feat(ui): implement Slate Dark theme, SettingsDialog, and dual-mode MainWindow"
```

---

### Task 6: Sources (TX) Carousel & Live Video Previews

**Files:**
- Create: `src/AvasRoutingTool/ViewModels/SourceCardViewModel.cs`
- Create: `src/AvasRoutingTool/Views/SourceCardView.xaml` / `.xaml.cs`

**Interfaces:**
- Produces: Source card with live `WriteableBitmap` preview surface, strict `DeviceName` header, IP/MAC label, live rolling FPS pill, and drag gesture initiator.

- [ ] **Step 1: Implement SourceCardViewModel**

Manage device telemetry, `WriteableBitmap` video buffer, `FPS` counter, and preview activation via `UnifiedRtpDemuxReceiver` on UDP port 5004.

- [ ] **Step 2: Implement SourceCardView with drag gesture detection**

```csharp
private Point _dragStartPoint;

private void Card_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
{
    _dragStartPoint = e.GetPosition(null);
}

private void Card_MouseMove(object sender, MouseEventArgs e)
{
    if (e.LeftButton == MouseButtonState.Pressed && DataContext is SourceCardViewModel vm)
    {
        Point current = e.GetPosition(null);
        Vector diff = _dragStartPoint - current;
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            DragDrop.DoDragDrop(this, vm.Device, DragDropEffects.Link);
        }
    }
}
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build AvasRoutingTool.sln`  
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add .
git commit -m "feat(sources): implement SourceCardView with live preview and drag gesture"
```

---

### Task 7: Displays (RX) Canvas & Single View Video Routing

**Files:**
- Create: `src/AvasRoutingTool/ViewModels/DisplayCardViewModel.cs`
- Create: `src/AvasRoutingTool/Views/DisplayCardView.xaml` / `.xaml.cs`
- Create: `src/AvasRoutingTool/Views/VisualRoutingView.xaml` / `.xaml.cs`

**Interfaces:**
- Produces: Display cards accepting dropped Source TX cards, displaying strict `DeviceName`, active routed source, and calling `RoutingOrchestrator.RouteSingleViewVideoAsync`.

- [ ] **Step 1: Implement DisplayCardViewModel**

Track Display `DeviceName`, IP, active routed source name, connected state, and provide `UnrouteCommand`.

- [ ] **Step 2: Implement DisplayCardView drop target handling**

```csharp
private void Card_Drop(object sender, DragEventArgs e)
{
    if (e.Data.GetDataPresent(typeof(AvasDevice)) && DataContext is DisplayCardViewModel vm)
    {
        var droppedSource = (AvasDevice)e.Data.GetData(typeof(AvasDevice));
        vm.RouteSourceCommand.Execute(droppedSource);
    }
}
```
Add visual states for drag-over: dashed cyan border `#3B82F6` and elevation.

- [ ] **Step 3: Implement VisualRoutingView**

Assemble the 3-tier layout: Sources reel on top, Displays grid in the middle, Layouts selector on the bottom.

- [ ] **Step 4: Build to verify compilation**

Run: `dotnet build AvasRoutingTool.sln`  
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat(displays): implement DisplayCardView drop target and VisualRoutingView canvas"
```

---

### Task 8: Bottom Layouts Bar & MultiView Staging

**Files:**
- Create: `src/AvasRoutingTool/ViewModels/LayoutItemViewModel.cs`
- Create: `src/AvasRoutingTool/Views/LayoutsBarView.xaml` / `.xaml.cs`

**Interfaces:**
- Produces: Bottom layout selector with `Off`, `Single View` (active default), and staged MultiView cards (`Matrix 2x2`, `PIP`, `Side-by-Side`, `1+3 Quad`).

- [ ] **Step 1: Implement LayoutItemViewModel and LayoutsBarView**

Create layout cards matching the SigmaXG reference:
* `Off`: Disconnects active display.
* `Single`: Active default (Phase 1).
* `Matrix 2x2`, `Picture-in-Picture`, `Side-by-Side`, `1+3 Quad`: Visually displayed with a clean "Phase 2" pill.

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build AvasRoutingTool.sln`  
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add .
git commit -m "feat(layouts): implement LayoutsBarView with Single View active and MultiView staged"
```

---

### Task 9: Comprehensive Test Suite, Packaging & Memory Synchronization

**Files:**
- Create: `scripts/build_package.ps1`
- Modify: `memory/active_context.md`
- Modify: `memory/progress.md`

- [ ] **Step 1: Run complete test suite**

Run: `dotnet test AvasRoutingTool.sln -v normal`  
Expected: 100% tests passing.

- [ ] **Step 2: Generate standalone portable package**

Run:
```powershell
dotnet publish src/AvasRoutingTool/AvasRoutingTool.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/AvasRoutingTool
```
Expected: `publish/AvasRoutingTool/AvasRoutingTool.exe` created.

- [ ] **Step 3: Synchronize memory state**

Update `memory/active_context.md` and `memory/progress.md` reflecting completed deliverables.

- [ ] **Step 4: Commit**

```bash
git add .
git commit -m "chore: complete Phase 1 implementation and packaging"
```
