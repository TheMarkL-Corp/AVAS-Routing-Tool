# Task 2 report
Did: AdaptiveCardPanel carousel mode (Spec.CarouselVisibleCards > 0): measure returns full row width; plan stored (plan, viewport, count) and reused in arrange; arrange recomputes only if viewport/count changed, viewport taken from ScrollViewer (ResolveEffectiveWidth(infinity)) falling back to finalSize.Width; startX = max(0,(viewport-row)/2). Non-carousel path unchanged.
Tests added: DisplayPanel_FiveCards_RowIsWiderThanViewport_ArrangeMatchesMeasure, DisplayPanel_ThreeCards_AreCentredAndDoNotScroll (panel directly as ScrollViewer content).
RED: dotnet test ... --filter AdaptiveCardPanelTests -> 1 failed: card width expected 525.71, actual 755.59 (plan came from finalSize/wrong width).
GREEN: same command -> Passed 5/5. AdaptiveGridLayoutTests -> Passed 18/18.
Files: src/AvasRoutingTool/Controls/AdaptiveCardPanel.cs, tests/AvasRoutingTool.Tests/AdaptiveCardPanelTests.cs
Concerns: none. Real behavior confirmed: ScrollViewer gives infinite width, fallback to its viewport works after first layout pass (SizeChanged re-measure).
