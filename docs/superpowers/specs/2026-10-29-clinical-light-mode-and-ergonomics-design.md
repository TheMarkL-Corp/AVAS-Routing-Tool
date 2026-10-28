# Clinical Light Mode Remediation & OR Ergonomics Overhaul

Status: Approved by user on 2026-10-29 (following two-pass `/impeccable critique` and interactive design walkthrough).
Implementation Plan: `docs/superpowers/plans/2026-10-29-clinical-light-mode-and-ergonomics.md`
Critique Evidence: `.impeccable/critique/2026-10-28-visual-routing-view.md`

---

## 1. Context & Problem Statement

The AVAS Routing Tool controls Advantech AVAS-223 SDVoE encoders (TX) and decoders (RX) in surgical operating rooms and endoscopy suites. An initial Clinical Light Mode and auto-hiding floating action dock were added previously, but visual inspection across 10 live screenshots (`ui_screenshots/*.PNG`) and static XAML audit revealed:

1. **17 Light Mode Contrast & Token Inversion Flaws**:
   - Text inside dark translucent video scrims (`NumberText`, `SourcePillText`, `NoPreviewName`, `LiveBlocks` labels) inherited Light Mode's dark `Brush.Text.Primary` (`#0F172A`), dropping contrast to `1.12:1–1.25:1` (virtually invisible black-on-black).
   - Buttons with saturated accent fills (`Style.Button.Primary` on `#2563EB`, completed `StepDot` checkmarks `✓` on `#2563EB`, `Stop` / `Done placing` / `Copy layout` armed buttons, and `DUAL LINK` / `ROUTED` status pills) bound their foregrounds to dark ink tokens (`#0F172A` or `#083344`), failing WCAG 2.1 AA (`1.96:1–4.05:1`).
   - Hardcoded dark alpha overlays (`#D90A0E16`, `#33EF4444`, `#1A10B981`, `#990B0F17`) polluted Light Mode cards.
   - `ThemeResourceUsageTests.cs` exempted `SourceCardView.xaml`, `DisplayCardView.xaml`, and `MultiViewComposerView.xaml` from raw hex checks and ignored 8-digit `#AARRGGBB` literals completely.
2. **Surgical Video Occlusion & Touch Hazards on `DisplayCardView`**:
   - `FloatingActionDock` (`Layout ▾`, `Copy layout`, `DraftChip`, `TurnOffButton`) and `UndoToast` sat directly inside `PreviewSurface`, covering 28%–35% of the top of the 16:9 surgical preview (and Quadrant 1/2 labels in MultiView).
   - `Copy layout` truncated to `"Copy layou"` when `TurnOffButton` appeared (`MaxWidth="110"`), and `TurnOffButton` rendered a broken tofu box (`⏻` `U+23FB` missing in Segoe UI).
   - `LayoutMenuPopup` opened downward at the bottom of the screen, clipping row 2 of the preset tiles.
3. **Mid-Action Layout Shift & Vertical Cramping in `VisualRoutingView`**:
   - Arming a source or copy-layout collapsed `SourceCardView` (`~291px`) into `CompactSourcePalette` (`96px`), causing all `TARGET DISPLAYS (RX)` to jump upward by `~195px` mid-reach.
   - The bottom `LayoutsBarView` (`110px`) duplicated the per-card `Layout ▾` menu and stole vertical space, forcing scrollbars and clipping card headers when `UndoToast` scrolled into view.
4. **Telemetry Clutter & Sub-Touch Controls on `SourceCardView`**:
   - Raw IT strings (`IP · MAC`, `MCAST: 224.x.x.x:5000`) competed with clinical status on the primary card face.
   - Footer stream buttons (`Refresh`, `Stop`, `Start`) were `22px–24px` tall with `11px` text and `IsTabStop="False"`, violating gloved OR touch and keyboard accessibility standards.

---

## 2. User-Approved Design Decisions (D1–D8)

