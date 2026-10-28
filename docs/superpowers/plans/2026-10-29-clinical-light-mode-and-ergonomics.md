# Clinical Light Mode Remediation & OR Ergonomics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate all 17 Clinical Light Mode contrast/token-inversion flaws, remove all UI overlays from the 16:9 surgical video preview (`DisplayCardView`), stabilize `VisualRoutingView` card heights during armed routing/copying, declutter raw network strings into an `ⓘ` tooltip, and upgrade `SourceCardView` stream buttons to touch-safe (`>= 36px`) keyboard-accessible controls.

**Architecture:**
1. **Theme & Token Layer (`Theme.Light.xaml`, `Theme.Dark.xaml`, `Theme.xaml`, `Icons.xaml`)**: Add explicit semantic surface/border/foreground tokens for badges, modal scrims, video scrims, and deep clinical teal (`#0E7490` / `#FFFFFF`) drop-target actions; enforce zero raw 6- or 8-digit hex literals outside `Resources/Themes/` via `ThemeResourceUsageTests`.
2. **Composer Scrim & Schematic Layer (`MultiViewComposerView.xaml`)**: Bind all dark-scrim overlays (`NumberText`, `SourcePillText`, `NoPreviewName`, `LiveBlocks`) to `Brush.Text.OnScrim` / `Brush.Surface.PreviewTile`, bind completed `StepDot` checkmarks to `Brush.Text.OnAction`, bind `BannerText` foreground per `Banner.Kind`, and soften resting `PickerPopup` tile borders to `Brush.Border.Control`.
3. **App Bar & Card Chrome Layer (`MainViewModel.cs`, `MainWindow.xaml`, `SourceCardView.xaml`, `DisplayCardView.xaml`, `GridSpec.cs`, `VisualRoutingView.xaml`)**: Fix `ThemeButtonLabel` to report active state, move `FloatingActionDock` and `UndoToast` out of `PreviewSurface` into fixed-height header/footer rows on `DisplayCardView` (`ChromeHeight = 160`, `SourceCard.ChromeHeight = 96`), keep `SourceCardView` full-height when armed in the overview, and remove the redundant bottom `LayoutsBarView` from `VisualRoutingView`.

**Tech Stack:** C# .NET 8 WPF, XAML (`DynamicResource`, frozen `SolidColorBrush`), xUnit (`[Collection("WpfSta")]` STA visual-tree and contrast tests).

**Spec:** `docs/superpowers/specs/2026-10-29-clinical-light-mode-and-ergonomics-design.md` (decisions D1–D8).

## Global Constraints

- **Zero Raw Hex Outside `Resources/Themes/`**: Every XAML file under `src/AvasRoutingTool/` outside `src/AvasRoutingTool/Resources/Themes/` must contain **zero** `#RRGGBB` or `#AARRGGBB` literals. Every new `Color.*` key in `Theme.Dark.xaml` and `Theme.Light.xaml` must have a matching `<SolidColorBrush po:Freeze="True" x:Key="Brush.*" Color="{StaticResource Color.*}" />`.
- **Thread-Safe STA Testing**: Never add `Dispatcher.Invoke` inside `ThemeService` (causes STA test deadlocks). Always keep `po:Freeze="True"` on every `SolidColorBrush` in theme dictionaries.
- **Exact 16:9 `PreviewSurface` Invariant**: `AdaptiveGridLayout.Compute` calculates card height as `(CardWidth - HorizontalPadding) / (16/9) + ChromeHeight`. Therefore, inside `DisplayCardView.xaml` and `SourceCardView.xaml`, every row outside `PreviewSurface` (`Height="*"`) **must have a fixed pixel height** that never changes when `HasDraft`, `DraftNotice`, `CanUndoScreen`, `IsCopySource`, or `HasError` toggles, and `GridSpec.DisplayCard.ChromeHeight` / `GridSpec.SourceCard.ChromeHeight` must equal the exact sum of non-video row heights + margins + card padding + border thickness.
- **CLAUDE.md / AGENTS.md Lifecycle**: Read `memory/active_context.md` and `memory/system_patterns.md` before starting; follow TDD (red test before green implementation); run the non-live test command after every task; commit code and `memory/` updates together in atomic commits.
- **Test Command**:
  ```powershell
  dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj --filter "FullyQualifiedName!~LiveControlServerTests&FullyQualifiedName!~LiveFunctionTests"
  ```
  (Note: if `bin/` is locked by a running instance, build/test with `-o` or stop the running `AvasRoutingTool` process first.)

## Review Focus

1. **WCAG 2.1 AA Contrast in Both Themes**: Every text/background pair in `Theme.Light.xaml` and `Theme.Dark.xaml` meets `>= 4.5:1` (`>= 3.0:1` for large text / borders), including `Color.Text.OnDropTarget` on `Color.Border.DropTarget` (`#FFFFFF` on `#0E7490` = `5.36:1`), `Color.Status.Online` on `Color.Surface.OnlineBadge` (`#047857` on `#ECFDF5` = `5.12:1`), `Color.Text.OnRoutedBadge` on `Color.Surface.RoutedBadge` (`#1E40AF` on `#DBEAFE` = `8.20:1`), and `Color.Text.OnPreviewMuted` on `Color.Surface.PreviewBackdrop` (`#94A3B8` on `#070A0F` = `6.75:1`).
2. **Zero Video Occlusion on `DisplayCardView`**: `LayoutButton`, `CopyLayoutHandle`, `DraftChip`, `TurnOffButton`, `CopySourceChip`, `DraftNoticeLine`, and `UndoToast` never intersect `PreviewSurface` (`BoundsIn(part, card).IntersectsWith(BoundsIn(preview, card)) == false`), while `ErrorBanner`, `DropChoicePanel`, `CopyTargetAllowedContent`, and `CopyTargetBlockedContent` remain intentional modal state overlays inside `PreviewSurface`.
3. **16:9 Aspect Ratio Preservation**: `DisplayCard_InTheOverview_AtRealWindowSizes_NothingInTheCardClips` passes at `1008x615`, `1264x755`, and `1920x1014` with `PreviewSurface` within `16:9 ± 4%` across all states.
4. **Overview Layout Stability**: Arming a source (`vm.ArmSource`) or arming copy-layout (`vm.ArmDisplayAsync`) in `VisualRoutingView` keeps `SourceCardView` visible (`CompactSourcePalette` stays `Collapsed` until `ActiveComposer != null`), and `LayoutsBarView` is no longer present in `VisualRoutingView`.

---

### Task 1: Palette Overhaul, Vector Power Icon & Automated Hex/Contrast Guardrails

**Files:**
- Modify: `src/AvasRoutingTool/Resources/Themes/Theme.Dark.xaml`
- Modify: `src/AvasRoutingTool/Resources/Themes/Theme.Light.xaml`
- Modify: `src/AvasRoutingTool/Resources/Theme.xaml` (lines 18–26, 77–105)
- Modify: `src/AvasRoutingTool/Resources/Icons.xaml`
- Modify: `src/AvasRoutingTool/Views/CompactSourceCardView.xaml` (line 121)
- Test: `tests/AvasRoutingTool.Tests/ThemeTests.cs`
- Test: `tests/AvasRoutingTool.Tests/ThemeResourceUsageTests.cs`

