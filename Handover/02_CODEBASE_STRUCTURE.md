# 02. Codebase Structure & Component Map

This document details every directory, file, and class within the **AVAS Routing Tool** solution.

---

## 1. Solution Tree

```
c:\Users\POC-615\Documents\GitHub\AVAS-Routing-Tool\
├── AvasRoutingTool.sln                     // Visual Studio / .NET 8 Solution File
├── AGENTS.md                               // Agent rules & memory maintenance protocols
├── CLAUDE.md                               // Claude Code primary entry point
├── memory/                                 // Persistent project memory
├── Handover/                               // Claude Code handover documents
├── src/
│   └── AvasRoutingTool/                    // Main WPF Application Project (.NET 8 Windows)
│       ├── App.xaml / .xaml.cs
│       ├── MainWindow.xaml / .xaml.cs
│       ├── Configuration/
│       ├── Controls/
│       ├── Logging/
│       ├── Resources/
│       ├── Rtp/
│       ├── Sdvoe/
│       ├── ViewModels/
│       └── Views/
├── tests/
│   └── AvasRoutingTool.Tests/              // xUnit Test Suite (.NET 8 Windows)
└── publish/
    └── AvasRoutingTool/                    // Release Standalone Executable & Assets
```

---

## 2. Source Code Components (`src/AvasRoutingTool/`)

### 2.1 Application Core
* **[`App.xaml`](../src/AvasRoutingTool/App.xaml) / [`App.xaml.cs`](../src/AvasRoutingTool/App.xaml.cs)**:
  * Application entry point.
  * Injects global exception handlers (`AppDomain.UnhandledException`, `DispatcherUnhandledException`, `TaskScheduler.UnobservedTaskException`) writing to `app.log`.
  * Merges resource dictionaries (`Theme.xaml`, `Icons.xaml`).
* **[`MainWindow.xaml`](../src/AvasRoutingTool/MainWindow.xaml) / [`MainWindow.xaml.cs`](../src/AvasRoutingTool/MainWindow.xaml.cs)**:
  * Top navigation bar with status pill (Connected / Offline), Refresh button, and Settings button.
  * Tab control switching between `VisualRoutingView`, `BavmConsoleView`, and `DiagnosticsView`.

### 2.2 Configuration (`Configuration/`)
* **[`AppConfig.cs`](../src/AvasRoutingTool/Configuration/AppConfig.cs)**:
  * Strongly-typed configuration model: `BavmUrl`, `ControlServerIp`, `ControlServerTcpPort` (6970), `ControlServerRestPort` (8090), `RtpPort` (5004), `AutoConnect`.
  * Helper `DerivedControlServerIp`: automatically extracts IPv4 host from `BavmUrl` (e.g. `http://192.168.1.100:80` -> `192.168.1.100`), converting `localhost` to `127.0.0.1`.
* **[`ConfigService.cs`](../src/AvasRoutingTool/Configuration/ConfigService.cs) / [`IConfigService.cs`](../src/AvasRoutingTool/Configuration/IConfigService.cs)**:
  * Loads and saves `appsettings.json`. Creates sensible defaults if missing.

### 2.3 Custom Controls (`Controls/`)
* **[`AdaptiveGridLayout.cs`](../src/AvasRoutingTool/Controls/AdaptiveGridLayout.cs)**:
  * Pure mathematical model: `Compute(int count, double availableWidth, double availableHeight, GridSpec spec)`.
  * Returns `GridPlan(int Columns, int Rows, double CardWidth, double CardHeight, bool NeedsScroll)`.
  * Preserves 16:9 widescreen video surface (`VideoAspect: 16.0 / 9.0`).
* **[`AdaptiveCardPanel.cs`](../src/AvasRoutingTool/Controls/AdaptiveCardPanel.cs)**:
  * Custom WPF `Panel` hosting cards in `VisualRoutingView.xaml`.
  * Hooks into parent `ScrollViewer`'s `ViewportWidth`/`ViewportHeight` to determine effective space.
  * Centers cards horizontally and vertically when viewport exceeds grid size.

### 2.4 Logging (`Logging/`)
* **[`FractionalLayoutPanel.cs`](../src/AvasRoutingTool/Controls/FractionalLayoutPanel.cs)**: Places children at fractional rectangles of a letterboxed canvas (MultiView blocks, schematics, thumbnails). With exactly one unbounded axis and an `AspectRatio`, it derives that axis from the ratio.
* **[`StepHeader.cs`](../src/AvasRoutingTool/Controls/StepHeader.cs)**: Numbered Composer step heading (number, then a check icon when done); look in `Resources/Styles/Composer.xaml` (`Style.StepHeader`); never a tab stop.
* **[`LogEntry.cs`](../src/AvasRoutingTool/Logging/LogEntry.cs)**: Formatted log structure (`Timestamp`, `Level`, `Category`, `Message`).
* **[`LogLevel.cs`](../src/AvasRoutingTool/Logging/LogLevel.cs)**: `DEBUG`, `INFO`, `WARN`, `ERROR`, `CRASH`.
* **[`LogService.cs`](../src/AvasRoutingTool/Logging/LogService.cs)**: Thread-safe ring buffer (max 1000 items in memory for live UI) + continuous append to disk (`app.log`).