| ID | Area | Decision |
|---|---|---|
| **D1** | **Light Mode Scope** | Fix all 17 Light Mode contrast, scrim-inversion, badge, and button-foreground flaws across `Theme.Light.xaml`, `Theme.Dark.xaml`, `Theme.xaml`, `MultiViewComposerView.xaml`, `DisplayCardView.xaml`, `SourceCardView.xaml`, and `CompactSourceCardView.xaml`. |
| **D2** | **Action Pill & Armed Cyan Direction** | Deepen `Color.Border.DropTarget` in `Theme.Light.xaml` to `#0E7490` (Cyan 700) and set `Color.Text.OnDropTarget` to `#FFFFFF` (`5.36:1` contrast) so `Stop`, `Done placing`, `COPYING FROM HERE`, and armed `Copy layout` all use crisp white text on deep clinical teal. |
| **D3** | **Overview Row Height Stability** | Keep full `SourceCardView` cards visible in `VisualRoutingView.xaml` when a source or copy is armed (do not swap to `CompactSourcePalette` in the overview; reserve `CompactSourceCardView` exclusively for `MultiViewComposerView`). |
| **D4** | **Zero Video Occlusion on `DisplayCardView`** | Move `Layout ▾`, `Copy layout`, `DraftChip`, `TurnOffButton`, and `UndoToast` out of `PreviewSurface` into dedicated non-occluding card chrome (Header / Action Bar / Footer rows) so **0% of the 16:9 surgical video or multiview quadrants is ever covered**. Replace `⏻` with vector `Icon.Power`, remove `MaxWidth="110"` truncation on `Copy layout`, and flip `LayoutMenuPopup` upward when near the bottom edge. |
| **D5** | **Retire Bottom `LayoutsBarView`** | Remove `LayoutsBarView` from `VisualRoutingView.xaml` to reclaim `110px` of vertical height for TX and RX cards. Keep `LayoutsBarView.xaml` in the project only if needed by isolated legacy unit tests, or update `ViewSmokeTests` accordingly. |
| **D6** | **Clinical Telemetry & Touch-Safe `SourceCardView`** | Move raw `IP · MAC` and `MCAST` strings off the primary card face into an info badge/tooltip (`ⓘ`) on `SourceCardView` and `DisplayCardView` (keeping clinical status `ONLINE`, `10G/1G`, `OUT 3840x2160@60`, `SOURCE: ...` prominent at `12px–13px`), and upgrade `SourceCardView` stream buttons to `MinHeight="36"`, `FontSize="12"`, `IsTabStop="True"`. |
| **D7** | **Composer `ON SCREEN NOW` & `PickerPopup`** | Keep `ON SCREEN NOW` (`LiveBlocks`) as a dark monitor schematic in both themes (`Brush.Surface.PreviewTile` `#0B0F17` with `Brush.Text.OnScrim` `#F1F5F9` labels), use neutral `Brush.Border.Control` on resting `PickerPopup` tiles (highlighting with `Brush.Border.DropTarget` on hover/focus), and keep `Side-by-Side` block geometry 100% hardware-accurate. |
| **D8** | **App Bar Theme Toggle Label** | `MainViewModel.ThemeButtonLabel` and `MainWindow.xaml` report the **currently active theme** (`"☀️  Light"` in Light Mode, `"🌙  Dark"` in Dark Mode) with an explicit tooltip (`"Current theme: Clinical Light. Click to switch to Slate Dark"` / vice versa). |

---

## 3. Section 1: Palette, Token & Contrast Architecture

### 3.1 New & Updated Semantic Tokens in `Theme.Dark.xaml` and `Theme.Light.xaml`

Every `Color.*` key must have a matching `SolidColorBrush` with `po:Freeze="True"` in both dictionaries:

