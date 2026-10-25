# Route/Layout Restore from Hardware & MultiView Settings Implementation Plan

> **Status (2026-10-25):** Part A (Tasks 1-6) implemented and unit-tested; the user confirmed the three decisions. Remaining: Task 7 (EDID VICs), Task 8 (Settings), Task 9 (hardware run of a restored multiview, publish).

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** (A) On startup, on Refresh, and when the operator comes back from the BAVM Console tab, make every display card show what the decoders are *really* doing (Single View source, or MultiView layout with its windows), read from the hardware instead of assuming "unrouted". (B) Let the operator override the multiview output format and choose the frame-divider mode in Settings.

**Why this replaces the old "routes.json" idea (Handover 05 Task 2):** the Control Server *can* report state. `POST /api/device/{rx}` `{"op":"get","subset":"settings"}` returns every HDMI `subscriptions[]` entry with `state` and `source.device_id/stream_index`, and `FRAME_BUFFER.configuration.display_mode` (`GENLOCKED` / `MULTIVIEW` / ...). `{"op":"get","subset":"multiview"}` returns the loaded layout (`name`, `width`, `height`, `windows[].target_surface`) while the RX is in multiview and fails with `INVALID_STATE` otherwise. A local file would go stale the moment someone routes in the embedded BAVM console; the hardware is the single source of truth. (Verified on the lab units 2026-10-25; fixtures to be captured from real replies.)

**Architecture**
1. **`RxStateReader` (pure parser)** - turns the `get settings` JSON (+ optional `get multiview` JSON) into `RxState`: `DisplayMode`, `OutputWidth/Height/Fps`, and either `SingleViewSource(mac, name)` or `MultiViewState(layoutName, canvas, windows[(index, surface)], subscriptions[(surface, sourceMac, sourceName)])`.
2. **`ISdvoeClient.ReadReceiverStateAsync(rxMac)`** - the two REST reads (reusing the request-polling helper); `get multiview` only when `display_mode == MULTIVIEW`; returns `null` on failure (never throws).
3. **Layout recognition** - `MultiViewPresets.TryParseName("avas_<preset>_<W>x<H>")`. Layouts we did not create (`compatibility_4k_2x2`, BAVM-made) are **external**: shown with a read-only badge, no drop zones.
4. **`MultiViewOrchestrator.Adopt(rx, plan, canvas, assignments)`** - registers a restored session (`LayoutCreated = true`) so the next drop just reloads. Dividers found on are *not* ours and are never switched back.
5. **`MainViewModel.RestoreStateAsync()`** - after discovery, reads all online receivers in parallel (bounded, per-device timeout, failures logged and non-fatal), then on the UI thread sets `ActiveSource*` + preview mirroring for Single View, or `ApplyLayout` + `AssignSlot` for recognised MultiView, or the external badge. Runs on startup, Refresh, a device coming back online, and when the Visual Routing tab is re-selected.
6. **Plain join on an external/unknown multiview RX** must genlock first (today only our own sessions do): `RouteVideoAsync` consults the restored `DisplayMode`, not just the orchestrator.
7. **Settings (Part B)** - `AppConfig`: `MultiViewCanvasMode` (`Auto` | `Fixed`), `MultiViewWidth/Height/Fps`, `MultiViewFrameDivider` (`Auto` | `Always` | `Never`). `MultiViewOptions` is read through a `Func<>` at apply time so a Settings change needs no restart. The override is validated (<= 3840x2160, width % 8 == 0, >= 640x360) and, when the display's EDID is readable, checked against the modes the display advertises (needs CTA VIC parsing - Task 7).

