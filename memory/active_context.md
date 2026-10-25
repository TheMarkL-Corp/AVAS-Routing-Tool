# Active Context

## Current Status & Focus
* **Current Phase**: Phase 1 (Single View Video Routing) Decoder Un-Routing, Live Preview Mirroring & Responsive FullHD Layout Completed and Verified.
* **Core Architecture**: Dual-Mode Shell (Native Visual Routing Canvas + Embedded BAVM Console in WebView2 + Live Diagnostics Tab).
* **Key Enhancements Delivered (Milestone M8)**:
  1. **Multi-Modal Decoder Un-Routing**:
     - Dedicated Red `[ ⏻ Turn Off / Disconnect ]` action button in the decoder card footer bound to `UnrouteCommand`, which dispatches Semtech BlueRiver REST `leave` (`POST /api/device/{rxMac}` with `op: "leave", subscription_type: "HDMI"`).
     - Drag-and-drop support from the bottom `"Off"` layout tile (`LayoutItemViewModel.Id == "off"`) onto any display card to trigger un-routing and clear active stream.
  2. **Live Video Preview Mirroring on Decoder Cards**:
     - `DisplayCardViewModel` reactively mirrors the active `SourceCardViewModel.PreviewBitmap` reference with zero additional network socket overhead or redundant decoding.
     - When routed to an AVAS transmitter (`TX2_DUAL LINK_SFPA`), the decoder card immediately renders the identical live video stream with an active source overlay pill (`● <ActiveSourceName>`).
     - When un-routed or turned off, the decoder card smoothly resets to the idle drop-target surface (`"Drop Source Here to Route"`).
  3. **Non-AVAS Routing Fallback State**:
     - When routed to a non-AVAS transmitter (`VEGA_1100`), the decoder displays an informative, polished banner: `"Routed to VEGA_1100"` with `"Preview Not Available (Non-AVAS Device)"`.
  4. **Fluid Responsive Card Sizing for FullHD Screens (1920x1080)**:
     - Upgraded both `SourceCardView` and `DisplayCardView` from cramped fixed dimensions (320px/300px) to fluid `Width="440" Height="295" Margin="10"` featuring 16:9 widescreen video preview surfaces (`Height="185"`).
     - Replaced rigid layout containers in `VisualRoutingView.xaml` with `WrapPanel` inside scrollable viewports and `MinHeight="330"` rows, fitting 4 cards across a 1920x1080 screen without wrapping and eliminating empty dark voids.
  5. **Clean Command Architecture**:
     - Extracted `RelayCommand.cs` into `AvasRoutingTool.ViewModels`.

## Test & Build Verification
* **Unit Tests**: All 32 unit tests passing in `tests/AvasRoutingTool.Tests/`.
* **Hardware API Verification**: Tested REST `start` on TX2 (`74fe488b07bb`), `join` on RX1 (`74fe483c059e`), and `leave` on RX1 via BlueRiver REST API (`127.0.0.1:8090`).
* **Standalone Release Binary**: Successfully built and published to `publish/AvasRoutingTool/AvasRoutingTool.exe`.

## Next Steps
1. User testing and visual sign-off of the standalone desktop application (`publish/AvasRoutingTool/AvasRoutingTool.exe`).
2. Plan Phase 2 MultiView canvas layouts (Matrix 2x2, PIP, Side-by-Side, 1+3 Quad).
