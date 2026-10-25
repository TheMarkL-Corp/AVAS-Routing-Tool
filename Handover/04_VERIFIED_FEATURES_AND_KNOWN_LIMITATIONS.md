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
   * **Recommended Next Step**: Implement local state persistence (see [05_NEXT_STEPS_AND_PHASE_2_PLAN.md](./05_NEXT_STEPS_AND_PHASE_2_PLAN.md)).

2. **Non-AVAS Transceivers Lack In-Band Preview**:
   * Units like `VEGA_1100` lack the Cypress Baseboard MCU UART and do not provide raw RFC 4175 preview streaming on UDP 5004.
   * They must remain represented with the `"Preview Not Available"` placeholder.
