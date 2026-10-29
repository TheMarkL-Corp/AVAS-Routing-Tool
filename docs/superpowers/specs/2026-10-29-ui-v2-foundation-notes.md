# UI v2 Foundation (checkpoint 1): what was built, what WPF-UI does to the app

Branch `ui-v2/foundation` (off `ui-v2/integration`), tasks F0-F6 of `2026-10-29-ui-v2-cross-review.md` section 6. Not merged, not pushed.
Non-live tests: **795 before, 1023 after**, all green (`-p:BaseOutputPath=bin/uiv2f/`).

## 1. What exists now (the contract for Sessions A and B)

| Task | Result |
|---|---|
| F0 | `ViewSmokeTests.cs` split into `partial class ViewSmokeTests` files `.Support` (helpers, rigs, `[Collection]`), `.Theme`, `.MainWindow`, `.VisualRouting`, `.SourceCard`, `.DisplayCard`, `.Composer`. Pure move: the 795 test names are identical (compared with `--list-tests`). `LayoutsBarView_*` is in `.DisplayCard`, `CompactSourceCard_*` in `.Composer`, `Rings_*` and `App_ThemeToggle_*` in `.Theme`. |
| F1 | `WPF-UI` 4.3.0. `App.xaml` merge order: `ui:ThemesDictionary`, `ui:ControlsDictionary`, `Themes/Theme.Dark`, `Tokens`, `Converters`, `Styles/WpfUiOverrides`, `Styles/{Buttons,Tabs,Text,Badges,Lists,Cards,Inputs,Overlays,SourceCard,DisplayCard,Composer}`, `Theme.xaml`, `Icons`. `ThemeService` swaps `ThemesDictionary.Theme` and calls `ApplicationAccentColorManager.Apply(Color.Accent.Primary, theme)`; it never calls `ApplicationThemeManager.Apply`. |
| F2 | `TestResources.Ensure()` (tests): merges exactly the `App.xaml` list (a test compares them), idempotent, resets a Light theme left by an earlier test. Use it at the top of every STA test body. |
| F3 | `Resources/Tokens.xaml`: `Size.Touch.Min/Compact` 48/36 (only these two), `Size.Icon.S/M/L/Brand` 16/22/28/28, `Size.Dot.S/M`, `Size.Card.Min` and `Size.Card.Row.*` (+26 = `GridSpec.DisplayCard.ChromeHeight`), app bar, window, dock/toast/notice/popup/tile, schematic menu, settings dialog, `GridLength` tokens `Size.Log.Col.*`, `Font.Size.Lead/Title/Body/Caption` 16/14/13/12. |
| F4 | Central styles under unchanged keys (see section 2), shared converters, empty merged files for the sessions. |
| F5 | `UiV2Guards` + `UiV2Guards.allowlist.json` (sections `pending`, `staticIcons`, `unvirtualizedLists`, `inlineStyleExceptions`); engine tested in `UiV2GuardScannerTests`. |
| F6 | `UiV2IconFontTests` (a rendered `ui:SymbolIcon` has a glyph that exists in the resolved FluentSystemIcons font, and an area) and this review. |

### Where each key lives now

