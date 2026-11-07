# RTSP View-Only Source Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** The user pastes one RTSP URL into one text box, and the Main Window shows that stream as a view-only tile next to the TX sources. The tile says plainly that it cannot be routed to an SDVoE display.

**Architecture:** An RTSP source is **not** an `AvasDevice` and never enters `MainViewModel.Sources` (routing, the Composer and arming all key off that list and an `AvasDevice` MAC). It gets its own `RtspSourceViewModel` in a separate `ViewOnlySources` collection, drawn by a new `RtspSourceCardView` after the TX tiles in the full (non-compact) strip. The decoder sits behind `IRtspStreamPlayer` so the state machine, retry policy and UI are tested with a fake player; one gated test drives the real decoder against an in-process fake RTSP server.

**Tech Stack:** .NET 8 WPF MVVM, WPF-UI 4.3.0, xUnit STA view tests, LibVLCSharp + `VideoLAN.LibVLC.Windows` (D-3, frames copied into a `WriteableBitmap` through video callbacks, the same `Image` pattern as `SourceCardViewModel.PreviewBitmap`).

**Spec:** the **Decisions** section below plus the planning request of 2026-11-05. D-1 to D-6 are fixed by the user.

## What was read (facts that shape the plan)

- `src/AvasRoutingTool/ViewModels/SourceCardViewModel.cs`: wraps one `AvasDevice`; owns `PreviewBitmap` (`WriteableBitmap`), `IsStreaming`, `Fps`, `IsArmed`, `UsedInBlocks`, start/stop/refresh preview commands; identity is `MacAddress`. Anything in `MainViewModel.Sources` can be armed and routed, so an RTSP tile must stay out of it.
- `src/AvasRoutingTool/Views/SourceCardView.xaml`: full 4-row tile (header with status dot, name, rename pencil, info badge; 16:9 thumbnail `Size.Strip.Thumb.Height` inside a 240 px tile `Size.Strip.Tile.Width`; footer) and a compact Composer layout. The new tile reuses its styles (`Style.Card.Source`, `Style.Text.Title`, `Style.StatusDot.Small`).
- `src/AvasRoutingTool/Views/VisualRoutingView.xaml` lines 78-110: two `ItemsControl`s bind `Sources` (compact palette when a Composer is open, full strip otherwise). The RTSP tiles go after the second one only.
- Room Cameras: `RoomCameraViewModel` re-resolves a bound `SourceCardViewModel` and shows its `PreviewBitmap` through the `CameraPreviewState` enum (`NoSourceBound, SourceOffline, Standby, Starting, Live`); `RoomCamerasViewModel.Dispose()` stops previews; `RoomCamerasView.xaml.cs` stops on `Unloaded`. The harness `tests/AvasRoutingTool.Tests/RoomCamerasPreviewHarness.cs` is a gated (`AVAS_RC_PREVIEW=1`) interactive window with fake VISCA servers, an in-memory `IConfigService` and a painted test pattern on a card. The RTSP harness copies that shape.
- `src/AvasRoutingTool/Configuration/AppConfig.cs`: flat properties plus `List<RoomCameraConfig> RoomCameras`; `ShowLogs`/`ShowAdvanced` are the existing switch pattern. Config is plain JSON (`appsettings.json`), so a stored URL would be readable.
- `AppLogger` (`Logging/`) is a static logger with an in-memory buffer shown on the Logs tab and a file under `Logs`. Every message goes there, so masking must happen before the call.
- The repo has no `PublishSingleFile`/`SelfContained` setting in any `.csproj`, `.pubxml` or script (searched `*.csproj`, `*.pubxml`, `*.ps1`, `*.props`); packaging lives outside the repo. The user confirmed (2026-11-05) that the final package need not be one exe, so `libvlc` ships in a folder beside it.

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

**Recommendation (accepted by the user as D-3):** LibVLCSharp with `VideoLAN.LibVLC.Windows` and video callbacks into a `WriteableBitmap`. It is the only option that gives RTSP, H.264 and H.265 and a permissive licence for little code. Its cost is size and the native-DLL layout, so Task 0 is a short spike; because the package may be a folder, the single-file cost no longer matters. The fallback, if a measured number is unacceptable, is FFmpeg with an LGPL shared build. Media Foundation is not recommended: compatibility with arbitrary IP-camera RTSP servers is the risk this feature cannot afford.

## Decisions

