using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AvasRoutingTool.Services;
using AvasRoutingTool.Views;
using Wpf.Ui.Controls;
using Xunit;
using Button = System.Windows.Controls.Button;
using Path = System.Windows.Shapes.Path;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// UI v2 Layout button size (task A14): the Layout dropdown on a display card is Size.Layout.Button.Width x Height (64 x 56), its
    /// icon and chevron are the Size.Icon.Layout tokens, and its fill and outline wrap it exactly. The action bar row is still
    /// Size.Card.Row.ActionBar (56), so the card does not get taller.
    /// </summary>
    public partial class ViewSmokeTests
    {
        private static Button LayoutOf(DisplayCardView card) => (Button)Named(card, "LayoutButton").Single();

        /// <summary>Two rectangles agree within <paramref name="tolerance"/> on each edge.</summary>
        private static bool SameBounds(Rect a, Rect b, double tolerance = 1.0) =>
            Math.Abs(a.Left - b.Left) <= tolerance && Math.Abs(a.Top - b.Top) <= tolerance &&
            Math.Abs(a.Right - b.Right) <= tolerance && Math.Abs(a.Bottom - b.Bottom) <= tolerance;

        [Fact]
        public void LayoutButton_IsTheTokenSize_64x56_AndBothAreAtLeastTheTouchFloor() =>
            RunSta(() =>
            {
                Assert.True(Token("Size.Layout.Button.Width") >= Token("Size.Touch.Min"), "the Layout width is under the touch floor");
                Assert.True(Token("Size.Layout.Button.Height") >= Token("Size.Touch.Min"), "the Layout height is under the touch floor");
                foreach (double width in ActionBarWidths())
                {
                    var rig = new CopyRig();
                    var view = rig.View(rig.Rx1, out var host, width);
                    Showing(host, () =>
                    {
                        var layout = LayoutOf(view);
                        Assert.True(Math.Abs(layout.ActualWidth - Token("Size.Layout.Button.Width")) <= 0.5,
                            $"Layout is {layout.ActualWidth:F1} wide at a {width} px card, not {Token("Size.Layout.Button.Width")}");
                        Assert.True(Math.Abs(layout.ActualHeight - Token("Size.Layout.Button.Height")) <= 0.5,
                            $"Layout is {layout.ActualHeight:F1} high at a {width} px card, not {Token("Size.Layout.Button.Height")}");
                    });
                }
            });

        [Fact]
        public void LayoutFill_AndOutline_WrapTheLayoutButtonExactly() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host, 613);
                Showing(host, () =>
                {
                    var group = (Grid)Named(view, "LayoutSegments").Single();
                    var layout = LayoutOf(view);
                    var outline = (System.Windows.Controls.Border)Named(view, "LayoutSegmentsOutline").Single();
                    var fill = (System.Windows.Controls.Border)group.Children[0];
                    var button = BoundsIn(layout, view);
                    Assert.True(SameBounds(BoundsIn(outline, view), button), $"the outline {BoundsIn(outline, view)} is not the button {button}");
                    Assert.True(SameBounds(BoundsIn(fill, view), button), $"the fill {BoundsIn(fill, view)} is not the button {button}");
                    Assert.True(SameBounds(BoundsIn(group, view), button), $"the group {BoundsIn(group, view)} is not the button {button}");
                });
            });

        [Fact]
        public void LayoutButton_TakesAPressAtItsCornersAndCentre_OnTheButton() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host, 613);
                Showing(host, () =>
                {
                    var layout = LayoutOf(view);
                    double w = layout.ActualWidth, h = layout.ActualHeight;
                    var spots = new[] { new Point(1, 1), new Point(w - 1, 1), new Point(1, h - 1), new Point(w - 1, h - 1), new Point(w / 2, h / 2) };
                    foreach (var spot in spots)
                    {
                        var hit = PressTarget(view, layout.TranslatePoint(spot, view));
                        Assert.True(hit != null && Within(hit, layout),
                            $"a press at ({spot.X:F1},{spot.Y:F1}) of the {w:F0}x{h:F0} Layout button lands on {(hit == null ? "nothing" : hit.GetType().Name)}, not on it");
                    }
                });
            });

        [Fact]
        public void LayoutIconAndChevron_AreTheIconTokens_InsideTheButton_AndCentred() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host, 613);
                Showing(host, () =>
                {
                    var layout = LayoutOf(view);
                    var icon = (Path)Named(view, "LayoutIcon").Single();
                    var chevron = FindAll<SymbolIcon>(layout).Single(i => i.Symbol == SymbolRegular.ChevronDown24);

                    Assert.True(Math.Abs(icon.ActualWidth - Token("Size.Icon.Layout")) <= 0.5 && Math.Abs(icon.ActualHeight - Token("Size.Icon.Layout")) <= 0.5,
                        $"the grid icon is {icon.ActualWidth:F1}x{icon.ActualHeight:F1}, not {Token("Size.Icon.Layout")}");
                    Assert.Equal(Token("Size.Icon.Layout.Chevron"), chevron.FontSize);
                    Assert.True(Math.Abs(chevron.ActualWidth - Token("Size.Icon.Layout.Chevron")) <= 0.5,
                        $"the chevron is {chevron.ActualWidth:F1} wide, not {Token("Size.Icon.Layout.Chevron")}");

                    var ib = BoundsIn(icon, layout);
                    var cb = BoundsIn(chevron, layout);
                    double w = layout.ActualWidth, h = layout.ActualHeight;
                    foreach (var (name, box) in new[] { ("icon", ib), ("chevron", cb) })
                        Assert.True(box.Left >= 0 && box.Top >= 0 && box.Right <= w && box.Bottom <= h, $"the {name} {box} is outside the {w:F0}x{h:F0} button");

                    double centreX = (Math.Min(ib.Left, cb.Left) + Math.Max(ib.Right, cb.Right)) / 2;
                    double centreY = (Math.Min(ib.Top, cb.Top) + Math.Max(ib.Bottom, cb.Bottom)) / 2;
                    Assert.True(Math.Abs(centreX - w / 2) <= 1, $"the icon and chevron are centred at x {centreX:F1}, the button's centre is {w / 2:F1}");
                    Assert.True(Math.Abs(centreY - h / 2) <= 1, $"the icon and chevron are centred at y {centreY:F1}, the button's centre is {h / 2:F1}");
                });
            });

        [Fact]
        public void ActionBar_WithTheTallerLayoutButton_FitsAtBothWidths_WithAnEightPixelGap() =>
            RunSta(() =>
            {
                foreach (double width in ActionBarWidths())
                {
                    var rig = new CopyRig();
                    var view = rig.View(rig.Rx1, out var host, width);
                    Showing(host, () =>
                    {
                        string at = $"at a {width} px card";
                        var dock = (FrameworkElement)Named(view, "FloatingActionDock").Single();
                        var layout = LayoutOf(view);
                        var copy = (Button)Named(view, "CopyLayoutHandle").Single();
                        var turnOff = (Button)Named(view, "TurnOffButton").Single();

                        foreach (var button in new[] { layout, copy, turnOff })
                        {
                            Assert.True(button.IsVisible, $"{button.Name} is not shown {at}");
                            Assert.True(button.ActualHeight >= Token("Size.Touch.Min"), $"{button.Name} is {button.ActualHeight:F1} high {at}");
                        }
                        AssertTheActionBarFits(view);

                        // The row keeps its height, and the Layout button is the row's full height: the card is not taller.
                        Assert.Equal(Token("Size.Card.Row.ActionBar"), dock.ActualHeight);
                        Assert.True(layout.ActualHeight <= dock.ActualHeight + 0.5, $"Layout is taller than the action bar {at}");

                        double gap = BoundsIn(copy, dock).Left - BoundsIn(layout, dock).Right;
                        Assert.True(gap >= 8, $"the gap between Layout and Copy layout is {gap:F1} px {at}");
                        Assert.True(BoundsIn(turnOff, dock).Left >= BoundsIn(copy, dock).Right, $"Turn Off overlaps Copy layout {at}");
                    });
                }
            });

        [Fact]
        public void ActionBar_WithADraft_KeepsTheEightPixelGap_FromLayoutToCopyLayout_AtTheNarrowWidth() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                rig.Rx1.HasDraft = true;
                var view = rig.View(rig.Rx1, out var host, Token("Size.Card.Min"));
                Showing(host, () =>
                {
                    var dock = (FrameworkElement)Named(view, "FloatingActionDock").Single();
                    var layout = LayoutOf(view);
                    var copy = (Button)Named(view, "CopyLayoutHandle").Single();
                    Assert.True(Named(view, "DraftChip").Single().IsVisible, "the draft chip is not shown");
                    double gap = BoundsIn(copy, dock).Left - BoundsIn(layout, dock).Right;
                    Assert.True(gap >= 8, $"the gap between Layout and Copy layout is {gap:F1} px with a draft at the narrow width");
                });
            });

        [Theory]
        [InlineData(ThemeMode.Dark)]
        [InlineData(ThemeMode.Light)]
        public void LayoutIcon_AndOutline_DrawInThePaletteOfTheTheme(ThemeMode mode) =>
            RunSta(() =>
            {
                var theme = new ThemeService();
                try
                {
                    // The rig first: its MainViewModel builds a ThemeService that re-applies the saved (Dark) theme on construction.
                    var rig = new CopyRig();
                    theme.SetTheme(mode);
                    var view = rig.View(rig.Rx1, out var host, 613);
                    Showing(host, () =>
                    {
                        var layout = LayoutOf(view);
                        var icon = (Path)Named(view, "LayoutIcon").Single();
                        var chevron = FindAll<SymbolIcon>(layout).Single(i => i.Symbol == SymbolRegular.ChevronDown24);
                        var outline = (System.Windows.Controls.Border)Named(view, "LayoutSegmentsOutline").Single();
                        Assert.Equal(PaletteColor("Brush.Text.Secondary"), ColorOf(icon.Fill));
                        Assert.Equal(PaletteColor("Brush.Text.Secondary"), ColorOf(chevron.Foreground));
                        Assert.Equal(PaletteColor("Brush.Border.Control"), ColorOf(outline.BorderBrush));
                    });
                }
                finally { theme.SetTheme(ThemeMode.Dark); }
            });

        [Fact]
        public void LayoutGroup_FillsTheActionBarRow_AndTheRowIsStillTheActionBarToken() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host, 613);
                Showing(host, () =>
                {
                    var rows = ((Grid)Named(view, "CardGrid").Single()).RowDefinitions;
                    Assert.Equal(Token("Size.Card.Row.ActionBar"), rows[1].Height.Value);
                    var group = (Grid)Named(view, "LayoutSegments").Single();
                    Assert.Equal(Token("Size.Card.Row.ActionBar"), group.ActualHeight);
                });
            });
    }
}
