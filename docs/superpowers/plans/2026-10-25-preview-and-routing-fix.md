# Live Preview, Video Routing, Link Mode & Diagnostics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable zero-latency live video preview for AVAS-223 transmitters via RFC 4175 RTP scanline reassembly and MCU RS-232 UART stream control, restore drag-and-drop Single View video routing via Semtech BlueRiver REST API (`op: join`), detect Single/Dual link mode, display stream telemetry (Multicast IP, FPS), and add manual preview lifecycle controls with strict vendor filtering.

**Architecture:** 
1. **RTP Video Pipeline**: A high-performance singleton `UnifiedRtpDemuxReceiver` binding UDP ports 5000 and 5004 with 8MB socket buffer and multi-adapter IGMP joins, demuxing incoming packets by sender/companion IP into zero-allocation `RtpHeader` structs, reassembling progressive scanlines via `ScanlineFrameReassembler`, and rasterizing YUV 4:2:2 directly into WPF `WriteableBitmap` backbuffers.
2. **AVAS MCU Stream Controller**: REST-based in-band MCU controller switching UART 1 to API mode (`op: "switch:api"`) and transmitting Cypress Baseboard MCU commands (`set rtp igmp <ip>` and `set rtp ON / OFF`) over `/api/device/{mac}` (`op: "send:rs232"`). Strict vendor filtering ensures only AVAS transceivers (`VendorId == 105`) start preview; non-AVAS units display a "Preview not supported" banner.
3. **Robust Visual Routing Engine**: WPF drag-and-drop fix (`DragOver` handling `e.Effects = DragDropEffects.Link`), issuing official Semtech BlueRiver REST `join` and `start` payloads (`POST /api/device/{rx_mac}` with `op: "join"` and `source_device`), updating DisplayCard state reactively.
4. **Card UI & Link Mode Telemetry**: Badges indicating "Single Link" vs "Dual Link" derived from `MULTI_LINK_TRANSMITTER` node configuration and device naming, preview multicast IP display, FPS counter, and manual `[ 🔄 Refresh ]`, `[ ▶ Start Preview ]`, and `[ ⏹ Stop Preview ]` controls.

**Tech Stack:** C# .NET 8 (`net8.0-windows`), WPF, System.Net.Sockets, System.Buffers.Binary, xUnit, FluentAssertions, Moq.

