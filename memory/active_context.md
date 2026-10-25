# Active Context

## Current Status & Focus
* **Current Phase**: Phase 1 (Single View Video Routing) Live Preview & Drag-to-Route Fixes Completed & Verified on Hardware.
* **Core Architecture**: Approach A (Dual-Mode Shell: Native Visual Routing Canvas + Embedded BAVM Console in WebView2 + Live Diagnostics Tab).
* **Key Fixes & Refinements Delivered**:
  1. **AVAS MCU Preview Control**: Integrated REST in-band MCU Cypress Baseboard controller (`POST /api/device/{mac}` with `switch:api`, `set rtp igmp <multicastIp>`, `set rtp ON / OFF` over UART node 1 `rs232:1`). Tested live on `TX2_DUAL LINK_SFPA` (`74fe488b07bb`), streaming 425+ RFC 4175 datagrams in 1.5 seconds from companion SFP `10.32.32.14:5000`.
  2. **Strict Device Filtering**: Guarded preview lifecycle by `VendorId == 105`. Non-AVAS transceivers (e.g. Advantech `VEGA_1100`, `VendorId == 192`) skip MCU commands and cleanly display `"Preview not supported"` on their card view.
  3. **WPF Drag-to-Route Restoration**: Resolved drag-to-drop suppression on `DisplayCardView.xaml` by implementing `DragOver` (`e.Effects = DragDropEffects.Link; e.Handled = true;`) and handling both `AvasDevice` and `SourceCardViewModel` payloads.
  4. **Official REST Video Routing**: Implemented Semtech BlueRiver REST `join` and `leave` (`POST /api/device/{rxMac}` with `op: "join"` / `op: "leave"`) with automatic transmitter start (`op: "start"`). Tested live between `TX2_DUAL LINK_SFPA` and `RX1_c059e_SFPA`.
  5. **Link Mode & Telemetry UI**: Added detection and stylish badge for "Single Link" vs "Dual Link", multicast IP display (`MCAST: 225.0.0.X:5000`), FPS badge, and manual preview lifecycle action buttons (`[ 🔄 Refresh ]`, `[ ▶ Start ]`, `[ ⏹ Stop ]`).
  6. **Ordered Dynamic Multicast Allocation & Startup Delay**: Ordered transmitters by `DeviceName` so TX1 gets the first IP in the range (`225.0.0.1`), TX2 gets the second, and delayed preview auto-start by 500ms after device handshake.

## Recent Changes
* Added `VendorId`, `ProductId`, `LinkMode`, `IsAvasDevice`, `IsPreviewSupported`, and `NodeIps` to `AvasDevice.cs`.
* Implemented `RtpHeader.cs` (20-byte combined RFC 3550 + RFC 4175 parser) and `ScanlineFrameReassembler.cs`.
* Overhauled `UnifiedRtpDemuxReceiver.cs` and `Yuv422Rasterizer.cs` with zero-allocation unsafe blitting, multi-adapter subnet IGMP joining, and alias demuxing.
* Implemented REST MCU preview and REST video routing in `SdvoeClient.cs`.
* Added `DragOver` handler to `DisplayCardView.xaml` and `.xaml.cs`.
* Added Link Mode badge, Multicast IP label, and action buttons (`Refresh`, `Start`, `Stop`) to `SourceCardView.xaml` and `SourceCardViewModel.cs`.
* All 27 unit tests passing in `tests/AvasRoutingTool.Tests/`.
* Re-published standalone release package in `publish/AvasRoutingTool/AvasRoutingTool.exe`.

## Next Steps
1. User testing and visual sign-off of the standalone desktop application (`publish/AvasRoutingTool/AvasRoutingTool.exe`).
2. Plan Phase 2 MultiView canvas layouts (Matrix 2x2, PIP, Side-by-Side, 1+3 Quad).
