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

namespace AvasRoutingTool.Tests
{
    /// <summary>Round 3, Main Window: placeholders on the always-dark preview tile are legible in both themes, a Copy layout with nothing to copy is dimmed by colour (not by opacity), and the arm banner's Stop clears the banner's ring.</summary>
    public partial class ViewSmokeTests
    {
        [Theory]
        [InlineData(ThemeMode.Dark)]
        [InlineData(ThemeMode.Light)]
        public void PreviewTile_PlaceholderText_And_Glyph_AreLegibleOnTheTile(ThemeMode mode) =>
            RunSta(() =>
            {
                try
                {
                    new ThemeService().SetTheme(mode);
                    var tile = BrushColour("Brush.Surface.PreviewTile");
                    var glyph = (SolidColorBrush)new MaterialIcon { Style = (Style)Application.Current.FindResource("Style.Icon.Placeholder") }.Foreground;
                    Assert.True(RatioOf(glyph.Color, tile) >= 4.5, $"placeholder glyph on the preview tile {RatioOf(glyph.Color, tile):F2}:1 in {mode}");
                    Assert.True(RatioOf(BrushColour("Brush.Text.OnPreviewMuted"), tile) >= 4.5);

                    // No placeholder on a source card is thinned by Opacity (it would drop the glyph under 3:1)
                    var vm = UiV2Source();
                    SourceCardAt(vm, view =>
                    {
                        foreach (var icon in FindAll<MaterialIcon>(view).Where(i => ReferenceEquals(i.Style, Application.Current.FindResource("Style.Icon.Placeholder"))))
                            Assert.True(icon.Opacity >= 1.0, $"a placeholder glyph is dimmed to {icon.Opacity} in {mode}");
                    });
                }
                finally { new ThemeService().SetTheme(ThemeMode.Dark); }
            });

        [Fact]
        public void DisplayCard_SlotLabel_IsNotThinnedByOpacity()
        {
            var xaml = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "src", "AvasRoutingTool", "Views", "DisplayCardView.xaml"));
            var i = xaml.IndexOf("Text=\"{Binding Label, Mode=OneWay}\"", StringComparison.Ordinal);
            Assert.True(i > 0);
            var end = xaml.IndexOf("/>", i, StringComparison.Ordinal);
            var element = xaml.Substring(i, end - i);
            Assert.DoesNotContain("Opacity", element);
            Assert.Contains("Brush.Text.OnPreviewMuted", element);
        }

        private static string RepoRoot()
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "AvasRoutingTool.sln"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
        }

        [Fact]
        public void LightTheme_CopyLayoutWithNothingToCopy_IsDimmedByColour_AtLeast4_5To1() =>
            RunSta(() =>
            {
                try
                {
                    new ThemeService().SetTheme(ThemeMode.Light);
                    var rig = new CopyRig();
                    var rx3 = rig.View(rig.Rx3, out var host);
                    Showing(host, () =>
                    {
                        var handle = (Button)Named(rx3, "CopyLayoutHandle").Single();
                        Assert.False(rig.Rx3.CanCopyFrom);
                        Assert.Equal(1.0, handle.Opacity);
                        var fg = ((SolidColorBrush)handle.Foreground).Color;
                        var card = BrushColour("Brush.Surface.Card");
                        Assert.True(RatioOf(fg, card) >= 4.5, $"Copy layout (off) text {RatioOf(fg, card):F2}:1 on the card");
                        Assert.NotEqual(BrushColour("Brush.Text.Primary"), fg);
                    });
                }
                finally { new ThemeService().SetTheme(ThemeMode.Dark); }
            });

        [Fact]
        public void ArmBanner_StopButton_ClearsTheBannerRing_ByAtLeastFourPixels() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = new VisualRoutingView { DataContext = rig.Vm };
                AtSize(view, 1024, 720, () =>
                {
                    rig.Vm.ArmSource(rig.Tx2);
                    Relayout(view);
                    var banner = Named(view, "ArmBanner").Single();
                    var stop = (Button)Named(view, "StopPlacingButton").Single();
                    var b = BoundsIn(stop, banner);
                    Assert.True(b.Top >= 4 - 0.5 && banner.ActualHeight - b.Bottom >= 4 - 0.5, $"Stop {b} inside banner of {banner.ActualHeight} px: needs 4 px above and below");
                    Assert.Equal(Token("Size.Touch.Min"), stop.ActualHeight);
                });
            });
    }
}
