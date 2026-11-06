# SDD ledger — plan: docs/superpowers/plans/2026-11-05-main-window-display-cards-scroll.md
Task 0: complete (already merged on branch: room-cameras 1c2bd37, rename 0ebc531, advanced 75de7f6; tag ui-round3-base)
Ruling: Task 0 needed no work, branch already holds the three merges — verified with git log --merges — costs nothing if wrong
Ruling: carousel width = (viewport-3g)/3.5 (525.71 at 1888); plan/brief figure 539.43 was my arithmetic slip — formula is the spec — tests assert 525.71 +-0.5
Ruling: existing test DisplayCard_OnATallWindow_GrowsPastTheOldCap (1 card -> 960) is superseded by the carousel rule (1 card = W35); update it — user chose 3.5 cards at 1920 — a single card is 526 not 960
Task 1: complete (commits 7abd3f5..6b2f1b3, controller diff-review clean, QA batched with Task 2)
Task 2: complete (commits 71bc15c..453abcb, controller diff-review clean, QA batched with Task 1)
Task 1-2: QA (sonnet) Spec OK, Quality Approved, 0 Critical/Important; minors in qa-tasks-1-2.md (deferred to final review)
Task 3: fix round 1/5 requested (6 smoke failures from carousel rule). Ruling: re-record FullHd baselines to carousel rule (user chose 3.5 across) — Ruling: carousel card min 400 px (copy tag needs ~124 px; 338 px cards squeezed it) — costs ~2.4 cards visible at 1008 instead of 3 — Ruling: keep vertical scroll Auto as safety net on short windows although user prefers sideways-only — clipped toast on a 615 px window is worse; scrollbar only appears when too short
Task 3: fix round 1/5 done (3 commits 552f63d d400fd7 d092f20); QA pending. Note: min-400 tests written with code, no separate RED (process gap, ledgered)
Task 4: commit b0badea (BringIntoView on keyboard focus). Task 3 QA: 1 Important (wheel swallowed when only vertical scroll) -> fix round 2 sent
Task 3: complete (commits 38c24b4..1d10f5e incl 2 fix rounds, QA Important fixed). Task 4: complete (b0badea; touch pan not simulatable, noted)
Task 3: fix round 3 done (33e6820 0ae14c4 a6238bf). Task 5: 878d98c shots. Full suite run 1 after tasks: 1842 pass/1 fail (styles guard) fixed in round 3
Final review: Important (resize keeps left card) fixed e35d9ff; full non-live suite 1844+ passed 0 failed on final head
