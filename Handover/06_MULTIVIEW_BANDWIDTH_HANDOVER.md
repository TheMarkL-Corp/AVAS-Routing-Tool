# 06. Multiview Bandwidth: Study, Spec and Merge Handover

Handover for the work on branch `claude/multiview-bandwidth-optimization-20d30b`. It tells the next engineer what exists, what does not, how to merge it with the new composer UI (`claude/ui-round3-6710d5`), and what is still unproven. Written 2026-11-08. Nothing in this branch has been pushed, and no pull request is open.

---

## 1. One-paragraph summary

Two multiview layouts on different displays could push one transmitter (TX) or one receiver (RX) past its 10G link, and every window on it went fuzzy. We measured it on the lab Control Server, built a bandwidth model that matches the live readings within about 2 %, and shipped a **warn-only** check (log line, never blocks). We then wrote a UX spec so the composer prevents the overflow instead of letting users hit it. **The spec is not implemented.** Mark has said the multiview works well, and implementation waits for QA on `claude/ui-round3-6710d5` to finish.

## 2. What is on this branch

| Item | Where | Status |
|---|---|---|
| Study: hypotheses, live inventory, model, 9+ live tests, Boom case, side-by-side and 1+3, BAVM output-format selector, lower-canvas test, evidence gaps | [`docs/studies/2026-11-07-multiview-bandwidth-study.md`](../docs/studies/2026-11-07-multiview-bandwidth-study.md) | Committed |
| Model as a runnable script (efficiency 0.93, limit 9.0 Gbps, compression 1.5:1) | [`docs/studies/multiview_bandwidth_model.py`](../docs/studies/multiview_bandwidth_model.py) | Committed |
| UX spec: ledger, canvas ladder, apply gate, health check, details panel, rollout, test matrix, merge-readiness (§10) | [`docs/studies/2026-11-07-multiview-composer-ux-spec.md`](../docs/studies/2026-11-07-multiview-composer-ux-spec.md) | Committed, draft 2 plus later edits |
| Warn-only pre-flight (commit `fcf793c`) | `Sdvoe/MultiView/MultiViewBandwidthBudget.cs`, `SourceVideoReader.cs`, `ISdvoeClient.ReadSourceVideoAsync`, `MultiViewOrchestrator.WarnIfOverBudgetAsync` | Committed, tests added, log only |

The only runtime change is the warning. `WarnIfOverBudgetAsync` is called once per Save & Apply, wraps everything in try/catch, and cannot change what is applied. Its `Describe` text reads, for example: "<device>: transmitter sends ~9.3 Gbps is over the 9 Gbps limit; pictures may break up."

**Not implemented, by decision:** the ledger across all displays, the apply gate, the canvas ladder, the post-apply health check, the engineer Details panel, the `MultiView.BandwidthGuard` setting. They are specified in the UX spec §4 and §6 and nowhere in code.

## 3. Findings the next engineer must know

1. **Wire rate** = `pixels × fps × bits per pixel ÷ 0.93`. Stream 1 (scaled) is uncompressed 24 bpp. Stream 0 (native) is auto-compressed about 1.5:1 when over about 9 Gbps. Limit is 9 Gbps per TX and per RX.
2. **The reported failure is the Boom case**: a TX whose own stream 0 plus a scaled stream 1 totals 9.30 Gbps. Stepping the canvas down cannot fix that. Only a different source or stream choice can.
3. **Lower canvas works**: the same layout on a smaller receiver canvas measured 0.78 Gbps scaled traffic against 3.13 Gbps (about 4x less) and looked clean. This is the main lever for the spec's ladder (display preferred, then 1080p, then 720p).
4. **Duplicate rule**: one receiver refuses the same (source, stream) in two windows ("Source stream is duplicated"). A full four-window 1+3 therefore needs four distinct streams. This lab has three TXs, so a real four-window 1+3 was **not** run (see study).
5. **Window size must equal scaler size**, except a PiP inset on UI v2, where the decoder scales the surface. Never infer a stream's size from its window; read the TX scaler (spec §10.3).
6. **netstat** is async: `{"op":"netstat","option":"select","select_list":["ALL"]}`, then `read`, poll `/api/request/{id}`, units bytes/s. `clear` resets counters.

## 4. Merge with `claude/ui-round3-6710d5`

State checked 2026-11-08 with a read-only `git merge-tree` dry run:

- **Result: clean, no conflicts.** UI branch tip at check time: `42d320e`. Merge base: `46d546b`.
- Files changed on both sides: `MultiViewOrchestrator.cs` (this branch +33 lines, UI branch 3 lines, different regions) and `SdvoeClient.cs` (this branch +6, UI branch 2, different regions).
- This branch adds `SourceVideoReader.cs`, `MultiViewBandwidthBudget.cs` and two test files; the UI branch does not touch them.
- The UI branch rewrites `MultiViewComposerView.xaml`; this branch does not touch it.

