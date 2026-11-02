using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using AvasRoutingTool.Controls;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>UI v2 centred strip (round 2 A10b): CenteredWrapPanel lays rows out left to right and centres every row.</summary>
    [Collection("WpfSta")]
    public class UiV2CenteredWrapPanelTests
    {
        private static void RunSta(Action body)
        {
            Exception? failure = null;
            var t = new Thread(() =>
            {
                try
                {
                    body();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("STA failure: " + failure);
        }

        /// <summary>Lays the children out in a Border of the given width (the panel is its child), as a ScrollViewer content is laid out.</summary>
        private static (CenteredWrapPanel Panel, Border[] Tiles) Lay(double width, params (double W, double H)[] sizes)
        {
            var panel = new CenteredWrapPanel();
            var tiles = new Border[sizes.Length];
            for (var i = 0; i < sizes.Length; i++)
            {
                tiles[i] = new Border { Width = sizes[i].W, Height = sizes[i].H };
                panel.Children.Add(tiles[i]);
            }
            var host = new Border { Width = width, Child = panel };
            host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            host.Arrange(new Rect(0, 0, width, panel.DesiredSize.Height));
            host.UpdateLayout();
            return (panel, tiles);
        }

        private static Point Offset(Border child, CenteredWrapPanel panel) => child.TransformToAncestor(panel).Transform(new Point(0, 0));

        [Fact]
        public void ThreeTilesThatFitOneRow_AreCentredAsOneRow()
        {
            RunSta(() =>
            {
                var (panel, tiles) = Lay(1000, (240, 100), (240, 100), (240, 100));
                // The row is 720 wide: (1000 - 720) / 2 = 140.
                Assert.Equal(140, Offset(tiles[0], panel).X, 1);
                Assert.Equal(380, Offset(tiles[1], panel).X, 1);
                Assert.Equal(620, Offset(tiles[2], panel).X, 1);
                Assert.All(tiles, t => Assert.Equal(0, Offset(t, panel).Y, 1));
            });
        }

        [Fact]
        public void FiveTiles_WrapIntoARowOfFourAndACentredRowOfOne()
        {
            RunSta(() =>
            {
                var (panel, tiles) = Lay(1000, (240, 100), (240, 100), (240, 100), (240, 100), (240, 100));
                // Row 1: four tiles, 960 wide, margin 20 on each side.
                Assert.Equal(20, Offset(tiles[0], panel).X, 1);
                Assert.Equal(260, Offset(tiles[1], panel).X, 1);
                Assert.Equal(500, Offset(tiles[2], panel).X, 1);
                Assert.Equal(740, Offset(tiles[3], panel).X, 1);
                Assert.All(tiles.Take(4), t => Assert.Equal(0, Offset(t, panel).Y, 1));
                // Row 2: one tile, centred: (1000 - 240) / 2 = 380, one row (100 px) lower.
                Assert.Equal(380, Offset(tiles[4], panel).X, 1);
                Assert.Equal(100, Offset(tiles[4], panel).Y, 1);
            });
        }

        [Fact]
        public void SingleTile_IsCentred()
        {
            RunSta(() =>
            {
                var (panel, tiles) = Lay(1000, (240, 100));
                Assert.Equal(380, Offset(tiles[0], panel).X, 1);
                Assert.Equal(0, Offset(tiles[0], panel).Y, 1);
            });
        }

        [Fact]
        public void DesiredHeight_IsTheSumOfTheRowHeights()
        {
            RunSta(() =>
            {
                var (five, _) = Lay(1000, (240, 100), (240, 100), (240, 100), (240, 100), (240, 100));
                Assert.Equal(1000, five.DesiredSize.Width, 1);
                Assert.Equal(200, five.DesiredSize.Height, 1);

                var (three, _) = Lay(1000, (240, 100), (240, 100), (240, 100));
                Assert.Equal(100, three.DesiredSize.Height, 1);
            });
        }

        [Fact]
        public void ArrangeOrder_IsTheLogicalOrder_ReadingLeftToRightThenDown()
        {
            RunSta(() =>
            {
                // Equal heights per row (as the strip's tiles are): a shorter child would be centred in its row by WPF's own alignment.
                var sizes = new[] { (300.0, 100.0), (200.0, 100.0), (250.0, 100.0), (400.0, 100.0), (150.0, 100.0), (240.0, 100.0), (500.0, 100.0), (100.0, 100.0), (60.0, 100.0) };
                var (panel, tiles) = Lay(1000, sizes);
                for (var i = 0; i + 1 < tiles.Length; i++)
                {
                    var a = Offset(tiles[i], panel);
                    var b = Offset(tiles[i + 1], panel);
                    var inReadingOrder = a.Y < b.Y - 0.5 || (Math.Abs(a.Y - b.Y) < 0.5 && a.X < b.X);
                    Assert.True(inReadingOrder, $"tile {i} at ({a.X:F1},{a.Y:F1}) does not come before tile {i + 1} at ({b.X:F1},{b.Y:F1})");
                }
            });
        }

        [Fact]
        public void OverWideTile_GetsItsOwnRow()
        {
            RunSta(() =>
            {
                var (panel, tiles) = Lay(1000, (240, 100), (1200, 100), (240, 100));
                Assert.Equal(0, Offset(tiles[0], panel).Y, 1);
                Assert.Equal(100, Offset(tiles[1], panel).Y, 1);
                Assert.Equal(200, Offset(tiles[2], panel).Y, 1);
                Assert.Equal(300, panel.DesiredSize.Height, 1);
            });
        }

        [Fact]
        public void ZeroChildren_MeasuresWithoutThrowing()
        {
            RunSta(() =>
            {
                var (panel, _) = Lay(1000);
                Assert.Equal(0, panel.DesiredSize.Height, 1);
                Assert.True(panel.DesiredSize.Width == 0 || Math.Abs(panel.DesiredSize.Width - 1000) < 0.5, $"width {panel.DesiredSize.Width}");
            });
        }
    }
}
