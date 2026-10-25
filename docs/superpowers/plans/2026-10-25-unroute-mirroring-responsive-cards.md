# Decoder Un-Route, Video Mirroring & Responsive Layout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement multi-modal decoder un-routing (`[ ⏻ Turn Off / Disconnect ]` card button + drag-and-drop from the bottom "Off" layout tile), real-time zero-overhead video preview mirroring on Decoder cards when routed to AVAS transmitters, and a fluid responsive layout system optimizing card dimensions (420px–500px, 16:9 preview surface) across FullHD (1920x1080) and high-resolution screens.

**Architecture:**
1. **Multi-Modal Video Un-Routing**:
   - Dedicated on-card action: Red `[ ⏻ Turn Off / Disconnect ]` button in the `DisplayCardView` footer bound to `UnrouteCommand`, dispatching Semtech BlueRiver REST `leave` (`POST /api/device/{rxMac}` with `op: "leave", subscription_type: "HDMI"`).
   - Drag-and-drop "Off" layout: Enable dragging `LayoutItemViewModel` (`Id == "off"`) from the bottom bar onto any routed display card to execute `UnrouteDisplayAsync`.
2. **Zero-Overhead Decoder Preview Mirroring**:
   - `DisplayCardViewModel` mirrors the active `SourceCardViewModel.PreviewBitmap` reference and `IsPreviewSupported` state with zero additional network socket connections or redundant CPU/GPU decoding.
   - When routed to an AVAS transmitter (`TX2_DUAL LINK_SFPA`), the decoder card immediately renders the identical live video preview stream.
   - When routed to a non-AVAS transmitter (`VEGA_1100`), the decoder card displays an informative `"Preview not supported - Non-AVAS Source"` banner with active routing telemetry.
   - When un-routed, the preview clears and the card transitions back to the idle drop-target surface (`"Drop Source Here to Route"`).
3. **Fluid Responsive Card & Canvas Geometry**:
   - Transform `SourceCardView` and `DisplayCardView` from fixed dimensions (320x265 / 300x220) to fluid responsive components (`MinWidth="380" MaxWidth="520" Height="295"`) featuring 16:9 widescreen video preview surfaces (`Height="185"`).
   - Update `VisualRoutingView.xaml` with balanced row sizing (`RowDefinition Height="*"` with `MinHeight="330"`), adaptive centering, and responsive wrap spacing (`Margin="12"`) to eliminate the dark void on 1920x1080 screens while preserving clean horizontal scrolling on smaller displays.

**Tech Stack:** C# .NET 8 (`net8.0-windows`), WPF XAML, System.Windows.Media.Imaging, xUnit.

---

## Global Constraints

* **Platform Floor**: Windows 10/11 x64, .NET 8.0 SDK.
* **Semtech REST Un-Route**: Un-routing must send `POST /api/device/{rxMac}` with `{ "op": "leave", "subscription_type": "HDMI", "subscription_index": 0 }`.
* **Zero Network Socket Leaks**: Preview mirroring must reuse existing `WriteableBitmap` instances without opening duplicate IGMP multicast sockets for decoders.
* **Responsive Visual Hierarchy**: Video preview containers must maintain 16:9 widescreen proportions across all screen sizes.
* **Memory Integrity**: Synchronize `memory/active_context.md` and `memory/progress.md` upon completion.

---

## File Structure

```
src/AvasRoutingTool/
├── ViewModels/
│   ├── DisplayCardViewModel.cs       // Add PreviewBitmap, IsPreviewSupported, UnrouteCommand, Source mirroring
│   ├── LayoutItemViewModel.cs        // Support drag payload
│   └── MainViewModel.cs              // Wire source bitmap sync on routing changes
├── Views/
│   ├── DisplayCardView.xaml          // Add video preview surface, [ ⏻ Turn Off ] button, responsive styling
│   ├── DisplayCardView.xaml.cs       // Handle "Off" layout tile drop & unroute
│   ├── SourceCardView.xaml           // Fluid responsive sizing (MinWidth 380, MaxWidth 520, 16:9 video)
│   ├── LayoutsBarView.xaml           // Drag initiation for layout items
│   ├── LayoutsBarView.xaml.cs        // MouseDrag handler for layout tiles
│   └── VisualRoutingView.xaml        // Balanced responsive canvas layout on FullHD
tests/AvasRoutingTool.Tests/
└── DisplayCardViewModelTests.cs      // Unit tests for unrouting, mirroring, and non-AVAS states
```

---

### Task 1: Decoder Un-Route & "Off" Layout Drag-and-Drop

**Files:**
- Modify: `src/AvasRoutingTool/ViewModels/DisplayCardViewModel.cs`
- Modify: `src/AvasRoutingTool/Views/DisplayCardView.xaml`
- Modify: `src/AvasRoutingTool/Views/DisplayCardView.xaml.cs`
- Modify: `src/AvasRoutingTool/Views/LayoutsBarView.xaml`
- Modify: `src/AvasRoutingTool/Views/LayoutsBarView.xaml.cs`
- Create: `tests/AvasRoutingTool.Tests/DisplayCardViewModelTests.cs`

