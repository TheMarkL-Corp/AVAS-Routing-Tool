# MultiView Composer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A touch-first MultiView Composer: choose a layout, place encoders into blocks (equal-size redundancy allowed), watch each block's live preview, and put the staged picture on the surgeon's display with one Take (with Undo) - plus **Copy a display onto another display** in one gesture, blocked when the target's native resolution is lower.

**Architecture:** A pure `MultiViewDraft` per display holds the staged picture; a pure `SurfacePlanner` turns a draft into windows/surfaces/subscriptions (shared surface for equal-size duplicates, black for empty blocks, per-display layout name); `MultiViewOrchestrator.TakeAsync` applies a whole draft atomically and keeps the previous one for Undo. A `MultiViewComposerViewModel` drives a new `MultiViewComposerView` that replaces the Displays row while open; the overview gains tap-to-arm and a draft chip. Copy uses a pure `DisplayCopyPlanner` (what the source shows now + the target's EDID modes -> allowed/blocked with spec text) and `MultiViewOrchestrator.CopyAsync`, applied immediately with a 10 s Undo of the target's previous screen.

**Tech Stack:** C# .NET 8 WPF (`net8.0-windows`), xUnit, existing `MockSdvoeClient` / `RecordingHandler` / `TestEdids` / fixtures; SDVoE API Reference PDS-062489 3.9 §5.

**Spec:** `docs/superpowers/specs/2026-10-25-multiview-composer-ux.md` (read it first; this plan argues from it).

## Confirmed decisions (product owner, 2026-10-25/26)

| # | Decision |
|---|---|
| 1 | MultiView changes are **staged**; the surgeon's screen changes only on **Take**. Single View stays **immediate**. |
| 2 | Operator: circulating nurse on a **touchscreen, possibly gloved**. Every drag has a tap and a keyboard path. |
| 3 | Same encoder in several blocks only when the blocks are **the same size**; otherwise a visible reason. |
| 4 | Empty blocks go on screen **black**; Take needs >= 1 placed source. **Undo = 10 s** (Take and Copy). |
| 5 | Step 1 entry: card-header **Layout ▾** menu (schematic tiles) -> Composer with that layout staged. Card badge shows **N/M** fill. |
| 6 | Placed block: always-visible **corner ✕** (one-tap clear, >= 48 px hit area) + tap block for **Replace / Move**. Duplicates labelled **"Same as N"**. |
| 7 | **Copy a display**: drag only from a labelled **Copy layout** handle (or tap it, then tap a display); **immediate**, 10 s Undo, **one-time** copy of what the source shows on screen, run at the **source's output format**; cyan ring / lock + reason cues; copy / no-drop cursors and a tooltip for mouse users. |
| 8 | Copy guard: target's **native** width and height >= source's **output** width and height, **and** the target's EDID lists that mode; unreadable EDID or offline target = blocked. |

## Global Constraints

- Nothing reaches a decoder until Take; Steps 1-3 change no device. Restore/state reads stay read-only.
- Duplicates only between equal-size blocks; every refusal carries the spec's reason text.
- Previews mirror existing `SourceCardViewModel.PreviewBitmap` references - no new sockets, IGMP joins or decoders.
- Touch: primary targets >= 64 px high (Take >= 72), others >= 48 px, >= 12 px spacing; composer text >= 14 px; no hover-only controls.
- Every drag has a tap and a keyboard alternative; status never by colour alone.
- New theme token: `Color.Status.Staged` = `#F59E0B` (+ `Brush.Status.Staged`). Reuse every other existing token.
- Layout names: `avas_<rxmac12>_<preset>_<W>x<H>`; restore still recognises `avas_<preset>_<W>x<H>`.
- Single View routing stays immediate (Phase 1 behaviour), now also reachable by tap-source-then-tap-display.
- Empty blocks go on screen as black; Take needs >= 1 placed source. Undo window = 10 s (Take and Copy).
- Copy: immediate on drop/tap, one-time (no link), copies what the source shows **on screen** (never a draft), runs at the **source's output format**; allowed only when the target's native (preferred EDID) resolution is >= the source's output in both dimensions **and** the target's EDID lists that mode; unreadable target EDID = blocked. Drag starts only from the card's Copy layout handle.
- Every XAML change gets an STA view-smoke test; live tests stay opt-in; build with `--artifacts-path` when the app is running.

## Review Focus

1. **Copy dropped on a lower-resolution or unreadable-EDID target** -> no hardware call at all, the target shows the spec's reason, nothing changes (Task 8 `Decide` tests; Task 9 `CopyDisplayAsync_Blocked_SendsNothing`).
2. **Encoder goes offline while staged** -> its blocks show OFFLINE and Take is blocked with "TX2 is offline - clear or replace block 2" (Task 2 `DraftValidator` test; Task 5 `CanTake` test).
3. **Display swapped / EDID changed between staging and Take** -> Take changes nothing on the device, returns `DisplayChanged`, and the composer redraws the layout for the new output (Task 4 test; Task 5 test).
4. **Take or Copy tapped twice / while busy** -> exactly one hardware apply (Task 5 and Task 9 tests with a gated mock).
5. **Screen changed elsewhere while composing** (restore re-read shows a different live state, including a Copy onto this display) -> draft kept and flagged, live monitor and change list recomputed (Task 6 test; Task 9 `Copy_MarksTheTargetsDraftStale`).

---

## File Structure

```
src/AvasRoutingTool/
├── Sdvoe/MultiView/
│   ├── MultiViewDraft.cs          // staged picture: plan + placements, Place/Clear/Move/WithLayout/Diff   (Task 2)
│   ├── DraftValidator.cs          // offline + cross-display size conflicts -> BlockIssue list            (Task 2)
│   ├── SurfacePlanner.cs          // draft -> SurfacePlan (layout name, windows, subscriptions, scalers)    (Task 3)
│   ├── MultiViewRequests.cs       // CreateWindow with optional surface (BLACK when null)                  (Task 3)
│   ├── MultiViewPresets.cs        // TryParseName: per-display + legacy names                              (Task 3)
│   ├── MultiViewRestore.cs        // restored windows -> MultiViewDraft (shared surfaces = duplicates)     (Task 4)
│   ├── MultiViewOrchestrator.cs   // TakeAsync / UndoAsync / GetLive; AssignAsync removed; CopyAsync      (Tasks 4, 9)
│   ├── EdidInfo.cs                // + SupportedModes (DTDs + CTA VICs), Supports(w,h,hz)                  (Task 7)
│   ├── CtaVic.cs                  // VIC -> (w,h,hz) table for the common CTA-861 codes                    (Task 7)
│   ├── ScreenState.cs             // what a display shows now: nothing / single / multiview / external     (Task 8)
│   └── DisplayCopyPlanner.cs      // source screen + target EDID -> CopyDecision (spec §15 texts)          (Task 8)
├── Sdvoe/RxState.cs, RxStateReader.cs // RxWindow gains geometry (x, y, w, h, offsets) for external copies (Task 8)
├── ViewModels/
│   ├── ComposerBlockViewModel.cs  // one block: rects, size text, staged source + preview, availability   (Task 5)
│   ├── MultiViewComposerViewModel.cs // steps, arm, place, picker, move, clear, take, undo, discard        (Task 5)
│   ├── MainViewModel.cs           // OpenComposer, drafts, tap-to-arm, drop choice; ArmDisplay, CopyDisplayAsync, per-card undo (Tasks 6, 9)
│   └── DisplayCardViewModel.cs    // HasDraft, Edit/MultiView commands, drop prompt; copy state, Undo toast (Tasks 6, 9)
├── Views/
│   ├── MultiViewComposerView.xaml(.cs) // left rail, staged canvas, picker, right rail                     (Task 10)
│   ├── LayoutSchematic.xaml(.cs)  // draws a plan's blocks as a tile                                      (Task 10)
│   ├── VisualRoutingView.xaml     // Displays row <-> Composer swap; copy drag adorner                     (Task 10)
│   ├── DisplayCardView.xaml(.cs)  // MultiView / Edit buttons, draft chip, drop prompt, Copy layout handle, copy target states, Undo toast (Task 10)
│   └── SourceCardView.xaml(.cs)   // armed ring, "in N blocks" badge, tap-to-arm                          (Task 10)
└── Resources/Theme.xaml           // Color.Status.Staged + composer text/button styles                     (Task 10)
tests/AvasRoutingTool.Tests/
├── MultiViewDraftTests.cs, DraftValidatorTests.cs, SurfacePlannerTests.cs,
├── MultiViewTakeTests.cs, MultiViewComposerViewModelTests.cs, MainViewModelComposerTests.cs,
├── EdidModesTests.cs, DisplayCopyPlannerTests.cs, DisplayCopyTests.cs,
└── ViewSmokeTests.cs (composer, schematic, card prompts, copy states)
```

---

### Task 1: Lab spike - shared surfaces, black windows, one-shot apply (gates Tasks 3-4)

**Files:** none in the repo. Use the scratch harness pattern from Phase 2 (a console project referencing `AvasRoutingTool.csproj`), with the user's go-ahead (changes RX1's picture).

