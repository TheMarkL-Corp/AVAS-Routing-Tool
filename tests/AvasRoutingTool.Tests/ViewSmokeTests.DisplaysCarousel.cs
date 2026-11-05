using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AvasRoutingTool.Views;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>Round 3: the displays row is one row that pans sideways, with an arrow button at each edge.</summary>
    public partial class ViewSmokeTests
    {
        private const double CarouselGap = 16;

        [Fact]
        public void Displays_FiveAt1920_ScrollsSideways_NotVertically() =>
            RunSta(() =>
            {
                var (view, _) = FullHdOverview(3, 5);
                WithFullHdWindow(view, () =>
                {
                    var scroll = (ScrollViewer)Named(view, "DisplaysScroll").Single();
                    Assert.Equal(ScrollBarVisibility.Auto, scroll.HorizontalScrollBarVisibility);
                    // Vertical Auto is only a safety net for short windows: at 1920x1080 it does not scroll.
                    Assert.Equal(ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
                    Assert.Equal(PanningMode.Both, scroll.PanningMode);
                    Assert.False(scroll.Focusable);
                    Assert.True(scroll.ScrollableWidth > 0, $"scrollable width {scroll.ScrollableWidth}");
                    Assert.Equal(0, scroll.ScrollableHeight, 1);
                    Assert.NotEqual(Visibility.Visible, scroll.ComputedVerticalScrollBarVisibility);
                    return 0;
                });
            });

        [Fact]
        public void Displays_Arrows_StepOneCard_AndDisableAtEnds() =>
            RunSta(() =>
            {
                var (view, _) = FullHdOverview(3, 12);
                WithFullHdWindow(view, () =>
                {
                    var scroll = (ScrollViewer)Named(view, "DisplaysScroll").Single();
                    var left = (Button)Named(view, "DisplaysScrollLeft").Single();
                    var right = (Button)Named(view, "DisplaysScrollRight").Single();
                    double step = FindAll<DisplayCardView>(view).First().ActualWidth + CarouselGap;

                    Assert.True(left.IsVisible && right.IsVisible);
                    Assert.True(left.ActualWidth >= 48 && left.ActualHeight >= 48);
                    Assert.Equal("Scroll displays left", System.Windows.Automation.AutomationProperties.GetName(left));
                    Assert.Equal("Scroll displays right", System.Windows.Automation.AutomationProperties.GetName(right));
                    Assert.False(left.IsEnabled, "left is enabled at the start");
                    Assert.False(left.IsTabStop && left.IsEnabled);
                    Assert.True(right.IsEnabled);
                    Assert.Equal(0, scroll.HorizontalOffset, 1);

                    Click(right);
                    Relayout(view);
                    Assert.Equal(step, scroll.HorizontalOffset, 1);
                    Assert.True(left.IsEnabled);

                    Click(left);
                    Relayout(view);
                    Assert.Equal(0, scroll.HorizontalOffset, 1);
                    Assert.False(left.IsEnabled);

                    // Clamps at the far end; the last card is whole, the right arrow is off.
                    view.ScrollDisplaysBy(100);
                    Relayout(view);
                    Assert.Equal(scroll.ScrollableWidth, scroll.HorizontalOffset, 1);
                    Assert.False(right.IsEnabled);
                    Assert.True(left.IsEnabled);
                    var lastCard = FindAll<DisplayCardView>(view).Last();
                    var b = lastCard.TransformToAncestor(scroll).TransformBounds(new Rect(0, 0, lastCard.ActualWidth, lastCard.ActualHeight));
                    Assert.True(b.Right <= scroll.ViewportWidth + 0.5, $"the last card ends at {b.Right}, viewport {scroll.ViewportWidth}");

                    // And clamps at the start.
                    view.ScrollDisplaysBy(-100);
                    Relayout(view);
                    Assert.Equal(0, scroll.HorizontalOffset, 1);

                    // The arrows sit over the picture, vertically.
                    var card = FindAll<DisplayCardView>(view).First();
                    var picture = (FrameworkElement)Named(card, "PreviewSurface").Single();
                    var pic = picture.TransformToAncestor(view).TransformBounds(new Rect(0, 0, picture.ActualWidth, picture.ActualHeight));
                    var lb = left.TransformToAncestor(view).TransformBounds(new Rect(0, 0, left.ActualWidth, left.ActualHeight));
                    Assert.Equal(pic.Top + pic.Height / 2, lb.Top + lb.Height / 2, 2);
                    return 0;
                });
            });

        [Fact]
        public void Displays_FourAt1920_ArrowsHidden() =>
            RunSta(() =>
            {
                var (view, _) = FullHdOverview(3, 4);
                WithFullHdWindow(view, () =>
                {
                    var scroll = (ScrollViewer)Named(view, "DisplaysScroll").Single();
                    Assert.Equal(0, scroll.ScrollableWidth, 1);
                    Assert.False(((Button)Named(view, "DisplaysScrollLeft").Single()).IsVisible);
                    Assert.False(((Button)Named(view, "DisplaysScrollRight").Single()).IsVisible);
                    return 0;
                });
            });

        [Fact]
        public void Displays_MouseWheel_PansSideways() =>
            RunSta(() =>
            {
                var (view, _) = FullHdOverview(3, 6);
                WithFullHdWindow(view, () =>
                {
                    var scroll = (ScrollViewer)Named(view, "DisplaysScroll").Single();
                    Assert.Equal(0, scroll.HorizontalOffset, 1);
                    var down = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent, Source = scroll };
                    scroll.RaiseEvent(down);
                    Relayout(view);
                    Assert.True(down.Handled);
                    Assert.True(scroll.HorizontalOffset > 0, $"offset {scroll.HorizontalOffset}");
                    double after = scroll.HorizontalOffset;

                    var up = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120) { RoutedEvent = UIElement.PreviewMouseWheelEvent, Source = scroll };
                    scroll.RaiseEvent(up);
                    Relayout(view);
                    Assert.True(scroll.HorizontalOffset < after);
                    return 0;
                });
            });
    }
}