**Spec:** [`docs/superpowers/specs/2026-10-25-avas-routing-tool-design.md`](file:///c:/Users/POC-615/Documents/GitHub/AVAS-Routing-Tool/docs/superpowers/specs/2026-10-25-avas-routing-tool-design.md)

---

## Global Constraints

* **Platform Floor**: Windows 10/11 x64, .NET 8.0 SDK.
* **Strict AVAS Preview Filtering**: Live RTP preview is restricted to AVAS transceivers (`VendorId == 105`). Non-AVAS transceivers (e.g. VEGA-1100, `VendorId == 192`) MUST display "Preview not supported" without sending MCU commands.
* **Official Video Routing API**: Video routing must use Semtech BlueRiver REST `POST /api/device/{rx_mac}` with `{ "op": "join", "source_device": "<tx_mac>", "stream_type": "HDMI", "stream_index": 0, "subscription_index": 0 }`.
* **Zero GDI+ Leaks**: Frame rendering must use `WriteableBitmap.Lock()` / `AddDirtyRect()` and unsafe pointer blitting from unmanaged buffers.
* **Strict Device Naming**: Display and Source cards display BAVM `DeviceName` only (no raw EDID sink strings).
* **Link Mode Detection**: Show whether AVAS TX is "Single Link" or "Dual Link" based on `MULTI_LINK_TRANSMITTER` node config or paired naming.
* **Phase 1 Routing Scope**: Video routing only; do not route audio or USB.
* **Memory Integrity**: Synchronize `memory/active_context.md` and `memory/progress.md` after completion.

## Review Focus

1. **WPF DragOver Suppression**: If `DragOver` is not handled with `e.Effects = DragDropEffects.Link` on `DisplayCardView.xaml`, WPF resets effect to `None` during mouse movement and silences `Drop`.
2. **Companion IP Demuxing in Dual Link**: AVAS-223 Dual Link transmitters stream preview packets from companion SFP B IP (e.g. `10.32.32.14`) while the device primary IP is `10.32.32.12`. The demuxer must register all node IP addresses as aliases.
3. **Multi-Adapter IGMP Multicast Join**: Windows will drop multicast traffic if `UdpClient.JoinMulticastGroup` is bound to the wrong local interface. Must resolve the local NIC matching the SDVoE subnet (e.g. `10.32.32.x`).
4. **RS-232 UART API Mode Prerequisite**: Before issuing `set rtp ON`, UART 1 must be confirmed in API mode (`POST /api/device/{mac}` with `stream_type: "rs232", stream_index: 1, op: "switch:api"`).
5. **Non-AVAS Command Guard**: Sending RS-232 MCU commands to non-AVAS hardware causes device errors or timeouts. Non-AVAS hardware must be guarded by `VendorId == 105`.

---

## File Structure

```
src/AvasRoutingTool/
├── Sdvoe/
│   ├── AvasDevice.cs                  // Add VendorId, ProductId, LinkMode, IsAvasDevice, IsPreviewSupported
│   ├── ISdvoeClient.cs                // Add StartAvasPreviewAsync, StopAvasPreviewAsync, JoinStreamAsync, LeaveStreamAsync
│   ├── SdvoeClient.cs                 // Implements REST MCU preview commands, REST join/leave, node alias extraction
│   └── RoutingOrchestrator.cs         // Connects DragDrop execution to SdvoeClient.JoinStreamAsync
├── Rtp/
│   ├── RtpHeader.cs                   // 20-byte RFC 3550 + RFC 4175 parser (zero heap allocation ref struct)
│   ├── ScanlineFrameReassembler.cs    // Line-based slotting, out-of-order absorption, marker bit frame boundary
│   ├── UnifiedRtpDemuxReceiver.cs     // Singleton UDP listener (ports 5000/5004), sender IP demuxing, bitmap updates
│   └── Yuv422Rasterizer.cs            // SIMD/unsafe YUV422 to BGR24 converter for WriteableBitmap backbuffer
├── ViewModels/
│   ├── SourceCardViewModel.cs         // LinkMode, PreviewMulticastIp, IsPreviewSupported, Refresh/Start/Stop commands, FPS
│   └── DisplayCardViewModel.cs        // ActiveSourceMac, ActiveSourceName, IsRouted, Unroute command
└── Views/
    ├── SourceCardView.xaml            // UI layout with LinkMode badge, Multicast IP, preview controls, "Preview not supported" banner
    ├── SourceCardView.xaml.cs         // Drag source initiation
    ├── DisplayCardView.xaml           // DragOver, DragEnter, Drop bindings
    └── DisplayCardView.xaml.cs        // Handles DragOver and Drop routing events

tests/AvasRoutingTool.Tests/
├── RtpHeaderTests.cs                  // Unit tests verifying RFC 3550 + RFC 4175 parsing
├── ScanlineFrameReassemblerTests.cs   // Unit tests verifying progressive line assembly and packet reordering
├── AvasDeviceTests.cs                 // Unit tests for LinkMode, IsAvasDevice, IsPreviewSupported
└── RoutingOrchestratorTests.cs        // Unit tests for REST JoinStream and state update
```

---

### Task 1: Update `AvasDevice` Model with Vendor Filtering & Link Mode

**Files:**
- Modify: `src/AvasRoutingTool/Sdvoe/AvasDevice.cs`
- Create: `tests/AvasRoutingTool.Tests/AvasDeviceTests.cs`

**Deliverables:**
- Properties: `int VendorId`, `int ProductId`, `string LinkMode` ("Single Link", "Dual Link", "N/A"), `bool IsAvasDevice => VendorId == 105`, `bool IsPreviewSupported => IsAvasDevice && IsTransmitter`.
- Node IP aliases list: `List<string> NodeIps` to store secondary node/SFP IP addresses for RTP demuxing.

- [ ] **Step 1: Write failing test in `AvasDeviceTests.cs`**
  - Verify that `VendorId == 105` returns `IsAvasDevice == true`.
  - Verify that `VendorId == 192` returns `IsAvasDevice == false` and `IsPreviewSupported == false`.
  - Verify `LinkMode` default and property assignment.
- [ ] **Step 2: Run `dotnet test` to confirm test failure**
- [ ] **Step 3: Update `AvasDevice.cs`**
  - Add `VendorId`, `ProductId`, `LinkMode`, `IsAvasDevice`, `IsPreviewSupported`, and `NodeIps`.
- [ ] **Step 4: Run `dotnet test` to confirm test passes**
- [ ] **Step 5: Git commit task changes**

---

### Task 2: Implement RFC 4175 Parser (`RtpHeader`) and `ScanlineFrameReassembler`

**Files:**
- Create: `src/AvasRoutingTool/Rtp/RtpHeader.cs`
- Create: `src/AvasRoutingTool/Rtp/ScanlineFrameReassembler.cs`
- Create: `tests/AvasRoutingTool.Tests/RtpHeaderTests.cs`
- Create: `tests/AvasRoutingTool.Tests/ScanlineFrameReassemblerTests.cs`

**Deliverables:**
- Zero-allocation `ref struct RtpHeader` parsing 20-byte combined RFC 3550 + RFC 4175 header (Sequence, Timestamp, SSRC, LineNo, Offset, Marker, Payload).
- `ScanlineFrameReassembler` collecting progressive scanlines per line number, assembling complete frame buffer when `Marker == true`, supporting partial frames if packets are lost.

- [ ] **Step 1: Write unit tests in `RtpHeaderTests.cs`**
  - Test valid 20-byte header with line 0, offset 0, marker true/false.
  - Test short buffers (< 20 bytes) return false.
  - Test RFC 3550 version != 2 returns false.
- [ ] **Step 2: Write unit tests in `ScanlineFrameReassemblerTests.cs`**
  - Ingest packet line 0, packet line 1 (with marker = true) -> outputs assembled frame of 2 lines.
  - Out of order lines: ingest line 1 then line 0 (marker on line 1) -> reassembles in correct order.
- [ ] **Step 3: Run `dotnet test` to confirm failures**
- [ ] **Step 4: Implement `RtpHeader.cs` and `ScanlineFrameReassembler.cs`**
- [ ] **Step 5: Run `dotnet test` to confirm all RTP tests pass**
- [ ] **Step 6: Git commit task changes**

---

### Task 3: Overhaul `UnifiedRtpDemuxReceiver` with Multi-NIC & Sender/Companion Demuxing

**Files:**
- Modify: `src/AvasRoutingTool/Rtp/UnifiedRtpDemuxReceiver.cs`
- Create: `tests/AvasRoutingTool.Tests/UnifiedRtpDemuxReceiverTests.cs`

**Deliverables:**
- Binds UDP ports 5000 and 5004 with `ReuseAddress = true` and 8MB buffer.
- Discovers local active network interface matching the transmitter subnet (e.g. `10.32.32.x`) to issue IGMP `JoinMulticastGroup(mcastIp, localNicIp)`.
- Channel Demuxing: Demux by `senderIp`, registered secondary aliases (`NodeIps`), and /24 subnet fallback.
- `DemuxChannel` manages its own `ScanlineFrameReassembler`, rolling FPS counter, and UI dispatch to `WriteableBitmap` via `Yuv422Rasterizer`.

- [ ] **Step 1: Write tests for `UnifiedRtpDemuxReceiver` demuxing and alias registration**
- [ ] **Step 2: Update `UnifiedRtpDemuxReceiver.cs`**
- [ ] **Step 3: Run `dotnet test` to confirm tests pass**
- [ ] **Step 4: Git commit task changes**

---

### Task 4: Implement AVAS MCU Preview Control & REST Video Routing in `SdvoeClient`

**Files:**
- Modify: `src/AvasRoutingTool/Sdvoe/ISdvoeClient.cs`
- Modify: `src/AvasRoutingTool/Sdvoe/SdvoeClient.cs`
- Modify: `src/AvasRoutingTool/Sdvoe/RoutingOrchestrator.cs`
- Modify: `tests/AvasRoutingTool.Tests/SdvoeClientTests.cs`
- Modify: `tests/AvasRoutingTool.Tests/RoutingOrchestratorTests.cs`

**Deliverables:**
- Discovery enhancement:
  - Populate `VendorId` and `ProductId` from device identity.
  - Parse `nodes` for type `MULTI_LINK_TRANSMITTER` -> extract `configuration.link_mode` ("DUAL" vs "SINGLE") or fallback to device name matching ("DUAL LINK").
  - Populate `NodeIps` with all IP addresses from `nodes[].status.ip.address`.
- MCU Preview Stream Lifecycle:
  - `EnsureUartApiModeAsync(string mac)`: `POST /api/device/{mac}` `{ "stream_type": "rs232", "stream_index": 1, "op": "switch:api" }`.
  - `StartAvasPreviewAsync(string mac, string multicastIp)`:
    - Verify `device.IsPreviewSupported`.
    - `POST /api/device/{mac}` `{ "op": "send:rs232", "port_index": 1, "data_string": "set rtp igmp <multicastIp>\r\n" }`.
    - `POST /api/device/{mac}` `{ "op": "send:rs232", "port_index": 1, "data_string": "set rtp ON\r\n" }`.
  - `StopAvasPreviewAsync(string mac)`:
    - `POST /api/device/{mac}` `{ "op": "send:rs232", "port_index": 1, "data_string": "set rtp OFF\r\n" }`.
- Video Routing:
  - `JoinStreamAsync(string rxMac, string txMac)`:
    - Ensure TX HDMI stream is active (`POST /api/device/{txMac}` `{ "op": "start", "stream_type": "HDMI", "stream_index": 0 }`).
    - Route RX to TX (`POST /api/device/{rxMac}` `{ "op": "join", "source_device": txMac, "stream_type": "HDMI", "stream_index": 0, "subscription_index": 0 }`).
  - `LeaveStreamAsync(string rxMac)`:
    - `POST /api/device/{rxMac}` `{ "op": "leave", "subscription_type": "HDMI", "subscription_index": 0 }`.

- [ ] **Step 1: Write unit tests in `SdvoeClientTests.cs` for MCU command generation and routing payloads**
- [ ] **Step 2: Update `ISdvoeClient.cs` and `SdvoeClient.cs`**
- [ ] **Step 3: Update `RoutingOrchestrator.cs` to call `JoinStreamAsync` and `LeaveStreamAsync`**
- [ ] **Step 4: Run `dotnet test` to verify passing**
- [ ] **Step 5: Git commit task changes**

---

### Task 5: Fix WPF Drag-and-Drop in `DisplayCardView` and ViewModels

**Files:**
- Modify: `src/AvasRoutingTool/Views/DisplayCardView.xaml`
- Modify: `src/AvasRoutingTool/Views/DisplayCardView.xaml.cs`
- Modify: `src/AvasRoutingTool/ViewModels/DisplayCardViewModel.cs`

**Deliverables:**
- Add `DragOver="Card_DragOver"` in `DisplayCardView.xaml`.
- In `DisplayCardView.xaml.cs`:
  - `Card_DragOver`: If dragging payload is `SourceCardViewModel`, set `e.Effects = DragDropEffects.Link; e.Handled = true;`.
  - `Card_Drop`: Retrieve `SourceCardViewModel` from `e.Data`, execute `DisplayCardViewModel.RouteFromCommand.Execute(source)`.
- In `DisplayCardViewModel`:
  - `RouteFromCommand`: calls `RoutingOrchestrator.RouteVideoAsync(source.MacAddress, MacAddress)`.
  - Updates `ActiveSourceMac`, `ActiveSourceName`, `IsRouted = true`.
  - Provides `UnrouteCommand` to clear route.

- [ ] **Step 1: Update `DisplayCardView.xaml` and `.xaml.cs` with `DragOver` handler**
- [ ] **Step 2: Update `DisplayCardViewModel.cs` with reactive routing state properties**
- [ ] **Step 3: Run `dotnet test`**
- [ ] **Step 4: Git commit task changes**

---

### Task 6: Enhance Source Card UI with Telemetry, Link Mode & Preview Action Buttons

**Files:**
- Modify: `src/AvasRoutingTool/ViewModels/SourceCardViewModel.cs`
- Modify: `src/AvasRoutingTool/Views/SourceCardView.xaml`
- Modify: `src/AvasRoutingTool/Views/SourceCardView.xaml.cs`

**Deliverables:**
- Properties in `SourceCardViewModel`:
  - `string LinkMode`: "SINGLE LINK", "DUAL LINK", or "N/A"
  - `string PreviewMulticastIp`: e.g. `225.0.0.1:5000`
  - `bool IsPreviewSupported`: true only for AVAS TX (`VendorId == 105`)
  - `bool IsPreviewStreaming`: true when preview is active
  - `double Fps`: current frame rate
  - `ICommand RefreshPreviewCommand`
  - `ICommand StartPreviewCommand`
  - `ICommand StopPreviewCommand`
- UI elements in `SourceCardView.xaml`:
  - Link Mode pill badge (`"SINGLE LINK"` / `"DUAL LINK"`) next to the device name.
  - Multicast IP info banner on card footer (`"MCAST: 225.0.0.1:5000"`).
  - FPS indicator badge when streaming (`"30.0 FPS"`).
  - If `!IsPreviewSupported`: Display a sleek overlay with icon: `"Preview not supported for non-AVAS device"`.
  - Card button toolbar:
    - `[ 🔄 Refresh ]`: Force re-send MCU commands and re-join multicast group.
    - `[ ▶ Start ]` / `[ ⏹ Stop ]`: Toggle preview streaming on demand.

- [ ] **Step 1: Add LinkMode, PreviewMulticastIp, and Commands to `SourceCardViewModel.cs`**
- [ ] **Step 2: Update `SourceCardView.xaml` with modern dark-themed badges, telemetry labels, and action buttons**
- [ ] **Step 3: Run `dotnet test` to verify no regressions**
- [ ] **Step 4: Git commit task changes**

---

### Task 7: End-to-End Live Verification & Standalone Binary Publishing

**Files:**
- Publish target: `publish/AvasRoutingTool/AvasRoutingTool.exe`
- Documentation / Memory: `memory/active_context.md`, `memory/progress.md`

**Deliverables:**
- Run end-to-end live test against local BlueRiver Control Server on `127.0.0.1`:
  - Confirm `TX2_DUAL LINK_SFPA` displays "DUAL LINK" badge, receives live video preview (FPS > 0), and renders on `WriteableBitmap`.
  - Confirm `VEGA_1100` displays "Preview not supported" and does not attempt MCU commands.
  - Confirm dragging `TX2_DUAL LINK_SFPA` or `VEGA_1100` onto `RX1_c059e_SFPA` successfully executes `join` on Control Server and updates Display card UI to show routed source name.
  - Test `[ ⏹ Stop Preview ]` and `[ ▶ Start Preview ]` buttons.
- Build and publish single-file standalone distribution package.
- Update memory tracking files.

- [ ] **Step 1: Execute all unit tests (`dotnet test`)**
- [ ] **Step 2: Publish standalone executable (`dotnet publish -c Release -r win-x64 --self-contained true`)**
- [ ] **Step 3: Run live verification script checking live Control Server state**
- [ ] **Step 4: Update `memory/active_context.md` and `memory/progress.md`**
- [ ] **Step 5: Git commit and present results to user**