| Token Key | `Theme.Dark.xaml` | `Theme.Light.xaml` | Purpose & WCAG 2.1 Contrast |
|---|---|---|---|
| `Color.Border.DropTarget` | `#06B6D4` (unchanged) | `#0E7490` (was `#0891B2`) | Deep clinical teal fill/border for armed `Stop`, `Done placing`, `COPYING FROM HERE`, `Copy layout` (`5.36:1` vs `#FFFFFF`). |
| `Color.Text.OnDropTarget` | `#04232B` (unchanged) | `#FFFFFF` (was `#083344`) | Crisp white text on `Brush.Border.DropTarget` in Light Mode (`5.36:1`). |
| `Color.Status.Online` | `#10B981` (unchanged) | `#047857` (was `#059669`) | Emerald 700 in Light Mode so `ONLINE` text/dot on `#ECFDF5` or `#FFFFFF` achieves `>= 5.0:1` (`5.48:1` on white, `5.12:1` on `#ECFDF5`). |
| `Color.Surface.OnlineBadge` | `#1A10B981` | `#ECFDF5` | Background for `ONLINE` badge on `SourceCardView` / `DisplayCardView`. |
| `Color.Border.OnlineBadge` | `#4010B981` | `#6EE7B7` | Border for `ONLINE` badge. |
| `Color.Surface.RoutedBadge` | `#263B82F6` | `#DBEAFE` | Background for `ROUTED` / `DUAL LINK` / active mode pill. |
| `Color.Border.RoutedBadge` | `#593B82F6` | `#93C5FD` | Border for `ROUTED` / `DUAL LINK` / active mode pill. |
| `Color.Text.OnRoutedBadge` | `#93C5FD` | `#1E40AF` | Text on `Brush.Surface.RoutedBadge` (`8.20:1` in Light Mode, `6.8:1` in Dark Mode). |
| `Color.Surface.OfflineOverlay` | `#990B0F17` | `#B3F1F5F9` | Translucent card-dimming veil when `IsActive == false` (dark veil in Dark Mode, frosted slate veil in Light Mode). |
| `Color.Surface.ModalScrim` | `#D90A0E16` | `#EBFFFFFF` | Card overlay surface for `DropChoicePanel`, `CopyTargetAllowed`, and `CopyTargetBlocked` (`Brush.Text.OnOverlay` `#0F172A` on `#EBFFFFFF` = `15.8:1` in Light Mode). |
| `Color.Surface.Scrim` | `#D9070A0F` | `#D9070A0F` | Video-overlay chip background (`Chip` style in Composer, `PreviewSurface` overlays). Intentionally dark in both themes. |
| `Color.Border.Scrim` | `#33FFFFFF` | `#33FFFFFF` | Subtle white border on dark video-overlay chips. |
| `Color.Surface.ScrimAccent` | `#D904232B` | `#D904232B` | Dark cyan scrim pill over video (`PlacePill`, `MovePill` in Composer). |
| `Color.Surface.ScrimTint` | `#2906B6D4` | `#2906B6D4` | Translucent cyan wash inside video drop rings (`DropRing`). |
| `Color.Surface.ScrimDanger` | `#F21F0A0A` | `#F21F0A0A` | Dark red scrim over video (`IssueBand`, `CornerClearGlyph`). |
| `Color.Surface.FloatingStrip` | `#F20F1626` | `#F2FFFFFF` | Background for the floating `Replace` / `Move` strip (`Strip`) in `MultiViewComposerView`. |
| `Color.Text.OnPreviewMuted` | `#94A3B8` (was `#475569`) | `#94A3B8` (was `#475569`) | Secondary text on `#050505` / `#070A0F` video canvas (`No video routed`, `Unassigned`). Raising from `#475569` (`2.55:1`) to `#94A3B8` yields `6.75:1` in both themes. |

### 3.2 Style & Scrim Foreground Fixes

1. **`Theme.xaml`**:
   - `Style.Button.Primary` (line 20): Change `Foreground` from `{DynamicResource Brush.Text.Primary}` to `{DynamicResource Brush.Text.OnAction}` (`#FFFFFF` on `#2563EB` = `5.17:1`).
   - `Style.Button.Unroute` (lines 77–105): Replace hardcoded `#33EF4444` and `#66EF4444` with `{DynamicResource Brush.Surface.Danger}`, `{DynamicResource Brush.Border.Danger}`, and hover `{DynamicResource Brush.Surface.DangerStrong}` / `{DynamicResource Brush.Text.OnDangerHover}`.
