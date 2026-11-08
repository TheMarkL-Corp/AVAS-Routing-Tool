using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Wpf.Ui.Controls;
using Xunit;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AvasRoutingTool.Tests
{
    /// <summary>UI v2 overview (plan task A7): composer/overview rows, arm banner, section headers, badges, hint text.</summary>
    public partial class ViewSmokeTests
    {
        private static Grid OverviewGrid(VisualRoutingView view) => (Grid)Named(view, "OverviewGrid").Single();

        [Fact]
        public void VisualRoutingView_SourcesRow_IsAutoInTheOverview_AndWhileComposing() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = new VisualRoutingView { DataContext = rig.Vm };
                Showing(view, () =>
                {
                    var rows = OverviewGrid(view).RowDefinitions;
                    Assert.True(rows[0].Height.IsAuto, $"overview sources row {rows[0].Height}"); // A9: the strip sizes to its tiles
                    Assert.True(rows[2].Height.IsStar);

                    Pump(() => rig.Vm.OpenComposerAsync(rig.Rx3, "matrix2x2"));
                    Relayout(view);
                    Assert.True(rig.Vm.IsComposing);
                    Assert.True(rows[0].Height.IsAuto, $"composer sources row {rows[0].Height}");
                    Assert.True(Named(view, "CompactSourcePalette").Single().IsVisible);
                    Assert.True(Named(view, "ComposerView").Single().IsVisible);

                    rig.Vm.CloseComposer();
                    Relayout(view);
                    Assert.True(rows[0].Height.IsAuto);
                    Assert.False(Named(view, "CompactSourcePalette").Single().IsVisible);
                    Assert.False(Named(view, "ComposerView").Single().IsVisible);
                });
            });

        [Fact]
        public void VisualRoutingView_ArmBanner_ShowsWhileSomethingIsArmed_IsAnnounced_AndStopDisarms() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = new VisualRoutingView { DataContext = rig.Vm };
                Showing(view, () =>
                {
                    var banner = Named(view, "ArmBanner").Single();
                    Assert.False(banner.IsVisible);
                    Assert.False(rig.Vm.HasArmBanner);

                    rig.Vm.ArmSource(rig.Tx2);
                    Relayout(view);
                    Assert.True(rig.Vm.HasArmBanner);
                    Assert.True(banner.IsVisible);
                    var label = (TextBlock)Named(view, "ArmBannerLabel").Single();
                    Assert.True(label.IsVisible);
                    Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(label));
                    Assert.False(Named(view, "CopyArmBannerLabel").Single().IsVisible);
                    Assert.True(label.FontSize >= 14);

                    var stop = Named(view, "StopPlacingButton").Single();
                    Assert.True(stop.ActualHeight >= 48);
                    Click(stop);
                    Relayout(view);
                    Assert.False(rig.Vm.HasArmBanner);
                    Assert.False(banner.IsVisible);
                });
            });

        [Fact]
        public void VisualRoutingView_SectionHeaders_AreTitleSizedLevel2Headings_WithSizedIcons() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = new VisualRoutingView { DataContext = rig.Vm };
                Showing(view, () =>
                {
                    // Finding M6: sentence case at title size (14 px, primary), still announced as a level 2 heading.
                    var title = (Style)Application.Current.FindResource("Style.Text.Title");
                    foreach (var text in new[] { "Video sources (TX)", "Target displays (RX)" })
                    {
                        var header = FindAll<TextBlock>(view).Single(t => t.Text == text);
                        Assert.Same(title, header.Style);
                        Assert.True(header.FontSize >= 14);
                        Assert.Equal(AutomationHeadingLevel.Level2, AutomationProperties.GetHeadingLevel(header));
                    }
                    var iconSize = (double)Application.Current.FindResource("Size.Icon.S");
                    var icons = FindAll<AvasRoutingTool.Controls.MaterialIcon>(view).Where(p => p.Style == Application.Current.FindResource("Style.Icon.Section")).ToList();
                    Assert.True(icons.Count >= 2, "section icons use Style.Icon.Section");
                    Assert.All(icons, p => Assert.Equal(iconSize, p.ActualWidth));
                });
            });

        [Fact]
        public void VisualRoutingView_SectionHints_AreSecondaryCaptions_NotMuted() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = new VisualRoutingView { DataContext = rig.Vm };
                Showing(view, () =>
                {
                    // Finding M6: hierarchy title 14 px primary > hint 12 px secondary > counts 12 px secondary.
                    var caption = Application.Current.FindResource("Style.Text.Caption");
                    var secondary = ((System.Windows.Media.SolidColorBrush)Application.Current.FindResource("Brush.Text.Secondary")).Color;
                    void AssertSecondaryCaptions(string when)
                    {
                        // "Tap a source, then" (not the Composer's own "Tap a source above, ..." hint, which it owns).
                        var hints = FindAll<TextBlock>(view).Where(t => t.IsVisible && (t.Text.StartsWith("Tap a source, then") || t.Text.StartsWith("Tap a layout"))).ToList();
                        Assert.NotEmpty(hints);
                        foreach (var hint in hints)
                        {
                            Assert.True(hint.FontSize >= 12, $"{when}: '{hint.Text}' is {hint.FontSize}px");
                            Assert.Same(caption, hint.Style);
                            Assert.Equal(secondary, ((System.Windows.Media.SolidColorBrush)hint.Foreground).Color);
                        }
                        var counts = FindAll<TextBlock>(view).Where(t => t.IsVisible && t.Text.Contains("discovered")).ToList();
                        Assert.NotEmpty(counts);
                        foreach (var count in counts)
                            Assert.Equal(secondary, ((System.Windows.Media.SolidColorBrush)count.Foreground).Color);
                    }

                    AssertSecondaryCaptions("overview");
                    Pump(() => rig.Vm.OpenComposerAsync(rig.Rx3, "matrix2x2"));
                    Relayout(view);
                    Assert.True(rig.Vm.IsComposing);
                    AssertSecondaryCaptions("composing");
                });
            });

        [Fact]
        public void VisualRoutingView_DiscoveredCounts_ArePlainCaption_AndHintsHaveNoGlyphIcons() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = new VisualRoutingView { DataContext = rig.Vm };
                Showing(view, () =>
                {
                    // One accent: the counts are plain caption text ("2 discovered"), not a coloured pill.
                    var caption = Application.Current.FindResource("Style.Text.Caption");
                    var counts = FindAll<TextBlock>(view).Where(t => t.IsVisible && t.Text.Contains("discovered")).ToList();
                    Assert.Equal(2, counts.Count);
                    Assert.All(counts, t => Assert.Same(caption, t.Style));
                    Assert.Empty(FindAll<Badge>(view).Where(b => b.IsVisible && FindAll<TextBlock>(b).Any(t => t.Text.Contains("DISCOVERED"))));
                    Assert.DoesNotContain(FindAll<TextBlock>(view).Where(t => t.DataContext == rig.Vm), t => t.IsVisible && t.Text.Any(c => c >= '←' && c <= '➿'));
                    foreach (var tb in FindAll<TextBlock>(view).Where(t => t.IsVisible && !string.IsNullOrWhiteSpace(t.Text) && t.DataContext == rig.Vm))
                        Assert.True(tb.FontSize >= 12, $"'{tb.Text}' is {tb.FontSize}px");
                });
            });

        [Fact]
        public void VisualRoutingView_Divider_IsAOneLineBorder() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = new VisualRoutingView { DataContext = rig.Vm };
                Showing(view, () =>
                {
                    var divider = (Border)Named(view, "SectionDivider").Single();
                    Assert.Equal(new Thickness(0, 1, 0, 0), divider.BorderThickness);
                    Assert.Equal(1, divider.ActualHeight);
                });
            });
    }
}
