using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AvasRoutingTool.Controls;
using Xunit;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// Round 3 action bar: "More layouts" and "Copy layout" are two ghost buttons of one style (Style.Button.ActionBar): the same height
    /// (Size.Touch.Min), the same horizontal padding (Size.Action.PadX), an icon + Size.Space.Action + label, and Size.Space.Action between them.
    /// The Layout button reads "More layouts" with the chevron after the label; the power button is a Material icon in the same colour.
    /// </summary>
    public partial class ViewSmokeTests
    {
        /// <summary>The carousel never makes a card narrower than this (DIPs): the action bar has to fit it.</summary>
        private const double CarouselCardWidth = 400;

        [Fact]
        public void ActionBar_LayoutAndCopy_ShareHeightAndPadding() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host, CarouselCardWidth);
                Showing(host, () =>
                {
                    var dock = (FrameworkElement)Named(view, "FloatingActionDock").Single();
                    var layout = LayoutOf(view);
                    var copy = (Button)Named(view, "CopyLayoutHandle").Single();

                    var style = Application.Current.FindResource("Style.Button.ActionBar");
                    Assert.Same(style, layout.Style);
                    Assert.True(ReferenceEquals(style, copy.Style) || ((Style)copy.Style).BasedOn == style, "Copy layout does not use Style.Button.ActionBar");

                    Assert.Equal(Token("Size.Touch.Min"), layout.ActualHeight);
                    Assert.Equal(Token("Size.Touch.Min"), copy.ActualHeight);
                    Assert.Equal(Token("Size.Action.PadX"), layout.Padding.Left);
                    Assert.Equal(layout.Padding, copy.Padding);
                    Assert.Equal(layout.Padding.Left, layout.Padding.Right);

                    double gap = BoundsIn(copy, dock).Left - BoundsIn(layout, dock).Right;
                    Assert.True(Math.Abs(gap - Token("Size.Space.Action")) <= 0.5, $"the gap is {gap:F1}, not Size.Space.Action {Token("Size.Space.Action")}");

                    // No fill, no outline at rest.
                    foreach (var button in new[] { layout, copy })
                    {
                        Assert.True(button.Background is null || (button.Background is SolidColorBrush b && b.Color.A == 0), $"{button.Name} has a fill at rest");
                        Assert.Equal(new Thickness(0), button.BorderThickness);
                    }
                    Assert.Empty(Named(view, "LayoutSegments"));
                    Assert.Empty(Named(view, "LayoutSegmentsOutline"));
                });
            });

        [Fact]
        public void ActionBar_LayoutButton_SaysMoreLayouts() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host, CarouselCardWidth);
                Showing(host, () =>
                {
                    var layout = LayoutOf(view);
                    var label = FindAll<TextBlock>(layout).Single(t => t.IsVisible && !string.IsNullOrEmpty(t.Text));
                    Assert.Equal("More layouts", label.Text);

                    var icons = FindAll<MaterialIcon>(layout);
                    var grid = icons.Single(i => i.Kind == "grid_view");
                    var chevron = icons.Single(i => i.Kind == "expand_more");
                    Assert.True(BoundsIn(grid, layout).Right <= BoundsIn(label, layout).Left + 0.5, "the grid icon is not before the label");
                    Assert.True(BoundsIn(chevron, layout).Left >= BoundsIn(label, layout).Right - 0.5, "the chevron is not after the label");
                    Assert.Equal(Token("Size.Icon.Layout"), grid.ActualWidth);
                    Assert.Equal(Token("Size.Icon.Layout"), chevron.ActualWidth);

                    Assert.Equal("More layouts for RX1_c059e_SFPA", System.Windows.Automation.AutomationProperties.GetName(layout));
                    Assert.Equal("More layouts", layout.ToolTip as string);
                });
            });

        [Theory]
        [InlineData(1.0)]
        [InlineData(1.5)]
        public void ActionBar_FitsA400pxCard_AtEveryDpi(double dpiScale) =>
            RunSta(() =>
            {
                foreach (bool draft in new[] { false, true })
                {
                    var rig = new CopyRig();
                    rig.Rx1.HasDraft = draft;
                    var view = rig.View(rig.Rx1, out var host, CarouselCardWidth);
                    // 150 % DPI: the same 400 DIP card drawn 1.5 x larger, so text is laid out and rounded at the larger device scale.
                    host.LayoutTransform = new ScaleTransform(dpiScale, dpiScale);
                    Showing(host, () =>
                    {
                        string at = $"at {dpiScale * 100:F0} % DPI{(draft ? " with a draft" : "")}";
                        var frame = (FrameworkElement)Named(view, "CardFrame").Single();
                        var layout = LayoutOf(view);
                        var copy = (Button)Named(view, "CopyLayoutHandle").Single();
                        var turnOff = (Button)Named(view, "TurnOffButton").Single();
                        var dock = (FrameworkElement)Named(view, "FloatingActionDock").Single();

                        foreach (var label in new[] { layout, copy }.SelectMany(b => FindAll<TextBlock>(b)).Where(t => t.IsVisible && !string.IsNullOrEmpty(t.Text)))
                        {
                            Assert.Equal(TextTrimming.None, label.TextTrimming);
                            AssertTextFits(label);
                        }
                        Assert.True(turnOff.IsVisible, $"the power button is hidden {at}");
                        AssertInside(turnOff, frame);
                        AssertInside(layout, dock);
                        AssertInside(copy, dock);
                        Assert.True(BoundsIn(turnOff, dock).Left >= BoundsIn(copy, dock).Right - 0.5, $"the power button overlaps Copy layout {at}");
                        if (draft)
                        {
                            // With a draft the chip needs the room of the More layouts label (a 400 px card has 374 px for the row): the Layout
                            // button keeps its icon and chevron, and nothing overlaps.
                            var chip = (FrameworkElement)Named(view, "DraftChip").Single();
                            Assert.True(chip.IsVisible, $"the draft chip is hidden {at}");
                            AssertInside(chip, dock);
                            Assert.True(BoundsIn(chip, dock).Left >= BoundsIn(copy, dock).Right - 0.5, $"the draft chip overlaps Copy layout {at}");
                            Assert.True(BoundsIn(turnOff, dock).Left >= BoundsIn(chip, dock).Right - 0.5, $"the power button overlaps the draft chip {at}");
                        }
                        else
                        {
                            Assert.Equal("More layouts", FindAll<TextBlock>(layout).Single(t => t.IsVisible).Text);
                        }
                    });
                }
            });

        [Fact]
        public void ActionBar_LayoutMenuStillOpens_OnTapAndKeyboard() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host, CarouselCardWidth);
                Showing(host, () =>
                {
                    var button = LayoutOf(view);
                    var popup = (Popup)Named(view, "LayoutMenuPopup").Single();
                    Assert.False(popup.IsOpen);

                    Click(button);
                    Assert.True(popup.IsOpen, "a tap on More layouts did not open the menu");
                    popup.IsOpen = false;
                    Idle();

                    Assert.True(button.Focus());
                    RaiseKey(button, Key.Enter);
                    Until(() => popup.IsOpen, "Enter on More layouts did not open the menu");
                    var tile = (Button)Named((FrameworkElement)popup.Child, "LayoutMenuTile").First();
                    RaiseKey(tile, Key.Escape);
                    Until(() => !popup.IsOpen, "Esc did not close the menu");
                });
            });
    }
}