**Interfaces:**
- Produces:
  - New `Color.*` and frozen `Brush.*` keys in both `Theme.Dark.xaml` and `Theme.Light.xaml`:
    - `Surface.OnlineBadge`, `Border.OnlineBadge`
    - `Surface.RoutedBadge`, `Border.RoutedBadge`, `Text.OnRoutedBadge`
    - `Surface.OfflineOverlay`, `Surface.ModalScrim`
    - `Surface.Scrim`, `Border.Scrim`, `Surface.ScrimAccent`, `Surface.ScrimTint`, `Surface.ScrimDanger`, `Surface.FloatingStrip`
  - Updated `Theme.Light.xaml` values:
    - `Color.Border.DropTarget` = `#0E7490`
    - `Color.Text.OnDropTarget` = `#FFFFFF`
    - `Color.Status.Online` = `#047857`
    - `Color.Text.OnPreviewMuted` = `#94A3B8` (also updated in `Theme.Dark.xaml`)
  - New `StreamGeometry` key `Icon.Power` in `src/AvasRoutingTool/Resources/Icons.xaml`.
  - Updated `Style.Button.Primary` (`Foreground="{DynamicResource Brush.Text.OnAction}"`) and `Style.Button.Unroute` (using `Brush.Surface.Danger`, `Brush.Border.Danger`, `Brush.Surface.DangerStrong`) in `src/AvasRoutingTool/Resources/Theme.xaml`.

- [ ] **Step 1: Write the failing contrast and hex-literal tests in `ThemeTests.cs` and `ThemeResourceUsageTests.cs`.**
  In `tests/AvasRoutingTool.Tests/ThemeTests.cs`, update `ThemePalettes_MeetWcagContrastRequirements` to add the new pairs:
  ```csharp
  ("Color.Text.OnDropTarget", "Color.Border.DropTarget", 4.5),
  ("Color.Status.Online", "Color.Surface.OnlineBadge", 4.5),
  ("Color.Text.OnRoutedBadge", "Color.Surface.RoutedBadge", 4.5),
  ("Color.Text.OnPreviewMuted", "Color.Surface.PreviewBackdrop", 4.5),
  ("Color.Text.OnAction", "Color.Accent.Primary", 4.5),
  ```
  In `tests/AvasRoutingTool.Tests/ThemeResourceUsageTests.cs`:
  - Change `HexColorLiteral` regex at line 20 to catch both 6-digit and 8-digit hex literals:
    ```csharp
    private static readonly Regex HexColorLiteral = new(
        @"#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?\b",
        RegexOptions.Compiled);
    ```
  - Remove `"CompactSourceCardView.xaml"` (and we will remove `"SourceCardView.xaml"`, `"DisplayCardView.xaml"`, `"MultiViewComposerView.xaml"` as each view is cleaned in Tasks 2, 4, 5—or keep only `"SourceCardView.xaml"`, `"DisplayCardView.xaml"`, `"MultiViewComposerView.xaml"` in `HexCheckExemptFiles` during Task 1 and remove them in Tasks 2–5).

- [ ] **Step 2: Run the test suite to verify `ThemeTests` and `ThemeResourceUsageTests` fail.**
  Run:
  ```powershell
  dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj --filter "FullyQualifiedName~ThemeTests|FullyQualifiedName~ThemeResourceUsageTests"
  ```
  Expected: FAIL (`Color.Text.OnDropTarget` vs `Color.Border.DropTarget` in Light Mode is `4.05 < 4.5`; `Color.Text.OnPreviewMuted` vs `Color.Surface.PreviewBackdrop` is `2.55 < 4.5`; `Theme.xaml` and `CompactSourceCardView.xaml` fail 8-digit hex check).

- [ ] **Step 3: Implement the token updates in `Theme.Dark.xaml`, `Theme.Light.xaml`, `Theme.xaml`, `Icons.xaml`, and `CompactSourceCardView.xaml`.**
  1. In `src/AvasRoutingTool/Resources/Themes/Theme.Dark.xaml`, update `Color.Text.OnPreviewMuted` to `#94A3B8` and add the new colors and frozen brushes:
     ```xml
     <Color x:Key="Color.Text.OnPreviewMuted">#94A3B8</Color>
     <Color x:Key="Color.Surface.OnlineBadge">#1A10B981</Color>
     <Color x:Key="Color.Border.OnlineBadge">#4010B981</Color>
     <Color x:Key="Color.Surface.RoutedBadge">#263B82F6</Color>
     <Color x:Key="Color.Border.RoutedBadge">#593B82F6</Color>
     <Color x:Key="Color.Text.OnRoutedBadge">#93C5FD</Color>
     <Color x:Key="Color.Surface.OfflineOverlay">#990B0F17</Color>
     <Color x:Key="Color.Surface.ModalScrim">#D90A0E16</Color>
     <Color x:Key="Color.Surface.Scrim">#D9070A0F</Color>
     <Color x:Key="Color.Border.Scrim">#33FFFFFF</Color>
     <Color x:Key="Color.Surface.ScrimAccent">#D904232B</Color>
     <Color x:Key="Color.Surface.ScrimTint">#2906B6D4</Color>
     <Color x:Key="Color.Surface.ScrimDanger">#F21F0A0A</Color>
     <Color x:Key="Color.Surface.FloatingStrip">#F20F1626</Color>
     ```
     (Plus matching `<SolidColorBrush po:Freeze="True" x:Key="Brush.*" Color="{StaticResource Color.*}" />` entries for each.)
  2. In `src/AvasRoutingTool/Resources/Themes/Theme.Light.xaml`, update:
     ```xml
     <Color x:Key="Color.Border.DropTarget">#0E7490</Color> <!-- Cyan 700 (5.36:1 with white) -->
     <Color x:Key="Color.Status.Online">#047857</Color> <!-- Emerald 700 (5.12:1 on Emerald 50) -->
     <Color x:Key="Color.Text.OnDropTarget">#FFFFFF</Color>
     <Color x:Key="Color.Text.OnPreviewMuted">#94A3B8</Color>
     <Color x:Key="Color.Surface.OnlineBadge">#ECFDF5</Color>
     <Color x:Key="Color.Border.OnlineBadge">#6EE7B7</Color>
     <Color x:Key="Color.Surface.RoutedBadge">#DBEAFE</Color>
     <Color x:Key="Color.Border.RoutedBadge">#93C5FD</Color>
     <Color x:Key="Color.Text.OnRoutedBadge">#1E40AF</Color>
     <Color x:Key="Color.Surface.OfflineOverlay">#B3F1F5F9</Color>
     <Color x:Key="Color.Surface.ModalScrim">#EBFFFFFF</Color>
     <Color x:Key="Color.Surface.Scrim">#D9070A0F</Color>
     <Color x:Key="Color.Border.Scrim">#33FFFFFF</Color>
     <Color x:Key="Color.Surface.ScrimAccent">#D904232B</Color>
     <Color x:Key="Color.Surface.ScrimTint">#2906B6D4</Color>
     <Color x:Key="Color.Surface.ScrimDanger">#F21F0A0A</Color>
     <Color x:Key="Color.Surface.FloatingStrip">#F2FFFFFF</Color>
     ```
     (Plus matching `<SolidColorBrush po:Freeze="True" x:Key="Brush.*" Color="{StaticResource Color.*}" />` entries for each.)
  3. In `src/AvasRoutingTool/Resources/Theme.xaml`:
     - Line 20 (`Style.Button.Primary`): set `<Setter Property="Foreground" Value="{DynamicResource Brush.Text.OnAction}" />`.
     - Lines 78–99 (`Style.Button.Unroute`): replace `#33EF4444` with `{DynamicResource Brush.Surface.Danger}`, `#66EF4444` border with `{DynamicResource Brush.Border.Danger}`, and hover background `#66EF4444` with `{DynamicResource Brush.Surface.DangerStrong}`.
  4. In `src/AvasRoutingTool/Views/CompactSourceCardView.xaml`:
     - Line 121: replace `Background="#990B0F17"` with `Background="{DynamicResource Brush.Surface.OfflineOverlay}"`.
  5. In `src/AvasRoutingTool/Resources/Icons.xaml`, add:
     ```xml
     <!-- IEC 60417-5009 Standby / Power symbol (24x24 viewBox, stroked) -->
     <StreamGeometry x:Key="Icon.Power">M 12,3 L 12,11 M 6.34,6.34 A 8,8 0 1 0 17.66,6.34</StreamGeometry>
     ```

