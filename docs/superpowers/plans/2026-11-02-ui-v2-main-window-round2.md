# UI v2 Main Window, round 2: plan for critique findings M1-M8

Status: **plan only** (no source or test change). Branch `ui-v2/main-window-round2`, cut from `ui-v2/integration` (tip 41e8cfe; it contains checkpoint 2 and 3, so the Main Window and the Composer are both merged).
Inputs: `docs/superpowers/specs/2026-11-02-ui-v2-critique-round2.md` (M1-M8; C1-C8 belong to the Composer session), `competitor_screens/jones-av/MyOR-03.jpg`, locked decisions D1-D12, the allowlist guards (`UiV2Guards`), the Main Window plan (rev 3) and its handover notes.
Method: read the current views, view models and the 1090 non-live tests that touch them; measured contrast myself from `Theme.Dark.xaml` / `Theme.Light.xaml` (python, WCAG formula), so no subagent was needed.

## 0. Measurements that change the critique (contrast, section "Accessibility")

Contrast ratios (text colour on the surface it really sits on):

| Token | Dark: Background / Card / Elevated | Light: Background / Card / Elevated |
|---|---|---|
| `Text.Secondary` | 7.48 / 6.52 / 5.71 | 7.24 / 7.58 / 6.92 |
| `Text.Muted` (also `Status.Offline`) | **4.03 / 3.51 / 3.07** | 4.55 / 4.76 / **4.34** |
| `Status.Online` | 7.56 / 6.59 / 5.77 | 5.24 / 5.48 / 5.01 |
| `Status.Offline` as a dot (needs 3:1) | 4.03 / 3.51 / 3.07 | **2.45 / 2.56 / 2.34** |
| `Accent.Primary` (blue) | 3.71 / 3.23 / 2.83 (fine as a fill with `Text.OnAction`, not as text) | 4.94 / 5.17 / 4.72 |
| `Border.DropTarget` (cyan) | 7.90 / 6.88 / 6.03 | 5.12 / 5.36 / 4.89 |

Findings:
* The critique's "grey hint text near 4:1" is **wrong for `Text.Secondary`** (>= 5.7 everywhere) and **right for `Text.Muted`**: it fails AA in Dark on Card (3.51), Elevated (3.07) and Background (4.03), and in Light on Elevated (4.34). `Muted` is used for hints in `VisualRoutingView` (3), `DisplayCardView` (3), `MainWindow` (2 Logs), `SettingsDialog` (2), `Cards.xaml`/`Text.xaml` (the muted placeholder icon and the Mono timestamp).
* `Status.Offline` has the same colour as `Muted` in Dark (fails text 4.5:1 and the 3:1 non-text rule on Card/Elevated) and **fails 3:1 in Light as a dot** (2.45-2.56). The grey "Connecting..." dot in the top bar (M8) is therefore not just ambiguous but too faint.
* Candidate fix (palette, both themes keep identical keys): Dark `Text.Muted` `#64748B` -> `#8190A6` (5.91 / 5.15 / 4.51 on Background / Card / Elevated; 6.11 on the preview backdrop); Light `Text.Muted` `#64748B` -> `#5B6B82` (5.19 / 5.43 / 4.95). `Status.Offline` follows the same value (dot 3:1 is then met in both themes). Existing `ThemeTests` contrast tables will be extended, not relaxed.

## 1. Cross-cutting decisions and conflicts (need the master / user)

