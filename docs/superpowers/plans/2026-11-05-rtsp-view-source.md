# RTSP View-Only Source Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** The user enters an RTSP stream address once, and the Main Window shows that stream as a view-only tile next to the TX sources. The tile says plainly that it cannot be routed to an SDVoE display.

**Architecture:** An RTSP source is **not** an `AvasDevice` and never enters `MainViewModel.Sources` (routing, the Composer and arming all key off that list and an `AvasDevice` MAC). It gets its own `RtspSourceViewModel` in a separate `ViewOnlySources` collection, drawn by a new `RtspSourceCardView` after the TX tiles in the full (non-compact) strip. The decoder sits behind `IRtspStreamPlayer` so the state machine, retry policy and UI are tested with a fake player; one gated test drives the real decoder against an in-process fake RTSP server.

**Tech Stack:** .NET 8 WPF MVVM, WPF-UI 4.3.0, xUnit STA view tests, decoder per Decision D-3 (recommended: LibVLCSharp + `VideoLAN.LibVLC.Windows`, frames copied into a `WriteableBitmap` through video callbacks, the same `Image` pattern as `SourceCardViewModel.PreviewBitmap`).

**Spec:** the **Decisions** section below plus the planning request of 2026-11-05. Nothing here is fixed by the user yet except D-1; D-2 to D-4 are open and are listed with a recommendation.

## What was read (facts that shape the plan)

- `src/AvasRoutingTool/ViewModels/SourceCardViewModel.cs`: wraps one `AvasDevice`; owns `PreviewBitmap` (`WriteableBitmap`), `IsStreaming`, `Fps`, `IsArmed`, `UsedInBlocks`, start/stop/refresh preview commands; identity is `MacAddress`. Anything in `MainViewModel.Sources` can be armed and routed, so an RTSP tile must stay out of it.
- `src/AvasRoutingTool/Views/SourceCardView.xaml`: full 4-row tile (header with status dot, name, rename pencil, info badge; 16:9 thumbnail `Size.Strip.Thumb.Height` inside a 240 px tile `Size.Strip.Tile.Width`; footer) and a compact Composer layout. The new tile reuses its styles (`Style.Card.Source`, `Style.Text.Title`, `Style.StatusDot.Small`).
- `src/AvasRoutingTool/Views/VisualRoutingView.xaml` lines 78-110: two `ItemsControl`s bind `Sources` (compact palette when a Composer is open, full strip otherwise). The RTSP tiles go after the second one only.
- Room Cameras: `RoomCameraViewModel` re-resolves a bound `SourceCardViewModel` and shows its `PreviewBitmap` through the `CameraPreviewState` enum (`NoSourceBound, SourceOffline, Standby, Starting, Live`); `RoomCamerasViewModel.Dispose()` stops previews; `RoomCamerasView.xaml.cs` stops on `Unloaded`. The harness `tests/AvasRoutingTool.Tests/RoomCamerasPreviewHarness.cs` is a gated (`AVAS_RC_PREVIEW=1`) interactive window with fake VISCA servers, an in-memory `IConfigService` and a painted test pattern on a card. The RTSP harness copies that shape.
- `src/AvasRoutingTool/Configuration/AppConfig.cs`: flat properties plus `List<RoomCameraConfig> RoomCameras`; `ShowLogs`/`ShowAdvanced` are the existing switch pattern. Config is plain JSON (`appsettings.json`), so a stored URL would be readable.
- `AppLogger` (`Logging/`) is a static logger with an in-memory buffer shown on the Logs tab and a file under `Logs`. Every message goes there, so masking must happen before the call.
- The repo has no `PublishSingleFile`/`SelfContained` setting in any `.csproj`, `.pubxml` or script (searched `*.csproj`, `*.pubxml`, `*.ps1`, `*.props`); the single-file packaging step lives outside the repo. Task 0 must be run with the real packaging command.

## Decoder comparison (D-3)

Verified = found on a package page or vendor doc during this planning turn; **unverified** = not confirmed, Task 0 must measure it.

