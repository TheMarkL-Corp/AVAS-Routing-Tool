# Task 3 report: displays pan sideways with arrow buttons

Status: DONE_WITH_CONCERNS. Commit 38c24b4 `feat(main-window): displays pan sideways with arrow buttons`.

## What was done
- `VisualRoutingView.xaml`: `DisplaysScroll` is now Horizontal Auto / Vertical Disabled / PanningMode HorizontalOnly / Focusable False, with PreviewMouseWheel, ScrollChanged and SizeChanged handlers. Two sibling buttons `DisplaysScrollLeft` / `DisplaysScrollRight` in the same Grid row (no wrapper, so the `DisplaysHeaderRow...` test that reads `.Parent` as the grid still works), `Style.Button.CarouselArrow`, ui:SymbolIcon ChevronLeft24 / ChevronRight24 (Size.Icon.M), AutomationProperties.Name and ToolTip "Scroll displays left/right".
- `VisualRoutingView.xaml.cs`: public `ScrollDisplaysBy(int cards)` (one step = first DisplayCardView.ActualWidth + GridSpec.DisplayCard.Gap, clamped 0..ScrollableWidth, ScrollToHorizontalOffset). Wheel: offset -= delta, `e.Handled = true`. `UpdateDisplayArrows`: visible only when ScrollableWidth > 0.5, left disabled at offset 0, right disabled at the end (disabled = not focusable/tab stop), top margin set so the arrow is centred on the first card's PreviewSurface.
- `Buttons.xaml`: `Style.Button.CarouselArrow` 48x48 round, Brush.Surface.FloatingStrip fill, Brush.Border.Default hairline, Brush.Text.Primary icon (accent on hover), 2 px focus ring via InputModality.ShowsFocus plus Style.FocusVisual.Ring, opacity 0.4 when disabled. Only existing tokens.
- Theme.xaml implicit ScrollViewer style checked: horizontal gutter (16 px, thin bar) works for a horizontal bar, vertical gutter collapses when Disabled. No change needed.
- Tests: new `tests/AvasRoutingTool.Tests/ViewSmokeTests.DisplaysCarousel.cs` (4 tests). Updated one assertion in `ViewSmokeTests.DisplayCard.cs` (PanningMode.None -> HorizontalOnly) because the brief reverses that earlier rule.

## RED
`dotnet test ... --filter "FullyQualifiedName~ViewSmokeTests.Displays_|FullyQualifiedName~UiV2Guards|FullyQualifiedName~AdaptiveCardPanelTests" -- RunConfiguration.DisableParallelization=true`
Failed to compile: `CS1061 'VisualRoutingView' does not contain a definition for 'ScrollDisplaysBy'`.

## GREEN
Same command: `Passed! - Failed: 0, Passed: 83, Skipped: 0, Total: 83` (4 new tests, UiV2Guards, AdaptiveCardPanelTests; allowlist not widened).
(The filter uses `ViewSmokeTests.Displays_` since the new tests live in the existing partial class ViewSmokeTests.)

## Files changed
- src/AvasRoutingTool/Views/VisualRoutingView.xaml
- src/AvasRoutingTool/Views/VisualRoutingView.xaml.cs
- src/AvasRoutingTool/Resources/Styles/Buttons.xaml
- tests/AvasRoutingTool.Tests/ViewSmokeTests.DisplaysCarousel.cs (new)
- tests/AvasRoutingTool.Tests/ViewSmokeTests.DisplayCard.cs

## Concerns (other existing smoke tests now fail; ran a targeted subset, not the full suite)
Failing in subset `DisplayCard_|DisplaysHeaderRow|FullHd_|UiV2DisplaysTop|UiV2NoShift`: 6 failed, 73 passed.
1. `FullHd_DisplayPictures_AreAtLeastTheRecordedBaseline` (1, 2, 3 displays) and `FullHd_DisplayPictures_GainTheStripHeight`: pictures are 500x281 at 1920, below the old 731/587/673 baselines. This is the intended W35 rule from Task 1 (not caused by this task); baselines need re-recording (Task 5 presumably).
2. `DisplayCard_CopySourceTag_LiesOutsideThePicture(1264x755)`: "'COPYING FROM HERE' needs 123.9 px, has 63.9". Still fails with vertical scroll Auto, so it comes from the narrower cards, not from this task.
3. `DisplayCard_CopyAndChoiceStates_ReadInsideTheDisplaysView_AtRealWindowSizes(1008x615)`: PAX Undo toast is below the displays viewport (y 262, viewport height 254). Passes if vertical scroll is Auto, so it is caused by Vertical=Disabled per the brief: at small windows (floor 1024x720) the tall card is clipped at the bottom with no way to scroll vertically. Needs a decision (let the card row shrink via height, or allow vertical scroll at small windows, or rework the test).
- Arrow vertical centring is computed from the first card's picture in code (ScrollChanged/SizeChanged); a unit test checks it to 2 px.
- Wheel is handled even when the row does not scroll (as specified).

