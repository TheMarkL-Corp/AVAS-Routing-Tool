# Phase 2 MultiView Layouts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Let an operator drag a MultiView layout tile (`Matrix 2x2`, `PIP`, `Side-by-Side`, `1+3 Quad`) from the bottom bar onto a decoder card, turning its single drop surface into per-window drop zones. Dropping a source on a zone places it in that window on the real decoder, and the card shows a composited live preview of every window.

**Source of truth for the hardware API:** `PDS-062489` SDVoE Developers API Reference 3.9.0.0, §5.2 pp.146-166 (extracted and verified; the PiP/Quad examples are pp.166-169). Layer: L2 (chipset/Control Server). Whether the AVAS-223 firmware honours every call is L4 and **must be validated on the lab units** (see "Hardware validation gate").

## Architecture

1. **`MultiViewLayoutPlan` (pure, no WPF/HTTP)** - `MultiViewPresets.Build(id, canvasW, canvasH)` returns a name, canvas size and an ordered list of `WindowSlot`s (API index, pixel rect, normalized rect for the UI, target surface). Validates API limits (even coordinates, width >= 8, forbidden tile widths, <= 32 windows, surface fits).
2. **`MultiViewRequests` (pure)** - builds the exact REST paths and JSON bodies from the reference: `layout create`, `layout window create`, `set:scaler`, `start` (stream 1), `set:multiview` with `subscriptions`, `set:video GENLOCKED`.
3. **`ISdvoeClient` intent-level methods** - `CreateMultiViewLayoutAsync`, `PrepareSourceScalerAsync`, `SetMultiViewAsync`, `SetGenlockedAsync`. `SdvoeClient` maps them onto the builders. Test seam: optional `HttpMessageHandler` constructor argument so REST sequencing is unit-tested without hardware.
4. **`MultiViewOrchestrator`** - owns the sequence for one decoder: scale + start each assigned TX stream 1, (re)create the layout, `set:multiview` with all current subscriptions, and the reverse path (back to Single View / Off).
5. **`DisplayCardViewModel` slots** - `LayoutId`, `IsMultiView`, `Slots` (`WindowSlotViewModel`: rect, source name, mirrored `PreviewBitmap`, drag-over state). Reuses the zero-socket bitmap mirroring from M8.
6. **`DisplayCardView`** - when `IsMultiView`, an `ItemsControl` on a `FractionalLayoutPanel` places one drop zone + preview per slot; single-view surface stays for `single`.
7. **`MainViewModel`** - `ApplyLayoutAsync(layoutId, display)` (UI state only, no hardware call until the first source lands), `AssignSlotAsync(display, slot, source)`, layout drop routing in `Card_Drop`.

## Design decisions (record in ADR 003)

* **Lazy hardware apply.** Dropping a layout tile only prepares empty drop zones. The first source drop performs the full apply. No black-screen flash and no `set:multiview` with an empty `subscriptions` array (the API rejects empty lists, p.165).
* **Stream 1 only.** Multiview windows are fed by TX scaled stream 1; stream 0 is left running so decoders in Single View keep working. The reference examples stop stream 0 to save bandwidth; we do not, because that would drop other decoders' feeds.
* **Window size = TX scaler size.** The reference examples set each TX scaler to its window size (`set:scaler`), so one TX has one stream-1 size. The same TX in two windows of different sizes, or on two decoders with different window sizes, is rejected with a clear message (`MultiViewConflict`).
* **Subscription index = target surface.** Matches every example in the reference (pp.167-169).
* **Surface allocation.** A window larger than 2048x1080 takes surface 0; everything else takes surfaces 1..11. Surfaces 17-20 are not used because they alias surface 0 (p.166).
* **Canvas = decoder output = the display's EDID (amended after hardware validation).** `get edid` on the RX, preferred progressive timing <= 4K, fallback 1920x1080@60. See ADR 003 amendment, which also adds request-result polling and the stream-1 frame divider. Layout names encode the size: `avas_<preset>_<W>x<H>` so presets never clash with the read-only `compatibility_4k_2x2`.
* **Z-order.** Higher API window index is drawn behind (reference PiP: background is window 1, inset is window 0). Slots are exposed to the UI in paint order (back to front).
* **Return to Single View.** `set:video` `GENLOCKED` (p.119) then the normal `join`.
* **Layout IDs.** Keep the existing code IDs (`single`, `matrix2x2`, `pip`, `side_by_side`, `quad_1_3`). The Handover doc lists different spellings (`sideBySide`, `quad1Plus3`); the code wins.

## Hardware validation gate (needs the user's go-ahead; these mutate live decoders)