| | LibVLCSharp + VideoLAN.LibVLC.Windows | FFmpeg.AutoGen + FFmpeg DLLs | Media Foundation (Windows built-in) |
|---|---|---|---|
| Licence | LibVLCSharp LGPL-2.1 (also sold under a commercial licence); `VideoLAN.LibVLC.Windows` nuspec LGPL-2.1-or-later. Verified. Commercial use is allowed if the DLLs stay replaceable (ship them as separate files, credit, offer LGPL text). | Bindings are MIT from 9.x (earlier versions LGPL). Verified. FFmpeg binaries keep their own licence: LGPL builds are fine, GPL builds (some forks bundle those) are not. Verified. | Part of Windows, no licence cost. |
| Native DLLs | Separate package of natives plus a `plugins` folder. Verified that natives are separate from the wrapper. Single-file behaviour **unverified**: native DLLs cannot run from inside the bundle, so either `IncludeNativeLibrariesForSelfExtract` (extracts to a temp folder on first start) or ship `libvlc` beside the exe. | Bindings do not include FFmpeg; you point at a DLL folder (or take a bundled LGPL clone such as `FFmpegDotNet._AutoGen`). Verified. Same single-file question as VLC. | None. |
| Added size | **Unverified** (package pages gave download counts only). Expect tens of MB compressed and well over 100 MB extracted with all plugins; Task 0 measures it and a trimmed `plugins` set. | **Unverified.** LGPL shared builds are usually smaller than VLC. | 0. |
| RTSP | Built in (not re-checked on a package page, but it is VLC's standard input). Our own reconnect is needed regardless. | Built in to libavformat; you write the demux, decode, colour-convert and display loop yourself. | Microsoft lists RTSP over UDP/TCP as supported. Verified. Forum reports show real servers rejected with "not a compatible streaming media server". Verified as reports, cause unknown. |
| H.264 / H.265 | **Unverified** in the packages; VLC decodes both. | Both, given the build. | H.264 is standard; H.265 needs an installed extension. **Unverified.** |
| Latency | Tunable with `:network-caching=` and `:rtsp-tcp`; **unverified** how low it goes; Task 0 measures. | Lowest, you control buffering. | Not controllable enough to promise. **Unverified.** |
| Reconnect | None promised; the app does it (Task 3). | None; the app does it. | None; the app does it. |
| WPF display | `VideoView` is an HWND (airspace problems with overlays and the tile's rounded corners); video callbacks into a `WriteableBitmap` avoid that at the cost of a CPU copy. | Always a copy into a `WriteableBitmap`. | Needs interop; no ready WPF control for RTSP. |
| Effort | Low: it is a player. | High: roughly 400+ lines of unsafe interop plus threading. | Medium, with the highest compatibility risk. |

**Recommendation:** LibVLCSharp with `VideoLAN.LibVLC.Windows` and video callbacks into a `WriteableBitmap`. It is the only option that gives RTSP, H.264 and H.265 and a permissive licence for little code. Its cost is size and the native-DLL layout, so Task 0 is a mandatory spike that gates everything else; if the size or the single-file cost is unacceptable, the fallback is FFmpeg with an LGPL shared build (more code, smaller, same DLL layout issue). Media Foundation is not recommended: compatibility with arbitrary IP-camera RTSP servers is the risk this feature cannot afford.

## Decisions

- **D-1 (fixed by the user)** The stream is view-only. It cannot be routed to an SDVoE display, and the tile says so. The URL is given in chat and never written to files, logs, commits or screenshots.
- **D-2 (open, recommended)** Entry: an "Add RTSP stream" tile at the end of the source strip opens a dialog with **Name**, **Address** (`rtsp://host:port/path`), **User** and **Password** (a `PasswordBox`). Pasting a full URL with credentials into Address splits it into the fields, and the Address box then shows it without credentials. Edit and Remove sit on the tile's menu. Several streams are allowed (one list), but the first release is tested with one.
- **D-3 (open, recommended)** Decoder: LibVLCSharp, subject to the Task 0 numbers. See the comparison above.
- **D-4 (open, recommended)** Storage: `AppConfig.RtspSources` (`List<RtspSourceConfig>`), where the whole URL including credentials is stored only as a Windows DPAPI blob (`ProtectedData`, `CurrentUser`) in `UrlProtected`; `Id`, `Name` and the masked display text are stored in clear. A blob made on one PC or user cannot be read on another, so the portable exe on a new machine shows the tile as "Re-enter password". Alternative: do not persist, ask each session.
- **D-5 (open, recommended)** Retry: automatic retry with delays 1, 2, 5, 10 s then every 10 s, forever, only for a lost or unreachable stream; an authentication or "not found" answer is an Error that stops retrying and offers a Retry button. A Stop button halts everything.
- **D-6 (open, recommended)** First place is the Main Window strip. The Room Cameras page can bind a camera to an RTSP source later (it would need `CameraPreviewState` to accept a non-`SourceCardViewModel` source); out of scope here.

## Global Constraints

- WPF-UI controls only; vector icons; touch targets 48 px (36 px compact); font floor 12 px; accent roles: blue = selection/primary, red = errors only, amber = staged only.
- Status is a dot plus a word, never colour alone.
- **No URL, host, user, password or query string is written to any file, log, exception text, commit, test output or screenshot.** Everything that prints a stream goes through `RtspUrl.Mask`. Tests use only the fake server on `127.0.0.1` with fake credentials `user`/`pass-fake`; the real URL appears only in the `AVAS_RTSP_URL` environment variable of an interactive run.
- The tile never offers arm, drag, route or "use in composer", and its always-visible badge reads "View only - cannot be routed to a display".
- A stream is stopped when its tab is left, when its window closes, and when it is removed; no decoder thread or native handle outlives that.
- Subagents are Sonnet 5.5 for development and Haiku 5.5 only for simple retrieval, never Opus. The lead diff-reviews every task and records red-then-green evidence.
- Test command: `dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj -p:BaseOutputPath=bin/<lane>/ --filter "FullyQualifiedName!~LiveControlServerTests&FullyQualifiedName!~LiveFunctionTests" --blame-hang-timeout 120s -- RunConfiguration.DisableParallelization=true`. A known flaky STA test: rerun once before treating a failure as real.
- No merge to `main`, no push.

## Review Focus

Each line has its test in the owning task.

1. Credentials in the URL leak through a log line, an exception message, the tooltip, the tile title, a UI automation name, or the decoder's own log callback. (T1, T3, T4, T5)
2. A URL with an `@` or `:` in the password, IPv6 host, no port, a query token (`?token=`), or whitespace pasted around it. (T1)
3. The stream drops mid-playback, the server is unreachable at start, the server answers 401, and the server comes back after being down. (T3, T4)
4. The user leaves the Visual Routing tab or closes the window while the state is Connecting (a connect call is in flight). (T6)
5. A frame size change mid-stream (the camera switches resolution) must not crash the bitmap copy. (T4)
6. The stored blob cannot be decrypted (other user or PC): the tile asks to re-enter, never crashes, never shows garbage. (T2)
7. An RTSP tile must never be armed, routed or counted in `OnlineSourcesCount`. (T5)

---

## Tasks

Sizes: S = under half a day, M = about a day, L = 1-2 days.

### T0: Decoder spike and numbers (M, gates T4 and T5)

**Files:** Create `docs/superpowers/specs/2026-11-05-rtsp-decoder-findings.md`; a throwaway console project under the scratchpad, not in the repo.
**Completion criterion:** the findings file holds, for LibVLC and (if LibVLC fails a limit below) FFmpeg: published exe size with and without the decoder, first-start time on a clean user profile, whether it runs when copied to another folder and another PC, glass-to-glass latency, H.264 and H.265 pass/fail, and the decision for D-3. No stream address appears in it (write "the user's camera").

- [ ] **Step 1:** Run the repo's real single-file packaging command (ask the lead for it; it is not in the repo) with LibVLCSharp added to a copy of the project; record size and whether `libvlc.dll` and `plugins` land beside the exe or in a temp extraction folder.
- [ ] **Step 2:** With `AVAS_RTSP_URL` set in the shell only, play the stream with `:network-caching=150 :rtsp-tcp`; measure latency against a running clock held in front of the camera; try `:rtsp-tcp` off.
- [ ] **Step 3:** Trim the `plugins` folder to the needed plugins and re-measure size and playback.
- [ ] **Step 4:** Write the findings file, commit, and stop for the user's D-3 answer.

### T1: `RtspUrl` parse and mask (S)

**Files:** Create `src/AvasRoutingTool/Rtsp/RtspUrl.cs` (namespace `AvasRoutingTool.Rtsp`; the existing `Rtp` folder is for SDVoE RTP, keep them apart); test `tests/AvasRoutingTool.Tests/RtspUrlTests.cs`.
**Interfaces:** Produces `sealed record RtspUrl(string Host, int Port, string PathAndQuery, string? User, string? Password)` with `static bool TryParse(string text, out RtspUrl url, out string reason)`, `string ToFullUrl()` (for the decoder only), `string ToMasked()`, and `static string MaskInText(string text)` which replaces any `rtsp(s)://...` found in free text with the masked form.

- [ ] **Step 1:** Write failing tests: `Parse_SplitsUserAndPassword`; `Parse_PasswordWithAtAndColon_PercentEncoded`; `Parse_NoPort_Uses554`; `Parse_Ipv6Host`; `Parse_TrimsWhitespace`; `Parse_RejectsHttp_WithReason`; `Mask_HidesUserPasswordAndQueryValues` asserting `ToMasked()` equals `rtsp://***:***@host:554/path?token=***` for a fake `user`/`pass-fake`/`token=abc` input and contains neither `pass-fake` nor `abc`; `MaskInText_ReplacesUrlInsideAnExceptionMessage`. Run: red.
- [ ] **Step 2:** Implement with `System.Uri` plus manual user-info handling. Run: green.
- [ ] **Step 3:** Commit.

### T2: Config and DPAPI store (S)

**Files:** Modify `Configuration/AppConfig.cs` (add `List<RtspSourceConfig> RtspSources`); create `Configuration/RtspSourceConfig.cs`, `Rtsp/RtspUrlProtector.cs`; test `RtspUrlProtectorTests.cs`.
**Interfaces:** Produces `RtspSourceConfig { string Id; string Name; string MaskedDisplay; string? UrlProtected; }`; `static string Protect(RtspUrl url)`; `static bool TryUnprotect(string blob, out RtspUrl url)`.

- [ ] **Step 1:** Failing tests: `RoundTrip_ReturnsSameUrl`; `Blob_DoesNotContainPasswordOrHostInClear` (decode base64, search bytes); `CorruptBlob_ReturnsFalse_NoThrow`; `Config_JsonRoundTrip_KeepsRtspSources` using a temp `ConfigService` file, then read the file text and assert it holds no `pass-fake`. Red.
- [ ] **Step 2:** Implement with `System.Security.Cryptography.ProtectedData` (add the `System.Security.Cryptography.ProtectedData` package reference if the SDK does not provide it for `net8.0-windows`). Green. Commit.

### T3: `RtspSourceViewModel` state machine (M)

**Files:** Create `ViewModels/RtspSourceViewModel.cs`, `Rtsp/IRtspStreamPlayer.cs`, `Rtsp/RtspRetryPolicy.cs`; test `RtspSourceViewModelTests.cs` with `Fakes/FakeRtspPlayer.cs`.
**Interfaces:** Produces
`enum RtspTileState { Stopped, Connecting, Live, Lost, Error }`;
`interface IRtspStreamPlayer : IDisposable { event Action<RtspPlayerEvent> Event; WriteableBitmap? Bitmap { get; } void Start(string fullUrl); void Stop(); }` with `RtspPlayerEvent` = `Playing | Dropped | Failed(RtspFailure kind: AuthFailed, NotFound, Unreachable, Other)`; `RtspRetryPolicy.NextDelay(int attempt) -> TimeSpan` (1, 2, 5, 10 s, then 10 s); `RtspSourceViewModel(RtspSourceConfig config, RtspUrl url, Func<IRtspStreamPlayer> playerFactory, Func<TimeSpan, CancellationToken, Task> delay)` with `State`, `StatusText`, `DisplayName`, `MaskedAddress`, `PreviewBitmap`, `StartCommand`, `StopCommand`, `RetryCommand`, `Dispose()`.
Status texts: Connecting "Connecting...", Live "Live", Lost "Connection lost - retrying in N s", Error "Cannot open stream: <kind sentence>"; the kind sentences never contain the URL.

- [ ] **Step 1:** Failing tests against the fake player and an instant `delay`: `Start_GoesConnecting_ThenLiveOnPlaying`; `Dropped_GoesLost_AndRetriesWithPolicyDelays` (assert delays 1, 2, 5, 10, 10); `RetrySucceeds_ReturnsToLive_ResetsAttemptCounter`; `AuthFailed_GoesError_NoRetry`; `Unreachable_AtStart_RetriesAutomatically`; `Stop_CancelsPendingRetry_AndStopsPlayer`; `Dispose_StopsPlayer_NoFurtherEvents`; `StatusText_NeverContainsPasswordOrHost` over every state and failure kind; `PlayerFailureMessage_WithUrl_IsMasked`. Run: red.
- [ ] **Step 2:** Implement; events marshal to the dispatcher; the retry loop is one cancellable task. Run: green. Commit.

### T4: Fake RTSP server and the LibVLC adapter (L)

**Files:** Create `tests/AvasRoutingTool.Tests/Fakes/FakeRtspServer.cs`, `tests/AvasRoutingTool.Tests/Fixtures/rtsp-testpattern.h264` (a few seconds of a synthetic pattern, Annex-B, under 200 KB, generated once with ffmpeg `testsrc`; contains no camera content), `src/AvasRoutingTool/Rtsp/LibVlcStreamPlayer.cs` (implements `IRtspStreamPlayer`); tests `FakeRtspServerTests.cs`, `LibVlcStreamPlayerTests.cs`.
**Interfaces:** Produces `FakeRtspServer(FakeRtspMode mode)` with `int Port`, `Drop()` (close all connections), `Dispose()`, modes `Normal | Require401 | NotFound404 | RefuseConnection`; speaks `OPTIONS`, `DESCRIBE` (SDP for H.264), `SETUP` and `PLAY` over TCP-interleaved RTP, sending the fixture as single-NAL and FU-A packets in a loop at 25 fps, with Basic or Digest auth for `user`/`pass-fake`. Consumes `IRtspStreamPlayer`, `RtspUrl.MaskInText`.

- [ ] **Step 1:** Failing server tests with a raw TCP client: DESCRIBE returns an SDP containing `H264`; `Require401` answers 401 then 200 with credentials; `Drop` closes the socket. Red, implement, green.
- [ ] **Step 2:** Failing adapter tests (gated by `AVAS_RTSP_REAL=1` like the harness because they need the native decoder): `Plays_FakeServer_RaisesPlaying_AndBitmapChanges`; `ServerDrop_RaisesDropped`; `Auth401_RaisesFailedAuth`; `Refused_RaisesFailedUnreachable`; `ResolutionChange_DoesNotThrow` (fake server switches to a second fixture size); `DecoderLogCallback_IsFilteredThroughMaskInText` using a capturing `AppLogger` listener. Red.
- [ ] **Step 3:** Implement `LibVlcStreamPlayer` per D-3: one shared `LibVLC` instance created lazily with `--no-video-title-show` and **no verbose logging**; the log callback, if used for failure classification, passes every line through `RtspUrl.MaskInText` before `AppLogger`. Video callbacks (format, lock, unlock, display) copy into a `WriteableBitmap` recreated on size change; media option `:rtsp-tcp` and `:network-caching` from T0. Map LibVLC's error and end-reached events to `RtspPlayerEvent`. Green.
- [ ] **Step 4:** Commit.

### T5: Tile, add/edit dialog, strip placement (M)

**Files:** Create `Views/RtspSourceCardView.xaml(.cs)`, `Views/RtspSourceDialog.xaml(.cs)`; modify `ViewModels/MainViewModel.cs` (add `ObservableCollection<RtspSourceViewModel> ViewOnlySources`, `AddRtspSourceCommand`, load from `AppConfig.RtspSources` at startup, save on add/edit/remove), `Views/VisualRoutingView.xaml` (after line 110's `ItemsControl`, a second `ItemsControl` over `ViewOnlySources` plus the "Add RTSP stream" tile; none in the compact palette); test `ViewSmokeTests.RtspSource.cs`.
**Interfaces:** Consumes T1-T3. The tile shows the name, status dot plus word (D-5 texts), the picture (`PreviewBitmap`, or the state message in the thumbnail area when not Live), the badge "View only - cannot be routed to a display", and a menu with Edit, Remove and Stop/Start/Retry. Same size as a TX tile.

- [ ] **Step 1:** Failing STA tests: tile renders each of the five states with its word; badge text present and in `AutomationProperties.HelpText`; tile has no arm/drag handlers (pressing it leaves `Sources.Any(s => s.IsArmed)` false and `MainViewModel` route methods uncalled); `OnlineSourcesCount` unchanged by an added RTSP source; dialog `OK` disabled with a reason until Address parses (reasons from `RtspUrl.TryParse`) and Name is 1-19 characters; pasting a full URL splits user, password and address and the Address box then shows no credentials; the tile's name, tooltip and automation name contain no credentials and no query values; Remove stops the player and deletes the config entry. Red.
- [ ] **Step 2:** Implement view, dialog and wiring. Green.
- [ ] **Step 3:** Run the full suite. Commit.

### T6: Lifecycle: stop on tab switch and window close (S)

**Files:** Modify `ViewModels/MainViewModel.cs` (`StopAllRtsp()`, called from the tab-change handler and the window `Closing`/`Dispose` path), `Views/VisualRoutingView.xaml.cs` (stop on `Unloaded`/`IsVisibleChanged` false; start again only streams the user had running when the tab returns); test `RtspLifecycleTests.cs`.

- [ ] **Step 1:** Failing tests with the fake player: `LeavingVisualRoutingTab_StopsPlayers`; `ReturningToTab_RestartsOnlyStreamsThatWereRunning`; `WindowClose_DisposesPlayers`; `TabSwitchWhileConnecting_CancelsConnect_NoLateEventRevivesTile` (the fake raises `Playing` after Stop; state must stay Stopped); `ShowLogs`/`ShowAdvanced` toggles do not restart anything. Red.
- [ ] **Step 2:** Implement. Green. Commit.

### T7: Preview harness and screenshots (S)

**Files:** Create `tests/AvasRoutingTool.Tests/RtspSourcePreviewHarness.cs`.
**Completion criterion:** gated by `AVAS_RTSP_PREVIEW=1` like `RoomCamerasPreviewHarness`; opens the Main Window strip with TX cards, one tile on a `FakeRtspServer` (Normal), one on `Require401`, one on `RefuseConnection`, with a "Drop connection" button on the first to show Lost then Live; optional `AVAS_RTSP_URL` adds a real tile for an interactive run only. Screenshots (both themes, 1920x1080 and 1024x720, tiles in Live with the fixture pattern, Lost, Error) go to `ui_screenshots/UI-v2-rtsp/`; they use the fake server only, never the real URL, and are checked by eye for any address text before commit.

- [ ] **Step 1:** Write the harness; smoke it with `AVAS_RTSP_PREVIEW_AUTOCLOSE_SECONDS=5`.
- [ ] **Step 2:** Shoot, inspect, run the full suite, commit.

## Order and sizes

T0 (M) first, then the user's D-3 answer. T1, T2 (S each) are independent and can run in parallel. T3 (M) needs T1. T4 (L) needs T0, T3. T5 (M) needs T2, T3. T6 (S) needs T5. T7 (S) last. Total about 5 to 6 working days.

## Self-review

Spec coverage: entry and storage (D-2, D-4, T2, T5); masking in UI and logs (T1, T3, T4, T5); tile states (T3, T5); stop on tab switch and close (T6); fake RTSP source (T4); decoder decision (comparison, D-3, T0). Types used across tasks (`RtspUrl`, `RtspSourceConfig`, `IRtspStreamPlayer`, `RtspTileState`, `RtspPlayerEvent`) are defined once in T1-T3.