### Re-check before every merge step

The UI branch is still moving (QA fixes land on it). Run:

```bash
git fetch origin claude/ui-round3-6710d5
git merge-tree --write-tree HEAD origin/claude/ui-round3-6710d5 >/dev/null && echo CLEAN || echo CONFLICTS
git diff --name-only HEAD...origin/claude/ui-round3-6710d5 | sort > /tmp/ui.txt
git diff --name-only $(git merge-base HEAD origin/claude/ui-round3-6710d5) HEAD | sort | comm -12 - /tmp/ui.txt
```

The last command lists the files both branches changed. If `MultiViewOrchestrator.cs` ever shows overlapping hunks, resolve by hand and keep both.

### Recommended merge order

1. **Wait for QA sign-off** on the Main UI and the composer. Do not edit `MultiViewComposerView.xaml` before then.
2. **Merge this branch into the UI branch (or the integration branch built from it)**: docs, model, warn-only code. Behaviour change is one log line per Save & Apply.
3. Build and run the safe tests (section 6). Fix nothing in the UI during this step.
4. **Later, as separate work**: implement spec step 1 (ledger plus visible warning, guard Off), then the health check, then Block. Follow spec §6 and §10.4.

Alternative if the team prefers the UI branch to merge to `main` first: this branch rebases cleanly in practice because overlap is additive; re-run the dry run, then merge or cherry-pick `a6249a3`, `fcf793c`, `5d33311`, `b01c3e8`, `c0a1938` plus the handover commit.

### 4.1 Trial merge branch: `claude/merge-ui-rtsp-multiview-eb5f44` (checked 2026-11-08)

Worktree: `.claude/worktrees/merge-ui-rtsp-multiview-eb5f44`, tip `4937fd4` ("Merge ... into trial/merge-check"). It merges, in order: `claude/ui-round3-6710d5` (3906e84), `claude/rtsp-source-352452`, then this branch.

| Check | Result |
|---|---|
| Commits `a6249a3`, `fcf793c`, `5d33311`, `b01c3e8`, `c0a1938`, `01e13c5` are ancestors of the tip | Yes (`git merge-base --is-ancestor`) |
| `claude/ui-round3-6710d5` and the RTSP branch are ancestors of the tip | Yes |
| Worktree clean, no conflict markers in `MultiViewOrchestrator.cs`, `SdvoeClient.cs` | Yes |
| Warn-only check present (`WarnIfOverBudgetAsync`, called at line 861) | Yes |
| Safe tests in that worktree (2026-11-08) | Full run: 2098 passed, **10 failed**, 2108 total, none in multiview or bandwidth code: `RoomCameraViewModelTests` x5, `RoomCamerasTabTests` x2, `ViscaTcpTransportTests` / `ViscaUdpTransportTests` handle-growth x2, `ViewSmokeTests.Overview_NothingMoves_WhenTheDropChoiceOpens`. Run in isolation, the same 10 tests give 4 failures (`RoomCameraViewModelTests` LinkDrop_DuringMove_SendsStop, LinkStateChange_RaisesPropertyChanged_ForLinkTextAndIsGood, Poll_RunsOnceEvery5s_AndNeverOverlaps, and the Overview smoke test) **with and without this branch** (checked against `ce54688`, the merge before this branch), so those 4 come from the ui-round3 / RTSP side. The other 6 passed in isolation and failed only in the full run; whether they also fail in a full run without this branch was **not** checked. An earlier full run in this worktree hung for over an hour with no output; a rerun with `--blame-hang-timeout 180s` finished in 5.5 minutes |
| Composer logic against this spec | Reviewed: spec §11. Layout, EDID and duplicate-stream rules agree; the ledger, ladder, gate and health check are absent; the frame-divider math differs from the model (§11.3) |

Documents added by this handover (`01e13c5` and later) are on this branch's history, so a merge of this branch carries them. If the trial branch was made before a later docs commit, merge this branch again; it only adds files under `docs/studies/` and `Handover/`.

### 4.2 Hand-off checklist for whoever completes the merge
1. Re-run the dry run and the safe tests (section 6) on the final integration branch after QA sign-off.
2. Confirm in the app, not just in code: apply a layout on a 4K display and on a 1080p display and check the log line `'<display>' output: ...` shows the EDID canvas.
3. Apply a layout that puts one source on two displays at different sizes and confirm the size-conflict refusal text uses "Block N" and the display name.
4. Do not start spec step 1 until QA signs off. Start with spec §11.3 (math fixes, test-first), guard Off.
5. Keep `docs/studies/` and this file updated as tests close the open items in section 7.