2. **`MultiViewComposerView.xaml`**:
   - `StepDot` TextBlocks (Steps 1, 2, 3 at lines 162, 271, 338): Default `Foreground="{DynamicResource Brush.Text.Primary}"`; when `StepLayoutDone` / `StepPlaceDone` / `CanTake` is `True`, add `<Setter Property="Foreground" Value="{DynamicResource Brush.Text.OnAction}" />` so `✓` is crisp white on `#2563EB`.
   - `NoPreviewName` (line 554), `NumberText` (line 744), `SourcePillText` (line 766): Explicitly set `Foreground="{DynamicResource Brush.Text.OnScrim}"` (`#F1F5F9`).
   - `ON SCREEN NOW` (`LiveBlocks` ItemTemplate, lines 937–953): Set `Border.Background="{DynamicResource Brush.Surface.PreviewTile}"`, `SourceName` `Foreground="{DynamicResource Brush.Text.OnPreview}"`, and `Label` (`1..4`) `Foreground="{DynamicResource Brush.Text.OnScrim}"`.
   - `BannerHost` & `BannerText` (lines 1043–1077): Explicitly bind `BannerText.Foreground`:
     - Default (`Info`): `{DynamicResource Brush.Text.Primary}`
     - `ArmedSource != null` (`Info` on `Brush.Surface.CopyTarget`): `{DynamicResource Brush.Text.OnCopyTarget}`
     - `Success`: `{DynamicResource Brush.Text.UndoToast}`
     - `Warning`: `{DynamicResource Brush.Status.StagedChipText}`
     - `Error`: `{DynamicResource Brush.Text.Danger}`
   - `PickerPopup` (`PickerTile` at line 1216): Change resting `BorderBrush` from `{DynamicResource Brush.Border.DropTarget}` to `{DynamicResource Brush.Border.Control}`, and add `<Setter TargetName="bd" Property="BorderBrush" Value="{DynamicResource Brush.Border.DropTarget}" />` on `IsMouseOver="True"`.
   - Replace all remaining inline `#...` hex strings in `MultiViewComposerView.xaml` (`#D9070A0F`, `#33FFFFFF`, `#2906B6D4`, `#D904232B`, `#C7070A0F`, `#F21F0A0A`, `#EB3B0D0D`, `#F20F1626`) with the semantic `Brush.Surface.Scrim*`, `Brush.Border.Scrim`, and `Brush.Surface.FloatingStrip` tokens above.
3. **`CompactSourceCardView.xaml`**:
   - Line 121: Replace `Background="#990B0F17"` on the offline overlay with `Background="{DynamicResource Brush.Surface.OfflineOverlay}"`.
4. **Automated Guardrails (`ThemeResourceUsageTests.cs` & `ThemeTests.cs`)**:
   - Remove the exemptions for `SourceCardView.xaml`, `DisplayCardView.xaml`, and `MultiViewComposerView.xaml` in `ThemeResourceUsageTests.cs`.
   - Upgrade `HexColorLiteral` regex to `@"#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?\b"` so both `#RRGGBB` and `#AARRGGBB` literals are banned across **all** `src/AvasRoutingTool/**/*.xaml` files outside `Resources/Themes/`.

---

## 4. Section 2: Zero-Occlusion `DisplayCardView` & `SourceCardView` Ergonomics

### 4.1 `DisplayCardView.xaml` 4-Row Non-Occluding Architecture

To guarantee **0% video occlusion** while preserving the exact `16:9` aspect ratio of `PreviewSurface` across all card states (verified by `ViewSmokeTests.DisplayCard_InTheOverview_AtRealWindowSizes_NothingInTheCardClips`), `DisplayCardView.xaml` uses a **fixed-chrome-height 4-row layout**:

