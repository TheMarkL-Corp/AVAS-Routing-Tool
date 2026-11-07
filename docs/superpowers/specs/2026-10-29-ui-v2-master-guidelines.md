# UI v2 Revamp: Master Guidelines & Orchestration

Owner: **master session** (this document). Status: planning only, no production code changes yet.
Nested planning sessions: **A. Main Window / Visual Routing** and **B. MultiView Composer**.

## 0. Locked decisions (user-approved 2026-10-29; these override anything below and any draft plan)

| # | Topic | Decision |
|---|---|---|
| D1 | WPF-UI depth | **Theme and controls only.** Merge `ui:ThemesDictionary` + `ui:ControlsDictionary`; keep the existing `Window` with normal Windows chrome and the existing app bar (underline tabs). **No `FluentWindow`, no `TitleBar`, no `NavigationView`.** |
| D2 | Safety / revert | Everything lives on branch `ui-v2/integration`. `main` is NOT merged until the user approves. Revert point: tag `pre-ui-v2` (= `main` at `46d546b`). |
| D3 | Icons | Brand/custom icons stay `StreamGeometry` in `Icons.xaml`; stock glyphs use `ui:SymbolIcon`. Guard test bans bitmap, emoji and Unicode-glyph icons. Exempt: `AppIcon.ico` and the live-video `PreviewBitmap` `<Image>` surfaces. |
| D4 | Touch targets | `Size.Touch.Min` = 48 px (primary actions: route, take, power, app bar, tabs, card actions). `Size.Touch.Compact` = 36 px (dense secondary controls, e.g. source-card stream controls) with >= 8 px spacing. Draft tokens such as Comfort 56 / Primary 72 are dropped. One recorded exception: the Composer's Save & apply is a `Style.Button.ActionBar` button filled with the action colour (6 px radius, Body SemiBold, icon, sentence-case label) at `Size.Composer.Take.Min` = 56 px, because it is the only action that changes what the surgeon sees. |
| D5 | `LayoutsBarView` | **Delete** `LayoutsBarView.xaml(.cs)`, its standalone smoke test, the six "absent" assertions and the `Handover/02_CODEBASE_STRUCTURE.md` line. `LayoutSchematic` stays. |
| D6 | Guard tests (G3/G4) | **Hard guards** (no `<ControlTemplate>` or keyed `<Style>` in view files; no literal Width/Height outside static icons) with ONE justified allowlist file. Written first (red), green as each view migrates. |
| D7 | Review stages | Three user checkpoints on `ui-v2/integration`: (1) Foundation, (2) Main Window, (3) Composer. Sessions A and B implement in separate worktrees off the integration branch and merge back only at checkpoints. Each checkpoint delivers screenshots (1920x1080 and 1024x720, Dark and Light), test count, runnable branch. |
| D8 | Font floor | 12 px. Tokens 16/14/13/12. Guard test bans `FontSize` literals and anything below 12. |
| D9 | Design target | **1920x1080 at 100% scaling, maximized.** 1024x720 remains a safety floor (existing tests stay, not optimized for). Use the Full HD space (labels back on app-bar actions, larger previews, `MaxCardWidth` caps). No 150% scaling check. |
| D10 | Test file | First foundation commit splits `ViewSmokeTests.cs` into partial class files by area, a pure move with the test count unchanged (795). |
| D11 | Virtualization (G5) | Only the Logs `ListBox`, via central `Style.ListBox.Virtualized` (`IsVirtualizing`, `ScrollUnit=Pixel`, `Recycling`). Card grids and small `ItemsControl`s are exempt (cards own live RTP previews: zero-socket-leak rule), listed in the allowlist with reasons. |
| D12 | Controls | Native `Button`/`RadioButton` with central `Style.Button.*` and `Style.Tab.Nav` (template part name `bd` kept). WPF-UI components where no test coupling exists: `ui:Card`, `ui:ToggleSwitch`, `ui:TextBox`, `ui:Badge`, `ui:SymbolIcon`. No `ui:Button`. |

Resolved draft-plan questions: Session A O1 (caption buttons) -> moot by D1; O2 (36 vs 48 px) -> D4; O3 (`LayoutsBarView`) -> D5. Sessions' foundation requests (WPF-UI 4.3.0, merge order, shared `TestResources.Ensure()` helper, empty `Styles/SourceCard.xaml`, `DisplayCard.xaml`, `Composer.xaml`, etc.) are accepted in principle and will be answered by the master after the revised plans are cross-reviewed.

## 1. The five guidelines (as adopted for WPF)

