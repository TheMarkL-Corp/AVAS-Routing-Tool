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

### 5. Multicast IP Management
* Pool range: `225.0.0.1` to `225.0.1.254` (510 Class D IP addresses).
* Thread-safe allocation and reclamation via `MulticastIpManager`.