- [ ] **Step 1:** Create layout `avas_spike_1920x1080`: windows 0 and 2 -> surface 1, window 1 -> surface 2, window 3 -> no `target_surface`. `set:scaler` 960x540 + stream 1 on TX2 and VEGA_1100; `set:multiview` on RX1 with subscriptions `[TX2@1, VEGA@2]` (no duplicate stream).
  Expected: request SUCCESS; user sees TX2 in blocks 1 and 3, VEGA in block 2, block 4 black.
- [ ] **Step 2:** Read back `get multiview` and `get settings`; confirm windows 0 and 2 both report `target_surface` 1 and only subscriptions 1 and 2 are STREAMING. Save both replies as fixtures `rx_multiview_shared_surface.json` / `_layout.json`.
- [ ] **Step 3:** Two RX on two different layouts at once (RX1 + PAX355S) with per-display names; re-create RX1's layout while PAX runs its own; confirm PAX is unaffected.
- [ ] **Step 4:** Record results in the spec §2 ("confirmed on lab"/"refuted") and restore the lab (genlock + join RX1 to TX2, stop TX2 stream 1). **If shared surfaces are refuted, stop and re-plan redundancy before Task 3.**

### Task 2: Staged draft and validation (pure)

**Files:** Create `Sdvoe/MultiView/MultiViewDraft.cs`, `Sdvoe/MultiView/DraftValidator.cs`; Test `MultiViewDraftTests.cs`, `DraftValidatorTests.cs`.

**Interfaces:**
- Produces:
  - `record DraftPlacement(int SlotIndex, string SourceMac, string SourceName)`
  - `record DraftResult(bool Ok, string? Reason)`
  - `record DraftChange(int SlotIndex, string? From, string? To)` (names; null = empty)
  - `sealed class MultiViewDraft(MultiViewLayoutPlan plan)` with `Plan`, `IReadOnlyDictionary<int, DraftPlacement> Placements`, `IReadOnlyList<DraftPlacement> Unplaced`, `DraftResult Place(int slot, string mac, string name)`, `void Clear(int slot)`, `void ClearAll()`, `DraftResult Move(int from, int to)` (swap when occupied), `MultiViewDraft WithLayout(MultiViewLayoutPlan plan)`, `IReadOnlyList<DraftChange> DiffFrom(MultiViewDraft? live)`, `MultiViewDraft Clone()`, `IEnumerable<int> SlotsShowing(string mac)`.
  - `enum BlockIssueKind { SourceOffline, SizeConflictOtherDisplay }`, `record BlockIssue(int SlotIndex, BlockIssueKind Kind, string Message)`
  - `record OtherDisplayUse(string DisplayName, string SourceMac, int Width, int Height)`
  - `static class DraftValidator { IReadOnlyList<BlockIssue> Validate(MultiViewDraft d, IEnumerable<OtherDisplayUse> others, Func<string, bool> isOnline); DraftResult CanPlace(MultiViewDraft d, int slot, string mac, string name, IEnumerable<OtherDisplayUse> others, Func<string,bool> isOnline); }`
