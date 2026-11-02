# UI v2 Composer, critique round 2: plan

Status: **plan only** (no code). Input: `docs/superpowers/specs/2026-11-02-ui-v2-critique-round2.md`, `competitor_screens/jones-av/MyOR-03.jpg`.
Base: `ui-v2/integration` @ `41e8cfe` (PIP Large preset, so the Composer now has **5** layout tiles: `matrix2x2, pip, pip_large, side_by_side, quad_1_3`). Branch for this plan: `ui-v2/composer-round2`.
Scope: Composer findings **C1-C8**; **M2/M5** only where they touch the shared source strip and the accent tokens. Main Window findings M1, M3, M4, M6-M8 are not planned here.
Rules that stay: D1-D12 (plain `Window`, native buttons + central styles, tokens only, 12 px floor, 48/36 touch, only Logs virtualized), `UiV2Guards` + one allowlist (no template/keyed style/literal size/FontSize in view files), video-overlay `Foreground`/`Background` token attributes stay on the elements in the view (`ThemeResourceUsageTests.VideoOverlayText_...`), tab order, spoken names, live regions.
Method for every commit: TDD (red STA test first), one atomic commit, fresh `-p:BaseOutputPath`, `--filter` BEFORE the `--` separator, full non-live suite green (baseline **1072** + the new ones; recount on the integration tip first).

## 0. Facts checked on the integration tip

* Composer files: `Views/MultiViewComposerView.xaml` (553 lines, layout and bindings only), `Resources/Styles/Composer.xaml` (all Composer styles and `Size.Composer.*`), `ViewModels/MultiViewComposerViewModel.cs`, `ViewModels/ComposerLayoutOptionViewModel.cs`, `Views/LayoutSchematic.xaml(.cs)`, `Views/CompactSourceCardView.xaml(.cs)`.
* Colour roles used by the Composer today (21 references in `Composer.xaml`): amber `Brush.Status.Staged` / `StagedText` / `Surface.Staged` (staged), cyan `Brush.Border.DropTarget` / `Surface.CopyTarget` / `Text.CopyAccent` (armed), blue `Brush.Accent.Primary` (selected tile, info banner), blue `Brush.Action.Take` (Take, done step markers), blue `Brush.Status.Routed` (on-screen frame), green `Brush.Status.Online` (success banner, Undo border). Task 11 of round 1 already switched the palette badges to `ui:Badge` styles from `Badges.xaml` (foundation-owned).
* Already true, keep as a regression test: arming a source adds a banner in the **pinned bottom cluster**, which is bottom-anchored, so **Take does not move** and the canvas does not move (the Composer already satisfies the intent of M1; the Main Window overview does not).
* The harness `ViewSmokeTests.ComposerShots.cs` paints source pictures with a synthetic RGB gradient. The critique's C7 ("gradient test pattern reads as decoration") is about **test data, not the product**: real previews are live video. Fix the harness (commit R2-10) so the next critique is not misled.
* Round-1 numbers to protect: rails 320/360 (240/264 at 1024x720), canvas about 1144x635 at 1920x1080 (height-bound), Take 72 px, layout tiles about 127x74, block number 28 px.

## 1. Decisions needed before coding (ask the master / user)

