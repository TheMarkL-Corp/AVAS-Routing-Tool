# UI Enhancement, Auto-Hide Controls & Clinical Light Mode Specification

**Date:** 2026-10-27  
**Status:** Draft / Proposed  
**Audience:** Circulating Nurses, Surgical Techs, Biomed/AV Engineers, Software Developers  
**Design Principles:** Impeccable Operate Mode, Touch-First (Gloved Touch >= 48px), Clean Visual Hierarchy, High-Contrast Clinical Ergonomics  

---

## 1. Problem Statement & Motivation

Following the completion and hardware merge of Phase 2 MultiView Composer, real-world inspection of the running application (`media_1793069548754_968d5bc6.jpg`) revealed key UX deficiencies:

1. **Button Clutter & Video Starvation**:
   - Each display card features a dedicated 56px-tall action row holding `[ ⊞ Layout ▾ ]` and `[ ⎘ Copy layout ]`, plus a 22px blocked-explanation row, telemetry lines, and a separate `[ ⏻ Turn Off ]` button.
   - Non-video chrome consumes 175px–205px per card.
   - On standard touchscreen displays (e.g., 1080p with display canvas height ~350px), the video preview is crushed to ~130px–145px height, occupying less than 40% of the card area.
   - The primary purpose of the display card—seeing the live surgical picture clearly—is severely compromised by persistent, oversized chrome.

2. **Absence of Light Mode**:
   - Operating rooms and surgical prep areas often feature intense overhead LED surgical lighting (10,000+ lux) or bright ambient conditions.
   - A dark-only interface causes glare, reflections, and eye strain in high-ambient-light environments.
   - Modern medical consoles (GE, Stryker, Olympus) offer high-contrast clinical light modes with crisp contrast and anti-glare palettes.

---

## 2. Design Vision & Operate Mode Principles

Following the **Impeccable Operate Mode** guidelines:
- **Chrome Recedes, Content Leads**: The live video preview must dominate the card. The operator’s eyes should land on the picture first, device identity second, and controls only when needed.
- **Progressive Disclosure**: Routine monitoring requires zero button chrome. Configuration actions (`Layout`, `Copy`, `Turn Off`) should be readily available via touch or hover, but invisible or minimal when idle.
- **Touch-First Accessibility**: Gloved fingers require touch targets $\ge 48\text{px} \times 48\text{px}$. Auto-hiding must not degrade touch usability. A single tap on the card or video must instantly reveal floating controls with generous touch buffers.
- **Deterministic State Visibility**: Critical operational states—such as an active Draft (`HasDraft`), armed Copy source (`IsCopySource`), or an active error banner—must never be hidden. They remain persistently pinned.
- **Dual Theme System**: Seamless switching between **Slate Dark** (current) and **Clinical Light** (medical grade, high contrast) with zero restart required and persistent user preference.

---

## 3. UI Component Architecture

### 3.1 DisplayCardView Redesign

```
+-------------------------------------------------------------+
| ● RX1_c059e_SFPA                        [MATRIX 2X2 · 4/4]  |  <- Row 0: Header (28px)
+-------------------------------------------------------------+
|                                                             |
|                                                             |
|                   LIVE VIDEO PREVIEW                        |
|                                                             |  <- Row 1: Video Surface
|                                                             |     (Fills available
|   +-----------------------------------------------------+   |      height, ~250px)
|   |  [ ⊞ Layout ▾ ]    [ ⎘ Copy layout ]    [ ⏻ Off ]   |   |  <- Floating Action Strip
|   +-----------------------------------------------------+   |     (Auto-hides when idle,
|                                                             |      fades in on hover/tap)
+-------------------------------------------------------------+
| 1920x1080@60Hz  ·  192.168.1.101  ·  00:14:be:c0:59:ea     |  <- Row 2: Status Line (20px)
+-------------------------------------------------------------+
```

#### Key Architectural Changes:
1. **Elimination of Rows 1, 2, and 5**:
   - The persistent 56px button row, the 22px blocked label, and the dedicated Turn Off button row are removed from the root layout.
   - Replaced by a single **Floating Action Strip** hosted directly inside `PreviewSurface`.
2. **Auto-Hide & Progressive Disclosure Behavior**:
   - **Idle State**: Action strip opacity = 0 (or subtle 10% translucent pill icon dock). Live video is 100% visible and uncropped.
   - **Hover / Tap / Focus State**: Action strip smoothly transitions to opacity = 1 with a frosted translucent backdrop (`#D90F172A` in Dark, `#E6FFFFFF` in Light).
   - **Touch Interaction**: Tapping the video preview reveals the floating action strip and starts a 4-second auto-dim timer. Tapping an action immediately executes it.
   - **Keyboard Navigation**: Pressing `Tab` or navigating with arrow keys into the card focuses the controls, instantly revealing the action strip (`IsKeyboardFocusWithin == true`).
   - **Persistent Overrides**: When `IsCopySource` is active or `HasDraft` is active, the relevant badge/action stays visible to prevent operator confusion.