- **D-1 (fixed by the user)** The stream is view-only. It cannot be routed to an SDVoE display, and the tile says so. The URL is given in chat and never written to files, logs, commits or screenshots.
- **D-2 (fixed by the user, 2026-11-05)** One single text box takes the whole URL in one go (credentials included). One stream only in this phase: no list, no Name field, no User/Password fields. The tile is named "RTSP stream" and shows the masked address.
- **D-3 (fixed by the user, 2026-11-05)** Decoder: LibVLCSharp with `VideoLAN.LibVLC.Windows`. The final package does **not** have to be one exe: `libvlc.dll` and the `plugins` folder ship beside the exe. T0 still measures size, latency and H.265, and may veto D-3 only if a number is unacceptable.
- **D-4 (fixed by the user, 2026-11-05: "keep it simple")** No encryption and no config file. The URL lives in memory for the session and is pasted again after a restart. This keeps the rule "the URL is never written to a file" true. Persistence is deferred (see the end of this plan).
- **D-5 (fixed by the user, 2026-11-05)** Retry a lost or unreachable stream after 1, 2, 5, then every 10 s, forever. A 401 or "not found" is an Error that stops retrying and shows a Retry button. A Stop button halts everything.
- **D-6 (fixed by the user, 2026-11-05)** First place is the Main Window strip. Room Cameras later.

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
6. Empty box, whitespace-only text, a non-rtsp URL, and a second paste replacing a running stream: the old stream stops first. (T1, T5)
7. An RTSP tile must never be armed, routed or counted in `OnlineSourcesCount`. (T5)

---

## Tasks

Sizes: S = under half a day, M = about a day, L = 1-2 days.

### T0: Decoder spike and numbers (S, gates T4)

**Files:** Create `docs/superpowers/specs/2026-11-05-rtsp-decoder-findings.md`; a throwaway console project under the scratchpad, not in the repo.
**Completion criterion:** the findings file holds the app folder size with and without LibVLC (full and trimmed `plugins`), glass-to-glass latency with the user's camera, H.264 and H.265 pass/fail, and whether a `win-x64` publish with `libvlc` beside the exe starts on a clean user profile. No stream address appears in it (write "the user's camera").

- [ ] **Step 1:** With `AVAS_RTSP_URL` set in the shell only, play the stream with `:network-caching=150 :rtsp-tcp`; measure latency against a running clock held in front of the camera; try `:rtsp-tcp` off.
- [ ] **Step 2:** Trim the `plugins` folder to the needed plugins and re-measure size and playback.
- [ ] **Step 3:** Write the findings file, commit, report the numbers.

### T1: `RtspUrl` parse and mask (S)

**Files:** Create `src/AvasRoutingTool/Rtsp/RtspUrl.cs` (namespace `AvasRoutingTool.Rtsp`; the existing `Rtp` folder is for SDVoE RTP, keep them apart); test `tests/AvasRoutingTool.Tests/RtspUrlTests.cs`.
**Interfaces:** Produces `sealed record RtspUrl(string Host, int Port, string PathAndQuery, string? User, string? Password)` with `static bool TryParse(string text, out RtspUrl url, out string reason)`, `string ToFullUrl()` (for the decoder only), `string ToMasked()`, and `static string MaskInText(string text)` which replaces any `rtsp(s)://...` found in free text with the masked form.

- [ ] **Step 1:** Write failing tests: `Parse_SplitsUserAndPassword`; `Parse_PasswordWithAtAndColon`; `Parse_NoPort_Uses554`; `Parse_Ipv6Host`; `Parse_TrimsWhitespace`; `Parse_Empty_ReturnsReason`; `Parse_RejectsHttp_WithReason`; `Mask_HidesUserPasswordAndQueryValues` asserting `ToMasked()` equals `rtsp://***:***@host:554/path?token=***` for a fake `user`/`pass-fake`/`token=abc` input and contains neither `pass-fake` nor `abc`; `MaskInText_ReplacesUrlInsideAnExceptionMessage`. Run: red.
- [ ] **Step 2:** Implement with `System.Uri` plus manual user-info handling. Run: green.
- [ ] **Step 3:** Commit.

### T3: `RtspSourceViewModel` state machine (M)

**Files:** Create `ViewModels/RtspSourceViewModel.cs`, `Rtsp/IRtspStreamPlayer.cs`, `Rtsp/RtspRetryPolicy.cs`; test `RtspSourceViewModelTests.cs` with `Fakes/FakeRtspPlayer.cs`.
**Interfaces:** Produces
`enum RtspTileState { Stopped, Connecting, Live, Lost, Error }`;
`interface IRtspStreamPlayer : IDisposable { event Action<RtspPlayerEvent> Event; WriteableBitmap? Bitmap { get; } void Start(string fullUrl); void Stop(); }` with `RtspPlayerEvent` = `Playing | Dropped | Failed(RtspFailure kind: AuthFailed, NotFound, Unreachable, Other)`; `RtspRetryPolicy.NextDelay(int attempt) -> TimeSpan` (1, 2, 5, 10 s, then 10 s); `RtspSourceViewModel(RtspUrl url, Func<IRtspStreamPlayer> playerFactory, Func<TimeSpan, CancellationToken, Task> delay)` with `State`, `StatusText`, `MaskedAddress`, `PreviewBitmap`, `StartCommand`, `StopCommand`, `RetryCommand`, `Dispose()`.
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

