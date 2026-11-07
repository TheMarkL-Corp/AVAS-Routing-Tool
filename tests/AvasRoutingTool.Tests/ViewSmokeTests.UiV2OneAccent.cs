using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Wpf.Ui.Controls;
using Xunit;
using Button = System.Windows.Controls.Button;
using Ellipse = System.Windows.Shapes.Ellipse;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// UI v2 one accent (finding M5b): blue is selection and the primary action, cyan is the armed target, status is a dot plus a plain word.
    /// The overview carries no coloured pills and no blue primary button.
    /// </summary>
    public partial class ViewSmokeTests
    {
        private static Color BrushColor(string key) => ((SolidColorBrush)Application.Current.FindResource(key)).Color;

        private static object Res(string key) => Application.Current.FindResource(key);

        /// <summary>The named status word is the one TextBlock with that name, in Style.Text.Status, and the other word is hidden.</summary>
        private static void AssertStatusWord(FrameworkElement view, string shownName, string word, string hiddenName)
        {
            var shown = Named(view, shownName);
            Assert.True(shown.Count == 1, $"no {shownName} TextBlock (the status is still a pill)");
            var tb = Assert.IsType<TextBlock>(shown[0]);
            Assert.True(tb.IsVisible, $"{shownName} is not visible");
            Assert.Equal(word, tb.Text);
            Assert.Same(Res("Style.Text.Status"), tb.Style);
            Assert.True(tb.FontSize >= 12, $"'{word}' is {tb.FontSize}px");

            var hidden = Named(view, hiddenName);
            Assert.True(hidden.Count == 1, $"no {hiddenName} TextBlock");
            Assert.False(hidden[0].IsVisible, $"{hiddenName} is visible next to {shownName}");
        }

        private static void AssertStatusDot(FrameworkElement view)
        {
            var dots = FindAll<Ellipse>(view).Where(e => e.Style == Res("Style.StatusDot.Small")).ToList();
            Assert.Contains(dots, e => e.IsVisible && e.ActualWidth == Token("Size.Dot.S"));
        }

        [Fact]
        public void SourceCard_StatusIsDotAndWord_NotAPill() =>
            RunSta(() =>
            {
                SourceCardAt(UiV2Source(online: true), view =>
                {
                    Assert.True(FindAll<Badge>(view).Where(b => b.IsVisible).All(b => FindAll<TextBlock>(b).All(t => t.Text != "ONLINE" && t.Text != "OFFLINE")),
                        "a visible ONLINE/OFFLINE pill is still on the source card");
                    AssertStatusWord(view, "OnlineWord", "Online", "OfflineWord");
                    AssertStatusDot(view);
                });
                SourceCardAt(UiV2Source(online: false), view =>
                {
                    Assert.True(FindAll<Badge>(view).Where(b => b.IsVisible).All(b => FindAll<TextBlock>(b).All(t => t.Text != "ONLINE" && t.Text != "OFFLINE")),
                        "a visible ONLINE/OFFLINE pill is still on the source card");
                    AssertStatusWord(view, "OfflineWord", "Offline", "OnlineWord");
                    AssertStatusDot(view);
                });
            });

        [Fact]
        public void SourceCard_DualLinkTag_IsNeutral_AndStartIsOutlinedActionBar() =>
            RunSta(() =>
                SourceCardAt(UiV2Source(link: "Dual Link"), view =>
                {
                    var tag = FindAll<Badge>(view).Single(b => FindAll<TextBlock>(b).Any(t => t.Text == "DUAL LINK"));
                    Assert.Same(Res("Style.Badge.Neutral"), tag.Style);

                    var start = Named(view, "StartStreamButton");
                    Assert.True(start.Count == 1, "no StartStreamButton");
                    Assert.Same(Res("Style.Button.ActionBar.Outlined"), start[0].Style);
                }));

        /// <summary>Routed or not, the card frame is the neutral border, and the mode tag is neutral; a drag over the card still shows the cyan 2 px target.</summary>
        private static void AssertNeutralDisplayCard(DisplayCardView view, DisplayCardViewModel vm)
        {
            var frame = Assert.IsType<Card>(Named(view, "CardFrame").Single());
            Assert.Equal(BrushColor("Brush.Border.Default"), ((SolidColorBrush)frame.BorderBrush).Color);
            Assert.Equal(1, frame.BorderThickness.Left);

            var mode = Assert.IsType<Badge>(Named(view, "ModeBadge").Single());
            Assert.Same(Res("Style.Badge.Neutral"), mode.Style);

            vm.IsDragOver = true;
            Relayout(view);
            Assert.Equal(BrushColor("Brush.Border.DropTarget"), ((SolidColorBrush)frame.BorderBrush).Color);
            Assert.Equal(2, frame.BorderThickness.Left);
            vm.IsDragOver = false;
            Relayout(view);
        }

        [Fact]
        public void DisplayCard_RoutedBorderIsNeutral_AndModeBadgeIsNeutral() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var routed = rig.View(rig.Rx1, out var routedHost);
                Showing(routedHost, () => AssertNeutralDisplayCard(routed, rig.Rx1));

                var unrouted = rig.View(rig.Rx3, out var unroutedHost);
                Showing(unroutedHost, () => AssertNeutralDisplayCard(unrouted, rig.Rx3));
            });

        [Fact]
        public void DisplayCard_TurnOff_IsTheGhostButton() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host);
                Showing(host, () =>
                {
                    var turnOff = (Button)Named(view, "TurnOffButton").Single();
                    Assert.Same(Res("Style.Button.Ghost"), turnOff.Style);
                    Assert.True(turnOff.IsVisible && turnOff.ActualHeight >= 48, $"Turn Off {turnOff.ActualWidth}x{turnOff.ActualHeight}");
                });
            });

        [Fact]
        public void Overview_HasNoPrimaryButton_AndDiscoveredIsPlainText() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = new VisualRoutingView { DataContext = rig.Vm };
                Showing(view, () =>
                {
                    var primaries = FindAll<Button>(view).Where(b => b.IsVisible && b.Style == Res("Style.Button.Primary")).ToList();
                    Assert.True(primaries.Count == 0, $"{primaries.Count} visible Style.Button.Primary buttons in the overview");

                    var discovered = FindAll<TextBlock>(view).Where(t => t.IsVisible && t.Text.Contains("discovered")).ToList();
                    Assert.True(discovered.Count == 2, $"{discovered.Count} visible 'discovered' texts (expected the Sources and the Displays counts)");
                    Assert.All(discovered, t => Assert.Same(Res("Style.Text.Caption"), t.Style));
                    Assert.All(discovered, t => Assert.True(t.FontSize >= 12, $"'{t.Text}' is {t.FontSize}px"));

                    Assert.DoesNotContain(FindAll<Badge>(view), b => b.IsVisible && FindAll<TextBlock>(b).Any(t => t.Text.Contains("DISCOVERED")));
                });
            });
    }
}
