### Task 2: Panel lays out one row and exposes its extent

**Files:**
- Modify: `src/AvasRoutingTool/Controls/AdaptiveCardPanel.cs` (measure returns the full row width; arrange reuses the measured plan)
- Test: `tests/AvasRoutingTool.Tests/AdaptiveCardPanelTests.cs`

- [ ] **Step 1: Write the failing test** `DisplayPanel_FiveCards_RowIsWiderThanViewport_ArrangeMatchesMeasure`: panel in a 1888 x 590 horizontal `ScrollViewer`; assert `panel.DesiredSize.Width > 1888`, each child `ActualWidth` equals the planned width, children sit at one `Y`.
- [ ] **Step 2: Run to verify fail.**
- [ ] **Step 3: Implement:** in carousel mode measure with the plan's full row width (do not clamp to `availableSize.Width`), store the plan and reuse it in arrange; left-align when scrolling, centre when it fits.
- [ ] **Step 4: Run to verify pass.**
- [ ] **Step 5: Commit** `feat(layout): card panel lays out a carousel row`.

