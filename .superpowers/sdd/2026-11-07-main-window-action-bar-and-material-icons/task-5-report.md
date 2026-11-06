# Task 5 report: action bar and Material icons screenshots

Command (bash, same shell): AVAS_UIV2_SHOTS="$(pwd -W)/ui_screenshots/UI-v2-round3" dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj -p:BaseOutputPath=bin/r7/ --filter "FullyQualifiedName~UiV2Round3Shots" -- RunConfiguration.DisableParallelization=true
Result: Passed 1/1 (runner prints "< 1 ms" but PNGs were written, timestamps ~5 s apart, so the variable reached the host).

Test change: Shot() got actionBar/theme/draft params. actionBar: RX1 routed to TX1 (Device.ActiveSourceId + NotifyRoutingChanged; single view, non-AVAS so no preview), RX2 ApplyLayout matrix2x2 (empty slots, 0/4), RX3/RX4 unrouted. draft: HasDraft=true on RX1..RX3. Theme restored to dark in finally (unchanged).

PNGs (bytes): after-actionbar-1920 83081, after-actionbar-1024 58636, after-actionbar-light-1920 81749, after-actionbar-draft-1920 87588; regenerated after-main-1920-4displays 68457, -5displays 110056, -5displays-scrolled-end 109746, -12displays 106785, after-main-1024-5displays 55884.

Observations (Read each image; shown downscaled to ~1904 px wide)
- 1920 dark: "More layouts" and "Copy layout" labels fully visible, same gap icon-to-text, icons same weight and vertically centred with text; chevron right after More layouts; power icon at far right of RX1 only (RX2 multiview and unrouted cards have none). Unrouted cards: Copy layout dimmed (disabled), looks intentional. Matches proposal order/structure. Header pencil/info icons consistent with the bar; app bar moon/refresh/settings icons consistent. Nothing clipped or overlapping.
- 1024 dark: RX1/RX2 bar fine. Carousel arrows (circle, chevron) look consistent, left disabled/dim. RX3 clipped by the scroll edge, expected. Vertical scrollbar in the TX/RX panels, pre-existing.
- Light 1920: legible; icons and labels dark on white, dimmed Copy layout still readable but low contrast (disabled). Preview panels stay near-black as in dark theme (unchanged design, not part of this task). Blue accents fine.
- Draft 1920: chip "DRAFT · Resume" appears on RX1-3. On RX1 (power icon present, narrower) the More layouts label collapsed to icon+chevron only while Copy layout label stays; on RX2/RX3 label stays. Chip text sits tight to its amber border (little horizontal padding) and it is close to Copy layout; probably worth a look. Collapsed-label state looks uneven vs. neighbours but is by design.
- vs before-actionbar-1920.png: not opened in detail beyond the structure check; the new bar is a ghost-button row consistent with the proposal image (grid icon + More layouts + chevron, copy icon + Copy layout, power far right).
Concerns: RX2 multiview card has no power icon (CanUnroute-style visibility? unverified whether intended); test duration print "< 1 ms" is misleading.
