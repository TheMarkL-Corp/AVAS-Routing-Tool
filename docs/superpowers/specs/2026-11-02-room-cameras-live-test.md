# Room Cameras: live test checklist

Who runs this: a person with the real room camera and the app on the real Room Cameras PC.
Agents do not run this list. Tick a box only when you saw the result yourself.

Plan: [`docs/superpowers/plans/2026-11-02-room-cameras.md`](../plans/2026-11-02-room-cameras.md). Byte source: `src/AvasRoutingTool/Services/Visca/ViscaCommands.cs`.

## Before you start

- [ ] Clear the room of people. Keep the camera's own handset or a staff member at the camera, with a hand near it.
- [ ] Have a way to stop the camera by hand (handset Stop, or the camera's power switch).
- [ ] Write down the camera model and firmware: ______________________
- [ ] Write down the camera's VISCA address (1 is the default in the app): ______
- [ ] Have the camera's VISCA manual open at the command table.

## 1. Which transport answers

Open the camera editor (pencil on the Room Cameras tab). Type the camera's host. Press **Detect**.

The app tries these candidates. Tick the ones that answer.

- [ ] TCP 5678
- [ ] TCP 1259
- [ ] TCP 3001
- [ ] UDP 52381
- [ ] UDP 1259
- [ ] UDP 3001

Result: transport ______ (TCP or UDP), port ______.

- [ ] Detect shows at least one hit. If it shows none, type the port from the camera's network page and press **Test connection**.
- [ ] If two hits show, note both and pick the one the camera's manual names as VISCA over IP.
- [ ] Save the camera. The status dot shows a green dot with the word "Connected" (dot plus word, never colour alone).

## 2. VISCA command table: check every row against the camera manual

Address byte `81` means camera 1. For camera N the first byte is `8N`. Terminator is `FF` on every packet.
`VV` is pan speed, `WW` is tilt speed, `p` is zoom or focus speed (0 to 7), and `0p 0q 0r 0s` are the four nibbles of a position.

For each row, look up the same command in the manual. Tick **matches manual** only when the bytes are identical.

### 2a. Pan and tilt drive (`81 01 06 01 VV WW pp qq FF`)

Speed used for the check: Medium (`VV` 0C, `WW` 0A).

| Direction | Bytes (`pp qq` set) | Matches manual |
|---|---|---|
| Up | `81 01 06 01 0C 0A 03 01 FF` | [ ] |
| Down | `81 01 06 01 0C 0A 03 02 FF` | [ ] |
| Left | `81 01 06 01 0C 0A 01 03 FF` | [ ] |
| Right | `81 01 06 01 0C 0A 02 03 FF` | [ ] |
| Up-left | `81 01 06 01 0C 0A 01 01 FF` | [ ] |
| Up-right | `81 01 06 01 0C 0A 02 01 FF` | [ ] |
| Down-left | `81 01 06 01 0C 0A 01 02 FF` | [ ] |
| Down-right | `81 01 06 01 0C 0A 02 02 FF` | [ ] |

### 2b. Stop, home

| Command | Bytes | Matches manual |
|---|---|---|
| Stop pan and tilt | `81 01 06 01 01 01 03 03 FF` | [ ] |
| Home | `81 01 06 04 FF` | [ ] |

### 2c. Zoom

Speed used for the check: `p` = 4.

| Command | Bytes | Matches manual |
|---|---|---|
| Zoom tele (in) | `81 01 04 07 24 FF` | [ ] |
| Zoom wide (out) | `81 01 04 07 34 FF` | [ ] |
| Zoom stop | `81 01 04 07 00 FF` | [ ] |
| Zoom direct (example position 0x2A00) | `81 01 04 47 02 0A 00 00 FF` | [ ] |

Zoom direct layout: `81 01 04 47 0p 0q 0r 0s FF`. For 0x2A00 the nibbles are 2, A, 0, 0.

### 2d. Focus

| Command | Bytes | Matches manual |
|---|---|---|
| Focus auto | `81 01 04 38 02 FF` | [ ] |
| Focus manual | `81 01 04 38 03 FF` | [ ] |
| Focus far (speed 4) | `81 01 04 08 24 FF` | [ ] |
| Focus near (speed 4) | `81 01 04 08 34 FF` | [ ] |
| Focus stop | `81 01 04 08 00 FF` | [ ] |
| Focus direct (example position 0x2A00) | `81 01 04 48 02 0A 00 00 FF` | [ ] |

### 2e. Power

| Command | Bytes | Matches manual |
|---|---|---|
| Power on | `81 01 04 00 02 FF` | [ ] |
| Power off | `81 01 04 00 03 FF` | [ ] |

### 2f. Inquiries (the camera answers; the app reads the reply)

| Inquiry | Bytes | Expected reply | Matches manual |
|---|---|---|---|
| Power | `81 09 04 00 FF` | `90 50 02 FF` on, `90 50 03 FF` off, `90 50 04 FF` fault | [ ] |
| Zoom position | `81 09 04 47 FF` | `90 50 0p 0q 0r 0s FF` | [ ] |
| Focus position | `81 09 04 48 FF` | `90 50 0p 0q 0r 0s FF` | [ ] |
| Focus mode | `81 09 04 38 FF` | `90 50 02 FF` auto, `90 50 03 FF` manual | [ ] |

### 2g. Replies the app must handle

| Reply | Bytes | Matches manual |
|---|---|---|
| ACK | `90 4y FF` | [ ] |
| Completion | `90 5y FF` | [ ] |
| Syntax error | `90 6y 02 FF` | [ ] |
| Buffer full | `90 6y 03 FF` | [ ] |
| Cancelled | `90 6y 04 FF` | [ ] |
| Not executable | `90 6y 41 FF` | [ ] |

### 2h. UDP only: header and sequence reset

UDP frames have an 8-byte header: payload type (2 bytes), payload length (2 bytes), sequence number (4 bytes). All big-endian.

| Payload type | Value | Matches manual or camera doc | 
|---|---|---|
| Command | `0x0100` | [ ] |
| Inquiry | `0x0110` | [ ] |
| Reply | `0x0111` | [ ] |
| Control command | `0x0200` | [ ] |
| Control reply | `0x0201` | [ ] |

- [ ] The camera manual confirms the 8-byte header is the VISCA over IP header on this camera.
- [ ] The camera uses the port and transport found in section 1.

## 3. Calibrate the ranges and the speed table

### 3a. Zoom and focus ranges

The app stores these as two fields for each camera, `ZoomMax` and `FocusMax`. The defaults are 16384 (0x4000) for zoom and 65535 (0xFFFF) for focus.

Steps:

- [ ] Close the app. The app rewrites its config file on Save, so edit only with the app closed.
- [ ] Open `appsettings.json` in the app folder (the folder that holds `AvasRoutingTool.exe`). The file comes from `src/AvasRoutingTool/Configuration/ConfigService.cs`.
- [ ] Find the `RoomCameras` entry for this camera.
- [ ] Start the app. Drive zoom tele at Fast until the lens stops at its end. The app shows no position number: read the end position with the zoom inquiry (`81 09 04 47 FF`, see 2f) from a VISCA tool, or compare where the slider ends with where the lens ends.
- [ ] Put that value in `ZoomMax`, as a decimal number. Example: 0x2A00 is 10752.
- [ ] Repeat for focus far and focus near, and put the end value in `FocusMax`.
- [ ] Restart the app and check the sliders reach both ends.

Recorded values: ZoomMax ______ (hex ______), FocusMax ______ (hex ______).

### 3b. Speed table

The speed table is in code, not in the config file. It lives in `SpeedFor` in `src/AvasRoutingTool/ViewModels/RoomCameraViewModel.cs`. Edit it there and rebuild.

Initial values (from the plan): pan and tilt speeds and zoom speed for each step.

| Step | Pan `VV` | Tilt `WW` | Zoom and focus `p` | Looks right in the room |
|---|---|---|---|---|
| Slow | 0x05 | 0x04 | 2 | [ ] |
| Medium | 0x0C | 0x0A | 4 | [ ] |
| Fast | 0x14 | 0x10 | 6 | [ ] |

- [ ] Fast is not the camera maximum. It is a comfortable speed for an operating room.
- [ ] Changed values: ______________________

## 4. Stop and safety checks

### 4a. Unplug the network cable mid-move

- [ ] Start a Medium pan move to the right. Hold the key.
- [ ] Unplug the network cable from the camera while it moves.
- [ ] Watch the camera. Does it stop by itself? Record: [ ] stops at once  [ ] keeps moving until its own limit  [ ] other: ______
- [ ] Plug the cable back in. Note what the app shows (the link text and dot) and whether the camera is still moving.
- [ ] If the camera keeps moving with the cable out, record that the 10 s watchdog cannot reach it. The watchdog is only a net while the network works. Note this in the report.

### 4b. Kill the app mid-move

- [ ] Start a Slow tilt move. Hold the key.
- [ ] End the app from Task Manager (not from the window).
- [ ] Watch the camera. Record: [ ] stops at once  [ ] keeps moving until its own limit  [ ] other: ______
- [ ] Stop the camera by hand if it keeps moving.

### 4c. A TX preview that is already live

- [ ] Start Visual Routing. Let a bound TX source card auto-start its preview.
- [ ] Open the Room Cameras tab. The bound camera shows the same picture.
- [ ] Leave the Room Cameras tab for Visual Routing. The preview keeps running. The tab does not stop a preview it did not start.
- [ ] The live surgical picture on the RX display does not change while you use the tab.

### 4d. Refresh in Visual Routing while the Room Cameras tab is open

- [ ] Open the Room Cameras tab. The bound camera shows its picture.
- [ ] Press **Refresh** in the app bar.
- [ ] The bound source is still bound, and the picture comes back (no "No source bound" message).
- [ ] The camera still moves after the refresh.

### 4e. Long move and Stop

The app waits up to 3 s for each Move or Zoom drive to complete. While it waits, it holds the command queue.

- [ ] Start a long Slow pan move to the left. Let it run for at least 5 s.
- [ ] Release the key. Confirm the camera stops within about 1 s. A Stop must go before any other command.
- [ ] Repeat with a long zoom tele move, then press Stop on the screen. The camera stops at once.

### 4f. UDP: sequence reset (only if the transport is UDP)

The app sends a sequence reset with sequence number 0 when it connects.

- [ ] Connect over UDP. Confirm the camera answers the reset (a control reply, `0x0201`).
- [ ] Confirm the next command gets a normal reply. The sequence then continues from 1.
- [ ] If the camera does not accept sequence 0, record the reply it sends: ______________________

### 4g. UDP: unplug and replug (ICMP unreachable, error 10054)

When the camera is unreachable, the app drops the transport and retries. The retry gaps are 1, 2, 4, 8, 16 and then 30 s at most.

- [ ] Power the camera network port off (or unplug it). Note the time: ________
- [ ] For the first three failed attempts the app shows "Not connected - retrying" or "Connecting..." in neutral grey (no red).
- [ ] After the third failed attempt the app shows "Cannot reach" with the endpoint, and the dot turns red. Note the time: ________
- [ ] Plug the network back in. Note the time: ________
- [ ] The app shows connected again. Note the time: ________ . Recovery took ______ s (at most about 30 s after the plug-in).

### 4h. Keyboard while a slider has focus

- [ ] Click into the Zoom slider so it has keyboard focus.
- [ ] Press the arrow keys. The camera does not move. The slider moves instead.
- [ ] Click on empty space on the tab. The arrow keys move the camera again.

### 4i. Light mode disabled look

- [ ] Switch to Light mode. Turn on the Lock switch.
- [ ] From the usual seat at arm's length, can you tell which controls are disabled? [ ] yes  [ ] no. If no, note which controls: ______________________

## 5. Power and status (quick check)

- [ ] Power on from the app. The camera powers on and the app shows the power state as On.
- [ ] Power off asks for an inline confirmation that names the camera. Confirm. The camera powers off.
- [ ] The power state shows as Off after the next 5 s poll.

## 6. Result

- [ ] All boxes above are ticked, or each open item has a note.
- [ ] Transport and port found: ______________________
- [ ] ZoomMax and FocusMax recorded in section 3a.
- [ ] Speed table changes recorded in section 3b.
- [ ] Tester name and date: ______________________