| # | Item | Conflict | Recommendation |
|---|---|---|---|
| X-A | **M8 vs D9.** D9 (user, locked) put labels back on the app-bar actions at wide widths. M8 asks for icon buttons with tooltips. | D9 | Ask the user. Default if no answer: Theme and Refresh become icon-only at every width (tooltip + automation name, as today at the floor); Settings keeps its label from 1400 px. Refresh/Theme are the two actions the critique calls "equal text buttons". |
| X-B | **M4 vs the earlier "words, never colour alone" rule** for why Copy layout is off (the `CopyFromBlockedLabel` line). | Clinical-light a11y decision of 2026-10-29 | Keep the words where they matter: the copy-target *blocked overlay* already shows the full reason while armed; for an idle disabled Copy button the reason moves to the tooltip (`ToolTipService.ShowOnDisabled` already set) and `AutomationProperties.HelpText`. Only the always-visible duplicate line goes. |
| X-C | **M2 changes what a source card is.** The overview card carries Refresh / Stop / Start of the RTP preview. A dense tile has less room. | Zero-socket-leak rule (preview lifecycle must stay user-controllable), D4 36 px compact exception | Preview lifecycle (VM, commands) is untouched. The tile keeps one compact (36 px) row: a single state-aware preview toggle (Start when stopped, Stop when streaming) plus Refresh only while streaming. Tests for the three buttons are rewritten, not deleted. |
| X-D | **M2 and C7 (Composer) want one shared source component.** | Session boundary | M2 re-skins `SourceCardView` into the dense tile (type name kept: it is referenced 103 times in tests). C7 then swaps `CompactSourceCardView` for it. Until C7, the Composer palette stays as is. M2 lands **last** of the Main Window items so C7 can start from it. |
| X-E | **M5 and C2/C3 share one set of colour roles.** | Two sessions defining the same keys | Commit R4 defines the role styles first (section M5); the Composer session consumes them. Nothing in R4 touches Composer files except through those shared keys. |
| X-F | Full HD overview is height-bound (A13). M2 is the only item that adds height to the displays. | The recorded baselines in `ViewSmokeTests.UiV2FullHd` are lower bounds, so they keep passing | After R9 re-record and raise the baselines (section M2 tests). |

Locked decisions otherwise respected: plain `Window` and native buttons with central styles (D1, D12), `ui:SymbolIcon` / `StreamGeometry` only (D3), 48 / 36 px touch tokens (D4), 12 px floor (D8), only the Logs list virtualized and the card grids stay fully realized (D11), Full HD target with the 1024x720 floor tests staying green (D9), no new literal sizes (hard guard: every new size is a `Size.*` token), no raw hex outside `Resources/Themes/`.

## 2. Per finding

### M1 (Critical): nothing may move when a source or a display is armed

Cause (verified in `VisualRoutingView.xaml`): the Displays section is `Grid` rows `Auto` (arm banner, Visible only while armed: 2 px borders + 6 px paddings + 48 px Stop button + 10 px margin = about 74 px), `Auto` (section header), `*` (cards). Arming inserts the banner and the cards row shrinks, so cards re-layout and `DisplaysScroll` gets a scrollbar.

