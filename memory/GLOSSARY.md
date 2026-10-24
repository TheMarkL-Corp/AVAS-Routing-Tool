# Canonical Domain Glossary

### Source (TX / Transmitter)
An Advantech AVAS-223 endpoint configured in encoder mode (`is_transmitter == true`). It captures HDMI video/audio from medical cameras, endoscopy units, or computers, serializing it over 10GbE network fabric, and emits a downscaled preview stream on UDP 5004 / 5000.

### Display (RX / Receiver)
An Advantech AVAS-223 endpoint configured in decoder mode (`is_receiver == true`). It is connected to a physical surgical or diagnostic display monitor via HDMI 2.0, receiving multicast video/audio datagrams from an assigned Source.

### Viewport
A distinct display window or raster area within an RX display monitor's layout. In **Single View**, Viewport 1 fills 100% of the display. In **MultiView** (Phase 2), multiple viewports partition the screen (e.g. 4 quadrants in Matrix 2x2).

### Layout
A predefined geometric configuration for display viewports:
* `off`: No active video output.
* `single`: Full-screen 1-to-1 video output (Single View).
* `matrix-2x2`: 4 equal-sized quadrant viewports.
* `picture-in-picture`: 1 full-screen background + 1 inset window.
* `side-by-side`: 2 equal vertical split windows.
* `one-plus-three`: 1 primary large left window + 3 smaller vertical right windows.

### Route (Join)
The operation of binding a Source TX to a Display RX Viewport. In this application, Single View routing atomically establishes:
1. Video Subscription (`join <rx>:HDMI:0 <tx>:HDMI:0`).
2. Audio Subscription (`join <rx>:AUDIO:0 <tx>:AUDIO:0`).
3. USB HID Extension (`ROUTE_IO_USB_HID` / `join <rx>:USB:0 <tx>:USB:0`) for Mouse Thru and Touch Thru.

### Unroute (Leave)
The operation of disconnecting a Source from a Display, terminating the active stream subscription (`leave <rx>:HDMI:0`).

### BlueRiver Control Server
The centralized software daemon (port 6970 TCP / 8080 REST) managing the SDVoE device database, topology, state notifications, and routing commands.

### Stream 1 Scaler
The hardware scaling engine on the Semtech AVP2000 SoC (`HDMI:1`). Used for acquiring low-bandwidth, downscaled monitoring video without interfering with the uncompressed 4K60 main video stream.
