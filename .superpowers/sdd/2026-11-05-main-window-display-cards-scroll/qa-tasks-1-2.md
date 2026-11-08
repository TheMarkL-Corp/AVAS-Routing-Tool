# QA review: Tasks 1-2 (7abd3f5..453abcb)

Spec verdict: OK
Quality verdict: Approved (Minor items only)

Method: read-only static review of the diff, the plan sections and AdaptiveCardPanel.cs (resolve/hook helpers). No build or tests run.

## Spec compliance
- Width rule: AdaptiveGridLayout.cs ComputeCarousel (line ~117-122): visible = 4 if count == 4 else CarouselVisibleCards (3.5); width = clamp((viewport - 3*gap)/visible, 320, 960). Matches the binding rule (note it clamps to MinCardWidth even when viewport < 320, per spec; the old fit-all fallback shrank to the viewport).
- One row, Columns == count, Rows == 1, height ignored (parameter unused), NeedsScroll = rowWidth > availableWidth + eps. OK.
- Gated on CarouselVisibleCards > 0; Default has 0, DisplayCard has 3.5. Source strip (GridSpec.Default) path is untouched: the carousel branch returns early only when the flag is set; panel changes are all behind IsCarousel (ArrangeOverride uses the original width/Compute in the else branch; startX uses `viewport`, which equals `width` in that branch). OK.
- Panel: measure returns full row width in carousel mode (not clamped); arrange computes the plan from the viewport (ScrollViewer viewport, else finalSize), reuses the measured plan when count and viewport match; left-aligned when row is wider (startX = max(0, ...)), centred when it fits. OK.
- Extra: none beyond the spec (plan doc figure fix 539 -> 526 is consistent: (1888-48)/3.5 = 525.71).

## Findings

### Critical
none

### Important
none

### Minor
1. AdaptiveCardPanel.cs ArrangeOverride (viewport resolution, ~line 207): measure resolves the viewport from `availableSize.Width` (finite value wins), arrange resolves it from `ResolveEffectiveWidth(PositiveInfinity)`, i.e. the ancestor ScrollViewer's ViewportWidth first. If the panel is ever measured with a finite width that differs from the ancestor ScrollViewer's viewport (e.g. a vertical-only outer ScrollViewer plus a padded parent), the stored plan is discarded in arrange and recomputed with another viewport, so arrange no longer matches measure and DesiredSize is stale until the next invalidation. Not hit by the planned horizontal scroller (infinite available width), so cosmetic risk only. Fix idea: in arrange prefer `_carouselViewport` when it is finite and `count` matches.
2. AdaptiveCardPanel.cs first layout pass: before the ScrollViewer has a ViewportWidth/ActualWidth, ResolveEffectiveWidth returns Infinity, Compute yields 960-wide cards (clamp), stored with viewport = Infinity. Arrange then recomputes (Abs(inf - x) never < 0.01; with inf/inf it is NaN, also false, so it recomputes - harmless) and ScrollViewer.SizeChanged re-measures, so it self-heals; the transient 960 measure of children (live previews) is wasted work. Optional: treat non-finite viewport as "no plan" in measure.
3. AdaptiveCardPanel.cs: the "store plan" block is duplicated in measure and arrange (three assignments each). A small `StorePlan(plan, viewport, count)` helper would remove the duplication. Local variable `v` in arrange is terse versus surrounding naming.
4. Test gaps (tests are otherwise real and use spec values: 525.71, 460, 320, 12 cards one row, centred 3-card symmetry, X = i*(w+gap), single Y):
   - AdaptiveGridLayoutTests.cs: no layout-level test for viewport < 320 (card stays 320, NeedsScroll true), nor for 0/2/3 cards at the layout level (2 and 3 are only indirectly covered; 3 via the panel test). Carousel_Twelve_OneRow does not assert card width (525.71) nor the 4-card-at-992 case (320, scrolls).
   - AdaptiveCardPanelTests.cs: nothing covers viewport change after first layout, cards added/removed while laid out (stale-plan path: count mismatch branch), a Collapsed child, or exactly four cards (no scroll, width 460), or that GridSpec.Default panel behaviour stays unchanged (existing Default tests at lines 47/104/147 cover that implicitly and are not modified).
   - Window.Close() is not in a finally (matches the existing pattern in this file).
5. AdaptiveGridLayoutTests.cs: `Assert.Equal(525.71, ..., 0.5)` uses the double tolerance overload (xunit 2.5.3 supports it); the file's older tests use int precision. Mixed style, harmless.
6. AdaptiveCardPanel.cs: carousel measure still calls ResolveEffectiveHeight although Compute ignores height (harmless).

## Edge-case walk-through
- 0 cards: Compute returns empty plan; panel Measure returns (0,0), arrange returns finalSize. OK.
- 1/2/3 cards: width = W35 clamped, row fits, centred (startX > 0), NeedsScroll false. OK (3 at 1888: 1610 < 1888).
- 4 cards: width = (v-48)/4, row == viewport, no scroll at 1888 (eps guards float). At 992 it clamps to 320 and scrolls. OK.
- 5 and 12 cards: width W35 (525.71), one row, scrolls; arrange startX = 0 (left-aligned). OK.
- Viewport < 320: width 320, row wider, scrolls, left-aligned. OK (untested).
- Viewport 992: 320 (tested for 5 cards).
- Viewport change after first layout: ScrollViewer.SizeChanged invalidates measure; measure overwrites stored plan; arrange also rechecks viewport. OK.
- Cards added/removed: children change invalidates measure; arrange also compares stored count, so a stale plan is never reused. OK.
- Collapsed children: counted out in both measure and arrange; plan is keyed on visible count. OK.