- [ ] **Step 4: Run the tests to verify they pass.**
  Run:
  ```powershell
  dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj --filter "FullyQualifiedName~ThemeTests|FullyQualifiedName~ThemeResourceUsageTests"
  ```
  Expected: PASS.

- [ ] **Step 5: Commit.**
  ```powershell
  git add src/AvasRoutingTool/Resources/ src/AvasRoutingTool/Views/CompactSourceCardView.xaml tests/AvasRoutingTool.Tests/ThemeTests.cs tests/AvasRoutingTool.Tests/ThemeResourceUsageTests.cs
  git commit -m "feat(theme): add clinical badge/scrim tokens, fix drop-target contrast, and ban 8-digit hex literals"
  ```

---

### Task 2: `MultiViewComposerView` Scrim Foreground Isolation, `StepDot` Checkmarks, `ON SCREEN NOW` Dark Schematic & `PickerPopup` Resting Borders

**Files:**
- Modify: `src/AvasRoutingTool/Views/MultiViewComposerView.xaml`
- Modify: `tests/AvasRoutingTool.Tests/ThemeResourceUsageTests.cs`
- Test: `tests/AvasRoutingTool.Tests/ViewSmokeTests.cs`

**Interfaces:**
- Consumes: Task 1 tokens (`Brush.Surface.Scrim`, `Brush.Border.Scrim`, `Brush.Surface.ScrimAccent`, `Brush.Surface.ScrimTint`, `Brush.Surface.ScrimDanger`, `Brush.Surface.FloatingStrip`, `Brush.Surface.PreviewTile`, `Brush.Text.OnScrim`, `Brush.Text.OnAction`).
- Produces: Zero raw hex literals in `MultiViewComposerView.xaml`; >= 4.5:1 contrast on all Composer block chips, `StepDot` checkmarks, `LiveBlocks` labels, and `BannerText` in Clinical Light Mode.

- [ ] **Step 1: Write the failing STA Light Mode contrast test for `MultiViewComposerView` in `ViewSmokeTests.cs` and remove `MultiViewComposerView.xaml` from `HexCheckExemptFiles`.**
  1. In `tests/AvasRoutingTool.Tests/ThemeResourceUsageTests.cs`, remove `"MultiViewComposerView.xaml"` from `HexCheckExemptFiles`.
  2. In `tests/AvasRoutingTool.Tests/ViewSmokeTests.cs`, add `Composer_InLightMode_ScrimLabels_StepDots_LiveBlocks_AndPickerTiles_KeepHighContrast`:
     ```csharp
     [Fact]
     public void Composer_InLightMode_ScrimLabels_StepDots_LiveBlocks_AndPickerTiles_KeepHighContrast() =>
         RunSta(() =>
         {
             var themeService = new AvasRoutingTool.Services.ThemeService();
             try
             {
                 themeService.SetTheme(AvasRoutingTool.Services.ThemeMode.Light);
                 var rig = new ComposerRig();
                 var c = rig.Composer("matrix2x2");
                 c.Arm(rig.Vega); // non-AVAS source (IsPreviewSupported == false)
                 c.TapBlock(0);
                 c.Disarm();
                 Pump(c.TakeAsync); // puts LiveBlocks on screen and marks Step 1 & Step 2 done
                 c.TapBlock(1);     // opens PickerPopup on empty block 2
                 var view = rig.View(c);

                 Showing(view, () =>
                 {
                     var onScrim = ((SolidColorBrush)Application.Current.FindResource("Brush.Text.OnScrim")).Color;
                     var onAction = ((SolidColorBrush)Application.Current.FindResource("Brush.Text.OnAction")).Color;
                     var controlBorder = ((SolidColorBrush)Application.Current.FindResource("Brush.Border.Control")).Color;

                     // 1. Scrim labels over dark video blocks use light Brush.Text.OnScrim, never dark Brush.Text.Primary
                     foreach (var name in new[] { "NumberText", "SourcePillText", "NoPreviewName" })
                     {
                         var tb = FindAll<TextBlock>(view).First(t => t.Name == name && t.IsVisible);
                         Assert.Equal(onScrim, ((SolidColorBrush)tb.Foreground).Color);
                     }

                     // 2. Completed StepDot checkmarks ("✓") use white Brush.Text.OnAction on Cobalt Blue Brush.Action.Take
                     var checks = FindAll<TextBlock>(view).Where(t => t.Text == "✓" && t.IsVisible && t.FontSize == 15).ToList();
                     Assert.NotEmpty(checks);
                     Assert.All(checks, t => Assert.Equal(onAction, ((SolidColorBrush)t.Foreground).Color));

                     // 3. PickerPopup resting tiles use neutral Brush.Border.Control, not cyan DropTarget
                     var popup = (Popup)Named(view, "PickerPopup").Single();
                     Assert.True(popup.IsOpen);
                     var pickerMenu = (FrameworkElement)popup.Child;
                     Relayout(pickerMenu);
                     var tile = Named(pickerMenu, "PickerTile").Cast<Button>().First(b => b.IsEnabled);
                     var bd = (Border)tile.Template.FindName("bd", tile);
                     Assert.Equal(controlBorder, ((SolidColorBrush)bd.BorderBrush).Color);
                 });
             }
             finally
             {
                 themeService.SetTheme(AvasRoutingTool.Services.ThemeMode.Dark);
             }
         });
     ```

- [ ] **Step 2: Run the tests to verify they fail.**
  Run:
  ```powershell
  dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj --filter "FullyQualifiedName~Composer_InLightMode_ScrimLabels|FullyQualifiedName~ThemeResourceUsageTests"
  ```
  Expected: FAIL (`MultiViewComposerView.xaml` has raw `#D9070A0F` etc., `NumberText` foreground is `#0F172A` instead of `#F1F5F9`, `StepDot` `✓` is `#0F172A`, `PickerTile` border is `#0E7490`).

