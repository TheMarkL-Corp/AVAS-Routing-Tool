# Tasks 3 and 4 report: Material icons on the Main Window scope

Status: DONE_WITH_CONCERNS (concerns are small and listed at the end). Nothing pushed or merged.

Commits (branch claude/ui-round3-6710d5):
- fccbe65 feat(display-card): Material icons
- 1add879 feat(main-window): Material icons on source cards, app bar, arrows and banners

## Task 3 (DisplayCardView)

RED: DisplayCard_UsesNoFluentOrHandDrawnIcons and DisplayCard_IconSizes_FollowTokens both failed (Failed: 2, Passed: 0; no Material edit/info icons, SymbolIcon present).
GREEN: after the change, ViewSmokeTests + guard filter set: 617 passed, 3 failed (the RenamePencil tests, whose shared helper still asserted a SymbolIcon). Helper made tolerant for Task 3 (Material or Fluent edit), then
`RenamePencil|DisplayCard_` filter: 77 passed, 0 failed. Task 4 tightened the helper to MaterialIcon only.

Changes:
- Views/DisplayCardView.xaml: header edit/info, CopyTargetLock (x:Name kept, MaterialIcon `lock`, Size.Icon.L), layout-menu check (`check`, S), both Icon.Monitor placeholders (`desktop_windows`).
- Resources/Styles/DisplayCard.xaml: Style.Icon.State converted to a MaterialIcon style (L, accent Foreground). Deleted Style.Icon.Button, Style.Icon.Button.Stroke, Style.Icon.Power (grep: no other user in src or tests).
- Tests: new tests above in ViewSmokeTests.UiV2DisplayCard.cs.

## Task 4 (everything else in scope)

RED: MainWindowMaterialIconsTests, 8 tests, Failed: 8, Passed: 0.
GREEN: full filter set (ViewSmokeTests, UiV2Guards, UiV2StylesTests, UiV2TokenTests, MaterialIconTests, MainWindowMaterialIconsTests, UiV2AppBarTests, UiV2IconFontTests, MainWindowHeaderTests, AdaptiveCardPanelTests, plus TestResourcesTests): Passed 630, Failed 0. (First GREEN attempt had 4 failures from pinned pixel baselines, see below; fixed.) No STA hang occurred.

Files:
- src: MainWindow.xaml (theme, Refresh, Settings, Clear view, Open log folder; xmlns:controls added), Views/SourceCardView.xaml (edit, info, sync, stop, play_arrow, four videocam placeholders; xmlns:controls added), Views/VisualRoutingView.xaml (section videocam / desktop_windows, chevron_left/right, warning, close), Resources/Styles/Buttons.xaml (Style.Icon.Action and Style.Icon.ThemeToggle now MaterialIcon styles; ThemeToggle sets Kind dark_mode, DataTrigger IsLightMode sets light_mode), Resources/Styles/Cards.xaml (Style.Icon.Section, Style.Icon.Placeholder to MaterialIcon styles), Resources/Icons.xaml (removed Icon.Monitor, Video, Grid, Copy, Power), Resources/Tokens.xaml (removed Size.Layout.Button.Width/Height, refreshed comments).
- tests: new MainWindowMaterialIconsTests.cs; edited the existing tests below.

The theme toggle is set in the Style (Buttons.xaml), not in code-behind; no code-behind set icons. Style.Button.CarouselArrow needed no change (the icon is the button content). The rename dialog and Logs view have no Fluent icons beyond the Logs toolbar buttons (done); rename banner is in VisualRoutingView (done).

New tests (MainWindowMaterialIconsTests): MainWindow_VisualRouting_HasNoFluentSymbolIcons, AppBar_Icons_AreTheMaterialSettingsSyncAndThemeKinds, LogsToolbar_Icons_AreMaterialBroomAndFolder, AppBar_ThemeToggle_SwapsSunAndMoonKinds, SourceCard_StartStop_UsePlayAndStopKinds, Arrows_AreMaterialChevrons, SectionHeaders_AndBanner_UseMaterialIcons, EveryKind_UsedInTheMainWindowScope_HasAMaterialGeometry (greps Kind="..." / Setter Kind in the scope XAML, styles and code-behind, asserts MIcon.<kind> exists).

## Existing tests changed (and why)

