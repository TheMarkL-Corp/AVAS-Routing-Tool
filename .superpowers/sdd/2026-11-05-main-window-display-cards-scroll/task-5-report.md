# Task 5 report: screenshots

Done: copied before shot; added tests/AvasRoutingTool.Tests/ViewSmokeTests.UiV2Round3Shots.cs (env-gated, dark only, real MainWindow + FullHdOverview(3 TX, N RX); TX2 armed in the 5-display shots).
Command: AVAS_UIV2_SHOTS=<abs>/ui_screenshots/UI-v2-round3 dotnet test ... --filter "FullyQualifiedName~UiV2Round3Shots" -- RunConfiguration.DisableParallelization=true  (1 passed)
ui_screenshots is git-ignored (.gitignore:48); nothing tracked there in this worktree; force-added with git add -f.

Files (bytes): before-main-1920.png 221354; after-main-1920-4displays 68036; -5displays 108295; -5displays-scrolled-end 108922; -12displays 105056; after-main-1024-5displays 57767.
No routed card (helpers give unrouted only); one shot set has armed TX2 (banner "Placing TX2" replaces the Target displays header).

Observations:
- 1920: cards ~520 px wide (client 1904), 3 whole cards + RX4 cut at the right edge, right arrow at the right half card; left arrow disabled/dim at start. Scrolled-end: last card RX5 whole, right arrow dim, left enabled.
- Horizontal scrollbar sits at the very bottom of the window (y~1020), ~150 px below the cards, which end ~y865: empty band between cards and scrollbar.
- Right arrow overlaps the half card's picture ("Ready to rou" text sits right beside it); the left arrow overlaps the left edge of the first card picture (inside the border). Arrows are over the picture, as designed, but touch the dashed drop border.
- Cards have unused vertical space below the picture (status row empty, locked chrome).
- 1024x720: vertical scrollbars appear on both the sources strip and displays area (safety net); displays cut off at bottom (picture bottom clipped, no status row visible), horizontal scrollbar at the bottom; works but cramped.
- 12 displays looks the same as 5 at the start, scrollbar thumb smaller.
