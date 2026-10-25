# Progress & Milestones

## Roadmap Status

- [x] **M0: Memory & Architecture Specification**
  - [x] Design specification written (`docs/superpowers/specs/2026-10-25-avas-routing-tool-design.md`)
  - [x] Memory sub-directory structure created (`memory/`)
  - [x] Canonical domain glossary established (`memory/GLOSSARY.md`)
  - [x] Architecture Decision Record ADR 001 created (`memory/decisions/2026-10-25-init-memory-and-hybrid-shell.md`)
  - [x] Architecture Decision Record ADR 002 created (`memory/decisions/2026-10-25-visual-routing-and-single-view-pipeline.md`)
  - [x] Repository initialization & Git baseline commit

- [x] **M1: Solution Scaffolding & Core Services Migration**
  - [x] .NET 8 WPF Solution & Project structure (`AvasRoutingTool.sln`)
  - [x] Implementation and verification of `ConfigService`, `MulticastIpManager`
  - [x] Implementation and verification of `SdvoeClient` (TCP 6970 / REST 8090)
  - [x] Semtech BlueRiver schema discovery (`get all identity` & REST `/api/device/ALL`)
  - [x] Implementation and verification of `UnifiedRtpDemuxReceiver` (UDP 5000/5004) and `Yuv422Rasterizer`
  - [x] Unit test suite passing (16/16 passed)

- [x] **M2: Dual-Mode App Shell & Navigation**
  - [x] Slate Dark Theme & Vector Icons (`Theme.xaml`, `Icons.xaml`)
  - [x] Segmented tab navigation (`Visual Routing` / `BAVM Console` / `Diagnostics & Logs`)
  - [x] Microsoft WebView2 integration with isolated `WebView2_UserData`
  - [x] Settings dialog with persistent BAVM URL, host-derived Control Server IP, and ports
  - [x] In-app real-time diagnostics log viewer and disk logging (`app.log`)

- [x] **M3: Sources (TX) Row & Live Preview Engine**
  - [x] Source card UI with live `WriteableBitmap` surface
  - [x] Strict BAVM `DeviceName` header
  - [x] Concurrency preview stream activation on UDP 5004/5000
  - [x] FPS counter pill, IP/MAC display, dynamic online/offline indicators

- [x] **M4: Displays (RX) Row & Single-View Routing**
  - [x] Display card UI with drop target surface & status badge
  - [x] Drag-and-drop gesture engine (Source TX card -> Display RX card)
  - [x] Video routing orchestrator (`join <rx>:HDMI:0 <tx>:HDMI:0`)
  - [x] Disconnect / Unroute handling (`leave <rx>:HDMI:0`)

- [x] **M5: Bottom Layouts Bar & MultiView Staging**
  - [x] Layout selector bar with Single View active and ready
  - [x] MultiView layout visual presets staged for Phase 2 (`Matrix 2x2`, `PIP`, `Side-by-Side`, `1+3 Quad`)

- [x] **M6: Verification, Packaging & Documentation**
  - [x] Live physical transceiver verification against active BlueRiver Control Server (4 devices discovered: 2 TX, 2 RX)
  - [x] End-to-end automated test suite passing (16/16)
  - [x] Standalone portable build generation (`publish/AvasRoutingTool/AvasRoutingTool.exe`)
  - [x] Complete memory synchronization

- [x] **M7: Hardware Live Preview, Video Routing & Telemetry Overhaul**
  - [x] RFC 4175 zero-allocation header parser (`RtpHeader.cs`) and progressive scanline frame reassembler (`ScanlineFrameReassembler.cs`)
  - [x] In-band Cypress Baseboard MCU UART 1 preview lifecycle control (`switch:api`, `set rtp igmp`, `set rtp ON / OFF`)
  - [x] Strict AVAS transceiver preview filtering (`VendorId == 105`) with `"Preview not supported"` badge for non-AVAS units
  - [x] Dual Link detection ("Single Link" vs "Dual Link") with SFP B companion IP demux alias registration
  - [x] WPF Drag-and-drop drop-suppression resolution with `DragOver` and multi-type payload support
  - [x] Official REST video routing (`POST /api/device/{rxMac}` with `op: "join"` and `source_device`)
  - [x] Telemetry labels (Multicast IP, FPS) and card controls (`Refresh`, `Start Preview`, `Stop Preview`)
  - [x] Deterministic sequential multicast IP allocation ordered by `DeviceName` with 0.5s startup delay
  - [x] Expanded test suite passing (27/27 passed) and updated standalone package (`publish/AvasRoutingTool/AvasRoutingTool.exe`)

- [x] **M8: Decoder Un-Route, Live Preview Mirroring & Responsive FullHD Layout**
  - [x] Multi-modal Decoder Un-Routing: Red `[ ⏻ Turn Off / Disconnect ]` card button dispatching REST `leave` (`op: "leave"`) and drag-and-drop support from bottom `"Off"` layout tile (`LayoutItemViewModel.Id == "off"`).
  - [x] Live Video Preview Mirroring: Zero-overhead reactive mirroring of active source `PreviewBitmap` onto routed `DisplayCardView`, with active source tag overlay (`● <SourceName>`).
  - [x] Fallback State for Non-AVAS Sources: Display cards routed to non-AVAS transmitters (e.g. `VEGA_1100`) clearly show `"Routed to <SourceName>"` with `"Preview Not Available (Non-AVAS Device)"` notice.
  - [x] Fluid Responsive Card Dimensions: Expanded cards from fixed 320px/300px blocks to `Width="440" Height="295" Margin="10"` featuring 16:9 widescreen video preview surfaces, with `WrapPanel` and row `MinHeight="330"` eliminating void space on FullHD (1920x1080) screens.
  - [x] Architecture Polish: Extracted clean, standalone `RelayCommand.cs` in `AvasRoutingTool.ViewModels`.
  - [x] Unit test suite expanded and passing (32/32 tests passed) and updated standalone package (`publish/AvasRoutingTool/AvasRoutingTool.exe`).

