using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AvasRoutingTool.Views;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>Round 3, Main Window: on a short window the strip's cap (a fraction of the overview) must still hold one whole tile row, so a tile's action row (Start) is never clipped.</summary>
    public partial class ViewSmokeTests
    {
        [Theory]
        [InlineData(1024, 720)]
        [InlineData(1024, 600)]
        [InlineData(1024, 560)]
        [InlineData(1366, 640)]
        public void Strip_ShortWindow_FirstTileRowIsFullyInView(double width, double height) =>
            RunSta(() =>
            {
                var (view, vm) = FullHdOverview(sources: 3, displays: 5);
                var window = new Window { Content = view, Width = width, Height = height, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Left = -10000 };
                window.Show();
                try
                {
                    view.UpdateLayout();
                    Idle();
                    view.UpdateLayout();
                    var scroll = SourcesScrollOf(view, vm);
                    var tiles = FindAll<SourceCardView>(view).Where(t => t.IsVisible && !t.IsCompact).ToList();
                    var firstRowBottom = tiles.Where(t => Math.Abs(BoundsIn(t, view).Top - BoundsIn(tiles[0], view).Top) < 0.5).Max(t => BoundsIn(t, view).Bottom);
                    var viewportBottom = BoundsIn(scroll, view).Bottom;
                    Assert.True(firstRowBottom <= viewportBottom + 0.5, $"{width}x{height}: the first tile row ends at {firstRowBottom:F1} but the strip ends at {viewportBottom:F1}: the tile is clipped (tile {tiles[0].ActualHeight:F1}, strip {scroll.ActualHeight:F1})");
                }
                finally { window.Close(); }
            });
    }
}
