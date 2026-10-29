# UI v2: MultiView Composer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the MultiView Composer (`MultiViewComposerView`, `LayoutSchematic`, `CompactSourceCardView`) onto the UI v2 foundation (WPF-UI 4.3.0 theme and controls, central styles, vector icons, dynamic layout) without changing its 3-step flow, its fractional preview canvas, its keyboard model or any hardware behaviour, and make it fit the 1920x1080 maximized design target.

**Architecture:** The view keeps only layout and bindings. Every `Style`, `ControlTemplate`, font size and glyph moves to `Resources/Styles/Composer.xaml` (contents owned by this plan; the master creates and merges the empty file) plus the foundation's token, text and button dictionaries. Block density (compact overlays on small blocks) becomes one inherited attached property so ~30 `TargetName` setters turn into ordinary style triggers. `FractionalLayoutPanel` keeps its placement algorithm and gains one rule (bounded width, unbounded height means height = width / aspect) so schematics and thumbnails need no fixed sizes. Rails become star columns clamped by size tokens, which removes the code-behind `FitRails`.

**Tech Stack:** .NET 8 WPF, MVVM, WPF-UI 4.3.0 (`ui:SymbolIcon`, `ui:Card`, `ui:Badge`; theme dictionaries), xUnit STA view tests.

**Spec:** `docs/superpowers/specs/2026-10-29-ui-v2-master-guidelines.md` (section 0 locked decisions D1-D12 override everything else; G1-G5; sections 3-6). Session A's plan: `docs/superpowers/plans/2026-10-29-ui-v2-main-window.md` (its F1-F7 are aligned with in section 8). Composer behaviour that must not regress: `Handover/04_VERIFIED_FEATURES_AND_KNOWN_LIMITATIONS.md`.

**Branch and execution (D2, D7):** work happens on `ui-v2/integration`'s descendants only; `main` is untouched, revert tag `pre-ui-v2`. The Composer is implemented in its own worktree (branch `ui-v2/composer`, cut from `ui-v2/integration`) **after checkpoint 1 (Foundation) and checkpoint 2 (Main Window) are merged**, and merged back at checkpoint 3 (section 7.3).

## Locked decisions and where they land in this plan

| D | Applied how |
|---|---|
| D1 | Theme + controls only. Composer is a `UserControl` inside the existing `Window`; no `FluentWindow`/`TitleBar`/`NavigationView` anywhere in these files. |
| D3 | Stock glyphs -> `ui:SymbolIcon` (section 3.3); no custom brand icon is needed in the Composer. Icon guard allowlist in 6.3. |
| D4 | Touch tokens are exactly `Size.Touch.Min` 48 and `Size.Touch.Compact` 36. Section 2.5 lists which control uses which. Comfort 56 / Primary 72 are dropped; Take keeps a content-specific `Size.Composer.Take.Min` (justified in 2.5). |
| D6 | Hard guards from the master + ONE shared allowlist. Composer entries and reasons in 6.3. Task 3 registers the three files as "pending migration" and keeps a per-task progress ratchet that is deleted in Task 12. |
| D8 | Font floor 12. Composer text maps onto A's tokens 16/14/13/12 (`Font.Size.Lead/Title/Body/Caption`); no 11 px; the 20/18/17/15 sizes are dropped; one content-specific exception (block number 28). Section 3.4. |
| D9 | Design target 1920x1080 at 100%, maximized; 1024x720 stays a safety floor with the existing tests. Section 2.3. |
| D10 | Splitting `ViewSmokeTests.cs` is foundation task zero. Composer partial: `ViewSmokeTests.Composer.cs`. |
| D11 | No virtualized list in the Composer (section 5). |
| D12 | Native `Button` + central templates (part name `bd` kept); `ui:Card` for rails, `ui:Badge` for chrome chips, `ui:SymbolIcon`; `ui:ToggleSwitch`/`ui:TextBox` are not needed (no toggle or text input exists in the Composer). No `ui:Button`. |

## Global Constraints

- TDD; every XAML change is verified on an STA thread by view tests. Run tests with a fresh `-p:BaseOutputPath=bin/<name>/`. Baseline: **795 non-live tests pass** (plus whatever the Foundation and Main Window checkpoints add). Test names must not contain `Live`.
- Theme contract: Dark/Light dictionaries keep identical keys; palette refs are `DynamicResource`; no raw `#RRGGBB` outside `Resources/Themes/`; `HexCheckExemptFiles` stays empty. No `Dispatcher.Invoke` in `ThemeService`.
- Clinical OR ergonomics: WCAG AA (>= 4.5:1), touch targets per D4, visible focus rings, spoken `AutomationProperties` names, 1024x720 safety floor, nothing occludes the 16:9 video canvas.
- Zero socket leaks: the Composer only mirrors `PreviewBitmap` references of source cards; this plan adds no receiver, socket or multicast code.
- `Brush.*` semantic tokens stay the app's own API; no new colour token is needed.
- Hardware/protocol code is out of scope. `MultiViewComposerViewModel` changes are limited to one optional read-only property (Task 9).
- Commits: code + `memory/active_context.md` + `memory/progress.md` together, one focused commit per task (CLAUDE.md rule 5). Stage by explicit path.

## Review Focus

Failure modes the spec implies but a straight "restyle" would miss, most likely first. Each has a test in the owning task.

1. **Preview canvas aspect.** A display that is not 16:9 (for example 1920x1200) must still letterbox the staged canvas at its real aspect; ON SCREEN NOW must use the LIVE plan's aspect, not a hard-coded 16:9 (suspected latent bug, Task 9).
2. **Small blocks (PIP inset, 2x2 at 1024x720).** Compact overlays (smaller number, no size chip, no empty hint, 36 px strip) must survive the move to inherited-property styles; a regression shows as chips overlapping the corner clear (Tasks 7a/7b; existing test `Composer_InVisualRoutingView_AtRealWindowSizes_...`).
3. **Keyboard model.** Tab order is Back, 4 layout tiles, ONE block (roving), Discard, Clear all, Take; no new control (`StepHeader`, `ui:Card`, `ui:Badge`, `ui:SymbolIcon`) may become a tab stop; the focus ring must still change pixels on every control (`AssertShowsFocus`) (Tasks 4, 7b).
4. **Video overlay tokens.** `Brush.Text.OnScrim/OnPreview/OnPreviewMuted` and the Scrim/Preview surfaces are identical in both themes and stay `Foreground`/`Background` attributes in the view (pinned by `ThemeResourceUsageTests.VideoOverlayText_...`) (Tasks 7a, 10, 11).
5. **Touch and type changes are deliberate regressions to catch.** Composer buttons drop from 56 to 48 (D4) and text from 20/18/17/15 to 16/14 (D8); four existing assertions (`>= 56`) change and the screenshots at the checkpoint must show the hierarchy still reads.
6. **Implicit WPF-UI styles** restyle plain `Button`/`ScrollViewer`/`ToolTip`: every Composer button that keeps a local template today gets an explicit central style with `OverridesDefaultStyle=True`.

---

## 1. Audit

Measured on the working tree 2026-10-29 (regex + manual read). "Style elements" counts real `<Style ` / `<Style>` tags; the master's "90" counted every `<Style` substring including 42 `<Style.Triggers>`.

### 1.1 Totals per file

