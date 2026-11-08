# UI v2: Cross-Review of the Main Window and Composer Plans (master)

Inputs: `docs/superpowers/plans/2026-10-29-ui-v2-main-window.md` (A, 14 tasks) and `docs/superpowers/plans/2026-10-29-ui-v2-multiview-composer.md` (B, 15 tasks), both revised to D1-D12 in `2026-10-29-ui-v2-master-guidelines.md`.
Status: **proposal for user approval.** Where this file and a plan disagree, this file wins; the sessions then apply the edits listed in section 5.

## 1. Verdict

Both plans follow D1-D12 (no FluentWindow, native buttons with central styles, `ui:Card/Badge/SymbolIcon`, hard guards, 12 px floor, Full HD target, only Logs virtualized, `LayoutsBarView` deleted). Their file ownership does not overlap. They are consistent on the hard parts: WPF-UI 4.3.0, merge order, shared test helper, test-file split, overlay-token rule for video. What is left is a set of naming/ownership conflicts (section 2), three plan-level exceptions that need the user's eye (section 3), and one real layout question for Full HD (section 4).

## 2. Conflicts and how the master resolves them

| # | Conflict | Resolution |
|---|---|---|
| X1 | **Icon sizes.** A's `Size.Icon.S/M/L` = 16/22/28 (F3). B says it adopted "A's 14/18/26", which is an older value. | Use A's final values: **S 16, M 22, L 28**. B maps its step marker/banner/back/undo icons to M (22) and the lock overlay to L (28); the 14 px lock sites become S (16). |
| X2 | **`Style.Text.Section`.** B builds `Style.Text.RailHeading` on A's `Section` role, but A's text table has only Lead/Title/Body/Caption/Mono. | Foundation defines `Style.Text.Section` = `Font.Size.Title` (14), SemiBold, uppercase letter-spaced, `Brush.Text.Secondary`, HeadingLevel2 settable by users. Roles are exactly: Lead 16, Title 14, Section 14 (caps), Body 13, Caption 12, Mono 12. No `Micro`; B's 20/18 roles dropped. |
| X3 | **Chip style.** A renamed its draft chip to `Style.Button.Chip.Draft`; B consumes "A's `Style.Button.Chip.Staged`" which A no longer defines. | ONE style, `Style.Button.Chip.Staged`, extracted by the foundation from today's `DisplayCardView` draft-chip template (keeps `InputModality.ShowsFocus`). A and B both consume it. `Chip.Draft` is dropped. |
| X4 | **Badges.** A and B each plan `Style.Badge.*` for the same "in N blocks" / "Placing" chips. | Foundation defines `Style.Badge.Info`, `Style.Badge.Staged`, `Style.Badge.Armed` (all `BasedOn {x:Type ui:Badge}`). B's Task 11 and A's A8 consume them; neither defines its own. Chips over video stay `Border` + scrim (both plans already agree). |
| X5 | **Converters.** A needs them early (A7); B plans to add `VisibilityConverter` in its Task 6. | Foundation builds the shared `VisibilityConverter { Rule: True, False, NotNull, Null, NotEmpty, HasItems, NoItems }`, `Converter.WidthAtLeast`, `Converter.BoolToGridLength` in a shared namespace, instances in `Resources/Converters.xaml`, with unit tests. B's Task 6 drops its converter work and consumes them. |
| X6 | **Guard ownership.** A1 creates the guard tests and allowlist; B's Task 3 registers its files and adds a temporary per-metric ratchet. | Foundation creates ONE `UiV2Guards` test class and ONE allowlist file (sections `Pending` file -> owner/task, `StaticIcons`, `UnvirtualizedLists`, `InlineStyleExceptions`). A1 and B3 only add entries. B's temporary per-metric ratchet for the 1315-line Composer is allowed (it gives red per task) but is deleted in B's Task 12. |
| X7 | **`Style.Button.Take`.** B moves it into `Composer.xaml`; foundation could also move it. | Foundation leaves it in `Theme.xaml`; B moves it (only user) in its Task 8. Same rule for any key only one session uses. |
| X8 | **`Icon.Lock`.** Last user outside Composer is `DisplayCardView` ~L884. | A moves that one to `ui:SymbolIcon LockClosed24` (A10); foundation deletes `Icon.Lock` and `Icon.Sun/Moon/Refresh/Settings` after both land. B confirmed it does not use the four. |
| X9 | **`LayoutSchematic` sizing.** A sizes it from outside by tokens (menu 72x40); B makes it proportional via `FractionalLayoutPanel` aspect rule. | Compatible: B's panel rule only applies when an axis is unbounded; A's token sizes bound both axes. Merge A first, then B, and run `DisplayCard` layout-menu tests after B merges. |
| X10 | **`SourceCardViewModel.UsedInText/HasUsage`** (A's A3) is used by B's Task 11. | A3 is a dependency of B Task 11; B keeps its trigger fallback if A3 has not merged yet. |
| X11 | **`Style.Button.Composer` at 48** also changes A's Stop button assertion (~L1130). | Foundation changes the style and both test edits ride in the split-file commit order: the Foundation commit adjusts the shared assertion once. |

## 3. Plan-level exceptions to D4/D8 that the user should accept or reject

1. **Take button stays 72 px** (`Size.Composer.Take.Min`, content-specific). It is the only action that changes what the surgeon sees, an existing test pins >= 72, and it is pinned alone at the bottom of its rail. Not a global token. *Master recommends: accept.*
2. **Block number font 28 px** (`Font.Size.Composer.BlockNumber`) in Composer blocks: read from across the room, sits on video, so it cannot be a 12-16 px chrome role. Compact blocks use Lead 16. *Master recommends: accept.*
3. **Hierarchy at 16 px max.** Dropping 20/18 px makes titles flatter. Checkpoint screenshots decide whether a second exception (title 20) is needed. *Master recommends: accept the plan, decide with the screenshots.*

## 4. Full HD question both plans found independently (A's O1, B's risk 1)

At 1920x1080 maximized the overview is **height-bound**: Sources get about 350 px and Displays about 596 px of height, so previews stay about 676x380 for two displays with about 470 px of width unused, and the Composer canvas stays about 1160x652. Raising caps or text size does not change the preview size. Both plans therefore only restyle and use the width for labels, rails and thumbnails.

Options:
- (a) Accept (restyle only), add labels back, caps raised, 12 px text. **Master and A recommend this for v2.**
- (b) Move Sources into a **left rail** when the window is at least about 1600 px wide. This frees about 350 px of height for displays (about 676x380 -> about 1000x560 for two displays) and is the only thing that really uses Full HD, but it is a layout change with its own tests and risk to drag-and-drop paths.
- (c) Slim non-video card chrome (action bar/dock) by a few rows.

## 5. Edits the sessions apply to their plans after approval

* **A:** drop `Chip.Draft` (consume foundation `Chip.Staged`); drop own `Style.Badge.*`; consume shared converters and guard allowlist (A1 only adds entries); move `Icon.Lock` use to `SymbolIcon` (A10); text roles table adds `Section`.
* **B:** icon sizes to 16/22/28; drop Task 6 converter work; consume foundation `Chip.Staged` and `Style.Badge.*`; keep ratchet only as temporary; roles Lead/Title/Section/Body/Caption.
* **Both:** list which tasks need foundation items so the foundation checkpoint can be scoped (section 6).

## 6. Foundation checkpoint (checkpoint 1) scope

Branch `ui-v2/foundation` off `ui-v2/integration`; merged on the user's approval. Tasks, each red-first and one commit:

| # | Task | Done when |
|---|---|---|
| F0 | Split `ViewSmokeTests.cs` into partials (pure move: `.Support`, `.Theme`, `.MainWindow`, `.VisualRouting`, `.SourceCard`, `.DisplayCard`, `.Composer`) | 795 tests, same names |
| F1 | Add `WPF-UI` 4.3.0; `App.xaml` merge order `ui:ThemesDictionary`, `ui:ControlsDictionary`, palette, `Styles/*`, `Theme.xaml`, `Icons.xaml`; `ThemeService` swaps `ThemesDictionary.Theme` and sets the accent from the app token (never `ApplicationThemeManager.Apply` while `Application.MainWindow` is set: it strips the native caption text/icon/menu) | `UiV2ThemeBridgeTests`; manual check that the title bar is intact after toggling |
| F2 | `TestResources.Ensure()` merging exactly what `App.xaml` merges; adopted in the four bootstrap sites | all tests green on it |
| F3 | Tokens `Resources/Tokens.xaml`: `Size.Touch.Min/Compact`, `Size.Icon.S/M/L`, `Size.Dot.*`, `Size.Card.*`, `Size.AppBar.Height`, `Size.Window.*`, dialog/popup/log tokens, `Font.Size.Lead/Title/Body/Caption` (16/14/13/12) | token test + font-floor guard |
| F4 | Central styles: `Buttons.xaml` (move from `Theme.xaml` and `MainWindow.Resources`; `Style.Button.Composer` at 48; `Chip.Staged`; `AppBar`), `Tabs.xaml`, `Text.xaml`, `Cards.xaml`, `Inputs.xaml`, `Lists.xaml` (`Style.ListBox.Virtualized`), `Overlays.xaml`, `Badges.xaml`, `Converters.xaml` with the shared converters, empty `SourceCard.xaml`/`DisplayCard.xaml`/`Composer.xaml` | existing keys stable, 795+ tests green |
| F5 | `UiV2Guards` + single allowlist file (all view files in `Pending`) incl. icon, font floor, virtualization guards | guards green with `Pending` full, red when emptied |
| F6 | Icon font check: a rendered `ui:SymbolIcon` has a glyph and a resolved `FluentSystemIcons` family; implicit WPF-UI style leakage review (Button, ScrollViewer, ToolTip, Popup) | notes in the checkpoint |

**Checkpoint 1 deliverable:** runnable branch, test count, screenshots at 1920x1080 and 1024x720 in Dark and Light (the app should look nearly the same; tooltips and un-templated inputs change), the leakage notes. Then A and B implement in parallel in their own worktrees; A merges at checkpoint 2, B at checkpoint 3.

## 7. Execution order

1. Foundation (F0-F6) on `ui-v2/foundation`, merged into `ui-v2/integration` at checkpoint 1.
2. A (`ui-v2/main-window`) and B (`ui-v2/composer`) in parallel from the new integration tip. B Task 11 needs A3 (or uses its fallback); B Tasks 1-3 can start at once.
3. Checkpoint 2 (A merged), then checkpoint 3 (B merged, `DisplayCard` layout-menu tests re-run). After each, the user reviews screenshots and the running app. `main` is only merged on the user's explicit approval; tag `pre-ui-v2` remains the revert point.
