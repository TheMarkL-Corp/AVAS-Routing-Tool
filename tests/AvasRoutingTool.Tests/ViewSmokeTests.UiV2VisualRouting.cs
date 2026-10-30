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
        public void VisualRoutingView_SourcesRow_IsStarInTheOverview_AndAutoWhileComposing() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = new VisualRoutingView { DataContext = rig.Vm };
                Showing(view, () =>
                {
                    var rows = OverviewGrid(view).RowDefinitions;
                    Assert.True(rows[0].Height.IsStar && Math.Abs(rows[0].Height.Value - 2) < 0.001, $"overview sources row {rows[0].Height}");
                    Assert.True(rows[2].Height.IsStar);

                    Pump(() => rig.Vm.OpenComposerAsync(rig.Rx3, "matrix2x2"));
                    Relayout(view);
                    Assert.True(rig.Vm.IsComposing);
                    Assert.True(rows[0].Height.IsAuto, $"composer sources row {rows[0].Height}");
                    Assert.True(Named(view, "CompactSourcePalette").Single().IsVisible);
                    Assert.True(Named(view, "ComposerView").Single().IsVisible);

                    rig.Vm.CloseComposer();
                    Relayout(view);
                    Assert.True(rows[0].Height.IsStar);
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
        public void VisualRoutingView_SectionHeaders_UseTheSectionRole_WithSizedIcons() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = new VisualRoutingView { DataContext = rig.Vm };
                Showing(view, () =>
                {
                    var section = (Style)Application.Current.FindResource("Style.Text.Section");
                    foreach (var text in new[] { "VIDEO SOURCES (TX)", "TARGET DISPLAYS (RX)" })
                    {
                        var header = FindAll<TextBlock>(view).Single(t => t.Text == text);
                        Assert.Same(section, header.Style);
                        Assert.True(header.FontSize >= 14);
                    }
                    var iconSize = (double)Application.Current.FindResource("Size.Icon.S");
                    var icons = FindAll<System.Windows.Shapes.Path>(view).Where(p => p.Style == Application.Current.FindResource("Style.Icon.Section")).ToList();
                    Assert.True(icons.Count >= 2, "section icons use Style.Icon.Section");
                    Assert.All(icons, p => Assert.Equal(iconSize, p.ActualWidth));
                });
            });

        [Fact]
        public void VisualRoutingView_DiscoveredCounts_AreInfoBadges_AndHintsHaveNoGlyphIcons() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = new VisualRoutingView { DataContext = rig.Vm };
                Showing(view, () =>
                {
                    var info = Application.Current.FindResource("Style.Badge.Info");
                    var badges = FindAll<Badge>(view).Where(b => b.IsVisible && FindAll<TextBlock>(b).Any(t => t.Text.Contains("DISCOVERED"))).ToList();
                    Assert.Equal(2, badges.Count);
                    Assert.All(badges, b => Assert.Same(info, b.Style));
                    Assert.All(badges, b => Assert.Contains(FindAll<TextBlock>(b), t => t.Text.Contains("DISCOVERED")));
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