**Deliverables:**
- Red `[ ⏻ Turn Off / Disconnect ]` button visible when `IsRouted == true`, bound to `UnrouteCommand`.
- Dragging "Off" tile (`LayoutItemViewModel` with `Id == "off"`) onto a display card triggers un-routing via `Card_Drop`.
- Unit tests verifying `UnrouteCommand` execution and state clearing.

- [ ] **Step 1: Write failing unit test in `DisplayCardViewModelTests.cs`**
  - Verify `UnrouteCommand` invokes unroute action and resets `IsRouted`.
- [ ] **Step 2: Run `dotnet test` to confirm test failure**
- [ ] **Step 3: Update `DisplayCardViewModel.cs`**
  - Implement robust `UnrouteCommand` invocation.
- [ ] **Step 4: Update `LayoutsBarView.xaml` / `.xaml.cs`**
  - Add drag gesture initiation for layout items (`DragDrop.DoDragDrop(this, vm, DragDropEffects.Link)`).
- [ ] **Step 5: Update `DisplayCardView.xaml` / `.xaml.cs`**
  - Add `[ ⏻ Turn Off / Disconnect ]` action button.
  - In `Card_Drop`, handle `LayoutItemViewModel` payload: if `item.Id == "off"`, execute unroute.
- [ ] **Step 6: Run `dotnet test` to verify tests pass**
- [ ] **Step 7: Git commit task changes**

---

### Task 2: Live Video Preview Mirroring on Decoder Cards

**Files:**
- Modify: `src/AvasRoutingTool/ViewModels/DisplayCardViewModel.cs`
- Modify: `src/AvasRoutingTool/ViewModels/MainViewModel.cs`
- Modify: `src/AvasRoutingTool/Views/DisplayCardView.xaml`
- Modify: `tests/AvasRoutingTool.Tests/DisplayCardViewModelTests.cs`

**Deliverables:**
- `DisplayCardViewModel` has `PreviewBitmap`, `IsPreviewSupported`, and `RoutedSourceName`.
- In `MainViewModel.RouteVideoAsync`:
  - When `sourceTx` is routed to `displayRx`, attach `sourceVm.PreviewBitmap` to `displayVm.PreviewBitmap`.
  - If `sourceVm.IsPreviewSupported == false`, set `displayVm.IsPreviewSupported = false`.
- In `DisplayCardView.xaml`:
  - Add live preview container matching `SourceCardView` (with `<Image Source="{Binding PreviewBitmap}" .../>`).
  - If routed to non-AVAS (`!IsPreviewSupported`): show `"Routed to <Name>"`, subtext `"Preview Not Supported - Non-AVAS Device"`.
  - If un-routed: show `"Drop Source Here to Route"`.

- [ ] **Step 1: Write failing unit test for preview mirroring in `DisplayCardViewModelTests.cs`**
- [ ] **Step 2: Implement mirroring logic in `DisplayCardViewModel.cs` and `MainViewModel.cs`**
- [ ] **Step 3: Update `DisplayCardView.xaml` preview surface markup**
- [ ] **Step 4: Run `dotnet test` to confirm all tests pass**
- [ ] **Step 5: Git commit task changes**

---

### Task 3: Fluid Responsive Card Sizing & FullHD Layout Polish

**Files:**
- Modify: `src/AvasRoutingTool/Views/SourceCardView.xaml`
- Modify: `src/AvasRoutingTool/Views/DisplayCardView.xaml`
- Modify: `src/AvasRoutingTool/Views/VisualRoutingView.xaml`

**Deliverables:**
- Fluid card sizing: `MinWidth="380" MaxWidth="520" Height="295"`.
- 16:9 widescreen video surface: `Height="185"` with dark matte background (`#070A0F`).
- Centered / balanced distribution in `VisualRoutingView.xaml` using adaptive wrap layout and generous row heights (`MinHeight="330"`).
- Elimination of empty void space on 1920x1080 screens.

- [ ] **Step 1: Update `SourceCardView.xaml` sizing and video proportions**
- [ ] **Step 2: Update `DisplayCardView.xaml` sizing and video proportions**
- [ ] **Step 3: Update `VisualRoutingView.xaml` row definitions and item spacing**
- [ ] **Step 4: Run `dotnet test` to confirm clean compilation and test execution**
- [ ] **Step 5: Git commit task changes**

---

### Task 4: End-to-End Live Verification & Packaging

**Files:**
- Publish target: `publish/AvasRoutingTool/AvasRoutingTool.exe`
- Documentation / Memory: `memory/active_context.md`, `memory/progress.md`, `task.md`

**Deliverables:**
- Execute unit test suite (`dotnet test`).
- Compile and publish release standalone executable (`dotnet publish`).
- Run live test against active Control Server (`127.0.0.1:6970` / `8090`):
  - Verify live preview mirrors on `RX1_c059e_SFPA` when `TX2_DUAL LINK_SFPA` is routed.
  - Verify `[ ⏻ Turn Off / Disconnect ]` button un-routes the decoder.
  - Verify dragging "Off" tile un-routes the decoder.
  - Verify responsive cards span FullHD without cramped left-clustering.
- Synchronize memory files.

- [ ] **Step 1: Run `dotnet test`**
- [ ] **Step 2: Publish standalone executable**
- [ ] **Step 3: Run live hardware verification**
- [ ] **Step 4: Update memory files and commit**