### 2.5 Resources (`Resources/`)
* **[`Theme.xaml`](../src/AvasRoutingTool/Resources/Theme.xaml)**: Modern medical-grade dark theme (Slate palette: `#0B0F17`, `#111827`, `#1F2937`, Emerald green `#10B981` online status, Sky blue `#0284C7` accent).
* **[`Icons.xaml`](../src/AvasRoutingTool/Resources/Icons.xaml)**: Vector path geometries for video, monitor, refresh, settings, close, info, and warning icons.

### 2.6 Real-Time Video Ingestion (`Rtp/`)
* **[`IRtpReceiver.cs`](../src/AvasRoutingTool/Rtp/IRtpReceiver.cs)**: Socket receiver interface.
* **[`RtpHeader.cs`](../src/AvasRoutingTool/Rtp/RtpHeader.cs)**: Zero-allocation parser for RFC 3550 & RFC 4175 RTP headers.
* **[`ScanlineFrameReassembler.cs`](../src/AvasRoutingTool/Rtp/ScanlineFrameReassembler.cs)**: Reassembles split RTP packets into unified frames, detecting frame complete boundaries via the RTP Marker bit.
* **[`UnifiedRtpDemuxReceiver.cs`](../src/AvasRoutingTool/Rtp/UnifiedRtpDemuxReceiver.cs)**: Dual-port UDP listener (5000/5004) with source-IP and companion-link alias dispatch.
* **[`Yuv422Rasterizer.cs`](../src/AvasRoutingTool/Rtp/Yuv422Rasterizer.cs)**: Q10 fixed-point BT.601 color-space converter rendering YUV422 directly into `WriteableBitmap.BackBuffer`.

### 2.7 SDVoE Hardware Communication (`Sdvoe/`)
* **[`AvasDevice.cs`](../src/AvasRoutingTool/Sdvoe/AvasDevice.cs)**: Device model containing `DeviceId`, `DeviceName`, `VendorId`, `IsTransmitter`, `IsReceiver`, `IsDualLink`, `NodeIpA`, `NodeIpB`, `ActiveSourceId`, and `IsPreviewSupported`.
* **[`ISdvoeClient.cs`](../src/AvasRoutingTool/Sdvoe/ISdvoeClient.cs) / [`SdvoeClient.cs`](../src/AvasRoutingTool/Sdvoe/SdvoeClient.cs)**:
  * TCP 6970 CLI client for BlueRiver commands (`get all identity`).
  * REST 8090 client for device querying (`GET /api/device/ALL`) and Baseboard MCU UART commands (`POST /api/device/{mac}`).
* **[`RestResponse.cs`](../src/AvasRoutingTool/Sdvoe/RestResponse.cs)**: Robust response parser handling Semtech's whitespace/newlines (`"status" : "PROCESSING"` or `"status" : "SUCCESS"`).
* **[`RoutingOrchestrator.cs`](../src/AvasRoutingTool/Sdvoe/RoutingOrchestrator.cs)**: Dispatches `RouteSingleViewVideoAsync` and `UnrouteDisplayAsync`.
* **[`MulticastIpManager.cs`](../src/AvasRoutingTool/Sdvoe/MulticastIpManager.cs)**: Allocates and reclaims multicast IPs (`225.0.0.1`–`225.0.1.254`).

### 2.8 ViewModels (`ViewModels/`)
* **[`MainViewModel.cs`](../src/AvasRoutingTool/ViewModels/MainViewModel.cs)**: Root ViewModel managing connection lifecycle, device discovery, drag-and-drop routing dispatch, and logging.
* **[`SourceCardViewModel.cs`](../src/AvasRoutingTool/ViewModels/SourceCardViewModel.cs)**: Manages preview stream activation (`StartCommand`, `StopCommand`, `RefreshCommand`), FPS counter, and video frame updates.
* **[`DisplayCardViewModel.cs`](../src/AvasRoutingTool/ViewModels/DisplayCardViewModel.cs)**: Manages decoder state, `UnrouteCommand`, and live video preview mirroring from the active source.
* **[`LayoutItemViewModel.cs`](../src/AvasRoutingTool/ViewModels/LayoutItemViewModel.cs)**: Represents layout selector buttons ("Single View", "Off", "Matrix 2x2", "PIP", etc.).
* **[`RelayCommand.cs`](../src/AvasRoutingTool/ViewModels/RelayCommand.cs)**: `ICommand` implementation for clean MVVM binding.