3. **Height Savings & Preview Expansion**:
   - `GridSpec.DisplayCard.ChromeHeight` drops from **175px** to **~75px**.
   - Available video preview height increases by **~70%–100%** on standard grid layouts!

### 3.2 Clinical Light Mode Color Palette

| Token | Dark Mode (Incumbent) | Light Mode (Clinical Medical) | Contrast Ratio |
|---|---|---|---|
| `Brush.Background.Dark` | `#0B0F17` | `#F8FAFC` (Slate 50) | N/A (Canvas) |
| `Brush.Surface.Card` | `#151E2E` | `#FFFFFF` (Pure White) | 1.1:1 vs Canvas |
| `Brush.Surface.Elevated` | `#1E293B` | `#F1F5F9` (Slate 100) | 1.2:1 vs Canvas |
| `Brush.Surface.Hover` | `#24334A` | `#E2E8F0` (Slate 200) | 1.3:1 vs Canvas |
| `Brush.Border.Default` | `#223049` | `#CBD5E1` (Slate 300) | 3.5:1 (Crisp edge) |
| `Brush.Text.Primary` | `#F8FAFC` | `#0F172A` (Slate 900) | 15.8:1 (AAA) |
| `Brush.Text.Secondary` | `#94A3B8` | `#475569` (Slate 600) | 7.0:1 (AAA) |
| `Brush.Text.Muted` | `#64748B` | `#64748B` (Slate 500) | 4.6:1 (AA) |
| `Brush.Accent.Primary` | `#3B82F6` | `#2563EB` (Cobalt Blue) | 4.8:1 (AA) |
| `Brush.Accent.PrimaryHover`| `#2563EB` | `#1D4ED8` (Blue 700) | 6.5:1 |
| `Brush.Status.Online` | `#10B981` | `#059669` (Emerald 600) | 4.5:1 (AA) |
| `Brush.Status.Offline` | `#64748B` | `#94A3B8` (Slate 400) | 2.5:1 |
| `Brush.Status.Routed` | `#3B82F6` | `#2563EB` (Blue 600) | 4.8:1 |
| `Brush.Action.Danger` | `#EF4444` | `#DC2626` (Red 600) | 4.9:1 |
| `Brush.Status.Staged` | `#F59E0B` | `#D97706` (Amber 600) | 4.5:1 |
| `Brush.Surface.CopyTarget` | `#082F3A` | `#ECFEFF` (Cyan 50) | Special card bg |
| `Brush.Text.CopyAccent` | `#A5F3FC` | `#0E7490` (Cyan 700) | 5.2:1 |

### 3.3 Dynamic Theming Engine

- **Resource Architecture**:
  - `Resources/Themes/Theme.Dark.xaml`: Contains all dark color tokens, solid brushes, and dark-specific overlays.
  - `Resources/Themes/Theme.Light.xaml`: Contains corresponding light color tokens, solid brushes, and light-specific overlays.
  - `Resources/Theme.xaml`: Common control templates, button styles, typography, scrollbars, and geometries. References color brushes via `{DynamicResource}`.
- **ThemeService (`IThemeService`)**:
  - Supports `ThemeMode.Dark`, `ThemeMode.Light`, `ThemeMode.System`.
  - On theme change, replaces the theme ResourceDictionary in `Application.Current.Resources.MergedDictionaries`.
  - Persists preference in `AppConfig.ThemeMode`.
- **Top App Bar Toggle**:
  - A clean toggle button placed in the top-right toolbar next to `Refresh` and `Settings`:
    - Shows `[ ☀️ Light ]` when currently dark.
    - Shows `[ 🌙 Dark ]` when currently light.

---

## 4. SourceCardView Streamlining

- Maintain video dominance on Source Cards as well:
  - Header: Status Dot, Device Name, Link Mode Badge, FPS Badge (clean 24px header).
  - Video Preview Surface: Dominant 16:9 canvas.
  - Footer: Single compact row containing IP/MAC and subtle action chips (`Refresh`, `Start`/`Stop` stream) with reduced vertical footprint.

---

## 5. Verification & Testing Guardrails

1. **Test-Driven Refactoring**:
   - `ViewSmokeTests.cs`: Update element bounds assertions to reflect the expanded preview and new action strip geometry while retaining 100% of functional coverage (`LayoutButton`, `CopyLayoutHandle`, `DraftChip`, `Turn Off`, keyboard focus, Esc, automation names).
   - Theme switching test: Add unit and STA smoke tests verifying that swapping between `Theme.Dark.xaml` and `Theme.Light.xaml` leaves no unresolved resources, null brushes, or binding errors.
2. **Accessibility & Usability Requirements**:
   - All interactive touch targets must have at least $48\text{px} \times 48\text{px}$ hit test area.
   - Text contrast must exceed WCAG AA ($\ge 4.5:1$) in both Dark and Light modes.
   - Keyboard path (`Arrows`, `Tab`, `Enter`, `Space`, `Esc`) must function identically in both modes.
