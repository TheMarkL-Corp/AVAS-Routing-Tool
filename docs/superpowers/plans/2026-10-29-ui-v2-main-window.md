# UI v2 Revamp, Session A: Main Window Plan (revision 2)

Status: **planning only** (no source, test or git changes made by this session). Branch context: `ui-v2/integration` (tag `pre-ui-v2`; `main` untouched).
Binding input: `docs/superpowers/specs/2026-10-29-ui-v2-master-guidelines.md` **section 0 (D1-D12, locked, overrides everything)**, then G1-G5 and sections 3-6. Also read: `docs/superpowers/plans/2026-10-29-ui-v2-multiview-composer.md` (session B).
Scope (files this plan owns): `MainWindow.xaml(.cs)`, `Views/VisualRoutingView.xaml`, `Views/SourceCardView.xaml`, `Views/DisplayCardView.xaml`, `Views/SettingsDialog.xaml(.cs)`, the Logs tab, deleting `Views/LayoutsBarView.xaml(.cs)` (D5). Out of scope: `MultiViewComposerView`, `LayoutSchematic`, `CompactSourceCardView` (B); `App.xaml`, csproj, `Resources/**`, `ThemeService`, `Controls/*` (foundation / B). View-model changes are additive read-only view-state properties only (A3, A4).

## 0. What changed from the first draft

| Draft | Revision 2 (locked decision) |
|---|---|
| `ui:FluentWindow` + `ui:TitleBar`, app bar folded into the title bar (T4-T6) | **Dropped (D1).** Plain `Window` with normal Windows chrome; the existing app bar (underline tabs, connection readout, actions) stays. |
| `ui:Button` for app bar, card actions, source footer | **Dropped (D12).** Native `Button` / `RadioButton` with central `Style.Button.*` and `Style.Tab.Nav` (template part `bd` kept). WPF-UI is used for `ui:Card`, `ui:ToggleSwitch`, `ui:TextBox`, `ui:Badge`, `ui:SymbolIcon` only, and only where no test is coupled to the old element. |
| Ratchet budgets in `UiV2MainWindowGuardTests` | **Hard guards (D6)** with ONE justified allowlist file; written first and red, green per migrated file (task A1). |
| Font floor 11 px, five roles | **12 px floor, tokens 16/14/13/12 (D8)**; guard bans `FontSize` literals and anything below 12. 28 literals in my files are below 12 today. |
| Touch tokens 48 / 36 / ad hoc | **Exactly `Size.Touch.Min` 48 and `Size.Touch.Compact` 36, >= 8 px spacing (D4).** Source-card stream controls stay Compact (O2 answered). |
| `LayoutsBarView` retire as default / open question | **Delete (D5)**: view, standalone test, six absence assertions, `Handover/02` line. |
| Design for 1024x720 and maximized | **Design target 1920x1080 at 100%, maximized (D9)**; 1024x720 is a safety floor (existing tests stay). Section 2.3. |
| `ViewSmokeTests` edited by both sessions | **Foundation task zero splits it into partial files (D10).** Section 7 lists the partials A owns. |
| Single worktree-less sequencing | **Own worktree off `ui-v2/integration` after the Foundation checkpoint; Main Window is user checkpoint 2 (D7).** Section 7. |

## 1. Audit (comment-stripped counts on the working tree; master's regex also counted `<Style.Triggers>`)

| File | Lines | Inline `<Style>` | keyed `<Style>` | `<ControlTemplate>` | literal `Width`/`Height` attrs | icon glyphs / emoji | `FontSize` literals (< 12) |
|---|---|---|---|---|---|---|---|
| `MainWindow.xaml` | 369 | 6 | 3 (`Style.Tab.Nav` L19, `Style.Button.AppBar` L54, `Style.Icon.AppBar` L72) | 1 (L30) | 23 | 2 | 7 (3) |
| `VisualRoutingView.xaml` | 346 | 10 | 0 | 0 | 5 | 1 | 8 (4) |
| `SourceCardView.xaml` | 450 | 17 | 0 | 0 | 6 | 4 | 14 (6) |
| `DisplayCardView.xaml` | 1113 | 32 | 1 (`BodyOverlay` L21) | 2 (L305 draft chip, L1041 layout tile) | 26 | 4 | 36 (14) |
| `SettingsDialog.xaml` | 188 | 0 | 0 | 0 | 6 | 0 | 12 (1) |
| `LayoutsBarView.xaml` | 92 | 1 | 0 | 0 | 5 | 1 | 3 (deleted, D5) |
| **Total (excluding the deleted view)** | | **65** | **4** | **3** | **66** | **11** | **77 (28)** |

* **G1**: zero WPF-UI today. The shared button/tab/scrollbar templates live in `Theme.xaml` and `MainWindow.Resources` (not in views of session B).
* **G2 glyph inventory** (the 11): `MainWindow.xaml:315` broom emoji, `:318` folder emoji; `VisualRoutingView.xaml:289` `▾`; `SourceCardView.xaml:94` `ⓘ`, `:352` `↻`, `:372` `■`, `:392` `▶`; `DisplayCardView.xaml:123` `&#9432;`, `:234` `&#9662;`, `:556` `●`, `:1083` `✓`. (`LayoutsBarView.xaml:37` `▾` and `•` die with the file.) `·` and `…` are punctuation and stay.
* **G3**: no hex anywhere, all palette refs `DynamicResource`. About 60 of the 65 inline styles are visibility or colour state triggers on view-model flags, not appearance.
* **G4**: tokens needed for the real hardcodes: Logs columns 170/70/120 and `MaxWidth=650` (`MainWindow.xaml:311,336-338`), Settings window 480x540 and buttons 90/110 (`SettingsDialog.xaml:5,179,184`), popup `Width=336` (`DisplayCardView.xaml:1007`), card rows 24/56/64 (L62-65), icons 14-28.
* **G5**: one virtualizable list: Logs `ListBox` (`MainWindow.xaml:325`), no virtualization properties, in a `*` row (not inside a `ScrollViewer`). `MainViewModel` caps `Logs` at 1000 (`MainViewModel.cs:291`).
* **Test infrastructure**: four test files repeat the resource bootstrap (`ViewSmokeTests.cs:41`, `MainWindowHeaderTests.cs:24`, `ReleaseDefaultsTests.cs:33`, `AppVersionTests.cs:31`); WPF-UI dictionaries must be in the test `Application` or `ui:` controls render unstyled (needs the shared helper F6).

