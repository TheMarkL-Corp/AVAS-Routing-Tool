# QA final review: display cards carousel (75de7f6..a6238bf)

Spec verdict: FAIL (one Review Focus item, "resize while scrolled keeps the left card", is neither implemented nor tested)
Quality verdict: Changes needed

Method: read-only static review of the source diff, the carousel tests, plan Review Focus, controller rulings and the earlier QA files. No build or tests run.

## Findings

### Critical
none

### Important
1. **Resize while scrolled does not keep the left card, and nothing tests it** (Review Focus item 5).
   VisualRoutingView.xaml.cs:129-131 (ScrollChanged / SizeChanged only call UpdateDisplayArrows) and AdaptiveCardPanel.cs ArrangeOverride: the offset is kept in pixels (ScrollViewer clamps it) while card width is a function of viewport width ((viewport-48)/3.5, min 400). Scenario: 12 displays at 1920 (card 525.7), user taps the right arrow 3 times (offset 3 x 541.7 = 1625), then restores/narrows the window to 1400 (card about 385 -> 400): the same 1625 px now shows card index about 3.9, so the left card changes (and an arrow press afterwards lands mid-card). No overlap occurs (the arrange is a plain plan), so the "no card overlap" half holds; the "same left card" half fails. No test in ViewSmokeTests.DisplaysCarousel.cs resizes at all (grep for Width/Resize shows only ShowingAt for fixed sizes).
   Fix idea: in DisplaysScroll_SizeChanged, if ViewportWidth changed and the previous step was known, compute leftIndex = round(oldOffset / oldStep) before and ScrollToHorizontalOffset(leftIndex * newStep) after relayout (or store the left-card index in ScrollChanged). Add a smoke test: 12 displays, scroll 3 cards, change window width 1920 -> 1400, assert HorizontalOffset / step is within 0.01 of 3. If the controller prefers to defer, record the ruling; otherwise this is the one thing to fix before merge.

### Minor
2. Review Focus items with no test:
   - "1 to 3 displays at 1920: centred, no arrows" at view level: only the panel test (AdaptiveCardPanelTests.cs:214, three cards centred, no scroll) and the 4-display "arrows hidden" smoke test exist. Risk: low (arrow visibility is driven by ScrollableWidth, which is 0 there), but a 1 and 3 display smoke test asserting arrows Collapsed and the first card centred would be cheap.
   - "12 displays reach the end, last card fully visible": covered (Displays_Arrows_StepOneCard_AndDisableAtEnds uses 12 and checks the last card right edge <= viewport).
   - "Exactly 4: no scroll, no arrows": covered (layout, and smoke Displays_FourAt1920_ArrowsHidden).
   - "Arrows disabled at ends": covered, but the line `Assert.False(left.IsTabStop && left.IsEnabled)` (DisplaysCarousel.cs:~57) is vacuous (IsEnabled false already makes it pass); assert only `!left.IsEnabled`.
   - "Tap vs pan": tap tested (Touch_Tap_OnCard_StillArms); a real touch pan cannot be simulated, only PanningMode.Both is asserted. Risk: a drag that starts on the Copy layout handle versus a card pan relies on the existing drag threshold; accepted per progress.md, verify by hand on the touch device.
3. Which earlier Minors still matter for merge:
   - qa-task-3 #5 (Style.ScrollViewer.VerticalOverlay: horizontal gutter fixed at 16 px whenever HorizontalScrollBarVisibility is Auto, even when nothing scrolls; no Hidden triggers): Lists.xaml HorizontalGutter. Cards are height-planned and the row is VerticalAlignment=Top, so the cost is 16 px of dead space under the row, and only matters on short windows; acceptable, but a trigger on ComputedHorizontalScrollBarVisibility would remove it. Not blocking.
   - qa-task-3 #2 (arrow top margin at 0 until measured): resolved (the arrows are now Collapsed until the picture is measured, VisualRoutingView.xaml.cs UpdateDisplayArrows). Gone.
   - qa-task-3 #4 (focus lost when the arrow disables at an end): resolved (MoveFocusFromArrow, with a test).
   - qa-task-3 #1 (wheel swallowed on short window): resolved with test. Residual: if ScrollableWidth > 0 and ScrollableHeight > 0 (very short window with 5+ displays) the wheel is always sideways, so vertical is reachable only by bar or touch. Acceptable; Shift+wheel would be a nicety.
   - qa-task-3 #3 (arrow over the 8 px vertical overlay thumb, right margin 8 inside the 16 px gutter): still there, cosmetic, only when the safety-net bar shows.
   - qa-task-3 #6a vacuous tab-stop assertion: still there (see 2).
   - qa-task-3 #7 stale comments in ViewSmokeTests.UiV2FullHd.cs: fixed in this diff (comment rewritten to the carousel rule). Comment says picture 434 wide for exactly 4; baseline InlineData is 430, fine.
   - qa-tasks-1-2 #1 (arrange resolves viewport differently from measure): benign for the planned scroller (infinite available width); does not matter for merge. #2 (transient 960 first pass): self-heals, wasted work only. #3/#5/#6 style nits: ignore. #4 test gaps: viewport < 400 at layout level and cards added/removed still untested; low risk (count/viewport are part of the stored-plan key).
