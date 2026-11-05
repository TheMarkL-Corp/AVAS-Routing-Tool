# Main Window Display Cards: One Row, Pan Sideways Implementation Plan (rev 2)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Display (RX) cards stay a comfortable size in a single row. At 1920 wide you see up to 4 cards, or 3.5 cards when there are more (the half card shows there is more to the side). You pan left and right by touch drag, mouse wheel or arrow buttons. No vertical scroll in the RX section.

**Architecture:** Today `AdaptiveGridLayout.Compute` shrinks every card until all of them fit the window (5 displays at 1920 become 5 columns of about 362 px). Add a `Carousel` mode to `GridSpec`: one row, card width from a target of 3.5 visible cards, rule below. `AdaptiveCardPanel` lays out that one row; `DisplaysScroll` scrolls horizontally; two overlay arrow buttons step one card.

**Tech Stack:** .NET 8 WPF, WPF-UI 4.3.0, xUnit STA tests in `tests/AvasRoutingTool.Tests`.

**Spec:** User feedback 2026-11-05 on `ui_screenshots/UI-v2-round3/before-main-1920.png` (Task 5); design rules `docs/superpowers/specs/2026-10-29-ui-v2-master-guidelines.md`; locked decisions in `docs/superpowers/handoff/2026-11-04-ui-round3-handoff.md`.

## Global Constraints

- Base: everything combined (round 2 + Room Cameras + rename + Advanced), merged on `claude/ui-round3-6710d5` first (Task 0).
- Touch targets 48 px (36 px compact); font floor 12 px; arrow buttons are icon-only vector buttons with `AutomationProperties.Name`.
- Card chrome rows stay 24 / 56 / 64, `GridSpec.DisplayCard.ChromeHeight` 170. Display cards keep the 64 x 56 `Layout` dropdown and `Copy layout`. Not reopened.
- Arming a source or a copy never moves a card. Never merge to `main`, never push.
- Floor window 1024x720, target 1920x1080.

## Card width rule (the decision this plan encodes)

`viewport` = scroller width, `g` = 16 gap, `n` = display count, `W35 = (viewport - 3g) / 3.5`, `Min` = 320, `Max` = 960.
- `n >= 5`: width = `max(Min, W35)`; one row; scrolls sideways (about 3.5 cards visible).
- `n == 4`: width = `max(Min, (viewport - 3g) / 4)` (about 460 at 1920): all four fit, no scroll at 1920.
- `n <= 3`: width = `max(Min, W35)`, row centred, no scroll.
- At 1024 (viewport about 992): `W35` is 270, so width is `Min` 320 (about 2.9 cards visible), scrolls for `n >= 4`.

## Review Focus

- 1 to 3 displays at 1920: cards are `W35` wide, centred, no arrows shown.
- Exactly 4 displays at 1920: no scroll, no arrows.
- 12 displays: all realized (live previews), arrows reach first and last card; the last card is fully visible at the end.
- Arrow at an end of the row is disabled (and not tab-stoppable), so a press never does nothing silently.
- Window resized while scrolled: the scroll position keeps the same left card, no card overlap.
- A touch drag on a card pans the row; a tap on a card still arms or routes (drag threshold), and dragging a card onto another still copies.

---

### Task 0: Combine the branches

Merge `ui-v2/rename`, `claude/advanced-page-3a0947` and `ui-v2/room-cameras` into this branch (reuse `ui-v2/preview-advanced` as the reference for conflicts). Apply the two known fixes: tab gaps 4 px so five tabs fit at 1024; the tab-order test lists Advanced last. Run the full non-live suite: 0 failed. One merge commit per branch. Tag `ui-round3-base` on the result (revert path).

### Task 1: Carousel rule in the layout policy

**Files:**
- Modify: `src/AvasRoutingTool/Controls/AdaptiveGridLayout.cs` (`GridSpec.Carousel`, `Compute`)
- Test: `tests/AvasRoutingTool.Tests/AdaptiveGridLayoutTests.cs`

