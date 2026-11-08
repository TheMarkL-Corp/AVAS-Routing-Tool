# Task 2 report: consistent action bar

Status: DONE_WITH_CONCERNS

## RED (before any production change)
`dotnet test ... --filter "FullyQualifiedName~ViewSmokeTests.ActionBar_"` -> `Failed! Failed: 2, Passed: 5, Total: 7`
- FAIL `ActionBar_LayoutButton_SaysMoreLayouts` (Assert.Equal strings differ: old "Layout for ..." name / no label)
- FAIL `ActionBar_LayoutAndCopy_ShareHeightAndPadding` (no Style.Button.ActionBar / Size.Action.PadX token)
- PASS (regression guards that already held before the change): `ActionBar_FitsA400pxCard_AtEveryDpi` (both DPI cases, old 64 px segment fit), `ActionBar_LayoutMenuStillOpens_OnTapAndKeyboard`.

## GREEN
Filter ViewSmokeTests | UiV2Guards | UiV2StylesTests | MaterialIconTests | AdaptiveCardPanelTests, BaseOutputPath bin/r7/:
`Passed! Failed: 0, Passed: 528, Skipped: 0, Total: 528, Duration 3 m 18 s`
(An earlier run in this session hung with no output for >14 min on the known STA hang; killed testhost and reran.)
Intermediate runs: 22 failed (old-design tests), 3 failed (AssertTextFits on a collapsed label), then 0.

## Files changed
- src/AvasRoutingTool/Resources/Tokens.xaml: `Size.Space.Action` 8, `Size.Action.PadX` 12, Thickness `Size.Action.Padding` 12,0 and `Size.Space.Action.Before` 8,0,0,0 (Margin/Padding cannot take a Double).
- src/AvasRoutingTool/Resources/Styles/Buttons.xaml: new `Style.Button.ActionBar` (ghost, transparent, no border, hover/pressed `Brush.Surface.Hover`, MinHeight Touch.Min, padding 12, SemiBold, primary text brush, own 2 px focus ring via InputModality.ShowsFocus, parts bd/FocusRing).
- src/AvasRoutingTool/Resources/Styles/DisplayCard.xaml: retired `Style.Button.CopyLayout`, `Style.Button.LayoutSegment`, `Style.Button.LayoutDropdown` (nothing else referenced them; grep src + tests). Added `Style.Button.ActionBar.Copy` (BasedOn ActionBar; armed cyan and nothing-to-copy dimming as style triggers) and `Style.Text.ActionBar.LayoutLabel`.
- src/AvasRoutingTool/Views/DisplayCardView.xaml: LayoutSegments grid, fill and outline Borders removed; LayoutButton = [MaterialIcon grid_view][More layouts][expand_more]; CopyLayoutHandle = [content_copy][Copy layout], margin = Size.Space.Action; TurnOffButton uses MaterialIcon power_settings_new (20 px), primary text colour; DraftChip margin = Size.Space.Action. AutomationProperties.Name "More layouts for {DeviceName}", tooltip "More layouts". All x:Names and handlers kept (new: LayoutLabel, LayoutChevron).
- tests: new ViewSmokeTests.UiV2ActionBar.cs; updated UiV2LayoutButton, UiV2SegmentedPresets, UiV2NoInlinePresets, UiV2DisplayCard, DisplayCard.

