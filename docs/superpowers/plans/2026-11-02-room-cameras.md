# Room Cameras Tab Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Implementers are Haiku 5.5 subagents (one per task); Sonnet or lower reviews; never Opus.

**Goal:** Add a main-window tab "Room Cameras" (order: Visual Routing, Room Cameras, BAVM Console, Logs) where the operator picks a room camera, moves it (pan/tilt in 8 directions, home, zoom, focus) over VISCA IP, sees its power and link state, and views its picture through the SDVoE TX source the camera is bound to.

**Architecture:** An own small VISCA-over-IP client in `Services/Visca/` (pure command builders, a frame codec, two socket transports behind `IViscaTransport`, an `IPtzCamera` facade with a link state machine) with a loopback fake camera for tests. A pure-logic `PtzMotionController` owns the safety rules (hold-to-move, Stop on every exit, watchdog). View models resolve a camera's bound TX (by MAC) against the existing `MainViewModel.Sources` cards and reuse that card's preview stream; no second RTP receiver is created. Config lives in `AppConfig.RoomCameras`. UI is a 16:9 picture hero with a right control rail, built only from WPF-UI controls and central styles.

**Tech Stack:** .NET 8 WPF MVVM, WPF-UI 4.3.0, `System.Net.Sockets`, xUnit with STA view tests. No new NuGet package.

**Spec:** this document is its own spec (decisions below, approved by the user on 2026-11-02 in a grilling session). Inherits `docs/superpowers/specs/2026-10-29-ui-v2-master-guidelines.md` (D1-D12, guards) and `docs/superpowers/plans/2026-11-02-ui-v2-round2-master.md` (Q-A..Q-E, ownership, execution model). The repo has no `CLAUDE.md` in the worktree or main checkout; the two documents above carry the repo rules.

## Global Constraints

- **Start gate:** nothing starts before `ui-v2/round2` exists with checkpoint 2 merged (tag `ui-v2-round2-cp2`). Work on a new branch `ui-v2/room-cameras` created from `ui-v2/round2`. Never merge to `main`, never push.
- D1-D12 stay locked: WPF-UI theme and controls only; vector icons only (`StreamGeometry` / `ui:SymbolIcon`); touch targets `Size.Touch.Min` 48 px (`Size.Touch.Compact` 36 px); font floor 12 px via `Font.Size.*` tokens; 1920x1080 maximized target, 1024x720 floor; Dark and Light.
- Hard guards (`UiV2Guards` + `UiV2Guards.allowlist.json`): no literal sizes, no keyed `Style` or `ControlTemplate` in view files, no raw hex outside `Resources/Themes/`. This feature adds **no allowlist entries**. Its sizes live in `Resources/Styles/RoomCameras.xaml` as `Size.Camera.*` (the Composer pattern), never in `Tokens.xaml`.
- Accent roles (Q-B): blue = selection, pressed key, primary action; green = success only; red = errors only; amber = staged only (not used here); status is a dot plus a word, never colour alone.
- Zero socket leaks: every `Socket`, `TcpClient`, `UdpClient`, `CancellationTokenSource` is owned by one object that disposes it; tests prove a port can be rebound after dispose.
- A PTZ command must never leave the camera moving: Stop is sent on button release, lost mouse/touch capture, focus loss, key up, tab switch, window deactivate, window close, view unload, and by a 10 s watchdog. A Stop that is not acknowledged is retried 3 times and then shown in red as "Stop not confirmed".
- The tab never changes the live surgical picture: it sends no routing command, never starts a stream the operator did not ask for, and stops only a preview stream it started itself.
- TDD: red test first, then the change. STA view tests for every view change. Atomic commits, one task per commit, subject `feat(room-cameras): <what>`, body ends with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.
- Test command (own BaseOutputPath, never the default): `dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj -p:BaseOutputPath=bin/rc/ --filter "FullyQualifiedName!~LiveControlServerTests&FullyQualifiedName!~LiveFunctionTests" -- RunConfiguration.DisableParallelization=true`, with `--blame-hang-timeout 120s`. No more than one or two `MainWindow` instances per new test class (known flake).
- Subagent models: Haiku 5.5 implements; Sonnet or lower reviews. Never Opus.

## Decisions (approved by the user on 2026-11-02, binding)

| ID | Decision |
|---|---|
| R1 | Preview path: the bound TX's existing SDVoE preview only. RTSP is out of scope for v1 (would need LibVLCSharp or FFmpeg native binaries and a second socket surface). |
| R2 | Storage: `AppConfig.RoomCameras` (`List<RoomCameraConfig>`) in `appsettings.json` via `IConfigService`. The TX binding is the TX MAC (`AvasDevice.DeviceId`), so renames and IP changes do not break it. |
| R3 | Transport per camera: `Udp` (VISCA over IP with the 8-byte header, default port 52381) or `Tcp` (raw VISCA, default port 5678). Any port may be entered: the on-site cameras use generic VISCA over IP on a user-set port (for example 1259 or 3001). The editor has a **Detect** button (R15). |
| R4 | Press-and-hold motion plus the Stop-on-everything and watchdog safety model above. |
| R5 | Preview: reuse the bound TX's `SourceCardViewModel` stream (one socket owner). Source cards auto-start previews on discovery (`MainViewModel.cs` ~l.355-368), so a bound camera is usually already live; the tab offers Start only when the card is in standby. |
| R6 | Camera setup is in the tab: a pencil opens `CameraEditorDialog` (name, host, port, transport, bound TX, Test connection). `SettingsDialog` is untouched. |
| R7 | No presets in v1. |
| R8 | Power: state from a VISCA power inquiry polled every 5 s; Power On is one tap; Power Off asks an inline confirmation (not a modal) naming the camera. |
| R9 | Selector: a row of 48 px camera chips above the picture, one active camera at a time. |
| R10 | Layout (revised after the design consult): picture hero on the left, control rail on the right (360 px at 1920, 300 px at 1024). Chosen over a control bar above the picture because it gives a 25 % larger picture at 1920x1080 (about 1547x870 against 1234x694) and puts the pad under the thumb. |
| R11 | Zoom and Focus are absolute-position sliders (VISCA Direct commands, throttled to 5 per second while dragging, final value on release) with position readback; Auto focus is a switch that disables the Focus slider while on. |
| R12 | Speed is a three-step segmented control (Slow, Medium, Fast), not a slider, so the operator never needs a second hand while holding a key. Remembered per camera for the session. |
| R13 | A Lock switch disables every motion control (accidental touches by staff leaning on the screen). Not persisted, one tap to unlock. |
| R14 | Keyboard: arrow keys move, `+` and `-` zoom, `Home` sends the camera home, `Esc` stops; Stop on key up. Active only when the Room Cameras view has focus and no text box is focused. |
| R15 | Generic VISCA IP with a user-configurable port (for example 1259 or 3001). Auto-detect: from the host the operator typed, probe a short candidate list of (transport, port) pairs with a power inquiry and offer what answers (RC6A). |
| R16 | Link state colours: neutral grey while retrying, red after 3 failed attempts (amber stays reserved for staged). Round 2 `MainWindow.xaml` scope: the tab plus one content-host line. No further UI inspirations beyond the two screenshots. Start gate and branch confirmed. |