```
+-----------------------------------------------------------------------+
| Row 0 (Height="28", Margin="0,0,0,6"): Identity & Mode Header         |
|   [●] RX1_c059e_SFPA (14px Bold)    [ⓘ]    [ MATRIX 2X2 · 3/4 ]       |
+-----------------------------------------------------------------------+
| Row 1 (Height="56", Margin="0,0,0,8"): Dedicated Touch Action Bar     |
|   [ Layout ▾ ]  [ Copy layout ]  [ DRAFT · Resume ]        [ ⏻ Off ]  |
+-----------------------------------------------------------------------+
| Row 2 (Height="*"): 16:9 Unobstructed PreviewSurface                  |
|   +---------------------------------------------------------------+   |
|   |                                                               |   |
|   |               16:9 Live Video / MultiView Slots               |   |
|   |         (0% covered by buttons, chips, or UndoToast)          |   |
|   |                                                               |   |
|   +---------------------------------------------------------------+   |
+-----------------------------------------------------------------------+
| Row 3 (Height="56", Margin="0,8,0,0"): Clinical Status & Toast Dock   |
|   Normal:  SOURCE: TX1_SFPA                 OUT 3840x2160@60          |
|            (or StaleDraftNotice / CopyFromBlockedText / ErrorBanner)  |
|   Overlay: [ PAX355S now shows RX1's Matrix 2x2            [ Undo ] ] |
+-----------------------------------------------------------------------+
```

#### Critical `GridSpec.DisplayCard.ChromeHeight` Math:
- `CardFrame` has `Padding="12"` (`24px` total vertical padding) and `BorderThickness="2"` (`4px` total vertical border, or accounted inside `ChromeHeight`).
- Let's check how `DisplayCardView` previously achieved `ChromeHeight = 87`:
  - Previously: `CardFrame` had `Padding="12"` (`24px`), Row 0 Header (`~22px` + `Margin="0,0,0,8"` = `30px`), Row 1 `*` (`PreviewSurface`), Row 2 Footer (`~25px` + `Margin="0,8,0,0"` = `33px`). Total non-video height = `24 + 30 + 33 = 87px`.
  - With the new 4-row layout:
    - `CardFrame` `Padding="12"` (`24px` vertical)
    - **Row 0 (Header)**: `Height="26"`, `Margin="0,0,0,6"` (`32px` total)
    - **Row 1 (Action Bar)**: `Height="56"`, `Margin="0,0,0,8"` (`64px` total) — houses `LayoutButton` (`MinHeight="56"`), `CopyLayoutHandle` (`MinHeight="56"`), `DraftChip` (`MinHeight="56"`), and `TurnOffButton` (`Width="56" Height="56"`).
    - **Row 2 (`PreviewSurface`)**: `Height="*"` — pure `16:9` video canvas!
    - **Row 3 (Footer / Status & Toast Host)**: `MinHeight="56"`, `Margin="0,8,0,0"` (`64px` total) — houses the 2-line clinical status strip (`SOURCE: ...`, `OUT ...`, `DraftNoticeLine`, `CopyFromBlockedText`, `ErrorBanner`) and, when `CanUndoScreen == true`, `UndoToast` (`MinHeight="56"`) overlays Row 3 cleanly without shifting Row 2 or touching `PreviewSurface`!
    - Total fixed vertical chrome = `24 + 32 + 64 + 64 = 184px` (plus `BorderThickness="2"` -> `GridSpec.DisplayCard.ChromeHeight = 184`).
    - Because Rows 0, 1, and 3 have fixed heights (`26`, `56`, `56`), `PreviewSurface` (Row 2 `*`) has **100% constant height** regardless of whether `HasDraft`, `DraftNotice`, `CanUndoScreen`, `IsCopySource`, or `HasError` is active! Thus `PreviewSurface.ActualWidth / PreviewSurface.ActualHeight` stays within `16:9 ± 4%` in every state and window size!