- [ ] **Step 3: Implement all token & foreground updates in `src/AvasRoutingTool/Views/MultiViewComposerView.xaml`.**
  1. `Chip` style (lines 25–32):
     ```xml
     <Style x:Key="Chip" TargetType="Border">
         <Setter Property="Background" Value="{DynamicResource Brush.Surface.Scrim}" />
         <Setter Property="BorderBrush" Value="{DynamicResource Brush.Border.Scrim}" />
         <Setter Property="BorderThickness" Value="1" />
         <Setter Property="CornerRadius" Value="6" />
         <Setter Property="Padding" Value="8,3" />
         <Setter Property="IsHitTestVisible" Value="False" />
     </Style>
     ```
  2. Step 1, Step 2, Step 3 `StepDot` inner `TextBlock` styles (around lines 162, 271, 338):
     Add `<Setter Property="Foreground" Value="{DynamicResource Brush.Text.Primary}" />` as default, and inside the `DataTrigger` (`StepLayoutDone="True"`, `StepPlaceDone="True"`, `CanTake="True"`), add:
     ```xml
     <Setter Property="Foreground" Value="{DynamicResource Brush.Text.OnAction}" />
     ```
  3. Block overlays over dark video (`NoPreviewName` line 554, `NumberText` line 744, `SourcePillText` line 766):
     Add `Foreground="{DynamicResource Brush.Text.OnScrim}"` to each of those three `TextBlock` elements.
  4. Replace all inline scrim hex colors in `MultiViewComposerView.xaml`:
     - Line 640 & Line 658: `Fill="#2906B6D4"` -> `Fill="{DynamicResource Brush.Surface.ScrimTint}"`
     - Line 641 & Line 697 (`PlacePill`, `MovePill`): `Background="#D904232B"` -> `Background="{DynamicResource Brush.Surface.ScrimAccent}"`
     - Line 662: `Background="#C7070A0F"` -> `Background="{DynamicResource Brush.Surface.Scrim}"`
     - Line 726 (`IssueBand`): `Background="#F21F0A0A"` -> `Background="{DynamicResource Brush.Surface.ScrimDanger}"`
     - Line 803 (`CornerClearGlyph`): `Background="#EB3B0D0D"` -> `Background="{DynamicResource Brush.Surface.ScrimDanger}"`
     - Line 846 (`Strip`): `Background="#F20F1626"` -> `Background="{DynamicResource Brush.Surface.FloatingStrip}"`
  5. `ON SCREEN NOW` (`LiveBlocks` `DataTemplate` around lines 937–953):
     - Change `Border` `Background="{DynamicResource Brush.Surface.Inset}"` to `Background="{DynamicResource Brush.Surface.PreviewTile}"`.
     - Change `SourceName` `TextBlock` `Foreground="{DynamicResource Brush.Text.Soft}"` to `Foreground="{DynamicResource Brush.Text.OnPreview}"`.
     - Add `Foreground="{DynamicResource Brush.Text.OnScrim}"` to the `Label` `TextBlock` (`<TextBlock Text="{Binding Label, Mode=OneWay}" FontSize="12" FontWeight="Bold" Foreground="{DynamicResource Brush.Text.OnScrim}" Margin="4,2" HorizontalAlignment="Left" VerticalAlignment="Top" />`).
  6. `BannerText` (around line 1066):
     In `BannerText.Style`, add foreground setters matching `Banner.Kind`:
     ```xml
     <Setter Property="Foreground" Value="{DynamicResource Brush.Text.Primary}" />
     <Setter Property="AutomationProperties.LiveSetting" Value="Polite" />
     <Style.Triggers>
         <DataTrigger Binding="{Binding Banner.Kind, Mode=OneWay}" Value="Success">
             <Setter Property="Foreground" Value="{DynamicResource Brush.Text.UndoToast}" />
         </DataTrigger>
         <DataTrigger Binding="{Binding Banner.Kind, Mode=OneWay}" Value="Warning">
             <Setter Property="Foreground" Value="{DynamicResource Brush.Status.StagedChipText}" />
         </DataTrigger>
         <DataTrigger Binding="{Binding Banner.Kind, Mode=OneWay}" Value="Error">
             <Setter Property="Foreground" Value="{DynamicResource Brush.Text.Danger}" />
             <Setter Property="AutomationProperties.LiveSetting" Value="Assertive" />
         </DataTrigger>
         <MultiDataTrigger>
             <MultiDataTrigger.Conditions>
                 <Condition Binding="{Binding Banner.Kind, Mode=OneWay}" Value="Info" />
                 <Condition Binding="{Binding ArmedSource, Mode=OneWay, Converter={StaticResource IsNull}}" Value="False" />
             </MultiDataTrigger.Conditions>
             <Setter Property="Foreground" Value="{DynamicResource Brush.Text.OnCopyTarget}" />
         </MultiDataTrigger>
     </Style.Triggers>
     ```
  7. `PickerPopup` (`PickerTile` template around lines 1216–1230):
     Change resting `BorderBrush` on `bd` to `{DynamicResource Brush.Border.Control}`, and in the `IsMouseOver="True"` trigger add `<Setter TargetName="bd" Property="BorderBrush" Value="{DynamicResource Brush.Border.DropTarget}" />`.

- [ ] **Step 4: Run the tests to verify they pass.**
  Run:
  ```powershell
  dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj --filter "FullyQualifiedName~Composer_InLightMode_ScrimLabels|FullyQualifiedName~ThemeResourceUsageTests"
  ```
  Expected: PASS.

- [ ] **Step 5: Commit.**
  ```powershell
  git add src/AvasRoutingTool/Views/MultiViewComposerView.xaml tests/AvasRoutingTool.Tests/ThemeResourceUsageTests.cs tests/AvasRoutingTool.Tests/ViewSmokeTests.cs
  git commit -m "fix(composer): isolate dark-scrim text foregrounds and fix Light Mode step/picker/schematic contrast"
  ```

---

### Task 3: Top App Bar Theme State Indicator (`MainViewModel` & `MainWindow`)

**Files:**
- Modify: `src/AvasRoutingTool/ViewModels/MainViewModel.cs` (lines 235–265)
- Modify: `src/AvasRoutingTool/MainWindow.xaml` (lines 201–210)
- Test: `tests/AvasRoutingTool.Tests/ThemeTests.cs` (or `MainWindowHeaderTests.cs` if present on branch)

**Interfaces:**
- Produces:
  - `string MainViewModel.ThemeButtonLabel` -> `"☀️  Light"` when `IsLightMode == true`, `"🌙  Dark"` when `IsLightMode == false`.
  - `string MainViewModel.ThemeButtonToolTip` -> `"Current theme: Clinical Light. Click to switch to Slate Dark."` when `IsLightMode == true`, `"Current theme: Slate Dark. Click to switch to Clinical Light."` when `IsLightMode == false`.

- [ ] **Step 1: Write the failing unit test in `ThemeTests.cs`.**
  In `tests/AvasRoutingTool.Tests/ThemeTests.cs`, add `MainViewModel_ThemeButtonLabel_ReportsActiveThemeState_AndUpdatesOnToggle`:
  ```csharp
  [Fact]
  public void MainViewModel_ThemeButtonLabel_ReportsActiveThemeState_AndUpdatesOnToggle()
  {
      var themeService = new ThemeService();
      try
      {
          themeService.SetTheme(ThemeMode.Dark);
          var vm = new AvasRoutingTool.ViewModels.MainViewModel(
              new ConfigService(),
              new MockSdvoeClient(),
              new MockRtpReceiver(),
              new MulticastIpManager(),
              new RoutingOrchestrator(new MockSdvoeClient()),
              themeService: themeService);

          Assert.False(vm.IsLightMode);
          Assert.Equal("🌙  Dark", vm.ThemeButtonLabel);
          Assert.Contains("Current theme: Slate Dark", vm.ThemeButtonToolTip);

          vm.ToggleThemeCommand.Execute(null);
          Assert.True(vm.IsLightMode);
          Assert.Equal("☀️  Light", vm.ThemeButtonLabel);
          Assert.Contains("Current theme: Clinical Light", vm.ThemeButtonToolTip);
      }
      finally
      {
          themeService.SetTheme(ThemeMode.Dark);
      }
  }
  ```
  (Check if any existing test in `ThemeTests.cs` or `MainWindowHeaderTests.cs` asserts the old inverted `"☀️  Light"` for Dark Mode and update it to match.)

- [ ] **Step 2: Run the test to verify it fails.**
  Run:
  ```powershell
  dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj --filter "FullyQualifiedName~ThemeTests"
  ```
  Expected: FAIL (`ThemeButtonLabel` was `"☀️  Light"` when `IsLightMode == false`, and `ThemeButtonToolTip` does not exist).

- [ ] **Step 3: Implement `ThemeButtonLabel` and `ThemeButtonToolTip` in `MainViewModel.cs` and bind in `MainWindow.xaml`.**
  1. In `src/AvasRoutingTool/ViewModels/MainViewModel.cs` around line 237:
     ```csharp
     public string ThemeButtonLabel => _isLightMode ? "☀️  Light" : "🌙  Dark";

     public string ThemeButtonToolTip => _isLightMode
         ? "Current theme: Clinical Light. Click to switch to Slate Dark."
         : "Current theme: Slate Dark. Click to switch to Clinical Light.";
     ```
     In `ToggleTheme()`, add `OnPropertyChanged(nameof(ThemeButtonToolTip));` right after `OnPropertyChanged(nameof(ThemeButtonLabel));`.
  2. In `src/AvasRoutingTool/MainWindow.xaml` around line 206:
     Change `ToolTip="Toggle between Clinical Light and Slate Dark mode"` to `ToolTip="{Binding ThemeButtonToolTip}"`.

- [ ] **Step 4: Run the tests to verify they pass.**
  Run:
  ```powershell
  dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj --filter "FullyQualifiedName~ThemeTests"
  ```
  Expected: PASS.