| # | Question | Options | My recommendation |
|---|---|---|---|
| Q1 (C1) | Empty Step 1 canvas | (a) ghost schematic that follows the hovered/focused tile, default = last-used layout (view only, nothing staged); (b) auto-stage the last-used layout when the Composer opens with no draft and no live layout | **(a) first**, then (b) as a separate commit only if approved. (b) puts a layout on the display's staged picture the operator did not choose, and "Step 1 done" would turn green by itself; (a) removes the void with zero state change. On touch there is no hover, so (a) alone helps touch users only through the default ghost. |
| Q2 (M5) | What is "the one accent" | Blue for selection, primary action, on-screen; cyan ONLY for the armed target; amber ONLY for staged; green only for success; red for errors (no new tokens, only which token each element uses) vs. a single cyan accent everywhere including Take | **Blue family + cyan only for armed + amber only for staged** (the competitor's single cyan would turn Take cyan, which breaks the existing Take contrast tests and the foundation palette). Needs the master to confirm because `Resources/Themes/*` and `Badges.xaml` are shared. |
| Q3 (C6) | Where the step 2 / step 3 headings live | step 2 in the canvas status row, step 3 above the pinned Take cluster; or drop the numbered steps for steps 2-3 and keep step 1 only | **Move, do not drop** (step headings are tested, spoken as `Step 2, Place sources: done` / `Step 3, ...`). Fallback if 1024x720 loses Take visibility: marker-only header next to the Take reason line. |
| Q4 (C7/M2) | One shared source strip | `CompactSourceCardView` becomes the shared dense tile (Composer session owns the file); the Main Window overview adopts it and moves stream controls (start/stop/standby/info) elsewhere | Needs the Main Window session. See section 5. |

## 2. Findings, one by one

### C5 `Changes (1)` is cryptic  (smallest, pure VM)

* Today: `ChangeLines` = `$"{NumberOf(c.SlotIndex)}: {c.From ?? "-"} -> {c.To ?? "-"}"` -> `1: - -> TX1_c07bb_SFPA`.
* Target: `Block 1: empty -> TX1_c07bb_SFPA`; removal `Block 4: TX2 -> empty`; replacement `Block 2: TX1 -> TX2`. ASCII `->` stays (no Unicode arrow: the icon guard rejects glyphs).
* Files: `ViewModels/MultiViewComposerViewModel.cs` (`ChangeLines`); tests only for the rest.
* Tests (red first): update the three pinned assertions: `MultiViewComposerViewModelTests` lines ~152 (`"1: - -> TX1"`), ~1054 (`"2: - -> TX1"`, display number, not API index), `MainViewModelComposerTests` ~299 (`"2: - -> TX2", "4: TX2 -> -"`). Add `ChangeLines_ReadAsWords_ForPlace_Remove_Replace`. The view text already wraps (`Style.Text.Change`).
* Conflicts: none. A screen reader reads it as plain words (better than before).
* Commit **R2-1**.

### C2 two primaries compete

* Today: `Style.Button.Done` (Composer.xaml) sets `Background`/`BorderBrush` to `Brush.Border.DropTarget` (cyan fill) and `Foreground` `Brush.Text.OnDropTarget` while armed; "Cancel move" uses the plain Composer look.
* Target: both states are the secondary look: `Background` `Brush.Surface.Elevated`, `BorderBrush` `Brush.Border.DropTarget` (2 px outline, cyan = "armed"), `Foreground` `Brush.Text.Primary`. Take stays the only filled button. Keep `x:Name="DoneButton"`, string `Content`, the `bd`-less template of `Style.Button.Composer` (`InputModality.ShowsFocus` ring still changes pixels).
* Files: `Resources/Styles/Composer.xaml` (`Style.Button.Done`). View untouched.
* Tests: `Composer_DoneButton_IsSecondary_TakeIsTheOnlyFilledButton`: arm a source; `Done.Background` colour == `Brush.Surface.Elevated`, `Done.BorderBrush` == `Brush.Border.DropTarget`; among all visible `Button`s of the Composer only `TakeButton` has `Background` == `Brush.Action.Take` (and the Strip/Chip/Tile backgrounds are none of the filled brushes). Keep `AssertShowsFocus(DoneButton)`. Contrast: `Done` text on Elevated and the cyan outline on the canvas surface >= 3:1 (component) in Dark and Light; use the existing `Contrast()` helper pattern in `UiV2StylesTests`.
* Conflicts: `Brush.Text.OnDropTarget` becomes unused by the Composer (still used elsewhere?) - grep before removing; do not remove tokens in this commit.
* Commit **R2-2**.

### C3 + M5 (Composer part): colour overload in the staged state

Roles after the change (no new tokens; Q2 must be confirmed):

| Element | Today | After |
|---|---|---|
| `STAGED - not on screen yet` text, `CHANGES (n)` heading | yellow `Brush.Status.StagedText` bold | `Brush.Text.Primary` bold, preceded by a small amber **dot** (`Size.Dot.S`, `Brush.Status.Staged`) so state is a shape + word, not text colour |
| Canvas frame | dashed amber (staged) / solid blue (on screen) | unchanged: the **one** amber frame |
| Block outline while staged (`Style.Shape.BlockState` `State=Staged`) | amber dashed 3 px | neutral `Brush.Border.Strong` **dashed** 2 px (dash = "not on screen yet" without a second amber frame) |
| Block outline, live | blue | unchanged (blue = on screen) |
| Armed place overlay (`Style.Panel.PlaceOverlay`) | cyan 3 px ring + `ScrimTint` fill on every free block | cyan 2 px ring, **no tint fill** (the pill already sits on `ScrimAccent`); cyan stays reserved for armed |
| Undo button border | green `Brush.Status.Online` | `Brush.Border.Control` (green stays for the success banner only) |
| Header chips `OUT 1920x1080@60 ...`, `RateNote` | `ui:Badge` `Style.Badge.Info` (blue pill) | plain `Style.Text.Meta` (Caption, `Brush.Text.Secondary`) separated by spacing; status is text, not a pill (M5). The palette "in N blocks" badge stays amber (it is the staged state) |

* Files: `Resources/Styles/Composer.xaml` (BlockState, PlaceOverlay, Status.Staged, RailHeading.Staged, Undo, new `Style.Text.Meta`, new `Style.Shape.StagedDot`), `Views/MultiViewComposerView.xaml` (header chips, status label dot). `Badges.xaml` is **not** touched (shared): the Composer stops using `Style.Badge.Info` in its header only.
* Tests: `Composer_StagedColours_AreOneAmberFrame` (canvas frame stroke == `Status.Staged`, block `BlockState` stroke for a staged block == `Border.Strong`, `StrokeDashArray` non-empty; STAGED label foreground == `Text.Primary`); `Composer_HeaderChips_ArePlainText` (update the round-1 test `Composer_HeaderChips_AreBadges_AndCollapseWhenEmpty`: no `Badge` in the header, texts visible, empty one collapsed); `Composer_ArmedOverlay_HasNoTintFill`; update `Composer_Undo_...` (border). Contrast: new neutral dashed stroke vs the block surface >= 3:1 in both themes (measure, do not assume).
* Conflicts: D12 allows plain text chips; icon guard n/a; `ThemeResourceUsageTests` unchanged (overlay text tokens stay in the view). The round-1 header-badge test and one Light-mode test that reads the live tile are edited, listed in section 4.
* Commits **R2-3a** (blocks/canvas/armed/Undo) and **R2-3b** (text headings + header chips + `Style.Text.Meta`).

### C8 overlay labels are long and sit on busy video

* Today: `PlaceText` = `+ Place {DeviceName}` / `Replace with {DeviceName}`, one line, wraps, inside `PlacePill` (already on `Brush.Surface.ScrimAccent`, so the scrim exists; the banner already prints the full name `Placing X - tap a block...`).
* Target: two-line pill: line 1 the verb (`+ Place` / `Replace`), line 2 the source name, `TextTrimming=CharacterEllipsis`, `MaxWidth` = `Size.Composer.Overlay.Text.Max`. Compact blocks: verb only (the name is in the banner and tooltip). Full name in a `ToolTip` on `BlockSurface` bound to `DataContext.ArmedSource.DeviceName` (`TargetNullValue={x:Null}` so no tooltip when nothing is armed; the overlay is `IsHitTestVisible=False`, so the tooltip belongs on the surface button). Touch has no hover: the banner is the touch path, so nothing is lost.
* Files: `Views/MultiViewComposerView.xaml` (PlacePill content: two `TextBlock`s, **each keeps its `Foreground` `Brush.Text.OnScrimAccent` attribute**), `Resources/Styles/Composer.xaml` (`Style.Text.Block.PlaceVerb`, `Style.Text.Block.PlaceName`; retire `Style.Text.Block.PlaceAction`'s text setters), `tests/ThemeResourceUsageTests.cs` (add `InlineData` rows for the two new named blocks `PlaceVerbText`, `PlaceNameText`).
* Tests: `Composer_PlacePill_TruncatesLongNames_AndTheTooltipHasTheFullName` (a 40-char device name: pill `ActualWidth <= MaxWidth + padding`, `PlaceNameText` is trimmed, `BlockSurface.ToolTip` equals the name, tooltip null when disarmed); `Composer_PlacePill_Compact_ShowsVerbOnly`. Existing `Composer_SmallBlocks_AreCompact_ThroughBlockDensity` (font >= 12) stays green.
* Conflicts: spoken names unchanged (`BlockSurface` `AutomationName` is the VM's, not the pill's).
* Commit **R2-4**.

### C4 right rail dead zone, empty ON SCREEN NOW

* Today: row 0 is `RightRailScroll` (`*`) holding a `StackPanel` of two cards, row 1 the pinned cluster (Auto). With little content the scroll viewer is taller than its content, so a gap shows between Changes and the cluster.
* Target: keep the element name and the touch panning (`RightRailScroll`, `PanningMode=VerticalOnly` are pinned by tests) and make the content fill the viewport: the scroll content becomes a `Grid` with rows `Auto` (ON SCREEN NOW) and `*` (CHANGES) whose `MinHeight` is bound to the scroll viewer's `ViewportHeight` (`{Binding ViewportHeight, RelativeSource={RelativeSource AncestorType=ScrollViewer}}`), so the Changes card **grows** to the free height and the list scrolls inside the rail only when it must. The banner already collapses on `Kind=None`.
* Empty preview: when `LiveBlocks` is empty, show a name plate over the black mat: the display name (`DisplayName`, new read-only VM property `=> _display.DeviceName`) in `Style.Text.Title` with `Foreground` `Brush.Text.OnPreview` (video-overlay rule) and the sentence `No MultiView on this screen` below it in `Brush.Text.OnPreviewMuted`. `OnScreenSummary` (the spoken name of `OnScreenPicture`) is unchanged and stays pinned.
* Files: `Views/MultiViewComposerView.xaml`, `Resources/Styles/Composer.xaml` (`Style.Card.Rail.Fill`, `Style.Text.Block.NamePlate`), `ViewModels/MultiViewComposerViewModel.cs` (one read-only property `DisplayName`, raised in `RaiseDerived`? it never changes: no notification needed), `tests/ThemeResourceUsageTests.cs` (+1 `InlineData` row for the new `NamePlateText`).
* Tests: `Composer_RightRail_ChangesCardFillsTheFreeHeight` (1904x940, one change: CHANGES card bottom within 1 px of the scroll viewport bottom; gap between the card and the pinned cluster <= 8 px); `Composer_RightRail_StillScrolls_At1008x615` (scroll viewer `ScrollableHeight > 0` with 4 changes, Take visible: existing `Composer_InVisualRoutingView_AtRealWindowSizes_...` stays green); `Composer_OnScreenPicture_EmptyShowsTheDisplayName`; `Composer_ArmingDoesNotMoveTake` (regression for M1's intent: `Take` bounds equal before/after `Arm`).
* Conflicts: none with D1-D12; the `Grid` MinHeight binding is a binding, not a literal (guard-clean).
* Commit **R2-5** (after R2-3a: shares `Style.Card.Rail`).

### C6 left rail steps 2 and 3 are instruction text

* Target: left rail = **Step 1 only** (heading + 5 tiles) + the unplaced-sources panel (which is state, not instruction; it keeps its place under the tiles with its notice). Step 2 heading moves to the canvas status row (left of the STAGED/ON SCREEN line: `StepHeader 2 PLACE SOURCES` then the status label, `Done placing` on the right). Step 3 heading moves into the pinned cluster, directly above the banner/Take. The two instruction sentences are dropped: the empty blocks already say `Tap to choose a source`, and the armed banner says what to do; keep them as `AutomationProperties.HelpText` on the two headings for screen-reader users.
* Space check (this is the risk): the status row is 48 px already, so the step 2 header (32 px marker) fits in it; the pinned cluster gains about 40 px of height, which matters at 1024x720 (round-1 risk 1: ON SCREEN NOW already scrolls there). Mitigation in the plan: at the 1008x615 floor the step 3 header collapses to its marker next to the `TakeBlockedReason` line (a `Converter.WidthAtLeast`-style trigger does not exist for height: use a `Style` trigger on `ActualHeight` of the rail via the existing `LessThanConverter` pattern with a `Size.Composer.Rail.Compact.Height` token, or measure first and only add the mitigation if `Composer_InVisualRoutingView_AtRealWindowSizes_*` goes red).
* Files: `Views/MultiViewComposerView.xaml` (move two `controls:StepHeader`s, delete two `TextBlock`s; names `StepPlaceHeading`, `StepTakeHeading` unchanged), `Resources/Styles/Composer.xaml` only if the mitigation is needed. No code-behind: `Named(...)` lookups are by name.
* Tests: update `Composer_StepHeadings_AreHeadings_...` only if it asserts position (it asserts name, state words and HeadingLevel2 -> unchanged); `Composer_LeftRail_HoldsOnlyStep1_AndUnplaced` (no `StepHeader` numbered 2/3 under `LeftRailScroll`, their `Name`s found under the canvas column / right column); `Composer_TabOrder_*` unchanged (StepHeaders are not tab stops); Light-mode `done` header test already iterates all headers.
* Conflicts: none.
* Commit **R2-6** (after R2-5).

### C1 step 1 canvas is a void

* **(a) Ghost schematic (view only), commit R2-7.** Replace the centred text block (`Style.Panel.Placeholder`, visible while `Draft` is null) by: a large `views:LayoutSchematic` inside the canvas's 16:9 `FractionalLayoutPanel`, `Opacity` from a style (not a literal in the view), over the mat, with the existing two sentences below it. A view-level dependency property `PreviewLayoutId` on `MultiViewComposerView` drives it; code-behind sets it from the tiles: `MouseEnter`/`GotKeyboardFocus` on a `LayoutTile` -> that tile's `Id`; `MouseLeave`/`LostKeyboardFocus` -> the default (`matrix2x2` now, last-used after R2-8). Selecting a tile clears the ghost because `Draft` becomes non-null (no change). `LayoutSchematic` already scales by its container (round 1), and its cell numbers are 12 px (floor).
  Files: `Views/MultiViewComposerView.xaml(.cs)` (DP + two handlers on the tile `ItemTemplate`: wire in code via `AddHandler` on the items host to avoid per-item event attributes), `Resources/Styles/Composer.xaml` (`Style.Panel.Ghost`).
  Tests: `Composer_Step1_ShowsAGhostSchematic_NotABlankCanvas` (no draft: a visible `LayoutSchematic` named `GhostSchematic` with `LayoutId` `matrix2x2`; after `RaiseEvent(MouseEnterEvent)` on the PIP tile its `LayoutId` is `pip`; after `MouseLeave` back to the default; with a draft it is not visible); `Composer_Ghost_FollowsKeyboardFocus`; ghost is not a tab stop and has no automation name (`IsHitTestVisible=False`, `AutomationProperties.AccessibilityView=Raw`).
  Conflicts: UiV2Guards: no literal sizes; the `Opacity` lives in the style. No new allowlist entry.
* **(b) Remember the last layout, commit R2-8 (only if Q1 = yes).** `AppConfig.LastComposerLayout` (string, default `""`), saved through `IConfigService.Save` when the Composer selects a layout (`MainViewModel` subscribes to the Composer's selection; `ConfigService` is already injected there), read in `OpenComposerAsync` when `layoutId == null`, no live layout and no resumed draft: stage it with the existing `StageAndArmAsync(_composer, layoutId, armed)` path. The ghost default becomes this id. Tests: `MainViewModelComposerTests.OpenComposer_NoLayout_UsesTheLastUsedLayout`, `..._WithLive_KeepsTheLive`, `..._WithResumedDraft_KeepsTheDraft`, `ConfigServiceTests` round-trip of the new field; the VM-level rig tests (`rig.Composer()` with no layout -> `Choose a layout (Step 1).`) stay unchanged because the preselect lives in `MainViewModel`, not in the Composer VM.
  Conflicts: `MainViewModel.cs` and `AppConfig.cs` are not Composer-owned: **needs the master's go-ahead and a hand-off to avoid editing the same file as the Main Window session** (they own `MainViewModel` changes for M1/M3). Keep the change to one method and one property, committed alone.

### C7 / M2 one shared dense source strip (shared with the Main Window session)

The Composer already uses a strip (`CompactSourceCardView` inside `CompactSourcePalette`, 340 min, 144 px thumbnail, ~99 px high, left-aligned). What is missing versus the competitor is **density and one component for both screens**.

* Composer-side proposal (files: `Views/CompactSourceCardView.xaml(.cs)`, `Resources/Styles/Composer.xaml`): a vertical tile like the competitor tray: 16:9 thumbnail on top (`Size.Composer.Palette.Thumb` -> a tile width token, about 176), the name under it on one line (trim, tooltip = full name), state as a **dot + word** in the same line (`Online`/`Offline`, no pill: M5), `in N blocks` amber badge and `Placing` cyan badge overlaid on the thumbnail corner, idle preview shows the last frame or a name plate (a `Style.Text.Block.Tiny` name over `Brush.Surface.PreviewBlack` when `PreviewBitmap` is null; `NO PREVIEW` stays for non-AVAS). Height about 130-140 px, which is within the existing `strip <= 160` test; width by count (`ItemsControl` stays a horizontal `StackPanel`; `UniformGrid` is not used because the strip scrolls).
* Public contract for the Main Window session (agree through the master): `CompactSourceCardView` takes a `SourceCardViewModel` DataContext, tap arms/disarms, drag places, `x:Name`s `UsedInBlocksBadge`, `ArmedRing`, `FocusRing` and automation name `AutomationName` stay; its width token moves from `Size.Composer.Palette.Card.Min` to a shared `Size.SourceStrip.Tile.Min` in `Tokens.xaml` (master-owned). The overview would host it in a single wrapping strip and move Start/Stop/Standby/info to a per-tile flyout: that is **their** change to `VisualRoutingView.xaml` and `SourceCardView.xaml`; this plan does not touch those files.
* Tests: `CompactSourceCard_IsADenseVerticalTile` (thumbnail width == tile width, 16:9, `DesiredSize.Height <= 140`), `CompactSourceCard_Idle_ShowsANamePlate`, `CompactSourceCard_StatusIsDotAndWord` (no `Badge` for online/offline), keep `CompactSourceCard_Badges_AreUiBadges`, `SourceCards_Keyboard_*`, `Composer_InVisualRoutingView_AtRealWindowSizes_*` (strip <= 160). Add `VideoOverlayText` rows for the name plate.
* After the Main Window merge (also round-1 follow-up): bind `UsedInBlocksBadge` to `SourceCardViewModel.UsedInText`/`HasUsage` and delete `Style.Badge.UsedIn`'s `ContentStringFormat` triggers (A3 `1143cfa` is now on integration if the Main Window checkpoint merged it: verify before starting).
* Commit **R2-9**, after the master settles Q4. Blocks nothing else.

## 3. Commit order (atomic, each green on its own)

| # | Commit | Findings | Depends on |
|---|---|---|---|
| R2-1 | Change lines read as words | C5 | none |
| R2-2 | Done placing becomes secondary | C2 | none |
| R2-3a | One amber frame; neutral staged blocks; lighter armed overlay; neutral Undo | C3, M5 | Q2 |
| R2-3b | Neutral STAGED/CHANGES headings with an amber dot; header chips as plain text | C3, M5 | R2-3a |
| R2-4 | Two-line, truncating place pill with tooltip | C8 | none |
| R2-5 | Right rail fills the free height; display name plate; `DisplayName` | C4 | R2-3a |
| R2-6 | Steps 2 and 3 move out of the left rail | C6 | R2-5, Q3 |
| R2-7 | Ghost schematic on the empty Step 1 canvas | C1 (a) | none |
| R2-8 | Remember the last layout (optional) | C1 (b) | Q1, master go-ahead (touches `MainViewModel`, `AppConfig`) |
| R2-9 | Dense source tile shared with the overview | C7, M2 | Q4, Main Window session |
| R2-10 | Harness: realistic neutral source frames, refreshed 20 screenshots + before/after numbers | all | after R2-1..R2-7 |

R2-1, R2-2, R2-4 and R2-7 can be done in any order or in parallel (disjoint regions of `Composer.xaml`; keep each commit to its own styles to keep merges trivial).

## 4. Existing tests that will change (and why)

| Test | Commit | Change |
|---|---|---|
| `MultiViewComposerViewModelTests` ChangeLines (~152, ~1054), `MainViewModelComposerTests` (~299) | R2-1 | new wording |
| `Composer_HeaderChips_AreBadges_AndCollapseWhenEmpty` | R2-3b | now plain text, still collapses when empty |
| Undo border / `Composer_Undo_*` | R2-3a | border colour |
| `ThemeResourceUsageTests.VideoOverlayText_...` | R2-4, R2-5, R2-9 | new `InlineData` rows (new named overlay texts) |
| `Composer_StepHeadings_...`, `Composer_TabOrder_*` | R2-6 | expected to stay green; re-run |
| `Composer_Step1_*` placeholder assertions | R2-7 | placeholder text now sits under the ghost |
| `ViewSmokeTests.ComposerShots.cs` | R2-10 | frames, new states (Step 1 hover) |
Everything else (keyboard, drag/drop, focus rings, PIP z-order, real window sizes, picker, automation names, 72 px Take) is the regression net and must stay green untouched.

## 5. Coordination with the Main Window session (through the master; no shared files)

* **Shared by name:** `CompactSourceCardView` (Composer-owned) as the one strip tile (Q4); a shared token `Size.SourceStrip.Tile.Min` in `Tokens.xaml` (master); the accent roles table in section 2 (C3) if Q2 is accepted, so the overview's pills (`ONLINE`, `DUAL LINK`, `DISCOVERED`) follow the same rule in `Badges.xaml` (foundation/master-owned).
* **Composer-owned files in this plan:** `MultiViewComposerView.xaml(.cs)`, `Composer.xaml`, `CompactSourceCardView.xaml(.cs)`, `LayoutSchematic.xaml`, `MultiViewComposerViewModel.cs`, `ViewSmokeTests.Composer.cs`, `ViewSmokeTests.ComposerShots.cs`, Composer test files.
* **Not touched without the owner:** `VisualRoutingView.xaml`, `SourceCardView.xaml`, `DisplayCardView.xaml`, `Cards.xaml`, `Badges.xaml`, `Tokens.xaml`, `Resources/Themes/*`, `MainViewModel.cs`, `AppConfig.cs` (only R2-8, with approval).
* `LayoutSchematic` and `FractionalLayoutPanel` are shared with the display card's layout menu: R2-7 enlarges one instance but changes neither class; re-run the `DisplayCard` layout-menu tests anyway.
* M3 (inline layout preset buttons under each display, Main Window) will reuse `LayoutSchematic` and the five layouts (now including `pip_large`): nothing in this plan blocks it.

## 6. Guard and decision check

* D1/D12: no new window chrome; `Done` stays a native `Button`; chips become plain text, not a new control. D3: no new glyph icons (the amber dot is a `Shape`, not an icon; `->` is ASCII). D4: no new touch targets; the 36 px compact strip exception stays. D8: every new text uses `Font.Size.*` tokens (>= 12). D9: Full HD numbers re-measured by R2-10; canvas must stay >= 1144x635 at 1920x1080 and rails 320/360. D11: no list added.
* Guards: no keyed style/template/literal size in view files (all new styles in `Composer.xaml`, sizes via `Size.Composer.*`); the allowlist keeps exactly one entry (the zero-size `Canvas` anchor).
* Theme contract: only existing `Brush.*` keys, `DynamicResource`, both themes identical; every colour claim in this plan (dashed neutral stroke >= 3:1, Done outline, name plate text) is **to be measured in R2-3a/R2-2/R2-5 with the existing contrast helper**, not assumed.

## 7. Risks

1. Q2 changes meaning of colours the operators already learned; screenshots in R2-10 must be reviewed by the user before R2-3b lands on `main`.
2. C6 adds height to the pinned right cluster; the 1024x720 floor may force the marker-only fallback.
3. R2-8 puts an unchosen layout on the staged picture; it is gated on Q1 and kept as its own revertable commit.
4. The `ViewportHeight` binding for the right rail is the only new layout trick: if it misbehaves with touch panning, fall back to `DockPanel`-free `Grid` rows without the outer `ScrollViewer` and a `MaxHeight` on the list (and update the `RightRailScroll` assertions).
5. Unmeasured: all contrast estimates from the critique; Narrator wording for the moved headings (round-1 risk, still manual).
