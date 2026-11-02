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
        public void Composer_Step1_RendersFiveSchematicTiles() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = rig.View(rig.Composer());

                Showing(view, () =>
                {
                    var schematics = FindAll<LayoutSchematic>(view).Where(s => s.IsVisible && s.Name != "GhostSchematic").ToList();
                    Assert.Equal(new[] { "matrix2x2", "pip", "pip_large", "side_by_side", "quad_1_3" }, schematics.Select(s => s.LayoutId));
                    Assert.Equal(new[] { 4, 2, 2, 2, 4 }, schematics.Select(s => s.CellCount));
                    Assert.All(schematics, s => Assert.True(s.ActualWidth >= 100 && s.ActualHeight >= 56, $"schematic {s.ActualWidth}x{s.ActualHeight}"));

                    var tiles = Named(view, "LayoutTile").Where(t => t.IsVisible).ToList();
                    Assert.Equal(5, tiles.Count);
                    Assert.All(tiles, t => Assert.True(t.ActualWidth >= 200 && t.ActualHeight >= 96, $"tile {t.ActualWidth}x{t.ActualHeight}"));
                    AssertVisibleText(view, "Step 1");
                });
            });

        [Fact]
        public void Composer_Step1_ShowsAGhostSchematic_NotABlankCanvas() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = rig.View(rig.Composer());

                ShowingAt(view, 1904, 940, () =>
                {
                    var ghost = (LayoutSchematic)Assert.Single(Named(view, "GhostSchematic"));
                    Assert.True(ghost.IsVisible);
                    Assert.Equal("matrix2x2", ghost.LayoutId);
                    Assert.Equal(4, ghost.CellCount);
                    Assert.True(ghost.Opacity < 1, $"ghost opacity is {ghost.Opacity}");
                    Assert.True(ghost.ActualWidth > 600 && ghost.ActualHeight > 300, $"ghost is {ghost.ActualWidth}x{ghost.ActualHeight}");
                    Assert.False(ghost.Focusable);
                    AssertVisibleText(view, "Step 1");

                    // Hovering a tile previews its layout on the ghost; leaving the tile goes back to the default
                    var pip = Named(view, "LayoutTile").Cast<Button>().Single(t => t.IsVisible && ((ComposerLayoutOptionViewModel)t.DataContext).Id == "pip");
                    pip.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent });
                    Assert.Equal("pip", ghost.LayoutId);
                    Assert.Equal(2, ghost.CellCount);
                    pip.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseLeaveEvent });
                    Assert.Equal("matrix2x2", ghost.LayoutId);
                });
            });

        [Fact]
        public void Composer_Ghost_FollowsKeyboardFocus() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = rig.View(rig.Composer());

                Showing(view, () =>
                {
                    var ghost = (LayoutSchematic)Assert.Single(Named(view, "GhostSchematic"));
                    var tiles = Named(view, "LayoutTile").Cast<Button>().ToList();
                    Button Tile(string id) => tiles.Single(t => ((ComposerLayoutOptionViewModel)t.DataContext).Id == id);
                    var close = (Button)Assert.Single(Named(view, "CloseComposerButton"));

                    KeyboardMode();
                    Assert.True(Tile("quad_1_3").Focus(), "the quad tile did not take keyboard focus");
                    Idle();
                    Assert.Equal("quad_1_3", ghost.LayoutId);

                    Assert.True(Tile("pip").Focus(), "the pip tile did not take keyboard focus");
                    Idle();
                    Assert.Equal("pip", ghost.LayoutId);

                    // Focus leaves the tiles for the header: the ghost is back on the default
                    Assert.True(close.Focus(), "the header button did not take keyboard focus");
                    Idle();
                    Assert.Same(close, Keyboard.FocusedElement);
                    Assert.Equal("matrix2x2", ghost.LayoutId);
                });
            });

        [Fact]
        public void Composer_Ghost_DisappearsWhenALayoutIsStaged() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = rig.View(rig.Composer("matrix2x2"));

                Showing(view, () =>
                {
                    var ghost = (LayoutSchematic)Assert.Single(Named(view, "GhostSchematic"));
                    Assert.False(ghost.IsVisible);
                    Assert.Empty(VisibleTexts(view, "Step 1"));
                });
            });

        [Fact]
        public void Composer_Ghost_IsNotATabStop() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = rig.View(rig.Composer());

                Showing(view, () =>
                {
                    var ghost = (LayoutSchematic)Assert.Single(Named(view, "GhostSchematic"));
                    Assert.False(ghost.IsTabStop);
                    Assert.False(ghost.Focusable);
                    Assert.True(string.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetName(ghost)), "the ghost has an automation name");
                });
            });

        [Fact]
        public void Composer_Ghost_IsLegible_AndTheHintStaysAtTheTop() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = rig.View(rig.Composer());

                ShowingAt(view, 1904, 940, () =>
                {
                    var ghost = (LayoutSchematic)Assert.Single(Named(view, "GhostSchematic"));
                    var canvas = (FrameworkElement)Named(view, "CanvasHost").Single();
                    var hint = FindAll<Border>(view).Single(b => b.Style == (Style)Application.Current.FindResource("Style.Panel.Placeholder"));

                    // The ghost is a preview: dimmed, but clearly readable over the black mat
                    Assert.True(ghost.Opacity >= 0.8 && ghost.Opacity < 1.0, $"ghost opacity is {ghost.Opacity}");

                    // The hint card sits at the top of the canvas, where every layout leaves room, never over the lower cells
                    Assert.True(hint.IsVisible);
                    var hintBounds = BoundsIn(hint, canvas);
                    Assert.True(Math.Abs(hintBounds.Top) <= 40, $"hint card top is {hintBounds.Top} in a canvas {canvas.ActualHeight} high");
                    Assert.True(hintBounds.Bottom < canvas.ActualHeight / 2, $"hint card bottom is {hintBounds.Bottom} in a canvas {canvas.ActualHeight} high");

                    // Previewing PIP large (inset bottom-right): the hint still keeps clear of the lower half
                    var pipLarge = Named(view, "LayoutTile").Cast<Button>().Single(t => t.IsVisible && ((ComposerLayoutOptionViewModel)t.DataContext).Id == "pip_large");
                    pipLarge.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent });
                    Relayout(view);
                    Assert.Equal("pip_large", ghost.LayoutId);
                    Assert.True(BoundsIn(hint, canvas).Bottom < canvas.ActualHeight / 2, $"hint card covers the lower half under pip_large: bottom {BoundsIn(hint, canvas).Bottom}");
                });
            });

        [Theory]
        [InlineData(AvasRoutingTool.Services.ThemeMode.Dark)]
        [InlineData(AvasRoutingTool.Services.ThemeMode.Light)]
        public void Composer_Step1Placeholder_TextReadsOnItsCard_InBothThemes(AvasRoutingTool.Services.ThemeMode mode) =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = rig.View(rig.Composer());
                new AvasRoutingTool.Services.ThemeService().SetTheme(mode);

                ShowingAt(view, 1904, 940, () =>
                {
                    // The placeholder is a card on the canvas: its two lines are measured against the card, not the ghost behind it
                    var card = ((SolidColorBrush)Application.Current.FindResource("Brush.Surface.Elevated")).Color;
                    var lines = VisibleTexts(view, "Step 1").Concat(VisibleTexts(view, "The blocks appear")).ToList();
                    Assert.Equal(2, lines.Count);
                    foreach (var line in lines)
                    {
                        var text = ((SolidColorBrush)line.Foreground).Color;
                        Assert.True(Contrast(text, card) >= 4.5, $"'{line.Text}' in {mode}: {Contrast(text, card):0.0}:1");
                    }
                });
            });

        [Fact]
        public void Composer_StepHeadings_AreHeadings_WithTheirStateInWords_AndTheCaptionWraps() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("matrix2x2");
                c.Arm(rig.Tx1);
                c.TapBlock(0);
                var view = rig.View(c);

                Showing(view, () =>
                {
                    var layout = Named(view, "StepLayoutHeading").Single();
                    var place = Named(view, "StepPlaceHeading").Single();
                    var take = Named(view, "StepTakeHeading").Single();
                    Assert.Equal("Step 1, Layout: done", System.Windows.Automation.AutomationProperties.GetName(layout));
                    Assert.Equal("Step 2, Place sources: done", System.Windows.Automation.AutomationProperties.GetName(place));
                    Assert.Equal("Step 3, Check, then take: ready", System.Windows.Automation.AutomationProperties.GetName(take));
                    Assert.All(new[] { layout, place, take }, h =>
                        Assert.Equal("Level2", System.Windows.Automation.AutomationProperties.GetHeadingLevel(h).ToString()));

                    // The caption must wrap, never be cut off with an ellipsis at a narrow width
                    var caption = (TextBlock)Named(view, "PreviewCaption").Single();
                    Assert.Equal(TextWrapping.Wrap, caption.TextWrapping);

                    var onScreen = Named(view, "OnScreenPicture").Single();
                    Assert.Equal("On screen now: no MultiView on this screen", System.Windows.Automation.AutomationProperties.GetName(onScreen));
                });
            });

        // ---- Composer round 2 C6: the left rail keeps Step 1 only; Step 2 heads the canvas, Step 3 heads the Take cluster ----

        [Fact]
        public void Composer_LeftRail_HoldsOnlyStep1_AndUnplaced() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = rig.View(rig.Composer());

                ShowingAt(view, 1904, 940, () =>
                {
                    var rail = (ScrollViewer)Named(view, "LeftRailScroll").Single();
                    var railColumn = (ColumnDefinition)view.FindName("LeftRailColumn");
                    var rightRail = (ScrollViewer)Named(view, "RightRailScroll").Single();
                    var layout = Named(view, "StepLayoutHeading").Single();
                    var place = Named(view, "StepPlaceHeading").Single();
                    var take = Named(view, "StepTakeHeading").Single();
                    var canvas = (FrameworkElement)Named(view, "CanvasHost").Single();
                    var takeButton = (FrameworkElement)Named(view, "TakeButton").Single();

                    Assert.True(IsInside(layout, rail), "the Step 1 heading is not in the left rail");
                    Assert.False(IsInside(place, rail), "the Step 2 heading is still in the left rail");
                    Assert.False(IsInside(take, rail), "the Step 3 heading is still in the left rail");

                    // Step 2 heads the canvas status row: right of the rail and above the canvas
                    var placeBounds = BoundsIn(place, view);
                    Assert.True(placeBounds.Left >= railColumn.ActualWidth - 0.5, $"Step 2 at x {placeBounds.Left}, the left rail is {railColumn.ActualWidth} wide");
                    Assert.True(placeBounds.Top < BoundsIn(canvas, view).Top, $"Step 2 at y {placeBounds.Top}, the canvas starts at {BoundsIn(canvas, view).Top}");

                    // Step 3 heads the Take cluster: above the Take button and right of the canvas
                    var takeHeading = BoundsIn(take, view);
                    Assert.True(takeHeading.Bottom <= BoundsIn(takeButton, view).Top + 0.5, $"Step 3 ends at {takeHeading.Bottom}, Take starts at {BoundsIn(takeButton, view).Top}");
                    Assert.True(takeHeading.Left >= BoundsIn(rightRail, view).Left - 0.5, $"Step 3 at x {takeHeading.Left}, the right rail starts at {BoundsIn(rightRail, view).Left}");

                    // The two instruction sentences are gone from the screen; screen readers get them as help text on the headings
                    Assert.Empty(VisibleTexts(view, "Tap a source above"));
                    Assert.Empty(VisibleTexts(view, "Nothing changes on screen until Take."));
                    Assert.Equal("Tap a source in the strip above, then its blocks - or tap an empty block.", System.Windows.Automation.AutomationProperties.GetHelpText(place));
                    Assert.Equal("Nothing changes on screen until you press Take.", System.Windows.Automation.AutomationProperties.GetHelpText(take));
                });
            });

        [Fact]
        public void Composer_Canvas_StaysAtLeast1144x635_AtFullHd() =>
            RunSta(() =>
            {
                // In the real screen: the composer under the source strip and the header, as the checkpoint harness shows it
                var (vm, rx, sources) = Overview();
                Pump(() => vm.OpenComposerAsync(rx, "matrix2x2"));
                var view = new VisualRoutingView { DataContext = vm };

                ShowingAt(view, 1904, 940, () =>
                {
                    var canvas = (FrameworkElement)Named(view, "CanvasHost").Single();
                    Assert.True(canvas.ActualWidth >= 1140 && canvas.ActualHeight >= 630, $"the canvas is {canvas.ActualWidth}x{canvas.ActualHeight}");
                });
            });

        private static bool IsInside(DependencyObject element, DependencyObject ancestor)
        {
            for (var parent = System.Windows.Media.VisualTreeHelper.GetParent(element); parent != null; parent = System.Windows.Media.VisualTreeHelper.GetParent(parent))
            {
                if (ReferenceEquals(parent, ancestor)) return true;
            }
            return false;
        }

        [Fact]
        public void Composer_WithStagedDuplicate_ShowsSameAsChip_AndStagedLabel() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("matrix2x2");
                c.Arm(rig.Tx1);
                c.TapBlock(0);
                c.TapBlock(2);
                c.Disarm();

                var view = rig.View(c);
                Showing(view, () =>
                {
                    AssertVisibleText(view, "Same as 1");
                    AssertVisibleText(view, "STAGED - not on screen yet");
                    Assert.Empty(VisibleTexts(view, "ON SCREEN - "));
                });
            });

        [Fact]
        public void Composer_UnavailableBlock_ShowsLockAndReasonText() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("quad_1_3"); // block 1 is large, blocks 2-4 are small
                c.Arm(rig.Tx1);
                c.TapBlock(0);

                var view = rig.View(c);
                Showing(view, () =>
                {
                    Assert.Equal(3, VisibleTexts(view, "TX1 is in block 1 at ").Count);
                    Assert.Equal(3, Named(view, "LockIcon").Count(e => e.IsVisible));
                });
            });

        [Fact]
        public void Composer_TakeButton_IsAtLeast72High_AndBlockedReasonVisibleWhenBlocked() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("matrix2x2");
                var view = rig.View(c);

                Showing(view, () =>
                {
                    var take = (Button)Named(view, "TakeButton").Single();
                    Assert.True(take.ActualHeight >= 72, $"Take is {take.ActualHeight} high");
                    Assert.False(take.IsEnabled);
                    AssertVisibleText(view, "Place at least one source (Step 2).");

                    c.Arm(rig.Tx1);
                    c.TapBlock(0);
                    Relayout(view);

                    Assert.True(take.IsEnabled);
                    Assert.Equal("TAKE 1 CHANGE TO SCREEN", take.Content as string);
                    Assert.False(Named(view, "TakeBlockedReasonText").Single().IsVisible);
                });
            });

        [Fact]
        public void Composer_PickerPopup_RendersSourceTilesWithThumbnails() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var bitmap = Picture();
                rig.Tx1.PreviewBitmap = bitmap;
                var c = rig.Composer("matrix2x2");
                c.TapBlock(1); // unarmed, empty: opens the picker

                var view = rig.View(c);
                Showing(view, () =>
                {
                    var popup = (Popup)Named(view, "PickerPopup").Single();
                    Assert.True(popup.IsOpen, "picker popup is not open");
                    var content = (FrameworkElement)popup.Child;
                    Relayout(content);

                    AssertVisibleText(content, "Choose a source for block 2");
                    foreach (var name in new[] { "TX1", "TX2", "VEGA_1100" })
                        Assert.True(FindAll<TextBlock>(content).Any(t => t.IsVisible && t.Text == name), $"no tile for {name}");
                    var thumbs = FindAll<Image>(content).Where(i => i.IsVisible && ReferenceEquals(i.Source, bitmap)).ToList();
                    Assert.Single(thumbs);
                    Assert.True(thumbs[0].ActualWidth >= 160 && thumbs[0].ActualHeight >= 90, $"thumbnail {thumbs[0].ActualWidth}x{thumbs[0].ActualHeight}");
                    AssertVisibleText(content, "NO PREVIEW");

                    c.Pick(1, rig.Tx2);
                    Relayout(view);
                    Assert.False(popup.IsOpen, "picker stays open after a pick");
                });
            });

        [Fact]
        public void Composer_CornerClear_HasAHitAreaOfAtLeast48() =>
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
                    var clear = Named(view, "CornerClear").Where(e => e.IsVisible).ToList();
                    Assert.Single(clear); // only the placed block has one
                    Assert.True(clear[0].ActualWidth >= 48 && clear[0].ActualHeight >= 48, $"hit area {clear[0].ActualWidth}x{clear[0].ActualHeight}");

                    var glyph = Named(clear[0], "CornerClearGlyph").Single();
                    Assert.Equal(32, glyph.ActualWidth);
                    Assert.Equal(32, glyph.ActualHeight);
                    Assert.Equal(Wpf.Ui.Controls.SymbolRegular.Dismiss24, FindAll<Wpf.Ui.Controls.SymbolIcon>(clear[0]).Single().Symbol);
                    Assert.False(((Button)clear[0]).IsTabStop);

                    ((Button)clear[0]).Command.Execute(null);
                    Assert.Empty(c.Draft!.Placements);
                });
            });

        [Theory]
        [InlineData(1264, 755)] // MainWindow default 1280x860: its content area under the title bar, borders and app header
        [InlineData(1008, 615)] // MainWindow minimum 1024x720, likewise
        public void Composer_InVisualRoutingView_AtRealWindowSizes_TakeVisible_BlocksWide_NothingOverlaps(int width, int height) =>
            RunSta(() =>
            {
                var (vm, rx, sources) = Overview();
                Pump(() => vm.OpenComposerAsync(rx, "matrix2x2"));
                var c = vm.ActiveComposer!;
                c.Arm(sources[1]); // the longest name, in every block
                foreach (var b in c.Blocks.ToList()) c.TapBlock(b.Index);
                c.Disarm();
                var view = new VisualRoutingView { DataContext = vm };

                ShowingAt(view, width, height, () =>
                {
                    // The sources stay above as a compact palette.
                    Assert.Empty(FindAll<SourceCardView>(view).Where(s => s.IsVisible));
                    var palette = FindAll<CompactSourceCardView>(view).Where(s => s.IsVisible).ToList();
                    Assert.Equal(4, palette.Count);
                    var strip = (ScrollViewer)Named(view, "CompactSourcePalette").Single();
                    Assert.True(strip.ActualHeight <= 160, $"palette strip is {strip.ActualHeight} high");

                    // Touch panning: the palette scrolls sideways, the rails up and down.
                    Assert.Equal(PanningMode.HorizontalOnly, strip.PanningMode);
                    Assert.Equal(PanningMode.VerticalOnly, ((ScrollViewer)Named(view, "LeftRailScroll").Single()).PanningMode);
                    Assert.Equal(PanningMode.VerticalOnly, ((ScrollViewer)Named(view, "RightRailScroll").Single()).PanningMode);

                    var take = (FrameworkElement)Named(view, "TakeButton").Single();
                    Assert.True(take.IsVisible);
                    var tr = BoundsIn(take, view);
                    Assert.True(tr.Top >= 0 && tr.Bottom <= view.ActualHeight + 0.5, $"Take at {tr} in a {view.ActualHeight} high view");
                    Assert.True(take.ActualHeight >= 72);

                    var blocks = Named(view, "BlockSurface").Where(e => e.IsVisible).ToList();
                    Assert.Equal(4, blocks.Count);
                    Assert.All(blocks, b => Assert.True(b.ActualWidth >= 150, $"block is {b.ActualWidth} wide"));

                    foreach (var root in Named(view, "BlockRoot"))
                    {
                        var clear = Named(root, "CornerClear").Single(e => e.IsVisible);
                        var pill = Named(root, "SourcePill").Single(e => e.IsVisible);
                        var size = Named(root, "SizeChip").Single();
                        Assert.False(BoundsIn(clear, view).IntersectsWith(BoundsIn(pill, view)), "corner clear overlaps the source name");
                        if (size.IsVisible)
                            Assert.False(BoundsIn(size, view).IntersectsWith(BoundsIn(pill, view)), "size chip overlaps the source name");
                    }
                });
            });

        [Fact]
        public void Composer_PickerPopup_WithTwelveEncoders_IsCapped_AndTheLastTileScrollsIntoView() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                for (int i = 0; i < 9; i++)
                {
                    rig.Sources.Add(new SourceCardViewModel(new AvasDevice
                    {
                        DeviceId = $"74fe488b10{i:x2}", DeviceName = $"TX{10 + i}_SFPA", VendorId = 105, IsTransmitter = true, IsActive = true,
                    }) { PreviewBitmap = Picture() });
                }
                Assert.Equal(12, rig.Sources.Count);
                var c = rig.Composer("matrix2x2");
                c.TapBlock(1);

                var view = rig.View(c);
                Showing(view, () =>
                {
                    var popup = (Popup)Named(view, "PickerPopup").Single();
                    Assert.True(popup.IsOpen);
                    var content = (FrameworkElement)popup.Child;
                    Relayout(content);

                    Assert.True(view.PickerMaxHeight >= 300 && view.PickerMaxHeight <= SystemParameters.WorkArea.Height);
                    Assert.True(content.ActualHeight <= view.PickerMaxHeight + 0.5, $"picker is {content.ActualHeight} high, cap {view.PickerMaxHeight}");

                    var scroll = (ScrollViewer)Named(content, "PickerScroll").Single();
                    var last = Named(content, "PickerTile").Last();
                    scroll.ScrollToEnd();
                    Relayout(content);
                    var r = BoundsIn(last, scroll);
                    Assert.True(r.Top >= -0.5 && r.Bottom <= scroll.ViewportHeight + 0.5, $"last tile at {r}, viewport {scroll.ViewportHeight}");
                });
            });

        [Fact]
        public void Composer_TapOnEmptyArea_Disarms_ButATapOnABlockDoesNot() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("matrix2x2");
                c.Arm(rig.Tx1);
                var view = rig.View(c);

                Showing(view, () =>
                {
                    // Raised as the bubbling MouseUp: WPF derives the (direct) MouseLeftButtonUp that the view listens to
                    // on each element of that route. This synthetic release has no press, so the block button does not
                    // handle it; what keeps the first tap from disarming is the view's own guard (a tap inside a button
                    // is not an empty area). The second release lands on the canvas background: an empty area.
                    var block = Named(view, "BlockSurface").First();
                    Tap(FindAll<TextBlock>(block).First(), Mouse.MouseUpEvent);
                    Assert.Same(rig.Tx1, c.ArmedSource);

                    Tap((UIElement)Named(view, "CanvasHost").Single(), Mouse.MouseUpEvent);
                    Assert.Null(c.ArmedSource);
                });
            });

        [Fact]
        public void Composer_BlockGesture_IsResetByAnyRelease_SoAStalePressCannotStartABlockDrag() =>
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
                    // A tap on a placed block: press, then release (tunnelling PreviewMouseUp, as real input).
                    var placed = (UIElement)Named(view, "BlockSurface").First();
                    Tap(placed, Mouse.PreviewMouseDownEvent);
                    Assert.True(view.BlockGesture.IsPressed);
                    Tap(placed, Mouse.PreviewMouseUpEvent);
                    Assert.False(view.BlockGesture.IsPressed);

                    // A press on the block released elsewhere in the view (on the empty canvas), then a new press on the
                    // empty canvas and a move across the placed block: the old press is gone, so no block drag starts.
                    var canvas = (UIElement)Named(view, "CanvasHost").Single();
                    Tap(placed, Mouse.PreviewMouseDownEvent);
                    Assert.True(view.BlockGesture.IsPressed);
                    Tap(canvas, Mouse.PreviewMouseUpEvent);
                    Assert.False(view.BlockGesture.IsPressed);
                    Tap(canvas, Mouse.PreviewMouseDownEvent);
                    Assert.False(view.BlockGesture.IsPressed);
                    Assert.False(view.BlockGesture.Move(new Point(5000, 5000)));

                    // A press that loses the mouse capture (e.g. a window switch) is forgotten too.
                    Tap(placed, Mouse.PreviewMouseDownEvent);
                    placed.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.LostMouseCaptureEvent });
                    Assert.False(view.BlockGesture.IsPressed);
                });
            });

        [Fact]
        public void Composer_At1008x615_UndoLabel_IsNotClipped() =>
            RunSta(() =>
            {
                var now = new DateTime(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc);
                var (vm, rx, sources) = Overview(() => now);
                Pump(() => vm.OpenComposerAsync(rx, "matrix2x2"));
                var c = vm.ActiveComposer!;
                c.Arm(sources[0]);
                c.TapBlock(c.Blocks[0].Index);
                c.Disarm();
                Pump(c.TakeAsync);
                c.Arm(sources[1]); // a second Take: there is now a previous picture to go back to
                c.TapBlock(c.Blocks[1].Index);
                c.Disarm();
                Pump(c.TakeAsync);
                Assert.True(c.CanUndo);
                var view = new VisualRoutingView { DataContext = vm };

                ShowingAt(view, 1008, 615, () =>
                {
                    var undo = (Button)Named(view, "UndoButton").Single();
                    Assert.True(undo.IsVisible && undo.ActualHeight >= 48);
                    var ub = BoundsIn(undo, view);
                    Assert.True(ub.Bottom <= view.ActualHeight + 0.5, $"Undo at {ub}");

                    var label = FindAll<TextBlock>(undo).Single(t => t.Text.StartsWith("Undo", StringComparison.Ordinal));
                    var probe = new TextBlock
                    {
                        Text = label.Text, FontSize = label.FontSize, FontWeight = label.FontWeight, FontFamily = label.FontFamily,
                        TextWrapping = label.TextWrapping,
                    };
                    probe.Measure(new Size(label.TextWrapping == TextWrapping.NoWrap ? double.PositiveInfinity : label.ActualWidth, double.PositiveInfinity));
                    Assert.True(probe.DesiredSize.Width <= label.ActualWidth + 0.5, $"label needs {probe.DesiredSize.Width} px, has {label.ActualWidth}");
                    Assert.True(probe.DesiredSize.Height <= label.ActualHeight + 0.5, $"label needs {probe.DesiredSize.Height} px high, has {label.ActualHeight}");
                    var lb = BoundsIn(label, undo);
                    Assert.True(lb.Left >= -0.5 && lb.Right <= undo.ActualWidth + 0.5 && lb.Bottom <= undo.ActualHeight + 0.5, $"label {lb} outside the button");
                });
            });

        [Fact]
        public void Composer_DropSourceOnBlock_PlacesIt_HonouringCheckPlace_AndBlockOnBlockMoves() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("quad_1_3");
                var view = rig.View(c);

                Showing(view, () =>
                {
                    var data = new DataObject(typeof(AvasDevice), rig.Tx1.Device);
                    Assert.Equal(DragDropEffects.Link, view.DropEffectFor(c.Blocks[0], data));
                    Assert.True(view.DropOn(c.Blocks[0], data)); // API window index of block 1
                    Assert.Equal("74fe488b07bb", c.Draft!.Placements[c.Blocks[0].Index].SourceMac);

                    // Block 2 is smaller: one encoder has one size per layout -> refused, nothing placed.
                    Assert.Equal(DragDropEffects.None, view.DropEffectFor(c.Blocks[1], data));
                    Assert.False(view.DropOn(c.Blocks[1], data));
                    Assert.False(c.Draft.Placements.ContainsKey(c.Blocks[1].Index));
                    Assert.Equal(BannerKind.Warning, c.Banner.Kind);

                    // Drag a block onto another block: move (empty target) / swap (occupied).
                    Assert.True(view.DropOn(c.Blocks[2], new DataObject(typeof(AvasDevice), rig.Tx2.Device)));
                    Assert.True(view.DropOn(c.Blocks[3], new DataObject(typeof(ComposerBlockViewModel), c.Blocks[2])));
                    Assert.False(c.Draft.Placements.ContainsKey(c.Blocks[2].Index));
                    Assert.Equal("74fe488b07bc", c.Draft.Placements[c.Blocks[3].Index].SourceMac);
                });
            });

        [Fact]
        public void CompactSourceCard_ArmedRing_AndUsedInBlocksBadge_Render() =>
            RunSta(() =>
            {
                var (_, _, sources) = Overview();
                sources[0].IsArmed = true;
                sources[0].UsedInBlocks = 2;
                var armed = new CompactSourceCardView { DataContext = sources[0] };
                Showing(armed, () =>
                {
                    Assert.True(Named(armed, "ArmedRing").Single().IsVisible);
                    AssertVisibleText(armed, "Placing");
                    AssertVisibleText(armed, "in 2 blocks");
                    Assert.True(armed.ActualHeight >= 56, "compact card is too small to tap");
                });

                var plain = new CompactSourceCardView { DataContext = sources[2] };
                Showing(plain, () =>
                {
                    Assert.False(Named(plain, "ArmedRing").Single().IsVisible);
                    Assert.False(Named(plain, "UsedInBlocksBadge").Single().IsVisible);
                    AssertVisibleText(plain, "NO PREVIEW");
                });
            });

        [Fact]
        public void Composer_UnplacedChips_RenderAndArmTheSource() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("matrix2x2");
                c.Arm(rig.Tx2);
                c.TapBlock(2);
                c.Arm(rig.Vega);
                c.TapBlock(3);
                c.Disarm();
                Pump(() => c.SelectLayoutAsync("side_by_side")); // two blocks: blocks 3 and 4 have nowhere to go
                Assert.Equal(2, c.UnplacedSources.Count);

                var view = rig.View(c);
                Showing(view, () =>
                {
                    var chips = Named(view, "UnplacedChip").Where(e => e.IsVisible).Cast<Button>().ToList();
                    Assert.Equal(new[] { "TX2", "VEGA_1100" }, chips.Select(b => b.Content as string));
                    Assert.All(chips, b => Assert.True(b.ActualHeight >= 48));
                    Click(chips[0]);
                    Assert.Same(rig.Tx2, c.ArmedSource);
                });
            });

        [Fact]
        public void Composer_ReplaceMoveStrip_OpensThePicker_OrStartsAMove() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("matrix2x2");
                c.Arm(rig.Tx1);
                c.TapBlock(0);
                c.Disarm();
                c.TapBlock(0); // a placed block: the strip opens

                var view = rig.View(c);
                Showing(view, () =>
                {
                    var replace = Named(view, "ReplaceButton").Single(e => e.IsVisible);
                    Assert.True(replace.ActualHeight >= 48);
                    Click(replace);
                    Assert.Equal(c.Blocks[0].Index, c.PickerBlockIndex);

                    c.Disarm();
                    c.TapBlock(0);
                    Relayout(view);
                    Click(Named(view, "MoveButton").Single(e => e.IsVisible));
                    Assert.Equal(c.Blocks[0].Index, c.MovingFromIndex);
                });
            });

        [Fact]
        public void Composer_Pip_LowestWindowIndexIsPaintedOnTop() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("pip");
                var inset = c.Blocks.Single(b => b.Index == 0);
                var main = c.Blocks.Single(b => b.Index == 1);
                Assert.True(inset.NormalizedWidth < main.NormalizedWidth);

                var view = rig.View(c);
                Showing(view, () =>
                {
                    var host = (ItemsControl)Named(view, "BlocksHost").Single();
                    var insetBox = (FrameworkElement)host.ItemContainerGenerator.ContainerFromItem(inset);
                    var mainBox = (FrameworkElement)host.ItemContainerGenerator.ContainerFromItem(main);
                    Assert.True(Panel.GetZIndex(insetBox) > Panel.GetZIndex(mainBox));

                    var centre = insetBox.TranslatePoint(new Point(insetBox.ActualWidth / 2, insetBox.ActualHeight / 2), host);
                    var hit = host.InputHitTest(centre) as DependencyObject;
                    while (hit != null && !(hit is ContentPresenter { DataContext: ComposerBlockViewModel })) hit = VisualTreeHelper.GetParent(hit);
                    Assert.Same(inset, ((ContentPresenter)hit!).DataContext);
                });
            });

        [Fact]
        public void Composer_Keyboard_BlocksAreOneTabStop_ArrowsMove_EnterPlaces_DeleteClears_CtrlEnterTakes() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("matrix2x2");
                var view = rig.View(c);
                Showing(view, () =>
                {
                    KeyboardMode();
                    var surfaces = Surfaces(view);
                    Assert.Equal(4, surfaces.Count);

                    // Roving focus: the canvas is one Tab stop, the first block until the keyboard has been on another.
                    Assert.Equal(new[] { true, false, false, false }, surfaces.Select(s => s.IsTabStop));
                    Assert.True(surfaces[0].Focus() && surfaces[0].IsKeyboardFocused);
                    Assert.Equal(0, c.FocusedBlockIndex);

                    // An arrow key moves the view model's block and the keyboard with it; the Tab stop follows.
                    Assert.True(view.HandleKey(Key.Right, ModifierKeys.None, surfaces[0]));
                    Until(() => surfaces[1].IsKeyboardFocused, "the arrow key did not move the keyboard to block 2");
                    Assert.Equal(1, c.FocusedBlockIndex);
                    Assert.Equal(new[] { false, true, false, false }, surfaces.Select(s => s.IsTabStop));

                    // The ring marks the block the keyboard is on, and only that one.
                    var rings = Enumerable.Range(0, 4).Select(i => (System.Windows.Shapes.Rectangle)InBlock(view, "BlockFocusRing", i)).ToList();
                    Assert.Equal(new[] { false, true, false, false }, rings.Select(r => r.IsVisible));
                    Assert.Equal(2, rings[1].StrokeThickness);

                    // Enter places the armed source (and it stays armed); Space does the same.
                    c.Arm(rig.Tx1);
                    Assert.True(view.HandleKey(Key.Enter, ModifierKeys.None, surfaces[1]));
                    Assert.True(view.HandleKey(Key.Down, ModifierKeys.None, surfaces[1]));
                    Until(() => surfaces[3].IsKeyboardFocused, "Down did not reach block 4");
                    Assert.True(view.HandleKey(Key.Space, ModifierKeys.None, surfaces[3]));
                    Assert.Equal(new[] { "1:74fe488b07bb", "3:74fe488b07bb" }, c.Draft!.Placements.OrderBy(p => p.Key).Select(p => $"{p.Key}:{p.Value.SourceMac}"));
                    Assert.Same(rig.Tx1, c.ArmedSource);
                    Assert.Empty(rig.Client.MultiViewCalls);

                    // Delete (and Backspace) clear the block the keyboard is on - staged only.
                    c.Disarm();
                    Assert.True(view.HandleKey(Key.Delete, ModifierKeys.None, surfaces[3]));
                    Assert.Equal(new[] { 1 }, c.Draft.Placements.Keys.OrderBy(k => k));
                    Assert.True(view.HandleKey(Key.Left, ModifierKeys.None, surfaces[3]));
                    Until(() => surfaces[2].IsKeyboardFocused, "Left did not reach block 3");
                    Assert.True(view.HandleKey(Key.Back, ModifierKeys.None, surfaces[2])); // empty block: nothing to clear, still consumed
                    Assert.Equal(new[] { 1 }, c.Draft.Placements.Keys.OrderBy(k => k));

                    // A key nobody maps is left alone (Tab above all), and so are keys on the strip's own buttons.
                    Assert.False(view.HandleKey(Key.Tab, ModifierKeys.None, surfaces[2]));
                    Assert.False(view.HandleKey(Key.A, ModifierKeys.None, surfaces[2]));

                    // Ctrl+Enter is Take, from anywhere in the Composer.
                    var take = (Button)Named(view, "TakeButton").Single();
                    Assert.True(view.HandleKey(Key.Enter, ModifierKeys.Control, take));
                    Until(() => c.Banner.Kind == BannerKind.Success, "Ctrl+Enter did not take");
                    Assert.Equal(new[] { 1 }, rig.Mv.GetLive(rig.Rx.Device)!.Placements.Keys.OrderBy(k => k));

                    // Plain Enter on Take is the button's own click, not a second Take.
                    Assert.False(view.HandleKey(Key.Enter, ModifierKeys.None, take));
                });
            });

        [Fact]
        public void Composer_Keyboard_ArrowsOnAPip_UseTheApiIndex_NotTheDisplayNumber() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("pip");
                var view = rig.View(c);
                Showing(view, () =>
                {
                    var surfaces = Surfaces(view);
                    Assert.Equal(new[] { 0, 1 }, c.Blocks.Select(b => b.Index));
                    Assert.True(surfaces[0].Focus()); // the inset: API index 0, "Block 1"
                    Assert.True(view.HandleKey(Key.Left, ModifierKeys.None, surfaces[0]));
                    Until(() => surfaces[1].IsKeyboardFocused, "Left from the inset did not reach the background");
                    Assert.Equal(1, c.FocusedBlockIndex);

                    c.Arm(rig.Tx1);
                    Assert.True(view.HandleKey(Key.Enter, ModifierKeys.None, surfaces[1]));
                    Assert.Equal(new[] { 1 }, c.Draft!.Placements.Keys.ToArray()); // the background, "Block 2"
                });
            });

        [Fact]
        public void Composer_Esc_ClimbsTheLadder_FromAnywhereInTheComposer_AndNeverClosesIt() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("matrix2x2");
                var view = rig.View(c);
                Showing(view, () =>
                {
                    var take = (Button)Named(view, "TakeButton").Single();
                    c.Arm(rig.Tx1);
                    Assert.True(view.HandleKey(Key.Escape, ModifierKeys.None, take));
                    Assert.Null(c.ArmedSource);

                    Assert.False(view.HandleKey(Key.Escape, ModifierKeys.None, take)); // nothing left: not handled, nothing closes
                    Assert.Equal("matrix2x2", c.SelectedLayoutId);
                });
            });

        [Fact]
        public void Composer_Picker_IsOperableByKeyboard_ArrowsMoveEnterPicks_EscCloses_FocusReturnsToTheBlock() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("matrix2x2");
                var view = rig.View(c);
                Showing(view, () =>
                {
                    var surfaces = Surfaces(view);
                    var popup = (Popup)Named(view, "PickerPopup").Single();
                    Assert.True(surfaces[1].Focus());

                    // Enter on an empty block (nothing armed) opens its picker, and the keyboard goes into it.
                    Assert.True(view.HandleKey(Key.Enter, ModifierKeys.None, surfaces[1]));
                    Until(() => popup.IsOpen, "the picker did not open");
                    var content = (FrameworkElement)popup.Child;
                    Until(() => content.IsKeyboardFocusWithin, "the keyboard did not go into the picker");
                    var first = (Button)Keyboard.FocusedElement;
                    Assert.Equal("PickerTile", first.Name);
                    Assert.Equal("TX1", ((PickerTile)first.DataContext).Source.DeviceName);

                    // Arrows move between the tiles (directional navigation stays inside the popup).
                    Assert.True(first.MoveFocus(new TraversalRequest(FocusNavigationDirection.Right)));
                    var second = (Button)Keyboard.FocusedElement;
                    Assert.Equal("TX2", ((PickerTile)second.DataContext).Source.DeviceName);

                    // Enter picks: placed in the block the picker was opened on, the picker closes, the keyboard is back on that block.
                    RaiseKey(second, Key.Enter);
                    Assert.Equal("74fe488b07bc", c.Draft!.Placements[1].SourceMac);
                    Until(() => !popup.IsOpen, "the picker stayed open after Enter on a tile");
                    Until(() => surfaces[1].IsKeyboardFocused, "the keyboard did not return to the block");

                    // Esc closes it without placing anything, and the keyboard returns again.
                    Assert.True(surfaces[2].Focus());
                    Assert.True(view.HandleKey(Key.Enter, ModifierKeys.None, surfaces[2]));
                    Until(() => popup.IsOpen && content.IsKeyboardFocusWithin, "the picker did not open on block 3");
                    Assert.True(view.HandleKey(Key.Escape, ModifierKeys.None, (DependencyObject)Keyboard.FocusedElement));
                    Until(() => !popup.IsOpen, "Esc did not close the picker");
                    Until(() => surfaces[2].IsKeyboardFocused, "the keyboard did not return to block 3 after Esc");
                    Assert.False(c.Draft.Placements.ContainsKey(2));
                    Assert.Equal("matrix2x2", c.SelectedLayoutId);
                });
            });

        [Fact]
        public void Composer_TabOrder_LeftRail_ThenTheCanvas_ThenTheRightRailWithTakeLast() =>
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
                    var start = (Button)Named(view, "CloseComposerButton").Single();
                    Assert.True(start.Focus());

                    var order = new List<string>();
                    for (int i = 0; i < 20; i++)
                    {
                        var current = (FrameworkElement)Keyboard.FocusedElement;
                        order.Add(current.Name);
                        if (!current.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)) || ReferenceEquals(Keyboard.FocusedElement, start)) break;
                    }

                    // Back to displays, the four layout tiles (left rail), ONE block (the canvas), then Discard, Clear all and Take last (right rail).
                    Assert.Equal(new[]
                    {
                        "CloseComposerButton", "LayoutTile", "LayoutTile", "LayoutTile", "LayoutTile", "LayoutTile",
                        "BlockSurface", "DiscardButton", "ClearAllButton", "TakeButton",
                    }, order);
                });
            });

        [Fact]
        public void Composer_TabOrder_AnOpenStrip_IsReachedRightAfterItsBlock_AndTheCornerClearIsNotAStop() =>
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
                    var surfaces = Surfaces(view);
                    Assert.True(surfaces[0].Focus());
                    Assert.True(view.HandleKey(Key.Enter, ModifierKeys.None, surfaces[0])); // a placed block: its Replace / Move strip opens
                    Relayout(view);
                    Assert.True(c.Blocks[0].IsSelected);

                    var order = new List<string>();
                    for (int i = 0; i < 3; i++)
                    {
                        Assert.True(((FrameworkElement)Keyboard.FocusedElement).MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
                        order.Add(((FrameworkElement)Keyboard.FocusedElement).Name);
                    }
                    Assert.Equal(new[] { "ReplaceButton", "MoveButton", "DiscardButton" }, order);
                    Assert.All(Named(view, "CornerClear"), e => Assert.False(((Button)e).IsTabStop));
                });
            });

        [Fact]
        public void Composer_FocusRing_ShowsOnEveryControlThatTakesFocus() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("matrix2x2");
                c.Arm(rig.Tx1);
                c.TapBlock(0);
                c.Arm(rig.Tx2);
                c.TapBlock(3);
                c.Disarm();
                Pump(() => c.SelectLayoutAsync("side_by_side")); // two blocks: TX2 has none and waits as an unplaced chip
                c.TapBlock(0); // the Replace / Move strip opens on the placed block
                var view = rig.View(c);
                Showing(view, () =>
                {
                    Relayout(view);
                    var surface = Surfaces(view)[0];
                    var cell = Ancestor<ContentPresenter>(surface)!; // the ring lies in the block's margin: render the whole cell

                    AssertShowsFocus(surface, cell, "a block");
                    AssertShowsFocus(Named(view, "LayoutTile").First(), Named(view, "LayoutTile").First(), "a layout tile");
                    AssertShowsFocus(Named(view, "CloseComposerButton").Single(), Named(view, "CloseComposerButton").Single(), "Back to displays");
                    AssertShowsFocus(Named(view, "TakeButton").Single(), Named(view, "TakeButton").Single(), "Take");
                    AssertShowsFocus(Named(view, "DiscardButton").Single(), Named(view, "DiscardButton").Single(), "Discard");
                    AssertShowsFocus(Named(view, "ClearAllButton").Single(), Named(view, "ClearAllButton").Single(), "Clear all");
                    AssertShowsFocus(InBlock(view, "ReplaceButton", 0), InBlock(view, "ReplaceButton", 0), "Replace");
                    AssertShowsFocus(InBlock(view, "MoveButton", 0), InBlock(view, "MoveButton", 0), "Move");
                    AssertShowsFocus(Named(view, "UnplacedChip").First(), Named(view, "UnplacedChip").First(), "an unplaced chip");

                    c.Arm(rig.Tx2);
                    Relayout(view);
                    AssertShowsFocus(Named(view, "DoneButton").Single(), Named(view, "DoneButton").Single(), "Done placing");
                    c.Disarm();

                    // The picker: its tiles and Cancel.
                    c.TapBlock(1); // empty
                    Relayout(view);
                    var popup = (Popup)Named(view, "PickerPopup").Single();
                    Until(() => popup.IsOpen, "the picker did not open");
                    var content = (FrameworkElement)popup.Child;
                    Relayout(content);
                    var tile = Named(content, "PickerTile").First();
                    AssertShowsFocus(tile, tile, "a picker tile");
                });
            });

        [Fact]
        public void Composer_AutomationNames_AreBound_AndTheBannersAreAnnouncementRegions() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("matrix2x2");
                var view = rig.View(c);
                Showing(view, () =>
                {
                    var surfaces = Surfaces(view);
                    Assert.Equal("Block 2, 960 by 540, empty", AutomationProperties.GetName(surfaces[1]));

                    var tiles = Named(view, "LayoutTile").ToList();
                    Assert.Equal("Matrix 2x2, selected", AutomationProperties.GetName(tiles[0]));
                    Assert.Equal("PIP, not selected", AutomationProperties.GetName(tiles[1]));

                    var take = Named(view, "TakeButton").Single();
                    Assert.Equal("TAKE 0 CHANGES TO SCREEN. Place at least one source (Step 2).", AutomationProperties.GetName(take));

                    c.Arm(rig.Tx1);
                    c.TapBlock(0);
                    Relayout(view);
                    Assert.Equal("Block 1, 960 by 540, TX1, staged", AutomationProperties.GetName(Surfaces(view)[0]));
                    Assert.Equal("TAKE 1 CHANGE TO SCREEN", AutomationProperties.GetName(take));
                    Assert.Equal("Clear block 1", AutomationProperties.GetName(Named(view, "CornerClear").First(e => e.IsVisible)));
                    Assert.Equal("Back to displays", AutomationProperties.GetName(Named(view, "CloseComposerButton").Single()));

                    // The banner is a polite live region (the hint, the Take result); an error is assertive.
                    var banner = Named(view, "BannerText").Single();
                    Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(banner));
                    c.Disarm();
                    Pump(c.TakeAsync);
                    Relayout(view);
                    Assert.Equal(BannerKind.Success, c.Banner.Kind);
                    Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(banner));

                    rig.Client.DuringCall = _ => throw new InvalidOperationException("boom");
                    c.Clear(0);
                    c.Arm(rig.Tx2);
                    c.TapBlock(1);
                    c.Disarm();
                    Pump(c.TakeAsync);
                    Relayout(view);
                    Assert.Equal(BannerKind.Error, c.Banner.Kind);
                    Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(banner));

                    // Picker tiles say which source they are, and why one is not available.
                    c.TapBlock(2);
                    Relayout(view);
                    var popup = (Popup)Named(view, "PickerPopup").Single();
                    Until(() => popup.IsOpen, "the picker did not open");
                    var content = (FrameworkElement)popup.Child;
                    Relayout(content);
                    Assert.Equal(new[] { "TX1", "TX2", "VEGA_1100" },
                        Named(content, "PickerTile").Select(t => AutomationProperties.GetName(t).Split(',')[0]));
                });
            });

        [Fact]
        public void Composer_Take_RunsTheFrameTransition_OnlyWhenWindowsAnimationsAreOn() =>
            RunSta(() =>
            {
                foreach (bool animations in new[] { true, false })
                {
                    var rig = new ComposerRig();
                    var c = rig.Composer("matrix2x2", () => animations);
                    var view = rig.View(c);
                    Showing(view, () =>
                    {
                        var frame = Named(view, "CanvasFrame").Single();
                        var banner = Named(view, "BannerHost").Single();
                        c.Arm(rig.Tx1);
                        c.TapBlock(0);
                        c.Disarm();
                        Relayout(view);
                        Assert.False(frame.HasAnimatedProperties);

                        Pump(c.TakeAsync); // staged frame -> live frame
                        Relayout(view);

                        Assert.Equal(BannerKind.Success, c.Banner.Kind);
                        Assert.Equal(animations, frame.HasAnimatedProperties); // 400 ms transition, skipped when Windows animations are off
                        Assert.Equal(animations, banner.HasAnimatedProperties); // 150 ms fade of "On screen"
                        if (!animations) Assert.Equal(1.0, frame.Opacity);      // off: straight to the end state
                    });
                }
            });

        [Fact]
        public void Composer_EnterOnReplace_OpensThePickerInKeyboardMode_FocusOnATile_EscReturnsToTheBlock() =>
            RunSta(() =>
            {
                var (rig, c, view) = WithStripOpen();
                Showing(view, () =>
                {
                    OpenStripByKeyboard(view, c);
                    var popup = (Popup)Named(view, "PickerPopup").Single();
                    var replace = InBlock(view, "ReplaceButton", 0);
                    Assert.True(replace.Focus());

                    RaiseKey(replace, Key.Enter);

                    Until(() => popup.IsOpen, "the picker did not open");
                    Until(() => FocusedName() == "PickerTile", $"the keyboard did not go into the picker (it is on {FocusedName()})");
                    Assert.True(FocusIsIn(view));

                    // Esc closes it and the keyboard is back on the block (never on the window).
                    RaiseKey((UIElement)Keyboard.FocusedElement, Key.Escape);
                    Until(() => !popup.IsOpen, "Esc did not close the picker");
                    Until(() => Surfaces(view)[0].IsKeyboardFocused, $"the keyboard did not return to the block (it is on {FocusedName()})");
                    Assert.True(FocusIsIn(view));
                    Assert.Equal(new[] { 0 }, c.Draft!.Placements.Keys.ToArray());
                });
            });

        [Fact]
        public void Composer_EnterOnMove_KeyboardGoesBackToABlock_ArrowAndEnterMove_OrSwap() =>
            RunSta(() =>
            {
                var (rig, c, view) = WithStripOpen();
                c.Arm(rig.Tx2);
                c.TapBlock(2);
                c.Disarm(); // TX1 in block 1, TX2 in block 3
                Showing(view, () =>
                {
                    OpenStripByKeyboard(view, c);
                    var move = InBlock(view, "MoveButton", 0);
                    Assert.True(move.Focus());

                    RaiseKey(move, Key.Enter);

                    Assert.Equal(0, c.MovingFromIndex);
                    Until(() => FocusedName() == "BlockSurface", $"the keyboard did not return to a block (it is on {FocusedName()})");
                    Assert.True(FocusIsIn(view));
                    var surfaces = Surfaces(view);
                    Assert.True(surfaces[0].IsKeyboardFocused);

                    // Down to block 3 (occupied): Enter swaps.
                    Assert.True(view.HandleKey(Key.Down, ModifierKeys.None, surfaces[0]));
                    Until(() => surfaces[2].IsKeyboardFocused, "the arrow key did not reach block 3");
                    Assert.True(view.HandleKey(Key.Enter, ModifierKeys.None, surfaces[2]));
                    Assert.Null(c.MovingFromIndex);
                    Assert.Equal(new[] { "0:74fe488b07bc", "2:74fe488b07bb" }, c.Draft!.Placements.OrderBy(p => p.Key).Select(p => $"{p.Key}:{p.Value.SourceMac}"));

                    // Again, from block 1 to the empty block 2: a move.
                    OpenStripByKeyboard(view, c);
                    var move2 = InBlock(view, "MoveButton", 0);
                    Assert.True(move2.Focus());
                    RaiseKey(move2, Key.Enter);
                    Until(() => surfaces[0].IsKeyboardFocused, "the keyboard did not return to block 1");
                    Assert.True(view.HandleKey(Key.Right, ModifierKeys.None, surfaces[0]));
                    Until(() => surfaces[1].IsKeyboardFocused, "the arrow key did not reach block 2");
                    Assert.True(view.HandleKey(Key.Enter, ModifierKeys.None, surfaces[1]));
                    Assert.Equal(new[] { "1:74fe488b07bc", "2:74fe488b07bb" }, c.Draft.Placements.OrderBy(p => p.Key).Select(p => $"{p.Key}:{p.Value.SourceMac}"));
                });
            });

        [Fact]
        public void Composer_EscOnReplace_ClosesTheStrip_AndTheKeyboardStaysInTheComposer() =>
            RunSta(() =>
            {
                var (rig, c, view) = WithStripOpen();
                Showing(view, () =>
                {
                    OpenStripByKeyboard(view, c);
                    var replace = InBlock(view, "ReplaceButton", 0);
                    Assert.True(replace.Focus());

                    RaiseKey(replace, Key.Escape);

                    Assert.False(c.Blocks[0].IsSelected);
                    Until(() => FocusedName() == "BlockSurface", $"the keyboard did not return to the block (it is on {FocusedName()})");
                    Assert.True(FocusIsIn(view));
                    Assert.True(Surfaces(view)[0].IsKeyboardFocused);
                });
            });

        [Fact]
        public void Composer_EnterOnTake_KeyboardStaysInTheComposer_OnTheBlock_ThenOnUndoWhenItIsOffered() =>
            RunSta(() =>
            {
                var (rig, c, view) = WithStripOpen();
                c.Disarm();
                Showing(view, () =>
                {
                    KeyboardMode();
                    var take = (Button)Named(view, "TakeButton").Single();
                    Assert.True(take.Focus());

                    RaiseKey(take, Key.Enter); // the first Take: nothing to undo yet
                    Until(() => c.Banner.Kind == BannerKind.Success, "Enter on Take did not take");
                    Until(() => FocusedName() == "BlockSurface", $"the keyboard fell out of the Composer (it is on {FocusedName()})");
                    Assert.True(FocusIsIn(view));

                    // A second Take: Undo is offered, and the keyboard goes there.
                    c.Arm(rig.Tx2);
                    c.TapBlock(1);
                    c.Disarm();
                    Relayout(view);
                    Assert.True(take.Focus());
                    RaiseKey(take, Key.Enter);
                    Until(() => c.CanUndo && FocusedName() == "UndoButton", $"the keyboard did not reach Undo (it is on {FocusedName()})");
                    Assert.True(FocusIsIn(view));
                });
            });

        [Fact]
        public void Composer_EnterOnDonePlacing_KeyboardStaysInTheComposer() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("matrix2x2");
                c.Arm(rig.Tx1);
                var view = rig.View(c);
                Showing(view, () =>
                {
                    KeyboardMode();
                    var done = (Button)Named(view, "DoneButton").Single();
                    Assert.True(done.IsVisible);
                    Assert.True(done.Focus());

                    RaiseKey(done, Key.Enter);

                    Assert.Null(c.ArmedSource);
                    Until(() => FocusedName() == "BlockSurface", $"the keyboard fell out of the Composer (it is on {FocusedName()})");
                    Assert.True(FocusIsIn(view));
                });
            });

        [Fact]
        public void Composer_FocusThatFallsAfterAPointerPress_IsNeverTakenBack() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("matrix2x2");
                c.Arm(rig.Tx1);
                var view = rig.View(c);
                Showing(view, () =>
                {
                    var done = (Button)Named(view, "DoneButton").Single();
                    Assert.True(done.Focus());
                    Tap(done, Mouse.PreviewMouseDownEvent); // the last input was a press: not the keyboard
                    Assert.False(InputModality.IsKeyboard);

                    c.Disarm(); // Done placing collapses under the focus
                    Idle();
                    Idle();

                    Assert.False(FocusIsIn(view), $"focus was put back for a pointer user (it is on {FocusedName()})");
                });
            });

        [Fact]
        public void Composer_CtrlEnter_WithTakeUnavailable_SaysWhy_AndTheLineGoesWhenItNoLongerHolds() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("matrix2x2");
                var view = rig.View(c);
                Showing(view, () =>
                {
                    var take = (Button)Named(view, "TakeButton").Single();
                    Assert.False(c.CanTake);

                    Assert.True(view.HandleKey(Key.Enter, ModifierKeys.Control, take));

                    Assert.Equal(BannerKind.Info, c.Banner.Kind);
                    Assert.Equal("Place at least one source (Step 2).", c.Banner.Text);
                    Assert.Empty(rig.Client.MultiViewCalls);

                    c.Arm(rig.Tx1);
                    c.TapBlock(0);
                    c.Disarm();
                    Assert.NotEqual("Place at least one source (Step 2).", c.Banner.Text);
                    Assert.Equal(BannerKind.None, c.Banner.Kind);
                });
            });

        [Fact]
        public void Composer_HeldKeys_AreOnePress_NoRepeatedPlaceTakeOrPick() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("matrix2x2");
                var view = rig.View(c);
                Showing(view, () =>
                {
                    KeyboardMode();
                    var surfaces = Surfaces(view);
                    var popup = (Popup)Named(view, "PickerPopup").Single();
                    Assert.True(surfaces[1].Focus());

                    // Enter on an empty block opens the picker; the repeats of the held key must not land on its first tile and pick it.
                    RaiseKey(surfaces[1], Key.Enter);
                    Until(() => popup.IsOpen && FocusedName() == "PickerTile", "the picker did not open with the keyboard in it");
                    RaiseRepeatedKey((UIElement)Keyboard.FocusedElement, Key.Enter);
                    RaiseRepeatedKey((UIElement)Keyboard.FocusedElement, Key.Space);
                    Idle();
                    Assert.True(popup.IsOpen);
                    Assert.Empty(c.Draft!.Placements);

                    // The direct calls: a repeat of Enter / Space / P / Delete / Escape / Ctrl+Enter is consumed and does nothing.
                    c.Escape();
                    Until(() => !popup.IsOpen, "the picker stayed open");
                    c.Arm(rig.Tx1);
                    foreach (var key in new[] { Key.Enter, Key.Space, Key.P, Key.Delete, Key.Back })
                        Assert.True(view.HandleKey(key, ModifierKeys.None, surfaces[0], isRepeat: true));
                    Assert.True(view.HandleKey(Key.Escape, ModifierKeys.None, surfaces[0], isRepeat: true));
                    Assert.True(view.HandleKey(Key.Enter, ModifierKeys.Control, surfaces[0], isRepeat: true));
                    Assert.Empty(c.Draft.Placements);
                    Assert.Same(rig.Tx1, c.ArmedSource);
                    Assert.Empty(rig.Client.MultiViewCalls);

                    // A held arrow key keeps walking.
                    Assert.True(view.HandleKey(Key.Right, ModifierKeys.None, surfaces[0], isRepeat: true));
                    Assert.Equal(1, c.FocusedBlockIndex);
                });
            });

        [Fact]
        public void Composer_HeldEnter_OnAButton_IsOnePress_NoSecondClick() =>
            RunSta(() =>
            {
                var (rig, c, view) = WithStripOpen();
                Showing(view, () =>
                {
                    KeyboardMode();
                    var take = (Button)Named(view, "TakeButton").Single();
                    Assert.True(take.Focus());

                    RaiseRepeatedKey(take, Key.Enter); // the repeat of a press that did not start here: nothing
                    Idle();
                    Assert.Equal(BannerKind.None, c.Banner.Kind);
                    Assert.Empty(rig.Client.MultiViewCalls);

                    RaiseKey(take, Key.Enter);
                    Until(() => c.Banner.Kind == BannerKind.Success, "the press itself did not take");
                });
            });

        [Fact]
        public void Composer_FrameTransition_RunsAfterATake_AndAfterAnUndo_OnlyWhenWindowsAnimationsAreOn() =>
            RunSta(() =>
            {
                foreach (bool animations in new[] { true, false })
                {
                    var rig = new ComposerRig();
                    var c = rig.Composer("matrix2x2", () => animations);
                    var view = rig.View(c);
                    Showing(view, () =>
                    {
                        var frame = Named(view, "CanvasFrame").Single();
                        c.Arm(rig.Tx1);
                        c.TapBlock(0);
                        c.Disarm();
                        Pump(c.TakeAsync);
                        c.Arm(rig.Tx2);
                        c.TapBlock(1);
                        c.Disarm();
                        Pump(c.TakeAsync);
                        Relayout(view);
                        Assert.Equal(animations, frame.HasAnimatedProperties);

                        // Undo changes the picture on screen without IsOnScreen ever flipping: it gets the transition too.
                        frame.BeginAnimation(UIElement.OpacityProperty, null);
                        Assert.False(frame.HasAnimatedProperties);
                        Assert.True(c.IsOnScreen);
                        Assert.True(c.CanUndo);
                        Pump(c.UndoAsync);
                        Relayout(view);

                        Assert.Equal(BannerKind.Success, c.Banner.Kind);
                        Assert.Equal(new[] { 0 }, rig.Mv.GetLive(rig.Rx.Device)!.Placements.Keys.ToArray());
                        Assert.Equal(animations, frame.HasAnimatedProperties);
                        if (!animations) Assert.Equal(1.0, frame.Opacity);
                    });
                }
            });

        // ---- Task 11 fix round 2: the guard covers removed elements and window deactivation; Ctrl+Enter is re-announced ----

        [Fact]
        public void Composer_EnterOnAnUnplacedChip_ArmsTheSource_AndTheKeyboardGoesToABlock_ArrowAndEnterPlace() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("matrix2x2");
                c.Arm(rig.Tx1);
                c.TapBlock(0);
                c.Arm(rig.Tx2);
                c.TapBlock(3);
                c.Disarm();
                Pump(() => c.SelectLayoutAsync("side_by_side")); // two blocks: TX2 waits as an unplaced chip
                var view = rig.View(c);
                Showing(view, () =>
                {
                    KeyboardMode();
                    var chip = (Button)Named(view, "UnplacedChip").Single();
                    Assert.True(chip.Focus());

                    RaiseKey(chip, Key.Enter); // arms TX2; the chip leaves the tree under the keyboard

                    Assert.Same(rig.Tx2, c.ArmedSource);
                    Until(() => FocusedName() == "BlockSurface", $"the keyboard did not go to a block (it is on {FocusedName()})");
                    Assert.True(FocusIsIn(view));

                    // Arrow, Enter: placed.
                    var surfaces = Surfaces(view);
                    var onBlock = surfaces.Single(s => s.IsKeyboardFocused);
                    int from = ((ComposerBlockViewModel)onBlock.DataContext).Index;
                    int to = 1 - from;
                    Assert.True(view.HandleKey(to == 1 ? Key.Right : Key.Left, ModifierKeys.None, onBlock));
                    Until(() => surfaces[to].IsKeyboardFocused, "the arrow key did not move to the other block");
                    Assert.True(view.HandleKey(Key.Enter, ModifierKeys.None, surfaces[to]));
                    Assert.Equal(rig.Tx2.MacAddress, c.Draft!.Placements[to].SourceMac);
                });
            });

        [Fact]
        public void Composer_LayoutChange_UnderTheKeyboard_KeepsItInsideTheComposer() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("matrix2x2");
                var view = rig.View(c);
                Showing(view, () =>
                {
                    KeyboardMode();
                    Assert.True(Surfaces(view)[3].Focus());
                    Assert.Equal(3, c.FocusedBlockIndex);

                    Pump(() => c.SelectLayoutAsync("pip")); // the blocks are rebuilt under the focus

                    // (The element that had the keyboard is removed from the tree: it is not "inside" any more, whatever it is called.)
                    Until(() => FocusIsIn(view) && Surfaces(view).Any(s => s.IsKeyboardFocused), $"the keyboard fell out of the Composer (it is on {FocusedName()})");
                    Assert.Equal(2, Surfaces(view).Count);
                    var focused = Surfaces(view).Where(s => s.IsKeyboardFocused).ToList();
                    Assert.True(focused.Count == 1, $"{focused.Count} blocks have the keyboard");
                    Assert.True(focused[0].IsTabStop, "the keyboard is not on the roving block"); // the roving block follows the keyboard
                });
            });

        [Theory]
        [InlineData("UndoButton")]
        [InlineData("LayoutTile")]
        [InlineData("DiscardButton")]
        [InlineData("ClearAllButton")]
        [InlineData("DoneButton")]
        public void Composer_EnterOnTheRailButtons_NeverLeavesTheKeyboardOutsideTheComposer(string name) =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("matrix2x2");
                c.Arm(rig.Tx1);
                c.TapBlock(0);
                c.Disarm();
                Pump(c.TakeAsync);
                c.Arm(rig.Tx2);
                c.TapBlock(1);
                c.Disarm();
                Pump(c.TakeAsync); // Undo is offered
                c.Arm(rig.Tx1);
                c.TapBlock(2); // a staged change: Discard and Clear all are on
                if (name != "DoneButton") c.Disarm();
                var view = rig.View(c);
                Showing(view, () =>
                {
                    KeyboardMode();
                    var button = (Button)Named(view, name).First(e => e.IsVisible && e.IsEnabled);
                    Assert.True(button.Focus());

                    RaiseKey(button, Key.Enter);
                    Idle();

                    Until(() => FocusIsIn(view) && Keyboard.FocusedElement is not Window, $"Enter on {name} left the keyboard outside the Composer (it is on {FocusedName()})");
                });
            });

        [Fact]
        public void Composer_WindowDeactivated_TheGuardDoesNothing() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("matrix2x2");
                c.Arm(rig.Tx1);
                var view = rig.View(c);
                Showing(view, () =>
                {
                    KeyboardMode();
                    var done = (Button)Named(view, "DoneButton").Single();
                    Assert.True(done.Focus());

                    // The window is not the active one (Alt-Tab): a Focus() of ours would activate it again and steal the user's place.
                    view.WindowActiveProbe = () => false;
                    c.Disarm(); // Done placing collapses under the focus
                    Idle();
                    Idle();
                    Assert.False(FocusIsIn(view), $"the guard moved the keyboard while the window was inactive (it is on {FocusedName()})");
                    Assert.False(Surfaces(view).Any(s => s.IsKeyboardFocused));

                    // The restore that was waiting is dropped, not kept for when the window comes back.
                    view.WindowActiveProbe = null;
                    Idle();
                    Idle();
                    Assert.False(FocusIsIn(view), "a restore that was pending when the window deactivated ran afterwards");
                });
            });

        [Fact]
        public void Composer_CtrlEnter_OnADisabledTake_IsAnnouncedEveryTime() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("matrix2x2");
                var view = rig.View(c);
                var announced = new List<string>();
                Motion.AnnounceObserver = element => announced.Add(((FrameworkElement)element).Name);
                try
                {
                    Showing(view, () =>
                    {
                        var take = (Button)Named(view, "TakeButton").Single();
                        Assert.True(view.HandleKey(Key.Enter, ModifierKeys.Control, take));
                        int afterFirst = announced.Count(n => n == "BannerText");
                        Assert.True(afterFirst >= 1);

                        // The banner text is the same record again, so nothing in the view model changes: the view announces it itself.
                        Assert.True(view.HandleKey(Key.Enter, ModifierKeys.Control, take));
                        Assert.Equal(afterFirst + 1, announced.Count(n => n == "BannerText"));
                        Assert.True(view.HandleKey(Key.Enter, ModifierKeys.Control, take));
                        Assert.Equal(afterFirst + 2, announced.Count(n => n == "BannerText"));
                    });
                }
                finally
                {
                    Motion.AnnounceObserver = null;
                }
            });

        // ---- Final fix round: keyboard entry, compact palette while armed, announcements and names ----

        [Theory]
        [InlineData("layout menu tile")]
        [InlineData("draft chip")]
        [InlineData("picture")]
        public void Composer_OpenedFromTheKeyboard_TheKeyboardIsInsideIt(string how) =>
            RunSta(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                var (vm, rx, sources) = Overview();
                if (how != "layout menu tile")
                {
                    Pump(() => vm.OpenComposerAsync(rx, "matrix2x2"));
                    var staged = vm.ActiveComposer!;
                    staged.Arm(sources[0]);
                    staged.TapBlock(0);
                    staged.Disarm();
                    if (how == "picture") Pump(staged.TakeAsync); // on screen: the picture opens the Composer
                    vm.CloseComposer();                           // otherwise a draft is kept: the chip resumes it
                }
                var view = new VisualRoutingView { DataContext = vm };
                Showing(view, () =>
                {
                    KeyboardMode();
                    var card = FindAll<DisplayCardView>(view).Single();
                    FrameworkElement opener;
                    if (how == "layout menu tile")
                    {
                        var layout = (Button)Named(card, "LayoutButton").Single();
                        var popup = (Popup)Named(card, "LayoutMenuPopup").Single();
                        Assert.True(layout.Focus());
                        RaiseKey(layout, Key.Enter);
                        Until(() => popup.IsOpen && ((FrameworkElement)popup.Child).IsKeyboardFocusWithin, "the keyboard did not go into the Layout menu");
                        opener = FindAll<Button>(popup.Child).Single(b => b.Name == "LayoutMenuTile" && b.DataContext is LayoutItemViewModel { Id: "matrix2x2" });
                    }
                    else
                    {
                        opener = (FrameworkElement)Named(card, how == "draft chip" ? "DraftChip" : "PreviewSurface").Single();
                    }
                    Assert.True(opener.Focus(), $"{how} did not take the keyboard");

                    RaiseKey(opener, Key.Enter);

                    Until(() => vm.ActiveComposer != null, $"Enter on the {how} did not open the Composer");
                    var composer = FindAll<MultiViewComposerView>(view).Single();
                    Until(() => composer.IsKeyboardFocusWithin && Keyboard.FocusedElement is not Window,
                        $"Enter on the {how} left the keyboard outside the Composer (it is on {FocusedName()})");
                });
            });

        [Fact]
        public void Composer_ClosedWhileAnEntryIsPending_LeavesNoStaleFlag_ALaterRestoreDoesNotPullFocusIn() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var first = rig.Composer("matrix2x2");
                var view = rig.View(first);
                var outside = new Button { Content = "elsewhere" };
                Showing(new StackPanel { Children = { outside, view } }, () =>
                {
                    KeyboardMode();
                    Assert.True(outside.Focus());

                    // The Composer is shown (opened from the keyboard): an entry is queued. It is closed before that runs.
                    view.Visibility = Visibility.Collapsed;
                    view.Visibility = Visibility.Visible;
                    view.DataContext = null;
                    Idle();

                    // A Composer for another display appears while the user is a pointer user, then the keyboard is back
                    // on another control and the blocks are rebuilt: only a keyboard that fell may be put back.
                    KeyboardMode(false);
                    var second = rig.Composer("matrix2x2");
                    view.DataContext = second;
                    Idle();
                    KeyboardMode();
                    Assert.True(outside.Focus());
                    Pump(() => second.SelectLayoutAsync("pip"));
                    Idle();
                    Idle();

                    Assert.True(outside.IsKeyboardFocused, $"a stale entry pulled the keyboard into the Composer (it is on {FocusedName()})");
                });
            });

        [Fact]
        public void Composer_AtThePipInset_TheEmptyHintStepsAside_SoItNeverWrapsOverTheNumberChip() =>
            RunSta(() =>
            {
                var (vm, rx, _) = Overview();
                Pump(() => vm.OpenComposerAsync(rx, "pip"));
                var view = new VisualRoutingView { DataContext = vm };

                ShowingAt(view, 1008, 615, () =>
                {
                    var roots = Named(view, "BlockRoot").Where(r => r.IsVisible).ToList();
                    Assert.Equal(2, roots.Count);
                    Assert.Contains(roots, r => r.ActualWidth < 240); // the inset is the small block
                    foreach (var root in roots)
                    {
                        var hint = Named(root, "EmptyHint").Single();
                        var chip = Named(root, "NumberChip").Single();
                        if (root.ActualWidth < 240)
                        {
                            Assert.False(hint.IsVisible, $"the hint shows in a {root.ActualWidth:F0} px block");
                        }
                        else
                        {
                            Assert.True(hint.IsVisible);
                            Assert.False(BoundsIn(hint, view).IntersectsWith(BoundsIn(chip, view)), "the hint overlaps the number chip");
                        }
                    }
                });
            });

        [Fact]
        public void Composer_InLightMode_ScrimLabels_StepDots_LiveBlocks_AndPickerTiles_KeepHighContrast() =>
            RunSta(() =>
            {
                var themeService = new AvasRoutingTool.Services.ThemeService();
                try
                {
                    themeService.SetTheme(AvasRoutingTool.Services.ThemeMode.Light);
                    var rig = new ComposerRig();
                    var c = rig.Composer("matrix2x2");
                    c.Arm(rig.Vega); // non-AVAS source (IsPreviewSupported == false)
                    c.TapBlock(0);
                    c.Disarm();
                    Pump(c.TakeAsync); // puts LiveBlocks on screen and marks Step 1 & Step 2 done
                    c.TapBlock(1);     // opens PickerPopup on empty block 2
                    var view = rig.View(c);

                    Showing(view, () =>
                    {
                        var onScrim = ((SolidColorBrush)Application.Current.FindResource("Brush.Text.OnScrim")).Color;
                        var onAction = ((SolidColorBrush)Application.Current.FindResource("Brush.Text.OnAction")).Color;
                        var controlBorder = ((SolidColorBrush)Application.Current.FindResource("Brush.Border.Control")).Color;

                        // 1. Scrim labels over dark video blocks use light Brush.Text.OnScrim, never dark Brush.Text.Primary
                        foreach (var name in new[] { "NumberText", "SourcePillText", "NoPreviewName" })
                        {
                            var tb = FindAll<TextBlock>(view).First(t => t.Name == name && t.IsVisible);
                            Assert.Equal(onScrim, ((SolidColorBrush)tb.Foreground).Color);
                        }

                        // 2. Completed step headers use Brush.Text.OnAction on Brush.Action.Take
                        var takeFill = ((SolidColorBrush)Application.Current.FindResource("Brush.Action.Take")).Color;
                        var doneHeaders = FindAll<AvasRoutingTool.Controls.StepHeader>(view).Where(h => h.IsDone && h.IsVisible).ToList();
                        Assert.NotEmpty(doneHeaders);
                        foreach (var header in doneHeaders)
                        {
                            var icon = FindAll<Wpf.Ui.Controls.SymbolIcon>(header).Single(i => i.IsVisible);
                            Assert.Equal(onAction, ((SolidColorBrush)icon.Foreground).Color);
                            var marker = (Border)header.Template.FindName("Marker", header);
                            Assert.Equal(takeFill, ((SolidColorBrush)marker.Background).Color);
                        }

                        // 3. PickerPopup resting tiles use neutral Brush.Border.Control, not cyan DropTarget
                        var popup = (Popup)Named(view, "PickerPopup").Single();
                        Assert.True(popup.IsOpen);
                        var pickerMenu = (FrameworkElement)popup.Child;
                        Relayout(pickerMenu);
                        var tile = Named(pickerMenu, "PickerTile").Cast<Button>().First(b => b.IsEnabled);
                        var bd = (Border)tile.Template.FindName("bd", tile);
                        Assert.Equal(controlBorder, ((SolidColorBrush)bd.BorderBrush).Color);

                        // 4. LiveBlocks schematic tile uses PreviewTile, Text.OnPreview, and Text.OnScrim
                        var previewTile = ((SolidColorBrush)Application.Current.FindResource("Brush.Surface.PreviewTile")).Color;
                        var onPreview = ((SolidColorBrush)Application.Current.FindResource("Brush.Text.OnPreview")).Color;
                        var liveTile = FindAll<Border>(view).First(b => b.Background is SolidColorBrush scb && scb.Color == previewTile);
                        Assert.NotNull(liveTile);
                        var liveSourceName = FindAll<TextBlock>(liveTile).First(t => t.HorizontalAlignment == HorizontalAlignment.Center);
                        Assert.Equal(onPreview, ((SolidColorBrush)liveSourceName.Foreground).Color);
                        var liveLabel = FindAll<TextBlock>(liveTile).First(t => t.HorizontalAlignment == HorizontalAlignment.Left);
                        Assert.Equal(onScrim, ((SolidColorBrush)liveLabel.Foreground).Color);
                    });
                }
                finally
                {
                    themeService.SetTheme(AvasRoutingTool.Services.ThemeMode.Dark);
                }
            });

        // ---- Composer round 2 C2: Done placing is an outline button; Take is the only filled button ----

        [Fact]
        public void Composer_DoneButton_IsSecondary_TakeIsTheOnlyFilledButton_InDarkMode() =>
            RunSta(() => AssertDoneIsOutlineAndTakeIsTheOnlyFilled(AvasRoutingTool.Services.ThemeMode.Dark));

        [Fact]
        public void Composer_DoneButton_IsSecondary_TakeIsTheOnlyFilledButton_InLightMode() =>
            RunSta(() => AssertDoneIsOutlineAndTakeIsTheOnlyFilled(AvasRoutingTool.Services.ThemeMode.Light));

        [Fact]
        public void Composer_DoneButton_Outline_HasEnoughContrast_InDarkMode() =>
            RunSta(() => AssertDoneOutlineHasEnoughContrast(AvasRoutingTool.Services.ThemeMode.Dark));

        [Fact]
        public void Composer_DoneButton_Outline_HasEnoughContrast_InLightMode() =>
            RunSta(() => AssertDoneOutlineHasEnoughContrast(AvasRoutingTool.Services.ThemeMode.Light));

        private static void AssertDoneIsOutlineAndTakeIsTheOnlyFilled(AvasRoutingTool.Services.ThemeMode mode)
        {
            var themeService = new AvasRoutingTool.Services.ThemeService();
            try
            {
                themeService.SetTheme(mode);
                var rig = new ComposerRig();
                var c = rig.Composer("matrix2x2");
                c.Arm(rig.Tx1);
                c.TapBlock(0); // a placed block: Take is ready
                c.Arm(rig.Tx2); // armed again: Done placing is on screen
                Assert.True(c.CanTake, "Take is not ready after a placement");
                Assert.Same(rig.Tx2, c.ArmedSource);
                var view = rig.View(c);

                Showing(view, () =>
                {
                    Relayout(view);
                    var done = (Button)Named(view, "DoneButton").Single();
                    var take = (Button)Named(view, "TakeButton").Single();
                    Assert.True(done.IsVisible, "Done placing is not on screen while a source is armed");
                    Assert.True(take.IsVisible, "Take is not on screen");

                    var elevated = ((SolidColorBrush)Application.Current.FindResource("Brush.Surface.Elevated")).Color;
                    var outline = ((SolidColorBrush)Application.Current.FindResource("Brush.Border.DropTarget")).Color;
                    var text = ((SolidColorBrush)Application.Current.FindResource("Brush.Text.Primary")).Color;
                    var takeFill = ((SolidColorBrush)Application.Current.FindResource("Brush.Action.Take")).Color;

                    // Done placing: the secondary look (elevated fill, cyan outline = armed, primary text), never a cyan fill
                    Assert.Equal(elevated, ((SolidColorBrush)done.Background).Color);
                    Assert.Equal(outline, ((SolidColorBrush)done.BorderBrush).Color);
                    Assert.Equal(text, ((SolidColorBrush)done.Foreground).Color);
                    Assert.NotEqual(outline, ((SolidColorBrush)done.Background).Color);
                    Assert.NotEqual(takeFill, ((SolidColorBrush)done.Background).Color);

                    // Take is the only visible filled button
                    var filled = FindAll<Button>(view)
                        .Where(b => b.IsVisible && b.Background is SolidColorBrush s && s.Color == takeFill)
                        .ToList();
                    Assert.Single(filled);
                    Assert.Same(take, filled[0]);
                });
            }
            finally
            {
                themeService.SetTheme(AvasRoutingTool.Services.ThemeMode.Dark);
            }
        }

        private static void AssertDoneOutlineHasEnoughContrast(AvasRoutingTool.Services.ThemeMode mode)
        {
            var themeService = new AvasRoutingTool.Services.ThemeService();
            try
            {
                themeService.SetTheme(mode);
                var elevated = ((SolidColorBrush)Application.Current.FindResource("Brush.Surface.Elevated")).Color;
                var outline = ((SolidColorBrush)Application.Current.FindResource("Brush.Border.DropTarget")).Color;
                var text = ((SolidColorBrush)Application.Current.FindResource("Brush.Text.Primary")).Color;
                var page = ((SolidColorBrush)Application.Current.FindResource("Brush.Background.Dark")).Color;

                Assert.True(Contrast(text, elevated) >= 4.5, $"Done placing text on Surface.Elevated: {Contrast(text, elevated):F2} (need 4.5)");
                Assert.True(Contrast(outline, elevated) >= 3.0, $"Done placing outline on Surface.Elevated: {Contrast(outline, elevated):F2} (need 3.0)");
                Assert.True(Contrast(outline, page) >= 3.0, $"Done placing outline on the page surface: {Contrast(outline, page):F2} (need 3.0)");
            }
            finally
            {
                themeService.SetTheme(AvasRoutingTool.Services.ThemeMode.Dark);
            }
        }

        // ---- Composer round 2 C3 (part a): one amber frame, neutral staged blocks, a plain armed ring, a neutral Undo ----

        [Theory]
        [InlineData(AvasRoutingTool.Services.ThemeMode.Dark)]
        [InlineData(AvasRoutingTool.Services.ThemeMode.Light)]
        public void Composer_StagedColours_AreOneAmberFrame(AvasRoutingTool.Services.ThemeMode mode) =>
            RunSta(() =>
            {
                var themeService = new AvasRoutingTool.Services.ThemeService();
                try
                {
                    themeService.SetTheme(mode);
                    var rig = new ComposerRig();
                    var c = rig.Composer("matrix2x2");
                    c.Arm(rig.Tx1);
                    c.TapBlock(0);
                    var view = rig.View(c);

                    ShowingAt(view, 1904, 940, () =>
                    {
                        var amber = ((SolidColorBrush)Application.Current.FindResource("Brush.Status.Staged")).Color;
                        var neutral = ((SolidColorBrush)Application.Current.FindResource("Brush.Text.Muted")).Color;

                        // The canvas frame is the one amber frame while the layout is staged
                        var frame = (System.Windows.Shapes.Rectangle)Named(view, "CanvasFrame").Single();
                        Assert.Equal(amber, ((SolidColorBrush)frame.Stroke).Color);
                        Assert.NotEmpty(frame.StrokeDashArray);

                        // The staged block's state line is a neutral dashed outline, 2 px
                        var stateStyle = (Style)Application.Current.FindResource("Style.Shape.BlockState");
                        var stateLines = FindAll<System.Windows.Shapes.Rectangle>(view).Where(r => r.Style == stateStyle && r.IsVisible).ToList();
                        var staged = stateLines.Single(r => r.DataContext is ComposerBlockViewModel b && b.State == BlockState.Staged);
                        Assert.Equal(neutral, ((SolidColorBrush)staged.Stroke).Color);
                        Assert.NotEmpty(staged.StrokeDashArray);
                        Assert.Equal(2.0, staged.StrokeThickness);

                        // No other state line is amber
                        Assert.DoesNotContain(stateLines, r => ((SolidColorBrush)r.Stroke).Color == amber);
                    });
                }
                finally
                {
                    themeService.SetTheme(AvasRoutingTool.Services.ThemeMode.Dark);
                }
            });

        [Fact]
        public void Composer_ArmedOverlay_HasNoTintFill() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("matrix2x2");
                c.Arm(rig.Tx1); // the empty blocks offer "+ Place"
                var view = rig.View(c);
                Showing(view, () =>
                {
                    Relayout(view);
                    var rings = Named(view, "PlaceRing").Where(r => r.IsVisible).Cast<System.Windows.Shapes.Rectangle>().ToList();
                    Assert.NotEmpty(rings);
                    var outline = ((SolidColorBrush)Application.Current.FindResource("Brush.Border.DropTarget")).Color;
                    foreach (var ring in rings)
                    {
                        Assert.Null(ring.Fill);
                        Assert.Equal(outline, ((SolidColorBrush)ring.Stroke).Color);
                        Assert.Equal(2.0, ring.StrokeThickness);
                    }
                });
            });

        [Fact]
        public void Composer_Undo_BorderIsNeutral_GreenStaysOnTheSuccessBanner() =>
            RunSta(() =>
            {
                var now = new DateTime(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc);
                var (vm, rx, sources) = Overview(() => now);
                Pump(() => vm.OpenComposerAsync(rx, "matrix2x2"));
                var c = vm.ActiveComposer!;
                c.Arm(sources[0]);
                c.TapBlock(c.Blocks[0].Index);
                c.Disarm();
                Pump(c.TakeAsync);
                c.Arm(sources[1]);
                c.TapBlock(c.Blocks[1].Index);
                c.Disarm();
                Pump(c.TakeAsync);
                Assert.Equal(BannerKind.Success, c.Banner.Kind);
                var view = new VisualRoutingView { DataContext = vm };
                ShowingAt(view, 1008, 615, () =>
                {
                    var undo = (Button)Named(view, "UndoButton").Single();
                    var neutral = ((SolidColorBrush)Application.Current.FindResource("Brush.Border.Control")).Color;
                    Assert.Equal(neutral, ((SolidColorBrush)undo.BorderBrush).Color);

                    var online = ((SolidColorBrush)Application.Current.FindResource("Brush.Status.Online")).Color;
                    var banner = (Border)Named(view, "BannerHost").Single();
                    Assert.Equal(online, ((SolidColorBrush)banner.BorderBrush).Color);
                });
            });

        [Theory]
        [InlineData(AvasRoutingTool.Services.ThemeMode.Dark)]
        [InlineData(AvasRoutingTool.Services.ThemeMode.Light)]
        public void Composer_NeutralStagedOutline_HasEnoughContrast_InBothThemes(AvasRoutingTool.Services.ThemeMode mode) =>
            RunSta(() =>
            {
                var themeService = new AvasRoutingTool.Services.ThemeService();
                try
                {
                    themeService.SetTheme(mode);
                    var neutral = ((SolidColorBrush)Application.Current.FindResource("Brush.Text.Muted")).Color;
                    var mat = ((SolidColorBrush)Application.Current.FindResource("Brush.Surface.PreviewMat")).Color;
                    Assert.True(Contrast(neutral, mat) >= 3.0, $"staged outline on the mat in {mode}: {Contrast(neutral, mat):F2} (need 3.0)");
                }
                finally
                {
                    themeService.SetTheme(AvasRoutingTool.Services.ThemeMode.Dark);
                }
            });

        // ---- UI v2 Task 5a: rails are token-clamped star columns ----

        [Fact]
        public void Composer_Rails_AreStarColumnsClampedByTokens() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = rig.View(rig.Composer("matrix2x2"));
                var leftMin = (double)Application.Current.FindResource("Size.Composer.Rail.Left.Min");
                var leftMax = (double)Application.Current.FindResource("Size.Composer.Rail.Left.Max");
                var rightMin = (double)Application.Current.FindResource("Size.Composer.Rail.Right.Min");
                var rightMax = (double)Application.Current.FindResource("Size.Composer.Rail.Right.Max");
                Assert.Equal((240d, 320d, 264d, 360d), (leftMin, leftMax, rightMin, rightMax));

                var left = (ColumnDefinition)view.FindName("LeftRailColumn");
                var right = (ColumnDefinition)view.FindName("RightRailColumn");
                Assert.True(left.Width.IsStar && right.Width.IsStar, "the rails are star columns");
                Assert.Equal(leftMin, left.MinWidth);
                Assert.Equal(leftMax, left.MaxWidth);
                Assert.Equal(rightMin, right.MinWidth);
                Assert.Equal(rightMax, right.MaxWidth);

                // 1024x720 floor: identical to the old narrow rails, the canvas keeps its 440 px
                ShowingAt(view, 976, 600, () =>
                {
                    Assert.Equal(240, left.ActualWidth, 1);
                    Assert.Equal(264, right.ActualWidth, 1);
                    var canvas = Named(view, "CanvasHost").Single();
                    Assert.InRange(canvas.ActualWidth, 420, 470); // the centre column minus its 16 px margins and the host margin
                });

                // In between the rails grow with the window instead of jumping at 1200
                ShowingAt(view, 1100, 700, () =>
                {
                    Assert.InRange(left.ActualWidth, 250.5, leftMax - 0.5);
                    Assert.InRange(right.ActualWidth, rightMin, rightMax);
                });

                // Full HD: both rails at their caps
                ShowingAt(view, 1888, 800, () =>
                {
                    Assert.Equal(leftMax, left.ActualWidth, 1);
                    Assert.Equal(rightMax, right.ActualWidth, 1);
                });
            });

        [Fact]
        public void Composer_At1920x1080_UsesTheFullHdSpace() =>
            RunSta(() =>
            {
                var (vm, rx, sources) = Overview();
                Pump(() => vm.OpenComposerAsync(rx, "matrix2x2"));
                var c = vm.ActiveComposer!;
                c.Arm(sources[1]);
                foreach (var b in c.Blocks.ToList()) c.TapBlock(b.Index);
                c.Disarm();
                var view = new VisualRoutingView { DataContext = vm };

                // A maximized Window at 1920x1080 and 100%: the content area is about 1904 x 940 under the title bar and the app header.
                ShowingAt(view, 1904, 940, () =>
                {
                    var composer = FindAll<MultiViewComposerView>(view).Single(v => v.IsVisible);
                    var left = (ColumnDefinition)composer.FindName("LeftRailColumn");
                    var right = (ColumnDefinition)composer.FindName("RightRailColumn");
                    Assert.Equal(320, left.ActualWidth, 1);
                    Assert.Equal(360, right.ActualWidth, 1);

                    var take = (FrameworkElement)Named(view, "TakeButton").Single();
                    Assert.True(take.IsVisible);
                    var tr = BoundsIn(take, view);
                    Assert.True(tr.Top >= 0 && tr.Bottom <= view.ActualHeight + 0.5, $"Take at {tr} in a {view.ActualHeight} high view");

                    var blocks = Named(view, "BlockSurface").Where(e => e.IsVisible).ToList();
                    Assert.Equal(4, blocks.Count);
                    Assert.All(blocks, b => Assert.True(b.ActualWidth >= 480, $"block is {b.ActualWidth} wide"));
                    var canvas = Named(view, "CanvasHost").Single();
                    Assert.True(canvas.ActualWidth >= 1000 && canvas.ActualHeight >= 560, $"canvas host is {canvas.ActualWidth}x{canvas.ActualHeight}");
                });
            });

        // ---- UI v2 Task 5b: central layout option style, proportional schematic ----

        [Fact]
        public void Composer_LayoutTile_UsesCentralStyle_KeepsPartBd_AndSchematicScales() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("matrix2x2");
                var view = rig.View(c);

                ShowingAt(view, 976, 600, () =>
                {
                    var central = Application.Current.FindResource("Style.Button.ComposerLayoutOption");
                    var tiles = Named(view, "LayoutTile").Where(t => t.IsVisible).Cast<Button>().ToList();
                    Assert.Equal(5, tiles.Count);
                    Assert.All(tiles, t =>
                    {
                        Assert.Same(central, t.Style);
                        Assert.IsType<Border>(t.Template.FindName("bd", t));
                        Assert.True(t.ActualHeight >= (double)Application.Current.FindResource("Size.Composer.Tile.Min"), $"tile is {t.ActualHeight} high");
                    });

                    // the schematic fills its column: 88-125 px at the 240 px rail, and its picture keeps 16:9
                    var schematics = FindAll<LayoutSchematic>(view).Where(s => s.IsVisible).ToList();
                    Assert.Equal(5, schematics.Count);
                    foreach (var s in schematics)
                    {
                        Assert.InRange(s.ActualWidth, 88, 125);
                        var panel = FindAll<AvasRoutingTool.Controls.FractionalLayoutPanel>(s).Single();
                        Assert.InRange(panel.ActualWidth / panel.ActualHeight, 16.0 / 9.0 - 0.02, 16.0 / 9.0 + 0.02);
                        Assert.InRange(s.ActualHeight, s.ActualWidth * 9 / 16 - 5, s.ActualWidth * 9 / 16 + 5);
                    }

                    // the selected tile says so with a check icon and the word, not colour alone
                    var selected = tiles.Single(t => ((ComposerLayoutOptionViewModel)t.DataContext).IsSelected);
                    var row = FindAll<StackPanel>(selected).Single(p => p.Name == "SelectedRow");
                    Assert.True(row.IsVisible);
                    Assert.Equal(Wpf.Ui.Controls.SymbolRegular.Checkmark24, FindAll<Wpf.Ui.Controls.SymbolIcon>(row).Single().Symbol);
                    Assert.Contains(FindAll<TextBlock>(row), t => t.Text == "Selected");
                    foreach (var other in tiles.Where(t => t != selected))
                        Assert.False(FindAll<StackPanel>(other).Single(p => p.Name == "SelectedRow").IsVisible);
                });
            });

        [Fact]
        public void LayoutSchematic_CellNumbers_AreAtLeast12() =>
            RunSta(() =>
            {
                TestResources.Ensure();
                var schematic = new LayoutSchematic { LayoutId = "matrix2x2", Width = 120 };
                var window = new Window { Content = schematic, SizeToContent = SizeToContent.WidthAndHeight, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -10000 };
                window.Show();
                try
                {
                    Relayout(schematic);
                    var numbers = FindAll<TextBlock>(schematic).Where(t => t.IsVisible).ToList();
                    Assert.NotEmpty(numbers);
                    Assert.All(numbers, t => Assert.True(t.FontSize >= 12, $"schematic text is {t.FontSize}"));
                    var off = new LayoutSchematic { LayoutId = "off", Width = 120 };
                    window.Content = off;
                    Relayout(off);
                    Assert.All(FindAll<TextBlock>(off).Where(t => t.IsVisible), t => Assert.True(t.FontSize >= 12));
                }
                finally { window.Close(); }
            });

        // ---- UI v2 Task 6: header, rail cards, status line ----

        [Fact]
        public void Composer_BackButton_HasArrowIcon_AndKeepsName() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = rig.View(rig.Composer("matrix2x2"));
                Showing(view, () =>
                {
                    var back = (Button)Named(view, "CloseComposerButton").Single();
                    Assert.Equal("Back to displays", AutomationProperties.GetName(back));
                    Assert.True(back.ActualHeight >= 48);
                    var icon = FindAll<Wpf.Ui.Controls.SymbolIcon>(back).Single();
                    Assert.Equal(Wpf.Ui.Controls.SymbolRegular.ArrowLeft24, icon.Symbol);
                    Assert.Equal((double)Application.Current.FindResource("Size.Icon.M"), icon.FontSize);
                    Assert.Contains(FindAll<TextBlock>(back), t => t.Text == "Back to displays");
                });
            });

        [Fact]
        public void Composer_RailCards_AreUiCards_WithAppBackground() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = rig.View(rig.Composer("matrix2x2"));
                Showing(view, () =>
                {
                    var cards = FindAll<Wpf.Ui.Controls.Card>(view);
                    Assert.Equal(2, cards.Count); // ON SCREEN NOW and CHANGES
                    var surface = ((SolidColorBrush)Application.Current.FindResource("Brush.Surface.Card")).Color;
                    var changes = Named(view, "ChangesCard").Single(); // the last card takes the free height: Style.Card.Rail.Fill
                    Assert.All(cards, c =>
                    {
                        var style = ReferenceEquals(c, changes) ? "Style.Card.Rail.Fill" : "Style.Card.Rail";
                        Assert.Same(Application.Current.FindResource(style), c.Style);
                        Assert.Equal(surface, ((SolidColorBrush)c.Background).Color);
                        Assert.False(c.Focusable);
                    });
                    Assert.Contains(FindAll<TextBlock>(view), t => t.Text == "ON SCREEN NOW" && t.IsVisible);
                });
            });

        [Fact]
        public void Composer_HeaderChips_ArePlainText_AndCollapseWhenEmpty() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var chosen = rig.View(rig.Composer("matrix2x2"));
                var none = rig.View(rig.Composer());

                Showing(chosen, () =>
                {
                    var output = (TextBlock)Assert.Single(Named(chosen, "OutputChip"));
                    Assert.True(output.IsVisible && output.Text.Length > 0, $"the output chip is not shown ('{output.Text}')");
                    Assert.Same(Application.Current.FindResource("Style.Text.Meta"), output.Style);

                    var rate = (TextBlock)Assert.Single(Named(chosen, "RateChip"));
                    Assert.Same(Application.Current.FindResource("Style.Text.Meta"), rate.Style);
                    Assert.Equal(!string.IsNullOrEmpty(rate.Text), rate.IsVisible);

                    // The header facts are plain text now: no badge in the view keeps the shared Info style
                    var info = Application.Current.FindResource("Style.Badge.Info");
                    Assert.DoesNotContain(FindAll<Wpf.Ui.Controls.Badge>(chosen), b => ReferenceEquals(b.Style, info));
                });
                Showing(none, () =>
                {
                    // no layout chosen yet: the output chip has nothing to say and takes no room
                    var output = (TextBlock)Assert.Single(Named(none, "OutputChip"));
                    Assert.False(output.IsVisible, $"the output chip is shown ('{output.Text}') before a layout is chosen");
                });
            });

        // ---- Composer round 2 C3 (part b): STAGED and CHANGES are neutral text with an amber dot; amber stays for staged only ----

        [Theory]
        [InlineData(AvasRoutingTool.Services.ThemeMode.Dark)]
        [InlineData(AvasRoutingTool.Services.ThemeMode.Light)]
        public void Composer_StatusHeadings_AreNeutralText_WithAnAmberDot(AvasRoutingTool.Services.ThemeMode mode) =>
            RunSta(() =>
            {
                var themeService = new AvasRoutingTool.Services.ThemeService();
                try
                {
                    themeService.SetTheme(mode);
                    var text = ((SolidColorBrush)Application.Current.FindResource("Brush.Text.Primary")).Color;
                    var amber = ((SolidColorBrush)Application.Current.FindResource("Brush.Status.Staged")).Color;
                    var accent = ((SolidColorBrush)Application.Current.FindResource("Brush.Text.Accent")).Color;
                    var dotSize = (double)Application.Current.FindResource("Size.Dot.M");

                    // Staged: the status word and the CHANGES heading are primary text, each with a 10 px amber dot in front of it
                    var rig = new ComposerRig();
                    var c = rig.Composer("matrix2x2");
                    c.Arm(rig.Tx1);
                    c.TapBlock(0);
                    c.Disarm();
                    var staged = rig.View(c);
                    ShowingAt(staged, 1904, 940, () =>
                    {
                        var label = (TextBlock)Assert.Single(Named(staged, "StagedLabel"));
                        Assert.True(label.IsVisible, "STAGED is not shown");
                        Assert.Equal(text, ((SolidColorBrush)label.Foreground).Color);
                        var dot = (System.Windows.Shapes.Ellipse)Assert.Single(Named(staged, "StagedDot"));
                        Assert.True(dot.IsVisible, "the STAGED dot is not shown");
                        Assert.Same(Application.Current.FindResource("Style.Shape.StagedDot"), dot.Style);
                        Assert.Equal(amber, ((SolidColorBrush)dot.Fill).Color);
                        Assert.Equal(dotSize, dot.ActualWidth);
                        Assert.Equal(dotSize, dot.ActualHeight);
                        Assert.Same(label.Parent, dot.Parent); // the dot and the word share one row

                        var changes = FindAll<TextBlock>(staged).Single(t =>
                            t.IsVisible && string.Concat(t.Inlines.OfType<Run>().Select(r => r.Text)).StartsWith("CHANGES (", StringComparison.Ordinal));
                        Assert.Equal(text, ((SolidColorBrush)changes.Foreground).Color);
                        var changesDot = (System.Windows.Shapes.Ellipse)Assert.Single(Named(staged, "ChangesDot"));
                        Assert.True(changesDot.IsVisible, "the CHANGES dot is not shown while a change is staged");
                        Assert.Equal(amber, ((SolidColorBrush)changesDot.Fill).Color);
                        Assert.Equal(dotSize, changesDot.ActualWidth);
                        Assert.Same(changes.Parent, changesDot.Parent);
                    });

                    // On screen: the status word stays blue, and no dot shows (nothing is staged, so CHANGES has none either)
                    var live = new ComposerRig();
                    var on = live.Composer("matrix2x2");
                    on.Arm(live.Tx1);
                    on.TapBlock(0);
                    on.Disarm();
                    Pump(on.TakeAsync);
                    var onScreen = live.View(on);
                    ShowingAt(onScreen, 1904, 940, () =>
                    {
                        var word = (TextBlock)Assert.Single(Named(onScreen, "OnScreenLabel"));
                        Assert.True(word.IsVisible, "ON SCREEN is not shown");
                        Assert.Equal(accent, ((SolidColorBrush)word.Foreground).Color);
                        Assert.False(Named(onScreen, "StagedDot").Single().IsVisible, "a STAGED dot shows while the screen is the staged picture");
                        Assert.False(Named(onScreen, "ChangesDot").Single().IsVisible, "a CHANGES dot shows with nothing staged");
                    });
                }
                finally
                {
                    themeService.SetTheme(AvasRoutingTool.Services.ThemeMode.Dark);
                }
            });

        [Theory]
        [InlineData(AvasRoutingTool.Services.ThemeMode.Dark)]
        [InlineData(AvasRoutingTool.Services.ThemeMode.Light)]
        public void Composer_PlainHeaderFacts_AndStatusWords_HaveEnoughContrastOnThePage(AvasRoutingTool.Services.ThemeMode mode) =>
            RunSta(() =>
            {
                var themeService = new AvasRoutingTool.Services.ThemeService();
                try
                {
                    themeService.SetTheme(mode);
                    var page = ((SolidColorBrush)Application.Current.FindResource("Brush.Background.Dark")).Color;
                    var secondary = ((SolidColorBrush)Application.Current.FindResource("Brush.Text.Secondary")).Color;
                    var primary = ((SolidColorBrush)Application.Current.FindResource("Brush.Text.Primary")).Color;
                    Assert.True(Contrast(secondary, page) >= 4.5, $"Text.Secondary on Background.Dark in {mode}: {Contrast(secondary, page):F2} (need 4.5)");
                    Assert.True(Contrast(primary, page) >= 4.5, $"Text.Primary on Background.Dark in {mode}: {Contrast(primary, page):F2} (need 4.5)");
                }
                finally
                {
                    themeService.SetTheme(AvasRoutingTool.Services.ThemeMode.Dark);
                }
            });

        [Fact]
        public void Composer_UnplacedChips_UseTheSharedStagedChipLook() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("matrix2x2");
                c.Arm(rig.Tx2);
                c.TapBlock(2);
                c.Disarm();
                Pump(() => c.SelectLayoutAsync("side_by_side"));
                var view = rig.View(c);
                Showing(view, () =>
                {
                    var chip = (Button)Named(view, "UnplacedChip").First(e => e.IsVisible);
                    Assert.IsType<System.Windows.Shapes.Rectangle>(chip.Template.FindName("DraftFrame", chip));
                    Assert.Equal((double)Application.Current.FindResource("Size.Touch.Min"), chip.MinHeight);
                });
            });

        // ---- UI v2 Tasks 7a/7b: block overlays, density, surface, corner clear, strip ----

        [Fact]
        public void Composer_SmallBlocks_AreCompact_ThroughBlockDensity() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = rig.View(rig.Composer("quad_1_3")); // block 1 is large, blocks 2-4 are small
                ShowingAt(view, 976, 600, () =>
                {
                    var roots = Named(view, "BlockRoot").Where(e => e.IsVisible).ToList();
                    Assert.Equal(4, roots.Count);
                    var small = roots.Where(r => r.ActualWidth < BlockDensity.CompactBelow).ToList();
                    var large = roots.Except(small).ToList();
                    Assert.NotEmpty(small);
                    Assert.Single(large);

                    var lead = (double)Application.Current.FindResource("Font.Size.Lead");
                    var number = (double)Application.Current.FindResource("Font.Size.Composer.BlockNumber");
                    foreach (var r in small)
                    {
                        Assert.True(BlockDensity.GetIsCompact(r));
                        Assert.Equal(lead, Named(r, "NumberText").Single().GetValue(TextBlock.FontSizeProperty));
                        Assert.False(Named(r, "SizeChip").Single().IsVisible);
                        Assert.False(Named(r, "EmptyHint").Single().IsVisible);
                    }
                    var big = large.Single();
                    Assert.False(BlockDensity.GetIsCompact(big));
                    Assert.Equal(number, Named(big, "NumberText").Single().GetValue(TextBlock.FontSizeProperty));
                    Assert.True(Named(big, "SizeChip").Single().IsVisible);
                    Assert.True(Named(big, "EmptyHint").Single().IsVisible);

                    // nothing on a block is smaller than the 12 px floor
                    Assert.All(roots.SelectMany(r => FindAll<TextBlock>(r)).Where(t => t.IsVisible), t => Assert.True(t.FontSize >= 12, $"text {t.Text} is {t.FontSize}"));
                });
            });

        [Fact]
        public void Composer_ReplaceMoveStrip_IsCompactOnSmallBlocks_AndEqualWidthOnNormalOnes() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("pip");
                c.Arm(rig.Tx1);
                c.TapBlock(0); // the PIP inset (window 0) is small
                c.Disarm();
                c.TapBlock(0); // a placed block: the strip opens
                var view = rig.View(c);

                ShowingAt(view, 976, 600, () =>
                {
                    var replace = (Button)Named(view, "ReplaceButton").Single(e => e.IsVisible);
                    var move = (Button)Named(view, "MoveButton").Single(e => e.IsVisible);
                    var compact = (double)Application.Current.FindResource("Size.Touch.Compact");
                    var min = (double)Application.Current.FindResource("Size.Touch.Min");
                    Assert.InRange(replace.ActualHeight, compact, min - 0.5);
                    Assert.InRange(move.ActualHeight, compact, min - 0.5);
                    Assert.Equal(replace.ActualWidth, move.ActualWidth, 1);
                    var gap = BoundsIn(move, view).Left - BoundsIn(replace, view).Right;
                    Assert.InRange(gap, 7.5, 9);
                });
            });

        [Fact]
        public void Composer_UnavailableBlock_LockIsASymbolIcon() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("quad_1_3");
                c.Arm(rig.Tx1);
                c.TapBlock(0);
                var view = rig.View(c);
                Showing(view, () =>
                {
                    var locks = Named(view, "LockIcon").Where(e => e.IsVisible).ToList();
                    Assert.Equal(3, locks.Count);
                    Assert.All(locks, l => Assert.Equal(Wpf.Ui.Controls.SymbolRegular.LockClosed24, ((Wpf.Ui.Controls.SymbolIcon)l).Symbol));
                });
            });

        // ---- UI v2 round 2 C8: the place pill is two lines (the verb, then the name trimmed); the full name is in the block's tooltip ----

        /// <summary>A source whose name is longer than a place pill shows on one line (48 characters).</summary>
        private static SourceCardViewModel LongNamedSource() =>
            new(new AvasDevice { DeviceId = "74fe488b07c0", DeviceName = "AVAS_ENCODER_OPERATING_ROOM_CAMERA_FEED_TWO_SFPA", VendorId = 105, IsTransmitter = true, IsActive = true });

        [Fact]
        public void Composer_PlacePill_TruncatesLongNames_AndTheTooltipHasTheFullName() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var longSource = LongNamedSource();
                rig.Sources.Add(longSource);
                var c = rig.Composer("matrix2x2");
                c.Arm(longSource);
                var view = rig.View(c);

                Showing(view, () => // full-size blocks: the name is on line 2 (a compact block shows the verb only)
                {
                    var verbs = Named(view, "PlaceVerbText").Where(e => e.IsVisible).Cast<TextBlock>().ToList();
                    var names = Named(view, "PlaceNameText").Where(e => e.IsVisible).Cast<TextBlock>().ToList();
                    Assert.Equal(4, verbs.Count);
                    Assert.Equal(4, names.Count);
                    Assert.All(verbs, v => Assert.Equal("+ Place", v.Text));
                    Assert.All(names, n => Assert.Equal(longSource.DeviceName, n.Text));
                    Assert.All(names, n => Assert.Equal(TextTrimming.CharacterEllipsis, n.TextTrimming));
                    var max = (double)Application.Current.FindResource("Size.Composer.Overlay.Text.Max");
                    Assert.All(names, n => Assert.True(n.ActualWidth <= max, $"name is {n.ActualWidth} wide"));

                    foreach (var pill in Named(view, "PlacePill").Where(e => e.IsVisible))
                    {
                        var block = Named(view, "BlockRoot").Single(r => ReferenceEquals(r.DataContext, pill.DataContext));
                        Assert.True(pill.ActualWidth <= block.ActualWidth, $"pill {pill.ActualWidth} in block {block.ActualWidth}");
                    }

                    Assert.All(Surfaces(view), s => Assert.Equal(longSource.DeviceName, s.ToolTip as string));
                    c.Disarm();
                    Relayout(view);
                    Assert.All(Surfaces(view), s => Assert.Null(s.ToolTip));
                });
            });

        [Fact]
        public void Composer_PlacePill_PlacedBlock_SaysReplace() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("matrix2x2");
                c.Arm(rig.Tx1);
                c.TapBlock(0);
                c.Arm(rig.Tx2);
                var view = rig.View(c);

                ShowingAt(view, 976, 600, () =>
                {
                    var placed = c.Blocks[0].Index;
                    var empty = c.Blocks[1].Index;
                    Assert.True(InBlock(view, "PlacePill", placed).IsVisible);
                    Assert.Equal("Replace", ((TextBlock)InBlock(view, "PlaceVerbText", placed)).Text);
                    Assert.Equal("+ Place", ((TextBlock)InBlock(view, "PlaceVerbText", empty)).Text);
                });
            });

        [Fact]
        public void Composer_PlacePill_Compact_ShowsVerbOnly() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var longSource = LongNamedSource();
                rig.Sources.Add(longSource);
                var c = rig.Composer("quad_1_3"); // block 1 is large, blocks 2-4 are small
                c.Arm(longSource);
                var view = rig.View(c);

                ShowingAt(view, 976, 600, () =>
                {
                    var roots = Named(view, "BlockRoot").Where(e => e.IsVisible).ToList();
                    var small = roots.Where(r => BlockDensity.GetIsCompact(r)).ToList();
                    Assert.Equal(3, small.Count);
                    foreach (var r in small)
                    {
                        Assert.True(Named(r, "PlacePill").Single().IsVisible);
                        var verb = (TextBlock)Named(r, "PlaceVerbText").Single();
                        Assert.True(verb.IsVisible);
                        Assert.False(Named(r, "PlaceNameText").Single().IsVisible);
                        Assert.True(verb.FontSize >= 12, $"verb is {verb.FontSize}");
                    }

                    var big = roots.Except(small).Single();
                    Assert.True(Named(big, "PlaceNameText").Single().IsVisible);
                });
            });

        // ---- UI v2 Task 8: right rail ----

        [Theory]
        [InlineData(BannerKind.Info, "Info24")]
        [InlineData(BannerKind.Success, "CheckmarkCircle24")]
        [InlineData(BannerKind.Warning, "Warning24")]
        [InlineData(BannerKind.Error, "ErrorCircle24")]
        public void Composer_Banner_ShowsASeverityIconPerKind(BannerKind kind, string symbol) =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("matrix2x2");
                // the banner kinds Warning and Error come from hardware failures: set them the way the view model does (its setter is private)
                typeof(MultiViewComposerViewModel).GetProperty(nameof(MultiViewComposerViewModel.Banner))!.SetValue(c, new ComposerBanner("Something to read", kind));
                var view = rig.View(c);
                Showing(view, () =>
                {
                    var host = Named(view, "BannerHost").Single();
                    Assert.True(host.IsVisible);
                    var icon = FindAll<Wpf.Ui.Controls.SymbolIcon>(host).Single();
                    Assert.Equal(symbol, icon.Symbol.ToString());
                    Assert.Equal((double)Application.Current.FindResource("Size.Icon.M"), icon.FontSize);
                    Assert.DoesNotContain(FindAll<TextBlock>(host), t => t.Text is "i" or "!" or "✓" or "✕");
                    var text = (TextBlock)Named(host, "BannerText").Single();
                    Assert.Equal("Something to read", text.Text);
                    Assert.Equal(kind == BannerKind.Error ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(text));
                });
            });

        [Fact]
        public void Composer_Undo_HasUndoIcon_AndTheLabelStillWraps() =>
            RunSta(() =>
            {
                var now = new DateTime(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc);
                var (vm, rx, sources) = Overview(() => now);
                Pump(() => vm.OpenComposerAsync(rx, "matrix2x2"));
                var c = vm.ActiveComposer!;
                c.Arm(sources[0]);
                c.TapBlock(c.Blocks[0].Index);
                c.Disarm();
                Pump(c.TakeAsync);
                c.Arm(sources[1]);
                c.TapBlock(c.Blocks[1].Index);
                c.Disarm();
                Pump(c.TakeAsync);
                var view = new VisualRoutingView { DataContext = vm };
                ShowingAt(view, 1008, 615, () =>
                {
                    var undo = (Button)Named(view, "UndoButton").Single();
                    Assert.True(undo.IsVisible && undo.ActualHeight >= 48);
                    Assert.Equal(Wpf.Ui.Controls.SymbolRegular.ArrowUndo24, FindAll<Wpf.Ui.Controls.SymbolIcon>(undo).Single().Symbol);
                    var label = FindAll<TextBlock>(undo).Single(t => t.Text.StartsWith("Undo", StringComparison.Ordinal));
                    Assert.Equal(TextWrapping.Wrap, label.TextWrapping);
                });
            });

        [Fact]
        public void Composer_OnScreenPicture_KeepsItsSpokenName_AndMiniCanvasFollowsWidth() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = rig.View(rig.Composer("matrix2x2"));
                ShowingAt(view, 976, 600, () =>
                {
                    var picture = Named(view, "OnScreenPicture").Single();
                    Assert.Equal("On screen now: no MultiView on this screen", AutomationProperties.GetName(picture));
                    var panel = FindAll<AvasRoutingTool.Controls.FractionalLayoutPanel>(picture).First();
                    Assert.True(panel.ActualWidth > 150, $"mini canvas {panel.ActualWidth}");
                    Assert.InRange(panel.ActualHeight, panel.ActualWidth / (16.0 / 9.0) - 1, panel.ActualWidth / (16.0 / 9.0) + 4); // the live-block layer has a 2 px margin
                });
            });

        [Fact]
        public void Composer_Take_IsTheOnlyControlAbove48() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("matrix2x2");
                c.Arm(rig.Tx1);
                c.TapBlock(0);
                c.Disarm();
                var view = rig.View(c);
                ShowingAt(view, 976, 700, () =>
                {
                    var take = (FrameworkElement)Named(view, "TakeButton").Single();
                    Assert.True(take.ActualHeight >= (double)Application.Current.FindResource("Size.Composer.Take.Min"));
                    Assert.IsType<string>(((Button)take).Content);
                    foreach (var name in new[] { "CloseComposerButton", "DiscardButton", "ClearAllButton" })
                    {
                        var b = (FrameworkElement)Named(view, name).Single();
                        Assert.InRange(b.ActualHeight, 36, 55.9);
                    }
                    Assert.Same(Application.Current.FindResource("Style.Button.Take"), ((Button)take).Style);
                });
            });

        // ---- UI v2 Task 9: ON SCREEN NOW follows the live plan's aspect ----

        [Fact]
        public void Composer_OnScreenPicture_UsesTheLiveAspect() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                rig.Client.DisplayEdid = TestEdids.Build((1920, 1200, 60, false)); // a 16:10 panel
                var c = rig.Composer("matrix2x2");
                c.Arm(rig.Tx1);
                c.TapBlock(0);
                c.Disarm();
                Pump(c.TakeAsync);
                var view = rig.View(c);
                Showing(view, () =>
                {
                    var picture = Named(view, "OnScreenPicture").Single();
                    var panels = FindAll<AvasRoutingTool.Controls.FractionalLayoutPanel>(picture);
                    Assert.Equal(2, panels.Count); // the mat and the live blocks
                    Assert.All(panels, p => Assert.Equal(c.LiveAspectRatio, p.AspectRatio, 5));
                    Assert.NotEqual(16.0 / 9.0, c.LiveAspectRatio, 3);
                });
            });

        // ---- Critique C4: the right rail fills the free height; the empty preview names the display ----

        [Fact]
        public void Composer_RightRail_ChangesCardFillsTheFreeHeight() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("matrix2x2");
                c.Arm(rig.Tx1);
                c.TapBlock(0);
                c.Disarm();
                var view = rig.View(c);

                ShowingAt(view, 1904, 940, () =>
                {
                    var scroll = (ScrollViewer)Named(view, "RightRailScroll").Single();
                    var changes = (FrameworkElement)Named(view, "ChangesCard").Single();
                    var changesBottom = BoundsIn(changes, scroll).Bottom;
                    Assert.True(Math.Abs(changesBottom - scroll.ViewportHeight) <= 1, $"Changes card ends at {changesBottom} in a {scroll.ViewportHeight} high rail");

                    // The pinned cluster (its Step 3 heading, banner, Take) starts 8 px under the rail: no dead zone between them
                    var clusterTop = BoundsIn((FrameworkElement)Named(view, "StepTakeHeading").Single(), view).Top;
                    var railBottom = BoundsIn(scroll, view).Bottom;
                    Assert.True(clusterTop - railBottom <= 8.5, $"{clusterTop - railBottom} px between the rail and the pinned cluster");
                });
            });

        [Fact]
        public void Composer_RightRail_StillScrolls_WhenTheContentIsTall() =>
            RunSta(() =>
            {
                var (vm, rx, sources) = Overview();
                Pump(() => vm.OpenComposerAsync(rx, "matrix2x2"));
                var c = vm.ActiveComposer!;
                c.Arm(sources[1]); // the longest name, in every block: four staged changes
                foreach (var b in c.Blocks.ToList()) c.TapBlock(b.Index);
                c.Disarm();
                Assert.Equal(4, c.ChangeCount);
                var view = new VisualRoutingView { DataContext = vm };

                ShowingAt(view, 1008, 615, () =>
                {
                    var scroll = (ScrollViewer)Named(view, "RightRailScroll").Single();
                    Assert.True(scroll.ScrollableHeight > 0, $"rail content {scroll.ExtentHeight} in a {scroll.ViewportHeight} high rail");

                    var take = (FrameworkElement)Named(view, "TakeButton").Single();
                    Assert.True(take.IsVisible);
                    var tr = BoundsIn(take, view);
                    Assert.True(tr.Top >= 0 && tr.Bottom <= view.ActualHeight + 0.5, $"Take at {tr} in a {view.ActualHeight} high view");
                });
            });

        [Fact]
        public void Composer_OnScreenPicture_EmptyShowsTheDisplayName() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("matrix2x2");
                var empty = rig.View(c);
                ShowingAt(empty, 1904, 940, () =>
                {
                    var plate = (TextBlock)Named(empty, "NamePlateText").Single();
                    Assert.True(plate.IsVisible);
                    Assert.Equal(rig.Rx.DeviceName, plate.Text);
                    AssertVisibleText(empty, "No MultiView on this screen");
                });

                c.Arm(rig.Tx1);
                c.TapBlock(0);
                c.Disarm();
                Pump(c.TakeAsync);
                var live = rig.View(c);
                ShowingAt(live, 1904, 940, () =>
                {
                    Assert.False(Named(live, "NamePlateText").Single().IsVisible);
                });
            });

        [Theory]
        [InlineData(AvasRoutingTool.Services.ThemeMode.Dark)]
        [InlineData(AvasRoutingTool.Services.ThemeMode.Light)]
        public void Composer_NamePlate_HasEnoughContrast_OnThePreviewMat(AvasRoutingTool.Services.ThemeMode mode) =>
            RunSta(() =>
            {
                new AvasRoutingTool.Services.ThemeService().SetTheme(mode);
                var text = ((SolidColorBrush)Application.Current.FindResource("Brush.Text.OnPreview")).Color;
                var mat = ((SolidColorBrush)Application.Current.FindResource("Brush.Surface.PreviewMat")).Color;
                Assert.True(Contrast(text, mat) >= 4.5, $"name plate in {mode}: {Contrast(text, mat):0.0}:1");
            });

        [Theory]
        [InlineData(1904, 940)] // a maximized window at 1920x1080
        [InlineData(1008, 615)] // the MainWindow minimum
        public void Composer_ArmingDoesNotMoveTake(int width, int height) =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("matrix2x2");
                var view = rig.View(c);

                ShowingAt(view, width, height, () =>
                {
                    var take = (FrameworkElement)Named(view, "TakeButton").Single();
                    var before = take.TranslatePoint(new Point(0, 0), view);
                    var beforeSize = new Size(take.ActualWidth, take.ActualHeight);

                    c.Arm(rig.Tx1);
                    Relayout(view);

                    var after = take.TranslatePoint(new Point(0, 0), view);
                    Assert.True(Math.Abs(after.X - before.X) <= 0.5 && Math.Abs(after.Y - before.Y) <= 0.5, $"Take moved from {before} to {after}");
                    Assert.True(Math.Abs(take.ActualWidth - beforeSize.Width) <= 0.5 && Math.Abs(take.ActualHeight - beforeSize.Height) <= 0.5,
                        $"Take resized from {beforeSize} to {take.ActualWidth}x{take.ActualHeight}");
                });
            });

        // ---- UI v2 Task 10: picker popup from tokens ----

        private static (ComposerRig Rig, MultiViewComposerViewModel C) PickerRig()
        {
            var rig = new ComposerRig();
            var c = rig.Composer("quad_1_3");
            c.Arm(rig.Tx1);
            c.TapBlock(0);   // TX1 takes the big block
            c.Disarm();
            c.TapBlock(1);   // an empty small block: the picker opens, TX1 cannot go here
            return (rig, c);
        }

        [Fact]
        public void Composer_PickerTile_UsesCentralStyle_AndKeepsPartBd() =>
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
                    Assert.Same(Application.Current.FindResource("Style.Surface.Popup"), ((Border)content).Style);
                    var tiles = Named(content, "PickerTile").Cast<Button>().ToList();
                    Assert.NotEmpty(tiles);
                    var controlBorder = ((SolidColorBrush)Application.Current.FindResource("Brush.Border.Control")).Color;
                    Assert.All(tiles, t =>
                    {
                        Assert.Same(Application.Current.FindResource("Style.Button.PickerTile"), t.Style);
                        var bd = Assert.IsType<Border>(t.Template.FindName("bd", t));
                        Assert.Equal(controlBorder, ((SolidColorBrush)bd.BorderBrush).Color);
                    });
                });
            });

        [Fact]
        public void Composer_PickerPopup_TilesAreTokenWide_AndThumbnailsAre16x9() =>
            RunSta(() =>
            {
                var (rig, c) = PickerRig();
                var view = rig.View(c);
                Showing(view, () =>
                {
                    var popup = (Popup)Named(view, "PickerPopup").Single();
                    var content = (FrameworkElement)popup.Child;
                    Relayout(content);
                    var slot = (double)Application.Current.FindResource("Size.Composer.Picker.Tile");
                    var frameMax = (double)Application.Current.FindResource("Size.Composer.Picker.Frame.Max");
                    Assert.Equal(frameMax, ((Border)content).MaxWidth);
                    foreach (var tile in Named(content, "PickerTile").Cast<Button>())
                    {
                        Assert.Equal(slot, tile.ActualWidth + tile.Margin.Right, 1);
                        var thumb = FindAll<AvasRoutingTool.Controls.FractionalLayoutPanel>(tile).Single();
                        Assert.InRange(thumb.ActualWidth / thumb.ActualHeight, 16.0 / 9.0 - 0.05, 16.0 / 9.0 + 0.05);
                        Assert.True(thumb.ActualWidth >= 160 && thumb.ActualHeight >= 90, $"thumbnail {thumb.ActualWidth}x{thumb.ActualHeight}");
                    }
                });
            });

        [Fact]
        public void Composer_PickerPopup_DisabledTile_ShowsLockSymbol() =>
            RunSta(() =>
            {
                var (rig, c) = PickerRig();
                var view = rig.View(c);
                Showing(view, () =>
                {
                    var popup = (Popup)Named(view, "PickerPopup").Single();
                    var content = (FrameworkElement)popup.Child;
                    Relayout(content);
                    var tiles = Named(content, "PickerTile").Cast<Button>().ToList();
                    var disabled = tiles.Where(t => !t.IsEnabled).ToList();
                    Assert.NotEmpty(disabled);
                    foreach (var t in tiles)
                    {
                        var locks = FindAll<Wpf.Ui.Controls.SymbolIcon>(t).Where(i => i.Symbol == Wpf.Ui.Controls.SymbolRegular.LockClosed24).ToList();
                        Assert.Single(locks);
                        Assert.Equal(!t.IsEnabled, locks[0].IsVisible);
                    }
                });
            });

        // ---- UI v2 Task 11: palette card ----

        [Fact]
        public void CompactSourceCard_HasNoFixedSize_AndKeepsAThumbnailAt16x9() =>
            RunSta(() =>
            {
                var (_, _, sources) = Overview();
                var card = new CompactSourceCardView { DataContext = sources[0] };
                Assert.Same(DependencyProperty.UnsetValue, card.ReadLocalValue(FrameworkElement.WidthProperty));
                Assert.Same(DependencyProperty.UnsetValue, card.ReadLocalValue(FrameworkElement.HeightProperty));
                Showing(card, () =>
                {
                    var min = (double)Application.Current.FindResource("Size.Composer.Palette.Card.Min");
                    Assert.True(card.ActualWidth >= min, $"palette card is {card.ActualWidth} wide");
                    var thumb = FindAll<AvasRoutingTool.Controls.FractionalLayoutPanel>(card).Single();
                    Assert.InRange(thumb.ActualWidth / thumb.ActualHeight, 16.0 / 9.0 - 0.05, 16.0 / 9.0 + 0.05);
                    Assert.True(thumb.ActualWidth >= 140, $"thumbnail {thumb.ActualWidth}x{thumb.ActualHeight}");
                    Assert.True(card.DesiredSize.Height <= 160, $"palette card wants {card.DesiredSize.Height} high"); // content-sized: it stretches in a bare window
                });
            });

        [Fact]
        public void CompactSourceCard_Badges_AreUiBadges() =>
            RunSta(() =>
            {
                var (_, _, sources) = Overview();
                sources[0].IsArmed = true;
                sources[0].UsedInBlocks = 1;
                sources[1].UsedInBlocks = 2;
                var one = new CompactSourceCardView { DataContext = sources[0] };
                var two = new CompactSourceCardView { DataContext = sources[1] };
                Showing(one, () =>
                {
                    var used = Assert.IsType<Wpf.Ui.Controls.Badge>(Named(one, "UsedInBlocksBadge").Single());
                    Assert.True(used.IsVisible);
                    AssertVisibleText(one, "in 1 block");
                    var placing = FindAll<Wpf.Ui.Controls.Badge>(one).Single(b => !ReferenceEquals(b, used));
                    Assert.True(placing.IsVisible);
                    Assert.Same(Application.Current.FindResource("Style.Badge.Armed"), placing.Style);
                    Assert.Same(Application.Current.FindResource("Style.Badge.Staged"), ((Style)used.Style).BasedOn);
                });
                Showing(two, () =>
                {
                    AssertVisibleText(two, "in 2 blocks");
                    Assert.False(FindAll<Wpf.Ui.Controls.Badge>(two).Single(b => b.Name != "UsedInBlocksBadge").IsVisible);
                });
            });
    }
}