- [ ] **Step 5: Commit.**
  ```powershell
  git add src/AvasRoutingTool/ViewModels/MainViewModel.cs src/AvasRoutingTool/MainWindow.xaml tests/AvasRoutingTool.Tests/ThemeTests.cs
  git commit -m "fix(ui): show currently active theme on top app bar toggle button and tooltip"
  ```

---

### Task 4: `SourceCardView` Clinical Streamlining, Badge Contrast, Info `ⓘ` Telemetry Tooltip & Touch-Safe Stream Controls

**Files:**
- Modify: `src/AvasRoutingTool/Views/SourceCardView.xaml`
- Modify: `src/AvasRoutingTool/Controls/GridSpec.cs` (update `SourceCard.ChromeHeight` if footer height changes)
- Modify: `tests/AvasRoutingTool.Tests/ThemeResourceUsageTests.cs` (remove `"SourceCardView.xaml"` from `HexCheckExemptFiles`)
- Test: `tests/AvasRoutingTool.Tests/ViewSmokeTests.cs`

**Interfaces:**
- Consumes: Task 1 tokens (`Brush.Surface.OnlineBadge`, `Brush.Border.OnlineBadge`, `Brush.Surface.RoutedBadge`, `Brush.Border.RoutedBadge`, `Brush.Text.OnRoutedBadge`, `Brush.Surface.OfflineOverlay`, `Brush.Surface.Scrim`).
- Produces:
  - Zero raw hex literals in `SourceCardView.xaml`.
  - `SourceInfoBadge` (`Border` with `ⓘ` and `ToolTip` containing `IpAddress`, `MacAddress`, and `PreviewMulticastIp`).
  - Touch-safe stream control buttons (`RefreshStreamButton`, `StartStreamButton`, `StopStreamButton`) with `MinHeight="36"`, `FontSize="12"`, `IsTabStop="True"`, and `8px` spacing.
  - Recalibrated `GridSpec.SourceCard.ChromeHeight = 96` (`CardFrame` padding `24` + Header Row 0 `28` + `8` margin + Footer Row 2 `36` + `8` margin = `104`, or exact measured non-video height so `SourceCardView` `PreviewSurface` stays `16:9`).

- [ ] **Step 1: Write the failing STA test in `ViewSmokeTests.cs` and remove `SourceCardView.xaml` from `HexCheckExemptFiles`.**
  1. In `tests/AvasRoutingTool.Tests/ThemeResourceUsageTests.cs`, remove `"SourceCardView.xaml"` from `HexCheckExemptFiles`.
  2. In `tests/AvasRoutingTool.Tests/ViewSmokeTests.cs`, add `SourceCard_HasTouchSafeStreamControls_InfoTooltip_AndHighContrastBadgesInLightMode`:
     ```csharp
     [Fact]
     public void SourceCard_HasTouchSafeStreamControls_InfoTooltip_AndHighContrastBadgesInLightMode() =>
         RunSta(() =>
         {
             var themeService = new AvasRoutingTool.Services.ThemeService();
             try
             {
                 themeService.SetTheme(AvasRoutingTool.Services.ThemeMode.Light);
                 var (_, _, sources) = Overview();
                 sources[0].IsStreaming = true;
                 var card = new SourceCardView { DataContext = sources[0], Width = 360, Height = 260 };

                 Showing(card, () =>
                 {
                     var refresh = (Button)Named(card, "RefreshStreamButton").Single();
                     var stop = (Button)Named(card, "StopStreamButton").Single();
                     Assert.True(refresh.IsVisible && refresh.ActualHeight >= 36, $"Refresh is {refresh.ActualHeight} high");
                     Assert.True(stop.IsVisible && stop.ActualHeight >= 36, $"Stop is {stop.ActualHeight} high");
                     Assert.True(refresh.IsTabStop && stop.IsTabStop);

                     var info = (FrameworkElement)Named(card, "SourceInfoBadge").Single();
                     Assert.True(info.IsVisible);
                     string tip = info.ToolTip as string ?? string.Empty;
                     Assert.Contains(sources[0].IpAddress, tip);
                     Assert.Contains(sources[0].MacAddress, tip);
                 });
             }
             finally
             {
                 themeService.SetTheme(AvasRoutingTool.Services.ThemeMode.Dark);
             }
         });
     ```

- [ ] **Step 2: Run the tests to verify they fail.**
  Run:
  ```powershell
  dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj --filter "FullyQualifiedName~SourceCard_HasTouchSafeStreamControls|FullyQualifiedName~ThemeResourceUsageTests"
  ```
  Expected: FAIL (`SourceCardView.xaml` contains raw hex `#1A10B981`, `#263B82F6`, `#A60B0F17`, `#990B0F17`, and lacks `RefreshStreamButton` / `SourceInfoBadge` with `ActualHeight >= 36`).

- [ ] **Step 3: Update `src/AvasRoutingTool/Views/SourceCardView.xaml` and `src/AvasRoutingTool/Controls/GridSpec.cs`.**
  In `src/AvasRoutingTool/Views/SourceCardView.xaml`:
  1. Replace the Row 0 Header (`Margin="0,0,0,8"`) so it shows:
     - Left: Status dot (`Brush.Status.Online` / `Brush.Status.Offline`), `DeviceName` (`14px` Bold), and `SourceInfoBadge` (`Border x:Name="SourceInfoBadge"` with `CornerRadius="10"`, `Padding="6,1"`, `Margin="6,0,0,0"`, `Background="{DynamicResource Brush.Surface.Elevated}"`, `BorderBrush="{DynamicResource Brush.Border.Default}"`, `BorderThickness="1"`, `Cursor="Help"`, `ToolTip` bound via `MultiBinding` or `TextBlock` string format `"IP: {IpAddress} · MAC: {MacAddress} · MCAST: {PreviewMulticastIp}"`, displaying `ⓘ` at `11px` Bold `Brush.Text.Secondary`).
     - Right: `DUAL LINK` badge (`Background="{DynamicResource Brush.Surface.RoutedBadge}"`, `BorderBrush="{DynamicResource Brush.Border.RoutedBadge}"`, `Foreground="{DynamicResource Brush.Text.OnRoutedBadge}"`) and `ONLINE`/`OFFLINE` badge (`Background="{DynamicResource Brush.Surface.OnlineBadge}"`, `BorderBrush="{DynamicResource Brush.Border.OnlineBadge}"`, `Foreground="{DynamicResource Brush.Status.Online}"` when active; `Brush.Surface.Elevated` / `Brush.Border.Default` / `Brush.Status.Offline` when offline).
  2. In Row 1 (16:9 `PreviewBlack` border):
     - Replace `Background="#A60B0F17"` on the `REFRESHING...` overlay with `Background="{DynamicResource Brush.Surface.Scrim}"`.
  3. Replace Row 2 (`Margin="0,8,0,0"`, `MinHeight="36"`) with the streamlined single-row touch-safe footer:
     - Left (`Grid.Column="0"`, `VerticalAlignment="Center"`):
       - Live FPS badge (`{Binding Fps} FPS` when `IsPreviewSupported && IsStreaming`, `PREVIEW PAUSED` when `IsPreviewSupported && !IsStreaming`, `EXTERNAL SOURCE` when `!IsPreviewSupported`, at `12px` SemiBold `Brush.Text.Secondary`).
     - Right (`Grid.Column="1"`, `Orientation="Horizontal"`, `VerticalAlignment="Center"`):
       - `Button x:Name="RefreshStreamButton"` (`Command="{Binding RefreshPreviewCommand}"`, `Style="{StaticResource Style.Button.Secondary}"`, `MinHeight="36"`, `Padding="10,6"`, `FontSize="12"`, `IsTabStop="True"`, `Content="↻ Refresh"`).
       - `Button x:Name="StopStreamButton"` (`Command="{Binding StopPreviewCommand}"`, `Style="{StaticResource Style.Button.Unroute}"`, `MinHeight="36"`, `Padding="10,6"`, `FontSize="12"`, `Margin="8,0,0,0"`, `IsTabStop="True"`, `Content="■ Stop"`, visible when `IsStreaming == true`).
       - `Button x:Name="StartStreamButton"` (`Command="{Binding StartPreviewCommand}"`, `Style="{StaticResource Style.Button.Primary}"`, `MinHeight="36"`, `Padding="10,6"`, `FontSize="12"`, `Margin="8,0,0,0"`, `IsTabStop="True"`, `Content="▶ Start"`, visible when `IsStreaming == false`).
  4. Replace `Background="#990B0F17"` on the offline overlay (line 301) with `Background="{DynamicResource Brush.Surface.OfflineOverlay}"`.
  5. Update `GridSpec.SourceCard` in `src/AvasRoutingTool/Controls/GridSpec.cs`: set `ChromeHeight = 96` (`24` padding + `28` header + `8` margin + `36` footer + `8` margin = `104`, or `96` to match exact layout).