- MACs compared with `SdvoeClient.NormalizeMac`.

- [ ] **Step 1: Write failing tests** (all on `MultiViewPresets.Build(id, 1920, 1080)` unless stated):

```csharp
[Fact] void Place_SameEncoderInTwoEqualBlocks_IsAllowed()            // matrix 0 and 3 -> Ok, both in Placements, SlotsShowing("tx2") == {0,3}
// "block N" in every message is the 1-based logical block number (quad: Focus = block 1).
[Fact] void Place_SameEncoderInADifferentSizeBlock_IsRefusedWithReason() // quad_1_3: Place(0,"tx2","TX2") then Place(1,...) -> !Ok,
    // Reason == "TX2 is in block 1 at 1280x720 - this block is 640x360. One encoder has one size per layout."
[Fact] void Place_ReplacingTheOnlyDifferentSizeUse_IsAllowed()        // quad: tx2 in 0, Place(0,"tx3"), then Place(1,"tx2") -> Ok
[Fact] void Move_ToEmptyBlock_Moves_ToOccupiedBlock_Swaps()
[Fact] void Move_ThatWouldCreateASizeConflict_IsRefused()
[Fact] void WithLayout_RemapsByBlockOrder_AndListsWhatNoLongerFits() // matrix 4 placed -> side_by_side: 0,1 kept; 2,3 in Unplaced
[Fact] void WithLayout_DropsADuplicateThatBecomesASizeConflict()     // matrix tx2 in 0 and 1 -> pip: inset+main differ -> second goes to Unplaced
[Fact] void DiffFrom_Live_ListsOnlyChangedBlocksInBlockOrder()        // live {0:tx1}, draft {0:tx2,1:tx1} -> [(0,"TX1","TX2"),(1,null,"TX1")]
[Fact] void DiffFrom_NullLive_TreatsEveryPlacementAsNew()
[Fact] void Validate_OfflineEncoder_FlagsEveryBlockShowingIt()        // Message == "TX2 is offline"
[Fact] void Validate_EncoderAtAnotherSizeOnAnotherDisplay_IsFlagged() // other ("RX2","tx2",1920,1080) vs block 960x540 ->
    // Message == "TX2 runs at 1920x1080 on RX2 - this block is 960x540."
[Fact] void CanPlace_ReturnsTheSameReasonsBeforePlacing()
```

- [ ] **Step 2:** Run `dotnet test --filter "FullyQualifiedName~MultiViewDraft|FullyQualifiedName~DraftValidator"` -> FAIL (types missing).
- [ ] **Step 3:** Implement both types. `WithLayout` maps the old plan's logical slot order onto the new plan's logical order; anything not placeable goes to `Unplaced` in order.
- [ ] **Step 4:** Re-run -> PASS.
- [ ] **Step 5:** Commit `feat(multiview): staged draft with equal-size redundancy and validation`.

### Task 3: Surface planning, black windows, per-display layout names (pure)

**Files:** Create `Sdvoe/MultiView/SurfacePlanner.cs`; Modify `MultiViewRequests.cs`, `MultiViewPresets.cs`; Test `SurfacePlannerTests.cs`, `MultiViewRequestsTests.cs`, `MultiViewPresetsNameTests.cs`.

