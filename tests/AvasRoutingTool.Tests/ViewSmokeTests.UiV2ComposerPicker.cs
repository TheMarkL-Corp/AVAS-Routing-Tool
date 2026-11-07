using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AvasRoutingTool.Controls;
using AvasRoutingTool.Views;
using Xunit;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// UI v2 round 3, the block-first picker: it opens beside the block it fills (never on it), shares one right edge with its Cancel
    /// button, says "no preview" once (an empty placeholder, not a caption on bars), gives every tile a status icon, and dims a locked
    /// tile's name while the lock reason stays at full contrast.
    /// </summary>
    public partial class ViewSmokeTests
    {
        private static Brush ResBrush(string key) => (Brush)Application.Current.FindResource(key);

        private static void WithPicker(Action<MultiViewComposerView, FrameworkElement> check) =>
            RunSta(() =>
            {
                var (rig, c) = PickerRig();
                var view = rig.View(c);
                Showing(view, () =>
                {
                    var popup = (Popup)Named(view, "PickerPopup").Single();
                    Assert.True(popup.IsOpen);
                    var content = (FrameworkElement)popup.Child;
                    Relayout(content);
                    check(view, content);
                });
            });

        [Fact]
        public void Composer_Picker_LastTile_AndCancel_ShareOneRightEdge() =>
            WithPicker((_, content) =>
            {
                var cancel = Named(content, "PickerCancelButton").Single();
                var cancelRight = cancel.TranslatePoint(new Point(cancel.ActualWidth, 0), content).X;
                var tiles = Named(content, "PickerTile").Cast<Button>().ToList();
                var visibleRight = tiles.Max(t => t.TranslatePoint(new Point(t.ActualWidth, 0), content).X);
                Assert.True(Math.Abs(visibleRight - cancelRight) <= 0.5, $"last tile ends at {visibleRight:F1}, Cancel at {cancelRight:F1}");
                // And the frame's own right padding is the same as its left: nothing hides in a slack gutter.
                var pad = ((Border)content).Padding;
                var firstLeft = tiles.Min(t => t.TranslatePoint(new Point(0, 0), content).X);
                Assert.Equal(pad.Left + 1, firstLeft, 1); // + the 1 px border
                Assert.True(Math.Abs((content.ActualWidth - 1 - pad.Right) - cancelRight) <= 0.5,
                    $"Cancel ends at {cancelRight:F1}, frame content ends at {content.ActualWidth - 1 - pad.Right:F1}");
            });

        [Fact]
        public void Composer_Picker_GapUnderTheHeader_IsTheFramePadding() =>
            WithPicker((_, content) =>
            {
                var cancel = Named(content, "PickerCancelButton").Single();
                var scroll = Named(content, "PickerScroll").Single();
                double gap = scroll.TranslatePoint(new Point(0, 0), content).Y - cancel.TranslatePoint(new Point(0, cancel.ActualHeight), content).Y;
                Assert.Equal(((Border)content).Padding.Left, gap, 1);
            });

        [Fact]
        public void Composer_Picker_NoPreviewTile_ShowsAnEmptyPlaceholder_NotACaptionOverBars() =>
            WithPicker((_, content) =>
            {
                var tile = Named(content, "PickerTile").Cast<Button>()
                    .Single(t => ((PickerTile)t.DataContext).Source.DeviceName == "VEGA_1100");
                Assert.False(((PickerTile)tile.DataContext).Source.IsPreviewSupported);
                var thumb = FindAll<FractionalLayoutPanel>(tile).Single();
                Assert.DoesNotContain(FindAll<TextBlock>(thumb), t => t.IsVisible && t.Text.Contains("NO PREVIEW", StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(FindAll<Image>(thumb), i => i.IsVisible);
                Assert.Contains(FindAll<MaterialIcon>(thumb), i => i.IsVisible && i.Kind == "desktop_windows");
                // The note still says it once.
                var note = FindAll<TextBlock>(tile).Single(tb => tb.Style == Application.Current.FindResource("Style.Text.Picker.Note"));
                Assert.StartsWith("No preview", note.Text);
            });

        [Fact]
        public void Composer_Picker_EveryTile_HasOneStatusIcon_ForItsState() =>
            WithPicker((_, content) =>
            {
                foreach (var tile in Named(content, "PickerTile").Cast<Button>())
                {
                    var t = (PickerTile)tile.DataContext;
                    var shown = FindAll<MaterialIcon>(tile).Where(i => i.IsVisible && i.Name.StartsWith("Status", StringComparison.Ordinal)).ToList();
                    Assert.Single(shown);
                    string want = !t.IsEnabled ? "lock" : t.Source.IsPreviewSupported ? "videocam" : "info";
                    Assert.Equal(want, shown[0].Kind);
                    // The icon is centred on the first line of the note, not hanging below it.
                    var note = FindAll<TextBlock>(tile).Single(tb => tb.Style == Application.Current.FindResource("Style.Text.Picker.Note"));
                    double line = note.FontFamily.LineSpacing * note.FontSize;
                    double noteMid = note.TranslatePoint(new Point(0, line / 2), tile).Y;
                    double iconMid = shown[0].TranslatePoint(new Point(0, shown[0].ActualHeight / 2), tile).Y;
                    Assert.True(Math.Abs(noteMid - iconMid) <= 1.0, $"icon centre {iconMid:F1} vs first line centre {noteMid:F1}");
                }
            });

        [Fact]
        public void Composer_Picker_NoteRow_ReservesTwoLines_SoShortNotesDoNotRaggedTheTiles() =>
            WithPicker((_, content) =>
            {
                foreach (var tile in Named(content, "PickerTile").Cast<Button>())
                {
                    var note = FindAll<TextBlock>(tile).Single(tb => tb.Style == Application.Current.FindResource("Style.Text.Picker.Note"));
                    double line = note.FontFamily.LineSpacing * note.FontSize;
                    double row = ((FrameworkElement)note.Parent).ActualHeight;
                    Assert.True(row >= 2 * line - 0.5, $"note row is {row:F1} high, two lines are {2 * line:F1}");
                }
            });

        [Fact]
        public void Composer_Picker_LockedTile_DimsItsName_ButKeepsTheReasonAtFullContrast() =>
            WithPicker((_, content) =>
            {
                var muted = ((SolidColorBrush)ResBrush("Brush.Text.Muted")).Color;
                var strong = ((SolidColorBrush)ResBrush("Brush.Text.Strong")).Color;
                var primary = ((SolidColorBrush)ResBrush("Brush.Text.Primary")).Color;
                var tiles = Named(content, "PickerTile").Cast<Button>().ToList();
                Assert.Contains(tiles, t => !t.IsEnabled);
                Assert.Contains(tiles, t => t.IsEnabled);
                foreach (var tile in tiles)
                {
                    var name = FindAll<TextBlock>(tile).Single(tb => tb.Style == Application.Current.FindResource("Style.Text.Picker.Name"));
                    var mode = FindAll<TextBlock>(tile).Single(tb => tb.Style == Application.Current.FindResource("Style.Text.Picker.LinkMode"));
                    var note = FindAll<TextBlock>(tile).Single(tb => tb.Style == Application.Current.FindResource("Style.Text.Picker.Note"));
                    Assert.Equal(tile.IsEnabled ? primary : muted, ((SolidColorBrush)name.Foreground).Color);
                    Assert.Equal(tile.IsEnabled ? ((SolidColorBrush)ResBrush("Brush.Text.Secondary")).Color : muted, ((SolidColorBrush)mode.Foreground).Color);
                    Assert.Equal(strong, ((SolidColorBrush)note.Foreground).Color);
                }
            });

        [Fact]
        public void Composer_Picker_Placement_SitsBesideTheBlock_InsideTheCanvas_NeverOnIt()
        {
            var popup = new Size(816, 400);
            var block = new Size(300, 200);
            // The canvas is 1200 x 800 and the block sits at (700, 100) in it: canvas in block coordinates starts at (-700, -100).
            var canvas = new Rect(-700, -100, 1200, 800);
            var placements = MultiViewComposerView.PickerPlacements(popup, block, canvas, 8);
            Assert.True(placements.Length >= 2);
            foreach (var p in placements.Take(placements.Length - 1))
            {
                var r = new Rect(p.Point, popup);
                Assert.False(r.IntersectsWith(new Rect(new Point(0, 0), block)), $"placement {p.Point} covers the block");
                Assert.True(r.Left >= canvas.Left - 0.5 && r.Right <= canvas.Right + 0.5, $"placement {p.Point} leaves the canvas sideways");
            }
            // Below first, above second.
            Assert.True(placements[0].Point.Y >= block.Height);
            Assert.True(placements[1].Point.Y + popup.Height <= 0);
        }
    }
}
