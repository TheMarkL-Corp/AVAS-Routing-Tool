# ADR 004: The hardware is the source of truth for routes and layouts

## Status
Accepted (user confirmed the three decisions on 2026-10-25). Implemented for Single View and MultiView; Settings (Part B) still to do.

## Date
2026-10-25

## Context
The app started with every display card "unrouted" because an earlier probe concluded the Control Server could not report subscriptions, and the Handover proposed a local `routes.json`. Phase 2 hardware work showed that `POST /api/device/{rx}` `{"op":"get","subset":"settings"}` returns every HDMI `subscriptions[]` entry (`state`, `source.device_id`, `stream_index`) and `FRAME_BUFFER.configuration.display_mode`, and `{"op":"get","subset":"multiview"}` returns the loaded layout (`name`, `width`, `height`, `windows[].target_surface`) while the RX is in multiview (and `INVALID_STATE` otherwise). The embedded BAVM console can change routes behind the app's back, so any local copy goes stale.

## Decision
1. **No local route/layout file.** On startup, Refresh, a decoder coming back online and returning from the BAVM Console tab, every online receiver is read (read-only, in parallel, at most 8 at once) and its card is set from what the hardware reports: Single View source (HDMI subscription 0), or MultiView.
2. **Layouts this tool created** (`avas_<preset>_<W>x<H>`) are adopted: the plan is rebuilt, windows are mapped from `windows[].target_surface` to subscription index, and the next drop just reloads the running layout (no re-create, no EDID read). Dividers found on are never switched back (not ours).
3. **Layouts from elsewhere** (BAVM, the built-in `compatibility_4k_2x2`) are *external*: shown read-only with a notice, no drop zones. A plain join, Off or the Single View tile leaves multiview first (`set:video GENLOCKED`; Off also leaves each subscription).
4. A receiver that cannot be read keeps its card as it is; one slow or failed read never blocks the others or the app start.
5. Hardware wins over local belief: a card or orchestrator session that disagrees with the hardware is replaced.

## Consequences
- Superseded: Handover 05 Task 2 (`routes.json`) and the "no subscription query" limitation.
- `get settings` is ~58 KB per receiver; fine for the lab, measure with 20+ decoders.
- Receivers in other display modes (e.g. `FAST_SWITCHED`, seen on PAX355S) are treated as Single View.
- Not live yet: the cards do not follow `SETTINGS_CHANGED` events; they re-sync on the triggers above (a follow-up).
- Unverified on hardware: restore of a running multiview layout (needs a state change on the lab); dual-link receivers (offline).