**Interfaces:**
- Consumes: `MultiViewDraft` (Task 2), `MultiViewLayoutPlan`, `WindowSlot`.
- Produces:
  - `record PlannedWindow(WindowSlot Slot, int? Surface)` (null = BLACK)
  - `record PlannedSource(string Mac, int Surface, int Width, int Height)`
  - `record SurfacePlan(string LayoutName, IReadOnlyList<PlannedWindow> Windows, IReadOnlyList<PlannedSource> Sources)`
  - `static SurfacePlan SurfacePlanner.Plan(MultiViewDraft draft, string rxMac)` - one surface per distinct encoder; an encoder whose block is > 2048x1080 gets surface 0, others 1..11 in block order.
  - `MultiViewRequests.CreateWindow(string layoutName, WindowSlot slot, int? surface)` - omits `target_surface` when null.
  - `MultiViewPresets.LayoutName(string preset, int w, int h, string rxMac)` -> `avas_<mac12>_<preset>_<W>x<H>`; `TryParseName` accepts it and the legacy form (out `id`, `w`, `h`).

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] void Plan_DuplicateEncoder_SharesOneSurfaceAndOneSubscription() // matrix tx2 in 0 and 3 -> Windows[0].Surface == Windows[3].Surface, Sources.Count == 1
[Fact] void Plan_EmptyBlocks_AreBlack()                                  // Windows with no placement -> Surface == null
[Fact] void Plan_OversizedBlockGetsSurfaceZero()                         // pip at 3840x2160, tx in main -> that source Surface == 0, size 3840x2160
[Fact] void Plan_UsesThePerDisplayLayoutName()                           // LayoutName == "avas_74fe483c059e_matrix2x2_1920x1080"
[Fact] void CreateWindow_WithoutSurface_OmitsTargetSurface()             // body has no "target_surface"
[Fact] void TryParseName_PerDisplayAndLegacyNames()                      // both forms -> ("matrix2x2",1920,1080)
```

- [ ] **Step 2:** Run -> FAIL. **Step 3:** Implement. **Step 4:** Run the whole non-live suite -> PASS (existing request/preset tests still green).
- [ ] **Step 5:** Commit `feat(multiview): shared surfaces, black windows, per-display layout names`.

### Task 4: Atomic Take, Undo, restore into drafts

**Files:** Modify `MultiViewOrchestrator.cs`, `MultiViewRestore.cs`, `ISdvoeClient.cs`/`SdvoeClient.cs` (`CreateMultiViewLayoutAsync(SurfacePlan, MultiViewLayoutPlan)`, `SetMultiViewAsync(rx, layoutName, w, h, fps, subs)`), `RoutingOrchestratorTests.cs` (mock); Test `MultiViewTakeTests.cs`, update `MultiViewOrchestratorTests.cs`, `MultiViewRestoreTests.cs`.

**Interfaces:**
- Consumes: `SurfacePlanner.Plan`, `MultiViewDraft`, existing EDID/divider logic.
- Produces:
  - `enum TakeOutcome { Applied, NothingToTake, DisplayChanged, Rejected }`, `record TakeResult(TakeOutcome Outcome, string? Error, MultiViewLayoutPlan? NewPlan)`
  - `Task<(MultiViewLayoutPlan Plan, MultiViewCanvas Canvas)> PlanForDisplayAsync(AvasDevice rx, string layoutId)` - reads the EDID and builds the plan; read-only (replaces `BeginLayoutAsync`)
  - `Task<TakeResult> MultiViewOrchestrator.TakeAsync(AvasDevice rx, MultiViewDraft draft)`
  - `Task<TakeResult> UndoAsync(AvasDevice rx)`; `bool CanUndo(AvasDevice rx)`
  - `MultiViewDraft? GetLive(AvasDevice rx)`; `void Adopt(AvasDevice rx, MultiViewDraft live, MultiViewCanvas canvas)`
  - `IEnumerable<OtherDisplayUse> UsesExcept(AvasDevice rx)` (live layouts of other displays, for `DraftValidator`)
  - `RestoredMultiView` gains `MultiViewDraft? Draft` (windows sharing a surface -> the same encoder in several blocks).
  - `AssignAsync` and `BeginLayoutAsync` are removed; `ReturnToSingleViewAsync`, `TurnOffAsync`, `ExitExternalAsync`, `Forget`, `DescribeCanvas`, `GetCanvas` stay.

- [ ] **Step 1: Write failing tests** (mock client, `TestEdids`):

```csharp
[Fact] async Task Take_FirstTime_ScalesEachDistinctEncoderOnce_CreatesTheLayout_LoadsItOnce()
    // matrix tx2 in 0,3 + tx1 in 1 -> calls: scaler:tx2, scaler:tx1, layout:avas_rx1..., multiview with 2 subscriptions
