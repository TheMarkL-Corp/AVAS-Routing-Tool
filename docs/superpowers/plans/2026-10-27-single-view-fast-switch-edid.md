# Single View Fast Switch at RX EDID Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Routing a TX into an RX in Single View first puts that RX into `FAST_SWITCHED` at the resolution of its display's EDID, then joins; the tool never sets `GENLOCKED` again.

**Architecture:** `RoutingOrchestrator` gains `EnsureSingleViewModeAsync` (read EDID -> `MultiViewCanvasPolicy.Choose` -> read RX state -> `set:video` only when it differs) and a result-returning `RouteSingleViewAsync`. `MultiViewOrchestrator` calls the same method wherever it used to call `SetGenlockedAsync`. `MainViewModel` shows the chosen format and any failure on the card.

**Tech Stack:** C# .NET 8 WPF, xUnit (`MockSdvoeClient`, `RecordingHandler`), SDVoE REST (`POST /api/device/<mac>`).

**Spec:** `docs/superpowers/specs/2026-10-27-single-view-fast-switch-edid.md` (decisions D1-D10).

## Global Constraints

- Request body, verbatim from the API Reference: `{ "op": "set:video", "display_mode": "FAST_SWITCHED", "video": { "width": W, "height": H, "fps": F } }`.
- Display mode is exactly `FAST_SWITCHED`; never `FAST_SWITCHED_CROP`/`_STRETCH`, never `GENLOCKED` (D5, D9).
- Resolution comes only from `MultiViewCanvasPolicy.Choose` (D2); fallback `MultiViewCanvasPolicy.DefaultFallback` = 1920x1080@60.
- EDID is read on every Single View route, no cache (D8). No change at launch, refresh or restore-from-hardware (D7).
- `set:video` failure: no `join`, previous source untouched, card error (D6).
- CLAUDE.md: read `memory/active_context.md` and `system_patterns.md` first; TDD (red before green); a view/XAML change is verified in `ViewSmokeTests` on an STA thread; commit code with its memory update in one atomic commit.
- Test command: `dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj --filter "FullyQualifiedName!~LiveControlServerTests&FullyQualifiedName!~LiveFunctionTests"` (714 pass at the start).

## Review Focus

1. No monitor / EDID unreadable or checksum bad: route still goes at 1920x1080@60 Fast Switch, card says why (`... (display EDID unavailable)`).
2. `set:video` rejected: no `join` sent, `ActiveSourceId` unchanged, card shows an error, next route can succeed.
3. RX already in the right mode: no `set:video`. In `FAST_SWITCHED_CROP`/`_STRETCH`, or at another size or fps: it is sent.
4. `ReadReceiverStateAsync` returns null: `set:video` is sent (cannot prove it is already correct).
5. Re-routing another TX to the same RX, or a multiview exit followed by a route: second `set:video` is skipped, join order stays `set:video` before `join`.

---

### Task 1: `set:video FAST_SWITCHED` request and client method

**Files:**
- Modify: `src/AvasRoutingTool/Sdvoe/MultiView/MultiViewRequests.cs` (next to `SetGenlocked`, line 79)
- Modify: `src/AvasRoutingTool/Sdvoe/ISdvoeClient.cs`, `src/AvasRoutingTool/Sdvoe/SdvoeClient.cs` (next to `SetGenlockedAsync`, line 446)
- Modify: `tests/AvasRoutingTool.Tests/RoutingOrchestratorTests.cs` (`MockSdvoeClient`)
- Test: `tests/AvasRoutingTool.Tests/MultiViewRequestsTests.cs`, `tests/AvasRoutingTool.Tests/SdvoeClientMultiViewTests.cs`

**Interfaces:**
- Produces: `RestCall MultiViewRequests.SetFastSwitched(string rxMac, int width, int height, int fps)`; `Task<bool> ISdvoeClient.SetFastSwitchedAsync(string rxMac, int width, int height, int fps)`; `MockSdvoeClient` records `fastswitch:{rxMac}:{width}x{height}@{fps}` in `MultiViewCalls`.
- Consumes: existing `DevicePath`, `SendRestCallAsync`, `Record`.