| Metric | `MultiViewComposerView.xaml` (1315 lines) | `.xaml.cs` (737) | `LayoutSchematic.xaml` (58) / `.cs` (86) | `CompactSourceCardView.xaml` (170) / `.cs` (25) |
|---|---|---|---|---|
| Style elements | **48** = 4 keyed (`Chip` L25, `RailCard` L34, `RailHeading` L43, `StepDot` L51) + 44 inline (10 are `BasedOn` a central/local key) | - | 1 (ItemContainerStyle L22) | 8 inline |
| Style.Triggers / DataTrigger / MultiDataTrigger | 42 / 65 / 8 | - | 0 | 8 / 8 / 0 |
| `ControlTemplate` (real) | **4**: LayoutTile L194, BlockSurface L528, CornerClear L808, PickerTile L1240 (the master's "7" counted 3 `.Triggers` closers) | - | 0 | 0 |
| `DataTemplate` | 6 (L184, L317, L506, L946, L1002, L1229) | - | 1 | 0 |
| `TargetName` setters in `DataTemplate.Triggers` | ~30 (L873-904, the compact-block overrides) | - | - | - |
| Literal `Width=`/`Height=` attributes | **12** (L691x2, L798x2, L811x2, L852x2, L1232, L1260, L1291x2) | `FitRails`: 240/264/280/320, schematic 120x70 / 96x56 | 0 | **4** (L5 292, L6 96, L34 112x63) |
| Literal Min/MaxWidth/Height | MinHeight 96 L187, 56 L375, 120 L926, 160 L1233; MaxWidth 560 L466, 380 L684, 760 L1206, 180 L1303; MinWidth 104 L859/L861 | - | - | - |
| Literal Width/Height in `Setter` | L52-53 (StepDot 28), L883-884 (LockIcon 16), MinWidth 0 L899/L902 | - | - | - |
| Grid column literals | L130 280, L131 16, L133 16, L134 320, L1153 12 | `FitRails` | - | - |
| Literal `FontSize` (attr + setter) | 46 (28, 20, 18, 17, 16, 15, 14, 13, 12) | - | **2 (11 at L37, 13 at L51; the 11 breaks the D8 floor)** | 5 (12, 14) |
| Literal `CornerRadius` | 15 | - | 2 | 7 |
| Glyph/letter icons | **10**: ✓ L170, L231, L282, L352, L1060; ✕ L748, L818, L1068; letters `i` L1056, `!` L1064 | - | 0 | 0 |
| Text separators (not icons) | `·` (U+00B7) in L477, L565, L1273 | - | - | L58 |
| Vector `Path` icons | `Icon.Lock` x2 (L687, L1291), sized by literal Width/Height | - | - | - |
| Bitmap icons | 0 | - | 0 | 0 |
| Raw hex | 0 (guarded by test) | - | 0 | 0 |
| `x:Name` | 63 (many are test/automation anchors, see 1.3) | - | 2 | 3 |
| Aspect literal | `1.7777777777777777` L926, L933 | - | L12 | - |
| Live-video `Image` surfaces | L545, L949, L1262 | - | - | L39 |

### 1.2 G1-G5 verdict

- **G1 WPF-UI**: not referenced. Decisions in section 2.4.
- **G2/D3 icons**: no bitmaps. 10 glyph/letter icons and 2 hand-sized `Path`s become `ui:SymbolIcon` (section 3.3).
- **G3/D6 styles**: the largest style-debt in the app. 4 keyed + 44 inline styles, 4 templates; the compact-block mechanism (L867-906) is a 40-line trigger list instead of a style.
- **G4/D8 sizes and fonts**: 12 literal attrs, 15 literal Min/Max, 5 column literals, 8 code-behind constants, 4 in the palette card (section 4); 53 literal `FontSize` values (section 3.4).
- **G5/D11 virtualization**: no `ListBox`/`ListView`/`DataGrid`; six small `ItemsControl`s (section 5).

### 1.3 The three-step flow and the behaviour that must survive

1. **Layout** (left rail): 4 tiles from `LayoutOptions` (matrix2x2, pip, side_by_side, quad_1_3), each a `Button x:Name="LayoutTile"` with a `LayoutSchematic` drawn from `MultiViewPresets.Build(id, 1920, 1080)`. States: selected (3 px accent), pending (DropTarget border + "Reading the display..."), focus ring.
2. **Place sources**: arm a source (palette strip in `VisualRoutingView`, `CompactSourceCardView`: tap arms, drag places) then tap blocks; or tap an empty block to open the picker `Popup`; or drag a card/block onto `BlockRoot` (`AllowDrop`, `DropEffectFor`/`DropOn`). A placed block opens a Replace/Move strip on tap. Unplaced sources (after a layout change) show as chips.
3. **Check, then Take** (right rail): ON SCREEN NOW mini picture (`OnScreenSummary` is its spoken name), CHANGES list, pinned banner (live region), `TakeButton` (`Content` is a `string`), Undo, Discard, Clear all. Ctrl+Enter takes from anywhere.
4. **Canvas** (centre): `CanvasHost` holds two `FractionalLayoutPanel`s with `AspectRatio` from `AspectRatioConverter(Draft.Plan.Width, Draft.Plan.Height)`: a black mat + state frame (`CanvasFrame`: dashed amber staged, solid routed when on screen), and `BlocksHost` whose items are placed by `NormalizedX/Y/Width/Height` with `Panel.ZIndex = -Index` (lowest API window index is painted on top; the PIP inset is index 0).
5. **Keyboard**: one roving `BlockSurface` tab stop, arrows move `FocusBlock`, Enter/Space place, `P` picks, Delete clears, Esc climbs a ladder and never closes the Composer, Ctrl+Enter takes; the picker is keyboard-operable and returns focus to its block; `RestoreFocus` keeps the keyboard from falling to the window. All of this lives in `MultiViewComposerView.xaml.cs` by element **names** (`BlockSurface`, `LayoutTile`, `TakeButton`, `UndoButton`, `PickerFrame`, `PickerItems`, `BlocksHost`, `CanvasHost`, `LeftRailScroll`, `BannerText`, `BannerHost`, `CanvasFrame`, `PickerPopup`). **Rule for every task: those names and their element types stay.**
6. **Screen reader**: step headings are `HeadingLevel2` with `StepLayoutText/StepPlaceText/StepTakeText` names; blocks, tiles, picker tiles, CornerClear, Take, Back have bound names; `BannerText` is a polite live region (assertive on error); `OnScreenPicture` carries `OnScreenSummary`.
7. **Video overlays**: block number, source pill, SameAs, size chip, issue band, NO PREVIEW text, lock reason, PlacePill/MovePill sit on `Brush.Surface.Scrim*`/`PreviewBackdrop` with `Brush.Text.OnScrim*`/`OnPreview*`. These tokens are **byte-identical in Dark and Light** (verified in both theme files) and are pinned by `ThemeResourceUsageTests.VideoOverlayText_UsesOnPreviewOrOnScrimTokens` (reads the `Foreground` attribute in the view XAML). Session A adopted the same rule (its section 3.3).

### 1.4 Findings that are not just style

- F1. `OnScreenPicture` uses a literal 16:9 for its panels (L926, L933), while `LiveBlocks` use `NormalizedX/...` of the LIVE plan. For a non-16:9 canvas the mini picture mis-scales. Verify in Task 9.
- F2. `CornerClearGlyph` border uses `Brush.Text.DangerSoft` (chrome token that differs per theme: #F87171 dark, #DC2626 light) over a scrim that is the same in both themes. Estimated contrast of the 1 px ring in Light is about 3.9:1 (acceptable for a non-text component, but it is the one overlay colour that is not theme-stable). Planned: use `Brush.Text.OnScrimDanger` for the ring.
- F3. `RunSta` in four test classes merges only `Themes/Theme.Dark`, `Theme`, `Icons` by hand (same finding as A's F6).
- F4. `LayoutSchematic.xaml` L37 uses 11 px for the cell numbers: below the D8 floor. Becomes 12.
- F5. `Style.Button.Composer` is 56 px high today; D4 drops the 56 token, so it becomes 48 and four assertions change (section 6.2).

---

## 2. Target design

### 2.1 Structure (unchanged flow, centralized chrome)

```
UserControl MultiViewComposerView            Foreground = Brush.Text.Primary
 Grid  rows: Auto | *
  Header (Auto)  Grid cols: * | Auto
   [Title Style.Text.Lead bold] [ui:Badge OutputText] [ui:Badge RateNote]    [ <- Back to displays ]  (Style.Button.Composer + ui:SymbolIcon ArrowLeft24, 48 px)
   Caption (Style.Text.ComposerLabel, wraps)
  Body (*)  Grid cols:  Left 25*  | Center 47* | Right 28*
              Left  MinWidth Size.Composer.Rail.Left.Min  (240) MaxWidth ...Max (320)
              Right MinWidth Size.Composer.Rail.Right.Min (264) MaxWidth ...Max (360)
              Center has Margin="16,0" (the old 16 px spacer columns)
   LEFT  ScrollViewer(LeftRailScroll)                 CENTER                       RIGHT  Grid rows * | Auto
    StepHeader 1 LAYOUT                                Row0 Auto: status line (48)   ScrollViewer(RightRailScroll)
    4x LayoutTile (Schematic 6* | texts 7*)            Row1 *:   CanvasHost            ui:Card ON SCREEN NOW (mini canvas)
    StepHeader 2 PLACE SOURCES + hint + unplaced chips           FractionalLayoutPanel   ui:Card CHANGES (n)
    StepHeader 3 CHECK, THEN TAKE + hint                         mat + frame + blocks   pinned: Banner / Take / reason / Undo / Discard | Clear all
  Popup PickerPopup (kept as a native Popup)
```

### 2.2 Which sizes stay, and why (the preview canvas)

The staged canvas is a deliberate layout algorithm, not view sizing. These stay exactly as they are:

| Kept | Why |
|---|---|
| `FractionalLayoutPanel.CanvasRect` (largest rectangle of `AspectRatio` that fits, centred) and its use for BOTH the mat/frame panel and the blocks panel | Matches how the decoder letterboxes its output; the operator must see the true aspect and the true gaps. A `Viewbox`/`UniformGrid` would crop or distort it. |
| `AspectRatio` bound to `Draft.Plan.Width/Height` through `AspectRatioConverter` (default 16:9 until a layout is chosen) | The display's real canvas (read from EDID) is not always 16:9. |
| `NormalizedX/Y/Width/Height` placement and `Panel.ZIndex = -Index` (`PaintOrderConverter`) | The PIP inset is window 0 and must be painted on top, as the decoder does (test `Composer_Pip_LowestWindowIndexIsPaintedOnTop`). |
| 4 px per-block `Margin` inside each cell, `CanvasHost Margin="8"`, `CanvasFrame Margin="-6"` + 3 px stroke | Spacing and a frame that draws just outside the picture; not Width/Height. |
| Compact threshold **240 px** per block | A layout-algorithm constant like `GridSpec`. It moves from a XAML `ConverterParameter` literal to `BlockDensity.CompactBelow` (tested constant). |
| `PickerMaxHeight = max(320, 70 % of work area)` in code-behind | Computed from the work area, cannot be a token; already tested (`Composer_PickerPopup_WithTwelveEncoders_...`). |
| 8 px drag-start distance (`TapGesture`) | Input algorithm, tested in `TapGestureTests`. |
| Mini-picture ratio: the literal `1.7777777777777777` becomes `{x:Static views:AspectRatioConverter.Default}` (16/9), and in Task 9 the live plan's ratio | Removes the magic number; a ratio is not a size. |
| `Font.Size.Composer.BlockNumber` 28 (the only font exception) | The block number is read from across the room on a 560 px block at Full HD and on a 215 px block at 1024x720; it sits on video, so it cannot be a smaller chrome role. Compact blocks use `Font.Size.Lead` (16; was 18). |
| `Size.Composer.Take.Min` 72 (the only touch exception) | See 2.5. |

What goes away: every fixed pixel on chrome (rails, tiles, schematics, steps, picker, palette card) as listed in section 4.

### 2.3 Behaviour at sizes (D9: Full HD is the design target)

**Estimates, to be measured by Task 5a's 1920x1080 test** (VisualRoutingView at a maximized 1920x1080 client): Composer about 1888 wide and 800 high; header about 75, body about 725; status row 48; canvas area about 1160 wide x 655 high.

| Area | Today at 1920x1080 | UI v2 at 1920x1080 | Why |
|---|---|---|---|
| Rails | 280 / 320 (fixed caps) | **320 / 360** (caps; 25:47:28 star weights) | The canvas is **height-bound** at 16:9 (about 1164 x 655 in about 1256 wide), so about 90 px of width is letterbox either way. Giving it to the rails costs the canvas nothing and makes the rail text, tiles and Take readable. |
| Canvas | about 1164 x 655 | about 1160 x 652 (same within 5 px) | Height-bound. The status row drops from 56 to 48 (D4), which the wider rails take back. The only way to a larger canvas is more vertical room, which belongs to session A's rows (palette strip <= 160, header): every 10 px of height is about 18 px of canvas width. Raised as an open question (section 8). |
| Layout tile schematic | 120x70 | about 135x76 (6* of a 320 rail) | Rail cap. |
| Picker | tiles 220, frame max 760, thumbnails about 196x110 | tiles **260**, frame max **880**, thumbnails about 236x133, three per row | More room, bigger live thumbnails; still capped by `PickerMaxHeight`. |
| Palette card (`CompactSourceCardView`) | 292 wide, 112x63 thumbnail, about 100 high | **340** wide, **144x81** thumbnail, about 105 high (strip stays <= 160) | Bigger previews at the design target; the strip scrolls sideways as before. |
| Type | 14 body, 16-28 mixed | 16 labels, 14 body, 13/12 captions; block number 28 | D8 tokens. |
| Touch | 56 buttons, 72 Take | 48 buttons, 36 in compact blocks, Take 72 | D4. |

**1024x720 safety floor** (`VisualRoutingView` 1008x615, Composer 976 wide): rails clamp to 240/264, centre about 440 wide with 16 px margins (identical to today); four 2x2 blocks are each >= 150 wide (existing test); palette strip <= 160 high; Take fully visible; Undo label wraps (existing test). Between 1024 and about 1300 the rails grow smoothly with the window (today they jump from 240/264 to 280/320 at exactly 1200); both reach their caps (320/360) from about 1320 up.

### 2.4 WPF-UI 4.3.0 control decisions (API verified)

Verified on 2026-10-29 against NuGet (`WPF-UI` **4.3.0**, targets net8.0) and `lepoco/wpfui` `main` sources: namespace `xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"`; `ui:ThemesDictionary` + `ui:ControlsDictionary`; `ApplicationThemeManager.Apply(...)`; `ui:SymbolIcon` has `Symbol` (`SymbolRegular`: `Checkmark24`, `Dismiss24`, `LockClosed24`, `ArrowLeft24`, `ArrowUndo24`, `Info24`, `Warning24`, `ErrorCircle24`, `CheckmarkCircle24` all exist) and `Filled`, is a font glyph sized by `FontSize`, and needs the `FluentSystemIcons` font resource; `ui:Card` template binds `Background/BorderBrush/BorderThickness/Padding/Border.CornerRadius` through `TemplateBinding` and is non-focusable; `ui:Badge` template binds `Background/BorderBrush/Padding` (border 1 px, radius 4 fixed) and only changes the background for non-`Primary` `Appearance` values, so it can be restyled by our tokens while `Appearance` stays default; `ui:InfoBar` has `IsOpen/IsClosable/Title/Message/Severity`; `ui:Flyout` exposes `IsOpen/Placement` but hosts its popup as a template part; `ui:Button` derives from `Button` (not used, D12). The library has **no** segmented control, stepper or step-flow control.

| Composer chrome | WPF-UI candidate | Decision | Reason |
|---|---|---|---|
| All glyph icons (✓ ✕ i ! lock, back, undo) | `ui:SymbolIcon` | **Adopt** | D3; sized by `FontSize` (so no Width/Height). Foreground always from our tokens. |
| Right-rail cards (`RailCard`) | `ui:Card` | **Adopt** as `Style.Card.Rail` (`BasedOn {x:Type ui:Card}`, our `Brush.Surface.Card`/`Border.Default`) | Template exposes what we need; non-focusable, so tab order is untouched. |
| Chrome chips (header output/rate chips; palette "in N blocks" and "Placing") | `ui:Badge` | **Adopt** as `Style.Badge.Info`, `Style.Badge.Staged`, `Style.Badge.Armed` (`BasedOn {x:Type ui:Badge}`, our Surface/Border/Text tokens, `Appearance` left at default) | D12; no test coupling. Chips drawn **over video** (number, source pill, SameAs, size, issue band, pills) stay `Border` (`Style.Chip.*`): they need the Scrim surfaces and hit-test off. |
| `ui:ToggleSwitch`, `ui:TextBox` | - | **Not needed** | The Composer has no toggle and no text input (`HandleKey` only guards against `TextBoxBase` defensively). |
| Picker frame | `ui:Card` / `ui:Flyout` | **Keep `Border`** (`Style.Surface.Popup`) and the native `Popup` | The code-behind anchors `PickerPopup.PlacementTarget` to the block container, handles `Closed`, `StaysOpen=False`, and tests cast `Named(view,"PickerPopup")` to `Popup` and read `popup.Child`. |
| Buttons (`Style.Button.Composer/Take`, strip, tiles) | `ui:Button` | **Native `Button` + central templates** (D12) | The 2 px border, `InputModality.ShowsFocus` ring (pixel-tested), `Take.Content as string` and `bd` template part names are contractual. Icon+text buttons use `StackPanel(ui:SymbolIcon, TextBlock)` as content. |
| Step markers 1-2-3 / check | none exists | **Custom `StepHeader`** (one control, three uses) | WPF-UI has no stepper. |
| Layout tiles (single choice of 4) | `ui:CardAction`, `RadioButton` | **Keep `Button`** (`Style.Button.ComposerLayoutOption`) | `RadioButton` would collapse four tab stops into one and break the specified tab order and `Name, selected` automation names; `CardAction` takes WPF-UI brushes. |
| Banner | `ui:InfoBar` | **Do not adopt; central `Style.Banner.Composer` with severity `ui:SymbolIcon`** | No "none" or "armed cyan" kind, a close button we do not want, severity colours from WPF-UI resources instead of `Brush.Surface.*`, and the live-region text must stay a named `TextBlock` (`BannerText`). |
| Take/Undo result toast | `ui:Snackbar` | **Do not adopt** | It floats over content: forbidden by the no-occlusion rule. |
| Picker tile list | `ui:VirtualizingWrapPanel` | **Not used** | Section 5. |

### 2.5 Touch targets (D4): which control uses which token

| Control | Size | Token | Note |
|---|---|---|---|
| Back to displays, Done placing / Cancel move, Discard, Clear all, Undo, picker Cancel | 48 | `Size.Touch.Min` (via `Style.Button.Composer`, which becomes 48 in the foundation) | Were 56. |
| Replace / Move strip buttons on a normal block | 48 | `Size.Touch.Min` | Were 56. |
| Replace / Move on a **compact** block (< 240 px wide: PIP inset, 2x2 on a small window) | 36 | `Size.Touch.Compact`, 8 px gap between the buttons | The strip is a dense secondary overlay centred on a tiny block; 48 would cover most of a 215x120 inset. |
| Unplaced-source chips | 48 | `Size.Touch.Min` (consume A's `Style.Button.Chip.Staged`) | Were 56. |
| Corner clear | 48 hit area, 32 glyph | `Size.Touch.Min`; glyph `Size.Composer.Badge.Round` | Unchanged; stays out of the tab order. |
| Layout tiles | 96 | `Size.Composer.Tile.Min` (content-specific) | A tile is a card with a schematic; also a test. |
| **Take** | 72 | `Size.Composer.Take.Min` (**content-specific, not global**) | The only action that changes what the surgeon sees; it is pinned alone at the bottom of a rail that has the room at Full HD, kept 8+ px from every neighbour, and pinned by an existing test (`take.ActualHeight >= 72`, also at 1008x615). `Style.Button.Take` moves into `Composer.xaml` (it has no other user) so no global token is needed. |
| Blocks, picker tiles, palette cards | >= 96 content-sized | none | Large by construction. |

Spacing: all adjacent controls keep >= 8 px (Discard/Clear all 12; compact strip 8; chips 8).

---

## 3. Style/icon migration map

Dictionary key: **C** = `Resources/Styles/Composer.xaml` (contents owned by this plan); **T** = foundation `Text.xaml` (A's roles `Lead/Title/Body/Caption/Section`); **B** = foundation `Buttons.xaml`; **V** = value converter in `Views/ComposerConverters.cs`. Where a style needs the compact state it triggers on the inherited `views:BlockDensity.IsCompact`.

### 3.1 Keyed and inline styles in `MultiViewComposerView.xaml`

| Lines | Today | Target key (dictionary) |
|---|---|---|
| L25-32 `Chip` | keyed Border, scrim | `Style.Chip.Scrim` (C; compact trigger: padding) |
| L34-41 `RailCard` | keyed Border | `Style.Card.Rail` (C; `ui:Card` based) |
| L43-48 `RailHeading` | keyed TextBlock, HeadingLevel2 | `Style.Text.RailHeading` (C; `BasedOn Style.Text.Section` + HeadingLevel2) |
| L51-59 `StepDot` + L154/266/336 triggers + L165/277/347 number/check TextBlocks | 3 copies of ~30 lines | `StepHeader` control + `Style.StepHeader` (C); ✓ becomes `ui:SymbolIcon Checkmark24` |
| L85-93, L97-105 header chips collapse when empty | inline Style | `ui:Badge` + `Style.Badge.Info` (C) + `Visibility` via `VisibilityConverter` rule `NotEmpty` (V) |
| L79 title, L94/L106 chip text | literal fonts | `Style.Text.Lead` (T, bold), `Style.Text.Title` (T) |
| L110, L293, L363, L981 hints | `Style.Text.ComposerLabel` (existing) | unchanged key, 14 px; moved to T by the foundation |
| L141-148, L425-432 disable when `IsBusy` | 2 identical inline Styles | `Style.Panel.BusyDisables` (C) |
| L185-257 `LayoutTile` Button + inline ControlTemplate + 2 inline text styles (L233, L245) | template + 2 styles | `Style.Button.ComposerLayoutOption` (C; **renamed so it does not clash with A's `Style.Button.LayoutTile`**; template part `bd` kept), `Style.Text.Selected` (C), `Style.Text.Pending` (C); visibility by `BoolToVisibility` binding |
| L299-307 unplaced panel visibility | inline | `VisibilityConverter` rule `HasItems` (V) |
| L308 notice, L320-326 chip | literal, local brushes | `Style.Text.Notice.Staged` (C); chip uses A's `Style.Button.Chip.Staged` (B) |
| L401-418 `DoneButton` | inline BasedOn | `Style.Button.Done` (C, triggers on `MovingFromIndex`/`ArmedSource`) |
| L383-395 STAGED/ON SCREEN labels | literal fonts | `Style.Text.Status.Staged`, `Style.Text.Status.OnScreen` (C) |
| L442-462 `CanvasFrame` | inline Rectangle style | `Style.Shape.CanvasFrame` (C) |
| L468-476, L477-479 step-1 placeholder | inline + literal fonts | `VisibilityConverter` rule `Null`; `Style.Text.Lead` (T), `Style.Text.ComposerLabel` (the 15 px hint becomes 14) |
| L497-503 and L937-943 item container styles | 2 identical inline Styles | one `Style.Item.Fractional.Block` (C) |
| L536-543 block opacity when unavailable | inline | `Style.Panel.BlockContent` (C) |
| L551-566 NoPreview panel (MultiDataTrigger) | inline | `Style.Panel.NoPreview` (C) |
| L564-565, L568 NoPreviewName/Label/EmptyHint | literal fonts, inline hide | `Style.Text.Block.Title`, `.Caption`, `.Hint` (C); **`Foreground` attribute stays in the view** (6.1) |
| L589-624 state rectangle | inline | `Style.Shape.BlockState` (C) |
| L627-665 armed overlay + PlacePill + PlaceText | inline x3 | `Style.Panel.PlaceOverlay`, `Style.Pill.Action`, `Style.Text.Block.Action` (C) |
| L672-704 unavailable overlay, DockPanel, lock `Path`, reason | inline | `Style.Panel.UnavailableOverlay`, `Style.Icon.BlockLock` (C), `Style.Text.Block.Reason` (C) |
| L707-732 MovePill + MoveText | inline | `Style.Pill.Action`, `Style.Text.Block.Action` (C) |
| L735-749 IssueBand + text | inline + `✕` literal | `Style.Panel.IssueBand` (C), `Style.Text.Block.Chip` (C), icon `Dismiss24` |
| L753-755 NumberChip/NumberText | `Chip` + literal 28 | `Style.Chip.Scrim`, `Style.Text.Block.Number` (C; `Font.Size.Composer.BlockNumber`, compact `Font.Size.Lead`) |
| L758-794 PillRow, SourcePill, SameAsChip, SizeChip | inline | `Style.Panel.BlockPills`, `Style.Chip.Scrim`, `Style.Chip.SameAs`, `Style.Chip.Size` (C) |
| L797-830 `CornerClear` + ControlTemplate | template | `Style.Button.CornerClear` (C), glyph `ui:SymbolIcon Dismiss24`, ring colour `Brush.Text.OnScrimDanger` (F2) |
| L833-845 `BlockFocusRing` | inline | `Style.Shape.BlockFocusRing` (C) |
| L852-865 Canvas strip + Replace/Move | Canvas anchor + StackPanel + MinWidth 104 | `Style.Surface.FloatingStrip` (C), `UniformGrid Columns=2`, `Style.Button.Strip` (C, `BasedOn Style.Button.Composer`; compact trigger -> `Size.Touch.Compact`) |
| L867-906 `DataTemplate.Triggers` (30 setters) | per-element compact setters | one trigger setting `BlockDensity.IsCompact` on `BlockRoot` (+ the `NoPreviewLabel` text swap); the rest lives in the styles above |
| L922, L985 rail cards, L924 heading, L987 CHANGES | `RailCard`, literals | `Style.Card.Rail`, `Style.Text.RailHeading`, `Style.Text.Status.Staged` |
| L947-962 live tile | inline | `Style.Surface.LiveTile` (C), `Style.Text.Tile.Name` / `.Label` (C, `Font.Size.Caption`) |
| L968-979 "No MultiView" text | inline | `VisibilityConverter` rule `NoItems` |
| L990-1000 "No staged changes" | inline | same converter |
| L1004 change line | literal | `Style.Text.Title` (T) |
| L1015-1047 banner Border | inline (6 triggers) | `Style.Banner.Composer` (C) |
| L1053-1074 banner glyph `i ! ✓ ✕` | letters as icons | `ui:SymbolIcon` with `Style.Icon.Banner` (C): Info24, CheckmarkCircle24, Warning24, ErrorCircle24 |
| L1076-1101 banner text | inline, LiveSetting triggers | `Style.Text.Banner` (C) (keeps `LiveSetting` Polite/Assertive) |
| L1106 Take | `Style.Button.Take` (Theme.xaml) | moves to **C** with `MinHeight = Size.Composer.Take.Min` |
| L1117-1125 TakeBlockedReason | inline | `Style.Text.Reason` (C) |
| L1135-1144, L1160-1169, L1176-1185 Undo / Discard / Clear all | 3 inline BasedOn | `Style.Button.Undo`, `Style.Button.Discard`, `Style.Button.ClearAll` (C) |
| L1194-1213 picker `Border` frame | inline attrs | `Style.Surface.Popup` (C) |
| L1230-1258 `PickerTile` ControlTemplate | template | `Style.Button.PickerTile` (C, part `bd` kept) |
| L1264-1304 picker image, NO PREVIEW, lock `Path`, note | inline | `Style.Image.Preview` (C), `Style.Text.Block.Caption`, `Style.Icon.PickerLock` (C) |
| L528 `BlockSurface` ControlTemplate | template | `Style.Button.BlockSurface` (C) |

### 3.2 Other files

| File | Today | Target |
|---|---|---|
| `LayoutSchematic.xaml` L22 ItemContainerStyle | inline | `Style.Item.Fractional.Cell` (C) |
| `LayoutSchematic.xaml` L37 (11 px), L51 (13 px) | literal fonts | `Style.Text.Schematic.Cell` (C, `Font.Size.Caption` 12), `Style.Text.Schematic.Off` (C, `Font.Size.Body` 13) |
| `LayoutSchematic.xaml` L12 aspect literal | `1.7777777777777777` | `{x:Static views:AspectRatioConverter.Default}` |
| `CompactSourceCardView.xaml` L37-45 NO PREVIEW | inline | `Style.Text.Block.Tiny` (C, `Font.Size.Caption`) with `Foreground` attr kept; visibility via `VisibilityConverter` rule `False` |
| `CompactSourceCardView.xaml` L53-66 name + status lines | inline + literals | `Style.Text.Palette.Name` (`Font.Size.Title`, SemiBold), `Style.Text.Palette.Status` (C) |
| `CompactSourceCardView.xaml` L70-118 badges | inline Border/TextBlock styles | `ui:Badge` + `Style.Badge.Staged`, `Style.Badge.Armed` (C; **shared keys, see 8.2**); text from A's `SourceCardViewModel.UsedInText` / `HasUsage` (A's T2) instead of the two triggers |
| `CompactSourceCardView.xaml` L121-134 offline overlay, L137-150 focus ring, L153-166 armed ring | inline | `Style.Shape.OfflineOverlay`, `Style.Shape.CardFocusRing`, `Style.Shape.ArmedRing` (C) |

### 3.3 Icon inventory (D3)

| Old | New | Task |
|---|---|---|
| ✓ in `StepDot` (L170, L282, L352) | `ui:SymbolIcon Checkmark24`, `FontSize={StaticResource Size.Icon.M}`, `Foreground=Brush.Text.OnAction` | 4 |
| `✓ Selected` text (L231) | `StackPanel(ui:SymbolIcon Checkmark24 Size.Icon.S, TextBlock "Selected")` | 5b |
| ✕ in IssueBand (L748) | `ui:SymbolIcon Dismiss24` (`Size.Icon.S`) + text | 7b |
| ✕ in CornerClear (L818) | `ui:SymbolIcon Dismiss24` (`Size.Icon.M`) in the 32 px round glyph | 7b |
| Banner `i`, `!`, `✓`, `✕` (L1056-1068) | `Info24`, `Warning24`, `CheckmarkCircle24`, `ErrorCircle24` by `Banner.Kind` (`Size.Icon.M`) | 8 |
| `Path Icon.Lock` 22/16/14 (L687, L883, L1291) | `ui:SymbolIcon LockClosed24`, `Size.Icon.L` (26) normal / `Size.Icon.S` (14) compact and picker | 7b, 10 |
| Back button (text only) | `ArrowLeft24` (`Size.Icon.M`) + "Back to displays" | 6 |
| Undo (text only) | `ArrowUndo24` (`Size.Icon.M`) + wrapped text | 8 |

`Icon.Lock` stays in `Icons.xaml` until session A also stops using it (`DisplayCardView` L884); once both have moved to `ui:SymbolIcon`, the foundation deletes it (section 8.2). Icon tokens are A's F3 values (`Size.Icon.S/M/L` = 14/18/26).

### 3.4 Typography (D8): 53 literals -> four tokens plus one exception

Tokens are A's F3 `Font.Size.Lead/Title/Body/Caption` = 16/14/13/12 (`Micro` 11 is dropped by D8). Central styles reference tokens; views carry no `FontSize`.

| Today | Used for | Becomes |
|---|---|---|
| 28 | block number | `Font.Size.Composer.BlockNumber` 28 (content-specific, 2.2); compact 18 -> `Lead` 16 |
| 20 | header title, step-1 hint, NoPreviewName | `Lead` 16, bold (hierarchy by weight; screenshots at the checkpoint must confirm it still reads) |
| 18 | picker title | `Lead` 16 |
| 17 | PlacePill / MovePill text | `Lead` 16 (compact 14 -> `Title` 14) |
| 16 | tile titles, source pill, picker device name, hints | `Lead` 16 |
| 15 | step number, step-1 sub-hint, banner glyph | `Title` 14 (icons are sized by `Size.Icon.*`) |
| 14 | labels, chips, reasons, changes | `Title` 14 |
| 13 | compact overlays, schematic OFF | `Body` 13 |
| 12 | live tile text, compact NO PREVIEW | `Caption` 12 |
| 11 | schematic cell number | `Caption` 12 |

---

## 4. Layout de-hardcoding table

`Size.*` tokens may be used for `Width`/`Height`/`Min*`/`Max*` (the shared guard accepts `{StaticResource Size.*}`, section 8). Foundation tokens: `Size.Touch.Min` 48, `Size.Touch.Compact` 36, `Size.Icon.S/M/L` 14/18/26. Composer tokens (defined in `Composer.xaml`): `Size.Composer.*`, `Font.Size.Composer.BlockNumber`.

| Where | Literal | Replacement |
|---|---|---|
| XAML L130 `LeftRailColumn` 280 | Width | `Width="25*"`, `MinWidth="{StaticResource Size.Composer.Rail.Left.Min}"` (240), `MaxWidth="{StaticResource Size.Composer.Rail.Left.Max}"` (**320**) |
| XAML L134 `RightRailColumn` 320 | Width | `Width="28*"`, Min 264 / Max **360** tokens |
| XAML L131, L133 16 px spacer columns | Width | removed; centre column `Width="47*"` with `Margin="16,0"` |
| XAML L1153 12 px spacer column | Width | removed; Discard/Clear all share a `UniformGrid` with button margins |
| `.xaml.cs` `FitRails`, `NarrowWidth`, `SchematicWidth/Height` DPs | 240/264/280/320, 120x70/96x56 | **deleted** |
| XAML L187 `LayoutTile MinHeight="96"` | MinHeight | `Size.Composer.Tile.Min` (96, tested) |
| XAML L225-227 `LayoutSchematic Width/Height` bound to the DPs | Width/Height | schematic fills a `6*` column; height from the panel's new aspect rule |
| XAML L375 status line `MinHeight="56"` | MinHeight | `Size.Touch.Min` (48) |
| XAML L466 `MaxWidth="560"` placeholder | MaxWidth | `Size.Composer.Text.Measure.Max` |
| XAML L684 `MaxWidth="380"` reason | MaxWidth | `Size.Composer.Overlay.Text.Max` |
| XAML L691 LockIcon 22x22 (+compact 16x16 L883-884) | Width/Height | `ui:SymbolIcon` `FontSize` `Size.Icon.L` / `Size.Icon.S` in compact |
| XAML L798 CornerClear 48x48 | Width/Height | `MinWidth/MinHeight = Size.Touch.Min` |
| XAML L811 CornerClearGlyph 32x32 | Width/Height | `Size.Composer.Badge.Round` (32; test asserts 32) |
| XAML L852 `Canvas Width="0" Height="0"` | zero-size anchor | **keep**: a layout anchor that centres the strip (allowlist 6.3) |
| XAML L859, L861 `MinWidth="104"` | MinWidth | `UniformGrid Columns="2"` gives equal widths; compact trigger drops padding |
| XAML L926 `MinHeight="120"` | MinHeight | removed (height now = width / aspect) |
| XAML L1206 `PickerFrame MaxWidth="760"` | MaxWidth | `Size.Composer.Picker.Frame.Max` (**880**) |
| XAML L1232 `PickerTile Width="220"` | Width | `WrapPanel ItemWidth="{StaticResource Size.Composer.Picker.Tile}"` (**260**) |
| XAML L1233 `MinHeight="160"` | MinHeight | removed (content-sized) |
| XAML L1260 thumbnail `Height="112"` | Height | `FractionalLayoutPanel AspectRatio=16:9` child (height = tile width / 16:9) |
| XAML L1291 picker lock 14x14 | Width/Height | `ui:SymbolIcon` `Size.Icon.S` |
| XAML L1303 `MaxWidth="180"` note | MaxWidth | removed (tile width bounds it) |
| XAML L52-53 `StepDot 28x28` | Width/Height | `StepHeader` marker `MinWidth/MinHeight = Size.Composer.StepMarker` (28) |
| Compact L5-6 `Width="292" Height="96"` | Width/Height | `MinWidth="{StaticResource Size.Composer.Palette.Card.Min}"` (**340**); height is content-sized (strip limit <= 160 stays tested) |
| Compact L34 thumbnail `112x63` | Width/Height | thumbnail column `Width="{StaticResource Size.Composer.Palette.Thumb}"` (**144**) + aspect panel for the 16:9 height |
| `LayoutSchematic` | none | n/a |
| Cell/overlay `Margin`, `Padding`, `CornerRadius`, `StrokeThickness` | spacing/decoration | allowed (not Width/Height); they move into styles |

---

## 5. Virtualization decisions (G5, D11)

The Composer contains **no `ListBox`, `ListView` or `DataGrid`** and I found no virtualizable list in these three files, so nothing is virtualized and nothing consumes `Style.ListBox.Virtualized`. Every `ItemsControl` is documented as intentionally not virtualized (each one is an allowlist entry, 6.3):

| Control | Items | Decision |
|---|---|---|
| Layout options (L182) | 4 fixed presets | Not virtualized: fixed size 4. |
| Unplaced chips (L310) | at most the number of blocks, <= 4 | Not virtualized: tiny. |
| `BlocksHost` (L483) | 2-4 blocks | **Must not be virtualized or recycled**: each block holds focus state, a drag-drop target, a roving tab stop and a mirrored `PreviewBitmap`; `FocusBlockSurface` finds containers through `ItemContainerGenerator.ContainerFromItem`. |
| Live blocks (L930) | 2-4 | Not virtualized: tiny; mirrors the same bitmaps. |
| Change lines (L1001) | one line per change, <= ~10 | Not virtualized: tiny. |
| `PickerItems` (L1222) | one tile per discovered encoder (tests use 12) | Not virtualized: bounded, inside a `ScrollViewer` with `PickerMaxHeight`; every tile must be realized because tests and keyboard focus (`FocusFirstPickerTile`, scroll-into-view) walk all tiles. If a site ever has dozens of encoders, switch to `ListBox` + `Style.ListBox.Virtualized` as a separate decision. |

Source/display card grids are not Composer files and stay exempt per G5.

---

## 6. Constraint check

### 6.1 Spec section 3

| # | Constraint | How it stays satisfied |
|---|---|---|
| 1 | TDD, STA view tests, 795 baseline | Every XAML task starts with a failing test in `ViewSmokeTests.Composer.cs`, runs the Composer filter, then the full non-live suite. The progress ratchet (Task 3) lowers a baseline per task so each commit is green while the shared hard guard still lists the views as pending. |
| 2 | Theme contract | No hex anywhere (guard). All brushes `DynamicResource`. `Composer.xaml` is scanned by `ThemeResourceUsageTests` and uses only `Brush.*` keys defined in both themes. `ui:SymbolIcon`/`ui:Badge` colour always comes from an explicit or inherited `Brush.*` token. **Video overlay rule: the `Foreground`/`Background` token reference of text and surfaces drawn over video stays as an attribute in the view; styles set size, weight, trimming, wrapping.** This keeps `VideoOverlayText_UsesOnPreviewOrOnScrimTokens` valid (same rule as A). |
| 3 | Clinical ergonomics | D4 in 2.5. Focus: all templates keep `views:InputModality.ShowsFocus` triggers; `StepHeader`, `ui:Card`, `ui:Badge` are not focusable. Spoken names unchanged (`Composer_AutomationNames_...`). Contrast: only existing tokens, plus the `OnScrimDanger` ring (lighter than the old value on the dark scrim). 1024x720: tests at 976 wide. No new floating UI: no Snackbar/InfoBar; the Replace/Move strip is the existing user-selected overlay on its own block. |
| 4 | Zero socket leaks | No receiver code touched. `PreviewBitmap` is only referenced by `Image.Source` bindings as today. Blocks are not virtualized (section 5). |
| 5 | `Brush.*` stay the API | No new colour tokens. New keys are `Style.*`, `Size.Composer.*`, `Font.Size.Composer.BlockNumber`. |
| 6 | Uncommitted work untouched | This plan edits no file other than itself. Implementation happens in a separate worktree after the checkpoints; tasks touch only the files they list. |
| 7 | Services out of scope | Untouched. |

### 6.2 Tests that change, and why

| Test (file `ViewSmokeTests*.cs` unless noted) | Change | Task |
|---|---|---|
| `Composer_CornerClear_HasAHitAreaOfAtLeast48` | replace `Text == "✕"` with "contains a `SymbolIcon` with `Symbol == Dismiss24`"; 32x32 glyph and >= 48 hit area stay | 7b |
| `Composer_InLightMode_ScrimLabels_StepDots_LiveBlocks_AndPickerTiles_KeepHighContrast` | item 2 (StepDot): find `StepHeader`s with `IsDone`, assert their `SymbolIcon.Foreground` is `Brush.Text.OnAction` and the marker background is `Brush.Action.Take`; items 1, 3 (part `bd`), 4 unchanged | 4 |
| `Composer_At1008x615_UndoLabel_IsNotClipped` (line ~992) | `undo.ActualHeight >= 56` -> `>= 48` (D4) | 8 |
| `VisualRoutingView_SwapsDisplaysForComposerWhenActive` (line ~658) | `close.ActualHeight >= 56` -> `>= 48` | 6 |
| `Composer_UnplacedChips_RenderAndArmTheSource` (line ~1157) | chips `>= 56` -> `>= 48` | 6 |
| `Composer_ReplaceMoveStrip_OpensThePicker_OrStartsAMove` (line ~1178) | `replace.ActualHeight >= 56` -> `>= 48` on a normal block; new assertions for the 36 px compact variant | 7b |
| A's Stop button assertion (line ~1130) | `>= 56` -> `>= 48` is A's change, forced by the same `Style.Button.Composer` move | foundation / A |
| `RunSta` helper in `ViewSmokeTests`, `AppVersionTests`, `MainWindowHeaderTests`, `ReleaseDefaultsTests` | use the shared `TestResources.Ensure()` (A's F6 = my R1) | foundation |
| `ThemeResourceUsageTests.VideoOverlayText_...` | add `InlineData` rows for the new overlay anchors (NoPreviewLabel, LockReason, picker NO PREVIEW, compact NO PREVIEW) | 11 |
| `FractionalLayoutPanelTests` | add 3 tests for the unbounded-axis rule; the existing 6 stay | 1 |
| `Composer_Step1_RendersFourSchematicTiles` | keep; add rail-width and schematic-aspect assertions | 5a, 5b |
| `Composer_TakeButton_IsAtLeast72High_...` (and the 72 assertion at ~755) | **unchanged** (Take keeps its content-specific 72) | - |
| `Composer_StepHeadings_AreHeadings_...` | assertions unchanged; `Named(...)` now returns `StepHeader` | 4 (stays green) |
| All other Composer tests (tab order, focus ring, drag/drop, keyboard, automation names, picker, PIP z-order, real window sizes, SourceCards keyboard) | **no change**; they are the regression net | all |
| `MultiViewComposerViewModelTests` | unchanged, except one new test for `LiveAspectRatio` if Task 9 is kept | 9 |
| New: `BlockDensityTests`, `StepHeaderTests`, `VisibilityConverterTests`, `ComposerUiV2GuardTests` (deleted in Task 12), `ComposerCheckpointShots` (manual) | new files | 2, 3, 4, 6, 13 |

### 6.3 Shared allowlist entries (D6, D3, D11) for the Composer files

The master owns the ONE allowlist file; these are the entries this plan needs, each with its reason. Anything not listed here must be zero when Task 12 removes the three files from the "pending migration" list.

| Entry | File | Guard | Reason |
|---|---|---|---|
| `Canvas Width="0" Height="0"` (1x) | `MultiViewComposerView.xaml` | G4 literal size | Zero-size anchor that centres the Replace/Move strip on its block (`NegativeHalfConverter`), so the strip may overhang a small block unclipped. It is a layout anchor, not a size. |
| `<Image Source="{Binding PreviewBitmap}">` (4x: block L545, live tile L949, picker thumbnail L1262, palette thumbnail Compact L39) | Composer, Compact | icon guard (D3) | Live-video surfaces; explicitly not icons. |
| `FractionalLayoutPanel` usage (5 instances) and `AspectRatio` attributes | Composer, Schematic | G4 | Not literals: the aspect algorithm (2.2). Listed so reviewers see that the canvas is deliberately not `*`/`Auto` sizing. The panel's C# is not scanned (no XAML literal). |
| `ItemsControl` x6 (section 5 rows) | Composer, Schematic (1) | D11 virtualization | Small bounded lists / live-preview blocks; each with the reason in section 5. |
| U+00B7 `·` text separator (4x) | Composer L477, L565, L1273; Compact L58 | icon guard (D3), only if the guard's character set includes it | Punctuation in visible strings, not an icon. If the guard is limited to symbol/emoji ranges this entry is unnecessary. |
| `Foreground`/`Background` token attributes on overlay elements | Composer, Compact | none (it is not a guard) | Listed for review only: the video-overlay rule (6.1 #2), pinned by `VideoOverlayText_...`. |

Code constants kept (not XAML, not scanned): `BlockDensity.CompactBelow` 240, `PickerMaxHeight`, `TapGesture` 8 px.

---

## 7. TDD task list

### 7.1 Preconditions and conventions

- **Foundation task zero (D10):** `ViewSmokeTests.cs` is split into `partial class ViewSmokeTests` files by area, pure move, 795 tests unchanged. This plan expects to own **`ViewSmokeTests.Composer.cs`** (Composer region lines 277-560, 700-1010, 1139-1230, 2287-2700, plus the Compact/Schematic cases). New pure unit tests get their own files.
- Foundation checkpoint (1) and Main Window checkpoint (2) are merged into `ui-v2/integration` (D7). Foundation provides: WPF-UI 4.3.0 merged per A's F2 order, `TestResources.Ensure()`, `Size.Touch.Min/Compact`, `Size.Icon.S/M/L`, `Font.Size.*`, empty `Resources/Styles/Composer.xaml` merged last among the styles, the shared hard guards + allowlist file with a "pending migration" list, `Style.Button.Composer` at 48, A's `Style.Button.Chip.Staged`, `Style.Text.Lead/Title/Body/Caption/Section`.
- Work in a worktree: `git worktree add ../AVAS-composer -b ui-v2/composer ui-v2/integration`. Merge back only at checkpoint 3.
- Common verification command (from the worktree root; fresh output folder per task):

```bash
dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj -p:BaseOutputPath=bin/uiv2b/ --filter "<FILTER>" -- RunConfiguration.DisableParallelization=true
```

- Full-suite gate at the end of each task: same command with `--filter "FullyQualifiedName!~LiveControlServerTests&FullyQualifiedName!~LiveFunctionTests"`; expected `0 failed` and a count >= the checkpoint-2 count + the task's new tests.
- Tasks 1-3 do not depend on the foundation's styles but follow D7 and also run in the worktree.

### 7.2 Tasks

### Task 1: FractionalLayoutPanel derives the missing axis from the aspect ratio

**Files:**
- Modify: `src/AvasRoutingTool/Controls/FractionalLayoutPanel.cs` (`MeasureOverride`)
- Test: `tests/AvasRoutingTool.Tests/FractionalLayoutPanelTests.cs`

**Interfaces:**
- Produces: when `AspectRatio > 0` and exactly one axis of `availableSize` is infinite, `DesiredSize` is `(w, w / AspectRatio)` or `(h * AspectRatio, h)`. Both infinite or `AspectRatio <= 0` with an infinite axis stays `(0, 0)`. `ArrangeOverride`/`CanvasRect` unchanged.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void AspectRatio_WithBoundedWidthAndUnboundedHeight_ReportsWidthOverRatio() => RunSta(() =>
{
    var panel = new FractionalLayoutPanel { AspectRatio = 16.0 / 9.0 };
    panel.Children.Add(Child(0, 0, 1, 1));
    panel.Measure(new Size(320, double.PositiveInfinity));
    Assert.Equal(320, panel.DesiredSize.Width, 1);
    Assert.Equal(180, panel.DesiredSize.Height, 1);
});
[Fact] public void AspectRatio_WithBoundedHeightAndUnboundedWidth_ReportsHeightTimesRatio() => RunSta(() => { /* Measure(inf, 90) -> (160, 90) */ });
[Fact] public void NoAspectRatio_WithOneUnboundedAxis_StillReportsZero() => RunSta(() => { /* Measure(320, inf) -> (0, 0) */ });
```

- [ ] **Step 2: Run to verify they fail**: `--filter "FullyQualifiedName~FractionalLayoutPanelTests"`. Expected: the first two FAIL (height 0), the third passes.
- [ ] **Step 3: Implement** in `MeasureOverride`: replace the unconditional "any infinity returns (0,0)" branch with the rule above; measure children against the derived canvas rect.
- [ ] **Step 4: Run** (all 9 panel tests). Expected: PASS.
- [ ] **Step 5: Commit** `feat(ui-v2/composer): FractionalLayoutPanel derives the free axis from AspectRatio` (+ memory files).

### Task 2: BlockDensity attached property

**Files:**
- Create: `src/AvasRoutingTool/Views/BlockDensity.cs`
- Test: `tests/AvasRoutingTool.Tests/BlockDensityTests.cs`

**Interfaces:**
- Produces: `public static class BlockDensity { public const double CompactBelow = 240; public static readonly DependencyProperty IsCompactProperty; public static bool GetIsCompact(DependencyObject d); public static void SetIsCompact(DependencyObject d, bool v); }` with `FrameworkPropertyMetadataOptions.Inherits`, default `false`.

- [ ] **Step 1: Failing tests**: `IsCompact_DefaultsToFalse`; `IsCompact_InheritsToDescendants` (set on a `StackPanel`, read on a nested `TextBlock` inside a `Window` on the STA thread); `CompactBelow_Is240`; `LessThanConverter_WithCompactBelow_IsTrueBelow240Only` (239.9 -> true, 240 -> false, 0 -> false, parameter passed as the boxed constant).
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~BlockDensityTests"`. Expected: compile error / FAIL.
- [ ] **Step 3: Implement** the static class (attached DP, `Inherits`).
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `feat(ui-v2/composer): BlockDensity inherited property for compact overlays`.

### Task 3: Register the Composer files with the shared guards and add a progress ratchet

**Files:**
- Modify: the master's shared allowlist / pending-migration file (add the three Composer files and the 6.3 entries; the exact file name is the master's)
- Create: `tests/AvasRoutingTool.Tests/ComposerUiV2GuardTests.cs`

**Interfaces:**
- Produces: `static class ComposerMetrics { static int KeyedStyles(string xaml); InlineStyles; ControlTemplates; NumericSizeAttrs; GlyphIcons; TargetNameSetters; FontSizeLiterals; }` over the raw XAML text, and a `Baseline` dictionary `{ file -> metric -> max }`. Metric definitions: keyed = `<Style x:Key`; inline = `<Style ` or `<Style>` minus keyed; templates = `<ControlTemplate ` and `<ControlTemplate>`; numeric size = `(Min|Max)?(Width|Height)="<digits>"` attributes plus `Property="(Min|Max)?(Width|Height)" Value="<digits>"` setters (except `Width="0" Height="0"` on a `Canvas`); glyph icons = `✓ ✕ ⓘ` characters in `Text`/`Value` plus `Text="i"`/`"!"`; target-name setters = `<Setter TargetName=` inside `DataTemplate.Triggers`; font literals = `FontSize="<digits>"` and `Property="FontSize" Value="<digits>"`.

- [ ] **Step 1: Failing test** `Metrics_MeasureTheRealFiles`: asserts today's exact values: Composer keyed 4, inline 44, templates 4, glyph 10, numeric size 31 (25 attributes + 6 setters, Canvas anchor excluded), target-name setters >= 28, font literals 46; Compact inline 8, numeric 4, font literals 5; Schematic inline 1, font literals 2. Fails (type missing).
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~ComposerUiV2GuardTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** `ComposerMetrics` (regex, file read via the existing `SourceRoot()` pattern from `ThemeResourceUsageTests`). Add `Guard_NeverExceedsBaseline` theory comparing each metric to `Baseline` (today's values). Add the three files to the shared pending-migration list.
- [ ] **Step 4: Run.** Expected: PASS.
- [ ] **Step 5: Commit** `test(ui-v2/composer): register Composer files with the shared guards, progress ratchet`.

> From here on every task's **Step 1** also lowers the affected baseline numbers (that is the red); Task 12 deletes the ratchet and removes the files from the pending list so the hard guards take over.

### Task 4: StepHeader control, replacing the three StepDot blocks

**Files:**
- Create: `src/AvasRoutingTool/Controls/StepHeader.cs`; `Style.StepHeader` in `Resources/Styles/Composer.xaml`
- Modify: `Views/MultiViewComposerView.xaml` (L151-180, L263-292, L333-362; remove the keyed `StepDot`; `RailHeading` stays until Task 6)
- Test: `tests/AvasRoutingTool.Tests/StepHeaderTests.cs`, `ViewSmokeTests.Composer.cs`

**Interfaces:**
- Produces: `public class StepHeader : Control` with DPs `int Number`, `bool IsDone`, `string Title`; ctor sets `Focusable=false`, `IsTabStop=false`; `OnCreateAutomationPeer` returns a peer with `ControlType.Text`. Used as `<controls:StepHeader x:Name="StepLayoutHeading" Style="{StaticResource Style.StepHeader}" Number="1" Title="LAYOUT" IsDone="{Binding StepLayoutDone}" AutomationProperties.Name="{Binding StepLayoutText}" />` (HeadingLevel2 set by the style). Step 3 binds `IsDone` to `CanTake`. The title uses A's `Style.Text.Section`.

- [ ] **Step 1: Failing tests**
  - `StepHeader_IsNotAFocusStop`: `Focusable == false && IsTabStop == false`.
  - `StepHeader_NotDone_ShowsNumber_Done_ShowsCheckSymbol`: render with `Number=2`: visible `TextBlock` "2", no visible `SymbolIcon`; `IsDone=true`: `SymbolIcon.Symbol == SymbolRegular.Checkmark24`, "2" not visible; marker background is `Brush.Action.Take` when done.
  - Edit the Light-mode test item 2 (6.2). `Composer_StepHeadings_...` and `Composer_TabOrder_LeftRail_...` must stay green unchanged.
  - Ratchet: keyed 4 -> 3; glyph 10 -> 7.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~StepHeader|FullyQualifiedName~Composer_StepHeadings|FullyQualifiedName~Composer_InLightMode|FullyQualifiedName~Composer_TabOrder|FullyQualifiedName~ComposerUiV2Guard"`. Expected: new tests FAIL.
- [ ] **Step 3: Implement** the control, `Style.StepHeader` (marker `Border` `MinWidth/MinHeight = Size.Composer.StepMarker`, number/check swap by `IsDone` trigger, number in `Font.Size.Title`), and replace the three XAML blocks.
- [ ] **Step 4: Run** the same filter plus the full non-live suite. Expected: PASS.
- [ ] **Step 5: Commit** `feat(ui-v2/composer): StepHeader control replaces three copy-pasted step markers`.

### Task 5a: Rails as clamped star columns (remove FitRails)

**Files:**
- Modify: `Views/MultiViewComposerView.xaml` (L128-135, L137-139 centre margin, L1153), `Views/MultiViewComposerView.xaml.cs` (delete `FitRails`, `NarrowWidth`, `SchematicWidth/Height` DPs, the `SizeChanged` hookup), `Resources/Styles/Composer.xaml` (`Size.Composer.Rail.*`)
- Test: `ViewSmokeTests.Composer.cs`

**Interfaces:**
- Produces: `Size.Composer.Rail.Left.Min` 240, `.Left.Max` 320, `.Right.Min` 264, `.Right.Max` 360 (`sys:Double`).

- [ ] **Step 1: Failing tests**: `Composer_Rails_AreStarColumnsClampedByTokens`: `LeftRailColumn.Width.IsStar && RightRailColumn.Width.IsStar` (fails today: absolute); at `ShowingAt(view, 976, 600)` left rail cell 240 +- 1, right 264 +- 1, centre 440 +- 2 (the 1024x720 floor, identical to today); at 1100 the left rail is `> 250 && < 320`; at 1920 left 320, right 360. New `Composer_At1920x1080_UsesTheFullHdSpace` in a maximized-size `VisualRoutingView` (1904x~960 client): records canvas size and asserts blocks of a 2x2 are each >= 480 wide, Take visible, rails at their caps; this is also the measurement behind the 2.3 estimates. `Composer_InVisualRoutingView_AtRealWindowSizes_...` stays green. Ratchet: numeric size 31 -> 26 (the five column literals).
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~Composer_Rails|FullyQualifiedName~Composer_At1920x1080|FullyQualifiedName~Composer_InVisualRoutingView"`. Expected: first two FAIL.
- [ ] **Step 3: Implement**: columns `25*` / `47*` / `28*` with Min/Max tokens (`StaticResource`), centre `Margin="16,0"`, delete the code-behind; status row `MinHeight = Size.Touch.Min`.
- [ ] **Step 4: Run** filter + full suite. Expected: PASS.
- [ ] **Step 5: Commit** `refactor(ui-v2/composer): rails are token-clamped star columns, FitRails removed`.

### Task 5b: Layout tile style and proportional schematic

**Files:**
- Modify: `Views/MultiViewComposerView.xaml` (L182-260), `Views/LayoutSchematic.xaml` (+`.cs` untouched), `Resources/Styles/Composer.xaml` (`Style.Button.ComposerLayoutOption`, `Style.Text.Selected`, `Style.Text.Pending`, `Style.Item.Fractional.Cell`, `Style.Text.Schematic.*`, `Size.Composer.Tile.Min`)
- Test: `ViewSmokeTests.Composer.cs`

- [ ] **Step 1: Failing tests**: `Composer_LayoutTile_UsesCentralStyle_KeepsPartBd_AndSchematicScales`: `Assert.Same(FindResource("Style.Button.ComposerLayoutOption"), tile.Style)`; `tile.Template.FindName("bd", tile)` is a `Border`; at 976 wide each schematic has `ActualWidth` in [88,125] and `ActualHeight == ActualWidth * 9/16 +- 1`; the Selected row contains a `SymbolIcon` (`Checkmark24`) and the text "Selected"; 4 tiles, `ActualHeight >= 96`; `LayoutSchematic_CellNumbers_AreAtLeast12` (the cell `TextBlock.FontSize >= 12`). `Composer_Step1_RendersFourSchematicTiles` stays. Ratchet: templates 4 -> 3, inline 44 -> 42, glyph 7 -> 6, font literals lowered, Schematic inline 1 -> 0.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~Composer_LayoutTile|FullyQualifiedName~LayoutSchematic|FullyQualifiedName~Composer_Step1"`. Expected: FAIL.
- [ ] **Step 3: Implement**: tile content `Grid` columns `6*`/`7*`; keep `AutomationProperties.Name` and `Click="LayoutTile_Click"`; move the template to the central style (all triggers, `InputModality.ShowsFocus` ring, part `bd`); `OverridesDefaultStyle=True`.
- [ ] **Step 4: Run** + `Composer_FocusRing_ShowsOnEveryControlThatTakesFocus` + full suite. Expected: PASS.
- [ ] **Step 5: Commit** `refactor(ui-v2/composer): central layout option style, proportional schematic`.

### Task 6: Header, rail cards, status line, visibility converter

**Files:**
- Modify: `Views/ComposerConverters.cs` (add `VisibilityConverter`), `Views/MultiViewComposerView.xaml` (L24-60 keyed styles, L70-124 header, L222-260 cards, L374-420 status/Done, L986-1009 changes), `Resources/Styles/Composer.xaml` (`Style.Card.Rail`, `Style.Badge.Info`, `Style.Button.Done`, `Style.Text.Status.*`, `Style.Text.RailHeading`, `Style.Panel.BusyDisables`)
- Test: `tests/AvasRoutingTool.Tests/VisibilityConverterTests.cs`, `ViewSmokeTests.Composer.cs`

**Interfaces:**
- Produces: `public sealed class VisibilityConverter : IValueConverter { public VisibilityRule Rule { get; set; } }` with `enum VisibilityRule { True, False, NotNull, Null, NotEmpty, HasItems, NoItems }`, collapsing otherwise. Declared in `UserControl.Resources` as instances (converters are not styles). Session A's foundation `Converter.*` set may already cover `True/False/Null/NotNull`; reuse those and add only `NotEmpty/HasItems/NoItems` if missing.

- [ ] **Step 1: Failing tests**: `VisibilityConverterTests` (one assertion per rule incl. `null`, `""`, empty array, non-empty list). View tests: `Composer_BackButton_HasArrowIcon_AndKeepsName` (contains `SymbolIcon` `ArrowLeft24`; name "Back to displays"; `ActualHeight >= 48`); `Composer_RailCards_AreUiCards_WithAppBackground` (both are `Wpf.Ui.Controls.Card`, `Background` equals `Brush.Surface.Card`); `Composer_HeaderChips_AreBadges_AndCollapseWhenEmpty` (`Wpf.Ui.Controls.Badge`; with no layout chosen the `OutputText` chip is not visible). Edit line ~658 (`close >= 56` -> `>= 48`). Ratchet: keyed 3 -> 1 here (`Chip` goes in 7a); inline lowered by the number removed.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~VisibilityConverter|FullyQualifiedName~Composer_BackButton|FullyQualifiedName~Composer_RailCards|FullyQualifiedName~Composer_HeaderChips"`. Expected: FAIL.
- [ ] **Step 3: Implement** converter and the XAML/styles; `Style.Card.Rail` is `BasedOn="{StaticResource {x:Type ui:Card}}"` with our `Background`, `BorderBrush`, `Border.CornerRadius`, margin `0,0,4,12`; `Style.Badge.Info` is `BasedOn="{StaticResource {x:Type ui:Badge}}"` with our tokens and `Appearance` unchanged.
- [ ] **Step 4: Run** filter + `Composer_AutomationNames_*` + `Composer_Esc_*` + full suite. Expected: PASS.
- [ ] **Step 5: Commit** `refactor(ui-v2/composer): ui:Card rails, ui:Badge chips, icon Back button, central header styles`.

### Task 7a: Block overlays: text, chips, pills, density

**Files:**
- Modify: `Views/MultiViewComposerView.xaml` (L505-585 content, L627-665, L707-732, L752-794, L867-906), `Resources/Styles/Composer.xaml` (`Style.Chip.Scrim|SameAs|Size`, `Style.Text.Block.*`, `Style.Pill.Action`, `Style.Panel.BlockPills`, `Style.Panel.BlockContent`, `Style.Panel.NoPreview`, `Style.Panel.PlaceOverlay`, `Font.Size.Composer.BlockNumber`)
- Test: `ViewSmokeTests.Composer.cs`

**Interfaces:**
- Consumes: Task 2 `BlockDensity`. The DataTemplate keeps exactly two triggers: `DropOk` (shows `DropRing`) and `ActualWidth < BlockDensity.CompactBelow`, which sets `BlockDensity.IsCompact=True` on `BlockRoot` and swaps the `NoPreviewLabel` text.

- [ ] **Step 1: Failing tests**: `Composer_SmallBlocks_AreCompact_ThroughBlockDensity` using `quad_1_3` in a 976 wide view: for blocks narrower than 240, `BlockDensity.GetIsCompact(BlockRoot) == true`, `NumberText.FontSize == 16` (`Font.Size.Lead`), `SizeChip` not visible, `EmptyHint` not visible; for the large block `IsCompact == false`, `NumberText.FontSize == 28`, `SizeChip` visible. No overlay text is below 12. `Composer_InVisualRoutingView_AtRealWindowSizes_...` and the light-mode scrim-label check stay green. Ratchet: target-name setters ~30 -> 2; keyed 1 -> 0; font literals lowered.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~Composer_SmallBlocks|FullyQualifiedName~Composer_InVisualRoutingView|FullyQualifiedName~Composer_InLightMode"`. Expected: first FAIL.
- [ ] **Step 3: Implement** styles with `<Trigger Property="views:BlockDensity.IsCompact" Value="True">` blocks reproducing every old compact setter (values in L873-905, with the D8 font mapping). Keep every `x:Name` and every overlay `Foreground`/`Background` token attribute in the view.
- [ ] **Step 4: Run** filter + `ThemeResourceUsageTests` + full suite. Expected: PASS.
- [ ] **Step 5: Commit** `refactor(ui-v2/composer): block overlays use central styles and BlockDensity`.

### Task 7b: Block surface, state ring, corner clear, strip, lock icon

**Files:**
- Modify: `Views/MultiViewComposerView.xaml` (L516-533 BlockSurface, L589-624, L672-704, L735-750, L797-865), `Resources/Styles/Composer.xaml` (`Style.Button.BlockSurface`, `Style.Shape.BlockState`, `Style.Shape.BlockFocusRing`, `Style.Panel.UnavailableOverlay`, `Style.Icon.BlockLock`, `Style.Panel.IssueBand`, `Style.Button.CornerClear`, `Style.Surface.FloatingStrip`, `Style.Button.Strip`, `Style.Item.Fractional.Block`, `Size.Composer.Badge.Round`)
- Test: `ViewSmokeTests.Composer.cs`

- [ ] **Step 1: Failing tests**: update `Composer_CornerClear_HasAHitAreaOfAtLeast48` (SymbolIcon `Dismiss24` instead of text; hit area >= 48; `CornerClearGlyph` exactly 32x32; `IsTabStop == false`); `Composer_ReplaceMoveStrip_...` (line ~1178 `>= 48`; buttons have equal `ActualWidth`; on a compact block both are `Size.Touch.Compact` high (>= 36, < 48) with an 8 px gap); `Composer_UnavailableBlock_ShowsLockAndReasonText` unchanged but `LockIcon` is now a `SymbolIcon` named `LockIcon`; `Composer_Pip_LowestWindowIndexIsPaintedOnTop`, drag/drop, keyboard and focus-ring tests unchanged. Ratchet: templates 3 -> 1 (PickerTile goes in Task 10), glyph 6 -> 4, numeric attrs and inline styles lowered.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~Composer_CornerClear|FullyQualifiedName~Composer_ReplaceMove|FullyQualifiedName~Composer_UnavailableBlock|FullyQualifiedName~Composer_Pip|FullyQualifiedName~Composer_FocusRing|FullyQualifiedName~Composer_Keyboard|FullyQualifiedName~Composer_DropSource"`. Expected: first two FAIL.
- [ ] **Step 3: Implement**; the CornerClear ring uses `Brush.Text.OnScrimDanger` (F2) and keeps its `ShowsFocus` trigger; the strip keeps the zero-size `Canvas` + `NegativeHalfConverter` centring so it may overhang small blocks.
- [ ] **Step 4: Run** filter + full suite. Expected: PASS.
- [ ] **Step 5: Commit** `refactor(ui-v2/composer): central block surface, corner clear and strip with SymbolIcons`.

### Task 8: Right rail: ON SCREEN NOW, banner, Take and action buttons

**Files:**
- Modify: `Views/MultiViewComposerView.xaml` (L920-1190), `Resources/Styles/Composer.xaml` (`Style.Surface.LiveTile`, `Style.Text.Tile.*`, `Style.Banner.Composer`, `Style.Icon.Banner`, `Style.Text.Banner`, `Style.Text.Reason`, `Style.Button.Undo|Discard|ClearAll`, `Style.Button.Take` moved here, `Size.Composer.Take.Min`)
- Test: `ViewSmokeTests.Composer.cs`

- [ ] **Step 1: Failing tests**: `Composer_Banner_ShowsASeverityIconPerKind` (Info -> `Info24`, Success -> `CheckmarkCircle24`, Warning -> `Warning24`, Error -> `ErrorCircle24`; no `TextBlock` with Text `i`, `!`, `✓`, `✕` remains in the banner); `Composer_Undo_HasUndoIcon_AndTheLabelStillWraps` (`ArrowUndo24`; the 1008x615 test now asserts `>= 48`); `Composer_OnScreenPicture_KeepsItsSpokenName_AndMiniCanvasFollowsWidth` (height == width / (16/9) +- 1 at the 264 rail); `Composer_Take_IsTheOnlyControlAbove48` (Take `ActualHeight >= 72`, every other Composer button `ActualHeight` in [36, 56)). `Composer_TakeButton_IsAtLeast72High_...` (Take `Content` stays a `string`) and `Composer_AutomationNames_...` (live regions Polite/Assertive) unchanged. Ratchet: glyph 4 -> 0.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~Composer_Banner|FullyQualifiedName~Composer_Undo|FullyQualifiedName~Composer_OnScreen|FullyQualifiedName~Composer_Take|FullyQualifiedName~Composer_AutomationNames|FullyQualifiedName~Composer_At1008x615"`. Expected: first three FAIL.
- [ ] **Step 3: Implement**; `OnScreenPicture` panels use `AspectRatio="{x:Static views:AspectRatioConverter.Default}"`; `Take` keeps string content and its pinned position, 8+ px from neighbours.
- [ ] **Step 4: Run** filter + full suite. Expected: PASS.
- [ ] **Step 5: Commit** `refactor(ui-v2/composer): central right-rail styles and severity SymbolIcons`.

### Task 9: ON SCREEN NOW follows the live plan's aspect (verify, then fix)

**Files:**
- Modify: `src/AvasRoutingTool/ViewModels/MultiViewComposerViewModel.cs` (one read-only property), `Views/MultiViewComposerView.xaml` (the two mini panels)
- Test: `tests/AvasRoutingTool.Tests/MultiViewComposerViewModelTests.cs`, `ViewSmokeTests.Composer.cs`

**Interfaces:**
- Produces: `public double LiveAspectRatio { get; }` = live plan width / height, `AspectRatioConverter.Default` (16:9) when nothing is live or the plan is degenerate; raised with the other live-derived properties in `RaiseDerived`.

- [ ] **Step 1: Failing tests**: VM `LiveAspectRatio_FollowsTheLivePlan_AndDefaultsTo16x9` (live plan 1920x1200 returns 1.6; none live returns `16/9`); view `Composer_OnScreenPicture_UsesTheLiveAspect` (mini panel `AspectRatio == c.LiveAspectRatio`). If a non-16:9 live plan cannot be built in the rig (live plans always come from a 16:9 canvas), record that in the test and close the task as "no defect; literal replaced by `Default`" (Task 8 already did that).
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~LiveAspectRatio|FullyQualifiedName~Composer_OnScreenPicture"`. Expected: FAIL.
- [ ] **Step 3: Implement** the property and bind both panels (`AspectRatio="{Binding LiveAspectRatio, Mode=OneWay}"`).
- [ ] **Step 4: Run** filter + `MultiViewComposerViewModelTests` + full suite. Expected: PASS.
- [ ] **Step 5: Commit** `fix(ui-v2/composer): ON SCREEN NOW mini picture uses the live plan aspect`.

### Task 10: Picker popup tiles from tokens

**Files:**
- Modify: `Views/MultiViewComposerView.xaml` (L1193-1313), `Resources/Styles/Composer.xaml` (`Style.Surface.Popup`, `Style.Button.PickerTile`, `Style.Image.Preview`, `Style.Icon.PickerLock`, `Size.Composer.Picker.Tile` 260, `Size.Composer.Picker.Frame.Max` 880)
- Test: `ViewSmokeTests.Composer.cs`

- [ ] **Step 1: Failing tests**: `Composer_PickerTile_UsesCentralStyle_AndKeepsPartBd` (`Assert.Same` on the style; `bd` part is a `Border` with `Brush.Border.Control` at rest, as the Light-mode test needs); `Composer_PickerPopup_TilesAreTokenWide_AndThumbnailsAre16x9` (every tile `ActualWidth` equals the token +- 1 including margin handling, thumbnail ratio 16/9 +- 0.05 and >= 160x90; frame `MaxWidth` equals the token); `Composer_PickerPopup_DisabledTile_ShowsLockSymbol` (`SymbolIcon LockClosed24` visible only for disabled tiles). `Composer_PickerPopup_WithTwelveEncoders_...` and the keyboard picker test unchanged. Ratchet: templates 1 -> 0, picker numeric attrs -> 0.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~Composer_Picker"`. Expected: first three FAIL.
- [ ] **Step 3: Implement** (WrapPanel `ItemWidth`, thumbnail inside a 16:9 `FractionalLayoutPanel` whose child has `W=1,H=1`).
- [ ] **Step 4: Run** filter + full suite. Expected: PASS.
- [ ] **Step 5: Commit** `refactor(ui-v2/composer): picker tiles, frame and thumbnails from tokens`.

### Task 11: CompactSourceCardView and LayoutSchematic cleanup

**Files:**
- Modify: `Views/CompactSourceCardView.xaml`, `Views/LayoutSchematic.xaml`, `Resources/Styles/Composer.xaml` (`Style.Text.Palette.*`, `Style.Badge.Staged|Armed`, `Style.Shape.*`, `Size.Composer.Palette.Card.Min` 340, `Size.Composer.Palette.Thumb` 144), `tests/AvasRoutingTool.Tests/ThemeResourceUsageTests.cs`
- Test: `ViewSmokeTests.Composer.cs`

- [ ] **Step 1: Failing tests**: `CompactSourceCard_HasNoFixedSize_AndKeepsAThumbnailAt16x9` (`ReadLocalValue(WidthProperty)` and `HeightProperty` are `DependencyProperty.UnsetValue`; `ActualWidth >= token`; thumbnail `Image` ratio 16/9 +- 0.05; strip still `<= 160` in the 1008x615 `VisualRoutingView` test); `CompactSourceCard_Badges_AreUiBadges` (`UsedInBlocksBadge` is a `Wpf.Ui.Controls.Badge` showing "in 1 block"/"in 2 blocks" from `UsedInText`, "Placing" visible when armed). Extend `VideoOverlayText_...` `InlineData` with the new anchors. `SourceCards_Keyboard_...` (focus ring pixels, names) must stay green. Ratchet: Compact inline 8 -> 0, numeric 4 -> 0, font literals 5 -> 0.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~CompactSourceCard|FullyQualifiedName~SourceCards_Keyboard|FullyQualifiedName~ThemeResourceUsage|FullyQualifiedName~Composer_InVisualRoutingView"`. Expected: first two FAIL.
- [ ] **Step 3: Implement**; `VisualRoutingView` (session A's file) needs no edit: the palette item keeps `Margin="0,0,10,0"`.
- [ ] **Step 4: Run** filter + full suite. Expected: PASS.
- [ ] **Step 5: Commit** `refactor(ui-v2/composer): palette card and schematic use central styles and tokens`.

### Task 12: Close out: hard guards take over, docs

**Files:**
- Modify: shared allowlist / pending-migration file (remove the three Composer files from "pending", keep the 6.3 entries), delete `tests/AvasRoutingTool.Tests/ComposerUiV2GuardTests.cs`, `Handover/02_CODEBASE_STRUCTURE.md` (add `Controls/StepHeader`, `Views/BlockDensity`, `Resources/Styles/Composer.xaml`), `memory/active_context.md`, `memory/progress.md`

- [ ] **Step 1: Failing test**: remove the three files from the pending list. The shared hard guards (templates, keyed styles, literal Width/Height, `FontSize` literals and floor, glyph icons) run on them and must pass with only the 6.3 allowlist entries. Fails if anything regressed.
- [ ] **Step 2: Run** the shared guard tests. Expected: PASS if Tasks 4-11 are complete; fix any leftover.
- [ ] **Step 3: Delete** the progress ratchet; update handover docs.
- [ ] **Step 4: Full suite** with the non-live filter. Expected: `0 failed`.
- [ ] **Step 5: Commit** `chore(ui-v2/composer): hard guards take over, handover docs and memory`.

### Task 13: Checkpoint 3 deliverables

**Files:**
- Create: `tests/AvasRoutingTool.Tests/ComposerCheckpointShots.cs` (renders states with `RenderTargetBitmap` through the existing `ComposerRig`, no hardware; returns immediately unless the environment variable `UIV2_SHOTS` is set; name has no `Live`), `ui_screenshots/UI-v2/composer/` (output)
- Modify: `memory/active_context.md`, `memory/progress.md`

- [ ] **Step 1: Write the harness** (a non-asserting test that saves PNGs): five states x two sizes (1920x1080 maximized client, 1008x615 = 1024x720 floor) x two themes (Dark, Light) = **20 screenshots**: A) Step 1 with nothing chosen; B) 2x2 staged with an armed source; C) PIP with the Replace/Move strip open on the inset; D) picker open; E) after Take with the success banner and Undo.
- [ ] **Step 2: Run** with `UIV2_SHOTS=1`; check each PNG; fix layout defects found (this is the visual review of D8/D4/D9 effects: hierarchy at 16 px, compact strip, wider rails).
- [ ] **Step 3: Manual checks** recorded in `memory/active_context.md`: Tab walk (Back, 4 tiles, one block, Discard, Clear all, Take); drag a card and a block; arm and place; open the picker with the keyboard and with a tap; Narrator reads step headings, tiles ("..., selected"), blocks, banner; Dark/Light toggle repaints every Composer element.
- [ ] **Step 4: Checkpoint package** for the user: runnable branch `ui-v2/composer`, test count (full non-live suite), guard status (hard guards green, allowlist entries listed), the 20 screenshots, open risks.
- [ ] **Step 5: Commit** `docs(ui-v2/composer): checkpoint 3 deliverables`.

**Total: 15 tasks** (1, 2, 3, 4, 5a, 5b, 6, 7a, 7b, 8, 9, 10, 11, 12, 13), one commit each.

### 7.3 Checkpoint 3 deliverable list (D7)

1. Branch `ui-v2/composer`, merged into `ui-v2/integration` on the user's approval; `main` untouched.
2. Full non-live test count (>= checkpoint 2 + new tests), `0 failed`; ViewSmokeTests STA green.
3. Hard guards green for the Composer files with only the 6.3 allowlist entries.
4. 20 screenshots in `ui_screenshots/UI-v2/composer/` (5 states x 1920x1080 and 1024x720 x Dark and Light).
5. Keyboard and Narrator walk-through notes; known limits.
6. A short before/after table of the 2.3 numbers measured by the tests (rails, canvas, tiles, picker, palette).

---

## 8. Foundation requests and risks

### 8.1 Requests (aligned with session A's F1-F7)

| Mine | A's | Status | Detail |
|---|---|---|---|
| R1 shared test resource helper | F6 `TestResources.Ensure()` | **Same request.** | One helper merging exactly what `App.xaml` merges (WPF-UI dictionaries, palette, `Styles/*`, `Theme.xaml`, `Icons.xaml`), idempotent, adopted once by the master in the 4 bootstrap sites. I need `Composer.xaml` and `Converters.xaml` in its list. |
| R2 split `ViewSmokeTests.cs` | D10 | **Foundation task zero**, decided. | I own `ViewSmokeTests.Composer.cs`. |
| R3 empty `Composer.xaml` | F5 | **Same request.** | Merged **last** among `Styles/*` so it can use `BasedOn` of `Buttons/Text/Cards` keys. |
| R4 tokens | F3 | **Use A's list.** | I need `Size.Touch.Min/Compact`, `Size.Icon.S/M/L` (14/18/26 - I adopted A's values, my earlier 16/22 are dropped), `Font.Size.Lead/Title/Body/Caption` (16/14/13/12). **Dropped:** my Comfort 56 / Primary 72. **Conflict to resolve:** F3's `Font.Size.Micro` 11 violates D8 and should go. G4 guard must accept `{StaticResource Size.*}` in Width/Height/Min/Max. Everything else (`Size.Composer.*`, `Font.Size.Composer.BlockNumber`) is mine, in `Composer.xaml`. |
| R5 text roles | F4 `Text.xaml` | **Use A's roles** `Style.Text.Lead/Title/Body/Caption/Section`; my 20/18 roles and `SectionHeading` are dropped. `Style.Text.ComposerLabel` (14) stays, moved by the foundation. |
| R6 stable keys | F4 `Buttons.xaml` | **Changed.** `Style.Button.Composer` becomes 48 (D4; A's Stop button test line ~1130 changes too). `Style.Button.Take` is **not** moved to the foundation: it moves into `Composer.xaml` (only user; keeps the content-specific 72). Keep `Style.Button.CardAction`, `Style.FocusVisual.Ring`, `Style.ScrollBar.Thin`, the implicit `ScrollViewer` style stable. |
| R7 overlay Foreground rule | A §3.3 | **Same rule**, already adopted by A. |
| R8 merge order | F2 | **Same**: `ui:ThemesDictionary`, `ui:ControlsDictionary`, palette, `Styles/*`, `Theme.xaml`, `Icons.xaml`. `Style.Card.Rail`/`Style.Badge.*` use `{StaticResource {x:Type ui:Card|Badge}}`. |
| R9 icon font | - | **Mine only**: confirm `ui:ControlsDictionary` supplies `FluentSystemIcons` (SymbolIcon uses `SetResourceReference`); `TestResources.Ensure()` must include it. Missing font = blank icons, no exception. |
| R10 implicit WPF-UI styles | A's R4 | **Same concern.** The Composer is the un-migrated-controls case A names: master and B review the visible effect before F2 merges (plain `Button`, `ScrollViewer`, `ToolTip`, `Popup`). All Composer buttons with local templates get explicit styles with `OverridesDefaultStyle=True`. |
| R11 hoist overlay styles | - | Optional at cross-review: if `DisplayCardView` ends with the same scrim chip/overlay text styles, hoist `Style.Chip.Scrim` and `Style.Text.Block.*` to `Overlays.xaml`. |

### 8.2 Overlaps and conflicts found against session A's plan

1. **`Style.Button.LayoutTile`** is A's (display-card layout menu, `MinHeight` 88, part `bd`). Mine is renamed **`Style.Button.ComposerLayoutOption`** (96, part `bd`). Both must keep part `bd` because two tests read it (lines ~1929, ~3871).
2. **`Style.Button.Chip.Staged`** (A, `Buttons.xaml`): I consume it for the unplaced-source chips instead of defining my own.
3. **Staged / Armed badges.** CompactSourceCardView's "in N blocks" and "Placing" are the same visual as `SourceCardView`'s usage and armed chips. Proposal: ONE pair `Style.Badge.Staged` and `Style.Badge.Armed` (`ui:Badge`-based) in the foundation badge section, used by both; I define them in `Composer.xaml` only if A has not by the time Task 11 starts. I reuse A's `SourceCardViewModel.UsedInText`/`HasUsage` (A's T2) instead of two triggers; if T2 is not merged, Task 11 keeps the trigger text.
4. **`Icon.Lock`.** A says it is shared and stays. After Tasks 7b/10 the Composer no longer uses it; `DisplayCardView` L884 is the last user. D3 says stock glyphs use `ui:SymbolIcon`: ask A to move that one to `LockClosed24` too so the foundation can delete `Icon.Lock`. **F7 confirmation:** the Composer does not use `Icon.Sun/Moon/Refresh/Settings`.
5. **Icon sizes**: A's `Size.Icon.S/M/L` 14/18/26 replace my earlier 16/22 (lock overlay is 26, picker/compact lock 14, banner/back/undo/step 18).
6. **`Style.Text.Section`** (A) is reused as the base of my `Style.Text.RailHeading`.
7. **`Style.Button.Composer` at 48** affects A's Stop button and its test; flagged as a shared consequence of D4.
8. **Test file**: both sessions' new tests live in their own partial files after D10, so no `ViewSmokeTests.cs` collisions.

### 8.3 Risks and open questions

1. **Canvas cannot grow at Full HD without vertical room (D9).** At 1920x1080 the canvas is height-bound (about 655 px tall); wider rails and bigger thumbnails use the spare width, but the canvas stays the same size. A larger canvas needs height from session A's rows (palette strip <= 160, header). Master/user: is that trade worth discussing? Estimates are verified by Task 5a's test.
2. **Hierarchy at 16 px max.** D8's 16/14/13/12 flattens the 20 px title and 28 px number. I kept one exception (block number 28); compact block number falls from 18 to 16. The checkpoint screenshots decide whether a second exception (title 20) is needed; it would be one more content-specific token.
3. **Compact strip at 36 px (D4).** Replace/Move on blocks narrower than 240 px are 36 px high. Touch-glove use on a PIP inset is the doubt; fallback is 48 with the strip overhanging the inset (already allowed by the zero-size Canvas anchor).
4. **`ui:SymbolIcon` is a font glyph**, not a path (allowed by D3/G2). It depends on the `FluentSystemIcons` font being available (R9). Icons are sized only by `FontSize`.
5. **`StepHeader` automation.** A custom `Control` replaces a `TextBlock` for step headings. The custom peer reports `ControlType.Text` with the bound name and HeadingLevel2; confirm with Narrator (Task 13).
6. **Inherited `BlockDensity` property** is inherited by every element in a block (2-4 blocks): fine. Verify no style trigger reads it outside a block.
7. **Rails proportion 25:47:28** with caps 320/360. At 976 it reproduces today's 240/264; at 1920 it uses the new caps. If the lab prefers stepwise widths, set weights equal to the caps.
8. **`FractionalLayoutPanel` change** touches the shared canvas panel; the new rule only applies when exactly one axis is unbounded, which no current caller relies on (it returned 0), and all 6 existing panel tests stay.
9. **ON SCREEN NOW aspect (F1)** may not be reproducible in the rig; Task 9 may close as "no defect".
10. **Occlusion.** No new floating UI. The Replace/Move strip overlaying its own block is existing, user-triggered behaviour.
11. **Real screen-reader and touch checks** remain manual (Task 13); STA tests only see automation properties and pixels.
12. **`CornerClear` stays `IsTabStop=False`** (existing test): keyboard users clear with Delete or `DiscardButton`.
13. **Deferred, not part of v2:** NO SIGNAL chip, drag auto-scroll, radio-group semantics for layout tiles, replacing the `Popup` with `ui:Flyout`.
