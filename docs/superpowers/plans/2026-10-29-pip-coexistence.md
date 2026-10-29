# PiP Coexistence With Other Displays Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A PiP inset (1280x720 on a 3840x2160 canvas) stays usable while other displays show a different multiview layout on the same encoder (for example a 2x2), without exceeding bandwidth or overflowing the canvas.

**Architecture:** An encoder (TX) has one scaled stream (stream 1) and one native stream (stream 0). Stream 1 has one scaler size, which the tool sets to the window size. A second display that shows the same TX at another size therefore conflicts today, and `DraftValidator` refuses it. Task 1 is a hardware spike that decides whether the firmware can hold two sizes. Its result selects Task 2A (keep the size lock, size the inset to match) or Task 2B (allow the inset to differ).

**Tech Stack:** C# .NET 8 WPF, xUnit, `MockSdvoeClient` / `RecordingHandler` test seams, SDVoE REST (PDS-062489 3.9).

**Spec:** none written. Reference inputs: the two Blueriver web UI screenshots (1920x1080 layout, window 1 full-canvas on surface 5, window 0 inset 640x360 at 1204,644 on surface 16, Stream 1, framerate divider on); `Handover/04` section 4.2; `docs/superpowers/specs/2026-10-25-multiview-composer-ux.md` R16.

## Global Constraints

- One encoder has one stream-1 scaler size (`DraftValidator.cs:16-17`). Any change must keep that unless Task 1 proves the firmware accepts two.
- The PiP canvas rule from the approved change stays: 1280x720 inset on exactly 3840x2160; every other canvas keeps the quarter-width inset (`MultiViewPresets.cs:147-159`).
- Windows are 16:9; `MultiViewPresets.Validate` rules stay (minimum 8 px, forbidden widths 1366/1368/2732/2734/4098/4100, inside the canvas).
- Bandwidth guideline 9 Gbps (`MultiViewCanvas.cs:21`); the frame-rate divider logic stays as it is.
- REST bodies live only in `MultiViewRequests.cs`.

## Review Focus

1. A second display asks for the same TX at another size while a PiP is live: the operator gets a clear refusal, and the live display is not changed silently.
2. A Take on another display changes the TX scaler while this PiP is on screen: the PiP is not blanked, or the refusal says so before any call is sent.
3. A canvas too small for the inset (the EDID floor is 640x360): no exception, the fallback rule applies, and the card says which rule was used.
4. More than 11 standard windows: today the planner refuses (`SurfacePlanner.cs:61-63`), while the API accepts surface indexes 0-31 (`PDS-062489` section 5.2.6) and the Blueriver templates use 15 and 16. The refusal message must match what the hardware can do.
5. The bandwidth estimate counts window pixels per display (`MultiViewCanvas.cs:54-58`), but the encoder's real load is per TX stream. A layout can show "over bandwidth" while its real per-TX stream is under budget, or the reverse.

## Task 1: Lab spike (hardware gate, no code)

**Files:**
- Modify: `memory/active_context.md` (append the result)

**Interfaces:**
- Consumes: none
- Produces: a recorded answer to "can one TX feed two decoders at two stream-1 sizes?", which selects Task 2A or 2B

- [ ] **Step 1: Set up display A.** On RX1 take a PiP with TX2 as the inset at 1280x720 (stream 1), VEGA_1100 as the main.

- [ ] **Step 2: Set up display B.** On RX2 take a 2x2 that shows TX2 at 960x540 (stream 1). Confirm the scaler reads 960x540 on TX2 (`get` the SCALER node).

- [ ] **Step 3: Observe display A.** Record whether the PiP still shows video, and whether TX2's scaler is 1280x720 or 960x540 at that moment. Repeat with the take order reversed.

- [ ] **Step 4: Check stream 0.** On display B, subscribe TX2 stream 0 at 960x540 instead of stream 1. Record whether the window shows a scaled picture or a crop of the 3840x2160 frame.

- [ ] **Step 5: Record the result** in `memory/active_context.md` with the outcome in one of three forms:
  - Both displays keep video at their own sizes: Task 2B.
  - Display A blanks or changes size when display B is taken: Task 2A.
  - Stream 0 only crops: stream 0 is not a way around the size lock.

  Expected: a single dated entry with the three observations. Run by the user on the lab units; no automated test covers this.