[Fact] async Task Take_ReReadsTheEdid_AndOnAChangedDisplayChangesNothing()   // Outcome DisplayChanged, MultiViewCalls empty, NewPlan at new canvas
[Fact] async Task Take_HeavyLayout_HalvesEachDistinctEncoderOnce()          // 4K matrix -> one divider:...:half per encoder
[Fact] async Task Take_Rejected_KeepsTheOldLiveState()                      // fail "multiview:" -> Rejected, GetLive unchanged
[Fact] async Task Take_SameAsLive_IsNothingToTake()                         // no calls
[Fact] async Task Undo_TakesThePreviousLiveDraftAgain()                     // after two takes, Undo -> live equals first draft
[Fact] async Task Take_EncoderLeavingEveryHeavyLayout_GetsItsFrameRateRestored()
[Fact] void Restore_SharedSurfaceFixture_GivesADraftWithTheEncoderInBothBlocks() // Task 1 fixture
[Fact] void Restore_LegacyLayoutNames_StillAdopt()
```

- [ ] **Step 2:** Run -> FAIL. **Step 3:** Implement: `TakeAsync` = EDID check -> per distinct encoder `PrepareSourceScalerAsync` (+ divider when heavy) -> `CreateMultiViewLayoutAsync` -> one `SetMultiViewAsync` -> on success swap live/previous and restore unused dividers. Update `MainViewModel` callers that used `AssignAsync` to go through a draft (temporary until Task 6) so the suite stays green.
- [ ] **Step 4:** Full non-live suite -> PASS.
- [ ] **Step 5:** Commit `feat(multiview): atomic take with undo; restore into drafts`.

### Task 5: Composer view model

**Files:** Create `ViewModels/ComposerBlockViewModel.cs`, `ViewModels/MultiViewComposerViewModel.cs`; Test `MultiViewComposerViewModelTests.cs`.

**Interfaces:**
- Consumes: Tasks 2-4; `SourceCardViewModel` (preview mirror), `LayoutItemViewModel` titles.
- Produces:
  - `enum BlockState { Empty, Staged, Live, Issue }`
  - `ComposerBlockViewModel`: `Index`, `Label`, `Normalized*`, `SizeText` ("960x540"), `SourceName`, `PreviewBitmap`, `IsPreviewSupported`, `State`, `IsAvailableForArmed`, `UnavailableReason`, `SameAsText` ("Same as 1"), `IsSelected` (shows the Replace / Move strip), `IssueText`, `ICommand ClearCommand` (corner ✕; enabled only when placed).
  - `MultiViewComposerViewModel(DisplayCardViewModel display, MultiViewOrchestrator mv, Func<IEnumerable<SourceCardViewModel>> sources, IReadOnlyList<LayoutItemViewModel> layouts, MultiViewDraft? resumeDraft)` with:
    `LayoutOptions`, `SelectedLayoutId`, `Blocks`, `LiveBlocks`, `ArmedSource`, `PickerBlockIndex`, `MovingFromIndex`, `Changes`, `StepLayoutDone`, `StepPlaceDone`, `CanTake`, `TakeLabel` ("TAKE 3 CHANGES TO SCREEN"), `TakeBlockedReason`, `IsBusy`, `Banner` (text + kind), `CanUndo`, `OutputText`, `HeaderNotice`;
    methods `Task SelectLayoutAsync(string id)` (calls `PlanForDisplayAsync`; no device change; re-maps via `MultiViewDraft.WithLayout`), `void Arm(SourceCardViewModel s)`, `void Disarm()`, `void TapBlock(int i)`, `void Pick(int block, SourceCardViewModel s)`, `void BeginMove(int i)`, `void Clear(int i)`, `void ClearAll()`, `void Discard()`, `Task TakeAsync()`, `Task UndoAsync()`, `MultiViewDraft Draft`, `void OnLiveChangedElsewhere(MultiViewDraft? live)`.
  - `TapBlock` semantics: armed -> place (stays armed); not armed + empty -> open picker; not armed + placed -> select (strip: Replace / Move); moving -> move/swap to this block. Clearing is the block's `ClearCommand` (corner ✕), one tap.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] async Task NewComposer_IsAtStepOne_WithNothingStaged()                // StepLayoutDone false, Blocks empty, CanTake false
[Fact] async Task SelectLayout_ShowsEmptyBlocks_AtTheDisplaysOutput_NoDeviceChange() // OutputText "OUT 3840x2160@60 (VP3268-4K)", MultiViewCalls empty
[Fact] async Task Armed_TapTwoEqualBlocks_PlacesTheEncoderInBoth_AndStaysArmed()  // SameAsText on block 3 == "Same as 1"
[Fact] async Task Armed_DifferentSizeBlock_IsUnavailableWithReason()          // IsAvailableForArmed false, UnavailableReason as Task 2
[Fact] async Task PlacedBlock_MirrorsTheEncodersLivePreview()                 // set source PreviewBitmap -> block PreviewBitmap same instance
[Fact] async Task TapEmptyBlock_Unarmed_OpensThePicker_PickPlacesAndCloses()
[Fact] async Task Move_ThenTapAnotherBlock_SwapsThem()
[Fact] async Task CornerClear_RemovesThePlacementInOneTap_AndNothingReachesTheDevice() // block.ClearCommand -> empty; MultiViewCalls empty
[Fact] async Task TapPlacedBlock_OpensTheReplaceMoveStrip()                  // IsSelected true; a second tap closes it
[Fact] async Task TakeLabel_CountsChanges_AndCanTakeFollowsIssues()           // offline encoder -> CanTake false, TakeBlockedReason "TX2 is offline - clear or replace block 2"
[Fact] async Task Take_WithNothingPlaced_IsBlocked()                          // TakeBlockedReason "Place at least one source (Step 2)."
[Fact] async Task Take_EmptyBlocks_AreAllowed_AndGoOnScreenBlack()            // 1 of 4 placed -> CanTake true; after Take blocks 2-4 State Live + empty
[Fact] async Task Take_Success_MakesStagedLive_AndOffersUndo()                // Blocks State Live, CanUndo true, Banner "On screen"
[Fact] async Task Undo_IsOfferedFor10Seconds_ThenWithdrawn()                 // injected clock: CanUndo true at +9.9 s, false at +10 s
[Fact] async Task Take_Rejected_KeepsTheDraft_AndSaysNothingChanged()          // Banner contains "Nothing changed on screen"
[Fact] async Task Take_DisplayChanged_RedrawsTheLayoutAndAsksToReview()
[Fact] async Task Take_WhileBusy_IsIgnored()                                  // gated mock: two TakeAsync calls -> one "multiview:" call
[Fact] async Task Discard_ReturnsToWhatIsOnScreen()
[Fact] async Task ChangeLayout_KeepsSourcesByBlockOrder_AndListsUnplaced()
```

- [ ] **Step 2:** Run -> FAIL. **Step 3:** Implement. **Step 4:** Run -> PASS.
- [ ] **Step 5:** Commit `feat(composer): composer view model (arm, place, picker, move, take, undo)`.

### Task 6: Overview integration

**Files:** Modify `ViewModels/MainViewModel.cs`, `ViewModels/DisplayCardViewModel.cs`, `ViewModels/SourceCardViewModel.cs`; Test `MainViewModelComposerTests.cs`.

