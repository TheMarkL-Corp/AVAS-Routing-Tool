using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AvasRoutingTool.Services;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// Composer round 3 screenshots (on demand): set AVAS_UIV2_SHOTS to the output folder and run
    /// <c>--filter FullyQualifiedName~UiV2ComposerShots</c>. Without the variable the test does nothing.
    /// </summary>
    public partial class ViewSmokeTests
    {
        [Fact]
        public void UiV2ComposerShots_Composer()
        {
            var folder = Environment.GetEnvironmentVariable("AVAS_UIV2_SHOTS");
            if (string.IsNullOrWhiteSpace(folder)) return;
            Directory.CreateDirectory(folder);

            RunSta(() =>
            {
                var themeService = new ThemeService();
                var notes = new System.Text.StringBuilder();
                try
                {
                    MultiViewComposerView Build(ComposerRig rig, MultiViewComposerViewModel c, ThemeMode mode)
                    {
                        for (int i = 0; i < rig.Sources.Count; i++) rig.Sources[i].PreviewBitmap = ShotPicture(i);
                        themeService.SetTheme(mode);
                        return rig.View(c);
                    }

                    MultiViewComposerViewModel Staged(ComposerRig rig)
                    {
                        var c = rig.Composer("quad_1_3");
                        c.Arm(rig.Tx1); c.TapBlock(0); c.Disarm();
                        Pump(c.TakeAsync);
                        c.Arm(rig.Tx2); c.TapBlock(1); c.Disarm();
                        Pump(c.TakeAsync); // a second Take: Undo is offered
                        c.Arm(rig.Tx1); c.TapBlock(2); c.Disarm();
                        c.Arm(rig.Vega); c.TapBlock(3); c.Disarm();
                        return c;
                    }

                    void Shoot(string name, double w, double h, ComposerRig rig, MultiViewComposerViewModel c, ThemeMode mode, Action<MultiViewComposerView>? prepare = null)
                    {
                        var view = Build(rig, c, mode);
                        ShowingAt(view, w, h, () =>
                        {
                            prepare?.Invoke(view);
                            Relayout(view);
                            var left = (ScrollViewer)view.FindName("LeftRailScroll");
                            var right = (ScrollViewer)Named(view, "RightRailScroll").Single();
                            notes.AppendLine($"{name}: leftScrollable={left.ScrollableHeight:F1} rightScrollable={right.ScrollableHeight:F1}");
                            SavePngWithPopup(view, Path.Combine(folder!, name));
                        });
                    }

                    {   // empty: no layout chosen yet
                        var rig = new ComposerRig();
                        Shoot("composer-empty-1920.png", 1920, 1080, rig, rig.Composer(), ThemeMode.Dark);
                    }
                    {
                        var rig = new ComposerRig();
                        Shoot("composer-staged-1920.png", 1920, 1080, rig, Staged(rig), ThemeMode.Dark);
                    }
                    {
                        var rig = new ComposerRig();
                        Shoot("composer-staged-1024.png", 1024, 720, rig, Staged(rig), ThemeMode.Dark);
                    }
                    {
                        var rig = new ComposerRig();
                        Shoot("composer-staged-light-1920.png", 1920, 1080, rig, Staged(rig), ThemeMode.Light);
                    }
                    {
                        var rig = new ComposerRig();
                        var c = rig.Composer("quad_1_3");
                        c.Arm(rig.Tx1); c.TapBlock(0); c.Disarm();
                        Pump(c.TakeAsync);
                        Shoot("composer-picker-1920.png", 1920, 1080, rig, c, ThemeMode.Dark, v =>
                        {
                            Relayout(v);
                            c.TapBlock(2); // an empty block: the block-first picker opens
                            Relayout(v);
                        });
                    }
                    {
                        var rig = new ComposerRig();
                        var c = rig.Composer("quad_1_3");
                        c.Arm(rig.Tx1); c.TapBlock(0); c.Disarm();
                        rig.Client.DuringCall = _ => throw new InvalidOperationException("boom");
                        Pump(c.TakeAsync);
                        Assert.Equal(BannerKind.Error, c.Banner.Kind);
                        Shoot("composer-banner-error-1920.png", 1920, 1080, rig, c, ThemeMode.Dark);
                    }
                    {
                        var rig = new ComposerRig();
                        var c = Staged(rig);
                        Shoot("composer-left-rail-scroll-1024.png", 1024, 720, rig, c, ThemeMode.Dark, v =>
                        {
                            Relayout(v);
                            var left = (ScrollViewer)v.FindName("LeftRailScroll");
                            if (left.ScrollableHeight > 0) left.ScrollToEnd();
                        });
                    }
                }
                finally
                {
                    themeService.SetTheme(ThemeMode.Dark);
                    File.WriteAllText(Path.Combine(folder, "composer-shots-notes.txt"), notes.ToString());
                }
            });
        }

        /// <summary>Renders the view, then the open picker popup (its own window, so not part of the view's visual tree) centred on its target.</summary>
        private static void SavePngWithPopup(MultiViewComposerView view, string path)
        {
            var w = (int)Math.Ceiling(view.ActualWidth);
            var h = (int)Math.Ceiling(view.ActualHeight);
            var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            var fill = new DrawingVisual();
            using (var dc = fill.RenderOpen()) dc.DrawRectangle((Brush)Application.Current.FindResource("Brush.Background.Dark"), null, new Rect(0, 0, w, h));
            bitmap.Render(fill);
            bitmap.Render(view);

            var popup = (Popup)view.FindName("PickerPopup");
            if (popup.IsOpen && popup.Child is FrameworkElement child && popup.PlacementTarget is FrameworkElement target)
            {
                child.UpdateLayout();
                var size = child.RenderSize;
                var tb = BoundsIn(target, view);
                var x = tb.X + (tb.Width - size.Width) / 2;
                var y = tb.Y + (tb.Height - size.Height) / 2;
                var pb = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
                pb.Render(child);
                var layer = new DrawingVisual();
                using (var dc = layer.RenderOpen()) dc.DrawImage(pb, new Rect(x, y, pb.Width, pb.Height));
                bitmap.Render(layer);
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }
    }
}
