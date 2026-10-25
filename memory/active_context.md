# Active Context

## Current Status & Focus
* **Current Phase**: Phase 2 (MultiView layouts) is **built, unit-tested and validated on the lab hardware** (what remains: 4K PiP, four live windows, dual link - needs more devices online). Phase 1 (Single View) remains as delivered in M8/M8.1.
* **Core Architecture**: Dual-Mode Shell (Native Visual Routing Canvas + Embedded BAVM Console in WebView2 + Live Diagnostics Tab).
* **Plan / design record**: `docs/superpowers/plans/2026-10-25-phase2-multiview-layouts.md`, ADR 003 incl. its hardware-validation amendment (`memory/decisions/2026-10-25-phase2-multiview-layouts.md`).

## What Phase 2 does now (M9, uncommitted at time of writing)
1. Drag a layout tile (`Matrix 2x2`, `PIP`, `Side-by-Side`, `1+3 Quad`) onto a display card -> one drop zone per window. The decoder's **display EDID is read** (`get edid`) and the canvas is the display's preferred timing (RX1's panel: 3840x2160@60); fallback 1920x1080@60 with a visible reason. Nothing is changed on a device yet.
2. Drop a source into a window (or on the card background -> first free window). First drop re-reads the EDID, then per source: `set:scaler` (window size), stream 1 start, **frame divider ON if the estimated stream-1 bandwidth > 9 Gbps** (4K60 Matrix/PiP/Quad), create layout `avas_<preset>_<W>x<H>`, `set:multiview` at the display's fps. All background requests are awaited via `/api/request/{id}`.
3. Windows mirror the sources' live preview bitmaps; non-AVAS sources show "Preview not available". The card shows `OUT 3840x2160@60 (VP3268-4K) ...` and a bandwidth/divider note.
4. Single View tile / plain drop -> `set:video GENLOCKED` then `join`; Turn Off / Off tile -> leave each subscription, genlock, and restore full frame rate on sources the tool halved (sources already halved by the lab, e.g. VEGA_1100, are never changed).
5. Refusals show on the card (`LastError`): offline/not a TX, one TX needing two scaler sizes, hardware rejection (rolled back). A source that refuses the divider just runs at full rate (noted on the card).

## Hardware validation so far (lab, 2026-10-25; RX1 + TX2_DUAL LINK + VEGA_1100; TX1 and RX2 offline)
* RX1/PAX355S: AVP2000 FW 2.2.0.0, `video_processing: true`. TX2 and VEGA_1100: `scaler: true`, `frame_rate_divider: true` (VEGA already had its divider on - lab config).
* Matrix at FHD, Matrix at 4K60 (two 1080p windows), 1+3 Quad at 4K (2560x1440 focus window on surface 0) all loaded with SUCCESS and **user confirmed the picture**; frame divider verified (scaler out 30 fps, RX1 stays 3840x2160@60).
* Stream 1 coexists with the in-band MCU preview (`set rtp ON`) on TX2.
* `get settings` exposes each RX's **subscriptions** (source device/state) - route restore at startup is possible (the old "no query" note was wrong).
* **Leave multiview validated** (real orchestrator): `ReturnToSingleViewAsync` -> RX1 GENLOCKED 3840x2160@60, then REST `join` TX2 -> subscription idx0 STREAMING from TX2#0; the tool switched TX2's divider back to full and left VEGA_1100's own divider alone. **Off validated**: leaves each subscription, RX1 GENLOCKED with no active subs, dividers restored as above.
* **User judgement**: half-rate (30 fps) sources in a 4K60 Matrix look fine.
* **Not yet validated**: 4K PiP; four live windows (only 2 sources online); dual-link units (TX1 / RX2 offline).

## Test & Build Verification
* **218 non-live tests pass** (`dotnet test --filter "FullyQualifiedName!~Live"`), including real-XAML STA smoke tests for every preset.
* **Live tests were deliberately NOT run** (`LiveFunctionTests` route/unroute real lab hardware whenever the Control Server on 127.0.0.1:8090 is up).
* `publish/AvasRoutingTool/AvasRoutingTool.exe` is not rebuilt for Phase 2 yet. Build with `--artifacts-path <scratch>` if the app is running (Debug output is locked).
* Lab harness used for validation (throwaway, not in the repo): a console project in the session scratchpad referencing `AvasRoutingTool.csproj` that drives the real `SdvoeClient` + `MultiViewOrchestrator`.

## Current lab state (restored after the validation)
RX1 genlocked at 3840x2160@60 and joined to TX2 stream 0 (its pre-test route is unknown; TX2 was the route used by the app earlier). TX2: divider off, stream 1 stopped, MCU preview off. VEGA_1100 untouched (stream 1 running and divider on were its own state). Three test layouts remain on the Control Server (harmless; the app overwrites them): `avas_matrix2x2_1920x1080`, `avas_matrix2x2_3840x2160`, `avas_quad_1_3_3840x2160`. TX2's scaler is still sized 1920x1080 from the last multiview run.

## Known limitations
* Routes and multiview layouts are not restored on app start (but `get settings` makes it possible - next step).
* `join`/`leave` still treat PROCESSING as success (only multiview calls await the request result).

## Next Steps
1. When more devices are online: 4K PiP, four live windows, dual-link TX/RX in multiview.
2. Publish and commit M9 (code + this memory) once the user is happy.
3. Route + layout restore at startup from `get settings` subscriptions (replaces the planned `routes.json`).
4. Expose canvas override / divider mode (Auto/On/Off) in Settings.
5. Independent Audio & USB routing (Handover 05 Task 3); PTZ/camera tray (Task 4).
