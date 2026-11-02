using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using AvasRoutingTool.Controls;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>UI v2 centred strip (round 2 A10b): the overview's source tiles are centred under the header, every wrapped row too.</summary>
    public partial class ViewSmokeTests
    {
        /// <summary>The panel that holds the overview's source tiles (the strip's content).</summary>
        private static CenteredWrapPanel StripPanelOf(VisualRoutingView view) =>
            FindAll<CenteredWrapPanel>(view).Single(p => FindAll<SourceCardView>(p).Any());

        /// <summary>The tiles in row order: grouped by their top edge, each row left to right.</summary>
        private static List<List<SourceCardView>> RowsOf(VisualRoutingView view, CenteredWrapPanel strip) =>
            FindAll<SourceCardView>(view).Where(t => t.IsVisible)
                .GroupBy(t => Math.Round(BoundsIn(t, strip).Top))
                .OrderBy(g => g.Key)
                .Select(g => g.OrderBy(t => BoundsIn(t, strip).Left).ToList())
                .ToList();

        [Fact]
        public void Strip_UsesACenteredWrapPanel_AndTheTilesAreCentredInIt() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = new VisualRoutingView { DataContext = rig.Vm };
                Showing(view, () =>
                {
                    var strip = StripPanelOf(view);
                    var rows = RowsOf(view, strip);
                    Assert.Single(rows);
                    var tiles = rows[0];
                    Assert.Equal(2, tiles.Count);

                    var left = BoundsIn(tiles[0], strip).Left;
                    var right = strip.ActualWidth - BoundsIn(tiles[^1], strip).Right;
                    Assert.True(Math.Abs(left - right) <= 1, $"left gap {left:F1} px, right gap {right:F1} px: the tiles are not centred");
                    Assert.True(left > 100, $"left gap {left:F1} px: the tiles sit at the left edge");
                });
            });

        [Fact]
        public void Strip_WrappedRows_AreEachCentred()
        {
            RunSta(() =>
            {
                var (view, _) = FullHdOverview(sources: 8, displays: 2);
                Showing(view, () =>
                {
                    var strip = StripPanelOf(view);
                    var rows = RowsOf(view, strip);
                    Assert.True(rows.Count >= 2, $"8 sources wrap into {rows.Count} row(s), expected 2 or more");
                    foreach (var row in rows)
                    {
                        var left = BoundsIn(row[0], strip).Left;
                        var right = strip.ActualWidth - BoundsIn(row[^1], strip).Right;
                        Assert.True(Math.Abs(left - right) <= 1, $"a row of {row.Count}: left gap {left:F1} px, right gap {right:F1} px");
                    }
                });
            });
        }

        [Fact]
        public void Strip_LogicalOrder_IsReadingOrder_TheFirstSourceIsTheLeftmostOfTheFirstRow()
        {
            RunSta(() =>
            {
                var (view, vm) = FullHdOverview(sources: 8, displays: 2);
                Showing(view, () =>
                {
                    var strip = StripPanelOf(view);
                    var tiles = FindAll<SourceCardView>(view).Where(t => t.IsVisible).ToList();
                    Assert.Equal(vm.Sources.Count, tiles.Count);
                    for (var i = 0; i < tiles.Count; i++)
                        Assert.Same(vm.Sources[i], tiles[i].DataContext);

                    var rows = RowsOf(view, strip);
                    Assert.Same(tiles[0], rows[0][0]);
                    for (var i = 0; i + 1 < tiles.Count; i++)
                    {
                        var a = BoundsIn(tiles[i], strip);
                        var b = BoundsIn(tiles[i + 1], strip);
                        var inOrder = a.Top < b.Top - 0.5 || (Math.Abs(a.Top - b.Top) < 0.5 && a.Left < b.Left);
                        Assert.True(inOrder, $"tile {i + 1} is not before tile {i + 2} in reading order");
                    }
                });
            });
        }

        [Fact]
        public void Arming_DoesNotMoveAnyTile_NorTheStripOrTheDisplays()
        {
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = new VisualRoutingView { DataContext = rig.Vm };
                Showing(view, () =>
                {
                    var strip = StripPanelOf(view);
                    var displays = (FrameworkElement)Named(view, "DisplaysScroll").Single();
                    var card = FindAll<DisplayCardView>(view).First(c => c.IsVisible);

                    Dictionary<SourceCardView, Rect> Tiles() => FindAll<SourceCardView>(view).Where(t => t.IsVisible).ToDictionary(t => t, t => BoundsIn(t, view));
                    var tilesBefore = Tiles();
                    var stripHeightBefore = strip.ActualHeight;
                    var displaysTopBefore = BoundsIn(displays, view).Top;
                    var cardTopBefore = BoundsIn(card, view).Top;

                    rig.Vm.ArmSource(rig.Tx2);
                    Relayout(view);
                    Assert.True(rig.Vm.HasArmBanner, "arming a source did not show the arm banner");

                    var tilesAfter = Tiles();
                    Assert.Equal(tilesBefore.Count, tilesAfter.Count);
                    foreach (var (tile, before) in tilesBefore)
                    {
                        var after = tilesAfter[tile];
                        Assert.True(Math.Abs(before.Left - after.Left) < 0.5 && Math.Abs(before.Top - after.Top) < 0.5
                            && Math.Abs(before.Width - after.Width) < 0.5 && Math.Abs(before.Height - after.Height) < 0.5,
                            $"tile {((SourceCardViewModel)tile.DataContext).DeviceName} moved from {before} to {after}");
                    }
                    Assert.True(Math.Abs(strip.ActualHeight - stripHeightBefore) < 0.5, $"the strip changed height: {stripHeightBefore:F1} -> {strip.ActualHeight:F1}");
                    Assert.True(Math.Abs(BoundsIn(displays, view).Top - displaysTopBefore) < 0.5, "the Displays row moved when a source was armed");
                    Assert.True(Math.Abs(BoundsIn(card, view).Top - cardTopBefore) < 0.5, "the first display card moved when a source was armed");
                });
            });
        }
    }
}