## Tests changed and why
ViewSmokeTests.UiV2LayoutButton.cs (rewritten, summary says round 3):
- `LayoutButton_IsTheTokenSize_64x56_AndBothAreAtLeastTheTouchFloor` -> `LayoutButton_IsAtLeastTheTouchFloor_AndAsHighAsTheActionBarButtons` (48 high, no 64x56).
- `LayoutFill_AndOutline_WrapTheLayoutButtonExactly` -> `LayoutButton_HasNoFillOrOutline_AtRest_AndNoSegmentGroup`.
- `LayoutButton_TakesAPressAtItsCornersAndCentre_OnTheButton` kept.
- `LayoutIconAndChevron_AreTheIconTokens_InsideTheButton_AndCentred` -> `LayoutIconAndChevron_AreTheIconToken_InsideTheButton_OnOneLineWithTheLabel` (MaterialIcons, 8 px gaps, 12 px pads).
- `ActionBar_WithTheTallerLayoutButton_FitsAtBothWidths_WithAnEightPixelGap` -> `ActionBar_Buttons_FitAtBothWidths_WithTheActionGap` (gap == Size.Space.Action).
- `ActionBar_WithADraft_KeepsTheEightPixelGap_..._AtTheNarrowWidth` -> `..._KeepsTheActionGap_..._AtTheCarouselWidth` (400 instead of Size.Card.Min 320).
- `LayoutIcon_AndOutline_DrawInThePaletteOfTheTheme` -> `LayoutIconAndChevron_DrawInThePrimaryTextColourOfTheTheme` (icons, label, power: Brush.Text.Primary, both themes).
- `LayoutGroup_FillsTheActionBarRow_AndTheRowIsStillTheActionBarToken` -> `ActionBarRow_IsStillTheActionBarToken_AndTheLayoutButtonIsCentredInIt`.
ViewSmokeTests.UiV2SegmentedPresets.cs:
- Removed `LayoutSegments_HoldTheLayoutDropdown_...` and `LayoutSegments_HaveOneOutline_AndNoSegmentBorder` (the segment is gone; covered by the tests above).
- `LayoutSegments_TheLayoutButton_TakesAPressOnItsWholeArea_IncludingTheOuterRing` -> `LayoutButton_TakesAPressOnItsWholeArea_IncludingTheEdges`.
- `LayoutButton_IsAnIconAndAChevron_NoText_WithTheMoreLayoutsTooltip` -> `LayoutButton_ShowsIconLabelAndChevron_WithTheMoreLayoutsTooltipAndName` (also 400 px width).
- `CopyLayout_IsAGhostButtonOutsideTheSegments_WithIconAndLabelSideBySide` -> `CopyLayout_IsAnActionBarGhostButton_WithIconAndLabelSideBySide` (style ActionBar.Copy based on ActionBar; MaterialIcon content_copy).
- `CopyLayout_WhenThereIsNothingToCopy_IsEnabledAndFocusable_ButLooksOff`: dimming/cursor now read on the button (style trigger), not the template root.
ViewSmokeTests.UiV2NoInlinePresets.cs: `ActionBarWidths()` = 613 and 400 (was Size.Card.Min 320); `DisplayCard_ActionBar_StartsWithLayout_...` asserts Layout and Copy are 48 high instead of 64x56 and name "More layouts for ..."; `AssertTheActionBarFits` skips collapsed labels.
ViewSmokeTests.UiV2DisplayCard.cs `DisplayCard_ActionBar_HasTouchSizedButtons_...`: power icon is MaterialIcon power_settings_new at 20, Layout has grid_view + expand_more MaterialIcons.
ViewSmokeTests.DisplayCard.cs: automation names "More layouts for ..."; `..._NothingInTheCardClips` skips collapsed labels.

