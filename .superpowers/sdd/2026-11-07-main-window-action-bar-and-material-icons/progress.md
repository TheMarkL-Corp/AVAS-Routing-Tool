# SDD ledger — plan: docs/superpowers/plans/2026-11-07-main-window-action-bar-and-material-icons.md
Task 0: complete (commit after cbe1fb1; baseline = existing 4-displays shot, the shot test ran as a no-op on env so the already-current PNG was reused)
Task 1: complete (commits c5c2e63..3cbfe49; contact sheet verified by controller by eye, 22 icons correct; no clean RED run recorded — process gap; QA pending)
Task 1: QA Spec OK, Quality changes needed (2 Important) -> fix round 1/5 sent
Task 2: commit 402fd2e; Ruling: while a card has a draft the More layouts label collapses to icon+chevron (bar cannot fit 450 px in 374 px) — accepted as fallback, flagged to user for a design call (alt: move draft chip to header) — costs a label that disappears while a draft exists
Task 1: fix round 1 done (b84d867), complete. Task 2: fix round 1 done (326b9a8) 599 pass; scoped re-review folded into final whole-branch QA (ruling: small, RED/GREEN evidence recorded)
Tasks 3+4: complete (fccbe65 1add879; 630 pass); source tile height 278.4->276.96 and info icon centre moved: exact-box Material icons, accepted
