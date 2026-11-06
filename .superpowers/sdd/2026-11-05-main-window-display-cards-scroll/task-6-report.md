# Task 6 report: source tiles 300 x 155

Status: DONE_WITH_CONCERNS. Commits: 32d336a feat(main-window) tiles 300 x 155; docs(ui-round3) screenshots refresh.

## RED / GREEN
- RED: new test SourceTiles_Are300By155At1920_OnOneRow_AndTheCompactPaletteTileIsUnchanged failed ("tile is 240.0 px wide, expected 300").
- Tokens changed: Size.Strip.Tile.Width 300, Size.Strip.Thumb.Height 155; added Size.Strip.Compact.Tile.Width 240.
- Forced extra change: SourceCardView.xaml had Width bound to Size.Strip.Tile.Width for BOTH layouts, so the Composer compact palette would have grown to 300. Width is now set in SourceCardView.xaml.cs via SetResourceReference (full -> Size.Strip.Tile.Width, IsCompact -> Size.Strip.Compact.Tile.Width). An inline XAML style was tried first and rejected by UiV2Guards (InlineStyleWithoutBasedOn).
- GREEN (full filter command): Passed 536 / Failed 0. Before the guard fix: 3 failures (2 guard, 1 icon centre), fixed.
- Command deviation: BaseOutputPath=bin/r6/ instead of bin/r3/, because a running AvasRoutingTool.exe (PID 3332, started 9:51 from this worktree's bin/r3) locks the r3 output. I did not kill it.

## Measurements (1920x1080 shot-test sizing, 3 sources, 5 displays; VisualRoutingView content height 952)
- Before: DisplaysScroll bottom 826.1, empty band 125.9 px, tile 240 x 243.4.
- After: DisplaysScroll bottom 861.1, empty band 90.9 px (<100), tile 300 x 278.4.
- Temp measuring test kept out of the repo (copy in scratchpad).

## Tests adjusted
- ViewSmokeTests.UiV2SourceStrip: SourceTile_ThumbnailIs16by9... 214x120 -> 274x155; new test added.
- ViewSmokeTests.UiV2FullHd: FullHd_SourceThumbnails_AreAtLeastTheTileBaseline 214x120 -> 274x155.
- ViewSmokeTests.UiV2SourceCard: tile height baseline 243.4 -> 278.4 (renamed SourceCard_TileHeight_IsTheLargerTileHeight_IdleAndStreaming, measures at 300); SourceCard_Width_IsTheStripTileToken 240 -> 300; SourceCard_InfoBadge_IconCentreDoesNotMove X 215 -> 275 (still 25 px from the right edge).
- ViewSmokeTests.UiV2Rename: RenamePencil_TileHeight_IsStill278_IdleAndStreaming (renamed, 300 / 278.4). Compact assertions untouched (240 wide, 81 high).

## Image (after-main-1920-5displays.png)
Three 300-wide tiles centred in one row, thumbnails larger, no strip scroll, placing banner and display cards below. Below the carousel horizontal scroll bar there is still a dark empty band (about 90-115 px in the screenshot incl. chrome); cards are not stretched.

## Concerns
- Thumbnail is 274 x 155 (inner width is 300 minus 26 chrome), not 276; ratio 1.768 vs 1.778, within existing 0.02 tolerance. 154 would be exact 16:9 if wanted.
- Full-tile height grew 35 px, which is where the band shrank.
- Remaining band 90.9 px (<100). If more is wanted (not implemented): let DisplaysScroll rows grow with a bounded MaxHeight / use leftover height to enlarge only the dock rows, or add a cap-raise of Size.Strip.Max.Fraction; or accept as breathing room.
- Running app PID 3332 left alone.
