using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AvasRoutingTool.Views;
using Xunit;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AvasRoutingTool.Tests
{
    /// <summary>Round 3, Main Window displays row: the arrows stay in view and off the scroll bar, the row says which displays it shows, and the clipped edge fades instead of cutting a card mid-word.</summary>
    public partial class ViewSmokeTests
    {
        private static Button ArrowOf(VisualRoutingView view, string name) => (Button)Named(view, name).Single();

        [Theory]
        [InlineData(1024, 720, 5)]
        [InlineData(1024, 720, 12)]
        [InlineData(1920, 952, 5)]
        public void DisplaysArrows_StayInsideTheVisibleViewport_AboveTheScrollBar(double width, double height, int displays) =>
            RunSta(() =>
            {
                var (view, _) = FullHdOverview(sources: 3, displays: displays);
                ShowingAt(view, width, height, () =>
                {
                    Idle();
                    Relayout(view);
                    var scroll = (ScrollViewer)Named(view, "DisplaysScroll").Single();
                    Assert.True(scroll.ScrollableWidth > 0.5, "the displays row does not scroll sideways: this test needs it to");
                    var viewportTop = BoundsIn(scroll, view).Top;
                    var viewportBottom = viewportTop + scroll.ViewportHeight;
                    foreach (var name in new[] { "DisplaysScrollLeft", "DisplaysScrollRight" })
                    {
                        var arrow = ArrowOf(view, name);
                        Assert.True(arrow.IsVisible, $"{name} is not shown");
                        var b = BoundsIn(arrow, view);
                        Assert.True(b.Top >= viewportTop - 0.5, $"{name} starts at {b.Top:F1}, above the viewport ({viewportTop:F1})");
                        Assert.True(b.Bottom <= viewportBottom + 0.5, $"{name} ends at {b.Bottom:F1} but the viewport (above the scroll bar) ends at {viewportBottom:F1}");
                    }
                });
            });

        [Fact]
        public void DisplaysArrows_HaveA3To1Outline_AndDisabledDoesNotFadeThem() =>
            RunSta(() =>
            {
                var (view, _) = FullHdOverview(sources: 3, displays: 12);
                ShowingAt(view, 1920, 952, () =>
                {
                    Idle();
                    Relayout(view);
                    var outline = ((SolidColorBrush)Application.Current.FindResource("Brush.Border.Control")).Color;
                    foreach (var name in new[] { "DisplaysScrollLeft", "DisplaysScrollRight" })
                    {
                        var arrow = ArrowOf(view, name);
                        Assert.Equal(outline, ((SolidColorBrush)arrow.BorderBrush).Color);
                    }
                    var left = ArrowOf(view, "DisplaysScrollLeft");
                    Assert.False(left.IsEnabled, "the left arrow is enabled at the start of the row");
                    Assert.Equal(1.0, left.Opacity);
                });
            });

        private static string RangeText(VisualRoutingView view) => ((TextBlock)Named(view, "DisplaysRange").Single()).Text;

        [Fact]
        public void DisplaysCaption_SaysWhichDisplaysAreShown_OnlyWhileTheRowScrolls() =>
            RunSta(() =>
            {
                var (view, _) = FullHdOverview(sources: 3, displays: 12);
                ShowingAt(view, 1920, 952, () =>
                {
                    Idle();
                    Relayout(view);
                    var scroll = (ScrollViewer)Named(view, "DisplaysScroll").Single();
                    Assert.True(scroll.ScrollableWidth > 0.5);
                    var start = RangeText(view);
                    Assert.StartsWith("Showing 1-", start);
                    Assert.EndsWith("of 12", start.TrimEnd());
                    scroll.ScrollToHorizontalOffset(scroll.ScrollableWidth);
                    Idle();
                    Relayout(view);
                    var end = RangeText(view);
                    Assert.Contains("-12 of 12", end);
                    Assert.NotEqual(start, end);
                });

                var (few, _) = FullHdOverview(sources: 3, displays: 3);
                ShowingAt(few, 1920, 952, () =>
                {
                    Idle();
                    Relayout(few);
                    Assert.Equal("", RangeText(few));
                });
            });

        private static (double First, double Last) MaskEnds(Brush? mask)
        {
            var g = Assert.IsType<LinearGradientBrush>(mask);
            var stops = g.GradientStops.OrderBy(s => s.Offset).ToList();
            return (stops.First().Color.A, stops.Last().Color.A);
        }

        [Fact]
        public void DisplaysRow_FadesTheEdgeWhereMoreCardsContinue_AndNotWhereItEnds() =>
            RunSta(() =>
            {
                var (view, _) = FullHdOverview(sources: 3, displays: 12);
                ShowingAt(view, 1920, 952, () =>
                {
                    Idle();
                    Relayout(view);
                    var scroll = (ScrollViewer)Named(view, "DisplaysScroll").Single();
                    var atStart = MaskEnds(scroll.OpacityMask);
                    Assert.Equal(255, atStart.First);
                    Assert.Equal(0, atStart.Last);

                    scroll.ScrollToHorizontalOffset(scroll.ScrollableWidth / 2);
                    Idle();
                    Relayout(view);
                    var middle = MaskEnds(scroll.OpacityMask);
                    Assert.Equal(0, middle.First);
                    Assert.Equal(0, middle.Last);

                    scroll.ScrollToHorizontalOffset(scroll.ScrollableWidth);
                    Idle();
                    Relayout(view);
                    var end = MaskEnds(scroll.OpacityMask);
                    Assert.Equal(0, end.First);
                    Assert.Equal(255, end.Last);
                });

                var (few, _) = FullHdOverview(sources: 3, displays: 3);
                ShowingAt(few, 1920, 952, () =>
                {
                    Idle();
                    Relayout(few);
                    Assert.Null(((ScrollViewer)Named(few, "DisplaysScroll").Single()).OpacityMask);
                });
            });
    }
}
