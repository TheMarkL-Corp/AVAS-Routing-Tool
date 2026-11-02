using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Wpf.Ui.Controls;
using Xunit;
using Button = System.Windows.Controls.Button;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// UI v2 inline layout presets (critique M3): the four common layouts are one tap away on the display card's action bar
    /// from <c>Size.Breakpoint.CardPresets</c> on; below it the Layout dropdown alone is shown.
    /// </summary>
    public partial class ViewSmokeTests
    {
        /// <summary>The card chrome around its action bar: frame 2 + padding 24 (the same 26 px as GridSpec.DisplayCard.ChromeHeight).</summary>
        private const double CardChromeWidth = 26;

        private static Button InlinePreset(DependencyObject view, string id) =>
            Named(view, "InlinePreset").Cast<Button>().Single(b => ((LayoutItemViewModel)b.DataContext).Id == id);

        private static string ItemStatus(DependencyObject item) => System.Windows.Automation.AutomationProperties.GetItemStatus(item);

        private static bool CheckIsShown(DependencyObject item) =>
            FindAll<SymbolIcon>(item).Any(i => i.Symbol == SymbolRegular.Checkmark24 && i.IsVisible);

        /// <summary>Two rectangles lie side by side or one above the other (touching is not overlapping).</summary>
        private static bool Separated(Rect a, Rect b) =>
            a.Right <= b.Left + 0.5 || b.Right <= a.Left + 0.5 || a.Bottom <= b.Top + 0.5 || b.Bottom <= a.Top + 0.5;

        /// <summary>
        /// Hosts a routed card with a draft at <paramref name="cardWidth"/>: every action-bar part is shown inside the bar, none overlaps another.
        /// </summary>
        private static void AssertTheBarFits(double cardWidth)
        {
            var rig = new CopyRig();
            rig.Rx1.HasDraft = true;
            var view = rig.View(rig.Rx1, out var host, cardWidth);
            Showing(host, () =>
            {
                var dock = (Border)Named(view, "FloatingActionDock").Single();
                Assert.True(dock.ActualWidth >= Token("Size.Breakpoint.CardPresets"), $"the bar is {dock.ActualWidth:F1} px at a {cardWidth} px card");
                var parts = new[] { "InlinePresets", "LayoutButton", "CopyLayoutHandle", "DraftChip", "TurnOffButton" }
                    .Select(name => (FrameworkElement)Named(view, name).Single()).ToList();
                foreach (var part in parts)
                {
                    Assert.True(part.IsVisible, $"{part.Name} is not shown at a {cardWidth} px card");
                    AssertInside(part, dock);
                }
                for (int i = 0; i < parts.Count; i++)
                    for (int j = i + 1; j < parts.Count; j++)
                        Assert.True(Separated(BoundsIn(parts[i], dock), BoundsIn(parts[j], dock)),
                            $"{parts[i].Name} {BoundsIn(parts[i], dock)} overlaps {parts[j].Name} {BoundsIn(parts[j], dock)} at a {cardWidth} px card");
            });
        }

        [Fact]
        public void DisplayCard_Wide_ShowsFourInlinePresets_AtLeast48_InTheBarOrder() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host, 613);
                Showing(host, () =>
                {
                    Assert.True(Named(view, "InlinePresets").Single().IsVisible, "the inline presets are not shown at 613 px");
                    var buttons = Named(view, "InlinePreset").Cast<Button>().Where(b => b.IsVisible).ToList();
                    Assert.Equal(new[] { "single", "pip", "side_by_side", "matrix2x2" }, buttons.Select(b => ((LayoutItemViewModel)b.DataContext).Id));
                    Assert.Equal(new[] { 1, 2, 2, 4 }, buttons.Select(b => FindAll<LayoutSchematic>(b).Single().CellCount));

                    var frame = Named(view, "CardFrame").Single();
                    foreach (var b in buttons)
                    {
                        Assert.True(b.ActualWidth >= Token("Size.Touch.Min") && b.ActualHeight >= Token("Size.Touch.Min"), $"{b.ActualWidth}x{b.ActualHeight}");
                        AssertInside(b, frame);
                    }
                    for (int i = 1; i < buttons.Count; i++)
                        Assert.True(BoundsIn(buttons[i], view).Left >= BoundsIn(buttons[i - 1], view).Right - 0.5, "the presets overlap or are out of order");
                });
            });

        [Fact]
        public void DisplayCard_AtTheMinimumWidth_KeepsTheLayoutDropdown_AndNoInlinePresets() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host, Token("Size.Card.Min"));
                Showing(host, () =>
                {
                    Assert.False(Named(view, "InlinePresets").Single().IsVisible);
                    Assert.Empty(Named(view, "InlinePreset").Where(b => b.IsVisible));
                    var layout = (Button)Named(view, "LayoutButton").Single();
                    Assert.True(layout.IsVisible && layout.ActualHeight >= Token("Size.Touch.Min"), $"Layout is {layout.ActualHeight} high");
                });
            });

        [Fact]
        public void DisplayCard_ActionBar_EverythingFits_WithTheDraftChip_AtTheBreakpoint() =>
            RunSta(() =>
            {
                // The breakpoint is the bar's width (the card less its 26 px chrome); also checked at two more card widths.
                AssertTheBarFits(Token("Size.Breakpoint.CardPresets") + CardChromeWidth);
                AssertTheBarFits(613);
                AssertTheBarFits(699);
            });

        [Fact]
        public void DisplayCard_ActionBar_Breakpoint_IsTheNaturalWidthOfTheBar_RoundedUpToTen() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                rig.Rx1.HasDraft = true;
                var view = rig.View(rig.Rx1, out var host, 900); // wide enough for the spacer column to take the rest
                Showing(host, () =>
                {
                    var bar = (Grid)((Border)Named(view, "FloatingActionDock").Single()).Child;
                    // Every fixed column (all but the spacer, index 4) takes its natural width at this width.
                    double natural = Enumerable.Range(0, bar.ColumnDefinitions.Count).Where(i => i != 4).Sum(i => bar.ColumnDefinitions[i].ActualWidth);
                    double breakpoint = Token("Size.Breakpoint.CardPresets");
                    Assert.True(breakpoint >= natural, $"the bar needs {natural:F1} px, the breakpoint is {breakpoint}");
                    Assert.True(breakpoint - natural < 10, $"the bar needs {natural:F1} px; {breakpoint} is more than 10 px above it");
                });
            });

        [Fact]
        public void InlinePreset_Single_AppliesAtOnce() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host, 613);
                Showing(host, () =>
                {
                    Assert.True(rig.Rx1.IsMultiView);
                    Click(InlinePreset(view, "single"));
                    Until(() => !rig.Rx1.IsMultiView, "the Single View preset did not apply at once");
                    Assert.Null(rig.Vm.ActiveComposer);
                });
            });

        [Fact]
        public void InlinePreset_MultiView_OpensTheComposerStaged_AndChangesNothingOnScreen() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host, 613);
                Showing(host, () =>
                {
                    string badgeBefore = rig.Rx1.ModeBadge;
                    rig.Client.MultiViewCalls.Clear();

                    Click(InlinePreset(view, "pip"));
                    Until(() => rig.Vm.ActiveComposer != null, "the Composer did not open");
                    Assert.Equal("pip", rig.Vm.ActiveComposer!.Draft!.Plan.Id);

                    // Nothing reaches the screen until Take: the display still shows its Matrix 2x2.
                    Assert.Empty(rig.Client.MultiViewCalls);
                    Assert.Equal(badgeBefore, rig.Rx1.ModeBadge);
                    Assert.True(rig.Rx1.LayoutMenu.Single(l => l.Id == "matrix2x2").IsOnScreen);
                });
            });

        [Fact]
        public void InlinePreset_MarksTheOnScreenLayout_WithBorderIconAndItemStatus() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host, 613);
                Showing(host, () =>
                {
                    var accent = ((System.Windows.Media.SolidColorBrush)Application.Current.FindResource("Brush.Accent.Primary")).Color;
                    var presets = Named(view, "InlinePreset").Cast<Button>().ToList();
                    Assert.Equal(4, presets.Count);
                    foreach (var preset in presets)
                    {
                        string id = ((LayoutItemViewModel)preset.DataContext).Id;
                        var edge = ((System.Windows.Media.SolidColorBrush)((Border)preset.Template.FindName("bd", preset)).BorderBrush).Color;
                        if (id == "matrix2x2")
                        {
                            Assert.True(CheckIsShown(preset), "the on-screen preset has no visible checkmark");
                            Assert.Equal("On screen", ItemStatus(preset));
                            Assert.Equal(accent, edge);
                        }
                        else
                        {
                            Assert.False(CheckIsShown(preset), $"{id} shows a checkmark but is not on screen");
                            Assert.Equal(string.Empty, ItemStatus(preset));
                            Assert.NotEqual(accent, edge);
                        }
                    }
                });
            });

        [Fact]
        public void AnUnroutedDisplay_MarksNoPreset() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx3, out var host, 613);
                Showing(host, () =>
                {
                    var presets = Named(view, "InlinePreset").Cast<Button>().ToList();
                    Assert.Equal(4, presets.Count(p => p.IsVisible));
                    Assert.All(presets, p => Assert.False(CheckIsShown(p)));
                    Assert.All(presets, p => Assert.Equal(string.Empty, ItemStatus(p)));
                });
            });

        [Fact]
        public void InlinePreset_SchematicTokens_AreTheInlineSize() =>
            RunSta(() =>
            {
                Assert.Equal(28.0, Token("Size.Schematic.Inline.Width"));
                Assert.Equal(16.0, Token("Size.Schematic.Inline.Height"));
            });
    }
}