## Fix round 1 (controller rulings)
- (A) `test(main-window): picture size baselines follow the carousel rule`: FullHd_DisplayPictures_AreAtLeastTheRecordedBaseline re-recorded to 500x281 for 1, 2, 3, 5 displays (4 stays 430x242); GainTheStripHeight renamed FullHd_DisplayPictures_KeepTheCarouselSize with gate 499x281.
- (B) `feat(layout): display carousel cards are at least 400 px wide`: GridSpec.CarouselMinCardWidth (default 0, DisplayCard 400), ComputeCarousel clamps to it. New tests Carousel_At992_Is400, Carousel_At1232_UsesCarouselMin400, Carousel_At1888_StaysW35_WithTheCarouselMin (written together with the code; no separate RED run). DisplayCard_CopySourceTag_LiesOutsideThePicture(1264x755) now passes with 400.
- (C) `fix(main-window): displays scroller keeps a vertical safety net on short windows`: Vertical Auto, PanningMode Both (WPF's enum name; HorizontalAndVertical does not exist). The theme's reserved 16 px vertical gutter shrank the cards to 495 px pictures, so DisplaysScroll uses a new Style.ScrollViewer.VerticalOverlay (Theme.xaml): same bars, but the vertical bar overlays the right edge instead of reserving a gutter. Test renamed Displays_FiveAt1920_ScrollsSideways_NotVertically (vertical Auto, ScrollableHeight 0, computed vertical bar not visible).
- Final run (ViewSmokeTests | UiV2Guards | AdaptiveCardPanelTests | AdaptiveGridLayoutTests): Passed 457, Failed 0.

## Fix round 2
Commit: `fix(main-window): displays wheel, arrow focus and test hygiene`.
- Wheel returns unhandled when ScrollableWidth <= 0.5 (short window scrolls vertically as usual).
- A focused arrow that disables itself hands focus to the other arrow if enabled, else to the first/last card.
- Arrows stay Collapsed until the first picture has a height (valid top margin).
- Keyboard test restores InputModality.IsKeyboard in finally; stale comment block in ViewSmokeTests.UiV2FullHd.cs trimmed to the current rule.
- RED (filter ViewSmokeTests.Displays_|Keyboard_Tab_ToCard): 2 failed (`Displays_MouseWheel_OnAShortWindow_ScrollsVerticallyAsUsual`: wheel swallowed; `Displays_Arrow_DisablingAtTheEnd_KeepsKeyboardFocusInTheView`: focus left the view). Same filter GREEN: 7 passed.
- Full run (ViewSmokeTests | UiV2Guards | AdaptiveCardPanelTests | AdaptiveGridLayoutTests): Passed 461, Failed 0.
- Note: the wheel test asserts Handled == false and HorizontalOffset 0 (a synthetic wheel event does not scroll the host).

## Fix round 3
- `fix(styles): move the vertical overlay scroll viewer style out of Theme.xaml`: Style.ScrollViewer.VerticalOverlay now lives in Resources/Styles/Lists.xaml (same key/behaviour; its inner Style.ScrollBar.Thin reference became DynamicResource because Theme.xaml merges after the Styles). UiV2StylesTests (unchanged) pass.
- `fix(main-window): displays scroller is as tall as its cards`: DisplaysScroll VerticalAlignment=Top. New test Displays_Scroller_IsAsTallAsItsCards_AtFullHd_AndFillsTheRowOnAShortWindow was RED (scroller 580.98 vs cards 451.09), now GREEN: scroller height = cards + 16 px bar gutter, bar directly under the cards, arrows still centred on the picture (2 px), short window 1008x615 fills the available row height.
- Run (ViewSmokeTests | UiV2Guards | UiV2StylesTests | AdaptiveCardPanelTests | AdaptiveGridLayoutTests): Passed 532, Failed 0.
- `docs(ui-round3): refresh screenshots after the scroller height fix`: 4 PNGs regenerated. after-main-1920-5displays.png: the horizontal bar now sits right under the cards (about 10 px), arrows centred on the pictures, left arrow dimmed at start. Free space is now empty below the scroller.

## Final-review fix wave
Commit `fix(main-window): resizing keeps the same card at the left edge`.
- RED: Displays_Resize_WhileScrolled_KeepsTheSameCardAtTheLeftEdge. First it exposed a real bug: after a window resize the carousel cards were NOT re-planned (extent stayed 6484 at a 1368 viewport) because the panel only re-measured on the scroller's SizeChanged, when ViewportWidth was still stale. AdaptiveCardPanel now also re-measures on ScrollChanged with a ViewportWidthChange (carousel only). Then RED was "left card index 3.907, expected 3".
- GREEN: VisualRoutingView remembers the left card index (updated on user/programmatic scrolls), flags a viewport-width change, and after the row is re-planned (extent change) scrolls to index * newStep (clamped). Not applied during ordinary scrolling.
- Minor: arrow test uses Assert.False(left.IsEnabled); new Displays_FewAt1920_NoArrows_RowCentred (1 and 3 displays: both arrows Collapsed, left and right margins within 2 px).
- Run (ViewSmokeTests | UiV2Guards | UiV2StylesTests | AdaptiveCardPanelTests | AdaptiveGridLayoutTests): Passed 535, Failed 0.