- [ ] **Step 4: Run the tests to verify they pass.**
  Run:
  ```powershell
  dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj --filter "FullyQualifiedName~SourceCard|FullyQualifiedName~ThemeResourceUsageTests"
  ```
  Expected: PASS.

- [ ] **Step 5: Commit.**
  ```powershell
  git add src/AvasRoutingTool/Views/SourceCardView.xaml src/AvasRoutingTool/Controls/GridSpec.cs tests/AvasRoutingTool.Tests/ThemeResourceUsageTests.cs tests/AvasRoutingTool.Tests/ViewSmokeTests.cs
  git commit -m "feat(source-card): streamline telemetry into info badge, fix Light Mode badges, and make stream buttons touch-safe"
  ```

---

### Task 5: `DisplayCardView` Non-Occluding Action Bar, Vector Power Icon, Footer `UndoToast`, Upward-Flipping `LayoutMenuPopup` & `ChromeHeight` Recalibration

**Files:**
- Modify: `src/AvasRoutingTool/Views/DisplayCardView.xaml`
- Modify: `src/AvasRoutingTool/Views/DisplayCardView.xaml.cs` (lines 55–61, 146–157)
- Modify: `src/AvasRoutingTool/Controls/GridSpec.cs` (`DisplayCard.ChromeHeight`)
- Modify: `tests/AvasRoutingTool.Tests/ThemeResourceUsageTests.cs` (remove `"DisplayCardView.xaml"` so `HexCheckExemptFiles` is now empty!)
- Test: `tests/AvasRoutingTool.Tests/ViewSmokeTests.cs`

**Interfaces:**
- Consumes: Task 1 tokens (`Brush.Surface.ModalScrim`, `Brush.Surface.Scrim`, `Brush.Border.Scrim`, `Brush.Surface.ScrimTint`, `Brush.Surface.RoutedBadge`, `Brush.Border.RoutedBadge`, `Brush.Text.OnRoutedBadge`, `Brush.Surface.OfflineOverlay`, `Icon.Power`).
- Produces:
  - Zero raw hex literals across the entire repository (`HexCheckExemptFiles` in `ThemeResourceUsageTests.cs` is empty).
  - 4-row `DisplayCardView.xaml`:
    - `Grid.Row="0"` (`Height="24"`, `Margin="0,0,0,6"`): Identity Header (`DeviceNameText`, `DeviceInfoBadge` `ⓘ` with IP/MAC tooltip, `CopySourceChip`, `ModeBadgeText`).
    - `Grid.Row="1"` (`Height="56"`, `Margin="0,0,0,8"`): Dedicated Non-Occluding Action Bar (`FloatingActionDock` always visible with `LayoutButton`, `CopyLayoutHandle` without `MaxWidth="110"` truncation, `DraftChip`, and `TurnOffButton` with vector `Icon.Power`).
    - `Grid.Row="2"` (`Height="*"`): Unobstructed 16:9 `PreviewSurface` (only modal state overlays `ErrorBanner`, `DropChoicePanel`, `CopyTargetAllowedContent`, `CopyTargetBlockedContent` live inside `PreviewSurface`).
    - `Grid.Row="3"` (`Height="56"`, `Margin="0,8,0,0"`): Clinical Status & Toast Dock (normal 2-line status: `SOURCE: {SourceLabel}` + `OutputLine` on line 1, `DraftNoticeLine` or `CopyFromBlockedLabel` on line 2; when `CanUndoScreen == true` and not copy-target/drop-choice, `UndoToast` (`MinHeight="56"`) overlays Row 3 cleanly outside `PreviewSurface`).
  - `GridSpec.DisplayCard.ChromeHeight = 176` (`CardFrame` vertical padding `24` + Row 0 `30` (`24+6`) + Row 1 `64` (`56+8`) + Row 3 `64` (`56+8`) - `6` = `176..182`, calibrated so `PreviewSurface.ActualWidth / PreviewSurface.ActualHeight` is `16:9` in `DisplayCard_InTheOverview_AtRealWindowSizes_NothingInTheCardClips`).

- [ ] **Step 1: Update `ThemeResourceUsageTests.cs` (empty `HexCheckExemptFiles`) and update/add the `DisplayCardView` tests in `ViewSmokeTests.cs`.**
  1. In `tests/AvasRoutingTool.Tests/ThemeResourceUsageTests.cs`, empty `HexCheckExemptFiles`:
     ```csharp
     private static readonly HashSet<string> HexCheckExemptFiles = new(StringComparer.OrdinalIgnoreCase);
     ```
  2. In `tests/AvasRoutingTool.Tests/ViewSmokeTests.cs`:
     - In `DisplayCard_InTheOverview_AtRealWindowSizes_NothingInTheCardClips` (around lines 1734–1737), update the `UndoToast` bounds check so it asserts that `UndoToast` is inside `CardFrame` and **does NOT intersect `PreviewSurface`**:
       ```csharp
       var tb = BoundsIn(toast, view);
       var pb = BoundsIn(paxPreview, view);
       Assert.False(tb.IntersectsWith(pb), $"the toast {tb} overlaps the picture {pb}");
       ```
     - Replace `DisplayCard_ActionDock_IsUntouchableUntilRevealed_AndATapKeepsItUpForFourSeconds_UntilTheCardUnloads` (line 3765) with `DisplayCard_ActionBar_IsAlwaysVisibleOutsideThePicture_AndTurnOffUsesVectorIcon`:
       ```csharp
       [Fact]
       public void DisplayCard_ActionBar_IsAlwaysVisibleOutsideThePicture_AndTurnOffUsesVectorIcon() =>
           RunSta(() =>
           {
               var rig = new CopyRig();
               var view = rig.View(rig.Rx1, out var host);
               Showing(host, () =>
               {
                   var dock = (Border)Named(view, "FloatingActionDock").Single();
                   var picture = (FrameworkElement)Named(view, "PreviewSurface").Single();
                   var turnOff = (Button)Named(view, "TurnOffButton").Single();
                   Assert.True(dock.IsVisible && dock.IsHitTestVisible && dock.Opacity == 1.0);
                   Assert.False(BoundsIn(dock, view).IntersectsWith(BoundsIn(picture, view)), "the action bar overlaps the 16:9 picture");
                   Assert.True(turnOff.IsVisible && turnOff.ActualHeight >= 48 && turnOff.ActualWidth >= 48);
                   Assert.NotEmpty(FindAll<System.Windows.Shapes.Path>(turnOff));
               });
           });
       ```

- [ ] **Step 2: Run the tests to verify they fail.**
  Run:
  ```powershell
  dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj --filter "FullyQualifiedName~DisplayCard_ActionBar|FullyQualifiedName~DisplayCard_DraftChip|FullyQualifiedName~ThemeResourceUsageTests"
  ```
  Expected: FAIL (`DisplayCardView.xaml` has raw hex literals, `FloatingActionDock` and `UndoToast` overlap `PreviewSurface`, and `TurnOffButton` has no `Path`).

