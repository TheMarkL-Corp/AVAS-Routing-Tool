# System Patterns & Architecture

## Architecture Overview
AVAS Routing Tool is a portable native Windows desktop application built with **C# .NET 8 (`net8.0-windows` WPF)** following the **MVVM (Model-View-ViewModel)** architectural pattern.

## Core Architectural Patterns

### 1. Dual-Mode Application Shell
* **Mode 1: Visual Routing Canvas**: Native WPF canvas rendering Sources (TX), Displays (RX), and Layouts with low-latency drag-and-drop.
* **Mode 2: BAVM Console**: Isolated Microsoft WebView2 browser control hosting the official BAVM web UI (`http://localhost:80`).
* **Zero Cross-Contamination**: Both modes interact with the same underlying Semtech BlueRiver Control Server on TCP 6970 / REST 8080.

### 2. Live Video Ingestion (`UnifiedRtpDemuxReceiver`)
* Singleton UDP listener listening simultaneously on **UDP port 5000 and 5004**.
* Socket flags: `ExclusiveAddressUse = false`, `SocketOptionName.ReuseAddress = true`, 8MB receive buffer.
* Source-IP Whitelist Filtering: Discards unmonitored datagrams immediately at socket entry before allocation.
* Scanline reassembly for RFC 4175 uncompressed video payloads.
* Q10 fixed-point BT.601 color-space conversion (`YUV422` to `RGB24/ARGB32`).
* Zero-leak rendering via direct updates to `WriteableBitmap.BackBuffer`.

### 3. Stream 1 Scaler Acquisition & Deterministic Teardown
* Scaled monitoring video is acquired via Semtech Stream 1 Scaler (`HDMI:1`):
  `set <mac> stream HDMI 1 enable true address <MCAST_IP> auto_stop true remove_audio true`
* Teardown requires double-action:
  1. Set address to `0.0.0.0` with `enable false`.
  2. Drop host IGMP multicast membership (`DropMulticastGroup`).

### 4. 3-in-1 Automated Routing Pattern
When Source TX is routed to Display RX (Single View):
1. Video: `join <rx>:HDMI:0 <tx>:HDMI:0`
2. Audio: `join <rx>:AUDIO:0 <tx>:AUDIO:0`
3. USB HID: `join <rx>:USB:0 <tx>:USB:0` (or `ROUTE_IO_USB_HID`)
When Disconnected:
`leave <rx>:HDMI:0`

### 5. MultiView Layouts (Phase 2, ADR 003)
* A layout tile dropped on a display card only prepares empty windows; the first source dropped into a window applies it (`MultiViewOrchestrator`): per source `set:scaler` (window size) + `start` HDMI stream 1, create layout `avas_<preset>_<W>x<H>` once, then `set:multiview` with every subscription (`subscription_index` == target surface).
* The canvas (output format) is read from the decoder's display EDID (`get edid` -> `EdidInfo` -> `MultiViewCanvasPolicy`), at layout drop and again before the first hardware apply; fallback 1920x1080@60 with a visible reason. Never hard-code the output resolution.
* Over 9 Gbps estimated stream-1 bandwidth -> TX stream 1 at half frame rate via the scaler frame divider; the tool only restores dividers it switched on itself.
* Multiview REST calls wait for `/api/request/{id}` and fail on `result.error` (a PROCESSING reply is not success).
* All windows are 16:9 cells fitted into the canvas (`MultiViewPresets`); window size == the TX scaler size, so one TX can have only one stream-1 size across all decoders (conflict is refused before any call).
* A source appears once per layout (duplicates are API errors) - re-dropping moves it. Leave multiview with `set:video GENLOCKED` then the normal `join`.
* Windows mirror source `PreviewBitmap` references (no extra sockets). `FractionalLayoutPanel` places them by fractional rect, letterboxed to the canvas aspect.
* REST bodies live in `MultiViewRequests` (pure) and are pinned by tests to the PDS-062489 3.9 examples; never hand-write them elsewhere.
* Test seam: `new SdvoeClient(HttpMessageHandler)`; `MockSdvoeClient.MultiViewCalls` logs the ordered calls.

### 6. Multicast IP Management
* Pool range: `225.0.0.1` to `225.0.1.254` (510 Class D IP addresses).
* Thread-safe allocation and reclamation via `MulticastIpManager`.
