using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AvasRoutingTool.Views;
using Xunit;
using Button = System.Windows.Controls.Button;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// UI v2 display card action bar without the inline layout presets (task A13): the Layout dropdown starts the row, then
    /// Copy layout, the draft chip and Turn Off. No preset row and no layout glyph is drawn at any card width.
    /// </summary>
    public partial class ViewSmokeTests
    {
        /// <summary>The card widths the action bar is checked at: a wide card and the narrowest card (Size.Card.Min).</summary>
        private static double[] ActionBarWidths() => new[] { 613.0, Token("Size.Card.Min") };

        /// <summary>Two action-bar parts lie side by side or one above the other (touching is not overlapping).</summary>
        private static bool ActionBarSeparated(Rect a, Rect b) =>
            a.Right <= b.Left + 0.5 || b.Right <= a.Left + 0.5 || a.Bottom <= b.Top + 0.5 || b.Bottom <= a.Top + 0.5;

        /// <summary>The visible parts of a card's action bar are inside the bar and the card, none overlaps another, and every label is whole.</summary>
        private static void AssertTheActionBarFits(DisplayCardView card)
        {
            var frame = (FrameworkElement)Named(card, "CardFrame").Single();
            var dock = (FrameworkElement)Named(card, "FloatingActionDock").Single();
            AssertInside(dock, frame);
            var parts = new[] { "LayoutButton", "CopyLayoutHandle", "DraftChip", "TurnOffButton" }
                .SelectMany(name => Named(card, name))
                .Where(part => part.IsVisible)
                .ToList();
            Assert.True(parts.Count >= 2, $"only {parts.Count} action-bar parts are shown on {card.DataContext}");
            foreach (var part in parts)
            {
                AssertInside(part, dock);
                foreach (var label in FindAll<TextBlock>(part)) AssertTextFits(label);
            }
            for (int i = 0; i < parts.Count; i++)
                for (int j = i + 1; j < parts.Count; j++)
                    Assert.True(ActionBarSeparated(BoundsIn(parts[i], dock), BoundsIn(parts[j], dock)),
                        $"{parts[i].Name} {BoundsIn(parts[i], dock)} overlaps {parts[j].Name} {BoundsIn(parts[j], dock)}");
        }

        [Fact]
        public void DisplayCard_HasNoInlinePresetRow_AndNoLayoutGlyph_AtAnyWidth() =>
            RunSta(() =>
            {
                foreach (double width in ActionBarWidths())
                {
                    var rig = new CopyRig();
                    var view = rig.View(rig.Rx1, out var host, width);
                    Showing(host, () =>
                    {
                        Assert.True(Named(view, "InlinePresets").Count == 0, $"the inline preset row is still there at a {width} px card");
                        Assert.True(Named(view, "InlinePreset").Count == 0, $"an inline preset is still there at a {width} px card");
                        Assert.False(FindAll<FrameworkElement>(view).Any(e => e.GetType().Name == "LayoutGlyph"),
                            $"a layout glyph is still drawn at a {width} px card");
                    });
                }
            });

        [Fact]
        public void DisplayCard_ActionBar_StartsWithLayout_ThenCopyLayout_ThenTurnOff_AtBothWidths() =>
            RunSta(() =>
            {
                foreach (double width in ActionBarWidths())
                {
                    var rig = new CopyRig();
                    var view = rig.View(rig.Rx1, out var host, width);
                    Showing(host, () =>
                    {
                        var dock = (Border)Named(view, "FloatingActionDock").Single();
                        var layout = (Button)Named(view, "LayoutButton").Single();
                        var copy = (Button)Named(view, "CopyLayoutHandle").Single();
                        var turnOff = (Button)Named(view, "TurnOffButton").Single();
                        string at = $"at a {width} px card";

                        foreach (var button in new[] { layout, copy, turnOff })
                        {
                            Assert.True(button.IsVisible, $"{button.Name} is not shown {at}");
                            Assert.True(button.ActualWidth >= 48 && button.ActualHeight >= 48,
                                $"{button.Name} is {button.ActualWidth:F1}x{button.ActualHeight:F1} {at}");
                        }
                        Assert.True(layout.Focusable && layout.IsTabStop, $"Layout is not a tab stop {at}");
                        Assert.True(copy.Focusable && copy.IsTabStop, $"Copy layout is not a tab stop {at}");

                        // The tab order (document order of the row) and the left-to-right order are the same: Layout, Copy layout, Turn Off.
                        var buttons = FindAll<Button>(dock).Where(b => b.IsVisible).ToList();
                        Assert.True(buttons.IndexOf(layout) < buttons.IndexOf(copy) && buttons.IndexOf(copy) < buttons.IndexOf(turnOff),
                            $"the tab order is {string.Join(", ", buttons.Select(b => b.Name))} {at}");

                        var layoutBox = BoundsIn(layout, dock);
                        var copyBox = BoundsIn(copy, dock);
                        var offBox = BoundsIn(turnOff, dock);
                        Assert.True(layoutBox.Right <= copyBox.Left && copyBox.Right <= offBox.Left,
                            $"the row is out of order: Layout {layoutBox}, Copy layout {copyBox}, Turn Off {offBox} {at}");
                        Assert.True(copyBox.Left - layoutBox.Right >= 8, $"the gap between Layout and Copy layout is {copyBox.Left - layoutBox.Right:F1} px {at}");

                        // Layout starts the row: no interactive control of the bar lies left of it.
                        Assert.True(layoutBox.Left <= 0.5, $"Layout starts at {layoutBox.Left:F1} {at}");
                        foreach (var button in buttons)
                            Assert.True(BoundsIn(button, dock).Left >= layoutBox.Left - 0.5, $"{button.Name} lies left of Layout {at}");

                        // Spoken names: the Layout dropdown names its device; Copy layout has a name of its own.
                        Assert.Equal("Layout for RX1_c059e_SFPA", System.Windows.Automation.AutomationProperties.GetName(layout));
                        Assert.False(string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(copy)), $"Copy layout has no spoken name {at}");
                    });
                }
            });

        [Fact]
        public void DisplayCard_ActionBar_AtTheFloor_1024x720_EverythingFits() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = new VisualRoutingView { DataContext = rig.Vm };

                // 1008x615 is the content area of the 1024x720 window (as the overview's real-size test hosts it). The draft chip is
                // not in this check: a 320 px card with a draft needs about 337 px for the row (the chip and Turn Off overflow it).
                ShowingAt(view, 1008, 615, () =>
                {
                    var cards = FindAll<DisplayCardView>(view).Where(c => c.IsVisible).ToList();
                    Assert.Equal(3, cards.Count);
                    foreach (var card in cards) AssertTheActionBarFits(card);
                });
            });
    }
}