- [ ] **Step 1: Write the failing tests.** `SetFastSwitched_SendsTheApiExampleBody` in `MultiViewRequestsTests`: path `/api/device/74fe483c059e`; body `op == "set:video"`, `display_mode == "FAST_SWITCHED"`, `video.width/height/fps == 1920/1080/60`, and no top-level `width`. `SetFastSwitchedAsync_SendsSetVideoFastSwitched` in `SdvoeClientMultiViewTests` (same shape as the genlocked test: one call, `ok == true`, body `display_mode == "FAST_SWITCHED"`, `video.fps == 60`).
- [ ] **Step 2: Run to verify they fail** - `dotnet test ... --filter "FullyQualifiedName~SetFastSwitched"`; expect a compile error (member missing).
- [ ] **Step 3: Implement** `SetFastSwitched` (anonymous-object body like `SetGenlocked`), `SdvoeClient.SetFastSwitchedAsync` (log `"MULTIVIEW"` line `Setting {mac} to fast switch {w}x{h}@{fps}`, then `SendRestCallAsync`), the interface member, and the mock override (`Record($"fastswitch:{rxMac}:{width}x{height}@{fps}")`). Leave `SetGenlocked*` in place until Task 3.
- [ ] **Step 4: Run to verify they pass** - same filter; expect PASS.
- [ ] **Step 5: Commit** - `feat(routing): add set:video FAST_SWITCHED request and client call`.

### Task 2: `RoutingOrchestrator` ensures the mode before the join

**Files:**
- Modify: `src/AvasRoutingTool/Sdvoe/RoutingOrchestrator.cs`
- Test: `tests/AvasRoutingTool.Tests/RoutingOrchestratorTests.cs`

**Interfaces:**
- Consumes: Task 1 `SetFastSwitchedAsync`; `EdidInfo.Parse`, `MultiViewCanvasPolicy.Choose`, `ISdvoeClient.ReadDisplayEdidAsync`/`ReadReceiverStateAsync`.
- Produces:
  - `RoutingOrchestrator(ISdvoeClient client, MultiViewCanvas? fallback = null)`
  - `sealed record SingleViewMode(bool Ok, MultiViewCanvas Format, bool Changed)`
  - `Task<SingleViewMode> EnsureSingleViewModeAsync(AvasDevice displayRx)`
  - `sealed record SingleViewRoute(bool Ok, MultiViewCanvas? Format, string? Error)`
  - `Task<SingleViewRoute> RouteSingleViewAsync(AvasDevice sourceTx, AvasDevice displayRx)`
  - `RouteSingleViewVideoAsync` stays, returning `(await RouteSingleViewAsync(...)).Ok`.

- [ ] **Step 1: Write the failing tests** (names; each asserts `mock.MultiViewCalls` order and the return value):
  - `Route_SetsFastSwitchAtTheEdidFormat_BeforeTheJoin` - 4K EDID (`TestEdids` fixture used by `MultiViewOrchestratorTests`) -> calls `["fastswitch:<rx>:3840x2160@60", join]`; `Format.FromDisplay == true`.
  - `Route_WithoutAnEdid_FallsBackTo1080p60_AndStillJoins` - `DisplayEdid = null` -> `fastswitch:...:1920x1080@60`, join sent, `Format.Description` contains `display EDID unavailable`.
  - `Route_WhenSetVideoIsRefused_SendsNoJoin_AndKeepsTheActiveSource` - mock `Record` returns false for `fastswitch:`; result `Ok == false`, `Error` non-empty, no join call, `ActiveSourceId` unchanged.
  - `Route_SkipsSetVideo_WhenTheRxIsAlreadyFastSwitchedAtTheFormat` - `ReceiverStates[rx]` with `DisplayMode "FAST_SWITCHED"`, same w/h/fps -> only the join is sent.
  - `Route_SendsSetVideo_WhenTheRxIsInCropStretchOrAnotherSize_OrUnreadable` - `[Theory]` over `FAST_SWITCHED_CROP`, `FAST_SWITCHED_STRETCH`, `GENLOCKED`, `FAST_SWITCHED` at 1280x720, and state missing (null).
  - Update the existing `RouteSingleViewVideoAsync_*` expectations for the extra leading `fastswitch:` call.
