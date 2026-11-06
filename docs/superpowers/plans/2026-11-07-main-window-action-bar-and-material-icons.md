# Main Window Action Bar Consistency and Material Symbols Icons Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** On the Main Window (Visual Routing tab and the shared app bar) the display-card action bar uses one consistent labelled ghost-button style ("More layouts", "Copy layout", power), and every icon on those screens is a Google Material Symbols Rounded icon.

**Architecture:** Import about 30 Material Symbols Rounded SVGs once with a script into `Resources/MaterialIcons.xaml` as `Geometry` resources in a 24 x 24 box (no font file). A small `MaterialIcon` control (`Kind` string, tinted by `Foreground`, sized by `Width`/`Height`) replaces `ui:SymbolIcon` and the hand-drawn `Path` icons on the Main Window. The action bar gets one `Style.Button.ActionBar` used by Layout and Copy layout, with spacing from tokens.

**Tech Stack:** .NET 8 WPF, WPF-UI 4.3.0, xUnit STA tests in `tests/AvasRoutingTool.Tests`, PowerShell for the import script.

**Spec:** User feedback 2026-11-07 (proposed action bar image saved as `docs/superpowers/specs/assets/2026-11-07-action-bar-proposal.jpg` in Task 0) and grill answers Q1 to Q6 recorded below; design rules `docs/superpowers/specs/2026-10-29-ui-v2-master-guidelines.md`; locked decisions in `docs/superpowers/handoff/2026-11-04-ui-round3-handoff.md`.

## Decisions (grill answers, binding)

- Q1: "More layouts" (with a chevron after the label) and "Copy layout" are the same ghost style: no fill, no outline, 48 px high, same horizontal padding. Power, pencil, info, theme and Refresh stay icon-only with tooltips.
- Q2: icons are vector shapes from Google's open-source repository (github.com/google/material-design-icons, Apache-2.0), downloaded once by the import script. The user approved the download (about 30 small SVGs, a few hundred KB).
- Q3: scope is the Visual Routing tab and the shared app bar (Settings, theme, Refresh, Logs toolbar), including the rename dialog and the Layout menu popup. Composer, Room Cameras, PTZ pad, Advanced and Settings keep their current icons for now.
- Q4: the brand mark (`BrandMark`, `Icon.Logo.*`) and the layout schematics (`LayoutSchematic`) stay custom. The "More layouts" button icon is Material `grid_view`.
- Q5: Rounded, weight 400, outline (not filled), sized by the existing `Size.Icon.S` 16, `Size.Icon.M` 22, `Size.Icon.Layout` 20 and `Size.Icon.L` 28 tokens. Filled variants only if a later task needs a selected state.
- Q6: build on branch `claude/ui-round3-6710d5`; Sonnet subagents build, a separate Sonnet QA pass after each task; tasks run one at a time (they share `Icons`/styles files). Haiku only for simple retrieval.

## Global Constraints

- WPF-UI controls only; touch targets 48 px (36 px compact); font floor 12 px; status = dot + word, never colour alone.
- Accent roles: blue = selection/primary, cyan = armed only, amber = staged only, green = success only, red = errors only. No hard-coded colours (guards in `UiV2Guards`, allowlist `UiV2Guards.allowlist.json`, must not be widened).
- Display card chrome rows stay 24 / 56 / 64 and `GridSpec.DisplayCard.ChromeHeight` 170; the action bar row stays 56 px high with 48 px buttons. Cards are at least 400 px wide (carousel rule), so the action bar must fit 400 px: More layouts + Copy layout + power without clipping or ellipsis at 100 % and 150 % DPI.
- Never merge to `main`, never push. Commit messages end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.
- Material Symbols is Apache-2.0: ship the notice in `THIRD-PARTY-NOTICES.md` (Task 1).

## Icon map (Fluent or hand-drawn to Material Symbols Rounded name)

| Where | Today | Material name |
|---|---|---|
| App bar: Settings | Settings24 | `settings` |
| App bar: Refresh | ArrowSync24 | `sync` |
| App bar: theme toggle | WeatherSunny24 / WeatherMoon24 | `light_mode` / `dark_mode` |
| Logs toolbar | Broom24, FolderOpen24 | `cleaning_services`, `folder_open` |
| Source card | Edit24, Info24, Play24, Stop24, ArrowSync24, Icon.Video | `edit`, `info`, `play_arrow`, `stop`, `sync`, `videocam` |
| Display card | Edit24, Info24, Checkmark24, LockClosed24, ChevronDown24, Icon.Copy, Icon.Grid, Icon.Power, Icon.Monitor | `edit`, `info`, `check`, `lock`, `expand_more`, `content_copy`, `grid_view`, `power_settings_new`, `desktop_windows` |
| Carousel arrows, banners | ChevronLeft24/Right24, Dismiss24, Warning24 | `chevron_left`, `chevron_right`, `close`, `warning` |
| Section headers | Icon.Video, Icon.Monitor | `videocam`, `desktop_windows` |

