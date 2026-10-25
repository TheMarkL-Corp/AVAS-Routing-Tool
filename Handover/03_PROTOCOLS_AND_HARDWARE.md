# 03. Protocols & Hardware Integration

This document defines the exact network protocols, API specifications, and physical hardware environment used by the **AVAS Routing Tool**.

---

## 1. Physical Hardware Setup in the Lab

The application has been verified against physical Semtech BlueRiver transceivers and an active Semtech BlueRiver Control Server:

| Device Name | MAC Address | Role | Hardware Type | Vendor ID | Link Mode & Network IPs |
|---|---|---|---|---|---|
| **`TX1_c07bc_SFPA`** | `74fe488b07bc` | Transmitter | AVAS Transceiver | `105` | Single Link (`169.254.7.189`) |
| **`TX2_DUAL LINK_SFPA`** | `74fe488b07bb` | Transmitter | AVAS Transceiver | `105` | Dual Link (SFP A: `169.254.7.187`, SFP B: `169.254.7.188`) |
| **`RX1_c059e_SFPA`** | `74fe483c059e` | Receiver | AVAS Transceiver | `105` | Single Link (`169.254.7.190`) |
| **`RX2_DUAL LINK_SFPA`** | `74fe483c059f` | Receiver | AVAS Transceiver | `105` | Dual Link (SFP A: `169.254.7.191`, SFP B: `169.254.7.192`) |
| **`VEGA_1100`** | `001e06...` | Transmitter | Third-Party Transceiver | `0` (or `!= 105`) | Single Link (Non-AVAS, No Preview MCU) |

### Server Ports:
* **BlueRiver REST API**: `http://127.0.0.1:8090`
* **BlueRiver TCP CLI**: `127.0.0.1:6970`
* **BAVM Web Console**: `http://127.0.0.1:80`
* **Local RTP Ingestion Ports**: UDP `5000` and `5004`

---

## 2. Semtech BlueRiver REST API Specifications

The tool communicates with the Control Server via HTTP REST on port 8090.

### 2.1 Device Discovery
* **Endpoint**: `GET http://127.0.0.1:8090/api/device/ALL`
* **Response**: Returns a JSON object indexed by device MAC with device capabilities, network interfaces, and operational state.

### 2.2 Single-View Video Routing (`join`)
* **Endpoint**: `POST http://127.0.0.1:8090/api/device/{rxMac}`
* **Headers**: `Content-Type: application/json`
* **Request Body**:
  ```json
  {
    "op": "join",
    "subscription_type": "HDMI",
    "subscription_index": 0,
    "source_device": "{txMac}",
    "source_index": 0
  }
  ```
* **Success Response**:
  ```json
  {
    "status": "PROCESSING",
    "request_id": 42
  }
  ```
  *(Note: The Control Server often returns pretty-printed JSON with spaces: `"status" : "PROCESSING"` or `"status" : "SUCCESS"`. The tool's `RestResponse.IsAccepted` parses this structurally.)*

### 2.3 Video Un-routing (`leave`)
* **Endpoint**: `POST http://127.0.0.1:8090/api/device/{rxMac}`
* **Headers**: `Content-Type: application/json`
* **Request Body**:
  ```json
  {
    "op": "leave",
    "subscription_type": "HDMI",
    "subscription_index": 0
  }
  ```
* **Success Response**: Status `PROCESSING` or `SUCCESS`.

---

## 3. Semtech BlueRiver TCP CLI Protocol

The tool maintains a low-level TCP socket connection to `127.0.0.1:6970`.

* **End-of-Line**: All commands must end with `\r\n`.
* **Identity Query**:
  ```
  get all identity\r\n
  ```
* **Stream 1 Scaler Acquisition**:
  ```
  set {txMac} stream HDMI 1 enable true address {multicastIp} auto_stop true remove_audio true\r\n
  ```
* **Stream 1 Scaler Teardown**:
  ```
  set {txMac} stream HDMI 1 enable false address 0.0.0.0\r\n
  ```

---

## 4. In-Band Cypress Baseboard MCU UART Protocol

AVAS units (`VendorId == 105`) contain an internal Cypress PSoC MCU managing hardware video encoding and preview streaming. Commands are dispatched over the BlueRiver in-band UART channel.

### Activation Sequence:
1. **Switch to API Command Mode**:
   ```
   switch:api\r\n
   ```
2. **Associate IGMP Multicast Address & Destination Port**:
   ```
   set rtp igmp {multicastIp} 5004 {sourceIp}\r\n
   ```
3. **Start RTP Stream**:
   ```
   set rtp ON\r\n
   ```
4. **Stop RTP Stream**:
   ```
   set rtp OFF\r\n
   ```

---

## 5. RFC 4175 Raw Video RTP Packet Specification

Preview video frames are transmitted as uncompressed RFC 4175 packets over RTP UDP:

### 5.1 RTP Fixed Header (RFC 3550):
* **Byte 0**: `Version = 2`, `Padding = 0`, `Extension = 0`, `CSRC Count = 0` (`0x80`).
* **Byte 1**: `Marker Bit (M)` (Bit 7: 1 if final packet of frame, 0 otherwise) + `Payload Type` (Bits 6-0: `96`).
* **Bytes 2–3**: `Sequence Number` (16-bit big-endian).
* **Bytes 4–7**: `Timestamp` (32-bit big-endian, 90 kHz clock).
* **Bytes 8–11**: `SSRC` (Synchronization Source Identifier).

### 5.2 RFC 4175 Payload Header:
* **Extended Sequence Number**: 16-bit.
* **Scanline Headers**:
  * `Line Number`: 15 bits (Y coordinate of the scanline).
  * `Field ID`: 1 bit.
  * `Offset`: 15 bits (X pixel offset in the scanline).
  * `Length`: 16 bits (byte count of pixels in this chunk).

### 5.3 Pixel Format (YUV 4:2:2):
* Packed as 4 bytes per 2 pixels: `[ U0, Y0, V0, Y1 ]`.
* Color conversion formula (Q10 fixed-point BT.601 in `Yuv422Rasterizer.cs`):
  $$\text{C} = \text{Y} - 16$$
  $$\text{D} = \text{U} - 128$$
  $$\text{E} = \text{V} - 128$$
  $$\text{R} = \text{Clamp}( (298 \times \text{C} + 409 \times \text{E} + 128) \gg 8 )$$
  $$\text{G} = \text{Clamp}( (298 \times \text{C} - 100 \times \text{D} - 208 \times \text{E} + 128) \gg 8 )$$
  $$\text{B} = \text{Clamp}( (298 \times \text{C} + 516 \times \text{D} + 128) \gg 8 )$$
* Output pixel format: `BGRA32` (32 bits per pixel: Blue, Green, Red, 0xFF Alpha).

---

## 6. Multicast IP Pool

Managed by `MulticastIpManager.cs`:
* **Pool Range**: `225.0.0.1` to `225.0.1.254` (510 usable Class D multicast addresses).
* **Deterministic Allocation**: IPs are allocated sequentially and sorted by `DeviceName` on startup.
* **Reclamation**: When a preview stream is stopped, its IP address is recycled for the next requester.