4. Test-only hygiene: `Displays_MouseWheel_PansSideways` and arrow tests use Relayout, no timing; fine. ViewSmokeTests.DisplayCard.cs comment line is run-on ("(touch drag, drag threshold ...)") nit.

## Cross-checks asked for

(1) Review Focus coverage: see Important 1 and Minor 2. Everything except resize-while-scrolled has at least one test; 1 and 3 displays have a panel-level centring test only.

(2) Source strip and Composer: GridSpec.Default unchanged (CarouselVisibleCards and CarouselMinCardWidth default to 0; Compute branches on CarouselVisibleCards > 0; the panel gates every new line on IsCarousel, the else branch in ArrangeOverride is the original code with viewport == width). Only one XAML use of AdaptiveCardPanel with a non-default spec (VisualRoutingView.xaml:232, DisplayCard); a grep of src shows no other consumer, and the source strip is not an AdaptiveCardPanel. The Composer shares VisualRoutingView but the new XAML is confined to the Displays scroller and two overlay buttons in Grid row 1 of the displays section; the Composer does not use DisplaysScroll. No break found. The updated source-strip tests only changed the display-picture gates (673x378 -> 499x281), not the strip.

(3) Style.ScrollViewer.VerticalOverlay: safe. Keyed (not implicit), used once, lives in Lists.xaml which is merged before Theme.xaml; the template's `DynamicResource Style.ScrollBar.Thin` is resolved at runtime from App resources, so the order does not matter and there is no StaticResource forward-reference risk. Both PART_ scroll bars exist. Differences from the implicit style are only the vertical overlay and the Hidden triggers (see Minor 3). Moving it out of Theme.xaml also fixed the styles guard.

(4) Locked design rules: arrows 48 x 48 (Size.Touch.Min) with MinWidth/MinHeight, vector SymbolIcon (ChevronLeft24/Right24), no hard-coded colours (all DynamicResource brushes that exist in both themes per qa-task-3), no text so no font floor concern (tooltip uses the app default), AutomationProperties.Name set on both ("Scroll displays left/right"), focus ring uses the InputModality ring. Status never colour-only: a disabled arrow is dimmed (Opacity 0.4) and, more importantly, the arrows are Collapsed when not scrollable; the disabled state is conveyed by opacity plus IsEnabled for AT, which is fine for a navigation control (the enabled one is the only actionable one).

(5) Resizing / DPI: all widths are DIPs (no pixel assumptions). The vertical bar is an overlay, so ViewportWidth is the full scroller width and the 3.5 / exactly-4 formulas stay valid with the bar showing; the cost is that on a short window the bar covers the right 16 px of the right-most card and, with 4 cards that exactly fill the row, a few pixels of the 4th card edge. The row's content width is never reduced, so there is no feedback loop (bar appearing does not change card width, so no flicker). Min 400 means below about 1,420 px viewport the cards stop shrinking and the row scrolls for 4+ displays; this is the controller ruling. See Important 1 for the one real resize defect.

(6) Screenshots test: ViewSmokeTests.UiV2Round3Shots.cs returns on the first statement when AVAS_UIV2_SHOTS is null or whitespace, before RunSta, ThemeService, windows or file IO; clean no-op. It sets Left = -10000 and restores the dark theme in finally (only on the enabled path). The [Fact] is not skipped (shows as passed), consistent with the existing UiV2 shot tests.

## Merge advice
Fix or explicitly rule on Important 1 (resize keeps left card); everything else can ship. Consider the one-line vacuous-assertion fix (Minor 2) in the same commit.