## Review Focus

- Dark and light theme: every icon follows `Foreground` (no baked colour); a disabled button dims its icon.
- Crispness at 100, 125 and 150 % DPI for 16, 20 and 22 px icons (geometry in a 24 box scaled by `Width`/`Height`; no pixel snapping blur beyond today's).
- An unknown `Kind` must fail loudly in tests (never a silent blank icon): a test lists every `Kind` used in XAML and asserts a `MIcon.<kind>` resource exists.
- Label fit: "More layouts" + chevron, "Copy layout" and the power button fit a 400 px card; the long device name still ellipsizes, the labels never do.
- Accessibility: every icon-only button keeps `AutomationProperties.Name` and a tooltip; "More layouts" button name is "More layouts for <device>" (the menu popup title stays "Layout for <device>").
- The Composer and other out-of-scope views still render (they keep `ui:SymbolIcon`); `UiV2IconFontTests` still covers those.

---

### Task 0: Save the proposal image and write the baseline

**Files:**
- Create: `docs/superpowers/specs/assets/2026-11-07-action-bar-proposal.jpg` (copy of `C:\Users\POC-615\.claude\uploads\ea1b6a89-1bd1-4991-a4d2-eaea7e34a0ff\0ea7b260-image.jpg`)
- Create: `ui_screenshots/UI-v2-round3/before-actionbar-1920.png` (render of the current Main Window with the existing `UiV2Round3Shots` test; force-add, the folder is git-ignored)

- [ ] **Step 1:** copy the image, render the before shot (`AVAS_UIV2_SHOTS=<abs path of ui_screenshots/UI-v2-round3>`, filter `FullyQualifiedName~UiV2Round3Shots`), commit `docs(ui-round3): action bar proposal and baseline screenshot`.

### Task 1: Material icon pipeline and the `MaterialIcon` control

**Files:**
- Create: `tools/icons/import-material-icons.ps1`, `src/AvasRoutingTool/Resources/MaterialIcons.xaml`, `src/AvasRoutingTool/Controls/MaterialIcon.cs`, `THIRD-PARTY-NOTICES.md` (or append if present)
- Modify: the resource dictionary merge list where `Icons.xaml` is merged (find it in `App.xaml`)
- Test: `tests/AvasRoutingTool.Tests/MaterialIconTests.cs`

**Interfaces:**
- Produces: `Geometry` resources keyed `MIcon.<name>` (24 x 24 coordinate space) for every name in the icon map; `Controls.MaterialIcon : Control` with `string Kind` (dependency property), `Foreground` (inherited), default `Width`/`Height` `Size.Icon.M`; looks up `MIcon.<Kind>` from resources; throws `InvalidOperationException` naming the Kind when the resource is missing.

- [ ] **Step 1: Write the failing tests** in `MaterialIconTests`: `EveryMapIcon_HasAGeometryInA24Box` (each name in the map: resource exists, `Bounds` inside 0..24 with at most 1 px of overshoot, non-empty); `MaterialIcon_UnknownKind_ThrowsWithTheKindName`; `MaterialIcon_FollowsForeground_InBothThemes` (render, read the fill brush from the template path); `MaterialIcon_SizeComesFromWidthAndHeight`.
- [ ] **Step 2: Run to verify fail:** `dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj -p:BaseOutputPath=bin/r3/ --filter "FullyQualifiedName~MaterialIconTests" -- RunConfiguration.DisableParallelization=true`. Expected: FAIL (type and resources missing).
- [ ] **Step 3: Implement** the import script: downloads `https://raw.githubusercontent.com/google/material-design-icons/master/symbols/web/<name>/materialsymbolsrounded/<name>_24px.svg` for each name in the map, converts each path (viewBox `0 -960 960 960`) to a 24 x 24 `StreamGeometry` string (scale 1/40, y + 960 then /40), and writes `MaterialIcons.xaml`; commit the generated XAML and the downloaded SVGs under `tools/icons/svg/`. Implement `MaterialIcon` and merge the dictionary. Add the Apache-2.0 notice.
- [ ] **Step 4: Run to verify pass.**
- [ ] **Step 5: Commit** `feat(icons): Material Symbols Rounded geometry set and MaterialIcon control`.

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

### Task 3: Display card icons to Material

**Files:**
- Modify: `DisplayCardView.xaml` (header edit/info, copy-source and lock icons, check marks in the layout menu, monitor placeholder), `Resources/Styles/DisplayCard.xaml` (`Style.Icon.Button*` Path styles)
- Test: `ViewSmokeTests.UiV2DisplayCard.cs` additions; a guard test below

**Interfaces:** Consumes `MaterialIcon`.

- [ ] **Step 1: Write the failing tests:** `DisplayCard_UsesNoFluentOrHandDrawnIcons` (no `SymbolIcon` and no `Icon.<name>` Path except `Icon.Logo.*` in `DisplayCardView` visual tree; every `MaterialIcon.Kind` resolves); `DisplayCard_IconSizes_FollowTokens` (header icons 16, action icons 20).
- [ ] **Step 2: Run to verify fail. Step 3: Implement** (replace per the icon map). **Step 4: Run to verify pass** (`ViewSmokeTests|UiV2Guards`). **Step 5: Commit** `feat(display-card): Material icons`.

### Task 4: Source card, app bar, carousel arrows, banners, section headers

**Files:**
- Modify: `Views/SourceCardView.xaml`(+`.cs` if icons are set in code), `MainWindow.xaml` (app bar icons, Logs toolbar, theme toggle in code or style), `Views/VisualRoutingView.xaml` (arrows, rename banner icons, section header icons), `Resources/Styles/Buttons.xaml` (`Style.Icon.Action`, `Style.Icon.ThemeToggle`, `Style.Button.CarouselArrow`), `Resources/Styles/Cards.xaml` (`Style.Icon.Section`)
- Test: `tests/AvasRoutingTool.Tests/MainWindowMaterialIconsTests.cs`; update `UiV2AppBarTests`, `UiV2IconFontTests` (still covers the out-of-scope views), `MainWindowHeaderTests`

- [ ] **Step 1: Write the failing tests:** `MainWindow_VisualRouting_HasNoFluentSymbolIcons` (visual tree of the Main Window on the Visual Routing tab and the app bar: no `SymbolIcon`; `Icon.Logo.*` brand marks allowed); `AppBar_ThemeToggle_SwapsSunAndMoonKinds`; `SourceCard_StartStop_UsePlayAndStopKinds`; `Arrows_AreMaterialChevrons`.
- [ ] **Step 2: Run to verify fail. Step 3: Implement. Step 4: Run to verify pass.**
- [ ] **Step 5: Commit** `feat(main-window): Material icons on source cards, app bar, arrows and banners`.

### Task 5: Screenshots, full suite, final QA

**Files:** `ui_screenshots/UI-v2-round3/after-actionbar-1920.png`, `after-actionbar-1024.png`, `after-actionbar-light-1920.png` (light theme)

- [ ] **Step 1:** render the after shots with the `UiV2Round3Shots` test; open them and report what you see (labels not clipped, icons aligned to text baseline, light theme legible).
- [ ] **Step 2: Run the full non-live suite:** `dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj -p:BaseOutputPath=bin/r3/ --filter "FullyQualifiedName!~LiveControlServerTests&FullyQualifiedName!~LiveFunctionTests" --blame-hang-timeout 120s -- RunConfiguration.DisableParallelization=true`. Expected: 0 failed (rerun once for the known STA flake).
- [ ] **Step 3: Commit** `docs(ui-round3): action bar and Material icons screenshots`.

## Subagent execution (recommended)

Subagent-driven, strictly in order Task 0 to 5, one Sonnet implementer per task (the tasks share style and resource files, so no parallel builds), then a read-only Sonnet QA reviewer per task or per pair of small tasks, and one whole-branch QA at the end. Haiku only for retrieval, for example checking which `Style.*` keys are still referenced after Task 2. Each implementer gets its task text as a brief file, the Decisions and Global Constraints sections, and the Icon map; the controller diff-reviews every task and records red-then-green evidence.

## Backlog (not in this plan, user-ordered)

1. Next priority: the same icon and button-consistency round for the Multi-view Composer window (icons: `ArrowLeft24`, `ArrowUndo24`, `Checkmark24`, `Dismiss24`, `LockClosed24` and the Composer button styles in `Resources/Styles/Composer.xaml`).
2. Then Room Cameras, PTZ pad, Advanced and Settings icons.
3. Retire `ui:SymbolIcon` and the Fluent font once nothing uses it.