1. RX capability: `capabilities.scaler` / `video_processing` true on `RX1_c059e_SFPA` and `RX2_DUAL LINK_SFPA` (read-only `GET`).
2. `layout create` + `layout window` + `set:multiview` on `RX1` with two AVAS TX; confirm picture, and that the window picture is 1:1 at the scaler size.
3. Stream 1 scaler vs the in-band MCU preview (`set rtp ON`): confirm enabling stream 1 does not disturb the app's own preview, and vice versa.
4. Leave multiview: `set:video GENLOCKED` + `join` restores Single View; `Off` works from multiview.
5. Dual Link TX/RX in multiview: unknown in the sources (`vendor-dependent`, `pending-validation`).
6. Non-AVAS source (VEGA_1100) as a window source.

## File structure

```
src/AvasRoutingTool/
├── Sdvoe/MultiView/
│   ├── WindowSlot.cs                 // record: index, pixel rect, normalized rect, surface
│   ├── MultiViewLayoutPlan.cs        // name, canvas, slots, validation
│   ├── MultiViewPresets.cs           // matrix2x2, pip, side_by_side, quad_1_3
│   ├── MultiViewRequests.cs          // REST path + body builders
│   └── MultiViewOrchestrator.cs      // apply / leave sequencing
├── Sdvoe/ISdvoeClient.cs, SdvoeClient.cs   // intent-level multiview methods
├── ViewModels/WindowSlotViewModel.cs
├── ViewModels/DisplayCardViewModel.cs      // LayoutId, IsMultiView, Slots
├── ViewModels/MainViewModel.cs             // ApplyLayoutAsync, AssignSlotAsync
├── Controls/FractionalLayoutPanel.cs       // places children by normalized rect
└── Views/DisplayCardView.xaml(.cs)         // slot drop zones + composite preview
tests/AvasRoutingTool.Tests/
├── MultiViewPresetsTests.cs
├── MultiViewRequestsTests.cs
├── SdvoeClientMultiViewTests.cs
├── MultiViewOrchestratorTests.cs
├── DisplayCardMultiViewTests.cs
├── FractionalLayoutPanelTests.cs
└── ViewSmokeTests.cs                        // multiview card smoke
```

## Tasks

### Task 1: Layout geometry (pure)
- [x] Failing tests: each preset returns the right slot count, rects tile/overlap as designed, all coordinates even, widths >= 8, no forbidden widths, surfaces valid, PiP inset is in front, names encode canvas.
- [x] Implement `WindowSlot`, `MultiViewLayoutPlan`, `MultiViewPresets`.
- [x] Commit.

### Task 2: REST request builders (pure)
- [x] Failing tests asserting paths/bodies match the reference examples (pp.147, 160-161, 164-167).
- [x] Implement `MultiViewRequests`.
- [x] Commit.

### Task 3: Client methods
- [x] Failing tests with a recording `HttpMessageHandler`: order and bodies for create-layout, prepare-scaler, set-multiview, set-genlocked; failure short-circuits.
- [x] Add methods to `ISdvoeClient`, implement in `SdvoeClient`, extend `MockSdvoeClient`.
- [x] Commit.

### Task 4: Orchestrator
- [x] Failing tests: first assign performs full sequence; later assigns resend `set:multiview` with all subscriptions; scaler conflict is rejected before any hardware call; back to single view; off.
- [x] Implement `MultiViewOrchestrator`; wire into `RoutingOrchestrator` or `MainViewModel`.
- [x] Commit.

### Task 5: View-model state
- [x] Failing tests: `ApplyLayout` creates empty slots; `AssignSlot` mirrors the source bitmap and name; non-AVAS source shows fallback; `single` clears slots; unroute clears all.
- [x] Implement `WindowSlotViewModel` and `DisplayCardViewModel` changes; `MainViewModel.ApplyLayoutAsync/AssignSlotAsync`; layout tiles become available (`IsPhase1Supported` renamed `IsAvailable`, "READY" badge).
- [x] Commit.

### Task 6: UI
- [x] `FractionalLayoutPanel` + tests; `DisplayCardView` multiview surface; drop handling per slot; layout tile drop on the card; `ViewSmokeTests` for a multiview card.
- [x] Commit.

### Task 7: Publish, memory, hardware gate
- [x] `dotnet test` (218 non-live), update `memory/`, ADR 003 (+ amendment), Handover docs. `dotnet publish` still to do before the commit.
- [x] Hardware validation gate run with the user's approval (items 1-4 and 6 done; item 5 dual link and 4K PiP / four-window layouts need more devices online).
