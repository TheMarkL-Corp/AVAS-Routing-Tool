### Task 2: Consistent action bar ("More layouts", "Copy layout", power)

**Files:**
- Modify: `src/AvasRoutingTool/Views/DisplayCardView.xaml` (action bar, lines 132-246), `src/AvasRoutingTool/Resources/Styles/DisplayCard.xaml`, `src/AvasRoutingTool/Resources/Styles/Buttons.xaml` (`Style.Button.ActionBar`; retire `Style.Button.LayoutDropdown` and `Style.Button.CopyLayout` if nothing else uses them), `src/AvasRoutingTool/Resources/Tokens.xaml` (`Size.Space.Action` 8, `Size.Action.PadX` 12)
- Test: `tests/AvasRoutingTool.Tests/ViewSmokeTests.UiV2ActionBar.cs`; update `ViewSmokeTests.UiV2LayoutButton.cs` and any test that names the old 64 x 56 Layout segment

**Interfaces:**
- Consumes: `MaterialIcon` from Task 1 (`grid_view`, `expand_more`, `content_copy`, `power_settings_new`).
- Produces: `Style.Button.ActionBar` (ghost, `MinHeight` `Size.Touch.Min`, `Padding` `Size.Action.PadX`,0, icon 20 + 8 gap + label, primary text colour, focus ring). Buttons keep their `x:Name`s `LayoutButton`, `CopyLayoutHandle`, `TurnOffButton` (existing handlers and tests rely on them).

- [ ] **Step 1: Write the failing tests:** `ActionBar_LayoutAndCopy_ShareHeightAndPadding` (both 48 high, equal horizontal padding, gap between them equals `Size.Space.Action`); `ActionBar_LayoutButton_SaysMoreLayouts` (visible text "More layouts", chevron after it, `AutomationProperties.Name` "More layouts for <device>"); `ActionBar_FitsA400pxCard_AtEveryDpi` (card 400 wide at 100 and 150 % DPI: no label ellipsized, power button inside the card); `ActionBar_LayoutMenuStillOpens_OnTapAndKeyboard` (existing behaviour kept).
- [ ] **Step 2: Run to verify fail** (filter `ViewSmokeTests|UiV2Guards`). Expected: new tests FAIL.
- [ ] **Step 3: Implement** the style and the XAML change (remove the segment fill and outline `Border`s, keep the click and keyboard handlers); update the old tests' expectations only where they assert the old 64 x 56 segment, renaming those that name it.
- [ ] **Step 4: Run to verify pass**, including `UiV2Guards` and `UiV2StylesTests`.
- [ ] **Step 5: Commit** `feat(display-card): More layouts and Copy layout share one ghost action-bar style`.

