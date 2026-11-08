using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AvasRoutingTool.Controls;
using AvasRoutingTool.Views;
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

        // No 150 % DPI case on purpose: WPF lays out in device-independent units, so a 400 DIP card has the same layout at every DPI and
        // a LayoutTransform in DIPs would prove nothing. Real per-monitor DPI rendering needs a manual or screenshot check.
        [Fact]
        public void ActionBar_FitsA400pxCard() =>
            RunSta(() =>
            {
                foreach (bool draft in new[] { false, true })
                {
                    var rig = new CopyRig();
                    rig.Rx1.HasDraft = draft;
                    var view = rig.View(rig.Rx1, out var host, CarouselCardWidth);
                    Showing(host, () =>
                    {
                        string at = $"at a 400 px card{(draft ? " with a draft" : "")}";
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

        private static TextBlock LabelOf(DisplayCardView card) => (TextBlock)Named(card, "LayoutLabel").Single();

        [Fact]
        public void ActionBar_CopyWithNothingToCopy_HasNoHoverFill() =>
            RunSta(() =>
            {
                var style = (Style)Application.Current.FindResource("Style.Button.ActionBar.Copy");
                var off = style.Triggers.OfType<DataTrigger>()
                    .Single(t => t.Binding is System.Windows.Data.Binding b && b.Path.Path == "CanCopyFrom" && string.Equals(t.Value?.ToString(), "False", StringComparison.OrdinalIgnoreCase));
                var fill = off.Setters.OfType<Setter>().SingleOrDefault(x => x.Property == Control.BackgroundProperty);
                Assert.True(fill is not null, "a Copy layout that looks off still takes the hover fill");
                Assert.Equal(Brushes.Transparent.ToString(), fill.Value?.ToString());
            });

        [Fact]
        public void ActionBar_WideCardWithADraft_KeepsTheMoreLayoutsLabel_AndNothingJumpsWhenTheDraftToggles() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host, 613);
                Showing(host, () =>
                {
                    var copy = (Button)Named(view, "CopyLayoutHandle").Single();
                    var dock = (FrameworkElement)Named(view, "FloatingActionDock").Single();
                    double before = BoundsIn(copy, dock).Left;
                    Assert.True(LabelOf(view).IsVisible);

                    rig.Rx1.HasDraft = true;
                    Relayout(view);
                    Assert.True(LabelOf(view).IsVisible, "a 613 px card with a draft lost the More layouts label");
                    Assert.Equal(before, BoundsIn(copy, dock).Left, 0.5);
                });
            });

        [Fact]
        public void ActionBar_Card400WithADraft_CollapsesTheLabel_ButKeepsTheNameAndTooltip() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                rig.Rx1.HasDraft = true;
                var view = rig.View(rig.Rx1, out var host, CarouselCardWidth);
                Showing(host, () =>
                {
                    Assert.False(LabelOf(view).IsVisible, "the label should give way to the draft chip at 400 px");
                    var layout = LayoutOf(view);
                    Assert.Equal("More layouts for RX1_c059e_SFPA", System.Windows.Automation.AutomationProperties.GetName(layout));
                    Assert.Equal("More layouts", layout.ToolTip as string);
                });
            });

        [Fact]
        public void ActionBar_Card400WithoutADraft_KeepsTheLabel() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host, CarouselCardWidth);
                Showing(host, () => Assert.True(LabelOf(view).IsVisible));
            });

        [Fact]
        public void ActionBar_ResizingA400CardWithADraftTo613_BringsTheLabelBack_AndBackAgain() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                rig.Rx1.HasDraft = true;
                var view = rig.View(rig.Rx1, out var host, CarouselCardWidth);
                Showing(host, () =>
                {
                    Assert.False(LabelOf(view).IsVisible);
                    view.Width = 613;
                    Relayout(view);
                    Assert.True(LabelOf(view).IsVisible, "the label did not come back at 613 px");
                    view.Width = CarouselCardWidth;
                    Relayout(view);
                    Assert.False(LabelOf(view).IsVisible, "the label did not give way again at 400 px");
                    Relayout(view);
                    Assert.False(LabelOf(view).IsVisible, "the label flip-flops at 400 px");
                });
            });

        [Fact]
        public void DisplayCards_AreNeverNarrowerThanTheBarNeeds_InAnyLayoutThePanelCanProduce() =>
            RunSta(() =>
            {
                Assert.Equal(CarouselCardWidth, GridSpec.DisplayCard.MinCardWidth);
                Assert.Equal(CarouselCardWidth, Token("Size.Card.Display.Min"));
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host, 300);
                Assert.Equal(CarouselCardWidth, view.MinWidth);

                // Every width the layout produces (carousel and grid mode, 1 to 12 cards, 1024 to 3840 px wide, any height) is at least 400.
                foreach (int count in Enumerable.Range(1, 12))
                    foreach (double width in new[] { 1008.0, 1264.0, 1888.0, 2560.0, 3840.0 })
                        foreach (double height in new[] { 300.0, 435.0, 900.0, double.PositiveInfinity })
                        {
                            var plan = AdaptiveGridLayout.Compute(count, width, height, GridSpec.DisplayCard);
                            Assert.True(plan.CardWidth >= CarouselCardWidth - 0.01, $"{count} cards in {width}x{height}: {plan.CardWidth:F1} px");
                        }

                // And the bar fits the narrowest of them (a draft collapses the label, which is covered above).
                Showing(host, () =>
                {
                    view.Width = (double)GridSpec.DisplayCard.MinCardWidth;
                    Relayout(view);
                    AssertTheActionBarFits(view);
                });
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