| # | Guideline as given | Adopted rule for this repo |
|---|---|---|
| G1 | Use WPF-UI (lepoco/wpfui) to style the app | Add the `WPF-UI` NuGet package. Merge `ui:ThemesDictionary` + `ui:ControlsDictionary` in `App.xaml`. Prefer WPF-UI components per D1/D12 (`ui:Card`, `ui:TextBox`, `ui:ToggleSwitch`, `ui:Badge`, `ui:SymbolIcon`); buttons and tabs stay native with central styles; no `FluentWindow`/`NavigationView`. Verify the API names against the installed package version before planning (do not rely on memory). |
| G2 | Vector iconography only; "StreamGeometry or FontImageSource" | `FontImageSource` is .NET MAUI, not WPF. WPF equivalent: `StreamGeometry` resources in a `Path`, or WPF-UI font icons (`ui:SymbolIcon`, `ui:FontIcon`, Fluent System Icons). **No bitmap files as icons and no emoji or Unicode glyphs as icons** (e.g. the old sun/moon and the `ⓘ` badge). Custom brand marks stay `StreamGeometry` in `Resources/Icons.xaml`. Stock glyphs may use `ui:SymbolIcon`. Exceptions that are NOT icons: `Assets/AppIcon.ico` (OS requires it for exe/window/taskbar) and the live-video `<Image Source="{Binding PreviewBitmap}">` surfaces. |
| G3 | Decouple styles into `App.xaml` | All colors, control templates, fonts and reusable `Style`s live in centralized `ResourceDictionary` files merged in `App.xaml`. Views may hold only layout and bindings, plus at most a trivial one-off `Style` with a `BasedOn` of a central key. Target: zero `<ControlTemplate>` and zero keyed `<Style>` blocks in view files. |
| G4 | Dynamic layouts, no hard-coded sizes | `Grid` with `*`/`Auto`, `StackPanel`, `WrapPanel`, `UniformGrid`. No absolute `Width`/`Height` on child elements except static icons. `MinWidth`/`MinHeight` for touch targets (48 px) are allowed and must come from a named size token (e.g. `Size.Touch.Min`), not a literal. Existing `GridSpec` constants that drive the adaptive panels are a layout algorithm, not view sizing, and may stay if tested. |
| G5 | WPF-native UI virtualization | On every `ListView`/`ListBox`/`DataGrid`: `VirtualizingStackPanel.IsVirtualizing="True"`, `VirtualizingPanel.ScrollUnit="Pixel"`, plus `VirtualizationMode="Recycling"`. Must not be nested in an auto-height `ScrollViewer` (that silently disables virtualization). **Do NOT virtualize the source/display card grids**: each card owns a live RTP preview and multicast membership; recycling or lazily realizing cards would break the zero-socket-leak rule. |

## 2. Baseline audit (master, 2026-10-29)

Measured on the working tree (rough, regex-based; sessions re-verify):

* **WPF-UI**: not referenced anywhere (`csproj` has only `Microsoft.Web.WebView2`). Own token system: `Resources/Themes/Theme.Dark.xaml` / `Theme.Light.xaml` (identical keys, `DynamicResource` everywhere, `ThemeService` swaps in place) + `Resources/Theme.xaml` (378 lines) + `Resources/Icons.xaml` (17 geometries).
* **G2**: no bitmap icons found; `Icons.xaml` already vector. Remaining risk: glyph/emoji-as-icon text in views (inventory required).
* **G3**: central dictionary has only ~11 styles. Views embed inline styles: MultiViewComposerView 90 `<Style`, DisplayCardView 61, SourceCardView 34, VisualRoutingView 20, CompactSourceCardView 16, MainWindow 10 (incl. `Style.Tab.Nav`, `Style.Button.AppBar`, both in `MainWindow.Resources`), plus ~13 `<ControlTemplate>` in views. This is the biggest gap.
* **G4**: ~59 literal `Width=`/`Height=` attributes across views (DisplayCardView 16, MainWindow 13, Composer 12, plus SettingsDialog 5, VisualRoutingView 4). Fixed numeric chrome heights also live in `GridSpec`.
* **G5**: the only virtualizable list is the Logs `ListBox` (`MainWindow.xaml` ~line 325), with no virtualizing properties. Everything else is a small bounded `ItemsControl`.

File sizes that matter for planning: `MultiViewComposerView.xaml` 1315 lines, `DisplayCardView.xaml` 1113, `SourceCardView.xaml` 450.

## 3. Non-negotiable repo constraints (from CLAUDE.md, memory, tests)

1. **TDD**; any XAML change is verified by `ViewSmokeTests` on an STA thread. Run tests from a repo copy or with a fresh `-p:BaseOutputPath=bin/<name>/` (stale testhosts lock the bin DLLs). Baseline: 795 non-live tests pass.
2. **Theme contract**: Dark and Light dictionaries keep identical keys; palette refs are `DynamicResource`; no raw `#RRGGBB` outside `Resources/Themes/` (`ThemeResourceUsageTests`, hex guard, `HexCheckExemptFiles` stays empty). WPF-UI's theme must follow `ThemeService` (single source of truth; do not add `Dispatcher.Invoke` there, STA tests deadlock).
3. **Clinical OR ergonomics**: WCAG AA (>= 4.5:1), 48 px touch targets, visible focus rings, spoken `AutomationProperties` names, minimum window 1024x720 with the app bar still fitting, FloatingActionDock/Undo toast never occlude the 16:9 video canvas.
4. **Zero socket leaks** for RTP preview (see G5 note).
5. **Keep `Brush.*` semantic tokens as the app's own API**; WPF-UI resources are mapped to or driven from them, not the other way round, unless a session proves a cleaner design with tests.
6. Uncommitted work exists in the tree (version display `AppInfo`, Settings, header tests, `ShowLogs`). Do not revert or overwrite it; the nested sessions run in this same checkout.
7. Hardware/protocol code (`Services/`, orchestrators, SDK calls) is out of scope for UI v2.

