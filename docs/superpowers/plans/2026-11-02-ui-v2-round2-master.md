# UI v2 Round 2 Master Execution Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Implementers are Haiku 5.5 subagents dispatched by the two lane sessions; the master session gates and integrates.

**Goal:** Apply the round-2 design critique (M1-M8, C1-C8) to the Visual Routing main window and the MultiView Composer, as atomic, test-first commits, without touching `main`.

**Architecture:** Two parallel lanes in separate worktrees, each already holding a detailed finding-by-finding plan on its own branch: Lane A (Main Window, `ui-v2/main-window-round2`, plan R1-R9) and Lane B (Composer, `ui-v2/composer-round2`, plan R2-1..R2-10). Lanes touch disjoint files except the shared items listed in section "Ownership". The master merges lanes into `ui-v2/round2` at checkpoints, runs the full suite, and tags a revert point.

**Tech Stack:** .NET 8 WPF MVVM, WPF-UI 4.3.0, xUnit STA view tests, central ResourceDictionaries (`Resources/Tokens.xaml`, `Resources/Styles/*.xaml`, `Resources/Themes/Theme.*.xaml`).

**Specs:** `docs/superpowers/specs/2026-11-02-ui-v2-critique-round2.md` (findings), `docs/superpowers/specs/2026-10-29-ui-v2-master-guidelines.md` (D1-D12, guards). **Detailed per-finding plans (read by the lane before any task):** `docs/superpowers/plans/2026-11-02-ui-v2-main-window-round2.md` on branch `ui-v2/main-window-round2`; `docs/superpowers/plans/2026-11-02-ui-v2-composer-round2.md` on branch `ui-v2/composer-round2`. Section numbers below ("MW §2 M3") point into those files.

## Global Constraints