- [ ] **Step 2: Run to verify they fail** - `--filter "FullyQualifiedName~RoutingOrchestratorTests"`; expect FAIL/compile errors.
- [ ] **Step 3: Implement** `EnsureSingleViewModeAsync` (EDID read wrapped in try/catch like `MultiViewOrchestrator.ReadEdidAsync` -> null; log `"ROUTING"` `'<name>' single view output: <description>`), `RouteSingleViewAsync` (ensure; on `!Ok` return `Error = $"Could not set Fast Switch {w}x{h}@{fps} on '{name}'; the route was not sent."` without joining; otherwise the existing join and `ActiveSource*` updates), and the wrapper. Skip rule: `state.DisplayMode == "FAST_SWITCHED"` (ordinal, exact) and `Width/Height/Fps` equal.
- [ ] **Step 4: Run to verify they pass** - same filter; expect PASS.
- [ ] **Step 5: Commit** - `feat(routing): put the RX in Fast Switch at its EDID format before a Single View join`.

### Task 3: Multiview exits use the same mode; genlock removed

**Files:**
- Modify: `src/AvasRoutingTool/Sdvoe/MultiView/MultiViewOrchestrator.cs` (constructor line 131 passes `fallback` through; sites at lines ~376, ~719, ~1025)
- Modify: `src/AvasRoutingTool/Sdvoe/MultiView/MultiViewRequests.cs`, `ISdvoeClient.cs`, `SdvoeClient.cs`, `RoutingOrchestratorTests.cs` (remove `SetGenlocked`, `SetGenlockedAsync`, the mock override)
- Test: `tests/AvasRoutingTool.Tests/MultiViewOrchestratorTests.cs`, `DisplayCopyTests.cs`, `MultiViewRestoreTests.cs`, `MainViewModelRestoreTests.cs`, `MainViewModelMultiViewTests.cs`, `MainViewModelComposerTests.cs`, `MultiViewRequestsTests.cs`, `SdvoeClientMultiViewTests.cs`

**Interfaces:**
- Consumes: Task 2 `RoutingOrchestrator.EnsureSingleViewModeAsync`.
- Produces: no new public surface; `ReturnToSingleViewAsync`, `ExitExternalAsync`, `TurnOffAsync` (via `GenlockAndEndAsync`) and `RestoreCoreAsync` `ScreenKind.Nothing` end with `Ok` from `EnsureSingleViewModeAsync`.

- [ ] **Step 1: Write the failing tests.** In `MultiViewOrchestratorTests`: `ReturnToSingleView_SetsFastSwitchAtTheEdidFormat_NotGenlock`, `ExitExternal_SetsFastSwitchAtTheEdidFormat`, `TurnOff_LeavesTheRxInFastSwitch`, and `ReturnToSingleView_WhenFastSwitchIsRefused_ReturnsFalse_AndKeepsTheSession`. Add a repo-wide guard `NoGenlockCallIsEverSent` (a test that scans `MultiViewCalls` of the existing scenarios is not enough): assert in `MultiViewRequestsTests` that `typeof(MultiViewRequests)` has no method named `SetGenlocked` (reflection), so the call cannot come back.
- [ ] **Step 2: Run to verify they fail** - `--filter "FullyQualifiedName~MultiViewOrchestratorTests"`; expect FAIL.
- [ ] **Step 3: Implement** - replace the three `_client.SetGenlockedAsync(rx.DeviceId)` calls with `(await _routing.EnsureSingleViewModeAsync(rx)).Ok`; construct `_routing` with the orchestrator's fallback; delete `SetGenlocked`, `SetGenlockedAsync` and the mock override. Then update every test expectation `genlock:<mac>` (28 occurrences in the 7 test files above) to `fastswitch:<mac>:<w>x<h>@<fps>` for the EDID the test sets (fallback 1920x1080@60 when it sets none).
- [ ] **Step 4: Run the full non-live suite** - expect PASS (count rises by the new tests; the two deleted genlock tests are gone).
- [ ] **Step 5: Commit** - `refactor(routing): every route into Single View uses Fast Switch; drop GENLOCKED`.

### Task 4: Card shows the output format and the failure

