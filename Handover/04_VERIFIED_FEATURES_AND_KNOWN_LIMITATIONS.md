# 04. Verified Features & Known Limitations

This document catalogs every feature verified on physical hardware, lessons learned from real-world debugging, and known constraints in the Semtech BlueRiver environment.

---

## 1. Verified Feature Catalog

The following capabilities have been tested against physical hardware and verified with automated test suites:

### 1.1 Device Discovery & Hardware Invariants
- [x] **Strict Device Discovery**: Reads both BlueRiver REST (`/api/device/ALL`) and TCP CLI (`get all identity`).
- [x] **Vendor ID Classification**: Accurately distinguishes AVAS units (`VendorId == 105`) from third-party units (e.g. `VEGA_1100`, `VendorId == 0`).
- [x] **Dual-Link SFP Detection**: Discovers SFP A (`NodeIpA`) and companion SFP B (`NodeIpB`) interfaces on dual-link transceivers (`TX2_DUAL LINK_SFPA` and `RX2_DUAL LINK_SFPA`).

### 1.2 Video Preview Engine & MCU Control
- [x] **In-Band MCU UART Activation**: Automatically dispatches `switch:api`, `set rtp igmp ...`, and `set rtp ON` to start hardware streaming on AVAS transmitters.
- [x] **Dual-Port Demux Receiver**: Ingests raw RFC 4175 video over UDP 5000 and 5004 simultaneously with `SO_REUSEADDR`.
- [x] **Zero-Allocation Color Conversion**: BT.601 Q10 fixed-point conversion writes directly into `WriteableBitmap.BackBuffer`.
- [x] **Live Telemetry & FPS Pill**: Real-time FPS calculation pill on active preview cards.
- [x] **Companion Alias Routing**: Routes RTP packets arriving from either SFP A or SFP B companion IP to the correct preview pipeline.

### 1.3 Routing, Un-Routing & Mirroring
- [x] **Single-View Drag-and-Drop Routing**: Touch-responsive drag initiation on Source cards with drop-target validation on Display cards. Dispatches REST `join` (`POST /api/device/{rxMac}` with `op: "join"`).
- [x] **Multi-Modal Decoder Un-Routing**:
  - Dedicated Red `[ ⏻ Turn Off / Disconnect ]` card button dispatching REST `leave` (`op: "leave"`).
  - Dragging the bottom `"Off"` layout tile (`LayoutItemViewModel.Id == "off"`) onto any display card immediately triggers un-routing.
- [x] **Zero-Overhead Live Preview Mirroring**: Routed decoders mirror the source `PreviewBitmap` reference without redundant network sockets or duplicate frame decoding.
- [x] **Non-AVAS Fallback Banners**: Display cards routed to non-AVAS transmitters display `"Routed to <Name>"` with `"Preview Not Available (Non-AVAS Device)"`.

### 1.4 Fluid Adaptive Layout
- [x] **Dynamic Card Sizing**: `AdaptiveGridLayout` calculates card dimensions based on viewport width/height, scaling cards up to 600px width on FullHD screens.
- [x] **Strict 16:9 Aspect Preservation**: Video preview containers strictly maintain 16:9 widescreen proportions across all window sizes.
- [x] **Symmetric Centering**: Cards are centered horizontally and vertically when viewport exceeds grid size, eliminating left-clustering and black voids.
- [x] **Graceful Scroll Fallback**: When 8+ devices exist, cards scale down to 320px width and enable smooth vertical scrolling.

### 1.5 App Shell, WebView2 & Diagnostics
- [x] **Embedded BAVM Console**: Isolated Microsoft WebView2 hosting `http://localhost:80` with dedicated user data directory.
- [x] **Real-Time Diagnostics Viewer**: In-app searchable log table bound to thread-safe ring buffer.
- [x] **Disk Logging & Crash Seam**: Continuous logging to `app.log` with unhandled exception capture hooks in `App.xaml.cs`.

---

