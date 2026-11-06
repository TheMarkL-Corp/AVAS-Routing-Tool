# Task 1 report: NEEDS_CONTEXT (not committed)

RED: new tests failed to compile (CS1061 GridSpec.CarouselVisibleCards missing), as expected.

Implemented (uncommitted, in worktree): GridSpec.CarouselVisibleCards (init, default 0; DisplayCard = 3.5), ComputeCarousel in AdaptiveGridLayout.cs using the brief's formula, 6 new tests.

Test run after implement: AdaptiveGridLayoutTests 15 pass / 3 fail; AdaptiveCardPanelTests 3/3 pass.

Blocking contradiction in the brief:
- Brief formula W35 = (viewport - 3g)/3.5 gives (1888-48)/3.5 = 525.71 at viewport 1888.
- Brief expected value is 539.43 (tests assert within 0.5) = 1888/3.5, i.e. no gap subtraction.
- Failing: Carousel_OneCard_Is539Wide, Carousel_FiveCards_Scrolls (actual 525.71).
- 4 cards = 460 = (1888-48)/4 is consistent with the formula; 992 -> 320 passes either way.
Which is authoritative: the formula (then expected values become 525.71) or 539.43 (then W35 = viewport/3.5)?

Second consequence: existing test ADisplayCard_OnATallWindow_GrowsPastTheOldCap (1 card, 1888x1200, expects 960) now fails because carousel mode gives one card ~526-539 wide, ignoring height. It needs updating or removal once the rule is confirmed; I did not change it.

## Resolution (controller ruling: formula authoritative, 525.71)
Renamed Carousel_OneCard_Is526Wide; 1- and 5-card asserts 525.71 +/-0.5; superseded test renamed ADisplayCard_OnATallWindow_IgnoresHeight_AndUsesTheCarouselWidth.
GREEN: AdaptiveGridLayoutTests Passed 18/18; AdaptiveCardPanelTests Passed 3/3.
Committed only AdaptiveGridLayout.cs and AdaptiveGridLayoutTests.cs (plan doc had a pre-existing uncommitted change, left alone).