**Files:**
- Modify: `src/AvasRoutingTool/ViewModels/MainViewModel.cs` (`RouteLockedAsync`, lines ~503-549)
- Modify: `src/AvasRoutingTool/ViewModels/DisplayCardViewModel.cs` (next to `ClearLayout`, line ~373)
- Test: `tests/AvasRoutingTool.Tests/MainViewModelTests.cs`, `tests/AvasRoutingTool.Tests/DisplayCardViewModelTests.cs`, `tests/AvasRoutingTool.Tests/ViewSmokeTests.cs`

**Interfaces:**
- Consumes: Task 2 `RouteSingleViewAsync` / `SingleViewRoute`.
- Produces: `void DisplayCardViewModel.SetSingleViewOutput(string info)` (sets `CanvasInfo`, raises `PropertyChanged`); `ClearLayout` and unroute clear it.

- [ ] **Step 1: Write the failing tests.** `MainViewModelTests.Route_ShowsTheOutputFormatOnTheCard` (4K EDID -> `CanvasInfo == "OUT 3840x2160@60 (<monitor>)"`); `Route_WithoutAnEdid_ShowsTheFallbackReason` (`CanvasInfo` contains `display EDID unavailable`); `Route_WhenFastSwitchIsRefused_SetsLastError_AndLeavesTheCardUnrouted` (`LastError` equals the orchestrator's `Error`, `IsRouted` false). `DisplayCardViewModelTests.SetSingleViewOutput_RaisesCanvasInfo_AndUnrouteClearsIt`. `ViewSmokeTests.DisplayCard_SingleView_ShowsTheOutputLine_AndItFitsWithoutEllipsis` (STA; existing `AssertTextFits`/`AssertInside` helpers; the 16:9 picture check must still hold with the line visible).
- [ ] **Step 2: Run to verify they fail** - `--filter "FullyQualifiedName~MainViewModelTests|FullyQualifiedName~DisplayCardViewModelTests|FullyQualifiedName~ViewSmokeTests"`; expect FAIL.
- [ ] **Step 3: Implement** - `RouteLockedAsync` calls `_orchestrator.RouteSingleViewAsync`; on success `displayVm?.SetSingleViewOutput(MultiViewCanvasPolicy.OutputLabel(result.Format!))`; on failure `displayVm.LastError = result.Error` and the existing warn log. `MainViewModel` keeps its other `RouteSingleViewVideoAsync` users unchanged. If the smoke test shows the output line collides with the telemetry rows, adjust only `DisplayCardView.xaml` row heights, not `GridSpec.DisplayCard.ChromeHeight`, unless the existing 16:9 smoke test fails.
- [ ] **Step 4: Run the full non-live suite** - expect PASS.
- [ ] **Step 5: Commit** - `feat(ui): show the Single View output format and Fast Switch failures on the display card`.

### Task 5: Lab gate, memory and handover

**Files:**
- Modify: `memory/active_context.md`, `memory/progress.md`, `memory/system_patterns.md` (section 4 "3-in-1 Automated Routing Pattern": Single View is now `set:video FAST_SWITCHED` at the EDID format, then `join`)
- Modify: `Handover/04_VERIFIED_FEATURES_AND_KNOWN_LIMITATIONS.md` (limits: AVP2000/AVP2000T only; EDID fallback)

- [ ] **Step 1: Hardware check on an AVAS-223 RX with a real monitor** (not automatable; record results in `Handover/04`): route TX A, then TX B, to the same RX - expect no blank on the second switch and the monitor at its native format; read `get` on the RX - expect `FAST_SWITCHED`, `width/height/frames_per_second` equal to the EDID; unplug the monitor, route - expect 1920x1080@60 and the fallback note on the card; route from a multiview - expect `FAST_SWITCHED`, never `GENLOCKED`; try a 4K TX into a 1080p monitor - expect letterboxing, no crop or stretch.
- [ ] **Step 2: Run the full non-live suite and a Release publish** - `dotnet publish src/AvasRoutingTool/AvasRoutingTool.csproj -c Release -o publish/AvasRoutingTool`; expect PASS and a clean publish.
- [ ] **Step 3: Update the three memory files and `Handover/04`** with the result of Step 1 (what passed, any decoder that rejected the call).
- [ ] **Step 4: Commit** - `docs(routing): record Single View Fast Switch behavior and lab result` (memory files are gitignored; commit the Handover change).