## 2. Critical Lessons Learned & Discovered Bugs

During real-hardware testing and deep debugging passes, several subtle bugs were caught and resolved:

### 2.1 The TwoWay Binding on Read-Only Property Crash
* **Symptom**: App crashed immediately on startup with `InvalidOperationException: A TwoWay or OneWayToSource binding cannot work on the read-only property 'ActiveSourceName'`.
* **Root Cause**: In WPF, `<Run Text="{Binding ActiveSourceName}" />` defaults to `TwoWay` binding mode. Because `ActiveSourceName` only has a getter, WPF throws a fatal runtime exception. Pure ViewModel unit tests cannot catch this.
* **Fix**: Created a dedicated one-way `SourceLabel` property and explicitly specified `Mode=OneWay`. Added [`ViewSmokeTests.cs`](../tests/AvasRoutingTool.Tests/ViewSmokeTests.cs) which renders real XAML views on an STA thread to catch binding flaws before commit.

### 2.2 The REST JSON Formatting Quirk
* **Symptom**: Hardware successfully executed video routing (`join` and `leave`), but the application logged every operation as failed.
* **Root Cause**: The Semtech BlueRiver Control Server pretty-prints JSON responses with variable whitespace and newlines:
  ```json
  {
     "status" : "PROCESSING",
     "request_id" : 12
  }
  ```
  The client performed a naive string match for `"status":"PROCESSING"`, which failed due to the space around the colon.
* **Fix**: Implemented [`RestResponse.cs`](../src/AvasRoutingTool/Sdvoe/RestResponse.cs), performing structural JSON parsing via `JsonDocument` and guarded by [`RestResponseTests.cs`](../tests/AvasRoutingTool.Tests/RestResponseTests.cs).

### 2.3 ScrollViewer Infinite Measure Dimension
* **Symptom**: Custom layout panel inside a `ScrollViewer` received `availableSize.Height = double.PositiveInfinity`, causing cards to maximize to 600px width and triggering unnecessary vertical scrollbars on 2-card screens.
* **Fix**: In [`AdaptiveCardPanel.cs`](../src/AvasRoutingTool/Controls/AdaptiveCardPanel.cs), the panel inspects its parent `ScrollViewer.ViewportHeight` / `ActualHeight` during `MeasureOverride` and invalidates measure on `ScrollViewer.SizeChanged`.

---

## 3. Known Hardware Constraints & Limitations

