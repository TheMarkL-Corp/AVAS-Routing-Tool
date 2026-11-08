using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AvasRoutingTool.Services;
using Xunit;
using Button = System.Windows.Controls.Button;

namespace AvasRoutingTool.Tests
{
    /// <summary>Composer round 3, light theme: control outlines meet the 3:1 non-text minimum, and keyboard focus is drawn in a colour the element's own fill does not hide.</summary>
    public partial class ViewSmokeTests
    {
        private static double RatioOf(Color a, Color b)
        {
            static double Lin(byte c) { var s = c / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
            static double Lum(Color c) => 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
            var hi = Math.Max(Lum(a), Lum(b));
            var lo = Math.Min(Lum(a), Lum(b));
            return (hi + 0.05) / (lo + 0.05);
        }

        private static Color BrushColour(string key) => ((SolidColorBrush)Application.Current.FindResource(key)).Color;

        [Fact]
        public void LightTheme_ControlOutline_Is3To1AgainstCanvasCardAndElevated() =>
            RunSta(() =>
            {
                new ThemeService().SetTheme(ThemeMode.Light);
                var outline = BrushColour("Brush.Border.Control");
                foreach (var surface in new[] { "Brush.Background.Dark", "Brush.Surface.Card", "Brush.Surface.Elevated" })
                {
                    var ratio = RatioOf(outline, BrushColour(surface));
                    Assert.True(ratio >= 3.0, $"Border.Control on {surface} = {ratio:F2}:1");
                }
            });

        [Fact]
        public void LightTheme_TakeFocus_ShowsAGapRingThatDiffersFromItsFill() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = AlignedComposerView(rig, ThemeMode.Light);
                ShowingAt(view, 1024, 720, () =>
                {
                    var take = (Button)VisibleNamed(view, "TakeButton");
                    take.SetValue(AvasRoutingTool.Views.InputModality.ShowsFocusProperty, true);
                    take.UpdateLayout();
                    var ring = (Border)take.Template.FindName("FocusRing", take);
                    var gap = (Border)take.Template.FindName("FocusGap", take);
                    Assert.Equal(Visibility.Visible, ring.Visibility);
                    Assert.Equal(Visibility.Visible, gap.Visibility);
                    var fill = ((SolidColorBrush)take.Background).Color;
                    var gapColour = ((SolidColorBrush)gap.BorderBrush).Color;
                    Assert.True(RatioOf(gapColour, fill) >= 3.0, $"focus gap vs Take fill {RatioOf(gapColour, fill):F2}:1");
                });
            });

        [Fact]
        public void LightTheme_SelectedLayoutTile_ShowsAKeyboardRingThatDiffersFromItsBorder() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = AlignedComposerView(rig, ThemeMode.Light);
                ShowingAt(view, 1024, 720, () =>
                {
                    var tile = Named(view, "LayoutTile").OfType<Button>().First(t => t.IsVisible && t.ActualWidth > 0);
                    tile.SetValue(AvasRoutingTool.Views.InputModality.ShowsFocusProperty, true);
                    tile.UpdateLayout();
                    var ring = (Border)tile.Template.FindName("FocusRing", tile);
                    Assert.Equal(Visibility.Visible, ring.Visibility);
                    var ringColour = ((SolidColorBrush)ring.BorderBrush).Color;
                    Assert.True(RatioOf(ringColour, BrushColour("Brush.Accent.Primary")) >= 3.0, $"tile ring vs selected border {RatioOf(ringColour, BrushColour("Brush.Accent.Primary")):F2}:1");
                    Assert.True(RatioOf(ringColour, BrushColour("Brush.Surface.Card")) >= 3.0, "tile ring vs tile fill");
                });
            });

        [Fact]
        public void ComposerCornerClear_GlyphIsTheLayoutIconSize() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = AlignedComposerView(rig, ThemeMode.Light);
                ShowingAt(view, 1024, 720, () =>
                {
                    var clear = Named(view, "CornerClear").First(e => e.IsVisible);
                    var icon = FindAll<AvasRoutingTool.Controls.MaterialIcon>(clear).First();
                    Assert.Equal((double)Application.Current.FindResource("Size.Icon.Layout"), icon.Width);
                });
            });
    }
}