## 4. Ownership matrix (prevents edit collisions)

| Area | Owner | Notes |
|---|---|---|
| `App.xaml`, `AvasRoutingTool.csproj`, `Resources/Theme*.xaml`, `Resources/Themes/*`, `Resources/Icons.xaml`, new `Resources/Styles/*`, `ThemeService` | **Master (foundation)** | Nested sessions *request* shared keys/styles via message; they do not edit these in planning. |
| `MainWindow.xaml(.cs)`, `VisualRoutingView`, `SourceCardView`, `DisplayCardView`, `SettingsDialog` (and deleting `LayoutsBarView`, D5) | **Session A** | `DisplayCardView` is A's even though it hosts multiview slots. |
| `MultiViewComposerView`, `LayoutSchematic`, `CompactSourceCardView` | **Session B** | |
| `tests/**` | Each session plans the tests for its own files | New test files named per area. |

## 5. Foundation contract (what both sessions can assume exists)

The master will specify and later build this foundation first. Sessions plan against it:

* `Resources/Styles/` merged from `App.xaml` after the WPF-UI dictionaries: `Buttons.xaml`, `Tabs.xaml`, `Text.xaml`, `Cards.xaml`, `Inputs.xaml`, `Lists.xaml` (virtualized list style), `Overlays.xaml`. Control templates live here.
* Naming: `Style.<Control>.<Variant>`, `Brush.<Role>[.<State>]`, `Icon.<Name>`, `Size.<Role>` (numeric tokens such as `Size.Touch.Min`), `Font.<Role>`.
* `ThemeService` additionally calls WPF-UI's `ApplicationThemeManager` so WPF-UI controls follow the app theme; accent comes from the app accent token.
* Icon policy as in G2; a short icon inventory table (old glyph -> new `Icon.*` or `ui:SymbolIcon`) is produced by each session for its files.

If a session needs a style, token or icon that is not in this list, it records it under "Foundation requests" in its plan and messages the master. It never invents a parallel mechanism.

## 6. Deliverable for each nested session (planning only)

Each session writes exactly one file, edits no code, commits nothing:

* A: `docs/superpowers/plans/2026-10-29-ui-v2-main-window.md`
* B: `docs/superpowers/plans/2026-10-29-ui-v2-multiview-composer.md`

Required sections:

1. **Audit** of its files against G1-G5 with counts and line references (inline styles, templates, literal sizes, glyph icons, lists).
2. **Target design**: what the screen looks like with WPF-UI (named controls), layout structure using `*`/`Auto`, how it behaves at 1024x720 and at large sizes. Mockup description or ASCII is fine; run the app if it helps.
3. **Style/icon migration map**: each inline style or template -> central key in which dictionary; each glyph -> icon.
4. **Layout de-hardcoding table**: every literal Width/Height -> replacement (token, `*`, `Auto`, or "static icon, keep").
5. **Virtualization decisions** (G5), including explicit "not virtualized because ..." entries.
6. **Constraint check**: how each item in section 3 stays satisfied; tests that must change and why.
7. **TDD task list**: ordered, small, each with the failing test first, files touched, and a verification command. Tasks sized for one commit each (code + memory update together, per CLAUDE.md).
8. **Foundation requests** (see section 5) and **risks / open questions** for the master.

## 7. Coordination protocol

* Sessions message the master (`SendMessage` to the master session) only for foundation requests or blocking cross-area questions. The master answers and updates this document's section 5.
* When both plans are done the master cross-reviews them for: duplicated style keys, conflicting tokens, shared-file collisions, order of execution, and overall consistency with G1-G5.
* Execution order (decided after review, expected): (1) foundation (WPF-UI package, dictionaries, ThemeService bridge, icon policy test); (2) Session A and B implementation in separate worktrees; (3) integration, visual check at 1024x720 and maximized, lab regression of routing.

## 8. Acceptance checklist for v2 (used in the cross-review and later in implementation)

* [ ] WPF-UI package merged in `App.xaml`; theme toggle drives both token dictionaries and WPF-UI.
* [ ] No bitmap/emoji/glyph icons; only `StreamGeometry` or WPF-UI font icons (guard test).
* [ ] No keyed `Style` or `ControlTemplate` in view files (guard test).
* [ ] No literal Width/Height on non-icon children (guard test with an allowlist for icons).
* [ ] Every `ListBox`/`ListView`/`DataGrid` virtualized per G5; card grids documented as exempt.
* [ ] 795+ non-live tests pass; ViewSmokeTests on STA green; WCAG AA and 48 px targets preserved.
