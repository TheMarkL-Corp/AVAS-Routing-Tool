# 01. Architecture & System Design

This document details the core architectural systems that power the **AVAS Routing Tool**.

---

## 1. Dual-Mode Application Shell

```
┌────────────────────────────────────────────────────────────────────────┐
│                          AVAS Routing Tool                             │
├────────────────────────────────────────────────────────────────────────┤
│ [ Visual Routing ]       │ [ BAVM Console ]     │ [ Diagnostics & Logs]│
├──────────────────────────┼──────────────────────┼──────────────────────┤
│ Native WPF Canvas        │ Microsoft WebView2   │ Live Observable Log  │
│ - Video Sources (TX)     │ - Hosts BAVM Web UI  │ - Filter by Level    │
│ - Target Displays (RX)   │ - http://localhost:80│ - Export to Disk     │
│ - Layout Presets & "Off" │ - Isolated User Data │ - Crash-Capture Seam │
└──────────────────────────┴──────────────────────┴──────────────────────┘
         │                           │
         ▼                           ▼
┌────────────────────────────────────────────────────────────────────────┐
│               Semtech BlueRiver Control Server Engine                  │
│       TCP 6970 (Discovery / CLI)  │  REST 8090 (Routing & Subscriptions│
└────────────────────────────────────────────────────────────────────────┘
```

### Key Architectural Tenets:
1. **Zero Cross-Contamination**:
   * Mode 1 (Native Visual Canvas) and Mode 2 (BAVM Console) run side-by-side without interference.
   * WebView2 stores browser state in an isolated directory (`publish/AvasRoutingTool/WebView2_UserData/`), preventing cookie/cache collisions with any external browser or system service.
2. **Crash-Capture Seam**:
   * Installed in `App.xaml.cs`: hooks `AppDomain.CurrentDomain.UnhandledException`, `DispatcherUnhandledException`, and `TaskScheduler.UnobservedTaskException`.
   * Uncaught exceptions are intercepted, formatted with stack traces, and flushed to `app.log` under the category `CRASH` before orderly shutdown.

---

## 2. Live Video Ingestion & Rendering Engine

Medical operating rooms demand ultra-low latency (< 50ms) with zero dropped frames. Video previews are delivered as uncompressed raw RFC 4175 video over RTP UDP datagrams.

```
Hardware Encoder (TX)
        │ RTP RFC 4175 (YUV 4:2:2) Multicast UDP 5000 / 5004
        ▼
┌────────────────────────────────────────────────────────────────────────┐
│ UnifiedRtpDemuxReceiver (Singleton UDP Listener)                       │
│ - SO_REUSEADDR enabled, ExclusiveAddressUse = false                     │
│ - 8MB OS Socket Receive Buffer                                         │
│ - Dual-Port listener (5000 and 5004)                                   │
│ - Source-IP & Companion Alias Filtering (SFP A & SFP B)                 │
└────────────────────────────────────────────────────────────────────────┘
        │
        ▼ Raw Datagram Buffer
┌────────────────────────────────────────────────────────────────────────┐
│ ScanlineFrameReassembler                                               │
│ - Parses RFC 4175 headers (Payload Type 96, Line Number, Field, Offset)│
│ - Sequence continuity tracking & packet re-ordering                    │
│ - Frame boundary detection via RTP Marker Bit                          │
└────────────────────────────────────────────────────────────────────────┘
        │
        ▼ Complete YUV 4:2:2 Scanline Frame
┌────────────────────────────────────────────────────────────────────────┐
│ Yuv422Rasterizer (Zero-Allocation Color Conversion)                   │
│ - Q10 Fixed-Point BT.601 color-space conversion (UYVY -> BGRA32)       │
│ - Direct memory blit into WriteableBitmap.BackBuffer                   │
│ - WriteableBitmap.Lock() -> AddDirtyRect() -> Unlock()                 │
└────────────────────────────────────────────────────────────────────────┘
        │
        ▼ Zero CPU-GPU Memory Copy
 WPF Video Container (Image.Source = PreviewBitmap)
```

### Technical Highlights:
* **Zero Heap Allocations in Render Loop**: `Yuv422Rasterizer` decodes directly into the unmanaged memory buffer of `WriteableBitmap.BackBuffer`. No intermediate byte arrays are allocated per frame, keeping Gen0 GC pauses at zero.
* **Dual-Link Companion Alias**: Transceiver `TX2_DUAL LINK_SFPA` transmits on SFP A (`169.254.7.187`) while its companion SFP B link is at `169.254.7.188`. `UnifiedRtpDemuxReceiver` registers alias routing so datagrams arriving from either IP map to the same channel handler.