## Decisions I recommend (please confirm or change)
* **Hardware is the source of truth; no local routes file.** (Supersedes Handover 05 Task 2; record as ADR 004.)
* **External layouts: read-only in v1.** Show the layout name and the sources in its windows, but no drop zones; a drop elsewhere genlocks and joins as usual. Generic adoption of any layout (drop zones for BAVM-made layouts) can come later.
* **Divider modes:** `Auto` (today's behaviour, > 9 Gbps), `Always` (use whenever the TX supports it), `Never` (full rate, warn on the card if > 9 Gbps).
* **Fixed override must be advertised by the display** when the EDID can be read; otherwise refuse with a clear message rather than risk a black surgical display. (Needs Task 7; without it the override is accepted with a warning.)

## Global Constraints
* Reads only on startup/refresh paths - restore must never send a command that changes a device.
* A failed or slow read for one decoder must not delay or break the others, nor the app start.
* No new sockets/IGMP; previews are mirrored by reference as before.
* TDD, STA view-smoke tests for every XAML change, memory update + atomic commit; live tests stay opt-in (they route real hardware).

## File Structure
```
src/AvasRoutingTool/
├── Sdvoe/
│   ├── RxState.cs                       // DisplayMode, output, single source / multiview state
│   ├── RxStateReader.cs                 // pure JSON -> RxState
│   ├── ISdvoeClient.cs / SdvoeClient.cs // ReadReceiverStateAsync
│   └── MultiView/
│       ├── MultiViewPresets.cs          // + TryParseName
│       ├── MultiViewOrchestrator.cs     // + Adopt, options (divider mode, canvas override)
│       ├── MultiViewOptions.cs          // canvas mode/override, divider mode
│       └── EdidInfo.cs                  // + CTA video data block VICs (Task 7)
├── Configuration/AppConfig.cs           // multiview settings
├── ViewModels/MainViewModel.cs          // RestoreStateAsync, tab re-sync, genlock-before-join
├── ViewModels/DisplayCardViewModel.cs   // external-layout badge
└── Views/SettingsDialog.xaml(.cs)       // multiview section; DisplayCardView.xaml (external badge)
tests/AvasRoutingTool.Tests/
├── Fixtures/rx_single_view.json, rx_multiview.json, rx_multiview_layout.json, rx_not_multiview.json
├── RxStateReaderTests.cs, SdvoeClientRxStateTests.cs, MultiViewPresetsNameTests.cs
├── MultiViewOrchestratorAdoptTests.cs, MainViewModelRestoreTests.cs
├── MultiViewOptionsTests.cs, SettingsDialog smoke in ViewSmokeTests.cs
```

## Tasks

### Task 1: Capture fixtures and parse receiver state (pure)
- [x] Capture read-only replies from the lab (RX1 Single View, RX1 Matrix at FHD and 4K, PAX not in multiview) into `tests/.../Fixtures`, trimmed to `FRAME_BUFFER`, `HDMI_ENCODER.status.video`, `subscriptions` and the `get multiview` layout.
- [x] Failing tests for `RxStateReader`: single view source (idx0 STREAMING), unrouted (all STOPPED), multiview with 1 and 2 subscriptions mapped to surfaces, `INVALID_STATE` multiview error, a source that is offline/unknown, malformed JSON -> `null`, unknown `display_mode`.
- [x] Implement `RxState` + `RxStateReader`. Commit.

### Task 2: Layout name recognition
- [x] Failing tests: `avas_matrix2x2_3840x2160` -> (`matrix2x2`, 3840, 2160); `avas_quad_1_3_1920x1080` (underscores in the preset); `compatibility_4k_2x2` / garbage -> not ours.
- [x] Implement `MultiViewPresets.TryParseName`. Commit.

### Task 3: Client read
- [x] Failing tests (recording handler): Single View makes one `get settings` call; MultiView adds `get multiview`; `get multiview` error is tolerated; server error -> `null`.
- [x] `ISdvoeClient.ReadReceiverStateAsync` + `SdvoeClient`; extend `MockSdvoeClient`. Commit.

### Task 4: Adopt a restored session
- [x] Failing tests: `Adopt` creates a session without hardware calls; the next `AssignAsync` does not re-create the layout nor re-read the EDID; a restored heavy layout does not restore dividers it never set; slot indexes map from subscription surfaces; an assignment whose source is unknown still shows (name from the reply).
- [x] Implement `Adopt`. Commit.

### Task 5: Restore into the view models
- [x] Failing tests with the mock client: Single View source + preview mirroring; recognised MultiView -> `ApplyLayout` + `AssignSlot` + `CanvasInfo`; external layout -> badge, no slots; unreadable RX stays unrouted and others still restore; offline RX skipped; parallel reads (a slow RX does not block).
- [x] `MainViewModel.RestoreStateAsync`, called from `RefreshDevicesAsync`, on device-online events and on tab re-select (`MainWindow`). Commit.

### Task 6: Join on a multiview RX we did not start
- [x] Failing test: RX restored as external multiview + `RouteVideoAsync` -> `genlock` then `join`.
- [x] Implement via the restored display mode. Commit.

### Task 7: Display-advertised modes (EDID VICs)
- [ ] Failing tests with the real VP3268-4K EDID: CTA Video Data Block VICs decode to (w, h, hz) for the common VIC table; DTD + VIC modes are merged without duplicates.
- [ ] Implement in `EdidInfo` (`SupportedModes`). Commit.

### Task 8: Multiview options + Settings
- [ ] Failing tests: `AppConfig` round-trips the new settings with safe defaults (`Auto`/`Auto`); fixed canvas validation (4K limit, % 8, minimum); override not advertised by the EDID -> refused message; `Always` forces the divider, `Never` suppresses it (card warns when > 9 Gbps); options changed at runtime apply to the next layout without restart.
- [ ] `MultiViewOptions`, orchestrator/policy changes, `SettingsDialog` section, `ViewSmokeTests` for the dialog. Commit.

### Task 9: Hardware validation, publish, memory
- [ ] With the user's go-ahead (read-only except where stated): restart the app with RX1 in Single View, in Matrix (4K) and unrouted -> the cards match; change a route in the BAVM Console tab and return -> the card follows; Fixed 1080p override on RX1 (changes the display mode - ask first); Divider `Never`/`Always` on the 4K Matrix.
- [ ] `dotnet test` (non-live), `dotnet publish`, update `memory/` + ADR 004 + Handover 04/05 (Task 2 rewritten), commit.

## Risks / open questions
* `get settings` is ~58 KB per RX; fine for a lab, measure with 20+ decoders and consider a smaller subset if one exists.
* A subscription can be `enable: true` but `state` not `STREAMING` (source stopped/offline) - decide whether that counts as routed (proposal: show the source name with an "idle" hint).
* The Control Server also pushes `SETTINGS_CHANGED` events over WebSocket/telnet; subscribing to them would make the cards live instead of re-synced on triggers. Out of scope here, worth a follow-up.
* Dual-link receivers (RX2) and TX1 are offline in the lab; their state shape is assumed identical until observed.