- Never merge to `main`, never push anything. Revert tags: `pre-ui-v2`, `ui-v2-checkpoint-1..3` exist; the master adds `ui-v2-round2-cp1` and `ui-v2-round2-cp2`.
- D1-D12 stay locked: WPF-UI theme and controls only; vector icons only (`StreamGeometry` / `ui:SymbolIcon`, ASCII `->` not glyph arrows); touch tokens 48 / compact 36; font floor 12 px (use `Font.Size.*` tokens); only the Logs list is virtualized (card grids and the source strip stay fully realized, zero socket leaks); 1920x1080 target with the 1024x720 floor tests green.
- Hard guards: `UiV2Guards` plus `UiV2Guards.allowlist.json`. No literal sizes or keyed styles in view files, no raw hex outside `Resources/Themes/`. The only allowlist change in this round is removing the Sources `RowDefinition` style entry (task A9).
- TDD: red test first, then the change. STA view tests for every view change. Zero socket leaks.
- Test command (use a lane-unique BaseOutputPath, never the default): `dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj -p:BaseOutputPath=bin/<lane>/ --filter "FullyQualifiedName!~LiveControlServerTests&FullyQualifiedName!~LiveFunctionTests" -- RunConfiguration.DisableParallelization=true`. Lane A uses `bin/r2a/`, Lane B `bin/r2b/`, master `bin/r2m/`. Known flake: an existing `MainWindow_*` STA test can hang; run with `--blame-hang-timeout 120s` and do not add `MainWindow` instances to new tests beyond one or two per class.
- Commits: atomic, one task per commit, subject `feat(ui-v2/<lane>): <what>`, body ends with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`. Update `memory/active_context.md` (gitignored, local) in the lane worktree's main checkout path notes only if the lane session says so; do not `git add` it.
- Subagent models: Haiku 5.5 implements; Sonnet or lower reviews. Never Opus.

## Decisions (approved by the user on 2026-11-02, binding on both lanes)

| ID | Decision |
|---|---|
| Q-A | App bar: Theme and Refresh icon-only at every width (tooltip + automation name); Settings keeps its label from 1400 px. Overrides the earlier D9 label rule for these two buttons only. |
| Q-B | Accent roles: blue = selection, primary action, on-screen; cyan = armed target only; amber = staged only; green = success only; red = errors only. Status is a dot plus a word, never a coloured pill. |
| Q-C | Composer Step 1: ghost schematic that follows the hovered/focused layout tile, nothing staged. Task R2-8 (remember last layout, touches `MainViewModel`/`AppConfig`) is **dropped** from this round. |
| Q-D | Composer steps 2 and 3 headings move (canvas status row; above the Take cluster), not dropped; the two instruction sentences become `AutomationProperties.HelpText`. |
| Q-E | Shared source tile = the Main Window `SourceCardView` re-skinned (type name kept). The Composer adopts it after A9 merges (task B9). `CompactSourceCardView` is retired only in B9. |

## Review Focus

Failure modes the findings imply that no single task's tests would otherwise exercise; each gets a test in the owning task.

1. Arming a source, arming a display for copy, and opening the drop-choice panel must not move any card (A1 test, three states, both sizes).
2. A source name 40+ characters long (place pill, source tile, display name plate) must truncate with a tooltip, never widen a layout (B4, A9, B5 tests).
3. Zero discovered sources and zero displays must still show a sensible empty state (A7 and A9 tests: empty strip, empty display list).
4. The Dark/Light toggle must repaint every new style (all new brushes `DynamicResource`; a Light-theme pass in each task's tests).
5. A colour-only state: every state added or changed keeps a word or icon beside colour (A3 on-screen check, A5 dot+word, B3 amber dot).

## Ownership (prevents lane collisions)

| Lane A owns | Lane B owns | Shared, edited only as stated |
|---|---|---|
| `VisualRoutingView.xaml`, `SourceCardView.xaml(.cs)`, `DisplayCardView.xaml(.cs)`, `MainWindow.xaml`, `MainViewModel.cs`, `Cards.xaml`, `Badges.xaml`, `Text.xaml`, `Buttons.xaml`, `Overlays.xaml`, `DisplayCard.xaml`, `SourceCard.xaml`, `Tokens.xaml`, `Resources/Themes/*`, `UiV2Guards.allowlist.json` | `MultiViewComposerView.xaml(.cs)`, `Composer.xaml`, `CompactSourceCardView.xaml(.cs)`, `LayoutSchematic.xaml`, `MultiViewComposerViewModel.cs`, Composer tests, `ViewSmokeTests.ComposerShots.cs` | `tests/.../ThemeResourceUsageTests.cs`: each lane only appends `InlineData` rows. Lane B consumes Lane A's new keys (`Style.Badge.Neutral`, `Style.Text.Status`, `Style.Button.Ghost`) only after task A4 is merged at checkpoint 1. Lane B adds no keys to `Tokens.xaml`: its sizes live in `Composer.xaml` as `Size.Composer.*`. |

## Execution model

Each lane session (`ui-v2-build-main` = `local_8c6e4071-a2bb-4608-a2ce-854f3428dcbe`, `ui-v2-build-composer` = `local_1b4ee9bc-a566-4a4d-b455-2e229f293013`) is the lane lead. It works on its round-2 branch, and for each task dispatches **one fresh Haiku subagent** (Agent tool, `model: "haiku"`, `subagent_type: "general-purpose"`, no worktree isolation: tasks in a lane are sequential on the lane branch) with the task card below, then reviews and runs the tests itself.

**Task card (what the lead sends to the Haiku subagent, verbatim structure):**
1. Branch and working directory; the BaseOutputPath to use.
2. The task's finding text copied from the lane plan section (not a pointer alone).
3. The exact files to change, the new test names with their assertions (copied from the lane plan), and the interfaces consumed and produced.
4. The steps: write the red tests; run them and confirm FAIL for the stated reason; implement; run focused tests PASS; run the full non-live suite PASS; commit with the message given.
5. Stop conditions: any test outside the task's list that goes red, any guard failure, any need to edit a file the lane does not own. The subagent stops and reports instead of working around it.

**Escalation:** if a Haiku subagent fails the same task twice, the lead re-dispatches that task once on Sonnet 5.5 and notes it in its report. Tasks that are XAML-layout-heavy (A3, A9, B5, B6) are the likely ones; the lead pre-writes the XAML skeleton in the task card for those.

**Lead gate per task (not delegated):** read the diff; confirm only owned files changed; confirm the red-then-green evidence in the subagent report; run the full suite once at the end of each task.

## Lane A tasks (Main Window; sequential; detail in MW §2 and §3)

| Task | Commit | Source |
|---|---|---|
| A1 | Nothing moves when armed: banner merged into the Displays header row (M1). New `ViewSmokeTests.UiV2NoShift.cs`. Must be first. | MW §2 M1 |
| A2 | Say it once: delete `ShowTapHint` chip; `SourceFooterText`; Copy-disabled reason to tooltip and HelpText (M4) | MW §2 M4 |
| A3 | Inline layout presets at card width >= 480; `InlineLayouts`, `Style.Button.LayoutPreset`, tokens `Size.Breakpoint.CardPresets`, `Size.Schematic.Inline.*` (M3) | MW §2 M3 |
| A4 | Palette and role keys: fix `Text.Muted` and `Status.Offline` contrast in both themes; add `Style.Badge.Neutral`, `Style.Text.Status`, `Style.Button.Ghost` (M5a) | MW §2 M5 R4, §0 |
| A5 | Apply one accent, status as dot plus word (M5b) | MW §2 M5 R5 |
| A6 | Section headers: sentence case, `Style.Text.Title`, explicit HeadingLevel2 (M6) | MW §2 M6 |
| A7 | Empty display tile: brand mark, "Ready to route a source", dashed neutral frame (M7) | MW §2 M7 |
| A8 | Top bar: `ConnectionHealth`, dot-only when connected, icon-only Theme/Refresh per Q-A (M8) | MW §2 M8 |
| A9 | Dense source strip: `SourceCardView` re-skin, WrapPanel strip, allowlist entry removed, FullHd baselines re-measured (M2) | MW §2 M2 |

**Checkpoint 1 = after A1-A8 and B1-B8 (see below).** A9 waits for checkpoint 1, so the strip is built on the merged palette keys and Lane B's tests do not move under it.

## Lane B tasks (Composer; detail in CP §2 and §3)

| Task | Commit | Source |
|---|---|---|
| B1 | Change lines read as words, `Block 1: empty -> TX1_c07bb_SFPA` (C5) | CP §2 C5 (R2-1) |
| B2 | Done placing becomes secondary; Take the only filled button (C2) | CP §2 C2 (R2-2) |
| B3 | One amber frame, neutral staged blocks, lighter armed overlay, neutral Undo (C3 part a) | CP §2 C3 (R2-3a) |
| B4 | Two-line truncating place pill with tooltip (C8) | CP §2 C8 (R2-4) |
| B5 | Right rail fills free height; display name plate; `DisplayName` property (C4) | CP §2 C4 (R2-5) |
| B6 | Neutral STAGED/CHANGES headings with amber dot; header chips as plain text (C3 part b) | CP §2 C3 (R2-3b) |
| B7 | Steps 2 and 3 move out of the left rail (C6), with the 1024x720 marker-only fallback if the real-size test goes red | CP §2 C6 (R2-6) |
| B8 | Ghost schematic on the empty Step 1 canvas (C1a), `PreviewLayoutId` DP | CP §2 C1 (R2-7) |
| B9 | Composer adopts the shared tile from A9; retire `CompactSourceCardView`; bind `UsedInText`/`HasUsage`. **Starts after A9 is merged.** | CP §2 C7, Q-E |
| B10 | Harness: refreshed 20 Composer screenshots plus before/after numbers (all) | CP §3 R2-10 |

Order inside Lane B: B1, B2, B4, B8 (any order), then B3, B6, B5, B7 (B6 after B3, B5 after B3, B7 after B5), then B10. B9 and a final B10 refresh after checkpoint 2.

## Master tasks

### Task M1: Checkpoint 1 merge (after A8 and B8 are reported green)

- [ ] **Step 1:** In the main checkout create `ui-v2/round2` from `ui-v2/integration` (tip `41e8cfe` or later). Merge `ui-v2/main-window-round2`, then `ui-v2/composer-round2`. Expected conflicts only in `ThemeResourceUsageTests.cs` (both append rows) and possibly `Composer.xaml` vs `Badges.xaml` none. Resolve by keeping both sides' rows.
- [ ] **Step 2:** Run the full non-live suite with `bin/r2m/`. Expected: 0 failed. Record the count in the lane notes.
- [ ] **Step 3:** `git tag ui-v2-round2-cp1`. Run the offscreen screenshot tests for both screens (`UIV2_SHOTS=ui_screenshots/UI-v2-round2/cp1`). Send the main-window and composer shots to the user with `SendUserFile` for review.
- [ ] **Step 4:** Message both leads: rebase their branches onto `ui-v2/round2` (fast-forward) before A9 and B9.

### Task M2: Checkpoint 2 merge (after A9, B9, B10)

- [ ] **Step 1:** Merge both lanes into `ui-v2/round2` again; full suite with `bin/r2m/`; expected 0 failed.
- [ ] **Step 2:** Measure at 1920x1080: Composer canvas >= 1144x635 and rails 320/360 (unchanged from checkpoint 3), displays row gain from the new strip (Lane A's A13 table). Record before/after in `ui_screenshots/UI-v2-round2/MEASUREMENTS.md`.
- [ ] **Step 3:** `git tag ui-v2-round2-cp2`; send the final shots to the user. `main` untouched. Ask the user before any merge into `main`.

### Task M3: Review gates

- [ ] After each checkpoint, dispatch one Sonnet subagent for an independent review of `git diff ui-v2/integration..ui-v2/round2`: standards (guards, tokens, D1-D12) and spec (the findings table). Findings go back to the owning lane as new tasks; none are fixed by the reviewer.

## Out of scope

Settings dialog and BAVM console (user instruction); `main` merge; pushing; live-hardware tests (not run); R2-8 (dropped); manual Narrator/touch checks (a person at the machine).

## Self-review notes

- Spec coverage: M1-M8 map to A1-A9 (M5 split A4/A5); C1-C8 map to B1-B8 plus C7 in B9. Dropped by decision: C1(b).
- Types and keys referenced across tasks match the lane plans: `SourceFooterText`, `InlineLayouts`, `ConnectionHealth`, `Style.Badge.Neutral`, `Style.Text.Status`, `Style.Button.Ghost`, `PreviewLayoutId`, `DisplayName`, `Size.Composer.*`.
- Proportion: this plan is about 1/3 the length of the two lane plans combined; the detail is in them by design, and each Haiku task card is built from the quoted lane section.
