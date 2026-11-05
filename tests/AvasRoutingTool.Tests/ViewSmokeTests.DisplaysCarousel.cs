using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using AvasRoutingTool.ViewModels;
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

        /// <summary>
        /// A tap on a card in the sideways row still arms/routes. Real touch manipulation (a finger pan) cannot be raised
        /// from a test; what can be checked: the scroller pans on touch (PanningMode.Both), the card does not take the
        /// press away from it (nothing handled on a press outside the Copy layout handle), and the card's 8 px
        /// threshold turns a moved press into a drag, never a tap.
        /// </summary>
        [Fact]
        public void Touch_Tap_OnCard_StillArms() =>
            RunSta(() =>
            {
                var (view, vm) = FullHdOverview(3, 5);
                WithFullHdWindow(view, () =>
                {
                    var scroll = (ScrollViewer)Named(view, "DisplaysScroll").Single();
                    Assert.True(scroll.ScrollableWidth > 0);
                    Assert.Equal(PanningMode.Both, scroll.PanningMode);
                    var cards = FindAll<DisplayCardView>(view);

                    // A press outside the copy handle is left to the scroller (the card never handles it).
                    var press = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent, Source = cards[0] };
                    cards[0].RaiseEvent(press);
                    Assert.False(press.Handled, "the card handled a press outside the copy handle");
                    Tap(cards[0], Mouse.PreviewMouseUpEvent);

                    // A tap routes the armed source onto the card, as without scrolling.
                    var tx = vm.Sources[0];
                    vm.ArmSource(tx);
                    Assert.Same(tx, vm.ArmedSource);
                    TapCard(cards[1]);
                    Until(() => vm.ArmedSource == null, "a tap did not route the armed source");
                    Assert.True(((DisplayCardViewModel)cards[1].DataContext).IsRouted, "the tapped display is not routed");

                    // A press that moved past the threshold is a drag (a pan), not a tap.
                    var gesture = new TapGesture();
                    gesture.Press(new Point(10, 10));
                    Assert.True(gesture.Move(new Point(10 + TapGesture.DragThreshold + 1, 10)));
                    Assert.False(gesture.Release());
                    return 0;
                });
            });

        [Fact]
        public void Keyboard_Tab_ToCardOffscreen_ScrollsItIntoView() =>
            RunSta(() =>
            {
                var (view, _) = FullHdOverview(3, 5);
                WithFullHdWindow(view, () =>
                {
                    var scroll = (ScrollViewer)Named(view, "DisplaysScroll").Single();
                    var cards = FindAll<DisplayCardView>(view);
                    var first = cards.First();
                    var last = cards.Last();
                    // The fourth card starts inside the viewport but ends beyond it: its first control is visible
                    // already, so only a card-level reveal brings the whole card in.
                    var partial = cards[3];
                    Assert.True(scroll.ScrollableWidth > 0);
                    Assert.Equal(0, scroll.HorizontalOffset, 1);

                    bool wasKeyboard = InputModality.IsKeyboard;
                    InputModality.IsKeyboard = true;
                    try
                    {
                    var near = FindAll<ButtonBase>(partial).First(e => e.Focusable && e.IsVisible && e.IsEnabled && KeyboardNavigation.GetIsTabStop(e));
                    Assert.True(near.Focus(), "could not focus an element of the fourth card");
                    Relayout(view);
                    Idle();
                    Relayout(view);
                    var p = partial.TransformToAncestor(scroll).TransformBounds(new Rect(0, 0, partial.ActualWidth, partial.ActualHeight));
                    Assert.True(p.Left >= -0.5 && p.Right <= scroll.ViewportWidth + 0.5, $"the fourth card spans {p.Left}..{p.Right}, viewport {scroll.ViewportWidth}");

                    var target = FindAll<ButtonBase>(last).First(e => e.Focusable && e.IsVisible && e.IsEnabled && KeyboardNavigation.GetIsTabStop(e));
                    Assert.True(target.Focus(), "could not focus an element of the last card");
                    Relayout(view);
                    Idle();
                    Relayout(view);

                    var b = last.TransformToAncestor(scroll).TransformBounds(new Rect(0, 0, last.ActualWidth, last.ActualHeight));
                    Assert.True(b.Right <= scroll.ViewportWidth + 0.5 && b.Left >= -0.5, $"the last card spans {b.Left}..{b.Right}, viewport {scroll.ViewportWidth}");
                    Assert.True(scroll.HorizontalOffset > 0);

                    // And back: the first card's element brings the first card whole into view on the left.
                    var back = FindAll<ButtonBase>(first).First(e => e.Focusable && e.IsVisible && e.IsEnabled && KeyboardNavigation.GetIsTabStop(e));
                    Assert.True(back.Focus());
                    Relayout(view);
                    Idle();
                    Relayout(view);
                    var f = first.TransformToAncestor(scroll).TransformBounds(new Rect(0, 0, first.ActualWidth, first.ActualHeight));
                    Assert.True(f.Left >= -0.5 && f.Right <= scroll.ViewportWidth + 0.5, $"the first card spans {f.Left}..{f.Right}");
                    }
                    finally { InputModality.IsKeyboard = wasKeyboard; }
                    return 0;
                });
            });

        [Fact]
        public void Displays_MouseWheel_OnAShortWindow_ScrollsVerticallyAsUsual() =>
            RunSta(() =>
            {
                var (view, _) = FullHdOverview(3, 2);
                ShowingAt(view, 1008, 615, () =>
                {
                    var scroll = (ScrollViewer)Named(view, "DisplaysScroll").Single();
                    Assert.True(scroll.ScrollableWidth <= 0.5, $"scrollable width {scroll.ScrollableWidth}");
                    Assert.True(scroll.ScrollableHeight > 0.5, $"scrollable height {scroll.ScrollableHeight}");
                    var down = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent, Source = scroll };
                    scroll.RaiseEvent(down);
                    Relayout(view);
                    Assert.False(down.Handled, "the sideways wheel swallowed a wheel the row cannot use sideways");
                    Assert.Equal(0, scroll.HorizontalOffset, 1);
                });
            });

        [Fact]
        public void Displays_Arrow_DisablingAtTheEnd_KeepsKeyboardFocusInTheView() =>
            RunSta(() =>
            {
                var (view, _) = FullHdOverview(3, 8);
                WithFullHdWindow(view, () =>
                {
                    var left = (Button)Named(view, "DisplaysScrollLeft").Single();
                    var right = (Button)Named(view, "DisplaysScrollRight").Single();
                    Assert.True(right.Focus() && right.IsKeyboardFocused);
                    view.ScrollDisplaysBy(100);
                    Relayout(view);
                    Assert.False(right.IsEnabled);
                    var focused = Keyboard.FocusedElement as DependencyObject;
                    Assert.NotNull(focused);
                    Assert.True(focused is System.Windows.Media.Visual v && v.IsDescendantOf(view), "focus left the view");
                    Assert.Same(left, focused);

                    // And back at the start: the left arrow disables, focus moves to the right one.
                    view.ScrollDisplaysBy(-100);
                    Relayout(view);
                    Assert.False(left.IsEnabled);
                    Assert.Same(right, Keyboard.FocusedElement);
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