### 2.9 Views (`Views/`)
* **[`VisualRoutingView.xaml`](../src/AvasRoutingTool/Views/VisualRoutingView.xaml)**: The primary visual routing canvas with top TX row, middle RX row, and bottom Layouts bar.
* **[`SourceCardView.xaml`](../src/AvasRoutingTool/Views/SourceCardView.xaml)**: Transmitter card rendering live video preview, telemetry badges, and initiating drag operations.
* **[`DisplayCardView.xaml`](../src/AvasRoutingTool/Views/DisplayCardView.xaml)**: Receiver card handling drop events, showing mirrored video or non-AVAS fallback banners, and unroute button.
* **[`LayoutsBarView.xaml`](../src/AvasRoutingTool/Views/LayoutsBarView.xaml)**: Bottom bar with layout presets and drag-to-unroute from the "Off" tile.
* **[`BavmConsoleView.xaml`](../src/AvasRoutingTool/Views/BavmConsoleView.xaml)**: WebView2 browser control hosting official BAVM web UI.
* **[`DiagnosticsView.xaml`](../src/AvasRoutingTool/Views/DiagnosticsView.xaml)**: Real-time searchable log table.
* **[`SettingsDialog.xaml`](../src/AvasRoutingTool/Views/SettingsDialog.xaml)**: Connection settings configuration modal.
* **[`MultiViewComposerView.xaml`](../src/AvasRoutingTool/Views/MultiViewComposerView.xaml)** (+ `LayoutSchematic`, `CompactSourceCardView`): the MultiView Composer. Views hold layout and bindings only; every style, template and Composer size token lives in `Resources/Styles/Composer.xaml`. `BlockDensity.IsCompact` (inherited, set on a block narrower than 240 px) switches the overlays to compact styles.

---

## 3. Test Suite Breakdown (`tests/AvasRoutingTool.Tests/`)

The test suite contains **59 automated tests** across 15 test files:

1. **`AdaptiveGridLayoutTests.cs` (9 tests)**: Pure mathematical calculation of columns, rows, card width, 16:9 aspect, and scroll detection.
2. **`AdaptiveCardPanelTests.cs` (3 tests)**: STA layout tests verifying card dimensions, centering, and `ScrollViewer` viewport awareness.
3. **`ViewSmokeTests.cs` (4 tests)**: Real STA XAML instantiation of `SourceCardView`, `DisplayCardView` (routed & unrouted), and `VisualRoutingView` to prevent TwoWay binding crashes and resource dictionary faults.
4. **`DisplayCardViewModelTests.cs` (5 tests)**: Verifies unrouting logic, reactive preview mirroring, non-AVAS fallback states, and dynamic bitmap reference updates.
5. **`RoutingOrchestratorTests.cs` (2 tests)**: Verifies `join` and `leave` REST dispatching.
6. **`RestResponseTests.cs` (7 tests)**: Structural JSON verification of Semtech's `PROCESSING`, `SUCCESS`, and error statuses.
7. **`AvasDeviceTests.cs` (4 tests)**: Verifies VendorId 105 checking, transmitter/receiver flags, link mode, and node IPs.
8. **`RtpHeaderTests.cs` (3 tests)**: Verifies RFC 3550 & RFC 4175 parsing and byte-boundary guards.
9. **`ScanlineFrameReassemblerTests.cs` (2 tests)**: Verifies out-of-order packet reassembly and frame marker handling.
10. **`Yuv422RasterizerTests.cs` (2 tests)**: Verifies BT.601 Q10 math on known black/white macropixels.
11. **`SdvoeClientTests.cs` (3 tests)**: Verifies JSON parsing of real Semtech BlueRiver devices.
12. **`ConfigServiceTests.cs` (2 tests)**: Verifies config creation, persistence, and host IP extraction.
13. **`MulticastIpManagerTests.cs` (3 tests)**: Verifies thread-safe allocation, caching, and recycling of multicast IPs.
14. **`UnifiedRtpDemuxReceiverTests.cs` (3 tests)**: Verifies socket start/stop, channel registration, and alias routing.
15. **`MainViewModelTests.cs` (2 tests)**: Verifies layout preset initialization and video routing invocation.
16. **`LiveFunctionTests.cs` & `LiveControlServerTests.cs`**: Integration tests that automatically run when an active Control Server is present on `127.0.0.1:8090` (and auto-skip when absent).