### T5: Tile, URL box, strip placement (M)

**Files:** Create `Views/RtspSourceCardView.xaml(.cs)`; modify `ViewModels/MainViewModel.cs` (add `RtspSourceViewModel? ViewOnlySource`, `SetRtspUrl(string text)` and `RemoveRtsp()`), `Views/VisualRoutingView.xaml` (after line 110's `ItemsControl`, one RTSP tile; none in the compact palette); test `ViewSmokeTests.RtspSource.cs`.
**Interfaces:** Consumes T1, T3. With no stream set, the tile is an empty "Add RTSP stream" tile holding one text box (48 px, placeholder "rtsp://...") and a Start button; Enter also starts. Once set, the box is replaced by the live tile: masked address, status dot plus word (D-5 texts), the picture, the badge "View only - cannot be routed to a display", and a menu with Stop/Start/Retry and Remove (Remove clears the stream and returns to the empty tile). Same size as a TX tile.

- [ ] **Step 1:** Failing STA tests: empty tile shows the box; Start stays disabled with the parser's reason until the text parses; the tile renders each of the five states with its word; badge text is present and in `AutomationProperties.HelpText`; pressing the tile leaves `Sources.Any(s => s.IsArmed)` false and calls no `MainViewModel` route method; `OnlineSourcesCount` is unchanged; the tile's text, tooltip and automation name contain no credentials and no query values; a second Start with a new URL stops the first player before starting the next; Remove stops the player. Red.
- [ ] **Step 2:** Implement view and wiring. Green.
- [ ] **Step 3:** Run the full suite. Commit.

### T6: Lifecycle: stop on tab switch and window close (S)

**Files:** Modify `ViewModels/MainViewModel.cs` (`StopAllRtsp()`, called from the tab-change handler and the window `Closing`/`Dispose` path), `Views/VisualRoutingView.xaml.cs` (stop on `Unloaded`/`IsVisibleChanged` false; start again only streams the user had running when the tab returns); test `RtspLifecycleTests.cs`.

- [ ] **Step 1:** Failing tests with the fake player: `LeavingVisualRoutingTab_StopsPlayers`; `ReturningToTab_RestartsOnlyStreamsThatWereRunning`; `WindowClose_DisposesPlayers`; `TabSwitchWhileConnecting_CancelsConnect_NoLateEventRevivesTile` (the fake raises `Playing` after Stop; state must stay Stopped); `ShowLogs`/`ShowAdvanced` toggles do not restart anything. Red.
- [ ] **Step 2:** Implement. Green. Commit.

### T7: Preview harness and screenshots (S)

**Files:** Create `tests/AvasRoutingTool.Tests/RtspSourcePreviewHarness.cs`.
**Completion criterion:** gated by `AVAS_RTSP_PREVIEW=1` like `RoomCamerasPreviewHarness`; opens the Main Window strip with TX cards and the RTSP tile pointed at a `FakeRtspServer` (buttons to switch it to Normal, Require401, Refuse and "Drop connection" to show Lost then Live). With `AVAS_RTSP_URL` set, the real stream is used instead, for an interactive run only. Screenshots (both themes, 1920x1080 and 1024x720; Live, Lost, Error) go to `ui_screenshots/UI-v2-rtsp/` and use the fake server only; check them by eye for any address text before commit.

- [ ] **Step 1:** Write the harness; smoke it with `AVAS_RTSP_PREVIEW_AUTOCLOSE_SECONDS=5`.
- [ ] **Step 2:** Shoot, inspect, run the full suite, commit.

## Order and sizes

T0 (S) first; T1 (S) can start at the same time. T3 (M) needs T1. T4 (L) needs T0 and T3. T5 (M) needs T3. T6 (S) needs T5. T7 (S) last. Total about 4 to 5 working days (the config and encryption task and the multi-field dialog are gone).

## Deferred (not in this phase)

- Persisting the URL: an `AppConfig` entry holding only a Windows DPAPI (`ProtectedData`, `CurrentUser`) blob; it cannot be read on another PC or user, so the tile would ask for a new paste.
- Several streams, a Name field, Room Cameras binding (`CameraPreviewState` would need to accept a non-`SourceCardViewModel` source).

## Self-review

Spec coverage: entry and session-only storage (D-2, D-4, T5); masking in UI and logs (T1, T3, T4, T5); tile states (T3, T5); stop on tab switch and close (T6); fake RTSP source (T4); decoder decision (comparison, D-3, T0). Types used across tasks (`RtspUrl`, `IRtspStreamPlayer`, `RtspTileState`, `RtspPlayerEvent`) are defined once in T1 and T3.
