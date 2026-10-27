# UI Enhancement, Auto-Hide Controls & Clinical Light Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Maximize video preview size by replacing persistent 56px card buttons with an auto-hiding floating action strip, and introduce a high-contrast clinical Light Mode with instant dynamic theme switching.

**Architecture:** Split the monolithic `Theme.xaml` into modular dynamic palette dictionaries (`Theme.Dark.xaml` and `Theme.Light.xaml`) swappable at runtime via `ThemeService` without restarting the app. Overhaul `DisplayCardView` and `SourceCardView` to eliminate dedicated button rows, hosting touch-friendly ($\ge 48\text{px}$) action pills (`Layout`, `Copy`, `Turn Off`) inside an auto-hiding overlay on the preview surface, dropping non-video chrome by >55% and expanding video previews by up to ~80%.

**Tech Stack:** C# .NET 8 WPF (`net8.0-windows`), XAML, MVVM, `DynamicResource` brush bindings, `WriteableBitmap` video rendering, xUnit STA UI testing.

**Spec:** [`docs/superpowers/specs/2026-10-27-ui-enhancement-and-light-mode.md`](file:///c:/Users/POC-615/Documents/GitHub/AVAS-Routing-Tool/docs/superpowers/specs/2026-10-27-ui-enhancement-and-light-mode.md)

---

## Global Constraints

- **Platform Target:** Windows 10/11 Desktop, .NET 8 WPF (`net8.0-windows`).
- **Touch Target Floor:** All interactive tap targets must maintain $\ge 48\text{px} \times 48\text{px}$ hit test envelopes for gloved OR operation.
- **Contrast Ratios:** Text on card backgrounds must exceed WCAG AA ($\ge 4.5:1$ for normal text, $\ge 3:1$ for large/bold text) in both Dark and Light modes.
- **Zero Restart Runtime Theming:** Toggling between Dark and Light mode must immediately repaint all active controls using `DynamicResource` with zero visual glitches or window reloads.
- **Keyboard & Screen Reader Parity:** Keyboard focus (`Tab`, `Arrows`, `Enter`, `Space`, `Esc`) must automatically reveal hidden action controls when focused (`IsKeyboardFocusWithin`). Automation names must remain 100% intact.
- **Test Integrity:** All 701 non-live tests must pass without regression.

---

## Review Focus

1. **Theme Switching Resource Leaks or Missing DynamicResources:** When swapping from Dark to Light or Light to Dark, any hardcoded `StaticResource Brush.*` will fail to update; all brush references in views must use `DynamicResource`.
2. **Touch Auto-Hide Usability on Non-Mouse Devices:** Touchscreens have no hover state; tapping anywhere on the card or video must reveal the floating action strip and keep it interactive for at least 4 seconds or until dismissed.
3. **Critical State Visibility Override:** When a card has an active draft (`HasDraft == true`) or is armed as a copy source (`IsCopySource == true`), the status badges must remain permanently visible and never auto-hide.
4. **Adaptive Grid Math Consistency:** Dropping `ChromeHeight` from 175 to ~75 in `AdaptiveGridLayout` must scale cards properly without causing negative sizes, infinite loops, or horizontal card clipping at min viewport (1024x720).
5. **STA Thread Concurrency in View Tests:** Theme resource dictionary swapping must run cleanly on the WPF STA dispatcher during test runs without throwing cross-thread resource access exceptions.

---

## Task Decomposition

### Task 1: Clinical Light Mode Palette & Modular Theme Dictionaries

**Files:**
- Create: `src/AvasRoutingTool/Resources/Themes/Theme.Dark.xaml`
- Create: `src/AvasRoutingTool/Resources/Themes/Theme.Light.xaml`
- Modify: `src/AvasRoutingTool/Resources/Theme.xaml`
- Create: `tests/AvasRoutingTool.Tests/ThemeTests.cs`

**Step-by-step:**
- [ ] **Step 1 (Test):** Write a failing unit test in `ThemeTests.cs` that instantiates both `Theme.Dark.xaml` and `Theme.Light.xaml` dictionaries and asserts that every color and brush key in Dark mode exists in Light mode with matching type and valid contrast.
- [ ] **Step 2 (Verify):** Run `dotnet test --filter "FullyQualifiedName~ThemeTests"` and verify it fails (files do not exist yet).
- [ ] **Step 3 (Implement):** Create `src/AvasRoutingTool/Resources/Themes/Theme.Dark.xaml` containing all incumbent dark slate colors and brushes.
- [ ] **Step 4 (Implement):** Create `src/AvasRoutingTool/Resources/Themes/Theme.Light.xaml` containing the clinical light colors and brushes (Slate 50 background `#F8FAFC`, Pure White card `#FFFFFF`, Slate 300 border `#CBD5E1`, Slate 900 primary text `#0F172A`, Cobalt Blue `#2563EB`).
- [ ] **Step 5 (Implement):** Update `src/AvasRoutingTool/Resources/Theme.xaml` to merge `Themes/Theme.Dark.xaml` by default and define common button styles, scrollbars, and templates using `{DynamicResource}` for color brushes.
- [ ] **Step 6 (Verify):** Run `dotnet test --filter "FullyQualifiedName~ThemeTests"` and confirm tests pass.
- [ ] **Step 7 (Commit):** Git commit: `feat(theme): modularize dark and clinical light theme dictionaries`.

---

### Task 2: ThemeService, Configuration Persistence & Top App Bar Toggle

**Files:**
- Create: `src/AvasRoutingTool/Services/IThemeService.cs`
- Create: `src/AvasRoutingTool/Services/ThemeService.cs`
- Modify: `src/AvasRoutingTool/Models/AppConfig.cs`
- Modify: `src/AvasRoutingTool/ViewModels/MainViewModel.cs`
- Modify: `src/AvasRoutingTool/MainWindow.xaml`
- Create: `tests/AvasRoutingTool.Tests/ThemeServiceTests.cs`

**Step-by-step:**
- [ ] **Step 1 (Test):** Write a unit test in `ThemeServiceTests.cs` testing `SetTheme(ThemeMode.Light)` and `SetTheme(ThemeMode.Dark)`, verifying the merged dictionary in `Application.Current.Resources` updates and persists to `AppConfig`.
- [ ] **Step 2 (Verify):** Run `dotnet test --filter "FullyQualifiedName~ThemeServiceTests"` and verify it fails.
- [ ] **Step 3 (Implement):** Add `ThemeMode` enum (`Dark`, `Light`, `System`) and `Theme` property to `AppConfig.cs`.
- [ ] **Step 4 (Implement):** Implement `ThemeService.cs` with thread-safe dictionary replacement logic on `Application.Current.Dispatcher`.
- [ ] **Step 5 (Implement):** Expose `ToggleThemeCommand` and `CurrentTheme` in `MainViewModel.cs`.
- [ ] **Step 6 (Implement):** Add a theme toggle pill button `[ ☀️ Light ]` / `[ 🌙 Dark ]` in `MainWindow.xaml` next to Refresh and Settings.
- [ ] **Step 7 (Verify):** Run `dotnet test --filter "FullyQualifiedName~ThemeServiceTests"` and verify pass.
- [ ] **Step 8 (Commit):** Git commit: `feat(theme): implement ThemeService with app bar toggle and persistence`.

---

### Task 3: DisplayCardView Progressive Disclosure & Auto-Hide Action Strip

**Files:**
- Modify: `src/AvasRoutingTool/Views/DisplayCardView.xaml`
- Modify: `src/AvasRoutingTool/Views/DisplayCardView.xaml.cs`
- Modify: `src/AvasRoutingTool/ViewModels/DisplayCardViewModel.cs`

**Step-by-step:**
- [ ] **Step 1 (Test):** Add UI test in `DisplayCardMultiViewTests.cs` validating that the floating action strip is present on `PreviewSurface`, exposes `LayoutButton`, `CopyLayoutHandle`, and `TurnOffButton`, and reveals on tap/hover/focus.
- [ ] **Step 2 (Verify):** Run `dotnet test --filter "FullyQualifiedName~DisplayCardMultiViewTests"` and observe failure on new assertion.
- [ ] **Step 3 (Implement):** Refactor `DisplayCardView.xaml`:
  - Collapse dedicated Row 1 (button row), Row 2 (blocked label), and Row 5 (turn off button).
  - Row 0: Clean Header with Status Dot, Device Name, Mode Badge, and persistent Draft Chip (when draft is active).
  - Row 1: `PreviewSurface` expanding to take all remaining vertical height (`Height="*"`).
  - Inside `PreviewSurface`: Add floating `FloatingActionDock` container at the bottom/top of the video:
    - Hosts `LayoutButton` (`[ ⊞ Layout ▾ ]`), `CopyLayoutHandle` (`[ ⎘ Copy layout ]`), and `TurnOffButton` (`[ ⏻ Off ]`).
    - Default `Opacity="0"` with smooth fade animation to `Opacity="1"` on mouse hover, touchscreen tap, or keyboard focus (`IsKeyboardFocusWithin`).
    - Buttons styled with min touch target height $\ge 48\text{px}$, sleek rounded pill aesthetics with frosted glass backdrop.
    - When `IsCopySource` is active or copy is armed, the dock stays locked at `Opacity="1"`.
  - Row 2: Compact telemetry line (IP, MAC, SOURCE, Canvas EDID info).
- [ ] **Step 4 (Implement):** In `DisplayCardView.xaml.cs`, add touch tap handler with 4-second auto-dim timer (`DispatcherTimer`) to auto-hide the action strip on touchscreens after inactivity.
- [ ] **Step 5 (Verify):** Run `dotnet test --filter "FullyQualifiedName~DisplayCardMultiViewTests"` and verify pass.
- [ ] **Step 6 (Commit):** Git commit: `feat(ui): convert display card controls to auto-hiding floating action dock`.

---

### Task 4: SourceCardView Streamlining & Adaptive Sizing Recalibration

**Files:**
- Modify: `src/AvasRoutingTool/Views/SourceCardView.xaml`
- Modify: `src/AvasRoutingTool/Controls/AdaptiveGridLayout.cs`
- Modify: `tests/AvasRoutingTool.Tests/AdaptiveCardPanelTests.cs`

**Step-by-step:**
- [ ] **Step 1 (Test):** Update `AdaptiveCardPanelTests.cs` to test the new `GridSpec.DisplayCard.ChromeHeight` (~75px) and verify that computed card plans yield significantly larger video rectangles on standard viewports (1280x720, 1920x1080).
- [ ] **Step 2 (Verify):** Run `dotnet test --filter "FullyQualifiedName~AdaptiveCardPanelTests"` and observe failure.
- [ ] **Step 3 (Implement):** In `AdaptiveGridLayout.cs`, update `GridSpec.DisplayCard`:
  ```csharp
  public static GridSpec DisplayCard { get; } = Default with { ChromeHeight = 75, HorizontalPadding = 20 };
  ```
- [ ] **Step 4 (Implement):** In `SourceCardView.xaml`, streamline the footer controls into a single clean row with compact action chips (`Refresh`, `Start`/`Stop`) and update all brush references to `{DynamicResource}` for Light Mode compatibility.
- [ ] **Step 5 (Verify):** Run `dotnet test --filter "FullyQualifiedName~AdaptiveCardPanelTests"` and verify pass.
- [ ] **Step 6 (Commit):** Git commit: `feat(layout): reduce DisplayCard ChromeHeight to 75px and streamline source footer`.

---

### Task 5: ViewSmokeTests Alignment & Full Theme Smoke Verification

**Files:**
- Modify: `tests/AvasRoutingTool.Tests/ViewSmokeTests.cs`

**Step-by-step:**
- [ ] **Step 1 (Refactor tests):** Update tests in `ViewSmokeTests.cs` that asserted the previous 56px separate button row geometry:
  - Update `DisplayCard_WithDraft_At320PxWide_AllHeaderPartsFitWithoutEllipsis_AndThePictureIsNotCovered` to verify that `LayoutButton`, `CopyLayoutHandle`, and `DraftChip` exist, are touch-accessible ($\ge 48\text{px}$), and function cleanly in the floating dock/header.
  - Verify that `LayoutButton` opens the popup, `CopyLayoutHandle` initiates copy drag/tap, `Esc` dismisses menus, and keyboard focus works without error.
- [ ] **Step 2 (Add STA Theme Smoke Test):** Add `ViewSmokeTests.App_ThemeToggle_DarkToLightAndBack_HasZeroMissingResourcesOrExceptions()` in `ViewSmokeTests.cs` to render all views (Sources, Displays, Composer, Logs) in both Dark and Light themes under STA.
- [ ] **Step 3 (Verify):** Run full test suite:
  `dotnet test --filter "FullyQualifiedName!~LiveControlServerTests&FullyQualifiedName!~LiveFunctionTests"`
- [ ] **Step 4 (Commit):** Git commit: `test(smoke): align view smoke tests with progressive disclosure and add theme STA tests`.

---

### Task 6: Final Verification, Documentation & Standalone Build

**Files:**
- Modify: `memory/active_context.md`
- Modify: `memory/progress.md`

**Step-by-step:**
- [ ] **Step 1 (Full Suite Run):** Execute `dotnet test AvasRoutingTool.sln --filter "FullyQualifiedName!~LiveControlServerTests&FullyQualifiedName!~LiveFunctionTests"`. Confirm 100% pass (701+ tests).
- [ ] **Step 2 (Publish Build):** Build standalone release to `publish/AvasRoutingTool`.
- [ ] **Step 3 (Memory Update):** Update `memory/active_context.md` and `memory/progress.md`.
- [ ] **Step 4 (Git Push):** Clean atomic commit and push to remote if requested.