- [ ] **Step 3: Implement the 4-row non-occluding `DisplayCardView.xaml`, `DisplayCardView.xaml.cs`, and `GridSpec.cs`.**
  1. In `src/AvasRoutingTool/Views/DisplayCardView.xaml`:
     - Replace all raw hex literals with semantic tokens:
       - `#263B82F6` / `#593B82F6` on `ModeBadge` -> `Brush.Surface.RoutedBadge` / `Brush.Border.RoutedBadge` with `ModeBadgeText` `Foreground="{DynamicResource Brush.Text.OnRoutedBadge}"` when routed.
       - `#D9070A0F` / `#33FFFFFF` on MultiView slot badges -> `Brush.Surface.Scrim` / `Brush.Border.Scrim`.
       - `#3306B6D4` on slot drag-over -> `Brush.Surface.ScrimTint`.
       - `#E67F1D1D` on `ErrorBanner` -> `Brush.Surface.ScrimDanger`.
       - `#D90A0E16` on `DropChoicePanel`, `CopyTargetAllowed`, `CopyTargetBlocked` -> `Brush.Surface.ModalScrim`.
       - `#2906B6D4` on `CopyTargetRing` -> `Brush.Surface.ScrimTint`.
       - `#990B0F17` on offline overlay -> `Brush.Surface.OfflineOverlay`.
     - Structure `CardFrame`'s inner `Grid` with 4 rows:
       ```xml
       <Grid.RowDefinitions>
           <RowDefinition Height="24" />
           <RowDefinition Height="64" />
           <RowDefinition Height="*" />
           <RowDefinition Height="64" />
       </Grid.RowDefinitions>
       ```
       - **Row 0 (`Height="24"`)**: Left = status dot + `DeviceNameText` (`FontSize="14" FontWeight="Bold"`) + `DeviceInfoBadge` (`ⓘ` badge with `ToolTip` showing `"IP: {IpAddress} · MAC: {MacAddress}"`); Right = `CopySourceChip` (`Margin="0,0,6,0"`, `Padding="8,2"`) + `ModeBadge` (`ModeBadgeText`).
       - **Row 1 (`Height="64"`)**: `<Border x:Name="FloatingActionDock" Grid.Row="1" Height="56" Margin="0,4,0,4" Background="Transparent" IsHitTestVisible="True" Opacity="1">`:
         - Left `StackPanel Orientation="Horizontal"`:
           - `LayoutButton` (`MinHeight="56"`, `Padding="10,6"`, `FontSize="14"`, `Margin="0,0,6,0"`) + `LayoutMenuPopup`.
           - `CopyLayoutHandle` (`MinHeight="56"`, `Padding="10,6"`, `FontSize="14"`, `Margin="0,0,6,0"`, **no `MaxWidth`**, **no `TextTrimming`**, so `"Copy layout"` never truncates!).
           - `DraftChip` (`MinHeight="56"`, `Padding="10,6"`, `FontSize="13"`, `Margin="0,0,6,0"`).
         - Right:
           - `TurnOffButton` (`Width="56" Height="56"`, `Padding="0"`, `Style="{StaticResource Style.Button.Unroute}"`, containing `<Path Data="{StaticResource Icon.Power}" Width="18" Height="18" Stretch="Uniform" Stroke="{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}" StrokeThickness="2" StrokeStartLineCap="Round" StrokeEndLineCap="Round" HorizontalAlignment="Center" VerticalAlignment="Center" />`).
       - **Row 2 (`Height="*"`)**: `PreviewSurface` (`16:9` video canvas). Contains `Slots`, `ErrorBanner` (docked at bottom of `PreviewSurface`), `DropChoicePanel`, `CopyTargetAllowed`, `CopyTargetBlocked`, and `PictureFocusRing`.
       - **Row 3 (`Height="64"`)**: `<Grid Grid.Row="3" Margin="0,8,0,0" Height="56">`:
         - Normal status layer (`StackPanel VerticalAlignment="Center"`):
           - Line 1 (`Grid`): Left = `SOURCE: ` + `SourceLabel` (`12px` Bold); Right = `OutputLine` (`Text="{Binding CanvasInfo, Mode=OneWay}"`, `12px` SemiBold `Brush.Text.Accent`).
           - Line 2: `DraftNoticeLine` (`FontSize="12"`, `Foreground="{DynamicResource Brush.Status.StagedText}"`) and `CopyFromBlockedLabel` (`FontSize="12"`, `Foreground="{DynamicResource Brush.Text.Secondary}"`).
         - Toast overlay layer (`Border x:Name="UndoToast" MinHeight="56"`): spans Row 3 (`Grid.Row="3"`), completely outside `PreviewSurface`! When `CanUndoScreen == true` (and `IsCopyTargetAllowed == false`, `IsCopyTargetBlocked == false`, `IsDropChoiceOpen == false`), `UndoToast` is `Visible` over Row 3 with `UndoToastLabel` and `UndoScreenButton` (`MinHeight="56"`).
  2. In `src/AvasRoutingTool/Controls/GridSpec.cs`:
     - Update `DisplayCard`: `ChromeHeight = 182` (`CardFrame` vertical padding `24` + border `4` + Row 0 `24` + Row 1 `64` + Row 3 `64` = `180`, verify exact pixel math in `DisplayCard_InTheOverview_AtRealWindowSizes_NothingInTheCardClips` so `PreviewSurface.ActualWidth / PreviewSurface.ActualHeight` is `1.778 ± 0.02`!).
     - Also check `DisplayCard_LayoutMenu_OpensWithSixSchematicTiles_AndHeaderButtonIsAtLeast56High` in `ViewSmokeTests.cs` line 1240: `Assert.True(BoundsIn(button, view).Top < 80, "the Layout button is not in the card header");` — with Row 0 (`24px`) + `CardFrame` padding (`12px`) + Row 1 top (`4px`), `BoundsIn(LayoutButton, view).Top` is `40px` (`< 80`), which passes!
     - Also check `CopyRig.View` default height in `ViewSmokeTests.cs` line 1171 (`double width = 420, double height = 400`): at `width = 420`, `PreviewSurface` width is `392` (`16:9` height = `220.5`), plus `180` chrome = `400.5px` (`height = 400` fits!).

- [ ] **Step 4: Run the tests to verify they pass.**
  Run:
  ```powershell
  dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj --filter "FullyQualifiedName~DisplayCard|FullyQualifiedName~ThemeResourceUsageTests"
  ```
  Expected: PASS.

- [ ] **Step 5: Commit.**
  ```powershell
  git add src/AvasRoutingTool/Views/DisplayCardView.xaml src/AvasRoutingTool/Views/DisplayCardView.xaml.cs src/AvasRoutingTool/Controls/GridSpec.cs tests/AvasRoutingTool.Tests/ThemeResourceUsageTests.cs tests/AvasRoutingTool.Tests/ViewSmokeTests.cs
  git commit -m "feat(display-card): move action dock and undo toast out of 16:9 video and add vector power icon"
  ```

---

### Task 6: `VisualRoutingView` Layout Stability (Full Source Cards When Armed & Retire Bottom `LayoutsBarView`)

**Files:**
- Modify: `src/AvasRoutingTool/Views/VisualRoutingView.xaml`
- Modify: `src/AvasRoutingTool/Views/DisplayCardView.xaml.cs` (`RevealState` header preservation)
- Test: `tests/AvasRoutingTool.Tests/ViewSmokeTests.cs`

**Interfaces:**
- Consumes: Task 4 & Task 5 card layouts.
- Produces:
  - In `VisualRoutingView.xaml`:
    - `SourcesScrollViewer` remains `Visible` whenever `ActiveComposer == null` (never collapses when `ArmedSource != null` or `IsCopyOrChoiceActive == true`).
    - `CompactSourcePalette` is `Visible` **only** when `ActiveComposer != null` (inside the MultiView Composer).
    - `LayoutsBarView` is removed from `VisualRoutingView.xaml`.
  - Updated `ViewSmokeTests.cs` tests (`DisplayCard_InTheOverview_AtRealWindowSizes_NothingInTheCardClips`, `DisplayCard_CopyAndChoiceStates_ReadInsideTheDisplaysView_AtRealWindowSizes`, `VisualRoutingView_WithASourceArmed_*`) verifying the new stability invariant.