---

## 3. In-Band Baseboard MCU UART Protocol

AVAS transceivers feature a proprietary Cypress PSoC microcontroller connected to the Semtech BlueRiver processor via internal UART 1.

### Activation Sequence:
When the user clicks **"Start Preview"** or when the app initializes an active AVAS device:
1. **Mode Switch**: Sends `switch:api\r\n` to switch the MCU UART from console mode to command mode.
2. **IGMP Multicast Association**: Sends `set rtp igmp <MCAST_IP> 5004 <SRC_IP>\r\n`.
3. **Stream Enable**: Sends `set rtp ON\r\n`.
4. **Hardware Verification**: Waits for the MCU acknowledgment response.

### Non-AVAS Device Protection:
* Third-party units (e.g., `VEGA_1100`, which has `VendorId == 0`) do not possess the Cypress MCU.
* `AvasDevice.IsPreviewSupported` strictly checks `VendorId == 105 && IsTransmitter`. Non-AVAS devices bypass UART activation and display a clear `"Preview not supported - Non-AVAS Device"` badge.

---

## 4. Adaptive Grid Layout Engine

To prevent small card clustering and empty black voids on 1920×1080 surgical displays, an adaptive layout engine automatically calculates card geometry.

```
Viewport Size (e.g. 1888 x 435 per row) + Card Count (e.g. 2 Sources)
        │
        ▼
┌────────────────────────────────────────────────────────────────────────┐
│ AdaptiveGridLayout.Compute(count, availableWidth, availableHeight, spec│
│ - Iterates column candidates (1 to N)                                  │
│ - Calculates card width constrained by 16:9 video surface aspect       │
│ - Honors MinCardWidth (320px) and MaxCardWidth (600px)                 │
│ - Detects when vertical scrolling is required                          │
└────────────────────────────────────────────────────────────────────────┘
        │
        ▼ Returns GridPlan(Columns, Rows, CardWidth, CardHeight, NeedsScroll)
┌────────────────────────────────────────────────────────────────────────┐
│ AdaptiveCardPanel (Custom WPF Panel)                                   │
│ - MeasureOverride: Dynamically inspects ScrollViewer viewport height   │
│ - ArrangeOverride: Centers card grid horizontally and vertically       │
└────────────────────────────────────────────────────────────────────────┘
```

### Surgical Sizing Rules (`GridSpec.Default`):
* `MinCardWidth`: 320px (minimum readable size for touch/mouse interaction).
* `MaxCardWidth`: 600px (prevents oversized cards on 4K/ultrawide displays).
* `Gap`: 16px between cards.
* `HorizontalPadding`: 20px (card borders, inner margins).
* `ChromeHeight`: 120px (card header, status indicators, and footer action buttons).
* `VideoAspect`: 16:9 widescreen video surface.

---

## 5. 3-in-1 Automated Routing & Preview Mirroring

When a user drags a Source TX card onto a Display RX card:
1. **Video Routing**:
   Dispatches Semtech BlueRiver REST `join`:
   `POST /api/device/{rxMac}` with:
   ```json
   {
     "op": "join",
     "subscription_type": "HDMI",
     "subscription_index": 0,
     "source_device": "{txMac}",
     "source_index": 0
   }
   ```
2. **Audio & USB Routing**:
   Dispatches companion `join` operations for `AUDIO:0` and `USB:0` (HID pass-through).
3. **Zero-Overhead Live Preview Mirroring**:
   * Instead of starting a second multicast stream or decoding the video twice, `DisplayCardViewModel` directly references the active `SourceCardViewModel.PreviewBitmap`.
   * The decoder card instantly mirrors the live video feed.
   * When routed to a non-AVAS transmitter, the decoder displays `"Routed to <Name>"` with `"Preview Not Available (Non-AVAS Device)"`.
4. **Un-Routing (`[ ⏻ Turn Off ]` or "Off" Tile Drag)**:
   Dispatches Semtech REST `leave`:
   `POST /api/device/{rxMac}` with:
   ```json
   {
     "op": "leave",
     "subscription_type": "HDMI",
     "subscription_index": 0
   }
   ```
   The decoder resets to its idle drop-target surface (`"Drop Source Here to Route"`).
