# 05. Next Steps & Phase 2 Roadmap

This document outlines the immediate implementation roadmap for Claude Code to continue development of the **AVAS Routing Tool**.

---

## 1. High-Priority Next Tasks

### Task 1: Phase 2 MultiView Canvas Layouts (Priority: High)
> **Status (2026-10-25): code-complete and unit-tested, hardware validation pending.** See [`docs/superpowers/plans/2026-10-25-phase2-multiview-layouts.md`](../docs/superpowers/plans/2026-10-25-phase2-multiview-layouts.md), [ADR 003](../memory/decisions/2026-10-25-phase2-multiview-layouts.md) and `memory/active_context.md`. What remains is the hardware validation gate in that plan, then publish/commit. The requirements below describe what was built; ids in code are `matrix2x2`, `pip`, `side_by_side`, `quad_1_3`.

* **Context**: Each display card's Layout menu (`DisplayCardView.xaml`) has tiles for:
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

> **MultiView Composer (2026-10-27): implemented and test-verified, NOT hardware-validated.** The Composer (stage-then-Take, Undo, Layout menu, Copy layout, keyboard path) was built on branch `feat/multiview-composer` (worktree `AVAS-Routing-Tool-composer`, last code commit `e07bfa4`) on top of the layouts above. Spec: [`docs/superpowers/specs/2026-10-25-multiview-composer-ux.md`](../docs/superpowers/specs/2026-10-25-multiview-composer-ux.md); plan: [`docs/superpowers/plans/2026-10-25-multiview-composer.md`](../docs/superpowers/plans/2026-10-25-multiview-composer.md). The branch is **not merged** and the lab validation (plan Task 12 steps 1-2) was **not run**: the product owner skipped it. Known limitations and follow-ups: [04](./04_VERIFIED_FEATURES_AND_KNOWN_LIMITATIONS.md) section 4. How to run the validation later: section 3 below.

---

### Task 2: Route & Layout Restore from Hardware (Priority: Medium) - PLANNED
> Replaces the earlier `routes.json` idea. The Control Server *does* report state: `get settings` returns each HDMI subscription's `state` and `source` plus `FRAME_BUFFER.display_mode`; `get multiview` returns the loaded layout. Hardware is the source of truth (a local file would go stale when someone routes in the BAVM console). Full plan, including MultiView settings: [`docs/superpowers/plans/2026-10-25-route-restore-and-multiview-settings.md`](../docs/superpowers/plans/2026-10-25-route-restore-and-multiview-settings.md).

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

---

## 3. MultiView Composer: how to run the lab validation later

Prerequisites: merge decision for `feat/multiview-composer` is the product owner's (not merged yet); the lab needs RX1 (4K) and PAX355S (4K) online with TX2 (AVAS) and VEGA_1100 (non-AVAS) as sources, and someone at the screens to confirm pictures. Never run the `Live*` test classes: they route real hardware.

1. **Automated check first** (no hardware changes): `dotnet test AvasRoutingTool.sln --filter "FullyQualifiedName!~LiveControlServerTests&FullyQualifiedName!~LiveFunctionTests"` (701 tests). A bare `dotnet test` or the older filter `FullyQualifiedName!~Live` is wrong: the latter silently skips real tests whose names contain "Live".
2. **Build a Release publish**: `dotnet publish src/AvasRoutingTool/AvasRoutingTool.csproj -c Release -o publish/AvasRoutingTool` (into the worktree or a scratch folder, not over the operators' installed app).
3. **Task12Harness** (idea carried over from Antigravity's scratch work; it is not in the repository): an interactive console that references the Release publish output `AvasRoutingTool.dll` (rebuild it after every publish, otherwise it uses a stale dll), prints each planned hardware change and asks **y/N before every change**, discovers RX1, PAX355S, TX2 and VEGA read-only, and has a restore option that returns the lab to baseline (genlock RX1 and join TX2 stream 0, stop TX2 stream 1, set the divider off, genlock and leave PAX355S). A copy lived in `C:\Users\POC-615\.gemini\antigravity\brain\8253d2b1-5561-4b79-8db0-ed3a04fe8b76\scratch\Task12Harness`. Someone must drive the console interactively; an agent cannot (no stdin).
4. **Steps to run** (plan Task 12 steps 1-2), the operator confirming each picture:
   - RX1 Matrix 2x2 with TX2 in blocks 1 and 3 and VEGA in block 2, **Take**, confirm; **Undo**.
   - RX1 change to PiP with TX2 in the main block: TX2 must be unavailable for the inset with the spec reason.
   - RX1 4K 1+3 with Take.
   - Disconnect a source while staged: Take must be blocked.
   - Copy RX1 (4K Matrix) onto PAX355S (4K): same picture; **Undo** returns PAX355S to its previous state; copy a Single View RX1 onto PAX355S.
   - Restore the lab with the harness restore option.
5. **Watch specifically for** the unverified assumptions in [04](./04_VERIFIED_FEATURES_AND_KNOWN_LIMITATIONS.md) section 4.2: R13 (copy guard vs the real output format of a Single View source), R16 (stream index of a copied external layout), flicker from the per-Take scaler re-send, and Off after a Take. Then record the result in the spec's "Validation status" notes and in `memory/`.

### Follow-ups after the validation (from the build ledger)

- TX egress budget guard in the BAVM style (stream 0 + stream 1 model).
- Read YCbCr 4:2:0 video data blocks and HDMI_VIC modes from the EDID.
- Composer Undo after non-copy output-format changes.
- Drag auto-scroll to second-row display targets at 1008x615.
- Real mouse, touch, screen reader, held-key and Alt-Tab checks; ghost at DPI other than 100 %.
- NO SIGNAL chip on blocks; kept-draft "changed elsewhere" notice after Route or restore.
- Tokenise the ~100 raw hex colours; enlarge Turn Off and the source-card Refresh/Start/Stop buttons.
- Remove dead code (`FirstEmptySlotIndex`, `ClearSlot`, `WindowSlot.TargetSurface`, `MultiViewPresets.AllocateSurfaces`) together with the tests that use it; de-duplicate the layout titles.
- Move the EDID read out of the global take gate (up to ~13 s) and make `RestoreDisplayAsync` awaited or observed.