## Reference study: why we write our own client

Cloned `https://github.com/louangr/VISCACameraController` to a scratch folder outside the repo and read it as untrusted data.

| Finding | Consequence |
|---|---|
| Licence **GPL-3.0**. The app is proprietary (Advantech). | Cannot copy code or link a GPL library. |
| Transport is **serial** (`System.IO.Ports`), no VISCA over IP, no UDP header, no TCP. | Does not do the job. |
| WinUI 3 app on `net6.0-windows10.0.19041`, Newtonsoft.Json, CommunityToolkit 8.0. | Not a library; not .NET 8 WPF. |
| Commands live as hex strings in `viscaCommands.json`; preset recall, pan/tilt with `{S}` speed placeholders, zoom, focus, power. | Confirms the byte patterns we implement; protocol facts, not code. We write our own builders and golden-hex tests. |

Decision: **port nothing, add no dependency.** About 300 lines of builders, codec and transports are cheaper than vetting a package, and own code can be mocked and leak-tested to our rules. No third-party VISCA NuGet package was evaluated.

## VISCA command set (v1)

Camera address `0x81` (camera 1), terminator `0xFF`. Pan speed `VV` 0x01-0x18, tilt speed `WW` 0x01-0x14, zoom/focus speed `p` 0-7. All values below are the Sony-standard patterns; **RC2's golden tests must be cross-checked by the reviewer against the on-site camera's VISCA manual, and RC14 lists the live checks.**

| Action | Bytes |
|---|---|
| Pan/tilt drive | `81 01 06 01 VV WW pp qq FF`; `pp` 01 left, 02 right, 03 none; `qq` 01 up, 02 down, 03 none. Up-left is `01 01`, down-right `02 02`. |
| Stop pan/tilt | `81 01 06 01 01 01 03 03 FF` |
| Home | `81 01 06 04 FF` |
| Zoom tele / wide / stop | `81 01 04 07 2p FF` / `3p` / `00` |
| Zoom direct | `81 01 04 47 0p 0q 0r 0s FF` (nibbles of the position) |
| Focus auto / manual | `81 01 04 38 02 FF` / `03` |
| Focus far / near / stop | `81 01 04 08 2p FF` / `3p` / `00` |
| Focus direct | `81 01 04 48 0p 0q 0r 0s FF` |
| Power on / off | `81 01 04 00 02 FF` / `03` |
| Inquiries | power `81 09 04 00 FF` (reply `y0 50 02 FF` on, `03` off, `04` fault); zoom position `81 09 04 47 FF`; focus position `81 09 04 48 FF`; focus mode `81 09 04 38 FF` (`02` auto, `03` manual) |
| Replies | ACK `90 4y FF`, completion `90 5y FF`, errors `90 6y 02` syntax, `03` buffer full, `04` cancelled, `41` not executable |

UDP framing (R3): 8-byte header, payload type `0x0100` command / `0x0110` inquiry / `0x0111` reply / `0x0200` control command (payload `01` = sequence number reset) / `0x0201` control reply; 2-byte payload length; 4-byte big-endian sequence number; then the VISCA bytes. TCP carries the VISCA bytes alone and is re-assembled on the `FF` terminator.

Initial speed table (tune on site): Slow pan 0x05 tilt 0x04 zoom 2; Medium 0x0C / 0x0A / 4; Fast 0x14 / 0x10 / 6 (not the camera maximum: this is an operating room). Zoom and focus slider ranges default to 0x0000-0x4000 (zoom) and 0x0000-0xFFFF (focus) and are calibrated in RC14; they are fields of `RoomCameraConfig` (`ZoomMax`, `FocusMax`).

## Camera and binding model

`Configuration/RoomCameraConfig.cs`:

```csharp
public enum ViscaTransportKind { Udp, Tcp }
public class RoomCameraConfig {
    public string Id { get; set; } = Guid.NewGuid().ToString("N");   // stable key
    public string Name { get; set; } = "Room camera";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 52381;
    public ViscaTransportKind Transport { get; set; } = ViscaTransportKind.Udp;
    public int ViscaAddress { get; set; } = 1;                       // 1-7
    public string BoundTxMac { get; set; } = "";                     // AvasDevice.DeviceId; empty = unbound
    public int ZoomMax { get; set; } = 0x4000;
    public int FocusMax { get; set; } = 0xFFFF;
}
// AppConfig: public List<RoomCameraConfig> RoomCameras { get; set; } = new();
```

A TX can be bound to several cameras; a camera to one TX. Unbound cameras still control; the picture shows "No source bound". The binding resolves against `MainViewModel.Sources` every time that collection changes (Refresh rebuilds every card). Config is read-modify-write through `IConfigService` (never a cached copy), because `ThemeService` and `SettingsDialog` also Load and Save the whole file.

## Preview states (the picture area)