## Task 2A: Size lock keeps the PiP usable (default if Task 1 shows conflicts)

**Files:**
- Modify: `src/AvasRoutingTool/Sdvoe/MultiView/MultiViewPresets.cs` (`Build` and `Pip`)
- Test: `tests/AvasRoutingTool.Tests/MultiViewPresetsTests.cs`
- Test: `tests/AvasRoutingTool.Tests/DraftValidatorTests.cs`

**Interfaces:**
- Consumes: `DraftValidator.Validate` (existing refusal for a TX used at another size)
- Produces: `MultiViewPresets.Build(string id, int canvasWidth, int canvasHeight, int? insetWidth = null)`; a null `insetWidth` keeps the current rule, a value sets the inset width (height follows 16:9)

- [ ] **Step 1: Write the failing test.** A PiP built with `insetWidth: 960` on 3840x2160 has an Inset of 960x540 at the bottom-right margin, with index order unchanged.

- [ ] **Step 2: Run it to verify it fails.** `dotnet test ... --filter "FullyQualifiedName~MultiViewPresetsTests"`. Expected: FAIL, the optional parameter does not exist.

- [ ] **Step 3: Implement** the optional `insetWidth` parameter in `Build` and `Pip`. Pass it into `Pip`; the 4K default stays 1280.

- [ ] **Step 4: Write the failing test for the validator.** With display B using TX2 at 960x540, a staged PiP inset of 1280x720 on TX2 is refused with `DraftValidator`'s existing message ("TX2 runs at 960x540 on RX2 - this block is 1280x720."). A 960x540 inset is accepted.

- [ ] **Step 5: Run it, make it pass,** and confirm the existing `DraftValidatorTests` still pass.

- [ ] **Step 6: Commit** with the memory update in the same commit.

## Task 2B: Allow a differing inset (only if Task 1 shows coexistence)

**Files:**
- Modify: `src/AvasRoutingTool/Sdvoe/MultiView/DraftValidator.cs` (`OtherDisplayConflict`)
- Test: `tests/AvasRoutingTool.Tests/DraftValidatorTests.cs`

**Interfaces:**
- Consumes: `OtherDisplayUse`, `WindowSlot` (existing)
- Produces: an exemption for PiP inset windows, expressed as a rule on the slot's role ("Inset"), not on a special case in the orchestrator

- [ ] **Step 1: Write the failing test.** An inset labelled "Inset" on TX2 at 1280x720 is accepted while another display uses TX2 at 960x540; a non-inset block at 1280x720 is still refused.

- [ ] **Step 2: Run it to verify it fails.**

- [ ] **Step 3: Implement** the inset exemption in `OtherDisplayConflict`, with a comment that cites the Task 1 result.

- [ ] **Step 4: Run the test to pass,** then the full non-live suite.

- [ ] **Step 5: Commit** with the memory update.

## Task 3: Surface ceiling (optional, independent)

**Files:**
- Modify: `src/AvasRoutingTool/Sdvoe/MultiView/SurfacePlanner.cs` (`StandardSurfaces`, the error message)
- Test: `tests/AvasRoutingTool.Tests/SurfacePlannerTests.cs`

**Interfaces:**
- Consumes: `SurfacePlanner.Plan(MultiViewDraft, string)` (existing)
- Produces: the same signature; the ceiling changes from 11 to the value the lab confirms

- [ ] **Step 1: Write the failing test.** A layout with 12 standard-size encoders plans without throwing once the ceiling is raised.

- [ ] **Step 2: Run it to verify it fails.**

- [ ] **Step 3: Implement** the ceiling change only after the lab confirms that surfaces above 11 play. Do not raise it on the document alone.

- [ ] **Step 4: Run the test to pass,** then the full suite. Commit with memory.

## Task 4: Verification and records

**Files:**
- Modify: `memory/active_context.md`, `memory/progress.md`, `Handover/04_VERIFIED_FEATURES_AND_KNOWN_LIMITATIONS.md` (only where it describes the size rule)

- [ ] **Step 1: Run the full non-live suite** with the command in `CLAUDE.md`'s memory notes. Expected: all pass, count recorded.
- [ ] **Step 2: Record** the Task 1 result and the chosen branch in the memory files.
- [ ] **Step 3: Commit** the docs with the code they describe.
