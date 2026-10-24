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