- ViewSmokeTests.UiV2DisplayCard.cs: DisplayCard_Frame_IsAnUiCard_AndTheHeaderIsMadeOfBadgesAndIcons (info is now Kind "info"); DisplayCard_BodyOverlays_ShareTheOverlayStyle_AndTheLockIsASymbolIcon renamed to ...AndTheLockIsAMaterialIcon (Kind lock, Width L); DisplayCard_PictureStates_... (state-style check looks for MaterialIcon instead of Path); DisplayCard_UndoToast_UsesTheTokens_AndLayoutMenuMarks... (check mark is MaterialIcon "check").
- ViewSmokeTests.UiV2Rename.cs: AssertRenamePencil and IconColour use a new PencilIconOf helper (MaterialIcon "edit", Width S); RenameBanner_ShowsTheReason_WithAWarningIcon... (Kind warning) and its Dismiss assertion (Kind close).
- ViewSmokeTests.UiV2SourceCard.cs: footer test (Kinds play_arrow / sync / stop instead of Fluent symbols; AssertFooter signature now string Kind), SourceCard_StreamingShowsStop..., SourceCard_StopStreamButton_IsTheGhostButton..., SourceCard_InfoBadge_IsAFocusableInfoIcon..., SourceCard_InfoBadge_TakesAPress..., SourceCard_InfoBadge_IconCentreDoesNotMove. Baselines: FullTileHeight 278.4 -> 276.96 (footer row now sits on its 36 px minimum because the 22 px Material icon is 1.44 px shorter than the Fluent glyph box; this also changes SourceCard_TileHeight_IsTheLargerTileHeight... and RenamePencil_TileHeight_IsStill278... which use the constant); InfoBadgeIconCentreY 25.72 -> 25.0 (16 px Material box is exactly centred; X unchanged).
- ViewSmokeTests.UiV2SourceStrip.cs: refresh button asserts Kind "sync" and no text at all.
- ViewSmokeTests.UiV2VisualRouting.cs: section icons are found as MaterialIcon (Style.Icon.Section).
- UiV2AppBarTests.cs: AppBar_AtFullHd_ShowsLabels_SymbolIcons_... renamed ..._MaterialIcons_...; Check_ActionIcons_AreSymbolIcons_WithAGlyph_... renamed Check_ActionIcons_AreMaterialIcons_WithAKind_AndTheButtonColour (Kind resolves to MIcon, no SymbolIcon, Foreground same as button); TheThemeIcon_NamesTheActiveTheme_SunInLightMoonInDark now uses MaterialIcon Kinds dark_mode / light_mode.
- MainWindowHeaderTests.cs: Actions_AreGhostButtons_WithVectorIcons accepts MaterialIcon (instead of SymbolIcon).
- UiV2IconFontTests.cs: only the summary comment (data and checks untouched; it still renders every Fluent symbol, so coverage of out-of-scope views is intact).
- UiV2TokenTests.cs: removed the two Size.Layout.Button.* rows (token deleted; nothing else used it).
- TestResourcesTests.cs: Ensure_ResolvesAppKeysWpfUiKeysAndIcons asserted Icon.Power; now asserts Icon.Logo.AChevron (Icon.Power deleted, unused).

## Left on Fluent in scope

Nothing in the Main Window scope. grep of the scope XAML finds no ui:SymbolIcon. Untouched out-of-scope: Composer, RoomCameras, PtzPad, Advanced, SettingsDialog, CameraEditorDialog, StepHeader (Composer), BrandMark and LayoutSchematic. No icon was missing from the 22.

## Concerns / notes

- Pixel baselines moved (tile height -1.44 px, info icon centre Y -0.72 px); both are Material icons being exact boxes. Worth a glance at screenshots in Task 5.
- Style.Icon.AppBar (Path style) and Icon.Settings/ArrowRight/Close/Lock/Refresh/Sun/Moon geometries remain; Style.Icon.AppBar is asserted by UiV2StylesTests and the others look unused but were outside the listed deletions, so I left them. A cleanup candidate.
- The "no Path icon" check ignores Paths inside templates (TemplatedParent != null), so the check relies on the XAML scan for template content.
- A bash heredoc quirk in this environment forced me to run edits through Python scripts in the scratchpad; no repo effects.

## Fix round 1

UiV2LogsViewTests.LogsTab_IsVirtualized_Tokenised_TouchSafe_AndColoursTheLevels (Check_TheToolbar_HasTouchSizedButtons_WithVectorIcons_AndReadableText) looked for the old SymbolIcon in the Logs toolbar. Updated deliberately: it now finds the one MaterialIcon per button and asserts Kind cleaning_services (ClearLogsButton) and folder_open (OpenLogFolderButton), no SymbolIcon, touch height and readable text unchanged. Grep of tests for other SymbolIcon leftovers: only vacuous glyph filters (`FindAll<SymbolIcon>(view).All(...)` in SourceCard, SourceCardCompact, DisplayCard) and out-of-scope Composer/PtzPad/StepHeader/IconFont uses; no change needed. Run UiV2LogsViewTests|ViewSmokeTests|UiV2AppBarTests: 386 passed, 0 failed.