* `Styles/Buttons.xaml`: `Style.FocusVisual.Ring`, `Style.Button.Primary/Secondary/Unroute/Composer/CardAction/AppBar`, `Style.Button.Chip.Staged` (part `DraftFrame`, `InputModality.ShowsFocus` kept, used by the `DisplayCardView` draft chip), `Style.Icon.AppBar`.
* `Styles/Tabs.xaml`: `Style.Tab.Nav` (part `bd`). `MainWindow.Resources` is gone.
* `Styles/Text.xaml`: `Style.Text.Lead/Title/Section/Body/Caption/Mono` + `Style.Text.ComposerLabel`. `Section` carries `AutomationProperties.HeadingLevel=Level2`. WPF has no text-transform or letter-spacing, so "uppercase" stays in the text.
* `Styles/Badges.xaml`: `Style.Badge.Base` (ui:Badge chain + plain template) and `Info`, `Staged`, `Armed` on it.
* `Styles/Lists.xaml`: `Style.ListBox.Virtualized` and `Style.ListBoxItem.Plain`.
* `Resources/Converters.xaml`: `Converter.Visible.{True,False,NotNull,Null,NotEmpty,HasItems,NoItems}`, `Converter.WidthAtLeast` (parameter = a token), `Converter.BoolToGridLength` (default true = `*`, false = `Auto`; parameter `"true;false"` such as `Auto;2*` overrides).
* `Theme.xaml` keeps only `Style.Button.Take` (Session B moves it), the scroll bar family and the implicit `ScrollViewer`.
* Empty on purpose, merged: `Cards`, `Inputs`, `Overlays` (Session A fills them, these were in A's F4 request but not in the foundation task list), `SourceCard`, `DisplayCard` (A), `Composer` (B, merged last).
* `Style.Button.Composer` is now 48 px. The six `>= 56` button-height assertions that pinned it became `>= 48` (once, in this commit).

## 2. WPF-UI findings that changed the plan (all covered by tests)

1. **Implicit `Window` style.** `ControlsDictionary` restyles every `Window` (own template, `OverridesDefaultStyle`, 1100x600, `AllowsTransparency`). A window built in code then throws on `Show()` ("Cannot change AllowsTransparency...") and a XAML window loses the stock chrome. `Styles/WpfUiOverrides.xaml` has an empty implicit `Window` style. Test: `WpfUiControlsDictionary_DoesNotRestyleAPlainWindow`.
2. **Implicit `ListBox`/`ListBoxItem` style.** 12 px item padding (a Logs row grew 22 -> 44 px) and a `PassiveScrollViewer` that bypasses our 16 px gutters. Neutralised the same way until the Logs list uses `Style.ListBox.Virtualized`.
3. **`ui:Badge` template triggers.** The default Primary appearance forces black text (and the other appearances force the border background) with template triggers that beat any style setter: black on our dark badge fills was 1.3:1. `Style.Badge.Base` swaps in a plain template; Info/Staged/Armed are contrast-tested at >= 4.5:1 in Dark and Light (the fills are translucent, so the test blends them over `Brush.Surface.Card`).
4. **`ApplicationThemeManager.Apply`** is never called (it also touches `Application.MainWindow`); verified by `ThemeService_NeverCallsApplicationThemeManagerApply` (the `Changed` event) and by running the app: title text, icon and system menu survive repeated theme toggles (screenshots).
5. `StaticResource` between sibling merged dictionaries works in `App.xaml` order and in `TestResources` (styles read `Size.*`/`Font.Size.*` with `{StaticResource}`).
6. `ui:ThemesDictionary` has only a setter-like `Theme`; the loaded theme is read from its `Source` (`.../Light.xaml`).
7. The two icon families resolve as `pack://application:,,,/Wpf.Ui;component/Resources/Fonts/#FluentSystemIcons-Regular` and `...#FluentSystemIcons-Filled`; a missing resource renders a blank icon without an exception, so the glyph/typeface test matters.

## 3. How WPF-UI's `ControlsDictionary` restyles plain (un-styled) controls in this app

Measured by rendering each control with and without the WPF-UI dictionaries (this machine, 100 %), Dark theme unless noted. The app's own keyed styles (every button, tab, card) do not change.

| Control | Stock WPF | With WPF-UI | Where it shows in the app |
|---|---|---|---|
| **ToolTip** | light grey `#F1F2F7`, 12 px, 1 px border, no width limit | Dark theme: `#2C2C2C` fill with white text; Light theme: `#F9F9F9` with near-black text; rounded, soft border; **max width 260** so long tips wrap (a 2-line tip is 72 px high) | every `ToolTip="..."` (icons, buttons, badges). Follows the theme now (was the same light grey in both). |
| **TextBox** | 18 px high, 12 px, white box | **35.6 px high**, 14 px, padding 10/8, translucent fill, gradient border with an accent line on focus | the 7 fields in Settings. At 480x540 the dialog now scrolls: "Show Logs tab" and the base-port field are below the fold. Session A's A12 redoes the dialog. |
| **CheckBox** | 17.6 px | min 32 px high, min width 120, 14 px, Fluent box | `ChkShowLogs` in Settings |
| **Button** (no style) | stretches, 12 px, system chrome | left aligned, 31.6 px, 14 px, Fluent fill | none left: every button in the views has a style |
| **ScrollViewer** | - | **no change**: our implicit style is merged later and wins (test) | all scrolling |
| **Window** | - | neutralised (finding 1) | all windows |
| **ListBox / ListBoxItem** | - | neutralised (finding 2) | Logs |
| **TextBlock** | default 14 px on this machine | measured identical (the WPF-UI implicit style does not change a plain TextBlock) | 170 text blocks |
| **Popup** | - | no implicit style; popup content is not restyled | layout menu popup, Composer picker |
| Label, ComboBox, RadioButton (plain), Expander, Menu/ContextMenu, TabControl, DataGrid, TreeView, ProgressBar, Slider | restyled by WPF-UI | **not used** by any view today | none |

Visible differences in the screenshots (before = tag `pre-ui-v2`, after = this branch; same window sizes, Dark and Light): app bar, cards, badges, previews, Logs rows look the same; the tooltip and the Settings fields/checkbox are the changes. Accent: WPF-UI's accent equals `Color.Accent.Primary` (`#2563EB`), derived hover shades come from WPF-UI.

## 4. Risks and open items

* **Settings dialog height** (section 3): fields are 8 px taller each, the checkbox is below the fold until A12. Functional, not blocked.
* **Tooltip width 260** wraps the longer tips (for example the draft chip's). Tests pass; readability is for the screenshots.
* **Take (72 px)** and **block number 28 px** exceptions of the cross-review stay open for the user (not part of the foundation).
* `Cards.xaml`, `Inputs.xaml`, `Overlays.xaml` exist empty: the foundation task list did not name them but A's F4 request and the cross-review table do. Remove them if you prefer A to create them.
* The pending list and the allowlist are the only thing standing between the guards and the views; a session that forgets to add its entry will see red, a session that migrates a file and forgets to remove it from `pending` sees red too (stale entry).
* `Icon.Lock` and `Icon.Sun/Moon/Refresh/Settings` are still in `Icons.xaml` (X8: deleted after both sessions land).
* WPF-UI upgrade: re-run `UiV2ThemeBridgeTests`, `UiV2StylesTests`, `UiV2IconFontTests`; the overrides and the badge template are the places a new version could bite.