#### Specific `DisplayCardView` Fixes:
1. **Always-Visible Action Bar (`FloatingActionDock`)**:
   - Move `FloatingActionDock` from `PreviewSurface` into `Grid.Row="1"`.
   - Remove the auto-hide opacity trigger (`Opacity="0"`, `IsHitTestVisible="False"`): buttons in Row 1 are always visible (`Opacity="1"`, `IsHitTestVisible="True"`). Keep `IsTouchRevealed` property/methods on `DisplayCardView.xaml.cs` as no-ops or simple flags if external callers reference them, and update `DisplayCard_ActionDock_*` test in `ViewSmokeTests.cs`.
2. **Fix `"Copy layou"` Truncation**:
   - Remove `MaxWidth="110"` and `TextTrimming="CharacterEllipsis"` from `CopyLayoutHandle`. Use compact `Padding="10,6"` and `FontSize="13"` so `"Copy layout"` fits completely alongside `Layout ▾` and `TurnOffButton` even at minimum card width (`340px`).
3. **Replace Broken `⏻` Tofu Glyph with Vector `Icon.Power`**:
   - Add `<StreamGeometry x:Key="Icon.Power">M 12,3 L 12,11 M 6.34,6.34 A 8,8 0 1 0 17.66,6.34</StreamGeometry>` to `src/AvasRoutingTool/Resources/Icons.xaml`.
   - Render a crisp `18x18` `<Path Data="{StaticResource Icon.Power}" Stroke="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}" StrokeThickness="2" StrokeStartLineCap="Round" StrokeEndLineCap="Round" Stretch="Uniform" />` inside `TurnOffButton`.
4. **Upward-Flipping `LayoutMenuPopup`**:
   - In `DisplayCardView.xaml.cs`, `LayoutMenuPopup.CustomPopupPlacementCallback` already provides two placements: below `(target.Width - popup.Width, target.Height + 6)` and above `(target.Width - popup.Width, -popup.Height - 6)`. Ensure `LayoutMenuFrame` sets a `MaxHeight="420"` (or wraps in a `ScrollViewer` when screen height is tight) and prefers the upward placement when the button's screen Y + `popup.Height` exceeds `SystemParameters.WorkArea.Bottom`.
5. **Info `ⓘ` Badge for Raw Network Telemetry**:
   - In Row 0 Header, place a subtle `ⓘ` info badge (`x:Name="DeviceInfoBadge"`) whose `ToolTip` displays `"IP: {IpAddress} · MAC: {MacAddress}"`, keeping the card face focused on `DeviceName`, `ModeBadge`, `SourceLabel`, and `CanvasInfo`.

### 4.2 `SourceCardView.xaml` Clinical Streamlining & Touch-Safe Controls

1. **Header & Badge Contrast**:
   - Bind the `ONLINE` / `OFFLINE` status pill to `Brush.Surface.OnlineBadge`, `Brush.Border.OnlineBadge`, and `Brush.Status.Online` (`#047857` on `#ECFDF5` in Light Mode = `5.12:1`).
   - Bind the `DUAL LINK` badge to `Brush.Surface.RoutedBadge`, `Brush.Border.RoutedBadge`, and `Brush.Text.OnRoutedBadge` (`#1E40AF` on `#DBEAFE` in Light Mode = `8.20:1`).
   - Add an `ⓘ` info badge (`x:Name="SourceInfoBadge"`) in the header with `ToolTip` showing `IP: {IpAddress} · MAC: {MacAddress} · MCAST: {PreviewMulticastIp}`.
