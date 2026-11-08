using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AvasRoutingTool.Services;
using Xunit;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AvasRoutingTool.Tests
{
    /// <summary>Composer round 3, right rail: Save and apply never moves, dark outlines are visible, the Changes card is as tall as its list, one heading look.</summary>
    public partial class ViewSmokeTests
    {
        [Fact]
        public void DarkTheme_ControlOutline_Is3To1AgainstCanvasCardAndElevated() =>
            RunSta(() =>
            {
                new ThemeService().SetTheme(ThemeMode.Dark);
                var outline = BrushColour("Brush.Border.Control");
                foreach (var surface in new[] { "Brush.Background.Dark", "Brush.Surface.Card", "Brush.Surface.Elevated" })
                {
                    var ratio = RatioOf(outline, BrushColour(surface));
                    Assert.True(ratio >= 3.0, $"Border.Control on {surface} = {ratio:F2}:1");
                }
            });

        [Theory]
        [InlineData(1024, 720)]
        [InlineData(1920, 1080)]
        public void ComposerSaveAndApply_StaysPut_WhenUndoAppearsOrGoes(double w, double h) =>
            RunSta(() =>
            {
                double withUndo = 0, withoutUndo = 0;
                var rigA = new ComposerRig();
                var viewA = AlignedComposerView(rigA, ThemeMode.Dark);
                ShowingAt(viewA, w, h, () => withUndo = BoundsIn(VisibleNamed(viewA, "TakeButton"), viewA).Top);

                var rigB = new ComposerRig();
                new ThemeService().SetTheme(ThemeMode.Dark);
                var c = rigB.Composer("quad_1_3");
                c.Arm(rigB.Tx1); c.TapBlock(0); c.Disarm();
                Pump(c.TakeAsync);
                Assert.False(c.CanUndo);
                c.Arm(rigB.Tx1); c.TapBlock(2); c.Disarm();
                c.Arm(rigB.Vega);
                var viewB = rigB.View(c);
                ShowingAt(viewB, w, h, () => withoutUndo = BoundsIn(VisibleNamed(viewB, "TakeButton"), viewB).Top);

                Assert.True(Math.Abs(withUndo - withoutUndo) <= EdgeTolerance, $"Save & Apply top {withUndo:F1} with Undo vs {withoutUndo:F1} without");
            });

        [Theory]
        [InlineData(1024, 720)]
        [InlineData(1920, 1080)]
        public void ComposerChangesCard_IsAsTallAsItsList(double w, double h) =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = AlignedComposerView(rig, ThemeMode.Dark);
                ShowingAt(view, w, h, () =>
                {
                    var card = VisibleNamed(view, "ChangesCard");
                    var content = FindAll<TextBlock>(card).Where(t => t.IsVisible).Select(t => BoundsIn(t, view).Bottom).Max();
                    var slack = BoundsIn(card, view).Bottom - content;
                    var padding = ((Thickness)card.GetValue(Control.PaddingProperty)).Bottom;
                    Assert.True(slack <= padding + 4, $"the Changes card has {slack:F1} px of empty space under its list");
                });
            });

        [Fact]
        public void ComposerRailHeadings_ShareOneColour() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = AlignedComposerView(rig, ThemeMode.Dark);
                ShowingAt(view, 1920, 1080, () =>
                {
                    var onScreen = FindAll<TextBlock>(view).First(t => t.Text == "ON SCREEN NOW");
                    var changes = FindAll<TextBlock>(VisibleNamed(view, "ChangesCard")).First(t => t.Inlines.Count > 0 && t.Inlines.FirstInline is System.Windows.Documents.Run r && r.Text.StartsWith("CHANGES"));
                    Assert.Equal(((System.Windows.Media.SolidColorBrush)changes.Foreground).Color, ((System.Windows.Media.SolidColorBrush)onScreen.Foreground).Color);
                    Assert.Equal(changes.FontWeight, onScreen.FontWeight);
                });
            });
    }
}