## 5. Where each spec piece lands in the new window

Hooks exist on `claude/ui-round3-6710d5` (verified by reading code; **not seen running**):

| Spec item | Hook |
|---|---|
| Canvas ladder result and reason | header `OutputChip` / `RateChip` |
| Per-source issue or lock reason | block `IssueBand` + `IssueText`; `UnavailableReason` |
| Won't-fit refusal and fixes | `TakeBlockedReason` (shown by `TakeBlockedReasonText`) plus pinned `BannerHost` / `BannerText` (`BannerKind`) |
| Tight (amber) note, health-check result, Revert | pinned banner, `ON SCREEN NOW` card |
| Engineer Details, "Apply anyway" | new collapsed section in the right rail (the only new control) |
| Layout disabled with reason | `LayoutTile` tooltip; also cover the new `pip_large` preset |

Wording: the action is **Save & Apply**, blocks are "Block N", never "window" or "Take" in user-facing text.

## 6. Verification checklist after merging

- [ ] `dotnet build AvasRoutingTool.sln` succeeds.
- [ ] Safe tests pass: `dotnet test AvasRoutingTool.sln --filter "FullyQualifiedName!~LiveControlServerTests&FullyQualifiedName!~LiveFunctionTests"` (add `--artifacts-path <scratch dir>` if the app is running and locks Debug output). Expect 812 on this branch (README.md); a custom `--artifacts-path` makes `ReleaseDefaultsTests` fail and crashes the test host, so use it only when the app locks Debug output.
- [ ] With the lab Control Server up, apply a two-display layout and confirm the log shows a bandwidth warning only when a device really is over 9 Gbps (use the Boom TX, or run the model script for expected numbers).
- [ ] Confirm Save & Apply behaviour is unchanged on a layout that fits.
- [ ] Do not run the `Live*` test classes without intending to route real hardware.

## 7. Open items (not settled by the study)

| Item | Why it matters |
|---|---|
| Frame divider on AVP2000T/TR | Study measured it on one device family only |
| Compression-version mismatch between TX and RX | Could make a stream-0 tile fail even under budget |
| Switch model, port map, IGMP behaviour | Link budget assumes a non-blocking switch; switch buffers and microbursts untested |
| Other display models for the canvas ladder | Ladder tested on one 4K PAX display; others may not accept 1080p/720p |
| Full four-window 1+3 | Needs four distinct source streams |
| PiP inset vs scaler behaviour | Decoder scales the inset surface; scaler readback unconfirmed |
| Field-notes PiP asymmetry | Health check could report "fits" while the picture still fails; this is why spec §6 puts the health check before Block |
| Source of the old "9.0274 Gbps" figure and a 10G usage calculator | Needed from Mark to close the model's provenance |

## 8. Housekeeping notes

- The class comment on `MultiViewBandwidthBudget` used to say it was not wired into any Take; that was stale after `fcf793c` and is corrected in this handover commit.
- Lab MACs used in the live tests are in the study; the scripts that drove them were scratch files and are **not** in the repo. Use the model script and the netstat calls in section 3 to reproduce readings.
- Test layouts created on the lab were deleted afterwards (HTTP `DELETE /api/multiview/layout/{name}`), and scaled streams were stopped with `{"op":"stop","stream_type":"HDMI","stream_index":1,"free":true}`. The rollback record is in the study.
- Release defaults (BAVM `http://localhost:80`, port 8080, maximized, logs hidden) are unchanged by this work.

## 9. Known test failures to fix before the final merge (not caused by this branch)

Owner: the ui-round3 / RTSP side. Verified 2026-11-08 on the trial merge (see section 4.1).
- `RoomCameraViewModelTests`: `LinkDrop_DuringMove_SendsStop`, `LinkStateChange_RaisesPropertyChanged_ForLinkTextAndIsGood`, `Poll_RunsOnceEvery5s_AndNeverOverlaps` (fail with and without this branch).
- `ViewSmokeTests.Overview_NothingMoves_WhenTheDropChoiceOpens`: card 2 moves from y=412.62 to y=332.91 at 1008x615 (fails with and without this branch).
- Full-run only, not yet attributed: `RoomCameraViewModelTests` LinkIsUnreachable_TracksLinkState and MotionChanged_RaisesPropertyChanged_ForMotionTextAndState, `RoomCamerasTabTests` x2, `ViscaTcpTransportTests` / `ViscaUdpTransportTests` handle growth. Re-run the full suite on the integration branch without this branch's commits to see whether they are load flakes.
- Test command that hangs vs finishes: use `--blame-hang-timeout 180s`; plain `dotnet test` once hung with no output.
