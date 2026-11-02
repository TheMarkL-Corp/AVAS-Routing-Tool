using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AvasRoutingTool.Controls;
using AvasRoutingTool.Views;
using Xunit;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AvasRoutingTool.Tests
{
    /// <summary>UI v2 Displays header (round 2 A10b): the reworded hint, and the display cards starting right under the header.</summary>
    public partial class ViewSmokeTests
    {
        private const string DisplaysHint = "Tap a layout to change a display, or copy one display's picture onto another.";

        [Fact]
        public void DisplaysHint_ReadsTheNewSentence_WithNoDash_UnderTheDisplaysHeader() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = new VisualRoutingView { DataContext = rig.Vm };
                Showing(view, () =>
                {
                    Assert.Empty(FindAll<TextBlock>(view).Where(t => t.Text.Contains("The Layout button")));
                    var hint = Assert.Single(FindAll<TextBlock>(view).Where(t => t.Text.StartsWith("Tap a layout", StringComparison.Ordinal)));
                    Assert.Equal(DisplaysHint, hint.Text);
                    Assert.DoesNotContain(" - ", hint.Text);
                    Assert.True(hint.IsVisible, "the hint is not shown");

                    var title = FindAll<TextBlock>(view).Single(t => t.Text == "Target displays (RX)");
                    var header = Ancestor<Grid>(title)!;
                    Assert.Contains(FindAll<TextBlock>(header), t => ReferenceEquals(t, hint));
                });
            });

        [Fact]
        public void DisplayCards_StartRightUnderTheDisplaysHeader_At1920x1080()
        {
            RunSta(() =>
            {
                var (view, _) = FullHdOverview(sources: 3, displays: 3);
                Showing(view, () =>
                {
                    var title = FindAll<TextBlock>(view).Single(t => t.Text == "Target displays (RX)");
                    var header = Ancestor<Grid>(title)!;
                    var headerBottom = BoundsIn(header, view).Bottom;
                    var displays = (FrameworkElement)Named(view, "DisplaysScroll").Single();
                    var card = FindAll<DisplayCardView>(view).First(c => c.IsVisible);
                    var cardTop = BoundsIn(card, view).Top;

                    Assert.True(cardTop - headerBottom < 28, $"the first display card starts {cardTop - headerBottom:F1} px under the header (it was ~80 px)");
                    Assert.True(cardTop - BoundsIn(displays, view).Top < 16, "the first display card is not at the top of the Displays view");
                });
            });
        }

        [Fact]
        public void DisplayCards_AtTheFloor_1024x720_StartAtTheTopOfTheScrollingViewport()
        {
            RunSta(() =>
            {
                var (view, _) = FullHdOverview(sources: 3, displays: 3);
                ShowingAt(view, 1024, 720, () =>
                {
                    var displays = (ScrollViewer)Named(view, "DisplaysScroll").Single();
                    Assert.True(displays.ScrollableHeight > 0.5, "the displays do not scroll at 1024x720: this test needs them to");
                    var card = FindAll<DisplayCardView>(view).First(c => c.IsVisible);
                    var top = BoundsIn(card, displays).Top;
                    Assert.True(Math.Abs(top) <= 0.5, $"the first display card is {top:F1} px below the top of the Displays view");
                });
            });
        }

        [Fact]
        public void AdaptiveCardPanel_CenterVertically_DefaultsTrue_AndTheDisplaysPanelSetsItFalse()
        {
            RunSta(() =>
            {
                Assert.True(new AdaptiveCardPanel().CenterVertically);

                var (view, _) = FullHdOverview(sources: 3, displays: 3);
                Showing(view, () =>
                {
                    var panel = FindAll<AdaptiveCardPanel>(view).Single(p => FindAll<DisplayCardView>(p).Any());
                    Assert.False(panel.CenterVertically);
                });
            });
        }

        [Fact]
        public void ArmBanner_DoesNotTouchTheFirstDisplayCard_ThereIsAGapBelowIt() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = new VisualRoutingView { DataContext = rig.Vm };
                Showing(view, () =>
                {
                    rig.Vm.ArmSource(rig.Tx2);
                    Relayout(view);
                    var banner = (FrameworkElement)Named(view, "ArmBanner").Single();
                    Assert.True(banner.IsVisible, "the arm banner is not shown");
                    var card = FindAll<DisplayCardView>(view).First(c => c.IsVisible);
                    var gap = BoundsIn(card, view).Top - BoundsIn(banner, view).Bottom;
                    Assert.True(gap >= 8, $"the first display card is {gap:F1} px below the arm banner (needs >= 8)");
                });
            });
    }
}