1. **No Subscription Query Endpoint in Control Server** *(correction 2026-10-25: `POST /api/device/{mac}` with `{"op":"get","subset":"settings"}` DOES return `subscriptions` with each HDMI subscription's `state` and `source` device - route restore at startup is possible)*:
   * Probed all known Semtech commands (`get all/<mac> subscription`, `get subscription`, `get join`). The BlueRiver Control Server does **not** expose active subscription state via query.
   * **Consequence**: Upon app restart or refresh, display cards start in an unrouted visual state until routed from this application.
   * **Superseded (2026-10-25, ADR 004)**: state is now read back from the hardware at startup/Refresh/BAVM-tab return. Original note: Implement local state persistence (see [05_NEXT_STEPS_AND_PHASE_2_PLAN.md](./05_NEXT_STEPS_AND_PHASE_2_PLAN.md)).

2. **Non-AVAS Transceivers Lack In-Band Preview**:
   * Units like `VEGA_1100` lack the Cypress Baseboard MCU UART and do not provide raw RFC 4175 preview streaming on UDP 5004.
   * They must remain represented with the `"Preview Not Available"` placeholder.

---

## 4. MultiView Composer (branch `feat/multiview-composer`): implemented and test-verified, NOT hardware-validated

Spec: [`docs/superpowers/specs/2026-10-25-multiview-composer-ux.md`](../docs/superpowers/specs/2026-10-25-multiview-composer-ux.md) (see its "Validation status" notes). Plan: [`docs/superpowers/plans/2026-10-25-multiview-composer.md`](../docs/superpowers/plans/2026-10-25-multiview-composer.md). Decision record: `memory/decisions/2026-10-26-multiview-composer.md` (local, git-ignored).

### 4.1 What is verified, and how

The Composer is code-complete: stage-then-**Take** with atomic Take and a 10 s **Undo**, per-display serialisation, shared surfaces for equal-size redundancy, Layout menu and draft chip on the display card, **Copy layout** to another display with an EDID resolution guard, keyboard path, automation names and reduced motion. Behaviour is covered by unit, integration and STA view tests (701 tests under the non-live filter below) and by four reviews. The `Live*` classes were not run for this work.

The one piece of hardware evidence is the Task 1 lab spike (user-confirmed pictures): one TX in two blocks through **one shared surface and one subscription** works, a block without a source is black, and a window without `target_surface` reads back as surface 255 (restore treats it as empty). The finished Composer, Take, Undo, PiP/1+3 on RX1 and Copy onto PAX355S were **never run on hardware**: the product owner skipped the lab validation (plan Task 12 steps 1-2). Treat every Composer behaviour on a decoder as unproven until the run described in [05](./05_NEXT_STEPS_AND_PHASE_2_PLAN.md) section 3 is done.

### 4.2 Known limitations and unverified assumptions

1. **Single View runs in Fast Switch at the display EDID format** (`set:video FAST_SWITCHED`, EDID via `MultiViewCanvasPolicy`, 1920x1080@60 if unreadable, then `join`; GENLOCKED is never sent). This is what R13 assumed, so the copy guard now matches the real output. **Not yet run on hardware** (plan `docs/superpowers/plans/2026-10-27-single-view-fast-switch-edid.md` Task 5 step 1): the `set:video` body comes from the SDVoE API Reference example; Fast Switch needs an AVP2000/AVP2000T decoder (any other chip refuses and the route is not sent); a switch may blank briefly when the mode changes.
2. **Copied external layouts (R16)** keep each subscription's stream index as read back; if the read-back omits it, the copy would subscribe stream 0 instead of 1.
3. **EDID modes** come only from detailed timing descriptors and the base CTA video data block. HDMI_VIC (4K24/25/30) and YCbCr 4:2:0-only 4K modes are not read, so a target listing 4K60 only as 4:2:0 is conservatively blocked for copy.
4. **Lower-resolution copy block** cannot be shown on hardware with two 4K decoders; only the `DisplayCopyPlanner` tests cover it.
5. **Real input is untested**: mouse and touch drag with the ghost at a display scale other than 100 %, held keys, a screen reader and Alt-Tab with the keyboard focus guard (tests use a fake drag starter).
6. **TX egress budget guard**: the bandwidth model counts stream 1 only and never touches stream 0 (BAVM counts stream 0 + stream 1, blanking-inclusive, 94 % efficiency, threshold 9.0274 Gbps, and may halve stream 0). A single-link 4K encoder with stream 0 near its limit could exceed its link when a window is added. Follow-up: BAVM-style stream0+stream1 guard.
7. **Composer Undo after non-copy format changes**: Undo restores the previous output format only for copied canvases; after other output-format changes it may be refused as "display changed" (a refusal, never a wrong send). Follow-up.
8. **Drag auto-scroll**: a drag cannot reach second-row display targets at 1008x615 (no auto-scroll while dragging); tap works.
9. **Product decisions to confirm**: a card running an external layout routes Single View on drop without a drop choice (R12); the Sources row is a compact strip while composing (R20/R21); the first Take on an empty display offers no Undo.
10. **Timing**: the EDID read runs inside the global take gate (up to ~13 s if the display does not answer); `RestoreDisplayAsync` is fire-and-forget.
11. **Kept-draft notice**: after non-copy changes (Route, restore) a kept draft gets no "changed elsewhere" notice.
12. **NO SIGNAL block chip** from the spec is not built (the block view-model has no signal/fps state).

### 4.3 Technical debt left on purpose

- ~100 raw hex colours duplicate existing theme tokens; tokenise later.
- Turn Off (~22 px) and the source-card Refresh/Start/Stop buttons (~21 px) are below the touch size (pre-existing).
- Dead code kept because tests still use it: `FirstEmptySlotIndex`, `ClearSlot`, `WindowSlot.TargetSurface`, `MultiViewPresets.AllocateSurfaces`.
- Duplicated layout titles (`ScreenState` vs `LayoutItemViewModel.CreateCatalog`; `Sdvoe` must not reference ViewModels).
- Every Take re-sends `set:scaler`/`start` to each distinct encoder (check for flicker on the lab).

## 5. Room Cameras tab (branch `ui-v2/room-cameras`): implemented and test-verified with a fake camera, NOT hardware-validated

Plan: [`docs/superpowers/plans/2026-11-02-room-cameras.md`](../docs/superpowers/plans/2026-11-02-room-cameras.md). Live checklist for the person with the real camera: [`docs/superpowers/specs/2026-11-02-room-cameras-live-test.md`](../docs/superpowers/specs/2026-11-02-room-cameras-live-test.md).

### 5.1 What is verified, and how

The tab is code-complete: a main-window tab between Visual Routing and BAVM Console, a camera chip row, a picture hero with the bound TX preview, a control rail (8-way pad, zoom, focus, speed, power, lock), keyboard control, and a camera editor with Detect. VISCA over IP is our own client (UDP with the 8-byte header, or raw TCP), with no third-party code. Behaviour is covered by unit, integration and STA view tests under the non-live filter, using a loopback fake camera (`tests/AvasRoutingTool.Tests/Fakes/FakeViscaServer.cs`). The fake covers the command bytes (golden hex), the frame codec, reconnect backoff, the Stop-on-everything rules and the 10 s watchdog, the Stop retries and "Stop not confirmed", and the leak rule (a port can be rebound after dispose). The screenshot harness (`ViewSmokeTests.RoomCamerasShots.cs`, gated by `UIV2_SHOTS`) writes the tab in the real main window at 1920x1080 and 1024x720, Dark and Light.

Tests use the fake camera only. The VISCA bytes match the Sony-standard patterns in the plan, but they are not checked against the on-site camera's manual yet.

### 5.2 Not verified (needs the real camera)

- Which transport and port the on-site camera answers on (Detect probes TCP 5678, 1259, 3001 and UDP 52381, 1259, 3001).
- Every row of the VISCA command table against the camera's manual, including the UDP header and the sequence reset.
- The zoom and focus ranges (`ZoomMax` 0x4000, `FocusMax` 0xFFFF defaults) and the Slow, Medium and Fast speed table (in `RoomCameraViewModel.SpeedFor`).
- Physical stop behaviour: cable pulled mid-move, app killed mid-move.
- A TX preview that is already live when the tab opens, and a Refresh in Visual Routing while the tab is open.
- Light-mode disabled look in the room.

### 5.3 Known limitations

1. The UDP sequence reset (control payload `01`, sequence 0) is sent on connect; the camera's acceptance of sequence 0 is unverified.
2. Move and Zoom drives wait up to 3 s for completion while holding the command queue; a Stop must still go first (checked only with the fake).
3. A UDP ICMP unreachable (error 10054) drops the transport; reconnect backoff is 1, 2, 4, 8, 16, then 30 s at most, and the link turns red after 3 failed attempts.
4. The 10 s watchdog only works while the network works; a camera that loses its network cannot be stopped by the app.
5. Keyboard control is ignored while a slider has focus (by design).
6. The disabled look in Light mode is subtle; check it in the room.
7. No RTSP path in v1 (R1): the picture is the bound TX's SDVoE preview only.
8. No presets in v1 (R7).
9. `ZoomMax`, `FocusMax` and the speed table are uncalibrated defaults until the live checklist is done.