**Interfaces:**
- Produces on `MainViewModel`: `MultiViewComposerViewModel? ActiveComposer`, `Task OpenComposerAsync(DisplayCardViewModel d, string? layoutId = null, SourceCardViewModel? armed = null)`, `void CloseComposer()` (keeps the draft), `SourceCardViewModel? ArmedSource`, `void ArmSource(SourceCardViewModel s)`, `Task TapDisplayAsync(DisplayCardViewModel d)` (armed -> Single View route, then disarm; MultiView card -> drop choice), `Task ChooseFullScreenAsync(DisplayCardViewModel d)`, `Task ChooseIntoBlockAsync(DisplayCardViewModel d)`.
- On `DisplayCardViewModel`: `bool HasDraft`, `bool IsDropChoiceOpen`, `SourceCardViewModel? DropChoiceSource`, `IReadOnlyList<LayoutItemViewModel> LayoutMenu` (Single View, the four presets, Off), `bool IsLayoutMenuOpen`, `ModeBadge` with fill ("MATRIX 2X2 · 3/4" from the on-screen state).
- On `MainViewModel`: `Task ChooseLayoutFromCardAsync(DisplayCardViewModel d, string layoutId)` (preset -> `OpenComposerAsync(d, layoutId)`; `single`/`off` -> existing behaviour).
- On `SourceCardViewModel`: `bool IsArmed`, `int UsedInBlocks` (composer only).
- `ApplyLayoutAsync`/`AssignSlotAsync` are retired; the layout tile drop calls `OpenComposerAsync(d, layoutId)`.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] async Task ArmThenTapDisplay_RoutesSingleViewImmediately_AndDisarms()
[Fact] async Task TapMultiViewCard_WithArmedSource_OpensTheDropChoice_NotARoute()
[Fact] async Task DropChoice_FullScreen_LeavesMultiViewAndRoutes()
[Fact] async Task DropChoice_IntoBlock_OpensTheComposerWithTheSourceArmed()
[Fact] async Task LayoutTileDrop_OpensTheComposerWithThatLayoutStaged()
[Fact] async Task CardLayoutMenu_Preset_OpensTheComposerWithThatLayoutStaged_NoDeviceChange()
[Fact] async Task CardLayoutMenu_SingleOrOff_BehaveLikeTheBottomBarTiles()
[Fact] void ModeBadge_ShowsLayoutAndFill_FromTheOnScreenState()           // live matrix with 3 placed -> "MATRIX 2X2 · 3/4"
[Fact] async Task CloseComposer_WithChanges_KeepsADraft_AndTheCardShowsIt()  // HasDraft true; reopening resumes it
[Fact] async Task RestoreWhileComposing_KeepsTheDraft_UpdatesLive_ShowsNotice()  // Review Focus 3
[Fact] async Task Arm_AutoDisarmsAfter15sIdle()                                 // inject a clock
```

- [ ] **Step 2:** Run -> FAIL. **Step 3:** Implement (clock injected as `Func<DateTime>` + a `DispatcherTimer` only in the view). **Step 4:** Run full non-live suite -> PASS.
- [ ] **Step 5:** Commit `feat(composer): tap-to-arm, drop choice, drafts on cards`.

### Task 7: Modes a display advertises (EDID) - needed by Copy

**Files:** Modify `Sdvoe/MultiView/EdidInfo.cs`; Create `Sdvoe/MultiView/CtaVic.cs`; Test `EdidModesTests.cs`.

**Interfaces:**
- Produces: `record EdidMode(int Width, int Height, int RefreshHz, bool Interlaced)`; `IReadOnlyList<EdidMode> EdidInfo.SupportedModes` (progressive + interlaced; DTDs from base and CTA blocks, plus CTA Video Data Block VICs, de-duplicated, preferred first); `bool EdidInfo.Supports(int width, int height, int refreshHz)` (progressive only, refresh rounded); `EdidMode? EdidInfo.Native` (= first progressive DTD); `static bool CtaVic.TryGet(int vic, out EdidMode mode)` covering at least VIC 4, 5, 16, 19, 20, 31, 32, 33, 34, 93, 94, 95, 96, 97 (CTA-861 table).

- [ ] **Step 1: Write failing tests** (real fixtures `TestEdids.ViewSonic4k`, `TestEdids.Pax355s`):

```csharp
[Fact] void ViewSonic4k_ListsItsNative4K60_And1080p60()     // Supports(3840,2160,60) && Supports(1920,1080,60); Native == (3840,2160,60)
[Fact] void Pax355s_Lists4K60_And1080p60()
[Fact] void SyntheticFullHdOnlyEdid_DoesNotList4K()          // TestEdids.Build((1920,1080,60,false)) -> !Supports(3840,2160,60)
[Fact] void CtaVic_KnownCodes_DecodeToTheirModes()          // 16 -> (1920,1080,60), 97 -> (3840,2160,60), 95 -> (3840,2160,30)
[Fact] void CtaVic_UnknownCode_IsIgnored()                  // TryGet(250, ...) == false; SupportedModes has no entry for it
[Fact] void SupportedModes_HasNoDuplicates()
```

- [ ] **Step 2:** Run -> FAIL. **Step 3:** Implement: parse the CTA data block collection (tag 2 = Video Data Block, low 7 bits = VIC; VIC >= 129 with native bit uses `vic & 0x7F` when <= 64). **Step 4:** Run -> PASS.
- [ ] **Step 5:** Commit `feat(edid): modes a display advertises (DTD + CTA VICs)`.

### Task 8: Copy decision (pure)

**Files:** Create `Sdvoe/MultiView/ScreenState.cs`, `Sdvoe/MultiView/DisplayCopyPlanner.cs`; Modify `Sdvoe/RxState.cs` + `Sdvoe/RxStateReader.cs` (window geometry); Test `DisplayCopyPlannerTests.cs`, `RxStateReaderTests.cs`.

**Interfaces:**
- Consumes: `EdidInfo` (Task 7), `MultiViewDraft` (Task 2), `RxLayout`.
- Produces:
  - `RxWindow(int WindowIndex, int Surface, int X, int Y, int Width, int Height, int HOffset, int VOffset)` (parsed from `get multiview`).
  - `enum ScreenKind { Nothing, Single, MultiView, External }`
  - `record ScreenState(ScreenKind Kind, int Width, int Height, int Fps, string? SourceMac, string? SourceName, MultiViewDraft? Draft, RxLayout? External, IReadOnlyList<RxSubscription> Subscriptions)` with `string Describe()` -> e.g. "Matrix 2x2 (TX2_DUAL LINK_SFPA, VEGA_1100) - 3840x2160" / "Single View (TX2_DUAL LINK_SFPA) - 3840x2160".
  - `record CopyDecision(bool Allowed, string Summary, string? Note, string? Reason)`
  - `static CopyDecision DisplayCopyPlanner.Decide(string sourceName, ScreenState source, string targetName, bool targetOnline, bool sameDisplay, EdidInfo? targetEdid)`
- Copy texts are exactly spec §15's table, with the real names and sizes filled in.

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] void SameNativeResolution_IsAllowed()                 // 4K -> 4K (ViewSonic EDID): Allowed, Summary "Copy RX1_c059e_SFPA - Matrix 2x2 (...) - 3840x2160", Note null
[Fact] void HigherNativeResolution_IsAllowed_AtTheSourcesFormat() // source 1920x1080@60, target 4K EDID -> Allowed,
    // Note "Runs at 1920x1080 @ 60 on this 3840x2160 display"
[Fact] void LowerNativeResolution_IsBlocked()                // source 3840x2160, target TestEdids.Build((1920,1080,60,false)) ->
    // Reason "PAX355S_SFP A is 1920x1080 - RX1_c059e_SFPA's picture is 3840x2160. Copy needs a display at least that large."
[Fact] void ModeNotListedInTheTargetEdid_IsBlocked()         // source 2560x1440@60 on a 4K target without that mode ->
    // Reason "PAX355S_SFP A does not list 2560x1440 @ 60 in its EDID"
[Fact] void UnreadableTargetEdid_IsBlocked()                 // null -> "Can't read PAX355S_SFP A's resolution - copy blocked to be safe"
[Fact] void OfflineTarget_IsBlocked()                        // "PAX355S_SFP A is offline"
[Fact] void SourceShowingNothing_IsBlocked()                 // "RX1_c059e_SFPA shows nothing to copy"
[Fact] void DroppingOnItself_IsNotACopy()                    // "Drop on another display"
[Fact] void SingleViewSource_UsesTheSameResolutionRule()     // Single 3840x2160 onto a 1080p-only target -> blocked
[Fact] void ExternalLayout_IsCopyable()                      // ScreenKind.External with geometry -> Allowed
[Fact] void ParseLayout_ReadsWindowGeometry()                // fixture rx_multiview_layout.json: window 3 -> X 960, Y 540, 960x540
```

