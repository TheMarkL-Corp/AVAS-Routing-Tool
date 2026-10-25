# 05. Next Steps & Phase 2 Roadmap

This document outlines the immediate implementation roadmap for Claude Code to continue development of the **AVAS Routing Tool**.

---

## 1. High-Priority Next Tasks

### Task 1: Phase 2 MultiView Canvas Layouts (Priority: High)
> **Status (2026-10-25): code-complete and unit-tested, hardware validation pending.** See [`docs/superpowers/plans/2026-10-25-phase2-multiview-layouts.md`](../docs/superpowers/plans/2026-10-25-phase2-multiview-layouts.md), [ADR 003](../memory/decisions/2026-10-25-phase2-multiview-layouts.md) and `memory/active_context.md`. What remains is the hardware validation gate in that plan, then publish/commit. The requirements below describe what was built; ids in code are `matrix2x2`, `pip`, `side_by_side`, `quad_1_3`.

* **Context**: The bottom bar ([`LayoutsBarView.xaml`](../src/AvasRoutingTool/Views/LayoutsBarView.xaml)) has tiles for:
  * `Matrix 2x2` (`matrix2x2`): 4 equal quadrant windows.
  * `PIP` (`pip`): 1 full-screen primary window with 1 picture-in-picture overlay inset.
  * `Side-by-Side` (`side_by_side`): Split-screen dual windows (50% / 50%).
  * `1+3 Quad` (`quad_1_3`): 1 large surgical focus window + 3 thumbnail column windows.
* **Requirements**:
  1. **Multi-Window Drop Targets on Decoders**:
     * When a MultiView layout is selected on a display card, transform the single drop-target surface into multiple sub-window drop zones (e.g., Quadrant 1, Quadrant 2, Quadrant 3, Quadrant 4).
     * Users can drag different sources into specific window quadrants.
  2. **MultiView Preview Mirroring**:
     * Render a composited preview surface on the decoder card mirroring all active source feeds simultaneously.
  3. **BlueRiver Multiview Subscriptions**:
     * Dispatch Semtech BlueRiver multiview layout configuration commands to position hardware scaler windows.

---

### Task 2: Route State Persistence (Priority: Medium)
* **Context**: The BlueRiver Control Server does not expose an endpoint to query existing subscriptions upon startup.
* **Requirements**:
  1. Store active routes in a lightweight local JSON file (`publish/AvasRoutingTool/routes.json` or within `appsettings.json`).
  2. Format:
     ```json
     {
       "routes": {
         "74fe483c059e": { "source_mac": "74fe488b07bb", "source_name": "TX2_DUAL LINK_SFPA", "layout": "single" }
       }
     }
     ```
  3. On startup, match persisted MACs with discovered online devices and restore visual routing states and preview mirroring.

---

### Task 3: Independent Audio & USB Breakout Routing (Priority: Medium)
* **Context**: Current implementation performs automated 3-in-1 routing (Video + Audio + USB to the same source).
* **Requirements**:
  1. Add an advanced routing modal or context menu on display cards allowing independent routing:
     * Video: Route to `TX1_c07bc_SFPA`
     * Audio: Route to `TX2_DUAL LINK_SFPA`
     * USB HID: Route to `TX1_c07bc_SFPA`
  2. Implement companion `join` operations for `subscription_type: "AUDIO"` and `subscription_type: "USB"`.

---

### Task 4: In-Band Baseboard MCU PTZ / Camera Controls (Priority: Low / Optional)
* **Context**: Proprietary surgical cameras connected to AVAS transmitters support remote control via Baseboard MCU UART.
* **Requirements**:
  1. Add a collapsible control tray on `SourceCardView` for AVAS transmitters (`VendorId == 105`).
  2. Dispatch camera commands via the existing in-band UART channel:
     * Freeze Frame (`set freeze ON / OFF`)
     * Pan / Tilt / Zoom adjustments
     * Color profile / White balance presets

---

## 2. Recommended Development Workflow for Claude Code

When starting your session with Claude Code, follow this exact lifecycle:

1. **Read Active Memory**:
   * Inspect [`memory/active_context.md`](../memory/active_context.md) to understand current operational status.
   * Review [`Handover/README.md`](./README.md) and [`Handover/01_ARCHITECTURE_AND_DESIGN.md`](./01_ARCHITECTURE_AND_DESIGN.md).
2. **Draft Implementation Plan**:
   * Create an actionable plan in `docs/superpowers/plans/YYYY-MM-DD-<feature-name>.md`.
3. **Practice Strict Test-Driven Development (TDD)**:
   * Write failing tests before writing production code.
   * Run `dotnet test AvasRoutingTool.sln` to watch tests fail (RED).
   * Implement minimal production code to pass (GREEN).
   * Refactor cleanly while maintaining green tests.
4. **STA Thread Testing for Views**:
   * When touching any XAML markup or bindings, add or update tests in [`ViewSmokeTests.cs`](../tests/AvasRoutingTool.Tests/ViewSmokeTests.cs) to prevent runtime binding exceptions.
5. **Publish & Verify**:
   * Run `dotnet publish src/AvasRoutingTool/AvasRoutingTool.csproj -c Release -o publish/AvasRoutingTool`.
6. **Update Memory & Commit**:
   * Update [`memory/active_context.md`](../memory/active_context.md) and [`memory/progress.md`](../memory/progress.md).
   * Commit atomically to git.
