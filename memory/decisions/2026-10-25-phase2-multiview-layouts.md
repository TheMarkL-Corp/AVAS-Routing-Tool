# ADR 003: Phase 2 MultiView Layouts - lazy apply, stream 1, 16:9 windows

## Status
Accepted (design and code). **Hardware behaviour not yet validated on the lab units.**

## Date
2026-10-25

## Context
Phase 2 turns the staged `Matrix 2x2`, `PIP`, `Side-by-Side` and `1+3 Quad` tiles into working layouts. The Control Server multiview API (SDVoE API Reference PDS-062489 3.9.0.0, §5.2 pp.146-166, examples pp.166-169) is layout/window/surface based:
`POST /api/multiview/layout/{name}` (create), `.../window/{index}` (create), `set:scaler` + `start` HDMI stream 1 on each TX, then `set:multiview` on the RX with a `subscriptions` array. The lab Control Server only ships the read-only `compatibility_4k_2x2` layout.

Open questions in the sources: how the scaler treats aspect ratio (it takes only width and height), what the RX does on `join` after multiview, and whether the AVAS-223 firmware honours all of it (layer L4, pending validation).

## Decision
1. **Lazy hardware apply.** Dropping a layout tile on a display only builds empty windows in the UI (`MultiViewOrchestrator.BeginLayout`). The first source dropped into a window performs the full sequence: `set:scaler` + `start` stream 1 for the source, create the layout (once), `set:multiview` with all current subscriptions. Reason: the API rejects an empty `subscriptions` list (p.165) and this avoids a black-screen flash.
2. **Stream 1, stream 0 untouched.** Windows are fed by each TX's scaled stream 1. The reference examples stop stream 0 (`free: true`); we do not, because other decoders in Single View may be using it.
3. **Window size = scaler size, all windows 16:9.** The reference sets each TX scaler to its window size, so the picture is 1:1 in the window. Presets are built from 16:9 cells (`MultiViewPresets.Fit`), so a 16:9 source is never stretched; leftover canvas is black. Consequence: one TX has one stream-1 size, so the same source in windows of different sizes (also across decoders) is refused up front (`FindScalerConflict`).
4. **One window per source per layout.** A duplicated source stream is `ILLEGAL_ARGUMENT` (p.165), so dropping a source that is already in the layout moves it.
5. **Subscription index = target surface.** True of every example in the reference. Surfaces: windows larger than 2048x1080 take surface 0, the rest take 1..11; 17-20 are never used because they alias surface 0 (p.166).
6. **Canvas = 1920x1080 @ 60 by default**, a safe output for any lab display; configurable through `MultiViewOrchestrator` constructor (not yet surfaced in `AppConfig`). Layout names `avas_<preset>_<W>x<H>` cannot clash with built-ins.
7. **Back to Single View** = `set:video` `GENLOCKED` (p.119), then the normal REST `join`. `Off` on a multiview display = `leave` each subscription, then genlock. A source already routed in Single View is carried into window 1 when a layout is applied.
8. **Layout ids stay as in code** (`single`, `matrix2x2`, `pip`, `side_by_side`, `quad_1_3`); the Handover doc's `sideBySide` / `quad1Plus3` spellings were wrong.
9. **Composited preview without new sockets.** Each window mirrors its source's `PreviewBitmap` reference (same pattern as M8); `FractionalLayoutPanel` lays windows out by fractional rect, letterboxed to the canvas aspect.

## Consequences
- Pure, unit-tested layers: geometry (`MultiViewPresets`), REST shapes (`MultiViewRequests`), sequencing (`MultiViewOrchestrator`), client REST order (`SdvoeClient` with a recording `HttpMessageHandler`).
- Operators get an explicit message instead of a silent failure when a drop is refused (`DisplayCardViewModel.LastError`).
- A 4K canvas works in the geometry layer but needs 4K sources/bandwidth validation (recommended <= 9 Gbps per TX and RX, API §5.1.1).
- **Risks to close on hardware** (see the plan's validation gate): stream 1 vs the in-band MCU preview (`set rtp ON`), dual-link TX/RX in multiview, non-AVAS sources, scaler aspect handling, and leaving multiview via genlock + join.
- Existing routes are still not restored at startup (unchanged limitation); multiview state is also in memory only.

## Amendment (2026-10-25, after the first hardware validation on the lab units)
Validated on RX1 (AVP2000, FW 2.2.0.0) with TX2_DUAL LINK (AVAS) and VEGA_1100 (non-AVAS, AVP2000T FW 2.1.0.0): layout create, scaler, stream 1, `set:multiview` and the picture all behave as designed (user-confirmed at FHD, 4K Matrix and 4K 1+3 Quad). Changes made because of it:

1. **Canvas comes from the display's EDID, not a fixed 1080p** (decision 6 is superseded). `get edid` on the RX returns the attached display's EDID in `HDMI_MONITOR.status.edid`. `EdidInfo` parses it; `MultiViewCanvasPolicy.Choose` takes the preferred progressive timing (<= 3840x2160, width % 8 == 0, 640x360 minimum), rounds the refresh rate, and falls back to 1920x1080@60 with a visible reason when the EDID is missing, has a bad checksum or has no usable timing. The EDID is read when a layout is applied and again right before the first hardware apply (the display may have been swapped; the card is redrawn if so). RX1's panel is 3840x2160@60, which the old fixed 1080p undersold.
2. **Background requests are awaited.** `set:multiview`, scaler, start, layout calls and `get` now follow `/api/request/{id}` until they finish and treat a non-empty `result.error` as failure (a device can refuse after the call returned PROCESSING/SUCCESS). `join`/`leave` still do not.
3. **Stream-1 frame divider.** When the estimated stream-1 bandwidth (window pixels x fps x 24 bit) exceeds 9 Gbps, each source's stream 1 is halved (`set:property nodes[SCALER:0].inputs[main:0].configuration.source.value` = 1; API §7.15). 4K60 Matrix 11.9 -> ~6.0 Gbps, 4K PiP 12.7 -> ~6.3, 4K 1+3 Quad 9.3 -> ~4.6; Side-by-Side and anything at FHD stay at full rate. Only sources the tool halved are restored to full rate when no heavy layout uses them any more (the current divider state is read first, so a source configured that way by the lab - VEGA_1100 - is never changed back). A source that refuses the divider keeps full rate; the card says so. The decoder output stays at the display's rate; the sources' stream 1 runs at half.
4. Findings: `get settings` returns each RX's subscriptions (source device and state) - route restore on startup is possible, contrary to the earlier "no subscription query" note. The AVAS TX2 scaler outputs YCbCr 4:4:4 8-bit (24 bit/pixel, same bandwidth as the RGB assumption). Multiview with stream 1 coexists with the in-band MCU preview on TX2 (1,081 RTP packets / 4 s while stream 1 fed RX1).
5. Also validated on hardware afterwards: leaving multiview (`set:video GENLOCKED` then REST `join` -> RX1 GENLOCKED 3840x2160@60, subscription idx0 STREAMING from TX2#0), `Off` (leaves each subscription, genlocks, no active subscriptions left), and the divider restore (TX2 back to full rate, VEGA_1100's own divider untouched). The user judged half-rate sources in the 4K Matrix acceptable. Still not validated: 4K PiP (main window ~12.7 Gbps at full rate, ~6.3 with the divider), four live windows (only two sources in the lab), dual-link units (RX2 and TX1 were offline).