| State | Shown | Next action named |
|---|---|---|
| No source bound | "No source bound to ROOM-CAM" | button "Bind a source" (opens the editor) |
| Source offline | "TX1_c07bb is offline" | none; auto-resolves |
| Standby | "Preview standby" | primary "Start preview" (calls the card's `StartPreviewCommand`; disabled with a reason when the card has no multicast address) |
| Starting | "Starting preview..." | none |
| Live | the `PreviewBitmap` image, `Stretch=Uniform` on `Brush.Background.Dark`; caption line: camera, bound TX, resolution, fps | "Stop preview" only when this tab started it |

Controls are never drawn over the picture (the picture must not be occluded).

## UI layout

Rail width token `Size.Camera.Rail` (min 300, max 360). Picture is `*`, rail is `Auto` with the min/max pair. Pad keys `Size.Camera.PadKey` 56 px (above the 48 px minimum).

```
1920x1080 (content area ~1920x930)
+----------------------------------------------------------------------+
| [ROOM-CAM o] [OR-2 ceiling o] [+ Add]                      chips 48px |
+--------------------------------------------------+-------------------+
|                                                  | ROOM-CAM  [edit]  |
|                                                  | o Connected       |
|            picture 16:9  (~1547 x 870)            | o Camera on       |
|                                                  | Moving up-left    |
|                                                  | [Power]  [Lock]   |
|                                                  | +---+---+---+     |
|                                                  | | UL| U | UR|     |
|                                                  | | L | H | R |     |
|                                                  | | DL| D | DR|     |
|                                                  | +---+---+---+     |
|                                                  | Speed [Slow|Med|Fast]
|                                                  | Zoom   Wide ---o-- Tele
|                                                  | Focus  Near ---o-- Far
|                                                  |        [x] Auto focus
+--------------------------------------------------+-------------------+
| ROOM-CAM · via TX1_c07bb_SFPA · 1080p60 · 29.9 fps  [Stop preview]   |
+----------------------------------------------------------------------+

1024x720 (content ~1024x620): rail 300; picture ~700x394; chips row scrolls
sideways instead of wrapping; rail content scrolls inside its own ScrollViewer
(never the picture); the pad, speed, zoom stay visible without scrolling.
```

Visual rules from the design consult (Operate mode: the tool disappears into the task): flat surfaces (tone steps, no shadows, no gradients); one blue only for a pressed key, the selected chip and the primary action; the pad shows the pressed key blue and a live "Moving up-left" line (dot plus word) beside the picture border switching to the selected-chip blue while moving; link states: green dot "Connected", neutral grey "Not connected - retrying in 4 s", red "Cannot reach 192.168.1.163:1259" after 3 failed attempts; red banner row above the picture for "Stop not confirmed - press Stop or switch the camera off" (never a toast, never over the picture). End words under the sliders (Wide/Tele, Near/Far) so no slider relies on an icon. Every control has an `AutomationProperties.Name` that includes its state (spoken: "Pan left, hold to move").

**Inspirations used:** the ViscaCamLink window (compact pad, status dots, slider look) and the Caresyntax OR screen (camera chip, power tile, Zoom and Focus sliders with Auto, picture hero). The Caresyntax bar-above layout was rejected for the measured picture-size reason in R10. These are the only inspirations (R16). Mockup of both sizes was shown to the user for approval before RC9 starts; in the mockup the failed-Stop banner is drawn at the top of the picture area, in the build it is its own row above the picture so it covers no picture pixels.

## Ownership (prevents collisions with round 2)

Round 2 is merged before this starts, so no lane overlaps. Files this plan touches in shared areas, and how:

| File | Edit |
|---|---|
| `MainWindow.xaml` | Two insertions only: the `TabRoomCameras` RadioButton before `TabBavmConsole` (`Style.Tab.Nav`, `GroupName="MainTabs"`, `Checked="Tab_Checked"`), and `<views:RoomCamerasView x:Name="ViewRoomCameras" Visibility="Collapsed" />` in the content grid. Nothing else. |
| `MainWindow.xaml.cs` | `Tab_Checked` branch, `Deactivated`/`Closed` stop hooks, construct and dispose `RoomCamerasViewModel`. |
| `App.xaml` and `TestResources.Merged` | One line each: merge `Resources/Styles/RoomCameras.xaml` after `Composer.xaml` (a test compares them). |
| `ThemeResourceUsageTests.cs` | Append `InlineData` rows only. |
| `Configuration/AppConfig.cs` | Add `RoomCameras`. |
| `Handover/02_CODEBASE_STRUCTURE.md` | Add the new folders (docs only). |
| **Not touched** | `MainViewModel.cs`, `SourceCardViewModel.cs`, `Tokens.xaml`, `Cards.xaml`, `Badges.xaml`, themes, `SettingsDialog`, `UiV2Guards.allowlist.json`. |

New files: `Services/Visca/*` (ViscaCommands, ViscaFrame, ViscaResponse, IViscaTransport, TcpViscaTransport, UdpViscaTransport, IPtzCamera, ViscaCamera, PtzCameraFactory), `Services/PtzMotionController.cs`, `ViewModels/RoomCameraViewModel.cs`, `ViewModels/RoomCamerasViewModel.cs`, `ViewModels/CameraEditorViewModel.cs`, `Views/RoomCamerasView.xaml(.cs)`, `Views/PtzPadView.xaml(.cs)`, `Views/HoldButton.cs`, `Views/CameraEditorDialog.xaml(.cs)`, `Resources/Styles/RoomCameras.xaml`; tests `tests/AvasRoutingTool.Tests/Visca*.cs`, `Fakes/FakeViscaServer.cs`, `Fakes/FakePtzCamera.cs`, `RoomCameras*Tests.cs`, `UiV2RoomCameras*Tests.cs`, `ViewSmokeTests.RoomCamerasShots.cs`.

## Review Focus

Failure modes the feature implies that need a named test (each is in the task shown):

1. **Camera keeps moving after any exit** (button released off the key, finger lifted outside, window deactivated, tab switched, view hidden, app closed, network drop mid-move): Stop is sent every time and the retry/"Stop not confirmed" path shows. (RC7, RC10, RC13)
2. **Refresh rebuilds every source card** while a camera is bound and live: the binding re-resolves by MAC, the picture is not lost, no duplicate stream is started, and a stream the tab started is still stopped once. (RC8)
3. **Camera unreachable, wrong port, or a UDP camera answered on the wrong transport**: no frozen UI, no exception to the user, no leaked sockets, reconnect with backoff and a plain-word state. (RC4, RC5, RC6)
4. **Config clobbering**: saving cameras must not erase the theme, and a theme toggle or the Settings dialog must not erase cameras; a legacy `appsettings.json` without `RoomCameras` loads. (RC1, RC8)
5. **Long or empty data**: a 40+ character camera name or TX name truncates with a tooltip; zero cameras shows an empty state with "Add a camera"; zero discovered TXs lets the editor save an unbound camera. (RC11, RC12)

## Execution model

Same as round 2: a lead session creates `ui-v2/room-cameras` from `ui-v2/round2`, and for each task dispatches **one fresh Haiku 5.5 subagent** (`model: "haiku"`, `subagent_type: "general-purpose"`, no worktree isolation, sequential on the branch) with a task card: branch and BaseOutputPath `bin/rc/`; the task text copied from this plan; the exact files, test names, assertions, interfaces consumed and produced; steps red, confirm FAIL for the stated reason, implement, focused PASS, full non-live suite PASS, commit; stop conditions (any other test red, any guard failure, any edit outside the task's files). A Haiku subagent that fails a task twice is re-dispatched once on Sonnet 5.5; XAML-heavy RC9, RC11, RC12 get a pre-written XAML skeleton in the card. The lead reads every diff and runs the full suite per task. Two lanes may run at once because their files are disjoint: **Lane P** (RC1-RC8 including RC6A, protocol and logic) and **Lane V** (RC9, RC10, then RC11-RC12 once RC8 is merged). RC13-RC14 are sequential at the end. A Sonnet reviewer checks `git diff ui-v2/round2..ui-v2/room-cameras` after RC8 and after RC14 (standards and this plan); findings return as new tasks.

## Tasks

### Task RC1: Config model

**Files:** Create `Configuration/RoomCameraConfig.cs`; Modify `Configuration/AppConfig.cs`; Test `tests/AvasRoutingTool.Tests/RoomCameraConfigTests.cs`.
**Produces:** `RoomCameraConfig`, `ViscaTransportKind`, `AppConfig.RoomCameras`.
- [ ] Red tests: `RoundTrip_PreservesAllCameraFields` (save then load a config with two cameras, assert every field); `LegacyFileWithoutRoomCameras_LoadsEmptyList`; `SavingCameras_KeepsTheme` (Theme "Light" survives); `SavingTheme_KeepsCameras` (Load, set Theme, Save, Load: cameras intact); `Transport_SerialisesAsName` (JSON contains `"Udp"`, not `0`; use `JsonStringEnumConverter` on the property); `CorruptCameraEntry_DoesNotDiscardOtherSettings` (a camera with `"Port": "abc"` falls back to defaults for the whole file, as `ConfigService.Load` already does, and the test pins that behaviour).
- [ ] Implement the class and the property exactly as in "Camera and binding model". Run focused then full suite. Commit `feat(room-cameras): camera config model in AppConfig`.

### Task RC2: VISCA command builders

**Files:** Create `Services/Visca/ViscaCommands.cs`; Test `tests/AvasRoutingTool.Tests/ViscaCommandsTests.cs`.
**Produces:** `enum PanTiltDirection { Up, Down, Left, Right, UpLeft, UpRight, DownLeft, DownRight }`, `enum ZoomDirection { Tele, Wide }`, `enum FocusDirection { Far, Near }`, `static class ViscaCommands` with `byte[] PanTilt(int address, PanTiltDirection dir, int panSpeed, int tiltSpeed)`, `StopPanTilt(int address)`, `Home(int)`, `Zoom(int, ZoomDirection, int speed)`, `StopZoom(int)`, `ZoomDirect(int, int position)`, `FocusAuto(int, bool auto)`, `Focus(int, FocusDirection, int speed)`, `StopFocus(int)`, `FocusDirect(int, int position)`, `Power(int, bool on)`, and inquiries `InquirePower`, `InquireZoom`, `InquireFocus`, `InquireFocusMode`. Address 1-7 maps to `0x80 + address`.
- [ ] Red golden-hex tests, one per row of the command table, e.g. `PanTilt_UpLeft_Speed12_10` equals `81 01 06 01 0C 0A 01 01 FF`; `StopPanTilt_` equals `81 01 06 01 01 01 03 03 FF`; `ZoomDirect_0x2A5C` equals `81 01 04 47 02 0A 05 0C FF`; `Power_Off` equals `81 01 04 00 03 FF`; address 3 yields first byte `0x83`; speed out of range is clamped (pan 0x01-0x18, tilt 0x01-0x14, zoom/focus 0-7); position is clamped 0..0xFFFF.
- [ ] Implement; the reviewer checks the table against the camera manual. Commit `feat(room-cameras): VISCA command builders`.

### Task RC3: Frame codec and response parser

**Files:** Create `Services/Visca/ViscaFrame.cs`, `Services/Visca/ViscaResponse.cs`; Test `ViscaFrameTests.cs`, `ViscaResponseTests.cs`.
**Produces:** `static class ViscaFrame { byte[] Encode(ViscaPayloadType type, uint sequence, byte[] visca); bool TryDecode(ReadOnlySpan<byte> datagram, out ViscaPayloadType type, out uint sequence, out byte[] visca); byte[] SequenceReset(); }`; `enum ViscaPayloadType : ushort { Command=0x0100, Inquiry=0x0110, Reply=0x0111, Control=0x0200, ControlReply=0x0201 }`; `record ViscaResponse(ViscaResponseKind Kind, int Socket, byte[] Data, int? ErrorCode)` with `ViscaResponseKind { Ack, Completion, InquiryReply, Error }`; `class ViscaStreamReader { IEnumerable<byte[]> Feed(ReadOnlySpan<byte> chunk); }` splitting on `FF`.
- [ ] Red tests: header bytes for a command with sequence 1 (`01 00 00 05 00 00 00 01 81 ...`); decode round-trip; short or length-mismatched datagram returns false (no exception); `Feed` of a reply split across two chunks yields one message; two replies in one chunk yield two; ACK `90 41 FF` -> Ack socket 1; `90 51 FF` -> Completion; `90 60 02 FF` -> Error 0x02; `90 50 02 FF` as inquiry reply with data `02`; garbage bytes without `FF` for 1000 bytes do not grow the buffer beyond a 256-byte cap (message dropped, state reset).
- [ ] Implement; commit `feat(room-cameras): VISCA frame codec and response parser`.

### Task RC4: TCP transport and the fake camera

**Files:** Create `Services/Visca/IViscaTransport.cs`, `Services/Visca/TcpViscaTransport.cs`, `tests/.../Fakes/FakeViscaServer.cs`; Test `ViscaTcpTransportTests.cs`.
**Produces:** `interface IViscaTransport : IAsyncDisposable { Task ConnectAsync(CancellationToken ct); Task SendAsync(byte[] visca, bool isInquiry, CancellationToken ct); event Action<byte[]> MessageReceived; event Action<Exception?> Disconnected; bool IsConnected { get; } }`. `FakeViscaServer(ViscaTransportKind kind)` binds `127.0.0.1:0`, exposes `Port`, `List<byte[]> Received`, `Func<byte[], IEnumerable<byte[]>> Responder` (default: ACK then completion; inquiries answered from settable `PowerOn`, `ZoomPosition`, `FocusPosition`, `AutoFocus`), `DropNext(int n)`, `Disconnect()`, `Dispose()`.
- [ ] Red tests: `Send_DeliversBytesToServer`; `ServerReply_RaisesMessageReceived`; `ServerClose_RaisesDisconnected`; `Connect_ToClosedPort_ThrowsOrFailsWithinTimeout` (2 s, `ConnectAsync` honours its token); `Dispose_ReleasesSocket` (after dispose the fake server's accept socket sees a close, and a second transport can connect); `ConnectDisposeLoop_200Times_DoesNotGrowHandles` (process handle count after minus before < 50); `Dispose_WhileSendPending_DoesNotThrowOrHang`.
- [ ] Implement with one owned `TcpClient`/`Socket`, a single receive loop task cancelled on dispose, `NoDelay`, 1 s send timeout. Commit `feat(room-cameras): TCP VISCA transport and fake camera server`.

### Task RC5: UDP transport

**Files:** Create `Services/Visca/UdpViscaTransport.cs`; Modify `FakeViscaServer.cs` (UDP mode); Test `ViscaUdpTransportTests.cs`.
**Consumes:** `ViscaFrame` (RC3), `IViscaTransport` (RC4).
- [ ] Red tests: `Connect_SendsSequenceReset_AndFirstCommandHasSequence1`; `SequenceNumbers_Increase_PerMessage`; `ReplyWithOtherSequence_IsStillDelivered` (cameras reply with the command's number; the transport does not drop on mismatch, it only logs); `LostDatagram_IsRetriedTwice_ThenFails` (server `DropNext(3)` -> `SendAsync` completes the send but the camera layer sees a timeout; transport itself does not retry, RC6 does); `Dispose_ReleasesLocalPort` (rebind the same local port after dispose); `ConnectDisposeLoop_200Times_DoesNotGrowHandles`; `Datagram_FromOtherHost_IsIgnored` (use `Connect` on the UDP socket so the OS filters).
- [ ] Implement with one connected `UdpClient`/`Socket`, receive loop cancelled on dispose. Commit `feat(room-cameras): UDP VISCA transport`.

### Task RC6: Camera facade and link state

**Files:** Create `Services/Visca/IPtzCamera.cs`, `Services/Visca/ViscaCamera.cs`, `Services/Visca/PtzCameraFactory.cs`, `tests/.../Fakes/FakePtzCamera.cs`; Test `ViscaCameraTests.cs`.
**Produces:**
```csharp
public enum CameraLinkState { Disconnected, Connecting, Connected, Unreachable }
public enum CameraPower { Unknown, On, Off, Fault }
public interface IPtzCamera : IAsyncDisposable {
    CameraLinkState LinkState { get; }  event Action<CameraLinkState> LinkStateChanged;
    Task ConnectAsync(CancellationToken ct);
    Task<bool> MoveAsync(PanTiltDirection d, PtzSpeed s, CancellationToken ct);   // false = not acknowledged
    Task<bool> StopMoveAsync(CancellationToken ct);
    Task<bool> HomeAsync(CancellationToken ct);
    Task<bool> ZoomAsync(ZoomDirection d, PtzSpeed s, CancellationToken ct);
    Task<bool> StopZoomAsync(CancellationToken ct);
    Task<bool> SetZoomPositionAsync(int pos, CancellationToken ct);
    Task<bool> SetFocusAutoAsync(bool auto, CancellationToken ct);
    Task<bool> SetFocusPositionAsync(int pos, CancellationToken ct);
    Task<bool> SetPowerAsync(bool on, CancellationToken ct);
    Task<CameraStatus?> PollAsync(CancellationToken ct);   // power, zoom, focus, af; null = no answer
    void StopImmediately();   // synchronous best-effort Stop datagram, for Closed/Deactivated
}
public readonly record struct PtzSpeed(int Pan, int Tilt, int Zoom);
public interface IPtzCameraFactory { IPtzCamera Create(RoomCameraConfig config); }
```
- [ ] Red tests against `FakeViscaServer` (both transports, `[Theory]`): `Move_ThenStop_ServerSeesDriveThenStop`; `Stop_JumpsTheQueue_WhenMoveIsAwaitingCompletion` (Stop is sent before the Move's completion arrives and Move result is false); `CommandWithoutAck_RetriesTwice_ThenReturnsFalse` for non-stop commands, 3 retries for Stop; `Poll_ReadsPowerZoomFocus`; `Poll_NoAnswer_ReturnsNull_AndSetsUnreachableAfter3`; `ConnectFailure_SetsConnecting_ThenUnreachable_BackoffDoublesTo30s` (clock injected as `Func<DateTime>` plus explicit `Tick`); `ServerDisconnect_MovesToDisconnected_AndReconnects`; `StopImmediately_SendsStopWithoutAwait`; `DisposeTwice_IsSafe`; `Dispose_ReleasesAllSockets`. `FakePtzCamera` records calls and lets a test fail the next Stop.
- [ ] Implement: one `SemaphoreSlim` serialises commands; Stop takes a priority path that bypasses the queue; ack timeout 1 s. Factory picks the transport from `Transport`. Commit `feat(room-cameras): PTZ camera facade with link state machine`.

### Task RC6A: Auto-detect probe

**Files:** Create `Services/Visca/ViscaProbe.cs`; Test `ViscaProbeTests.cs`.
**Consumes:** `IPtzCameraFactory`-free: uses `ViscaCommands.InquirePower`, `ViscaFrame`, the two transports (RC4, RC5).
**Produces:** `public record ProbeHit(ViscaTransportKind Transport, int Port, CameraPower Power)`; `public static class ViscaProbe { public static readonly (ViscaTransportKind, int)[] DefaultCandidates; public static Task<IReadOnlyList<ProbeHit>> DetectAsync(string host, IEnumerable<(ViscaTransportKind, int)>? candidates, TimeSpan perProbeTimeout, CancellationToken ct); }`. Default candidates: Tcp 5678, Tcp 1259, Tcp 3001, Udp 52381, Udp 1259, Udp 3001, plus any port the operator already typed (tried first, on both transports).
- [ ] Red tests against `FakeViscaServer` on random ports with injected candidate lists: `FindsTcpCameraOnCustomPort`; `FindsUdpCameraOnCustomPort`; `NothingListening_ReturnsEmpty_WithinTimeoutBudget` (candidates probed in parallel, at most 4 at once, total under 3 s for 6 candidates at 1 s timeout); `PortThatAcceptsButNeverReplies_IsNotAHit`; `Cancellation_StopsAndDisposesEverySocket` (ports rebindable afterwards); `GarbageReply_IsNotAHit`; `TwoHits_BothReturned_TcpFirst`.
- [ ] Implement with one short-lived transport per candidate, disposed in `finally`. Commit `feat(room-cameras): VISCA port and transport auto-detect`.

### Task RC7: Motion safety controller

**Files:** Create `Services/PtzMotionController.cs`; Test `PtzMotionControllerTests.cs`.
**Consumes:** `IPtzCamera`, `FakePtzCamera`. **Produces:** `PtzMotionController(IPtzCamera camera, Func<DateTime> now)` with `void BeginMove(PanTiltDirection d)`, `void BeginZoom(ZoomDirection d)`, `void End()` (stops whatever is active), `void StopAll()`, `void Tick()` (watchdog and stop retry), `PtzSpeed Speed { get; set; }`, `MotionState State` (`Idle`, `Moving`, `Stopping`, `StopNotConfirmed`), `string MovingText` ("Moving up-left"), `event Action Changed`.
- [ ] Red tests: `Begin_SendsMove_End_SendsStop`; `BeginWhileMoving_SwitchesDirection_NoStopBetween` (one Stop total only at the end); `End_WhenIdle_SendsNothing`; `Watchdog_StopsAfter10s_AndTextSays_Stopped`; `StopFails_RetriesThreeTimes_ThenStopNotConfirmed`; `StopNotConfirmed_ClearsWhenLaterStopSucceeds`; `StopAll_AfterBeginZoomAndMove_StopsBoth`; `Begin_WhenCameraUnreachable_DoesNotEnterMoving` (state stays Idle, text says why); `Dispose_SendsStopOnce`.
- [ ] Implement; commit `feat(room-cameras): PTZ motion safety controller`.

### Task RC8: View models

**Files:** Create `ViewModels/RoomCameraViewModel.cs`, `ViewModels/RoomCamerasViewModel.cs`, `ViewModels/CameraEditorViewModel.cs`; Test `RoomCamerasViewModelTests.cs`, `CameraEditorViewModelTests.cs`.
**Consumes:** `IConfigService`, `IPtzCameraFactory`, `ObservableCollection<SourceCardViewModel>` (the `MainViewModel.Sources` instance), `PtzMotionController`.
**Produces:** `RoomCamerasViewModel(IConfigService, IPtzCameraFactory, ObservableCollection<SourceCardViewModel> sources, Func<DateTime> now)`: `ObservableCollection<RoomCameraViewModel> Cameras`, `Selected`, `AddCommand`, `EditCommand`, `RemoveCommand`, `SelectCommand`, `Tick()`, `StopAll()`, `Dispose()`. `RoomCameraViewModel`: `Name`, `Endpoint` ("192.168.1.163:1259"), `LinkText`, `LinkIsGood`, `PowerText`, `IsLocked`, `Speed` (Slow/Medium/Fast), `ZoomValue`, `FocusValue`, `AutoFocus`, `Motion` (`PtzMotionController`), `PowerCommand`, `ConfirmPowerOffCommand`, `CancelPowerOffCommand`, `IsPowerOffPending` (auto-cancel after 8 s), `Preview` (`CameraPreviewState` per the table plus `SourceName`, `StartPreviewCommand`, `StopPreviewCommand`, `PreviewBitmap`, `CaptionText`), `AutomationName`. Zoom and focus send throttled to 5 per second plus a final value; readback does not fight a slider being dragged (ignored while dragging).
- [ ] Red tests: `Loads_CamerasFromConfig_InOrder`; `Add_Edit_Remove_Persist_ThroughReadModifyWrite` (a theme change made between Load and Save is kept); `Binding_ResolvesByMac_AndFollowsSourcesRebuild` (clear and re-add cards with the same MAC: preview state returns to Live, no new start); `Binding_ToUnknownMac_IsOfflineState_NotException`; `Unbound_ShowsNoSourceBound_ControlStillWorks`; `StartPreview_OnlyWhenStandby_AndDisabledWithoutMulticast`; `TabStartedStream_IsStopped_OnStopAll_ButForeignStream_IsNot`; `Lock_DisablesAllMotionCommands_AndEndsActiveMotion`; `SelectingAnotherCamera_StopsMotionOfThePrevious`; `PowerOff_NeedsConfirm_AutoCancelsAfter8s`; `ZoomSlider_ThrottlesTo5PerSecond_PlusFinal`; `Readback_UpdatesSlider_ExceptWhileDragging`; `LongName_AutomationNameStillHasState`. Editor tests: `Host_Empty_Invalid`; `Port_0_And_65536_Invalid`; `TransportChange_SetsDefaultPort_OnlyIfPortUntouched`; `DuplicateName_Warns_NotBlocks`; `BoundTxList_IsTransmittersOnly_PreviewSupportedFirst`; `Unbound_IsAllowed`.
- [ ] Implement; lead reviews the Sonnet diff review gate here (checkpoint RC-cp1). Commit `feat(room-cameras): room camera view models`.

### Task RC9: Styles and tokens

**Files:** Create `Resources/Styles/RoomCameras.xaml`; Modify `App.xaml` (one merge line after `Composer.xaml`), `tests/.../TestResources.cs` (`Merged`), `ThemeResourceUsageTests.cs` (rows); Test `UiV2RoomCamerasStylesTests.cs`.
**Produces:** tokens `Size.Camera.Rail.Min` 300, `Size.Camera.Rail.Max` 360, `Size.Camera.PadKey` 56, `Size.Camera.Chip.Min` (= touch min); styles `Style.Button.PtzKey` (flat, blue when `IsPressed`, disabled state), `Style.Button.PtzHome`, `Style.Slider.Ptz` (48 px thumb, track from tone steps), `Style.ToggleButton.Segment` (speed), `Style.Chip.Camera` (selectable RadioButton, name plus dot), `Style.Text.CameraStatus`. All brushes `DynamicResource`.
- [ ] Red tests: every key resolves in Dark and Light; `Merged` equals the App.xaml list; no literal hex; keys have both `Pressed` and `Disabled` visual triggers (template search); thumb `MinHeight` equals `Size.Touch.Min`. Implement; commit `feat(room-cameras): camera styles and size tokens`.

### Task RC10: Hold behaviour and pad

**Files:** Create `Views/HoldButton.cs`, `Views/PtzPadView.xaml(.cs)`; Test `HoldButtonTests.cs`, `UiV2PtzPadTests.cs`.
**Produces:** attached properties `HoldButton.PressCommand` / `ReleaseCommand` / `CommandParameter` on any `ButtonBase`; fires Press on `PreviewMouseLeftButtonDown`/`TouchDown`/key `Space`+`Enter` down, and Release exactly once on mouse up, touch up, `LostMouseCapture`, `LostTouchCapture`, `LostKeyboardFocus`, `IsEnabledChanged` to false, `Unloaded`, and `Window.Deactivated`. `PtzPadView` is a 3x3 `UniformGrid` of eight direction keys plus Home (`ui:SymbolIcon` arrows, tooltips, spoken names "Pan left, hold to move"); keys bind `HoldButton` to `Motion.BeginMove`/`End`.
- [ ] Red STA tests: `Press_Release_FiresOnceEach`; `PressThenLoseCapture_FiresRelease`; `PressThenDisable_FiresRelease`; `PressThenUnload_FiresRelease`; `ReleaseWithoutPress_FiresNothing`; `Pad_HasNineKeys_AllWithAutomationNames`; `Keys_AreAtLeast48px` (Size.Touch.Min) at both test sizes. Commit `feat(room-cameras): hold-to-move behaviour and direction pad`.

### Task RC11: Room Cameras view  *(start after the user approves the UI mockup shown in the planning session)*

**Files:** Create `Views/RoomCamerasView.xaml(.cs)`; Test `UiV2RoomCamerasViewTests.cs`, `ViewSmokeTests.RoomCamerasShots.cs`.
**Consumes:** RC8 view models, RC9 styles, RC10 pad. Layout exactly as in "UI layout": chips row (`ItemsControl` + `WrapPanel` at 1920; horizontal `ScrollViewer` at narrow width), Grid `*` / `Auto` (rail min/max tokens), picture area with the five states, caption row, red Stop-not-confirmed banner row, rail (status lines, Power with inline confirm, Lock, pad, speed segment, Zoom, Focus, Auto focus). Code-behind: a `DispatcherTimer` at `VisualRoutingView.TickInterval` while loaded calls `Tick()`; `IsVisibleChanged` to false and `Unloaded` call `StopAll()`; key handling per R14 (key up ends motion; ignored while a `TextBox` has focus).
- [ ] Red STA tests (one `MainWindow` at most): `Renders_AtFullHdAndFloor_NoOverflow_Dark_And_Light` (1920x1080 and 1024x720; picture area width >= 1500 / >= 680; rail within 300-360); `PictureIsNotCoveredByAnyControl` (no sibling overlaps the picture bounds); `EmptyConfig_ShowsAddCameraEmptyState`; `FourPreviewStates_ShowTheirNextAction`; `LongNames_Truncate_WithTooltip` (40+ chars, chip and caption); `HidingTheView_SendsStop` (fake camera sees Stop); `ArrowKeyUp_EndsMotion`; `Locked_DisablesPadSlidersPower`; `DarkLightSwitch_RepaintsRail`; guard tests pass unchanged. Commit `feat(room-cameras): Room Cameras view`.

### Task RC12: Camera editor dialog

**Files:** Create `Views/CameraEditorDialog.xaml(.cs)`; Test `UiV2CameraEditorDialogTests.cs`.
**Consumes:** `CameraEditorViewModel`. WPF-UI controls only (`ui:TextBox`, `ComboBox`, segmented transport choice); fields: name, host, port, transport, bound TX (list shows name and MAC; "None" allowed); a **Detect** button next to the port runs `ViscaProbe.DetectAsync` and fills port and transport from the first hit (several hits are offered as a list; none shows "No camera answered on 192.168.1.163. Check the address, or enter the port by hand."); `Test connection` runs `ConnectAsync` + `PollAsync` on a temporary camera and reports "Connected, camera on" / "No answer on 192.168.1.163:1259 (UDP)" in words, always disposing the temporary camera; Save disabled with the reason beside it; Remove asks an inline confirmation.
- [ ] Red STA tests: `Detect_FillsPortAndTransport_FromFirstHit`; `Detect_NoHit_ShowsWordsAndKeepsManualEntry`; `Detect_Cancelled_OnDialogClose_LeaksNoSocket`; `Save_Disabled_WhenHostInvalid_WithVisibleReason`; `TestConnection_Failure_ShowsWords_AndDisposesCamera` (fake factory counts Create vs Dispose); `Escape_Cancels_NoConfigWritten`; `EmptyTxList_StillSavesUnbound`; both themes, both sizes. Commit `feat(room-cameras): camera editor dialog`.

### Task RC13: Main window integration

**Files:** Modify `MainWindow.xaml` (the two insertions), `MainWindow.xaml.cs`, `Handover/02_CODEBASE_STRUCTURE.md`; Test `RoomCamerasTabTests.cs`.
- [ ] Red tests: `TabOrder_IsVisualRouting_RoomCameras_BavmConsole_Logs` (Grid column order of the RadioButtons); `SelectingRoomCamerasTab_ShowsOnlyThatView`; `LeavingRoomCamerasTab_StopsMotionAndTabStartedPreview`; `Deactivated_CallsStopImmediately`; `Closed_DisposesViewModel_Once`; `ThreeTabsFitAt1024` (app bar still fits); existing `MainWindowHeaderTests` stay green. Implement: `RoomCamerasViewModel` is built after `_viewModel` from `_viewModel.Sources`; `Tab_Checked` gets a branch and the other branches also collapse the new view (set `_leftVisualRouting` as for BAVM so routes are re-read when returning to Visual Routing). Commit `feat(room-cameras): add the Room Cameras tab to the main window`.

### Task RC14: Harness, live checklist, docs

**Files:** Create `docs/superpowers/specs/2026-11-02-room-cameras-live-test.md`; Modify `ViewSmokeTests.RoomCamerasShots.cs`, `Handover/04_VERIFIED_FEATURES_AND_KNOWN_LIMITATIONS.md`.
- [ ] Offscreen screenshots (`UIV2_SHOTS=ui_screenshots/UI-v2-room-cameras`): 1920x1080 and 1024x720, Dark and Light, states: live, standby, no source, moving, Stop not confirmed, locked, empty, editor. Send to the user with `SendUserFile`.
- [ ] Write the **live checklist** (a person with the real camera; not run by agents): which transport answers; confirm every command-table row on the camera; calibrate `ZoomMax`/`FocusMax` and the speed table; unplug the cable mid-move and confirm the camera stops (or report that it cannot, so the 10 s watchdog is the only net); kill the app mid-move; a TX preview that is already live; Refresh in Visual Routing while the tab is open.
- [ ] Full non-live suite, record counts, tag `room-cameras-cp1`. Commit `docs(room-cameras): live test checklist and handover notes`.

## Master tasks

- **M-RC1:** after RC8 is green, merge `ui-v2/room-cameras` into a throwaway integration check (not into `ui-v2/round2`) and run the full suite with `bin/rcm/`; dispatch the Sonnet review (standards + this plan).
- **M-RC2:** after RC14 and the user's screenshot review, ask the user before merging into `ui-v2/round2`. `main` stays untouched.

## Risks

| Risk | Mitigation |
|---|---|
| VISCA bytes or speed/range limits differ on the site camera (the repo cannot test a real camera). | Command table lives in one file with golden tests; RC14 live checklist; ranges and speeds are config/constants. |
| Camera loses network while moving and no packet can stop it. | Watchdog, `StopImmediately`, visible state; true camera-side timeout is camera-dependent and is recorded in the live checklist. |
| UDP replies with unexpected sequence numbers. | Transport delivers by content, not by number; tests pin it. |
| `Sources` rebuilds on every Refresh and stale cards are held. | Bind by MAC on every collection change; a held reference is never stored across a rebuild (RC8 test). |
| Preview auto-start by `MainViewModel` races a tab Start. | Tab Start is disabled while a card is Refreshing or Streaming; stop only if `_startedByTab`. |
| Three tabs plus Logs no longer fit at 1024. | `ThreeTabsFitAt1024` test; fall back to the `Style.Tab.Nav` compact padding if it fails. |
| Config file written by two code paths. | Read-modify-write only (RC1, RC8 tests). |
| The approved mockup changes after review. | RC1-RC8 do not depend on layout; RC9-RC12 start only after approval. |

## Open items

1. **UI mockup approval** (1920x1080 and 1024x720, dark) before RC9 starts.
2. Camera make and model, VISCA address and whether focus Direct works are confirmed on site during RC14; the transport and port come from Detect (R15), so no answer is needed before RC1.

## Self-review notes

- Spec coverage: camera selection (R9, RC8, RC11), PTZ and focus and zoom and power (R4, R8, R11, RC2-RC7), speed sliders as a segmented control (R12), connection and active state (RC6, RC8), edit IP:port (R6, RC12), TX binding and preview (R1, R2, R5, RC8), library decision (study section), mock camera (RC4 fake server, RC6 fake camera), safety (Global Constraints, RC7, RC10, RC13), layouts at both sizes (UI layout, RC11 tests). Presets: deliberately absent (R7).
- Interface names used across tasks match: `IPtzCamera`, `PtzSpeed`, `PanTiltDirection`, `ZoomDirection`, `PtzMotionController`, `CameraLinkState`, `RoomCameraConfig`, `Size.Camera.*`.
- Proportion: the plan carries signatures, test names and assertions, not bodies; byte tables and layout are the only large blocks because the implementer cannot derive them.
