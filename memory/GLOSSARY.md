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

### MultiView Window / Surface / Layout (API terms)
* **Layout** (server side): a named canvas (`avas_matrix2x2_1920x1080`) holding up to 32 windows; created with `POST /api/multiview/layout/{name}` and loaded on an RX with `set:multiview`.
* **Window**: a rectangle on the decoder output (position + size) showing one surface; a lower window index paints on top.
* **Surface**: a frame-buffer slot a source stream is written into (0 up to 4096x2160, 1-11 up to 2048x1080). Subscription index == surface index.
* **Scaled stream (stream 1)**: a TX's `HDMI:1` output sized by `set:scaler`; the only stream a multiview RX can use together with uncompressed video.
* **Genlocked**: the full-screen display mode Single View uses; `set:video GENLOCKED` is how a decoder leaves multiview.
* **EDID canvas**: the multiview output format (width, height, refresh) taken from the preferred timing in the EDID the decoder read from its display.
* **Frame divider (stream 1)**: TX scaler option that runs the scaled stream at half frame rate (`nodes[SCALER:0].inputs[main:0].configuration.source.value` = 1) to cut multiview bandwidth.