## 2. Target design

### 2.1 WPF-UI facts used (verified against the NuGet nuspec and lepoco/wpfui `main` sources, not assumed)

| Item | Verified fact |
|---|---|
| Version | `WPF-UI` **4.3.0** (latest stable), MIT, dependency group `net8.0-windows7.0` (+net9/net10/.NET Framework), only dependency `WPF-UI.Abstractions` 4.3.0. |
| Namespace / resources | `xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"`; `<ui:ThemesDictionary Theme="Dark"/>` + `<ui:ControlsDictionary/>` (`Markup/ThemesDictionary.cs`, `ControlsDictionary.cs`). `ThemesDictionary.Theme` is settable (it swaps its own `Source`). |
| Controls used | `ui:Card : ContentControl`, `ui:Badge : ContentControl`, `ui:TextBox : TextBox`, `ui:ToggleSwitch : ToggleButton` (**not a `CheckBox`**), `ui:SymbolIcon : FontIcon` (`Symbol`, `Filled`, `FontSize`; XAML `{ui:SymbolIcon Settings24}`). `ui:Button` exists but is **not used** (D12). |
| Icons | `SymbolRegular` (Fluent System Icons 1.1.316, private-use glyphs, embedded font): `WeatherSunny24`, `WeatherMoon24`, `ArrowSync24`, `Settings24`, `Broom24`, `FolderOpen24`, `Info24`, `Play24`, `Stop24`, `Checkmark24`, `ChevronDown24`, `Delete24` all exist. |
| `ApplicationThemeManager.Apply(ApplicationTheme, WindowBackdropType = Mica, bool updateAccent = true)` | Swaps its `theme` dictionary through `ResourceDictionaryManager` and returns early if no WPF-UI `ThemesDictionary` is merged. **New finding (risk R1):** on success it calls `WindowBackgroundManager.UpdateBackground(Application.MainWindow, ...)`, which calls `WindowBackdrop.ApplyBackdrop`, which calls `UnsafeNativeMethods.RemoveWindowCaption` (`SetWindowThemeAttribute` with `WTNCA_NODRAWCAPTION | NODRAWICON | NOSYSMENU`) on that window. On our **plain `Window` this would blank the native caption text, icon and system menu** (D1 wants normal chrome). `ThemeService` therefore must **not** call that overload while `Application.MainWindow` is set; it should swap the `ui:ThemesDictionary` itself (set `.Theme`) and set the accent through `ApplicationAccentColorManager.Apply(Color, ApplicationTheme, bool)`. |
| `ui:ListBox` default style | `IsVirtualizing=True` but `VirtualizationMode=Standard`, and an items host templated with `PassiveScrollViewer` (derived type: the app's implicit 16 px-gutter `ScrollViewer` style does not apply to it). A central `Style.ListBox.Virtualized` must fix both. |

### 2.2 Window shell (unchanged chrome)

```
Window (normal Windows caption, Icon, min 1024x720, maximized by release default)
+---------------------------------------------------------------------------------------------+
| [AV] AVAS ROUTING TOOL | Visual Routing | BAVM Console | (Logs) |  (o) Connected | sun Light  sync Refresh  gear Settings |
|      v1.0.0            |=================                                                    |
+---------------------------------------------------------------------------------------------+
|  Sources (TX)   2*  : AdaptiveCardPanel of SourceCards  (or CompactSourcePalette while composing)
|---------------------------------------------------------------------------------------------|
|  Displays (RX) 3.4* : AdaptiveCardPanel of DisplayCards (or MultiViewComposerView)           |
+---------------------------------------------------------------------------------------------+
```
* Structure, behaviours (`Tab_Checked`, lazy WebView2, `_leftVisualRouting` restore, `ShowLogs` visibility) and every x:Name used by tests (`AppBar`, `BrandBlock`, `BrandLogo`, `AppVersionText`, `ConnectionBadge`, `AppBarDivider`, `Tab*`, `ThemeToggleButton`, `RefreshButton`, `SettingsButton`, `ViewVisualRouting`, `ViewBavmConsole`, `BavmWebView`, `ViewLogs`) are unchanged.
* The tab `RadioButton`s and action `Button`s move to central `Style.Tab.Nav` (`Tabs.xaml`) and `Style.Button.AppBar` (`Buttons.xaml`); `MainWindow.Resources` ends empty. `bd` part name, 3 px accent underline, 48 px minimum height and the `Style.FocusVisual.Ring` focus visual are preserved because the header tests read them.
* Action icons: `ui:SymbolIcon` (`WeatherSunny24`/`WeatherMoon24` by `IsLightMode`, `ArrowSync24`, `Settings24`) instead of the three custom strokes; the brand mark stays a `StreamGeometry` composition, extracted to `Views/BrandMark.xaml` (static icon, allowlisted).
* **App-bar labels return at wide widths (D9)**: each action shows `icon + label` (`ThemeButtonLabel` "Light"/"Dark", "Refresh", "Settings") when the window is at least `Size.Breakpoint.AppBarLabels` (1400) wide, icon-only below. Implemented without code-behind: label `Visibility` bound to the `AppBar` element's `ActualWidth` through a `Converter.WidthAtLeast` with the token as `ConverterParameter`. At the 1024 floor the existing `AppBar_FitsAtMinimumWindowWidth` test must still pass (labels hidden). Tooltips and automation names are unchanged.

### 2.3 Design target 1920x1080 at 100%, maximized (D9): what is different from today

Maximized at 1920x1080 the working area is 1920x1040 (taskbar), caption 31, app bar 57: content about **1920x952** (today's tests use 1264x755 and 1008x615). The overview rows give Sources about 350 px and Displays about 596 px of height. Real numbers from `AdaptiveGridLayout` with the current `GridSpec` (estimates; task A13 step 1 measures the real window and records them):

| Case at 1920x1080 | Today (estimate) | Why |
|---|---|---|
| 3-4 sources | 4 cards of 393 px, preview 373x210, centred in one row | height-bound: the Sources row is already at the 320 px minimum-card limit; `MaxCardWidth` 420 is not reached |
| 1-2 displays | cards 702 px wide, preview 676x380, centred, about 470 px of unused width for two | height-bound; `MaxCardWidth` 760 is not reached |
| 3 displays | 613 px, preview 587x330 | width-bound |
| 4 displays | 456 px, preview 430x242 | width-bound |
| 6 displays (or 6 sources) | no arrangement reaches 320 px: falls back to minimum cards with vertical scroll | by design |

Findings: **at Full HD the overview is bound by height, so raising `MaxCardWidth` alone changes nothing.** What v2 does with the space (all testable):
1. **Labels back** on the theme/Refresh/Settings actions (section 2.2), plus the connection readout keeps its full text.
2. **Tokenised card chrome** so the 170 px display chrome is a single sum of named row tokens (guarded), and the `GridSpec` caps become `DisplayCard` 960 / `SourceCard` 480 so a taller or more spacious window (portrait, larger screen) is not capped. No effect at 1080p, no regression at the floor.
3. **12 px text floor** and Lead 16 / Title 14 / Body 13 / Caption 12: larger overlay and chip text; this is the main visible change at Full HD (overlay widths are the risk R5).
4. **Not done, needs a decision (O1 below):** using the unused horizontal slack by moving the Sources into a left rail when the window is at least about 1600 wide would free about 350 px of height for the displays (display preview about 676x380 -> about 1000x560 for two displays). That is a layout change, not a restyle; the plan does not include it.

At the 1024x720 floor nothing is optimised: all existing floor tests stay (`DisplayCard_InTheOverview_AtRealWindowSizes_...`, `Composer_InVisualRoutingView_AtRealWindowSizes_...`, `AppBar_FitsAtMinimumWindowWidth`), and the 12 px floor must not make them fail (the tests are the judge).

### 2.4 WebView2 (BAVM Console tab)

With the plain `Window` kept (D1) nothing structural changes: the island sits in its own `Grid` row, no WPF-UI control overlaps it, and ToolTips/Popups are top-level windows. Rule for the plan: nothing of ours (toast, banner, `ui:Snackbar`, `ui:ContentDialog`) may be drawn over the Console tab. WPF-UI's `ControlsDictionary` has no effect on `WebView2`. The `ThemeService` caption risk (R1) is the only WPF-UI interaction with the window itself.

### 2.5 Views with WPF-UI where no test is coupled

| Element | Tests that touch it | Decision |
|---|---|---|
| Card frames (`SourceCardView` outer `Border`, `DisplayCardView.CardFrame`) | `CardFrame` is only used as a `FrameworkElement` for bounds (`ViewSmokeTests` 1753, 1769, 1833, 2116, 2199) | `ui:Card` with `Style.Card.Source` / `Style.Card.Display` (stretch content, our tokens, state border via `Tag`). Fallback `Border` + same style if the 16:9 measurement test fails (R2). |
| DUAL LINK / ONLINE / OFFLINE / mode badge / DISCOVERED count / "in N blocks" | tests read their `TextBlock` text and `Foreground` (`OFFLINE` secondary brush 576), `ModeBadge`/`ModeBadgeText` names (141) | `ui:Badge` with central `Style.Badge.*` where the content is plain text over chrome. Chips lying **over video** (`UsedInBlocksBadge`, "Placing", "TAP OR DRAG", active-source tag) keep `Border` with explicit token attributes (same call as session B). |
| Settings fields and toggle | `ReleaseDefaultsTests` casts `ChkShowLogs` to `CheckBox` | `ui:TextBox` (x6), `ui:ToggleSwitch` (test updated to `ToggleButton`) |
| All stock glyphs | header test wants a vector icon | `ui:SymbolIcon` (test accepts `Path` or `SymbolIcon`, still rejects emoji) |
| Buttons and tabs | `bd` part, `FocusVisualStyle`, `Template` | **native**, central styles (D12) |

### 2.6 Logs and Settings

* **Logs**: toolbar of two native buttons on `Style.Button.Secondary` + `ui:SymbolIcon` (`Broom24` "Clear view", `FolderOpen24` "Open log folder"), `Size.Touch.Min` high, text >= 12; `ListBox` on `Style.ListBox.Virtualized` with `Style.Text.Mono` and tokenised column widths. Level filter / follow-tail / status feedback remain out of scope (still open in `active_context.md`).
* **SettingsDialog**: still a `Window` with normal chrome and `ResizeMode=NoResize`; `SizeToContent="Height"`, width from `Size.Dialog.Settings.Width`, `MaxHeight` = primary screen height (the existing `ScrollViewer` takes over if needed); `ui:TextBox` fields on `Style.TextBox.Field` (`Size.Touch.Min` high, now with `AutomationProperties.Name`, which today's unlabeled boxes lack), `ChkShowLogs` as `ui:ToggleSwitch`, Cancel/Save on `Style.Button.Secondary` / `Style.Button.Primary` with `MinWidth` tokens.

## 3. Style / icon migration map

### 3.1 Keyed styles and templates (all leave the view files)

| Today | Target key | Dictionary | Notes |
|---|---|---|---|
| `MainWindow.xaml:19` `Style.Tab.Nav` (+ `ControlTemplate` L30) | `Style.Tab.Nav` | `Tabs.xaml` | part `bd`, underline, `MinHeight=Size.Touch.Min`, `MinWidth=Size.Tab.MinWidth`, ring via `Style.FocusVisual.Ring` |
| `:54` `Style.Button.AppBar` | `Style.Button.AppBar` | `Buttons.xaml` | ghost, 48 px, label hidden below the breakpoint via the content, not the style |
| `:72` `Style.Icon.AppBar` | deleted | - | `ui:SymbolIcon` inherits the button foreground |
| `DisplayCardView.xaml:21` `BodyOverlay` (+3 BasedOn users) | `Style.Overlay.Body` | `Overlays.xaml` | |
| `:305` draft chip `ControlTemplate` | `Style.Button.Chip.Draft` | `Resources/Styles/DisplayCard.xaml` (A-owned) | keeps `InputModality.ShowsFocus` trigger. Renamed from `Chip.Staged`: **B's plan already uses `Style.Button.Chip.Staged` and `Style.Button.LayoutTile` in `Composer.xaml`** (section 8 conflict list). |
| `:1041` layout-tile `ControlTemplate` | `Style.Button.LayoutMenuTile` | `DisplayCard.xaml` | keeps part `bd` (tests 1929); the `IsRouted` ancestor binding is replaced by a per-tile `IsOnScreen` (A4) |

### 3.2 Inline (unkeyed) styles

| Pattern | Occurrences | Replacement |
|---|---|---|
| Show/hide on one flag | VisualRoutingView 83,115,143,201,227,244,336; SourceCard 109; DisplayCard 136,386,422,449,676 and similar | `Visibility="{Binding X, Converter={StaticResource Converter.Visible.True}}"` etc. (the converter instances of F4; session B's `VisibilityConverter` with `Rule` is the shared implementation, see section 8) |
| Show/hide on 2+ flags | SourceCard Start 394, standby 210, unsupported 177, usage chips 249/279/297; DisplayCard preview states 490/564/605/635, toast 913, notice lines 724/745/766 | new read-only view-model properties (A3/A4): `SourceCardViewModel.ShowRefresh/ShowStop/ShowStart/ShowStandby/ShowUnsupported/HasUsage/UsedInText/ShowTapHint/StreamStatusText`; `DisplayCardViewModel.HasRoutedBadge/ShowSingleViewPicture/ShowRoutedNonAvasNotice/ShowUnroutedPlaceholder/ShowUndoToast`, tile `IsOnScreen`, `MainViewModel.IsComposing`; truth tables tested against the trigger they replace |
| Colour by state | status dots (MainWindow 215, SourceCard 55, DisplayCard 84), badges (SourceCard 132/145, DisplayCard 155/174), card borders (SourceCard 24, DisplayCard 35), log level (MainWindow 344), rings (SourceCard 421/439, DisplayCard 966/986) | central styles reading `Tag` (plain `Trigger Property="Tag"`; colours stay `DynamicResource Brush.*`): `Style.StatusDot`, `Style.Badge.*`, `Style.Card.Source/Display`, `Style.Text.LogLevel`, `Style.Ring.Armed/Focus` |
| `RowDefinition` height by composer | VisualRoutingView 23 | `Converter.BoolToGridLength` on `IsComposing`; if binding a `RowDefinition` proves unreliable, ONE allowlisted `RowDefinition.Style` |
| `ItemContainerStyle` for `FractionalLayoutPanel` | DisplayCard 402 | allowlisted, central key `Style.Slot.Fractional` in `DisplayCard.xaml` |

### 3.3 Typography (D8: floor 12, tokens 16/14/13/12; guard bans `FontSize` literals)

| Style (`Text.xaml`) | Size | Replaces |
|---|---|---|
| `Style.Text.Lead` | 16 | 15-18 px: brand, arm banner, Undo label, dialog title |
| `Style.Text.Title` | 14 | 14 px: card/menu titles, field titles |
| `Style.Text.Body` | 13 | 13 px: buttons, status text |
| `Style.Text.Caption` | 12 | 9-12 px: footers, chips, hints, overlays (the 28 literals below 12 become 12); includes the Logs toolbar 11 px text |
| `Style.Text.Mono` | 12 | Logs (Consolas) |

Overlay texts over video keep an **explicit `Foreground` attribute** (`ThemeResourceUsageTests.VideoOverlayText_UsesOnPreviewOrOnScrimTokens` reads it); those two `FontSize`-selected rows (`ActiveSourceName` 10 and 13) become `x:Name`-selected.

### 3.4 Icons (D3)

| Old | New | File:line |
|---|---|---|
| broom / folder emoji | `ui:SymbolIcon Broom24` / `FolderOpen24` | MainWindow 315 / 318 |
| sun / moon strokes (`Icon.Sun/Moon`) | `WeatherSunny24` / `WeatherMoon24` by `IsLightMode` (the swap lives in the central theme-toggle content style, not the view) | MainWindow 247 |
| refresh / settings strokes | `ArrowSync24` / `Settings24` | MainWindow 267, 275 |
| `▾` after "Layout" | `ChevronDown24` beside the label; hint sentence in `VisualRoutingView:289` becomes plain words | DisplayCard 234, VisualRouting 289 |
| `ⓘ` | `Info24` (focusable, same tooltip and automation name) | SourceCard 94, DisplayCard 123 |
| `↻ Refresh`, `■ Stop`, `▶ Start` | `ArrowSync24`, `Stop24`, `Play24` + the word, inside a `StackPanel` content (native button) | SourceCard 352/372/392 |
| `●`, `✓ On screen` | `Style.StatusDot` ellipse; `Checkmark24` + "On screen" | DisplayCard 556, 1083 |
| brand `Canvas` (40 lines) | extracted to `Views/BrandMark.xaml` (static icon, allowlisted); `BrandLogo` host name kept | MainWindow 107-154 |
| **keep** `Icon.Power` (tests need a `Path`), `Icon.Monitor/Video/Grid/Copy/Lock`, brand geometries | StreamGeometry in `Icons.xaml` | - |

`Icon.Sun/Moon/Refresh/Settings` have no user in my files afterwards (Foundation may delete them once B confirms).

## 4. Layout de-hardcoding table

| Where | Literal | Replacement |
|---|---|---|
| MainWindow.xaml:8,10 | `Height=860 Width=1280`, `MinHeight=720 MinWidth=1024` | `Size.Window.Default.*`, `Size.Window.Min.*` tokens (root window; a test pins 1024x720) |
| :97 | app bar `MinHeight=56` | `Size.AppBar.Height` |
| :107 | BrandLogo 28x28; canvas 64x64 and 4/12 px ellipses (:114-151) | host via `Size.Icon.Brand`; canvas content moves to `BrandMark.xaml` (static icon, allowlist) |
| :208 | `MinHeight=48` | `Size.Touch.Min` |
| :213 | status ellipse 10x10 | `Style.StatusDot` (`Size.Dot.M`) |
| :234 | divider `Width=1 Height=28` | `Border BorderThickness="1,0,0,0"` + `Margin`; test needs ActualWidth >= 1, ActualHeight >= 24 |
| :311 | `MaxWidth=650` | removed; the `*` column and `TextTrimming` bound it |
| :336-338 | log columns 170 / 70 / 120 | `GridLength` tokens `Size.Log.Col.Time/Level/Category` (a shared-size scope would jitter while recycling) |
| VisualRoutingView:66,277 | Path 16x16 | `Style.Icon.Section` (`Size.Icon.S`) |
| :168 | divider `Height=1` | `Border BorderThickness="0,1,0,0"` |
| SourceCardView:5 | `MinWidth=320` | `Size.Card.Min` (test pins it equal to `GridSpec.MinCardWidth`) |
| :37,319,348,367,387 | `MinHeight=36` | `Size.Touch.Compact`; spacing between stream controls stays >= 8 (`Margin="8,0,0,0"`, tested) |
| :51 | dot 8x8 | `Style.StatusDot.Small` (`Size.Dot.S`) |
| :188,225 | placeholder Paths 22 / 24 | `Style.Icon.Placeholder` (`Size.Icon.M`) |
| DisplayCardView:6 | `MinWidth=320` | `Size.Card.Min` |
| :62-65 | rows 24 / 56 / * / 64 | `Size.Card.Row.Header/ActionBar/Dock`; test `DisplayCard_ChromeHeight_EqualsTheSumOfTheRowTokens` ties them to `GridSpec.DisplayCard.ChromeHeight` (170). `Auto` rejected: `AdaptiveCardPanel` sizes cards from `ChromeHeight` before measuring, any drift squeezes the 16:9 picture |
| :80 | dot 8x8 | `Style.StatusDot.Small` |
| :195 | dock `Height=48` | `MinHeight={Size.Touch.Min}` |
| :212,241,287 | `MinHeight=48` | `Size.Touch.Min` |
| :230,272 | icons 14x14 | `Style.Icon.Section` (`Size.Icon.S`) |
| :336 | TurnOff `Width=48 Height=48` | `MinWidth/MinHeight={Size.Touch.Min}`, `Padding=0` |
| :353 | power 18x18 | `Style.Icon.Power` (`Size.Icon.S`) |
| :580,616,888,647 | state icons 26 / 26 / 26 / 28 | `Style.Icon.State` (`Size.Icon.L` = 28, requested) |
| :602 | `MaxWidth=320` | `Size.Notice.Max` |
| :700 | dock `Height=56` | `Size.Dock.Content` |
| :904 | toast `MinHeight=56` | `Size.Toast.Min` |
| :1007 | popup `Width=336` | `Size.Popup.LayoutMenu` |
| :1033 | tile `MinHeight=88` | `Size.LayoutTile.Min` |
| :1074 | schematic 72x40 | `Size.Schematic.Menu.Width/Height` (control owned by B, sized from outside) |
| SettingsDialog:5 | `Height=540 Width=480` | `SizeToContent="Height"`, `Width={Size.Dialog.Settings.Width}`, `MaxHeight` = primary screen height |
| :78,115 | gap columns 16 | two `*` columns + `Margin` on the cells |
| :179,184 | buttons 90 / 110 | `MinWidth={Size.Button.Dialog.Min}` |

Allowed to stay literal: `Margin`/`Padding`/`CornerRadius`/`BorderThickness`, `GridSpec` constants (algorithm, covered by `AdaptiveGridLayoutTests`; only the two `MaxCardWidth` caps change in A13), static icon geometry in `BrandMark.xaml`.

## 5. Virtualization decisions (G5, D11)

| Control | Decision | Reason (also the allowlist text) |
|---|---|---|
| Logs `ListBox` (`MainWindow.xaml:325`) | **Virtualize** with central `Style.ListBox.Virtualized`: `VirtualizingPanel.IsVirtualizing=True`, `VirtualizationMode=Recycling`, `ScrollUnit=Pixel`, `CanContentScroll=True`; in a `*` row, no `ScrollViewer` ancestor. The style supplies a plain `ScrollViewer` template (WPF-UI's `PassiveScrollViewer` bypasses the 16 px touch gutters). Pixel unit because wrapped messages give variable item heights. | up to 1000 entries of a 4-column grid |
| Sources grid (`VisualRoutingView.xaml:151`) | **Not virtualized** | each `SourceCardViewModel` owns a live RTP preview (UDP socket + multicast membership); realizing or recycling cards would start/stop it (zero-socket-leak rule). `AdaptiveCardPanel` is not virtualizing. |
| Displays grid (`:312`) | **Not virtualized** | mirrors source `PreviewBitmap` references; same rule |
| Compact palette (`:124`) | **Not virtualized** | few items, must stay realized for arm/drag (card control is B's) |
| Display `Slots` (`DisplayCardView.xaml:384`) | **Not virtualized** | at most 9 windows in a `FractionalLayoutPanel` |
| Layout menu (`:1024`) | **Not virtualized** | 6 tiles in a `UniformGrid` |

Guard: every `ListBox`/`ListView`/`DataGrid` in my XAML uses `Style.ListBox.Virtualized` or is in the allowlist with a reason (exactly one list today), and `CardGrids_AreNotVirtualized_EveryCardIsRealized` proves every card container exists.

## 6. Constraint check (master section 3)

| # | Constraint | How it stays satisfied | Tests that change and why |
|---|---|---|---|
| 1 | TDD, STA view tests, 795 baseline | each task is red first; XAML tasks run STA view tests; guards turn green per file. Run with `-p:BaseOutputPath=bin/uiv2a/`. | Four bootstrap blocks call the shared `TestResources.Ensure()` (done in Foundation, not by me) |
| 2 | Theme contract | new central files use only `DynamicResource Brush.*` (`ThemeResourceUsageTests` scans every `*.xaml` under `src/`, including `Resources/Styles/*`); ThemeService change is the foundation's, with **R1** (no caption-stripping `Apply` overload) | `VideoOverlayText_...` rows 3-4 select by `x:Name` instead of `FontSize`; new `UiV2ThemeBridgeTests` (window background equals `Brush.Background.Dark` after toggling both ways; the native caption keeps its title and icon: manual check, since STA tests cannot see non-client drawing) |
| 3 | Clinical ergonomics | `Size.Touch.Min`/`Compact` only (D4); `FocusVisualStyle` = `Style.FocusVisual.Ring` kept on tabs/actions/power; names unchanged; Undo toast/dock rows untouched; text >= 12 | `Actions_AreGhostButtons_WithVectorIcons` accepts `Path` or `SymbolIcon`; `Tabs_*` unchanged; new `AppBar_ShowsLabelsAtFullHd_AndHidesThemAtTheFloor`; `ReleaseDefaultsTests.cs:134` `CheckBox` -> `ToggleButton` |
| 4 | Zero socket leaks | no preview lifecycle touched; card grids not virtualized and tested | new `CardGrids_AreNotVirtualized_EveryCardIsRealized` |
| 5 | `Brush.*` stays the API | views reference `Brush.*`, `Size.*`, `Style.*`, `Icon.*`, `Font.*` only | - |
| 6 | Uncommitted work | superseded by D2: the user's changes are committed on `ui-v2/integration` before A starts; A works in its own worktree off it. This planning session touched only this file. | - |
| 7 | Hardware code out of scope | untouched; only additive view-state properties | - |

Other existing tests that change: delete `ViewSmokeTests.cs:1710-1721` and the six `Assert.Empty(FindAll<LayoutsBarView>(view))` lines (~1801, 1829, 1870, 1878, 1909, 3582) with D5; `:1929` and `:3871` read `Template.FindName("bd", tile)` (layout tile and Composer picker tile): keep the part name; `:2771` asserts the `TurnOffButton` focus visual is the `Style.FocusVisual.Ring` instance; `:815` reads the implicit `ScrollViewer` style from the application (must survive WPF-UI's own implicit style: merge order, F2); the `Rings_ShowAfterAKeyPress_...` and theme-repaint tests keep passing. `AdaptiveGridLayoutTests` keeps 170/26 and gets two new assertions for the new caps (`DisplayCard` 960, `SourceCard` 480); `AdaptiveCardPanelTests` unchanged.

## 7. TDD task list

**Sequencing (D7, D10).** Foundation checkpoint first (master): WPF-UI package, dictionaries, `Resources/Styles/*` with the keys of section 8, `ThemeService` bridge, icon policy test, shared `TestResources.Ensure()`. Foundation task zero is the pure-move split of `ViewSmokeTests.cs` into partial class files (795 tests unchanged). Then A implements **in its own worktree off `ui-v2/integration`** and merges back at **checkpoint 2 (Main Window)**.

Partial files I expect to own (names are a proposal; B proposed `.Composer.cs`; shared helpers and rigs stay in a master-owned `.Support.cs`, theme/scrollbar/contrast tests in `.Theme.cs`):

| Partial | Contents from today's file |
|---|---|
| `ViewSmokeTests.DisplayCard.cs` (A) | all `DisplayCard_*` (lines about 76-186, 1318-1677 except removed, 1734-2243, 2691-2794, 2877, 3234, 3640, 3767, 3974) |
| `ViewSmokeTests.SourceCard.cs` (A) | `SourceCardView_*`, `SourceCard_*`, `SourceCards_*` (245, 550, 576, 1040, 2593, 3209) |
| `ViewSmokeTests.VisualRouting.cs` (A) | `VisualRoutingView_*` (160, 230, 252, 625, 1097, 1121, 1645, 2820, 3570, 3608) |
| `ViewSmokeTests.MainWindow.cs` (A) | `MainWindow_*` (3892, 3911) and `App_ThemeToggle_*` that need the main window |
| `.Composer.cs` (B), `.Theme.cs` (master: `ThemeScrollBar_*` 820/835, `TakeStyle_*` 885), `.Support.cs` (master: `RunSta`, `Layout`, `FindAll`, `Named`, `Showing`, rigs) | |

New A tests go in new files (`UiV2*Tests.cs`, `*StateTests.cs`) or in the partials above. Test commands (every task: focused, then full non-live):
```
dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj -p:BaseOutputPath=bin/uiv2a/ --filter "FullyQualifiedName~<Name>" -- RunConfiguration.DisableParallelization=true
dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj -p:BaseOutputPath=bin/uiv2a/ --filter "FullyQualifiedName!~LiveControlServerTests&FullyQualifiedName!~LiveFunctionTests" -- RunConfiguration.DisableParallelization=true
```
One commit per task, including `memory/active_context.md` and `memory/progress.md`.

| # | Task | Failing test first | Files | Done when |
|---|---|---|---|---|
| A1 | Hard guards + the single allowlist (D6) | `UiV2GuardTests` over the A files (and a shared `UiV2Allowlist` file with sections: `Pending` = file -> task id, `StaticIcons`, `UnvirtualizedLists` with reasons, `InlineStyleExceptions`). Rules, no budgets: no `<ControlTemplate`; no keyed `<Style`; inline `<Style` must have `BasedOn` or be allowlisted; no numeric `Width/Height/Min*/Max*` (incl. Row/ColumnDefinition) unless `{StaticResource Size.*}` or allowlisted static icon; no `FontSize` literal and nothing below 12 (resolved text roles); no bitmap/emoji/Unicode-glyph icon (ranges U+2190-27BF, 2B00-2BFF, 24B6-24FF, 25A0-25FF, surrogates; `·` `…` allowed; `AppIcon.ico` and `PreviewBitmap` images exempt); every list virtualized or allowlisted. **Red-first:** the test is committed with every A file in `Pending` (suite green); each later task deletes its file from `Pending` first (red) and migrates until green. | new tests only | all A files listed, all rules red when `Pending` is emptied |
| A2 | Delete `LayoutsBarView` (D5) | remove the standalone test (1710) and the six absence asserts; add `Overview_HasNoLayoutsBar` (no element named `LayoutTile`) | delete `LayoutsBarView.xaml(.cs)`, edit tests, `Handover/02_CODEBASE_STRUCTURE.md` line 100 | full suite green, `grep LayoutsBarView` empty outside history |
| A3 | `SourceCardViewModel` view state | `SourceCardViewStateTests`: truth tables for the 9 properties, `PropertyChanged` on `IsStreaming`/`IsPreviewSupported`/`UsedInBlocks`/`IsArmed`/`Fps` | `ViewModels/SourceCardViewModel.cs` | focused + full |
| A4 | `DisplayCardViewModel` / `MainViewModel` view state | `DisplayCardViewStateTests`: `HasRoutedBadge`, `ShowSingleViewPicture`, `ShowRoutedNonAvasNotice`, `ShowUnroutedPlaceholder`, `ShowUndoToast`, per-tile `IsOnScreen`, `MainViewModel.IsComposing`, notifications | `DisplayCardViewModel.cs`, `MainViewModel.cs` | focused + full |
| A5 | App bar and brand | `MainWindow` removed from `Pending` (red: keyed styles, template, glyphs, sizes); `AppBar_ShowsLabelsAtFullHd_AndHidesThemAtTheFloor` (1920 wide: labels visible, text >= 12; 1024: hidden and still fits); `BrandMark` extraction keeps `BrandLogo` >= 28 px; header tests updated (`SymbolIcon` accepted) | `MainWindow.xaml`, new `Views/BrandMark.xaml(.cs)` | `MainWindowHeaderTests`, `AppVersionTests`, theme tests green |
| A6 | Logs tab | `UiV2LogsViewTests`: 5000 entries -> realized `ListBoxItem`s < 100; Recycling + Pixel; no `ScrollViewer` ancestor; toolbar buttons >= 48 with icons, no emoji, text >= 12; level colours per `Warn/Error/Debug` in both themes | `MainWindow.xaml` (Logs section) | focused |
| A7 | `VisualRoutingView` | file removed from `Pending`; `UiV2VisualRoutingTests`: composer/overview rows, arm banner show/hide + still announced, `Stop`, section headers on role styles; existing `VisualRoutingView_*` unchanged | `VisualRoutingView.xaml` | VisualRouting partial green |
| A8 | `SourceCardView` | file removed from `Pending`; `ui:Card` frame fills the 16:9 preview (measured), footer buttons >= 36 with >= 8 px spacing, info badge focusable with the same name/tooltip, offline badge `Brush.Text.Secondary`, placeholders >= 12 | `SourceCardView.xaml` (+ `Resources/Styles/SourceCard.xaml`) | SourceCard partial green |
| A9 | `DisplayCardView` part 1: frame, header, action bar | `DisplayCard_ChromeHeight_EqualsTheSumOfTheRowTokens`; buttons >= 48; draft chip via `Style.Button.Chip.Draft`; `ModeBadge`/`ModeBadgeText`/`DeviceNameText` names kept; long-name test still passes | `DisplayCardView.xaml`, `Resources/Styles/DisplayCard.xaml` | DisplayCard subset green |
| A10 | `DisplayCardView` part 2: picture states and overlays | overlay truth tables via A4; `Style.Overlay.Body`; overlay text >= 12 on `OnPreview*`/`OnScrim` tokens; readability tests at 1008x615 and 1264x755 pass | `DisplayCardView.xaml` | + `ThemeResourceUsageTests` |
| A11 | `DisplayCardView` part 3: dock, Undo toast, layout menu | `Style.Button.LayoutMenuTile` keeps `bd`; popup width from token; selected tile marked by border and words; toast never overlaps the picture | `DisplayCardView.xaml` | layout-menu + Undo tests green |
| A12 | `SettingsDialog` | `UiV2SettingsDialogTests`: fields >= 48 high with automation names; `ChkShowLogs` is a `ToggleButton` and persists (update `ReleaseDefaultsTests.cs:134`); dialog fits 1024x720; normal chrome kept | `SettingsDialog.xaml(.cs)` | `ReleaseDefaultsTests`, `AppVersionTests`, new tests |
| A13 | Full HD pass (D9) | step 1: a measuring test at a maximized 1920x1080 window records card and preview sizes for 1-6 displays and 3-6 sources (replace the estimates in section 2.3); then failing assertions: previews >= the recorded baseline, `GridSpec` caps 960 / 480, labels visible | `AdaptiveGridLayoutTests` (new caps), `GridSpec` | measured table added to this plan's checkpoint notes |
| A14 | Close-out and exemptions | `CardGrids_AreNotVirtualized_EveryCardIsRealized`, `NoListOutsideTheVirtualizedStyle`; `Pending` empty; allowlist reviewed; memory docs updated | tests, `memory/*` | full suite 795+ green, guards green |

Task count: **14** (A1-A14), plus Foundation task zero (D10) owned by the master.

**Checkpoint 2 deliverables (Main Window, on `ui-v2/integration`):** (1) screenshots in `ui_screenshots/Version 4/` at **1920x1080 and 1024x720, each in Dark and Light**: overview idle, source armed, copy armed with the banner, display with Undo toast, layout menu open, drop-choice panel, Logs tab (ShowLogs on), BAVM Console tab, Settings dialog; (2) non-live test count (795+ plus new, expected about 850) and the guard report (Pending empty, allowlist entries with reasons); (3) the measured Full HD table (A13); (4) a runnable branch (`dotnet run`), plus the manual checklist: native caption keeps title/icon after toggling the theme (R1), Console tab works maximized, touch drag of a source onto a display, one lab routing regression.

## 8. Foundation requests, alignment with session B, and risks

### 8.1 Foundation requests (accepted in principle by the master; this is the final list)

| Id | Request | Same as B |
|---|---|---|
| F1 | `WPF-UI` **4.3.0** package reference. | B 2.4 |
| F2 | Merge order: `ui:ThemesDictionary`, `ui:ControlsDictionary`, palette, `Resources/Styles/*`, `Theme.xaml`, `Icons.xaml` so our implicit `ScrollViewer` and button styles win. **`ThemeService` must not call `ApplicationThemeManager.Apply(theme, ...)` while `Application.MainWindow` is set (R1, strips native caption text/icon/sysmenu from a plain `Window`)**; swap `ThemesDictionary.Theme` directly and set the accent from the app token. | B R8, R10 |
| F3 | Tokens: `Size.Touch.Min` 48, `Size.Touch.Compact` 36 (only these two, D4), `Size.AppBar.Height`, `Size.Tab.MinWidth`, `Size.Breakpoint.AppBarLabels` 1400, `Size.Window.*`, `Size.Icon.S/M/L` = **16 / 22 / 28** (S, M as in B R4; A adds L), `Size.Dot.S/M` 8/10, `Size.Card.Min` 320, `Size.Card.Row.Header/ActionBar/Dock` 24/56/64, `Size.Dock.Content`, `Size.Toast.Min`, `Size.Notice.Max`, `Size.Popup.LayoutMenu`, `Size.LayoutTile.Min`, `Size.Schematic.Menu.*`, `Size.Dialog.Settings.Width`, `Size.Button.Dialog.Min`, `GridLength` tokens `Size.Log.Col.*`; `Font.Size.Lead/Title/Body/Caption` = 16/14/13/12. | B R4 (Comfort 56 / Primary 72 dropped by D4) |
| F4 | Central styles: `Buttons.xaml` (`Primary`, `Secondary`, `Unroute`, `CardAction`, `Composer`, `Take`, `AppBar`), `Tabs.xaml` (`Style.Tab.Nav`, part `bd`), `Text.xaml` (roles of 3.3, `Style.Text.LogLevel`), `Cards.xaml` (`Style.Card.*` on `{x:Type ui:Card}`), `Inputs.xaml` (`Style.TextBox.Field`, `Style.ToggleSwitch.Setting`), `Lists.xaml` (`Style.ListBox.Virtualized`), `Overlays.xaml` (`Style.Overlay.Body`, `Style.Banner.Arm`, toast, popup frame), badge/dot/ring/icon styles, `Resources/Converters.xaml`. All brushes `DynamicResource`; state styles read `Tag`; keys listed in B R6 stay stable (`Style.Button.Composer/Take/CardAction`, `Style.Text.ComposerLabel`, `Style.FocusVisual.Ring`, `Style.ScrollBar.Thin`, implicit `ScrollViewer`, `Icon.Lock`). | B R5, R6 |
| F5 | Empty merged `Styles/SourceCard.xaml` and `Styles/DisplayCard.xaml` (A content) next to B's `Composer.xaml`. | B R3 |
| F6 | One shared test helper (`TestResources.Ensure()` in my draft, `UiTestApp.EnsureResources` in B's; master picks the name) merging exactly what `App.xaml` merges, adopted in the four bootstrap sites. | B R1 |
| F7 | Delete `Icon.Sun/Moon/Refresh/Settings` after A lands (B confirms no use). | - |
| F8 | One converter set: adopt B's `VisibilityConverter { Rule }` (True, False, NotNull, Null, NotEmpty, HasItems, NoItems) as the shared implementation, hoisted by the foundation to `Resources/Converters.xaml` instances (B plans to add it in its own Task 6 inside `ComposerConverters.cs`); plus `Converter.WidthAtLeast` and `Converter.BoolToGridLength`. | B Task 6 |
| F9 | The single guard allowlist file (D6): one file, sections per session, so A and B do not both own it. | B Task 3 |

### 8.2 Overlaps and conflicts with session B (for the cross-review)

1. **Duplicate style keys.** B plans `Style.Button.LayoutTile` and `Style.Button.Chip.Staged` in `Composer.xaml`; my layout-menu tile and draft chip use the same ideas. Resolved here by distinct keys (`Style.Button.LayoutMenuTile`, `Style.Button.Chip.Draft` in `DisplayCard.xaml`); the master may later unify (both keep part `bd`, same triggers).
2. **Text roles.** B R5 asks for `Style.Text.Title` 20 / `Heading` 18 / `Body` 14; D8 allows only 16/14/13/12 and my `Body` is 13. One of us must rename: suggested `Lead` 16, `Title` 14, `Body` 13, `Caption` 12 for everyone, B's 20/18 headings mapped to `Lead` (master decision).
3. **Icon sizes.** B: S 16, M 22. A needs 14-28 px icons; mapped to S 16 / M 22 / new L 28 (a +2 px change for the 14 px icons).
4. **Guards.** B Task 3 uses ratchet baselines; D6 wants hard guards + one allowlist. A follows D6 (`Pending` per file); B should do the same.
5. **Converters** (F8) and **test bootstrap** (F6) are requested twice with different names; one implementation each.
6. **Shared files.** `ViewSmokeTests` partial ownership (section 7) must be agreed with B's R2 (their `.Composer.cs` line ranges 277-560, 700-1010, 1139-1230, 2287-2700 do not overlap the A ranges listed above). `Icon.Lock` is shared and stays; `Controls/FractionalLayoutPanel` change by B affects `DisplayCardView` slots (A9/A10 run the multi-window tests after B's change merges; the integration branch resolves the order).
7. **Implicit WPF-UI styles** (B R10) apply to anything unstyled in A's views too; A has no unstyled `Button`/`TextBox`/`ToolTip` after the migration except `ToolTip` content, which the Fluent look will restyle (visual check at the checkpoint).

### 8.3 Risks and open questions

* **R1 ThemeService caption stripping** (verified in WPF-UI source, section 2.1): would blank the native title bar text/icon on the plain `Window`. Needs an explicit foundation test and the manual check.
* **R2 `ui:Card` fit**: template padding/stretch may break the 4-row layout; A8/A9 measure first, fallback `Border` + the same style.
* **R3 WPF-UI font resource**: `ui:SymbolIcon` needs the embedded Fluent System Icons font; without the dictionary in the harness it renders blank but does not throw (B R9). A5 adds a test that a rendered icon has a glyph and a resolved font family.
* **R4 Implicit style leakage** into B's unmigrated controls and the Console tab chrome: review both plans together before F2 merges.
* **R5 12 px floor**: overlay and chip text grows from 9-11 px to 12 px (28 literals); card readability tests at 1008x615 and 1264x755 are the judge, strings wrap.
* **R6 Accent contrast**: WPF-UI derives hover/pressed shades from the accent; the contrast test must cover `ui:Badge`/`ui:Card` text as well as our buttons, in both themes.
* **R7 `RowDefinition` binding** may not update reliably; one allowlisted style is the fallback.
* **O1 (user decision, Full HD)**: the overview is height-bound at 1920x1080. Options: (a) accept and rely on labels, 12 px text and higher caps (this plan); (b) move Sources into a left rail at about 1600+ px wide (+about 350 px for displays; layout change, new tests); (c) slim the per-card non-video chrome. Recommend (a) now, (b) as a follow-up if the lab wants bigger previews.
* **O2** Deferred, not in this plan: Logs level filter/follow-tail, Settings/Refresh feedback, `BavmWebView.DefaultBackgroundColor` from the theme (avoids a white flash on the Console tab).
