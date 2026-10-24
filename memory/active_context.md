# Active Context

## Current Status & Focus
* **Current Phase**: Phase 1 (Single View Video Routing) Live Verification, Discovery Fix & Diagnostics Engine Complete.
* **Core Architecture**: Approach A (Dual-Mode Shell: Native Visual Routing Canvas + Embedded BAVM Console in WebView2 + Live Diagnostics Tab).
* **Key Fixes & Refinements Delivered**:
  1. **Semtech Device Discovery**: Resolved root cause of 0 devices discovered: switched to Semtech BlueRiver standard `get all identity` via Telnet and REST `POST /api/device/ALL` (`{"op":"get","subset":"identity"}`), parsing nested `result.devices[...]`, `identity.is_transmitter`/`is_receiver`, and `nodes[].status.ip.address`.
  2. **Socket Concurrency**: Eliminated concurrent read collision on `StreamReader` by implementing a dedicated asynchronous read loop delivering command responses to `_pendingCommandTcs` and unsolicited notifications to `DeviceStateChanged`.
  3. **IPv4 Host Normalization**: `DerivedControlServerIp` normalizes loopback `localhost` to `127.0.0.1` to prevent Windows IPv6 lookup issues on control ports.
  4. **Integrated Diagnostics & Logging Subsystem**: Added `AppLogger` recording timestamped trace events to `app.log` and streaming live into a new in-app `[ 📋 Diagnostics & Logs ]` tab with log folder navigation and buffer clearing.
  5. **Live Verification**: Successfully connected to active BlueRiver Control Server at `127.0.0.1:6970` / `8090`, discovering all 4 physical transceivers:
     - `RX1_c059e_SFPA` (`74fe483c059e`, IP: `10.32.32.17`, RX)
     - `TX2_DUAL LINK_SFPA` (`74fe488b07bb`, IP: `10.32.32.12`, TX)
     - `PAX355S_SFP A` (`74fe488b22ca`, IP: `10.32.32.15`, RX)
     - `VEGA_1100` (`0016c0589c1e`, IP: `10.32.32.20`, TX)

## Recent Changes
* Implemented `AppLogger` (`src/AvasRoutingTool/Logging/AppLogger.cs`).
* Overhauled `SdvoeClient.cs` with robust discovery and concurrency control.
* Added Diagnostics & Logs UI in `MainWindow.xaml` and `MainViewModel.cs`.
* Added 3 new tests in `SdvoeClientTests.cs` and `LiveControlServerTests.cs` (16/16 tests passing).
* Re-published standalone package in `publish/AvasRoutingTool/AvasRoutingTool.exe`.

## Next Steps
1. Perform video routing drag-and-drop test between `TX2_DUAL LINK_SFPA` / `VEGA_1100` and `RX1_c059e_SFPA` / `PAX355S_SFP A`.
2. Prepare Phase 2 MultiView layouts pipeline.
