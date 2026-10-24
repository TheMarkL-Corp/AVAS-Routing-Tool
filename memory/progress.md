# Progress & Milestones

## Roadmap Status

- [x] **M0: Memory & Architecture Specification**
  - [x] Design specification written (`docs/superpowers/specs/2026-10-25-avas-routing-tool-design.md`)
  - [x] Memory sub-directory structure created (`memory/`)
  - [x] Canonical domain glossary established (`memory/GLOSSARY.md`)
  - [x] Architecture Decision Record ADR 001 created (`memory/decisions/`)
  - [x] Repository initialization & Git baseline commit

- [ ] **M1: Solution Scaffolding & Core Services Migration**
  - [ ] .NET 8 WPF Solution & Project structure (`AvasRoutingTool.sln`)
  - [ ] Migration and verification of `ConfigService`, `MulticastIpManager`
  - [ ] Migration and verification of `SdvoeClient` (TCP 6970 / REST 8080)
  - [ ] Migration and verification of `UnifiedRtpDemuxReceiver` (UDP 5000/5004)
  - [ ] Unit test suite setup and passing

- [ ] **M2: Dual-Mode App Shell & Navigation**
  - [ ] WPF Shell with modern toolbar & status indicators
  - [ ] Segmented tab navigation (`Visual Routing` / `BAVM Console` / `Diagnostics`)
  - [ ] Microsoft WebView2 integration with isolated `WebView2_UserData`
  - [ ] Settings dialog with network adapter selector

- [ ] **M3: Sources (TX) Row & Live Preview Engine**
  - [ ] Source card UI with live `WriteableBitmap` surface
  - [ ] Automatic device filtering for AVAS-223 TX encoders
  - [ ] Concurrency preview stream activation on UDP 5004
  - [ ] FPS counter pill, IP/MAC display, audio presence badge

- [ ] **M4: Displays (RX) Row & Single-View Routing**
  - [ ] Display card UI with viewport surface & status badge
  - [ ] Drag-and-drop gesture engine (Source TX card -> Display RX card)
  - [ ] Automated 3-in-1 routing orchestrator (Video `join HDMI:0`, Audio, USB HID)
  - [ ] Disconnect / `Off` layout handling (`leave HDMI:0`)

- [ ] **M5: Bottom Layouts Bar & MultiView Staging**
  - [ ] Layout selector bar with Single View active
  - [ ] MultiView layout visual presets staged for Phase 2
  - [ ] Drag-layout-to-display gesture handler

- [ ] **M6: Verification, Packaging & Documentation**
  - [ ] End-to-end automated testing
  - [ ] Standalone portable build generation
  - [ ] Final user manual and operational checklist