- [ ] **Step 2:** Run -> FAIL. **Step 3:** Implement. Native = `targetEdid.Native`; blocked when `native.Width < source.Width || native.Height < source.Height`; then `Supports(source.Width, source.Height, source.Fps)`. **Step 4:** Run -> PASS.
- [ ] **Step 5:** Commit `feat(copy): copy decision with resolution guard`.

### Task 9: Copy apply, Undo of the target, overview wiring

**Files:** Modify `MultiViewOrchestrator.cs`, `MainViewModel.cs`, `DisplayCardViewModel.cs`, `SurfacePlanner.cs` (plan from explicit window geometry); Test `DisplayCopyTests.cs`.

**Interfaces:**
- Consumes: Tasks 3, 4, 7, 8.
- Produces:
  - `ScreenState MainViewModel.CurrentScreen(DisplayCardViewModel d)` (from orchestrator live draft, restored external layout, or Single View route).
  - `Task<CopyResult> MultiViewOrchestrator.CopyAsync(AvasDevice target, ScreenState source)`; `record CopyResult(bool Ok, string? Error)`. Single -> genlock if the target is in multiview, then `join` the same encoder; MultiView/External -> target layout `avas_<targetmac12>_copy_<W>x<H>` with the source's windows, same surfaces, `set:multiview` with the source's subscriptions at the source's `W x H @ fps` (no EDID canvas re-pick, no scaler or divider change).
  - Target Undo: `ScreenState? PreviousScreen(AvasDevice)` kept 10 s; `Task<bool> RestoreScreenAsync(AvasDevice target, ScreenState previous)` (Nothing -> leave + genlock; Single -> join; MultiView -> Take of that draft).
  - On `MainViewModel`: `DisplayCardViewModel? ArmedDisplay`, `Task ArmDisplayAsync(DisplayCardViewModel d)` (reads every other online target's EDID once, in parallel, read-only, and caches it for this arm/drag), `CopyDecision CopyDecisionFor(DisplayCardViewModel target)`, `Task<bool> CopyDisplayAsync(DisplayCardViewModel source, DisplayCardViewModel target)`, `void DisarmDisplay()`.
  - `static class CopyDragFeedback` in `ViewModels/CopyDragFeedback.cs`: `DragDropEffects Effects(CopyDecision d)` (Copy / None) and `string Tooltip(CopyDecision d)` (Reason when blocked, else Note or Summary) - the view's `DragOver` uses only these.
  - On `DisplayCardViewModel`: `bool CanCopyFrom` + `string CopyFromBlockedText` (source has nothing on screen), `bool IsCopyTargetAllowed`, `string CopyTargetText` (Summary/Note or Reason), `bool IsCopyTargetBlocked`, `string? UndoToastText`, `bool CanUndoScreen`, `ICommand UndoScreenCommand`, `ICommand CopyLayoutCommand` (arms).

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] async Task CopyMultiView_ToSameResolution_LoadsTheSourcesWindowsAndSubscriptions_OnTheTarget() // layout:avas_<pax>_copy_3840x2160, multiview:<pax>:... same subs; no scaler/divider calls
[Fact] async Task CopyMultiView_ToHigherResolution_RunsAtTheSourcesFormat()   // SetMultiView video 1920x1080@60 on a 4K target
[Fact] async Task CopySingleView_RoutesTheSameEncoder()                       // join <target> <tx>
[Fact] async Task CopyDisplayAsync_Blocked_SendsNothing()                     // lower-res target -> MultiViewCalls empty, LastVideoTx null
[Fact] async Task Copy_ShowsAnUndoToastOnTheTarget_For10Seconds()             // UndoToastText "PAX355S_SFP A now shows RX1_c059e_SFPA's Matrix 2x2"
[Fact] async Task UndoScreen_PutsBackWhatTheTargetShowedBefore()              // previous Single View -> join previous encoder
[Fact] async Task Copy_MarksTheTargetsDraftStale()                            // target draft kept, notice "Screen changed - draft is based on an older picture"
[Fact] async Task Copy_WhileBusy_IsIgnored()                                  // gated mock: two copies -> one apply
[Fact] async Task ArmDisplay_ReadsEachTargetEdidOnce_AndUpdatesEveryCardsCopyState()
[Fact] async Task ArmedDisplay_TapAllowedTarget_CopiesImmediately_AndDisarms()
[Fact] async Task ArmedDisplay_TapBlockedTarget_DoesNothing_ButShowsTheReason()
[Fact] async Task SourceShowingNothing_CannotBeArmed()                        // CanCopyFrom false, CopyFromBlockedText "RX1_c059e_SFPA shows nothing to copy"
[Fact] void CopyDragEffects_AllowedIsCopy_BlockedIsNone()                    // static CopyDragFeedback.Effects(decision) -> DragDropEffects.Copy / None; Tooltip(decision) == Reason or Note
```

- [ ] **Step 2:** Run -> FAIL. **Step 3:** Implement. **Step 4:** Full non-live suite -> PASS.
- [ ] **Step 5:** Commit `feat(copy): copy a display onto another with 10 s undo`.

### Task 10: Views and theme

**Files:** Create `Views/MultiViewComposerView.xaml(.cs)`, `Views/LayoutSchematic.xaml(.cs)`; Modify `Views/VisualRoutingView.xaml`, `Views/DisplayCardView.xaml(.cs)`, `Views/SourceCardView.xaml(.cs)`, `Views/LayoutsBarView.xaml`, `Resources/Theme.xaml`; Test `ViewSmokeTests.cs`.

**Interfaces:** Consumes Task 5/6/9 view models only. Composer layout per spec §5.2 (left rail 280 px, right rail 320 px, canvas fills, `FractionalLayoutPanel` with the plan's aspect). Picker = `Popup` anchored to the block. Copy per spec §15: a **Copy layout** handle (>= 56 px) on every display card is the only drag start (8 px threshold; the card body never starts a copy drag); a drag adorner shows the source's mini picture; target cards show cyan ring + `CopyTargetText` (allowed) or lock + reason (blocked); armed banner with Stop; Undo toast on the target card. Theme adds `Color/Brush.Status.Staged`, `Style.Button.Take` (min height 72, 16 px semibold), `Style.Button.Composer` (min height 56), `Style.Text.ComposerLabel` (14 px).

- [ ] **Step 1: Write failing smoke tests** (`RunSta`, window kept open while asserting `IsVisible`):

```csharp
[Fact] void Composer_Step1_RendersFourSchematicTiles()
[Fact] void Composer_WithStagedDuplicate_ShowsSameAsChip_AndStagedLabel()
[Fact] void Composer_UnavailableBlock_ShowsLockAndReasonText()
[Fact] void Composer_TakeButton_IsAtLeast72High_AndBlockedReasonVisibleWhenBlocked()
[Fact] void Composer_PickerPopup_RendersSourceTilesWithThumbnails()
[Fact] void DisplayCard_DropChoice_AndDraftChip_Render()
[Fact] void DisplayCard_CopyHandle_IsAtLeast56High_AndDisabledTextShowsWhenNothingToCopy()
[Fact] void DisplayCard_CopyTargetAllowed_ShowsSummary_Blocked_ShowsLockAndReason()
[Fact] void DisplayCard_UndoToast_Renders()
[Fact] void DisplayCard_LayoutMenu_OpensWithSixSchematicTiles_AndHeaderButtonIsAtLeast56High()
[Fact] void Composer_CornerClear_HasAHitAreaOfAtLeast48()                   // ActualWidth/Height of the ✕ button >= 48 while the glyph is 32
[Fact] void DisplayCard_BadgeShowsFill()                                     // "MATRIX 2X2 · 3/4" visible
[Fact] void SourceCard_ArmedRing_AndUsedInBlocksBadge_Render()
[Fact] void VisualRoutingView_SwapsDisplaysForComposerWhenActive()
```

- [ ] **Step 2:** Run -> FAIL. **Step 3:** Implement the XAML; code-behind only for drag (8 px threshold) and keyboard routing. **Step 4:** Run -> PASS; render the composer and an overview with a copy in progress at 1920x1080 to PNG (throwaway test, as in Phase 2) and review against spec §5.2, §9 and §15; fix, re-render once.
- [ ] **Step 5:** Commit `feat(composer): composer view, schematic tiles, copy handle and target states`.

### Task 11: Keyboard, automation names, reduced motion

**Files:** Modify `MultiViewComposerView.xaml.cs`, `MultiViewComposerViewModel.cs`, `DisplayCardView.xaml.cs`; Test `MultiViewComposerViewModelTests.cs`, `DisplayCopyTests.cs`, `ViewSmokeTests.cs`.

- [ ] **Step 1: Write failing tests:** `Keyboard_ArmMovePlaceClearTake()` (VM: `ArmFocused()`, `MoveFocus(Direction)`, `PlaceAtFocus()`, `ClearAtFocus()`, Ctrl+Enter -> `TakeAsync`), `Keyboard_CopyLayout_EnterArms_EnterOnTargetCopies_EscStops()`, `Blocks_HaveAutomationNames()` ("Block 2, 960 by 540, empty"), `CopyTargets_AnnounceAllowedOrBlocked()` (automation name contains `CopyTargetText`), `Transitions_AreSkipped_WhenWindowsAnimationsAreOff()` (inject `Func<bool> animationsEnabled`).
- [ ] **Step 2:** FAIL. **Step 3:** Implement (`SystemParameters.ClientAreaAnimation` as the default source). **Step 4:** PASS.
- [ ] **Step 5:** Commit `feat(composer): keyboard path, automation names, reduced motion`.

### Task 12: Lab validation, publish, docs

- [ ] **Step 1 (user go-ahead):** On RX1: Matrix with TX2 in blocks 1 and 3 and VEGA in 2, Take, user confirms picture; Undo; change layout to PiP with TX2 in main -> TX2 unavailable for the inset with the spec reason; 4K 1+3 with Take; disconnect a source while staged -> Take blocked.
- [ ] **Step 2 (user go-ahead):** Copy RX1 (4K Matrix) onto PAX355S (4K) -> PAX355S shows the same picture (user confirms); Undo -> PAX355S back to its previous state; copy a Single View RX1 onto PAX355S. The lower-resolution block cannot be shown on hardware with today's two 4K decoders - it is covered by Task 8 tests; record that in the spec. Restore the lab.
- [ ] **Step 3:** `dotnet test --filter "FullyQualifiedName!~Live"` -> all pass; `dotnet publish -c Release -o publish/AvasRoutingTool`.
- [ ] **Step 4:** Update spec §2/§15 (lab results), `memory/` (local), Handover 04/05; commit `docs(composer): lab results and handover`.
