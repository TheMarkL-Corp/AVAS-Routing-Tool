using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Sdvoe.MultiView;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public partial class ViewSmokeTests
    {
        [Fact]
        public void ThemeScrollBar_Thumb_IsProportional_AndThePageAreasExist() =>
            RunSta(() =>
            {
                var scroll = ThemedScroll(400);
                Showing(scroll, () =>
                {
                    var bar = FindAll<ScrollBar>(scroll).Single(b => b.Orientation == Orientation.Vertical && b.IsVisible);
                    var thumb = FindAll<Thumb>(bar).Single();
                    Assert.True(thumb.ActualHeight > 20, $"thumb is {thumb.ActualHeight} high");
                    Assert.True(thumb.ActualHeight < bar.ActualHeight, "thumb fills the whole track");
                    Assert.Equal(2, FindAll<RepeatButton>(bar).Count); // page up / page down
                });
            });

        [Fact]
        public void ThemeScrollBar_IsTouchSized_ThinToTheEye_WithAMinimumThumb_AndAStableGutter() =>
            RunSta(() =>
            {
                var scroll = ThemedScroll(20000); // a long list: the proportional thumb would be 2 px
                double longViewport = 0, shortViewport = 0;
                Showing(scroll, () =>
                {
                    longViewport = scroll.ViewportWidth;
                    var bar = FindAll<ScrollBar>(scroll).Single(b => b.Orientation == Orientation.Vertical && b.IsVisible);
                    Assert.True(bar.ActualWidth >= 16, $"bar hit width is {bar.ActualWidth}");
                    var thumb = FindAll<Thumb>(bar).Single();
                    Assert.True(thumb.ActualHeight >= 24, $"thumb is {thumb.ActualHeight} high");
                    Assert.True(thumb.ActualWidth >= 16, $"thumb hit width is {thumb.ActualWidth}");
                    var visual = Named(thumb, "ThumbVisual").Single();
                    Assert.True(visual.ActualWidth <= 10, $"visible thumb is {visual.ActualWidth} wide");

                    // The thumb is drawn at >= 3:1 against the page and the card colours.
                    var thumbColour = ((SolidColorBrush)((Border)visual).Background).Color;
                    foreach (var key in new[] { "Color.Background.Dark", "Color.Surface.Card" })
                    {
                        var against = (Color)Application.Current.FindResource(key);
                        Assert.True(Contrast(thumbColour, against) >= 3.0, $"thumb on {key}: {Contrast(thumbColour, against):F2}");
                    }
                });

                // The gutter is reserved: the content width does not change when the bar appears.
                var fits = ThemedScroll(50);
                Showing(fits, () => shortViewport = fits.ViewportWidth);
                Assert.Equal(longViewport, shortViewport);
                // ... and the bar fits that 16 px gutter (the system bar is 17 px: it would push the content, and the thumb would sit off centre).
                Assert.Equal(300 - 16, longViewport);
            });

        [Fact]
        public void TakeStyle_LabelContrast_IsAtLeast4_5_InEveryState() =>
            RunSta(() =>
            {
                var style = (Style)Application.Current.FindResource("Style.Button.Take");
                // Setter values are DynamicResource references (the palette swaps at runtime): resolve them against the app resources
                Color Of(object? v) => v is DynamicResourceExtension dyn
                    ? ((SolidColorBrush)Application.Current.FindResource(dyn.ResourceKey)).Color
                    : ((SolidColorBrush)v!).Color;
                var setters = style.Setters.OfType<Setter>().ToList();
                var fg = Of(setters.Single(s => s.Property == Control.ForegroundProperty).Value);
                var bg = Of(setters.Single(s => s.Property == Control.BackgroundProperty).Value);
                Assert.True(Contrast(fg, bg) >= 4.5, $"normal {Contrast(fg, bg):F2}");

                var template = (ControlTemplate)setters.Single(s => s.Property == Control.TemplateProperty).Value;
                foreach (var trigger in template.Triggers.OfType<Trigger>())
                {
                    var ts = trigger.Setters.OfType<Setter>().ToList();
                    var tFg = ts.FirstOrDefault(s => s.Property == Control.ForegroundProperty) is { } f ? Of(f.Value) : fg;
                    var tBg = ts.FirstOrDefault(s => s.Property == Control.BackgroundProperty) is { } b ? Of(b.Value) : bg;
                    Assert.True(Contrast(tFg, tBg) >= 4.5, $"{trigger.Property.Name}={trigger.Value}: {Contrast(tFg, tBg):F2}");
                }
            });

        [Fact]
        public void Rings_ShowAfterAKeyPress_AndNotAfterAPointerPress() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("matrix2x2");
                c.Arm(rig.Tx1);
                c.TapBlock(0);
                c.Disarm();
                var view = rig.View(c);
                Showing(view, () =>
                {
                    var surface = Surfaces(view)[1];
                    var ring = (System.Windows.Shapes.Rectangle)InBlock(view, "BlockFocusRing", 1);
                    var take = (Button)Named(view, "TakeButton").Single();
                    var tile = Named(view, "LayoutTile").First();

                    // A finger or mouse press focuses a control: no ring.
                    KeyboardMode(false);
                    Assert.True(surface.Focus());
                    Idle();
                    Assert.False(ring.IsVisible, "a block shows a ring after a pointer focus");
                    Assert.True(take.Focus());
                    byte[] takeAfterPointer = Pixels(take);
                    Assert.True(tile.Focus());
                    byte[] tileAfterPointer = Pixels(tile);

                    // The first key press brings the ring of the focused control.
                    Assert.True(surface.Focus());
                    RaiseKey(surface, Key.Right);
                    Idle();
                    Assert.True(InputModality.IsKeyboard);
                    Assert.True(ring.IsVisible, "no ring after a key press");
                    Assert.True(take.Focus());
                    Idle();
                    Assert.False(takeAfterPointer.SequenceEqual(Pixels(take)), "Take shows no ring after a key press");
                    Assert.True(tile.Focus());
                    Idle();
                    Assert.False(tileAfterPointer.SequenceEqual(Pixels(tile)), "a layout tile shows no ring after a key press");

                    // A press takes it away again, from the control that has focus.
                    Tap(tile, Mouse.PreviewMouseDownEvent);
                    Idle();
                    Assert.False(InputModality.IsKeyboard);
                    Assert.True(tileAfterPointer.SequenceEqual(Pixels(tile)), "the ring stayed after a pointer press");
                    Assert.True(surface.Focus());
                    Idle();
                    Assert.False(ring.IsVisible, "the block ring stayed after a pointer press");
                });
            });

        [Fact]
        public void App_ThemeToggle_DarkToLightAndBack_HasZeroMissingResourcesOrExceptions() =>
            RunSta(() =>
            {
                var themeService = new AvasRoutingTool.Services.ThemeService();
                var rig = new CopyRig();
                var routingView = new VisualRoutingView { DataContext = rig.Vm };

                Showing(routingView, () =>
                {
                    // 1. Initial Dark Theme
                    Assert.Equal(AvasRoutingTool.Services.ThemeMode.Dark, themeService.CurrentTheme);
                    var card = FindAll<DisplayCardView>(routingView).First();
                    Assert.NotNull(card);

                    // 2. Toggle to Light Theme
                    themeService.ToggleTheme();
                    Assert.Equal(AvasRoutingTool.Services.ThemeMode.Light, themeService.CurrentTheme);
                    Relayout(routingView);
                    var bgLight = ((SolidColorBrush)Application.Current.FindResource("Brush.Background.Dark")).Color;
                    Assert.Equal(System.Windows.Media.Color.FromRgb(0xF8, 0xFA, 0xFC), bgLight);

                    // 3. Toggle back to Dark Theme
                    themeService.ToggleTheme();
                    Assert.Equal(AvasRoutingTool.Services.ThemeMode.Dark, themeService.CurrentTheme);
                    Relayout(routingView);
                    var bgDark = ((SolidColorBrush)Application.Current.FindResource("Brush.Background.Dark")).Color;
                    Assert.Equal(System.Windows.Media.Color.FromRgb(0x0B, 0x0F, 0x17), bgDark);
                });
            });

        [Fact]
        public void App_ThemeToggle_RepaintsEveryRenderedView_NoDarkChromeLeftInLightMode() =>
            RunSta(() =>
            {
                var themeService = new AvasRoutingTool.Services.ThemeService();
                try
                {
                    var rig = new CopyRig();
                    var routing = new VisualRoutingView { DataContext = rig.Vm };
                    var composerRig = new ComposerRig();
                    var composer = composerRig.View(composerRig.Composer("matrix2x2"));
                    // Colours only the Dark palette gives to chrome: card/elevated/hover surfaces and the light-on-dark text. (Its background and text-on-dark values
                    // are left out on purpose: the Light canvas and the preview tile legitimately reuse them.)
                    var darkSurfaces = DarkChromeColours("Color.Surface.Card", "Color.Surface.Elevated", "Color.Surface.Hover");
                    var darkText = DarkChromeColours("Color.Text.Primary");
                    bool IsDarkChrome((string Where, Color Colour, bool IsText) p) => (p.IsText ? darkText : darkSurfaces).Contains(p.Colour);

                    foreach (var view in new FrameworkElement[] { routing, composer })
                    {
                        Showing(view, () =>
                        {
                            var before = PaintedColours(view);
                            Assert.Contains(before, IsDarkChrome); // the Dark palette is on screen

                            themeService.SetTheme(AvasRoutingTool.Services.ThemeMode.Light);
                            Relayout(view);

                            var leftovers = PaintedColours(view).Where(IsDarkChrome).Select(p => $"{p.Where} {p.Colour}").Distinct().ToList();
                            Assert.True(leftovers.Count == 0, $"{view.GetType().Name} still paints Dark chrome in Light mode:\n" + string.Join("\n", leftovers));

                            themeService.SetTheme(AvasRoutingTool.Services.ThemeMode.Dark);
                            Relayout(view);
                            Assert.Contains(PaintedColours(view), IsDarkChrome);
                        });
                    }
                }
                finally
                {
                    themeService.SetTheme(AvasRoutingTool.Services.ThemeMode.Dark);
                }
            });
    }
}
