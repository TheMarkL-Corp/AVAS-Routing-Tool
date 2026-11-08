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
