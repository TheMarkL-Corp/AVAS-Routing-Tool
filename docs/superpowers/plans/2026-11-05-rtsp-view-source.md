# Room Cameras RTSP Preview Choice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** On the Room Cameras page, each camera lets the user choose its reference picture while driving PTZ: the live preview from the bound TX (today's behaviour) or the camera's own RTSP stream.

**Architecture:** `RoomCameraConfig` gains a preview mode and an RTSP URL; `RoomCameraViewModel` drives either the existing TX preview path or an `RtspSourceViewModel` (already built: state machine, retry, `IRtspStreamPlayer` seam, LibVLC player). The picture area of the Room Cameras view shows whichever is active. Nothing touches the Main Window, routing, or `MainViewModel.Sources`.

**Tech Stack:** .NET 8 WPF MVVM, WPF-UI 4.3.0, LibVLCSharp 3.9.1 + `VideoLAN.LibVLC.Windows` 3.0.21 (`win-x64`), xUnit STA view tests, the in-repo fake RTSP server.

**Spec:** the **Decisions** section below (the user's answers, 2026-11-05 and 2026-11-07).

## Correction record (read this first)

The first version of this plan (2026-11-05) put the stream on the **Main Window** as a view-only source tile next to the TX tiles ("Main Window first, Room Cameras later"). That was wrong. The user's use case is on the Room Cameras page: when adjusting PTZ, the operator's only visual reference is the live preview from the encoder, and the user wants to choose between that and the camera's RTSP stream. The Main Window tile, its strip edit, its lifecycle hooks and its screenshots were built, then removed in commit `618905c`. See the memory notes `rtsp-source-is-room-cameras-preview` and `ask-purpose-before-building`.

What stays from that work and is reused as is: `Rtsp/RtspUrl.cs`, `Rtsp/IRtspStreamPlayer.cs`, `Rtsp/LibVlcStreamPlayer.cs`, `Rtsp/RtspFrameSink.cs`, `Rtsp/RtspRetryPolicy.cs`, `ViewModels/RtspSourceViewModel.cs`, `tests/.../Fakes/FakeRtspPlayer.cs`, `Fakes/FakeRtspServer.cs` and the H.264 fixture, plus the decoder findings in `docs/superpowers/specs/2026-11-05-rtsp-decoder-findings.md`.

## What was read

- `Configuration/RoomCameraConfig.cs`: `Id, Name, Host, Port, Transport, ViscaAddress, BoundTxMac, ZoomMax, FocusMax`; stored in `AppConfig.RoomCameras` (plain JSON, `appsettings.json`).
- `ViewModels/RoomCameraViewModel.cs`: the picture area comes from `CameraPreviewState { NoSourceBound, SourceOffline, Standby, Starting, Live }`, derived from the bound `SourceCardViewModel`; `StartPreview()` / `StopPreview()` (stop only what this tab started); `PreviewBitmap`, `PreviewMessage`, `CaptionText`, `StartDisabledReason`; `Dispose()`.
- `ViewModels/RoomCamerasViewModel.cs`: builds one `RoomCameraViewModel` per config (`CreateCameraViewModel`), rebuilds it on edit (`ReplaceCamera`), `StopAll()` calls `cam.StopPreview()`, `Dispose()` disposes every camera.
- `Views/RoomCamerasView.xaml.cs`: calls `ViewModel.StopAll()` when the view is hidden, unloaded, when the window is deactivated and when keyboard focus leaves the view, so the TX preview stops in all those cases today. The RTSP preview inherits the same rule.
- `Views/RoomCamerasView.xaml` lines 180-270: the picture area (an `Image` plus one panel per state, a "Start preview" button, a "Stop preview" button).
- `Views/CameraEditorDialog.xaml(.cs)` and `CameraEditorViewModel`: Name, Host/Port (with Detect), Transport radio buttons, a TX `ComboBox` (`TxBox`), a Test button, Save, Remove.
- `tests/.../RoomCamerasPreviewHarness.cs`: gated interactive window with fake VISCA servers, an in-memory `IConfigService`, and a painted test pattern on a TX card.

## Decoder (decided, kept for the record)

LibVLCSharp with `VideoLAN.LibVLC.Windows`, `win-x64` natives only, trimmed plugins, video callbacks into a `WriteableBitmap`. Licence: LGPL-2.1 for both packages (verified). Measured on the user's camera (H.264, 1920x1090): first frame about 0.25 s, 53-58 fps, TCP and UDP both work; folder size 205 MB untrimmed, about 80 MB for `win-x64` with trimmed plugins. Not measured: glass-to-glass latency, H.265. FFmpeg.AutoGen and Media Foundation were compared and rejected (more code, or RTSP compatibility risk). Details: the findings file.

## Decisions (fixed by the user; do not reopen)

- **D-1 (confirmed 2026-11-07)** Use case and screen: on the **Room Cameras page**, each camera has a preview source choice, **TX live preview** or **RTSP stream**, so the operator can use the camera's own picture as the reference while adjusting PTZ. RTSP is not a Main Window source and is never routed.
- **D-2 (2026-11-07)** The RTSP address is typed in each camera's **Add/Edit dialog**, next to the VISCA host. One text box for the whole URL.
- **D-3 (2026-11-07)** The **whole RTSP URL, credentials included, is saved with the camera** in `appsettings.json` (`RoomCameras`). The user accepted plain-text storage. Everywhere else the URL is masked (`RtspUrl.ToMasked()`): UI text, tooltips, automation names, logs, screenshots, test output, commits.
- **D-4 (2026-11-05)** Retry a lost or unreachable stream after 1, 2, 5, then every 10 s, forever. A 401 or "not found" is an Error that stops retrying and shows a Retry button.
- **D-5 (2026-11-05)** Decoder: LibVLCSharp. The final package need not be a single exe.
- **D-6 (default, change on request)** The RTSP preview follows the TX preview's rules: it starts with the "Start preview" button, and it stops when the Room Cameras page is hidden, the window is deactivated or keyboard focus leaves the page (the existing `StopAll` hooks). Starting it automatically is not part of this plan.

## Global Constraints

- WPF-UI controls only; vector icons; touch targets 48 px (36 px compact); font floor 12 px; accent roles: blue = selection/primary, red = errors only, amber = staged only. New XAML must pass `UiV2Guards` (size tokens, no inline styles without a base).
- Status is a dot plus a word, never colour alone.
- **The full RTSP URL appears only in `appsettings.json`, in the editor box while it is being edited, and in memory.** It never appears in `AppLogger`, exception text, captions, tooltips, automation names, harness labels, screenshots, test output or commits. Tests use only the fake server on `127.0.0.1` with fake credentials `user` / `pass-fake`; the real URL appears only in the `AVAS_RTSP_URL` environment variable of an interactive run and is never saved by a harness.
- Existing TX preview behaviour and every existing Room Cameras test stay unchanged: a config without the new fields loads as TX mode.
- PTZ motion never depends on the preview: motion keeps working while the stream is Connecting, Lost or Error.
- Subagents are Sonnet 5.5 for development and Haiku 5.5 only for simple retrieval, never Opus. The lead diff-reviews every task, gives red-then-green evidence (a mutation check where the subagent did not capture a real red), and runs the **full** non-live suite before committing, not only filtered runs.
- Test command: `dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj -p:BaseOutputPath=bin/<lane>/ --filter "FullyQualifiedName!~LiveControlServerTests&FullyQualifiedName!~LiveFunctionTests" --blame-hang-timeout 120s -- RunConfiguration.DisableParallelization=true`. A filter for these tests must be `FullyQualifiedName~Rtsp` or `~RoomCamera`, not a narrower name. A known flaky STA test: rerun once before treating a failure as real.
- No merge to `main`, no push.

## Review Focus

Each line has its test in the owning task.

1. The URL leaks into a log line, caption, tooltip, automation name, exception text or harness label. (RC0, RC1, RC2, RC3)
2. An old `appsettings.json` without `PreviewMode` and `RtspUrl` loads as TX mode, and saving it again keeps TX behaviour. (RC1)
3. Switching a camera from RTSP to TX (or back) in the editor while its preview runs: the old preview is stopped and disposed first. (RC1)
4. RTSP mode with an empty or invalid URL: the picture area says "No RTSP address" or the parser's reason, Start stays disabled, the editor refuses to save an invalid URL. (RC1, RC2)
5. Two cameras in RTSP mode at once: each stream, state and failure classification is its own (a failure on one camera must not show on the other). (RC0, RC1)
6. PTZ buttons, sliders, power and Home keep working while the stream is Lost or Error. (RC1)
7. The window is deactivated or the page is hidden while the stream is Connecting: it stops and a late "Playing" does not revive it. (RC1)

---

## Done earlier and reused (no further work)

- **T0** decoder spike and findings; **T1** `RtspUrl` (parse, `ToFullUrl`, `ToMasked`, `MaskInText`); **T3** `RtspSourceViewModel` (states Stopped/Connecting/Live/Lost/Error, `StartCommand`, `StopCommand`, `RetryCommand`, `PreviewBitmap`, `StatusText`, `StatusDetail`, `IsLive`, `MaskedAddress`, `Dispose`) and `RtspRetryPolicy`; **T4** `FakeRtspServer` (+ fixture) and `LibVlcStreamPlayer` (+ `RtspFrameSink`).
- Known limit to fix in RC0: `LibVlcStreamPlayer` classifies a failure from a decoder log hook that is shared by all players, so it is correct only while one stream is active. The Room Cameras page can have several.

## Tasks

Sizes: S = under half a day, M = about a day, L = 1-2 days.

### RC0: Per-player failure classification (M)

**Files:** Create `src/AvasRoutingTool/Rtsp/RtspProbe.cs`; modify `Rtsp/LibVlcStreamPlayer.cs`; test `tests/AvasRoutingTool.Tests/RtspProbeTests.cs`, modify `LibVlcStreamPlayerTests.cs`.
**Interfaces:** Produces `static Task<RtspFailure> RtspProbe.ClassifyAsync(RtspUrl url, CancellationToken token)`: opens its own TCP connection to the camera and sends `OPTIONS` then `DESCRIBE` (with Basic and Digest credentials from the URL when a 401 asks), returning `AuthFailed` for 401 after credentials, `NotFound` for 404, `Unreachable` for a refused, timed-out or reset connection, `Other` otherwise. It never logs or returns text.

- [ ] **Step 1:** Failing tests against `FakeRtspServer`: `Probe_Require401_WithoutCredentials_AuthFailed`; `Probe_Require401_WithWrongCredentials_AuthFailed`; `Probe_NotFound404_NotFound`; `Probe_RefuseConnection_Unreachable`; `Probe_Normal_ReturnsOther` (the server is fine, so the decoder's own failure stays "Other"); `Probe_Timeout_Unreachable` (a listener that never answers, 2 s limit); `Probe_NeverLogsOrThrowsTheUrl`. Run: red.
- [ ] **Step 2:** Implement `RtspProbe`. In `LibVlcStreamPlayer`, when the media errors or ends before the first frame, call `RtspProbe.ClassifyAsync` with that player's own URL instead of reading the shared log hook; keep the log scrubbing only as a guard (never store a log line). Remove the shared classification flag.
- [ ] **Step 3:** Extend `LibVlcStreamPlayerTests` (gated `AVAS_RTSP_REAL=1`): two players at once, one against `Require401` and one against `Normal`, each reports its own result. Run all Rtsp tests, gated and ungated: green. Commit.

### RC1: Camera config and view model (M)

**Files:** Modify `Configuration/RoomCameraConfig.cs`, `ViewModels/RoomCameraViewModel.cs`, `ViewModels/RoomCamerasViewModel.cs`; test `tests/AvasRoutingTool.Tests/RoomCameraRtspPreviewTests.cs` (and the existing Room Cameras tests must still pass).
**Interfaces:** Produces `enum RoomCameraPreviewMode { Tx, Rtsp }` (JSON as string, default `Tx`); `RoomCameraConfig.PreviewMode` and `string RtspUrl` (default empty); `CameraPreviewState` gains `Reconnecting` and `Failed`; `RoomCameraViewModel` ctor gains optional `Func<IRtspStreamPlayer>? rtspPlayerFactory` and `Func<TimeSpan, CancellationToken, Task>? rtspDelay` (defaults: `new LibVlcStreamPlayer()`, `Task.Delay`; `RoomCamerasViewModel` passes its own pair through for tests); `RetryPreviewCommand`; `PreviewMode` and `IsRtsp` read-only properties.
Behaviour: in `Rtsp` mode `Preview` maps the `RtspSourceViewModel` state (Stopped -> Standby, Connecting -> Starting, Live -> Live, Lost -> Reconnecting, Error -> Failed; no or invalid URL -> NoSourceBound with `PreviewMessage` "No RTSP address"); `StartPreview` creates and starts the `RtspSourceViewModel` from `Config.RtspUrl`; `StopPreview`, `StopAll` and `Dispose` stop and dispose it; `PreviewBitmap` comes from it; `CaptionText` is `"<Name> · via RTSP <masked address>"` with no fps; `StartDisabledReason` is the parser's reason when the URL is invalid. In `Tx` mode nothing changes.

- [ ] **Step 1:** Failing tests with `FakeRtspPlayer` and the fake clock pattern used by the existing Room Cameras tests: `OldConfig_WithoutNewFields_LoadsAsTx` (deserialize JSON lacking both fields); `ConfigJsonRoundTrip_KeepsModeAndUrl`; `Rtsp_StartPreview_GoesStartingThenLive`; `Rtsp_Dropped_ShowsReconnecting_ThenLive`; `Rtsp_AuthFailed_ShowsFailed_RetryRestarts`; `Rtsp_EmptyUrl_NoSourceBound_StartDisabled`; `Rtsp_InvalidUrl_StartDisabledWithReason`; `Rtsp_StopAll_StopsAndLateEventIgnored`; `Rtsp_Dispose_DisposesPlayer`; `Rtsp_CaptionAndMessagesNeverContainCredentialsOrQuery` (URL `rtsp://user:pass-fake@example.test:554/live/1?token=abc`; also assert the `AppLogger` memory buffer holds neither); `Rtsp_PtzControlsWorkWhileLostOrFailed` (MotionEnabled and commands stay available); `SwitchingMode_StopsTheOldPreviewFirst` (TX running then rebuilt as RTSP, and RTSP running then rebuilt as TX, through `UpdateCamera`); `TwoRtspCameras_AreIndependent`. Run: red.
- [ ] **Step 2:** Implement. Reuse `RtspSourceViewModel` unchanged where possible; raise `PropertyChanged` for the preview properties from its events. Run the new tests and every existing `RoomCamera*` test: green.
- [ ] **Step 3:** Run the full non-live suite. Commit.

### RC2: Add/Edit dialog (M)

**Files:** Modify `Views/CameraEditorDialog.xaml(.cs)` and `CameraEditorViewModel` (find it with `grep -rn "class CameraEditorViewModel"`); test `ViewSmokeTests.RoomCameraEditorRtsp.cs`.
**Interfaces:** Consumes RC1's config fields. Produces, in the dialog: radio buttons "TX live preview" / "RTSP stream" (names `PreviewTxChoice`, `PreviewRtspChoice`); in TX mode the existing TX combo is shown, in RTSP mode a 48 px text box `RtspUrlBox` (placeholder `rtsp://...`) and its reason caption replace it; `Result` carries `PreviewMode` and `RtspUrl` (the whole URL, trimmed).

- [ ] **Step 1:** Failing STA tests: default for a new camera is TX; choosing RTSP shows the URL box and hides the TX combo (and the reverse); Save is disabled with the parser's reason for empty, whitespace, `http://x` and `rtsp://` (no host) in RTSP mode, enabled for a valid URL; editing a saved RTSP camera pre-fills the box and the RTSP choice; Save returns a config whose `RtspUrl` equals the typed text and whose `PreviewMode` is `Rtsp`; the dialog's title, status line, tooltips and automation names (everything except the URL box's own text) contain no `pass-fake` and no `abc`; switching to TX keeps the old URL in memory but saves `PreviewMode = Tx`. Red.
- [ ] **Step 2:** Implement (XAML must pass `UiV2Guards`). Green.
- [ ] **Step 3:** Run the full non-live suite. Commit.

### RC3: Room Cameras picture area (S-M)

**Files:** Modify `Views/RoomCamerasView.xaml` (picture area lines ~180-270); test `ViewSmokeTests.RoomCamerasRtspPreview.cs`.
**Interfaces:** Consumes RC1's `CameraPreviewState.Reconnecting` and `Failed`, `PreviewMessage`, `RetryPreviewCommand`. Produces two panels beside the existing ones: Reconnecting (message "Connection lost - retrying in N s", dot plus word) and Failed (the failure sentence and a 48 px "Retry" button); the existing Standby "Start preview" and "Stop preview" buttons serve RTSP unchanged; the caption shows "via RTSP".

- [ ] **Step 1:** Failing STA tests with a view model built over `FakeRtspPlayer`: each of Standby, Starting, Live, Reconnecting, Failed shows its panel and message; Retry is shown only in Failed and runs `RetryPreviewCommand`; the Live picture shows `PreviewBitmap`; the PTZ pad stays enabled in Reconnecting and Failed; no visible text, tooltip or automation name contains the credentials or the query. Red.
- [ ] **Step 2:** Implement. Green.
- [ ] **Step 3:** Run the full non-live suite. Commit.

### RC4: Harness, screenshots and a live check (S)

**Files:** Modify `tests/AvasRoutingTool.Tests/RoomCamerasPreviewHarness.cs`.
**Completion criterion:** the existing gated harness gains a fourth camera "Camera D (RTSP)" in RTSP mode on a `FakeRtspServer` (Normal) with buttons "RTSP: Drop", "RTSP: 401", "RTSP: Normal"; with `AVAS_RTSP_URL` set, Camera D uses that address instead (in memory only, never written, never labelled). Screenshots of the Room Cameras page in both themes at 1920x1080 and 1024x720, camera D in Standby, Live, Reconnecting and Failed (fake server only; `AVAS_RTSP_URL` ignored in shot mode), plus the editor dialog in RTSP mode, go to `ui_screenshots/UI-v2-rtsp-room-cameras/` (git-ignored); the lead opens every PNG and confirms no address text beyond the fake `127.0.0.1` one. A live smoke with the real camera ends with the state word "Live" printed and nothing else.

- [ ] **Step 1:** Extend the harness; smoke it with `AVAS_RC_PREVIEW=1 AVAS_RC_PREVIEW_AUTOCLOSE_SECONDS=5 AVAS_RTSP_REAL=1`.
- [ ] **Step 2:** Shoot, inspect all PNGs, live smoke, full suite, commit.

### RC5: Package only `win-x64` and trim plugins (S)

**Files:** Modify `src/AvasRoutingTool/AvasRoutingTool.csproj`.
**Completion criterion:** `VlcWindowsX86Enabled=false` and `VlcWindowsX64ExcludeFiles` for `libvlc.lib`, `libvlccore.lib`, `hrtfs\**`, `lua\**` and the plugin folders `stream_out, visualization, lua, text_renderer, access_output, services_discovery, meta_engine, spu, audio_filter, mux, video_splitter, audio_mixer`; a `dotnet build` output folder of about 80 MB (was 205 MB) with no `libvlc\win-x86`; the gated real-decoder tests (`AVAS_RTSP_REAL=1`) still pass against the trimmed folder.

- [ ] **Step 1:** Edit the csproj (the package's `build\VideoLAN.LibVLC.Windows.targets` defines both properties), build into a scratch `BaseOutputPath`, check size and the folder list.
- [ ] **Step 2:** Run the gated real-decoder tests and the full suite. Commit.

## Status (2026-11-08)

RC0-RC5 are built and committed on `claude/rtsp-source-352452`; nothing is pushed or merged. RC5 measured: the app build output is 89 MB (was 205 MB) with only `libvlc\win-x64` and 15 plugin groups; the MSBuild wildcards in the exclude list must be written escaped (`%2A%2A`), as the package does. The player also runs with audio off (`--no-audio`, `:no-audio`) because the preview is a picture reference only. Not measured: glass-to-glass latency, H.265. Open default: D-6.

## Order and sizes

RC0 (M) and RC1 (M) are independent and can run in parallel (different files; use different `BaseOutputPath` lanes). RC2 (M) and RC3 (S-M) need RC1 and touch different files. RC4 (S) needs RC1-RC3. RC5 (S) is independent. About 4 working days in all.

## Open questions

- None blocking. D-6 (manual Start, stops on window deactivation like the TX preview) is a default; say if the RTSP preview should instead start by itself and survive deactivation.

## Self-review

Spec coverage: use case and screen (D-1); entry in the dialog (D-2, RC2); whole URL saved with the camera (D-3, RC1, RC2); masking everywhere else (Global Constraints, Review Focus 1, tests in RC0-RC3); states and retry (D-4, RC1, RC3); stop on page hide and close (D-6, RC1); decoder (D-5, RC5); fake RTSP source for tests (reused `FakeRtspServer`, RC0, RC4). Types used across tasks (`RoomCameraPreviewMode`, `CameraPreviewState.Reconnecting/Failed`, `RtspProbe.ClassifyAsync`, `RetryPreviewCommand`) are defined once, in RC0 and RC1.
