### Task 1: Carousel rule in the layout policy

**Files:**
- Modify: `src/AvasRoutingTool/Controls/AdaptiveGridLayout.cs` (`GridSpec.Carousel`, `Compute`)
- Test: `tests/AvasRoutingTool.Tests/AdaptiveGridLayoutTests.cs`

**Interfaces:**
- Produces: `GridSpec.CarouselVisibleCards` (`double`, default `0` = today's fit-all behaviour, so the source strip is untouched; `DisplayCard` sets `3.5`). `Compute` signature unchanged; in carousel mode it returns `Rows == 1`, `Columns == count`, and `NeedsScroll == true` when the row is wider than `availableWidth`.

- [ ] **Step 1: Write the failing tests** (names and values from the rule above, viewport 1888): `Carousel_OneCard_Is539Wide`, `Carousel_FourCards_FitWithoutScroll`, `Carousel_FiveCards_Scrolls`, `Carousel_At992_UsesMinWidth`, `Carousel_Twelve_OneRow`; the existing fit-all tests stay green.
- [ ] **Step 2: Run to verify fail:** `dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj -p:BaseOutputPath=bin/r3/ --filter "FullyQualifiedName~AdaptiveGridLayoutTests" -- RunConfiguration.DisableParallelization=true`. Expected: new tests FAIL.
- [ ] **Step 3: Implement** the rule in `Compute` behind `CarouselVisibleCards > 0`.
- [ ] **Step 4: Run to verify pass.**
- [ ] **Step 5: Commit** `feat(layout): display cards use a one-row carousel rule`.