## Concerns
1. A draft does not fit in a 400 px card (inner 374 px): More layouts 163 + 8 + Copy layout 128 + 8 + chip 108 + 48 > 374 (about 80 px over); wrapping the chip cannot recover that. So while HasDraft is true the "More layouts" label collapses (icon + chevron remain, name/tooltip/menu unchanged) via `Style.Text.ActionBar.LayoutLabel`; without it the power button was pushed out of the card and 11 existing draft tests failed. This departs from "one consistent label"; needs a design decision (alternatives: draft chip into the header row, or a shorter chip).
2. Cards narrower than 400 (Size.Card.Min is still 320, tests used it for the action bar) cannot fit the new bar (needs about 350 px); I moved the action-bar width tests to 400 per the carousel rule. If the app can still lay out 320 px cards before the carousel lands, the bar clips there.
3. "150 % DPI" is emulated with a LayoutTransform(1.5) around a 400 DIP card; WPF layout is in DIPs so this mostly proves nothing changes with scale. Real per-monitor-DPI rendering was not tested.
4. Focus ring: `Style.Button.ActionBar` uses its own template ring (InputModality.ShowsFocus, same as Ghost/CopyLayout, and `AssertShowsFocus` is pixel-based on the element) with FocusVisualStyle null to avoid a double ring, rather than `Style.FocusVisual.Ring`; same 2 px `Brush.Focus.Ring` visual.
5. Copy layout is not literally `Style.Button.ActionBar`: it is `Style.Button.ActionBar.Copy` (BasedOn) because its armed and nothing-to-copy states must not apply to the Layout button (same DataContext).
6. Left unused but not removed: `Style.Icon.Button`, `.Stroke`, `.Power`, `Icon.Grid/Copy/Power` geometries and tokens `Size.Layout.Button.*` / `Size.Icon.Layout.Chevron` (UiV2TokenTests asserts them; later tasks may own cleanup).

## Fix round 1 (QA findings 1-3)
RED (new tests, before the change): `Failed: 3, Passed: 8` of the 11 ActionBar_/DisplayCards_ tests: `ActionBar_WideCardWithADraft_KeepsTheMoreLayoutsLabel_...` (613 px with a draft lost the label), `ActionBar_ResizingA400CardWithADraftTo613_BringsTheLabelBack_AndBackAgain` (label did not come back), `DisplayCards_AreNeverNarrowerThanTheBarNeeds_InAnyLayoutThePanelCanProduce` (MinCardWidth 320). `ActionBar_Card400WithADraft_CollapsesTheLabel...` and `..._Card400WithoutADraft_KeepsTheLabel` already held with the old HasDraft trigger.
GREEN: filter ViewSmokeTests|UiV2Guards|UiV2StylesTests|UiV2TokenTests|MaterialIconTests|AdaptiveCardPanelTests|AdaptiveGridLayoutTests: `Passed! Failed: 0, Passed: 599, Total: 599`.
1. Label: the HasDraft DataTrigger is gone. `DisplayCardView.UpdateLayoutLabel()` (on dock SizeChanged, Loaded, and HasDraft/IsRouted changes) measures More layouts + Copy layout + DraftChip + power (with margins) with the label visible and collapses it only if HasDraft and the need exceeds the dock width; no draft or enough room keeps it. The decision never depends on the label's current state, so no flip-flop. Name and tooltip are on the button and stay.
2. `GridSpec.DisplayCard` MinCardWidth = 400; new token `Size.Card.Display.Min` 400 used by DisplayCardView MinWidth (`Size.Card.Min` 320 stays for GridSpec.Default / other users); Tokens.xaml comment updated. Tests that used Size.Card.Min for the display card now use the display token (UiV2Rename x2, DisplayCard x2, UiV2DisplayCard, UiV2NoInlinePresets comment); UiV2TokenTests lists the new token and checks it equals GridSpec.DisplayCard.MinCardWidth. AdaptiveGridLayoutTests needed no change. New `DisplayCards_AreNeverNarrowerThanTheBarNeeds_...` samples counts 1-12, widths 1008-3840, heights 300-inf: every plan >= 400, MinWidth 400, bar fits at 400.
3. `ActionBar_FitsA400pxCard_AtEveryDpi` (Theory with ScaleTransform) replaced by `ActionBar_FitsA400pxCard` plus a comment that DPI is not testable in DIP layout (needs a manual/screenshot check).
Changed/new tests: UiV2ActionBar (+4 label tests, +1 width test, DPI test renamed), UiV2TokenTests, UiV2Rename, DisplayCard, UiV2DisplayCard, UiV2NoInlinePresets.
