# ADR 002: Visual Video Routing & Stream 1 Scaler Ingestion Pipeline

## Status
Accepted

## Date
2026-10-25

## Context
Phase 1 of the AVAS Routing Tool project requires delivering an application that functions with Advantech AVAS-223 transceivers and BAVM with a SigmaXG-inspired visual drag-and-drop routing canvas. The user provided critical refinements:
1. Video Routing Only for Phase 1 (`join <rx>:HDMI:0 <tx>:HDMI:0` / `leave <rx>:HDMI:0`). Audio and USB-KVM routing remain active in the embedded BAVM console.
2. Dynamic cards reflecting discovered transceivers as reported by the Control Server as-is, with badging and drag disablement for offline transceivers.
3. Strict Device Name display without EDID monitor model overrides.
4. User-configurable BAVM URL (persisted across sessions), automatically deriving the SDVoE Control Server IP from the URL host.

## Decision
1. **Core Video Routing**: Implemented `RoutingOrchestrator` wrapping `ISdvoeClient`. It commands `join <rx>:HDMI:0 <tx>:HDMI:0` over Telnet port 6970 upon dropping a TX card onto an RX card, and `leave <rx>:HDMI:0` upon unrouting.
2. **Stream 1 Scaler Ingestion**: Live previews for TX source cards are ingested using uncompressed RFC 4175 packets on UDP 5004/5000 via `UnifiedRtpDemuxReceiver`, transformed via Q10 fixed-point BT.601 math in `Yuv422Rasterizer`, and blitted directly to `WriteableBitmap.BackBuffer`.
3. **Dual-Mode Shell**: Hosted in WPF (`MainWindow.xaml`) with top-level tabs `Visual Routing` (primary canvas) and `BAVM Console` (`Microsoft.Web.WebView2` pointing to configured BAVM URL with an isolated `WebView2_UserData` cache).
4. **Layout Bar Staging**: Single View (1:1) is fully active and selected; MultiView layouts (`Matrix 2x2`, `PIP`, `Side-by-Side`, `1+3 Quad`) are visually staged with "PHASE 2" badges.

## Consequences
- Fast single-view video routing with zero interference with existing BAVM configurations.
- Minimal CPU/network footprint thanks to the Stream 1 Scaler and fixed-point color conversion.
- Extensible baseline ready for Phase 2 multiview compositing.
