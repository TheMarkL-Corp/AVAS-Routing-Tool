using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AvasRoutingTool.Controls;
using AvasRoutingTool.Services;
using AvasRoutingTool.Views;
using Xunit;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// The "More layouts" button on a display card (round 3, replacing the 64 x 56 segment of task A14): a ghost action-bar button, Size.Touch.Min
    /// high, [grid_view icon] label [expand_more chevron], icon and chevron the Size.Icon.Layout token, drawn in the primary text colour, with no
    /// fill or outline of its own. The action bar row is still Size.Card.Row.ActionBar (56), so the card does not get taller.
    /// </summary>
    public partial class ViewSmokeTests
    {
        private static Button LayoutOf(DisplayCardView card) => (Button)Named(card, "LayoutButton").Single();

        [Fact]
        public void LayoutButton_IsAtLeastTheTouchFloor_AndAsHighAsTheActionBarButtons() =>
            RunSta(() =>
            {
                foreach (double width in ActionBarWidths())
                {
                    var rig = new CopyRig();
                    var view = rig.View(rig.Rx1, out var host, width);
                    Showing(host, () =>
                    {
                        var layout = LayoutOf(view);
                        Assert.True(layout.ActualWidth >= Token("Size.Touch.Min"), $"Layout is {layout.ActualWidth:F1} wide at a {width} px card");
                        Assert.Equal(Token("Size.Touch.Min"), layout.ActualHeight);
                        Assert.Equal(Token("Size.Touch.Min"), layout.MinHeight);
                    });
                }
            });

        [Fact]
        public void LayoutButton_HasNoFillOrOutline_AtRest_AndNoSegmentGroup() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host, 613);
                Showing(host, () =>
                {
                    var layout = LayoutOf(view);
                    Assert.Empty(Named(view, "LayoutSegments"));
                    Assert.Empty(Named(view, "LayoutSegmentsOutline"));
                    var bd = (Border)layout.Template.FindName("bd", layout);
                    Assert.Equal(0, ColorOf(bd.Background).A);
                    Assert.Equal(0.0, bd.BorderThickness.Left);
                    // No Border in the button draws a visible edge (the keyboard ring is collapsed until the keyboard moves focus here).
                    Assert.Empty(FindAll<Border>(layout).Where(b => b.IsVisible && b.BorderThickness.Left > 0));
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
        public void LayoutIconAndChevron_AreTheIconToken_InsideTheButton_OnOneLineWithTheLabel() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host, 613);
                Showing(host, () =>
                {
                    var layout = LayoutOf(view);
                    var icon = (MaterialIcon)Named(view, "LayoutIcon").Single();
                    var chevron = (MaterialIcon)Named(view, "LayoutChevron").Single();
                    var label = FindAll<TextBlock>(layout).Single(t => t.IsVisible && t.Text == "More layouts");

                    foreach (var glyph in new[] { icon, chevron })
                        Assert.True(Math.Abs(glyph.ActualWidth - Token("Size.Icon.Layout")) <= 0.5 && Math.Abs(glyph.ActualHeight - Token("Size.Icon.Layout")) <= 0.5,
                            $"the {glyph.Kind} icon is {glyph.ActualWidth:F1}x{glyph.ActualHeight:F1}, not {Token("Size.Icon.Layout")}");

                    var ib = BoundsIn(icon, layout);
                    var lb = BoundsIn(label, layout);
                    var cb = BoundsIn(chevron, layout);
                    double w = layout.ActualWidth, h = layout.ActualHeight;
                    foreach (var (name, box) in new[] { ("icon", ib), ("label", lb), ("chevron", cb) })
                        Assert.True(box.Left >= 0 && box.Top >= 0 && box.Right <= w && box.Bottom <= h, $"the {name} {box} is outside the {w:F0}x{h:F0} button");

                    // Icon, label, chevron in a row with Size.Space.Action between them, and the same padding at both ends.
                    Assert.True(Math.Abs(lb.Left - ib.Right - Token("Size.Space.Action")) <= 0.5, $"the icon-to-label gap is {lb.Left - ib.Right:F1}");
                    Assert.True(Math.Abs(cb.Left - lb.Right - Token("Size.Space.Action")) <= 0.5, $"the label-to-chevron gap is {cb.Left - lb.Right:F1}");
                    Assert.True(Math.Abs(ib.Left - Token("Size.Action.PadX")) <= 0.5 && Math.Abs(w - cb.Right - Token("Size.Action.PadX")) <= 0.5,
                        $"the padding is {ib.Left:F1} left and {w - cb.Right:F1} right");
                    foreach (var box in new[] { ib, lb, cb })
                        Assert.True(Math.Abs((box.Top + box.Bottom) / 2 - h / 2) <= 1.5, $"{box} is not centred on the button's {h / 2:F1}");
                });
            });

        [Fact]
        public void ActionBar_Buttons_FitAtBothWidths_WithTheActionGap() =>
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

                        // The row keeps its height: the 48 px buttons sit in the 56 px bar and the card is not taller.
                        Assert.Equal(Token("Size.Card.Row.ActionBar"), dock.ActualHeight);
                        Assert.True(layout.ActualHeight <= dock.ActualHeight + 0.5, $"Layout is taller than the action bar {at}");

                        double gap = BoundsIn(copy, dock).Left - BoundsIn(layout, dock).Right;
                        Assert.True(Math.Abs(gap - Token("Size.Space.Action")) <= 0.5, $"the gap between Layout and Copy layout is {gap:F1} px {at}");
                        Assert.True(BoundsIn(turnOff, dock).Left >= BoundsIn(copy, dock).Right, $"Turn Off overlaps Copy layout {at}");
                    });
                }
            });

        [Fact]
        public void ActionBar_WithADraft_KeepsTheActionGap_FromLayoutToCopyLayout_AtTheCarouselWidth() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                rig.Rx1.HasDraft = true;
                var view = rig.View(rig.Rx1, out var host, 400);
                Showing(host, () =>
                {
                    var dock = (FrameworkElement)Named(view, "FloatingActionDock").Single();
                    var layout = LayoutOf(view);
                    var copy = (Button)Named(view, "CopyLayoutHandle").Single();
                    Assert.True(Named(view, "DraftChip").Single().IsVisible, "the draft chip is not shown");
                    double gap = BoundsIn(copy, dock).Left - BoundsIn(layout, dock).Right;
                    Assert.True(Math.Abs(gap - Token("Size.Space.Action")) <= 0.5, $"the gap between Layout and Copy layout is {gap:F1} px with a draft");
                });
            });

        [Theory]
        [InlineData(ThemeMode.Dark)]
        [InlineData(ThemeMode.Light)]
        public void LayoutIconAndChevron_DrawInThePrimaryTextColourOfTheTheme(ThemeMode mode) =>
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
                        var icon = (MaterialIcon)Named(view, "LayoutIcon").Single();
                        var chevron = (MaterialIcon)Named(view, "LayoutChevron").Single();
                        var label = FindAll<TextBlock>(layout).Single(t => t.IsVisible && t.Text == "More layouts");
                        var power = FindAll<MaterialIcon>((Button)Named(view, "TurnOffButton").Single()).Single();
                        var expected = PaletteColor("Brush.Text.Primary");
                        Assert.Equal(expected, ColorOf(icon.Foreground));
                        Assert.Equal(expected, ColorOf(chevron.Foreground));
                        Assert.Equal(expected, ColorOf(label.Foreground));
                        Assert.Equal(expected, ColorOf(power.Foreground));
                    });
                }
                finally { theme.SetTheme(ThemeMode.Dark); }
            });

        [Fact]
        public void ActionBarRow_IsStillTheActionBarToken_AndTheLayoutButtonIsCentredInIt() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host, 613);
                Showing(host, () =>
                {
                    var rows = ((Grid)Named(view, "CardGrid").Single()).RowDefinitions;
                    Assert.Equal(Token("Size.Card.Row.ActionBar"), rows[1].Height.Value);
                    var dock = (FrameworkElement)Named(view, "FloatingActionDock").Single();
                    Assert.Equal(Token("Size.Card.Row.ActionBar"), dock.ActualHeight);
                    var box = BoundsIn(LayoutOf(view), dock);
                    Assert.True(Math.Abs((box.Top + box.Bottom) / 2 - dock.ActualHeight / 2) <= 0.5, $"Layout {box} is not centred in the {dock.ActualHeight} px bar");
                });
            });
    }
}
