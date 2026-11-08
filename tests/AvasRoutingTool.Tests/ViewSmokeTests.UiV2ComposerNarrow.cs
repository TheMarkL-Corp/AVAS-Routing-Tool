using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AvasRoutingTool.Services;
using AvasRoutingTool.Views;
using Xunit;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AvasRoutingTool.Tests
{
    /// <summary>Composer round 3, narrow layout and light theme: the canvas keeps its width, small blocks stay legible, no-preview shows no stale picture, one output line.</summary>
    public partial class ViewSmokeTests
    {
        [Fact]
        public void ComposerNarrow_CanvasFrameUsesTheFreedMargin() =>
            RunSta(() =>
            {
                new ThemeService().SetTheme(ThemeMode.Dark);
                var rig = new ComposerRig();
                var c = rig.Composer("quad_1_3");
                c.Arm(rig.Tx1); c.TapBlock(0); c.Disarm();
                var view = rig.View(c);
                ShowingAt(view, 1024, 720, () =>
                {
                    var frame = BoundsIn(VisibleNamed(view, "CanvasFrame"), view);
                    Assert.True(frame.Width >= 500, $"canvas frame is {frame.Width:F0} px wide at 1024");
                });
            });

        [Fact]
        public void ComposerStepMarker_OutlineIsControlBorder_SoItReaches3To1InTheLightTheme() =>
            RunSta(() =>
            {
                new ThemeService().SetTheme(ThemeMode.Light);
                var rig = new ComposerRig();
                var view = rig.View(rig.Composer("quad_1_3"));
                ShowingAt(view, 1024, 720, () =>
                {
                    var take = VisibleNamed(view, "StepTakeHeading"); // not current, not done
                    var marker = FindAll<Border>(take).First(b => b.Name == "Marker");
                    Assert.False(((AvasRoutingTool.Controls.StepHeader)take).IsCurrent);
                    var outline = ((SolidColorBrush)marker.BorderBrush).Color;
                    Assert.Equal(ColorOf("Brush.Border.Control"), outline);
                    Assert.True(RatioOf(outline, ColorOf("Brush.Background.Dark")) >= 3.0);
                });
            });

        [Fact]
        public void ComposerNoPreview_ShowsNoStalePicture_AndTheCaptionIsHiddenOnSmallBlocks() =>
            RunSta(() =>
            {
                new ThemeService().SetTheme(ThemeMode.Light);
                var rig = new ComposerRig();
                var c = rig.Composer("pip");
                c.Arm(rig.Vega); c.TapBlock(0); c.TapBlock(1); c.Disarm();
                var view = rig.View(c);
                ShowingAt(view, 1024, 720, () =>
                {
                    var roots = Named(view, "BlockRoot").Where(r => r.IsVisible).ToList();
                    Assert.Equal(2, roots.Count);
                    foreach (var root in roots)
                    {
                        var image = FindAll<Image>(root).First(i => i.Parent is Grid);
                        Assert.False(image.IsVisible, "a stale colour-bar picture sits behind the no-preview text");
                        var label = Named(root, "NoPreviewLabel").Single();
                        if (root.ActualWidth < 240) Assert.False(label.IsVisible, "the caption sits on a compact block");
                    }
                    Assert.Contains(roots, r => r.ActualWidth < 240);
                });
            });

        [Fact]
        public void ComposerCompactBlock_EmptyShowsAnAddIcon_AndTheCornerClearHitAreaShrinks() =>
            RunSta(() =>
            {
                new ThemeService().SetTheme(ThemeMode.Dark);
                var rig = new ComposerRig();
                var c = rig.Composer("pip");
                c.Arm(rig.Tx1); c.TapBlock(1); c.Disarm(); // the large block; the inset (block 0) stays empty
                var view = rig.View(c);
                ShowingAt(view, 1024, 720, () =>
                {
                    var roots = Named(view, "BlockRoot").Where(r => r.IsVisible).ToList();
                    var inset = roots.Single(r => r.ActualWidth < 240);
                    var add = Named(inset, "EmptyAdd").Single();
                    Assert.True(add.IsVisible, "an empty compact block says nothing about being tappable");
                    var big = roots.Single(r => r.ActualWidth >= 240);
                    Assert.False(Named(big, "EmptyAdd").Single().IsVisible, "the add icon is for compact blocks only");

                    c.Arm(rig.Tx2); c.TapBlock(0); c.Disarm();
                    Relayout(view);
                    var clear = Named(inset, "CornerClear").Single();
                    Assert.True(clear.IsVisible);
                    Assert.True(clear.ActualWidth <= 40.5 && clear.ActualHeight <= 40.5, $"corner clear hit area {clear.ActualWidth:F0}x{clear.ActualHeight:F0}");
                });
            });

        [Fact]
        public void ComposerRightRail_SaysTheOutputOnce()=>
            RunSta(() =>
            {
                new ThemeService().SetTheme(ThemeMode.Dark);
                var rig = new ComposerRig();
                var c = rig.Composer("quad_1_3");
                c.Arm(rig.Tx1); c.TapBlock(0); c.Disarm();
                var view = rig.View(c);
                ShowingAt(view, 1024, 720, () =>
                {
                    var text = c.OutputText;
                    Assert.False(string.IsNullOrEmpty(text));
                    var n = FindAll<TextBlock>(view).Count(t => t.IsVisible && t.Text == text);
                    Assert.Equal(1, n);
                });
            });
    }
}
