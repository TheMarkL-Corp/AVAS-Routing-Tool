# Active Context

## Current Status & Focus
* **Current Phase**: Phase 1 (Single View Video Routing) Implementation & Packaging Complete.
* **Core Architecture**: Approach A (Dual-Mode Shell: Native Visual Routing Canvas + Embedded BAVM Console in WebView2).
* **Key Refinements Delivered**:
  1. Configurable BAVM URL (defaults to `http://localhost:80`, saved in `appsettings.json`).
  2. SDVoE Control Server IP derived automatically from BAVM host IP; Telnet port (6970) and REST port (8080) configurable.
  3. Strict BAVM Device Name display across all cards and routing feedback.
  4. Video Routing Only for Phase 1 (`join <rx>:HDMI:0 <tx>:HDMI:0` / `leave <rx>:HDMI:0`).
  5. Dynamic Cards: Online and offline devices dynamically reflect Control Server status as-is; offline cards are disabled from dragging.
  6. Live Video Preview: Stream 1 Scaler ingested via `UnifiedRtpDemuxReceiver` (UDP 5004/5000), decoded via `Yuv422Rasterizer`, and rendered onto `WriteableBitmap`.
  7. Bottom Layouts Bar: Single View active and selected; MultiView layouts (`Matrix 2x2`, `PIP`, `Side-by-Side`, `1+3 Quad`) staged with Phase 2 badges.

## Recent Changes
* Built and verified full test suite (`13/13` xUnit tests passing).
* Packaged self-contained standalone 64-bit Windows binary in `publish/AvasRoutingTool/AvasRoutingTool.exe`.
* Recorded ADR 002: `memory/decisions/2026-10-25-visual-routing-and-single-view-pipeline.md`.

## Next Steps
1. Deploy standalone binary to physical/virtual lab with Advantech AVAS-223 transceivers for hardware validation.
2. Prepare Phase 2 implementation for MultiView layouts (Matrix 2x2, PIP, Side-by-Side, 1+3 Quad) and viewport source assignment.