**Interfaces:**
- Produces: `GridSpec.CarouselVisibleCards` (`double`, default `0` = today's fit-all behaviour, so the source strip is untouched; `DisplayCard` sets `3.5`). `Compute` signature unchanged; in carousel mode it returns `Rows == 1`, `Columns == count`, and `NeedsScroll == true` when the row is wider than `availableWidth`.

- [ ] **Step 1: Write the failing tests** (names and values from the rule above, viewport 1888): `Carousel_OneCard_Is526Wide`, `Carousel_FourCards_FitWithoutScroll`, `Carousel_FiveCards_Scrolls`, `Carousel_At992_UsesMinWidth`, `Carousel_Twelve_OneRow`; the existing fit-all tests stay green.
- [ ] **Step 2: Run to verify fail:** `dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj -p:BaseOutputPath=bin/r3/ --filter "FullyQualifiedName~AdaptiveGridLayoutTests" -- RunConfiguration.DisableParallelization=true`. Expected: new tests FAIL.
- [ ] **Step 3: Implement** the rule in `Compute` behind `CarouselVisibleCards > 0`.
- [ ] **Step 4: Run to verify pass.**
- [ ] **Step 5: Commit** `feat(layout): display cards use a one-row carousel rule`.

### Task 2: Panel lays out one row and exposes its extent

**Files:**
- Modify: `src/AvasRoutingTool/Controls/AdaptiveCardPanel.cs` (measure returns the full row width; arrange reuses the measured plan)
- Test: `tests/AvasRoutingTool.Tests/AdaptiveCardPanelTests.cs`

- [ ] **Step 1: Write the failing test** `DisplayPanel_FiveCards_RowIsWiderThanViewport_ArrangeMatchesMeasure`: panel in a 1888 x 590 horizontal `ScrollViewer`; assert `panel.DesiredSize.Width > 1888`, each child `ActualWidth` equals the planned width, children sit at one `Y`.
- [ ] **Step 2: Run to verify fail.**
- [ ] **Step 3: Implement:** in carousel mode measure with the plan's full row width (do not clamp to `availableSize.Width`), store the plan and reuse it in arrange; left-align when scrolling, centre when it fits.
- [ ] **Step 4: Run to verify pass.**
- [ ] **Step 5: Commit** `feat(layout): card panel lays out a carousel row`.

### Task 3: Horizontal scroller with arrow buttons

**Files:**
- Modify: `src/AvasRoutingTool/Views/VisualRoutingView.xaml` (`DisplaysScroll`, lines 215-234) and `VisualRoutingView.xaml.cs`
- Modify: `src/AvasRoutingTool/Resources/Styles/Buttons.xaml` (one `Style.Button.CarouselArrow`, 48 x 48, round, icon only)
- Test: the Main Window smoke test that builds `VisualRoutingView`

**Interfaces:**
- Produces: `VisualRoutingView.ScrollDisplaysBy(int cards)` (positive = right, one step = one card width plus gap, animated or snapped; clamps at both ends). Arrow buttons `DisplaysScrollLeft` and `DisplaysScrollRight` named for tests.

- [ ] **Step 1: Write the failing tests:** `Displays_FiveAt1920_HorizontalScrollOnly` (`HorizontalScrollBarVisibility=Hidden/Auto`, vertical `Disabled`, `ScrollableWidth > 0`); `Displays_Arrows_StepOneCard_AndDisableAtEnds`; `Displays_FourAt1920_ArrowsHidden`; `Displays_MouseWheel_PansSideways`.
- [ ] **Step 2: Run to verify fail.**
- [ ] **Step 3: Implement:** `HorizontalScrollBarVisibility="Auto"` (thin bar, existing theme), `VerticalScrollBarVisibility="Disabled"`, `PanningMode="HorizontalOnly"`; arrows overlay the left and right edges (48 px, vertically centred on the pictures, visible only when scrollable, disabled at the ends via `ScrollChanged`); `PreviewMouseWheel` turns wheel delta into horizontal offset.
- [ ] **Step 4: Run to verify pass.**
- [ ] **Step 5: Commit** `feat(main-window): displays pan sideways with arrow buttons`.

### Task 4: Drag versus pan, and keyboard

**Files:**
- Modify: `src/AvasRoutingTool/Views/DisplayCardView.xaml.cs` only if the drag threshold needs it
- Test: Main Window smoke test

- [ ] **Step 1: Write the failing tests:** `Touch_Tap_OnCard_StillArms`; `Keyboard_Tab_ToCardOffscreen_ScrollsItIntoView` (focus a card beyond the right edge, assert it is inside the viewport).
- [ ] **Step 2: Run to verify fail,** then **Step 3:** fix (`BringIntoView` on card `GotKeyboardFocus`; keep the existing copy-drag threshold, pan only on a drag that starts outside the copy handle). **Step 4:** pass. **Step 5:** commit `fix(main-window): tap, drag and keyboard focus work in the sideways row`.

### Task 5: Screenshots and the full suite

**Files:** Create `ui_screenshots/UI-v2-round3/` (`before-main-1920.png`, `after-main-1920-5displays.png`, `after-main-1920-12displays.png`, `after-main-1920-4displays.png`, `after-main-1024.png`).

- [ ] **Step 1:** Save the before screenshot; produce the after shots with the env-gated `AVAS_UIV2_SHOTS` renderer (add 4, 12 and 1024x720 cases if missing).
- [ ] **Step 2: Run the full non-live suite:** `dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj -p:BaseOutputPath=bin/r3/ --filter "FullyQualifiedName!~LiveControlServerTests&FullyQualifiedName!~LiveFunctionTests" --blame-hang-timeout 120s -- RunConfiguration.DisableParallelization=true`. Expected: 0 failed (rerun once for the known STA flake).
- [ ] **Step 3: Commit** `docs(ui-round3): main window display cards before and after screenshots`.

---

## Assumptions (tell me if wrong)

- The empty 64 px status row on idle cards stays as it is (locked chrome, stable height); you did not answer decision 2.
- The sources strip stays fixed above the displays; you did not answer decision 3. With one short row of displays it now has more room.
