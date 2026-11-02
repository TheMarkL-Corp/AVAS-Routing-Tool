using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AvasRoutingTool.Views;
using Xunit;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace AvasRoutingTool.Tests
{
    /// <summary>UI v2 empty tile (critique M7): an unrouted display's picture says what to do, once, inside a dashed neutral frame.</summary>
    public partial class ViewSmokeTests
    {
        private const string ReadyText = "Ready to route a source";
        private const string HintText = "Pick a source above, then tap here or drag it in.";

        private static Color PaletteColour(string key) => ((SolidColorBrush)Application.Current.FindResource(key)).Color;

        private static FrameworkElement EmptyTile(DependencyObject view)
        {
            var tiles = Named(view, "UnroutedPlaceholder");
            Assert.True(tiles.Count == 1, $"no UnroutedPlaceholder element in the display card ({tiles.Count} found)");
            return tiles[0];
        }

        [Fact]
        public void DisplayCard_Unrouted_ShowsBrandMark_ReadyToRoute_AndDashedFrame() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx3, out var host);
                Showing(host, () =>
                {
                    var tile = EmptyTile(view);
                    Assert.True(tile.IsVisible, "the empty tile is not shown on an unrouted display");

                    var mark = Assert.Single(FindAll<BrandMark>(tile));
                    Assert.True(mark.IsVisible, "the brand mark is not shown");
                    Assert.Equal(Token("Size.Brand.Empty"), mark.ActualWidth);
                    Assert.Equal(Token("Size.Brand.Empty"), mark.ActualHeight);

                    var ready = Assert.Single(VisibleTexts(tile, ReadyText));
                    Assert.Same(Application.Current.FindResource("Style.Text.Title"), ready.Style);
                    Assert.True(ready.FontSize >= 14, $"'{ready.Text}' is {ready.FontSize}px");
                    Assert.Equal(PaletteColour("Brush.Text.OnPreview"), ((SolidColorBrush)ready.Foreground).Color);

                    var hint = Assert.Single(VisibleTexts(tile, HintText));
                    Assert.True(hint.FontSize >= 12, $"'{hint.Text}' is {hint.FontSize}px");
                    Assert.Equal(PaletteColour("Brush.Text.OnPreviewMuted"), ((SolidColorBrush)hint.Foreground).Color);

                    var frame = Assert.IsType<Rectangle>(Assert.Single(Named(tile, "EmptyFrame")));
                    Assert.True(frame.IsVisible, "the dashed frame is not shown");
                    Assert.Same(Application.Current.FindResource("Style.Shape.EmptyFrame"), frame.Style);
                    Assert.NotNull(frame.StrokeDashArray);
                    Assert.NotEmpty(frame.StrokeDashArray);
                    Assert.NotEqual(PaletteColour("Brush.Border.DropTarget"), ((SolidColorBrush)frame.Stroke).Color);

                    var picture = (FrameworkElement)Named(view, "PreviewSurface").Single();
                    Assert.All(new FrameworkElement[] { mark, ready, hint, frame }, el => AssertInside(el, picture));

                    foreach (var t in FindAll<TextBlock>(view).Where(t => t.IsVisible))
                    {
                        Assert.DoesNotContain("Drag any TX card", t.Text ?? string.Empty);
                        Assert.DoesNotContain("Drop Source Here", t.Text ?? string.Empty);
                    }
                });
            });

        [Fact]
        public void DisplayCard_Routed_HidesTheEmptyTile() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host);
                Showing(host, () => Assert.False(EmptyTile(view).IsVisible, "the empty tile is shown on a routed display"));
            });

        [Fact]
        public void DisplayCard_EmptyTile_RepaintsInLightTheme() =>
            RunSta(() =>
            {
                var themeService = new AvasRoutingTool.Services.ThemeService();
                try
                {
                    var rig = new CopyRig();
                    var view = rig.View(rig.Rx3, out var host);
                    Showing(host, () =>
                    {
                        var tile = EmptyTile(view);
                        var frame = Assert.IsType<Rectangle>(Assert.Single(Named(tile, "EmptyFrame")));
                        var darkStroke = ((SolidColorBrush)frame.Stroke).Color;

                        themeService.SetTheme(AvasRoutingTool.Services.ThemeMode.Light);
                        Relayout(view);

                        // The frame colour differs between the palettes, so this shows the frame really repainted.
                        var lightStroke = ((SolidColorBrush)frame.Stroke).Color;
                        Assert.Equal(PaletteColour("Brush.Border.Control"), lightStroke);
                        Assert.NotEqual(darkStroke, lightStroke);

                        Assert.True(tile.IsVisible, "the empty tile is not shown in Light mode");
                        Assert.True(Assert.Single(FindAll<BrandMark>(tile)).IsVisible, "the brand mark is not shown in Light mode");
                        var ready = Assert.Single(VisibleTexts(tile, ReadyText));
                        Assert.Equal(PaletteColour("Brush.Text.OnPreview"), ((SolidColorBrush)ready.Foreground).Color);
                    });
                }
                finally
                {
                    themeService.SetTheme(AvasRoutingTool.Services.ThemeMode.Dark);
                }
            });

        [Fact]
        public void DisplayCard_EmptyTile_FitsAtTheMinimumCardWidth() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx3, out var host, width: 320);
                Showing(host, () =>
                {
                    var tile = EmptyTile(view);
                    var picture = (FrameworkElement)Named(view, "PreviewSurface").Single();
                    var mark = Assert.Single(FindAll<BrandMark>(tile));
                    var ready = Assert.Single(VisibleTexts(tile, ReadyText));
                    var hint = Assert.Single(VisibleTexts(tile, HintText));

                    Assert.All(new FrameworkElement[] { mark, ready, hint }, el => AssertInside(el, picture));
                    // Not clipped. AssertTextFits measures the text alone: DesiredSize would also count the Margin above each line.
                    AssertTextFits(ready);
                    AssertTextFits(hint);
                });
            });
    }
}
