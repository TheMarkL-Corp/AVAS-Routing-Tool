# Active Context

## Current Status & Focus
* **Current Phase**: Phase 1 (Single View Video Routing) Decoder Un-Routing, Live Preview Mirroring & Adaptive FullHD Layout Completed and Verified.
* **Core Architecture**: Dual-Mode Shell (Native Visual Routing Canvas + Embedded BAVM Console in WebView2 + Live Diagnostics Tab).
* **Key Enhancements Delivered (Milestones M8 & M8.1)**:
  1. **Multi-Modal Decoder Un-Routing**:
     - Dedicated Red `[ ⏻ Turn Off / Disconnect ]` action button in the decoder card footer bound to `UnrouteCommand`, which dispatches Semtech BlueRiver REST `leave` (`POST /api/device/{rxMac}` with `op: "leave", subscription_type: "HDMI"`).
     - Drag-and-drop support from the bottom `"Off"` layout tile (`LayoutItemViewModel.Id == "off"`) onto any display card to trigger un-routing and clear active stream.
  2. **Live Video Preview Mirroring on Decoder Cards**:
     - `DisplayCardViewModel` reactively mirrors the active `SourceCardViewModel.PreviewBitmap` reference with zero additional network socket overhead or redundant decoding.
     - When routed to an AVAS transmitter (`TX2_DUAL LINK_SFPA`), the decoder card immediately renders the identical live video stream with an active source overlay pill (`● <ActiveSourceName>`).
     - When un-routed or turned off, the decoder card smoothly resets to the idle drop-target surface (`"Drop Source Here to Route"`).
  3. **Non-AVAS Routing Fallback State**:
     - When routed to a non-AVAS transmitter (`VEGA_1100`), the decoder displays an informative, polished banner: `"Routed to VEGA_1100"` with `"Preview Not Available (Non-AVAS Device)"`.
  4. **Fluid Adaptive Card & Grid Panel Layout (Milestone M8.1)**:
     - Implemented `AdaptiveGridLayout` pure algorithm and `AdaptiveCardPanel` custom WPF panel.
     - Cards automatically grow to fill the available section height on FullHD (1920x1080) screens while strictly maintaining 16:9 video preview surfaces, eliminating left-clustering and large dead black voids.
     - When few cards exist (e.g. 2 sources / 2 decoders), cards scale up to 580px width and are centered horizontally and vertically; when many cards exist (8+), cards gracefully fall back to 320px readable width with smooth vertical scrolling.
     - Integrated `AdaptiveCardPanel` into `VisualRoutingView.xaml` for both Video Sources and Target Displays.
  5. **Clean Command Architecture**:
     - Extracted `RelayCommand.cs` into `AvasRoutingTool.ViewModels`.

## Test & Build Verification
* **Unit Tests**: All 59 tests passing in `tests/AvasRoutingTool.Tests/` (including 9 `AdaptiveGridLayoutTests`, 3 `AdaptiveCardPanelTests`, and 4 STA `ViewSmokeTests`).
* **Hardware API Verification**: Tested REST `start` on TX2 (`74fe488b07bb`), `join` on RX1 (`74fe483c059e`), and `leave` on RX1 via BlueRiver REST API (`127.0.0.1:8090`).
* **Standalone Release Binary**: Successfully built and published to `publish/AvasRoutingTool/AvasRoutingTool.exe`.

## Full Function Test & Debugging Pass (post-M8)
Two root-cause defects found and fixed via systematic debugging (both were invisible to the earlier 32 unit tests):
1. **App crashed on every launch** (`InvalidOperationException`: TwoWay binding on read-only `DisplayCardViewModel.ActiveSourceName` in a `<Run>`). Fixed with a one-way `SourceLabel` property ("NONE" when unrouted). Guarded by `ViewSmokeTests` (real XAML views rendered on an STA thread).
2. **Every route/unroute was reported as failed** although the hardware accepted it: the Control Server pretty-prints JSON (`"status" : "PROCESSING"`) and the client matched the literal `"status":"PROCESSING"`. Fixed by `RestResponse.IsAccepted` (structural JSON parse). Guarded by `RestResponseTests`.
* Added crash-capture seam in `App.xaml.cs` (AppDomain / Dispatcher / Task unhandled exceptions -> `app.log` category `CRASH`).
* Added `LiveFunctionTests` (all TX x RX route/unroute pairs, AVAS preview start/stop, discovery invariants; auto-skip without a Control Server).
* **Known limitation**: the Control Server exposes no subscription-state `get` subset (probed `get all/<mac> ...`), so existing routes are NOT restored on app start/refresh; cards show unrouted until routed from this app.
* Lesson: a green unit suite does not prove the app launches; keep view-smoke tests for every view.

## Next Steps
1. User visual sign-off of `publish/AvasRoutingTool/AvasRoutingTool.exe` (drag-to-route, Turn Off, "Off" tile drag, decoder preview mirroring, FullHD layout).
2. Decide how to handle route-state restore on startup (needs a server-side state source or local persistence).
3. Plan Phase 2 MultiView canvas layouts (Matrix 2x2, PIP, Side-by-Side, 1+3 Quad).