- [ ] **Step 1: Update the overview layout tests in `ViewSmokeTests.cs` to assert full source card stability and no `LayoutsBarView` in `VisualRoutingView`.**
  In `tests/AvasRoutingTool.Tests/ViewSmokeTests.cs`:
  1. In `DisplayCard_InTheOverview_AtRealWindowSizes_NothingInTheCardClips` (lines 1701 and 1729):
     Replace `Assert.False(FindAll<LayoutsBarView>(view).Single().IsVisible);` and `AssertInside(FindAll<LayoutsBarView>(view).Single(), view);` with:
     ```csharp
     Assert.Empty(FindAll<LayoutsBarView>(view));
     ```
  2. In `DisplayCard_CopyAndChoiceStates_ReadInsideTheDisplaysView_AtRealWindowSizes` (lines 1770–1779 and 1809):
     Update the assertions to verify that `SourceCardView` stays visible (no layout jump to `CompactSourcePalette` in the overview) and `LayoutsBarView` is absent:
     ```csharp
     Assert.Empty(FindAll<LayoutsBarView>(view));

     // Copy armed (with the banner): the full source cards stay in place (no mid-reach vertical jump).
     Pump(() => rig.Vm.ArmDisplayAsync(rig.Rx1));
     Relayout(view);
     AssertVisibleText(view, "Copying RX1_c059e_SFPA - ");
     Assert.False(Named(view, "CompactSourcePalette").Single().IsVisible);
     Assert.NotEmpty(FindAll<SourceCardView>(view).Where(s => s.IsVisible));
     ```
  3. Update `VisualRoutingView_WithASourceArmed_TheSourcesGoCompact_AndTheCardPictureIsInTheDisplaysView` (line 3470):
     Keep the method name (or update its body) so it asserts the new D3/D5 ergonomic invariant:
     ```csharp
     [Theory]
     [InlineData(1264, 755)]
     [InlineData(1008, 615)]
     public void VisualRoutingView_WithASourceArmed_TheSourcesGoCompact_AndTheCardPictureIsInTheDisplaysView(int width, int height) =>
         RunSta(() =>
         {
             var (vm, rx, sources) = Overview();
             var view = new VisualRoutingView { DataContext = vm };

             ShowingAt(view, width, height, () =>
             {
                 var scroll = (ScrollViewer)Named(view, "DisplaysScroll").Single();
                 var picture = (FrameworkElement)Named(FindAll<DisplayCardView>(view).Single(), "PreviewSurface").Single();
                 var sourceCard = FindAll<SourceCardView>(view).First(s => s.IsVisible);
                 double initialSourceHeight = sourceCard.ActualHeight;
                 Assert.Empty(FindAll<LayoutsBarView>(view));

                 // Arming a source in the overview keeps full SourceCardView height (zero vertical jump);
                 // CompactSourcePalette is reserved exclusively for the MultiView Composer.
                 vm.ArmSource(sources[0]);
                 Relayout(view);

                 Assert.False(Named(view, "CompactSourcePalette").Single().IsVisible);
                 Assert.NotEmpty(FindAll<SourceCardView>(view).Where(s => s.IsVisible));
                 Assert.Equal(initialSourceHeight, sourceCard.ActualHeight, 1);
                 AssertReadable(picture, scroll, "the card's picture while a source is armed");

                 // Opening the Composer switches the top strip to CompactSourcePalette.
                 Pump(() => vm.OpenComposerAsync(rx, "matrix2x2"));
                 Relayout(view);
                 Assert.True(Named(view, "CompactSourcePalette").Single().IsVisible);
                 Assert.Empty(FindAll<SourceCardView>(view).Where(s => s.IsVisible));

                 vm.CloseComposer();
                 vm.ArmSource(sources[0]); // disarm
                 Relayout(view);
                 Assert.NotEmpty(FindAll<SourceCardView>(view).Where(s => s.IsVisible));
             });
         });
     ```

- [ ] **Step 2: Run the tests to verify they fail.**
  Run:
  ```powershell
  dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj --filter "FullyQualifiedName~VisualRoutingView_WithASourceArmed|FullyQualifiedName~DisplayCard_CopyAndChoiceStates|FullyQualifiedName~DisplayCard_InTheOverview"
  ```
  Expected: FAIL (`VisualRoutingView` still has `LayoutsBarView` and swaps to `CompactSourcePalette` when `ArmedSource != null`).

- [ ] **Step 3: Implement the changes in `src/AvasRoutingTool/Views/VisualRoutingView.xaml` and `src/AvasRoutingTool/Views/DisplayCardView.xaml.cs`.**
  1. In `src/AvasRoutingTool/Views/VisualRoutingView.xaml`:
     - On the full sources `ScrollViewer` (lines 77–94), remove the `ArmedSource` and `IsCopyOrChoiceActive` triggers so it only collapses when `ActiveComposer != null`:
       ```xml
       <ScrollViewer.Style>
           <Style TargetType="ScrollViewer" BasedOn="{StaticResource {x:Type ScrollViewer}}">
               <Setter Property="Visibility" Value="Collapsed" />
               <Style.Triggers>
                   <DataTrigger Binding="{Binding ActiveComposer, Mode=OneWay}" Value="{x:Null}">
                       <Setter Property="Visibility" Value="Visible" />
                   </DataTrigger>
               </Style.Triggers>
           </Style>
       </ScrollViewer.Style>
       ```
     - On `CompactSourcePalette` (`ScrollViewer` at lines 109–127), show it **only** when `ActiveComposer` is non-null:
       ```xml
       <ScrollViewer.Style>
           <Style TargetType="ScrollViewer" BasedOn="{StaticResource {x:Type ScrollViewer}}">
               <Setter Property="Visibility" Value="Visible" />
               <Style.Triggers>
                   <DataTrigger Binding="{Binding ActiveComposer, Mode=OneWay}" Value="{x:Null}">
                       <Setter Property="Visibility" Value="Collapsed" />
                   </DataTrigger>
               </Style.Triggers>
           </Style>
       </ScrollViewer.Style>
       ```
     - Delete `<views:LayoutsBarView Grid.Row="3">...</views:LayoutsBarView>` (lines 287–307) and `<RowDefinition Height="Auto" />` for `Grid.Row="3"`.
  2. In `src/AvasRoutingTool/Views/DisplayCardView.xaml.cs` (`RevealState()` at lines 146–157):
     - Ensure `RevealState()` brings the relevant state parts into view cleanly without clipping the card header when the card fits in the viewport.

- [ ] **Step 4: Run the full non-live test suite to verify 100% pass rate.**
  Run:
  ```powershell
  dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj --filter "FullyQualifiedName!~LiveControlServerTests&FullyQualifiedName!~LiveFunctionTests"
  ```
  Expected: All ~748+ non-live tests PASS with zero errors or warnings.

- [ ] **Step 5: Update `memory/active_context.md` and `memory/progress.md`, then commit.**
  1. Update `memory/active_context.md` and `memory/progress.md` with the completed tasks, test count, and updated `ChromeHeight` / token architecture.
  2. Run a Release publish check:
     ```powershell
     dotnet publish src/AvasRoutingTool/AvasRoutingTool.csproj -c Release -o publish/AvasRoutingTool
     ```
  3. Commit:
     ```powershell
     git add src/AvasRoutingTool/Views/VisualRoutingView.xaml src/AvasRoutingTool/Views/DisplayCardView.xaml.cs tests/AvasRoutingTool.Tests/ViewSmokeTests.cs memory/
     git commit -m "feat(overview): keep source cards full-height when armed and remove redundant bottom layouts bar"
     ```