2. **Touch-Safe Footer Stream Controls**:
   - Replace the cramped `22px` footer with a single clean control row (`MinHeight="36"`):
     - Left: Live FPS pill (`{Binding Fps} FPS` when `IsStreaming`, or `PREVIEW PAUSED` / `EXTERNAL SOURCE`).
     - Right: Touch-safe `Refresh` (`x:Name="RefreshStreamButton"`, `MinHeight="36"`, `Padding="10,6"`, `FontSize="12"`, `IsTabStop="True"`) and a single contextual `Start` / `Stop` button (`MinHeight="36"`, `Padding="10,6"`, `FontSize="12"`, `IsTabStop="True"`, `Margin="8,0,0,0"`).
   - Recalibrate `GridSpec.SourceCard.ChromeHeight` if needed so `SourceCardView`'s `16:9` preview surface remains distortion-free.

---

## 5. Section 3: Overview Layout Stability & App Bar Clarity

### 5.1 `VisualRoutingView.xaml` Layout Stability (No Mid-Reach Shift, No Bottom `LayoutsBarView`)

1. **Keep Full `SourceCardView` Cards Visible When Armed**:
   - In `VisualRoutingView.xaml`, remove the `DataTrigger` on `SourcesScrollViewer` that collapsed it when `ArmedSource != null` or `IsCopyOrChoiceActive == true`, and remove `CompactSourcePalette` from `VisualRoutingView.xaml` (remember: `CompactSourcePalette` inside `VisualRoutingView.xaml` was shared with Composer when `ActiveComposer != null`, so keep `CompactSourcePalette` **only** visible when `ActiveComposer != null`, and keep `SourcesScrollViewer` visible whenever `ActiveComposer == null`!).
   - Specifically:
     - When `ActiveComposer == null` (Overview mode): `SourcesScrollViewer` is **always `Visible`**, and `CompactSourcePalette` is **always `Collapsed`**, even when `ArmedSource != null` or `IsCopyOrChoiceActive == true`.
     - When `ActiveComposer != null` (Composer mode): `SourcesScrollViewer` is `Collapsed`, and `CompactSourcePalette` is `Visible` so the Composer keeps its compact `96px` top source strip!
2. **Remove Bottom `LayoutsBarView`**:
   - Remove `<views:LayoutsBarView Grid.Row="3" />` from `VisualRoutingView.xaml` so `VisualRoutingView` dedicates 100% of its vertical space to the TX and RX sections.
3. **Header-Safe `RevealState()` in `DisplayCardView.xaml.cs`**:
   - Because `UndoToast` now lives inside Row 3 (`Footer`) of `DisplayCardView` and `LayoutsBarView` is gone, include `CardFrame` (or include Row 0 Header in the `Rect.Union`) when calling `BringIntoView(show)` in `DisplayCardView.RevealState()` whenever the card's total height fits within the `ScrollViewer` viewport, so scrolling a card into view never clips its top header (`RX1_c059e_SFPA`) off the top edge of `DisplaysScrollViewer`.

### 5.2 Top App Bar Theme State Indicator (`MainViewModel.cs` & `MainWindow.xaml`)

1. **`MainViewModel.ThemeButtonLabel`**:
   - Update `MainViewModel.cs` line 237:
     ```csharp
     public string ThemeButtonLabel => _isLightMode ? "☀️  Light" : "🌙  Dark";
     ```
   - Add `ThemeButtonToolTip`:
     ```csharp
     public string ThemeButtonToolTip => _isLightMode
         ? "Current theme: Clinical Light. Click to switch to Slate Dark."
         : "Current theme: Slate Dark. Click to switch to Clinical Light.";
     ```
   - Raise `OnPropertyChanged(nameof(ThemeButtonToolTip))` alongside `OnPropertyChanged(nameof(ThemeButtonLabel))` in `ToggleTheme()`.
2. **`MainWindow.xaml`**:
   - Bind the theme toggle button's `ToolTip="{Binding ThemeButtonToolTip}"`.
3. **Update `MainWindowHeaderTests.cs` / `ViewSmokeTests.cs` / `ThemeTests.cs`**:
   - Update any test asserting the old inverted label (`"🌙  Dark"` in Light Mode) to assert the active state label (`"☀️  Light"` in Light Mode, `"🌙  Dark"` in Dark Mode).
