# Single View: enforce Fast Switch at the RX EDID resolution

Status: decided with Mark on 2026-10-27 (design interview). Implementation plan: `docs/superpowers/plans/2026-10-27-single-view-fast-switch-edid.md`.

## Requirement

When a TX is routed into an RX in Single View, the target RX must first be put into **Fast Switch** (`display_mode FAST_SWITCHED`) at the resolution of **its display's EDID**, then the video `join` is sent. The tool no longer sets `GENLOCKED` anywhere.

## Decisions

| # | Decision |
|---|---|
| D1 | Applies to every path that ends in a Single View state: drop/tap route, return from a multiview, exit of an external layout, Off, copy/undo restore. The tool never sends `GENLOCKED` again. |
| D2 | Resolution = `MultiViewCanvasPolicy.Choose(edid)` (first progressive timing, width % 8 == 0, 640x360..3840x2160, rounded refresh). One format policy for Single View and multiview. |
| D3 | EDID unreadable / checksum invalid / no usable timing: fall back to 1920x1080@60 Fast Switch, log a warning, still route. The card shows the reason (`MultiViewCanvas.Description` already carries it). |
| D4 | Skip `set:video` when `ReadReceiverStateAsync` shows `FAST_SWITCHED` with the same width, height and fps. `FAST_SWITCHED_CROP` / `_STRETCH` do not count as a match. Unreadable state: send it. |
| D5 | Aspect handling is plain `FAST_SWITCHED` (black bars, aspect kept). Never `_CROP` or `_STRETCH`. |
| D6 | `set:video` fails: abort, send no `join`, leave the previous source as it was, show an error on the card. |
| D7 | No change at launch, refresh or restore-from-hardware. The mode changes only when a route (or a multiview exit) is made. |
| D8 | EDID is read on every Single View route. No cache. |
| D9 | No setting to bring back genlock. |
| D10 | The card's output line (`CanvasInfo`) also shows `OUT <format>` in Single View, including the fallback reason. |

## Request body (from the SDVoE API Reference, supplied by Mark)

```json
{ "op": "set:video", "display_mode": "FAST_SWITCHED", "video": { "width": 1920, "height": 1080, "fps": 60 } }
```

`POST /api/device/<rx mac>`. The read side (`get`) reports the same values as `configuration.width/height/frames_per_second` on `FRAME_BUFFER:0`.

## Known limits

- Fast Switch and scaling need an AVP2000 / AVP2000T decoder; an RX that rejects the call hits D6.
- Not yet validated on hardware (plan Task 5 is the lab gate).