Approach (no extra reserved band; the height already exists in the header row):
* **Merge the banner into the Displays section header row.** The header row becomes a fixed row with `MinHeight = Size.Touch.Min` (48), so the Stop button always fits. Idle: title, hint (this is also where M4's "say it once" text lives) and count. Armed: the same row shows the banner (`Style.Banner.Arm`: `Brush.Surface.CopyTarget` fill, `Border.DropTarget`, `CornerRadius 8`, no bottom margin) over columns 1-2, with the label and the Stop button; hint and count are hidden (`HasArmBanner`, existing). The header row grows once (about +20 px, from about 28 px to 48 px), never on arm.
* Text size inside the banner: `Style.Text.Title` (14) with `TextWrapping Wrap` and `MaxLines` 2, so the 90-character sentence fits two lines at 1024 wide inside 48 px. At Full HD it is one line.
* The Sources row needs nothing: the armed source only shows its ring and its "Placing" chip inside the card (no layout change).
* The removed top banner row (`Grid.Row 0`) is deleted; names `ArmBanner`, `ArmBannerLabel`, `CopyArmBannerLabel`, `StopPlacingButton` stay.

Files: `Views/VisualRoutingView.xaml`, `Resources/Styles/Overlays.xaml` (`Style.Banner.Arm`: drop `Margin`, padding 12,0).

Tests (STA, red first), new `ViewSmokeTests.UiV2NoShift.cs`:
* `Overview_NothingMoves_WhenASourceIsArmed`, `..._WhenADisplayIsArmedForCopy`, `..._WhenTheDropChoiceOpens`: record `TransformToAncestor` bounds of every `SourceCardView`, `DisplayCardView` and of `DisplaysScroll` (`ScrollableHeight`, `ViewportHeight`) before and after; assert deltas < 0.5 px and no scrollbar appearance, at 1920x952 and 1008x615 (floor). Red today (banner pushes cards).
* `ArmBanner_FitsTheHeaderRow_AtTheFloor`: banner and Stop inside the row, Stop >= 48, label not clipped (`AssertTextFits`), live region still `Polite`.
* Existing `VisualRoutingView_*Arm*` and `DisplayCard_*Copy*` tests stay green (names and texts unchanged); the `HasArmBanner` and `IsComposing` tests are untouched.
Conflicts: none. Allowlist: none.

### M4 (Moderate): say it once

* `SourceCardView.xaml`: delete the `TAP OR DRAG TO ROUTE` chip and its `ShowTapHint` binding. The hint stays once, in the Sources section header (`Tap a source, then tap a display - or drag the source onto the display`, already there). Remove `SourceCardViewModel.ShowTapHint` and its rows in `SourceCardViewStateTests` (no other user; the Composer compact card does not use it, verified by grep).
* `DisplayCardView.xaml` dock: idle (unrouted) card shows only `Ready to route a source` (Caption, Secondary) instead of `SOURCE: NONE` plus the second line; routed and multiview cards keep `SOURCE: ...` and the output format. New VM text property `SourceFooterText` on `DisplayCardViewModel` (derived from `SourceLabel`/`IsRouted`, with PropertyChanged) so the XAML needs no trigger. Remove the `CopyFromBlockedLabel` line (X-B): `Copy layout` is `IsEnabled=false`, its tooltip and `AutomationProperties.HelpText` carry `CopyFromBlockedText`. The VM property stays (used by tooltip, the copy-target overlay and tests).
* Dock row stays `Size.Card.Row.Dock` (64): ChromeHeight 170 unchanged.

Tests: update `UiV2SourceCard` (`SourceCard_TapHint_ShowsOnlyForAnIdleSource` becomes `SourceCard_HasNoTapHintChip`), `DisplayCard_UnroutedFooter_SaysReadyToRouteOnly` (new), `DisplayCard_CopyButton_ExplainsWhyDisabled_ByTooltipAndHelpText` (new), adjust the one `CopyFromBlockedLabel` assertion in `ViewSmokeTests.DisplayCard.cs` to the tooltip/HelpText. `SourceCardViewStateTests` loses the tap-hint rows.
Conflicts: X-B only.

### M3 (Moderate): inline layout presets under each display

Facts: presets in `LayoutItemViewModel.CreateCatalog()`: single, matrix2x2, pip, pip_large, side_by_side, quad_1_3, off. The Layout button opens a popup of tiles; a tile click goes through `MainViewModel.ChooseLayoutFromCardAsync` (single/off apply now, MultiView presets open the Composer with the layout staged: nothing reaches the screen until Take, by design). The card's action bar (row `Size.Card.Row.ActionBar` = 56) holds Layout, Copy layout, the draft chip and Turn Off; at the minimum card width (320, inner 294 px) 7 buttons of 48 px cannot fit.

Approach:
* **Two modes by card width, no code-behind**: the action bar's `ActualWidth` against a new token `Size.Breakpoint.CardPresets` (480) through the existing `Converter.WidthAtLeast`.
  * Wide (>= 480): an `ItemsControl` of 4 inline preset buttons bound to a new `DisplayCardViewModel.InlineLayouts` (the same `LayoutItemViewModel` instances as `LayoutMenu`, subset single, pip, side_by_side, matrix2x2, so `IsOnScreen` and `IsSelected` stay in one place), then `Copy layout` (icon + word), the draft chip, Turn Off. `LayoutButton` stays as a 48 px icon-only "More layouts" button (chevron/ellipsis `SymbolIcon`) that opens the existing popup with all tiles (1+3 Quad, PIP Large, Off, and the four again). It keeps its name, automation name `Layout for {0}`, its click and key handlers, and its `Layout` `TextBlock` (collapsed in this mode), so the 17 existing `LayoutButton` test uses and the popup tests keep working.
  * Narrow (< 480): today's bar unchanged (Layout dropdown with its label, Copy, chip, Turn Off).
* Preset button = `Style.Button.LayoutPreset` in `DisplayCard.xaml`, `BasedOn` the Composer's `Style.Button.LayoutTile` family if its template allows (check at implementation; otherwise a small own template that keeps part name `bd`), `MinWidth/MinHeight = Size.Touch.Min`, content `views:LayoutSchematic` at `Size.Schematic.Inline.Width/Height` (new tokens, about 28x16). On-screen state: `Brush.Accent.Primary` border and `Brush.Surface.Selected` fill (existing tile look) **plus** a `Checkmark24` `SymbolIcon` (Size.Icon.S) in the corner and `AutomationProperties.ItemStatus="On screen"`, so colour is never the only signal (M5 rule). Tooltip = title and subtitle; `AutomationProperties.Name` = title.
* Click handler: the existing `LayoutMenuTile_Click` renamed `LayoutChoice_Click`, shared by popup tiles and inline presets.
* "One tap": Single View applies at once. MultiView presets still open the Composer (staging then Take): this is a safety property, not a missing feature; the plan keeps it and says so in the user-facing tooltip ("Opens the Composer").

Files: `Views/DisplayCardView.xaml(.cs)`, `Resources/Styles/DisplayCard.xaml`, `Resources/Tokens.xaml` (2-3 tokens), `ViewModels/DisplayCardViewModel.cs` (`InlineLayouts`).
Tests (new `ViewSmokeTests.UiV2InlinePresets.cs`, plus edits):
* `DisplayCard_AtFullHd_ShowsFourInlinePresets_AtLeast48_InTheBarOrder` (single, pip, side_by_side, matrix2x2 with `LayoutSchematic.CellCount` 1, 2, 2, 4); `DisplayCard_AtTheMinimumWidth_KeepsTheLayoutDropdown_AndNoInlinePresets` (card width 320).
* `InlinePreset_Single_AppliesAtOnce`, `InlinePreset_MultiView_OpensTheComposerStaged_AndChangesNothingOnScreen` (as the existing tile test).
* `InlinePreset_MarksTheOnScreenLayout_WithBorderIconAndItemStatus` and `AnUnroutedDisplay_MarksNoPreset` (reuse `IsOnScreen`).
* Row fit: everything inside the card frame at 480, 613 and 699 px; ChromeHeight test unchanged (still 170).
* `LayoutButton` tests stay as they are (red-free); popup tests unchanged.
Conflicts: D4 (48 px: met), D3 (schematic is a vector control: ok), D12 (native buttons: ok), hard guards (tokens only). Dependency on the Composer's `Style.Button.LayoutTile` only as `BasedOn`.

### M5 (Moderate): one accent, status as dot + word

Role table the plan implements (both screens, shared with the Composer's C2/C3):

| Role | Colour | Used for |
|---|---|---|
| Armed / selected / drop target | cyan `Border.DropTarget` (+ `Surface.CopyTarget`) | the armed source ring and "Placing", the copy-armed display, drop-target ring, the on-screen layout preset |
| Primary action fill | `Accent.Primary` blue | at most one per view (overview: none; Composer: Take) |
| Status | dot + plain word: `Status.Online`, neutral grey (fixed `Muted`), `Action.Danger` for errors only | online, offline, connection |
| Staged | amber `Status.Staged` | Composer only |
| Everything else | neutral borders and `Text.Secondary` | tags, counts, hints |

Commits:
* **R4 (M5a, palette and keys):** fix `Text.Muted` / `Status.Offline` in both themes (section 0), add neutral and status keys: `Style.Badge.Neutral` (Elevated fill, Default border, Secondary text; for DUAL LINK and the mode tag), `Style.Text.Status` (Caption, Secondary, word beside a dot), `Style.Button.Ghost` (transparent, neutral, danger tint only on hover; for Turn Off). `Style.Badge.Online/Offline/Mode`, `Info`-as-pill uses are deprecated here and deleted in R5 once unused (`Badge.Info` itself stays, the Composer uses it). Composer can consume these keys at once.
* **R5 (M5b, apply):**
  * `SourceCardView`: ONLINE/OFFLINE `ui:Badge` pills -> `StatusDot.Small` already present + `Style.Text.Status` word ("Online" / "Offline"); `DUAL LINK` -> `Style.Badge.Neutral`; `Start` button -> `Style.Button.Secondary` (today a blue primary on every card, which breaks "one primary per view"); `Stop` keeps `Unroute` but only while streaming.
  * `DisplayCardView`: mode badge -> `Style.Badge.Neutral` always (the picture and the border already say routed); routed card border `Status.Routed` blue -> neutral `Border.Default` (the picture is the signal); armed/drag-over cyan stays; `CopySourceChip` stays cyan (it is "armed"); Turn Off -> `Style.Button.Ghost`.
  * `VisualRoutingView`: `DISCOVERED: 2` pills -> plain Caption text "2 discovered" (words are in the header hint row).
  * Muted hints that sit on cards move to `Secondary` where the pair is still < 4.5:1 after R4 (none expected; a test enforces it).
* Colour is never the only carrier: every state keeps a word or icon (dot + "Online", on-screen check on presets, "Placing" chip).

Tests: extend `ThemeTests` contrast tables for the new Muted/Offline values, the new neutral badge and `Style.Text.Status` pairs in both themes (>= 4.5 text, >= 3 dot); `UiV2StylesTests` (no FontSize < 12 through BasedOn chains: new styles carry no WPF-UI base); new `SourceCard_StatusIsDotAndWord_NotAPill` (no `Badge` with ONLINE/OFFLINE, word visible, dot size `Size.Dot.S`); `DisplayCard_RoutedBorderIsNeutral` / `ModeBadgeIsNeutral`; `Overview_HasOnePrimaryButtonAtMost` (count visible buttons whose style is `Style.Button.Primary`: 0); update `UiV2VisualRouting` discovered test (plain text, still >= 12 px). Existing `ONLINE`/`OFFLINE` text asserts keep the same strings. A Light-theme offline-text test replaces the current `Brush.Text.Secondary` assertion with the same colour (unchanged).
Conflicts: none with D1-D12. Cross-session: X-E.

### M6 (Minor): section headers

* Texts become sentence case at card-title size: `Video sources (TX)` / `Target displays (RX)` using `Style.Text.Title` (14, primary) with `AutomationProperties.HeadingLevel="Level2"` set explicitly (the `Style.Text.Section` role stays for its other users, for example the Composer rail headings). The hint moves to Caption `Text.Secondary` on the same baseline (it is the M1/M4 slot, so it is not "below"): hierarchy = title 14 primary > hint 12 secondary > counts 12 secondary.
* Files: `VisualRoutingView.xaml` only. Tests: update the `SectionHeaders_UseTheSectionRole` test to `..._AreTitleSizedAndLevel2Headings` (headings still announced as level 2: `AutomationProperties.GetHeadingLevel`), no other.
Conflicts: none.

### M7 (Minor): empty display tile

* The unrouted placeholder (`ShowUnroutedPlaceholder`) becomes: `views:BrandMark` at `Size.Icon.Brand`-derived large size (new token `Size.Brand.Empty`, about 64), the line `Ready to route a source` (`Style.Text.Title`, `Foreground Text.OnPreview`, kept explicit for `VideoOverlayText_*`), and one hint `Tap a source, then tap here - or drag it here` (Caption, `OnPreviewMuted`; replaces the misleading "Drag any TX card above or 'Off' to turn off"), inside a dashed frame (`Rectangle` with `StrokeDashArray`, stroke neutral `Brush.Border.Control` so that cyan stays reserved for "armed") laid over the picture surface with an 8 px inset. When the display is a valid drop target during an armed source the existing cyan drop ring still wins.
* The tile keeps its 16:9 size (AdaptiveCardPanel cards are uniform); the information gain is the name plate: the header already holds the name, so the placeholder does not repeat it.
* Files: `DisplayCardView.xaml`, `Resources/Styles/DisplayCard.xaml` (`Style.Shape.EmptyFrame`). Tests: `DisplayCard_Unrouted_ShowsBrandMark_ReadyToRoute_AndDashedFrame`, update the three `Drop Source Here` references (ThemeResourceUsage row, `ViewSmokeTests.DisplayCard`, `UiV2DisplayCard` state test) to the new text and keep the `OnPreview*` foreground rule.
Conflicts: D3 (brand mark is a vector view: ok).

### M8 (Minor): top bar

* **Connection**: new `MainViewModel.ConnectionHealth` (`Connecting`, `Connected`, `Offline`; set where `ConnectionStatus` is assigned in `InitializeAsync`/discovery failure) with PropertyChanged. `Style.StatusDot.Connection` (Cards.xaml) reads `Tag`: Connected = `Status.Online`, Offline = `Action.Danger`, Connecting = hollow ring (neutral stroke, no fill) so it reads differently from both. The status text is shown only when not `Connected` (Offline and Connecting carry the word and the host); when connected only the dot shows, and the tooltip and `AutomationProperties.HelpText` carry `Connected: 127.0.0.1:6970`. `ConnectionBadge` keeps its name and 48 px height; `AutomationProperties.Name` stays "Connection status" and the live setting is `Polite` so a drop to Offline is announced.
* **Actions**: per X-A. Default: Theme and Refresh icon-only at every width (the label `TextBlock`s `ThemeToggleLabel`/`RefreshLabel` are removed), Settings keeps `SettingsLabel` from 1400 px. Icons use `Size.Icon.M`; ghost style unchanged.
* Files: `MainWindow.xaml`, `ViewModels/MainViewModel.cs`, `Resources/Styles/Cards.xaml`. Tests: `UiV2AppBarTests` (`AppBar_AtFullHd_...` loses the two labels; new `ConnectionDot_TellsOfflineFromConnecting_AndOnlyProblemsShowAWord`, `ConnectionHealth_FollowsConnectAndFailure` as a VM test with the mock client), header tests stay (48 px, focus rings, names, fits at 1024).
Conflicts: **D9** (X-A), nothing else.

### M2 (Moderate): one dense source strip (lands last)

* `VisualRoutingView`: the Sources `AdaptiveCardPanel` becomes a left-aligned `WrapPanel` strip (tiles fixed at `Size.Strip.Tile.Width`, about 220, `ItemHeight` from the 16:9 thumbnail plus the footer), inside a `ScrollViewer` with `MaxHeight = Size.Strip.Max.Height` (two tile rows) and `VerticalScrollBarVisibility Auto`. The Sources row becomes `Auto`; the Displays row takes the rest (`*`). The allowlisted `RowDefinition` style and the `2*` / `3.4*` ratio disappear, so the allowlist entry is **removed** in the same commit (the stale-entry guard enforces it).
* `SourceCardView` (same class, new template): 16:9 thumbnail (the live `PreviewBitmap`; last frame stays after Stop, which is today's behaviour; before the first start a name plate: device name, link mode, "Preview off" in `OnPreview*` tokens), name row (dot + name + info icon), and one compact 36 px row with the state-aware preview toggle (X-C). Armed ring, focus ring, "Placing" and "in N blocks" chips stay (positions adapt). `MinWidth` becomes `Size.Strip.Tile.Min` (test pinned to `GridSpec.MinCardWidth` is changed to the new token and `GridSpec.SourceCard` is no longer used by the overview; keep the type for the Composer-independent `AdaptiveGridLayoutTests`).
* Not virtualized (D11): all tiles realized; the strip is a `WrapPanel`, not a `VirtualizingPanel`; `CardGrids_AreNotVirtualized_EveryCardIsRealized` updated for the new panel.
* Expected gain at 1920x952 content: the Sources section drops from about 350 px to about 270 px (one row of 6 tiles); the displays row gains about 80 px, so 1-2 display pictures grow from 673x378 toward the 960 cap (about 740-760 wide); to be **measured** by the A13 table test before and after (X-F) and the baselines raised.
* Files: `Views/VisualRoutingView.xaml`, `Views/SourceCardView.xaml(.cs)`, `Resources/Styles/SourceCard.xaml` (tile styles), `Resources/Tokens.xaml` (strip tokens), `ViewModels/SourceCardViewModel.cs` (`ShowPreviewToggle`, `PreviewToggleText` view state, additive), allowlist JSON (remove one entry).
Tests (red first): `Strip_IsLeftAligned_TilesHaveEqualSizeFromTokens`, `Strip_WrapsToASecondRow_AndThenScrolls_WithEveryTileRealized` (8 and 14 sources), `SourceTile_ThumbnailIs16by9_AndLastFrameStaysAfterStop`, `SourceTile_IdleShowsANamePlate`, `SourceTile_PreviewToggle_StartsAndStops_AtLeast36_WithEightPxSpacing`, floor tests at 1008x615 (the displays remain reachable and not clipped), `FullHd_*Baseline` raised to the measured values, rewrite of the Refresh/Stop/Start assertions in `ViewSmokeTests.SourceCard` / `UiV2SourceCard` (same behaviour, new layout), tap-to-arm, keyboard and drag tests unchanged (handlers kept).
Conflicts: X-C, X-D; D4 (36 px exception, already accepted), D11.

## 3. Commits (atomic, one per finding step; each ends with the full non-live suite green and memory notes updated)

| # | Commit | Findings | Main files | Blocks / blocked by |
|---|---|---|---|---|
| R1 | Nothing moves when armed | M1 | `VisualRoutingView.xaml`, `Overlays.xaml` | none (do first) |
| R2 | Say it once | M4 | `SourceCardView.xaml`, `DisplayCardView.xaml`, 2 VMs | after R1 (header hint slot) |
| R3 | Inline layout presets | M3 | `DisplayCardView.xaml(.cs)`, `DisplayCard.xaml`, `Tokens.xaml`, VM | after R2 (same action bar and dock) |
| R4 | Palette and role keys | M5a | `Theme.Dark/Light.xaml`, `Badges.xaml`, `Text.xaml`, `Buttons.xaml`, theme tests | share with Composer (C2/C3) |
| R5 | One accent, dot + word | M5b | `SourceCardView.xaml`, `DisplayCardView.xaml`, `VisualRoutingView.xaml`, `Cards.xaml` | after R4 |
| R6 | Section headers | M6 | `VisualRoutingView.xaml` | after R5 |
| R7 | Empty display tile | M7 | `DisplayCardView.xaml`, `DisplayCard.xaml`, `Tokens.xaml` | after R3, R5 |
| R8 | Top bar | M8 | `MainWindow.xaml`, `MainViewModel.cs`, `Cards.xaml` | needs the D9 answer (X-A) |
| R9 | Dense source strip | M2 | `VisualRoutingView.xaml`, `SourceCardView.xaml`, `SourceCard.xaml`, `Tokens.xaml`, allowlist | last; C7 depends on it |

Priority check against the critique's list (M1, M3+M4, M5, M2): R1, R2+R3, R4+R5, R9 hold; M6, M7, M8 are small and slot between.

## 4. Verification per commit and for the round

* TDD: each commit starts with its red STA tests (listed above), then the change, then the focused tests and the full non-live suite with a fresh `BaseOutputPath` (`bin/uiv2r2/`) and `--blame-hang-timeout`. Known flake: the suite has hung intermittently on an existing `MainWindow_*` STA test (each `MainWindow` starts real UDP/network setup); new tests must not add `MainWindow` instances beyond one or two per class.
* Guards: `UiV2Guards` stays green with no new `pending`; new sizes only as `Size.*` tokens; the only allowlist change is removing the Sources `RowDefinition` style entry in R9.
* Screenshots after R5 and after R9 (offscreen renderer `UiV2_MainWindowScreenshots`, 1920x1080 and 1024x720, Dark and Light), the real-app title-bar check after R8, and the A13 measurement table before/after R9.
* Contrast: every new foreground/background pair is registered in the theme contrast tests in both themes (R4/R5/R7/R8).

## 5. Out of scope here

Composer findings C1-C8 (the Composer session). The dense strip's Composer adoption (C7) and the shared role keys are the only touch points (X-D, X-E). Hardware and protocol code is untouched.
