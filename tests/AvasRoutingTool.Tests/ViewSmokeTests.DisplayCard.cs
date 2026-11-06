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
        public void DisplayCardView_Unrouted_RendersWithoutBindingCrash() =>
            RunSta(() => Layout(new DisplayCardView { DataContext = new DisplayCardViewModel(Rx()) }));

        [Fact]
        public void DisplayCardView_Routed_RendersWithoutBindingCrash() =>
            RunSta(() => Layout(new DisplayCardView { DataContext = new DisplayCardViewModel(Rx("74fe488b07bb")) }));

        [Theory]
        [InlineData("matrix2x2")]
        [InlineData("pip")]
        [InlineData("side_by_side")]
        [InlineData("quad_1_3")]
        public void DisplayCardView_MultiViewEmptyWindows_RendersWithoutBindingCrash(string layoutId) =>
            RunSta(() => Layout(new DisplayCardView { DataContext = MultiViewDisplay(layoutId, fill: false) }));

        [Theory]
        [InlineData("matrix2x2")]
        [InlineData("pip")]
        public void DisplayCardView_MultiViewFilledWindows_RendersWithoutBindingCrash(string layoutId) =>
            RunSta(() => Layout(new DisplayCardView { DataContext = MultiViewDisplay(layoutId, fill: true) }));

        [Fact]
        public void DisplayCardView_MultiViewWithError_RendersWithoutBindingCrash() =>
            RunSta(() =>
            {
                var vm = MultiViewDisplay("matrix2x2", fill: false);
                vm.LastError = "Camera A is already scaled to 960x540.";
                Layout(new DisplayCardView { DataContext = vm });
            });

        [Fact]
        public void DisplayCardView_MultiViewWindows_AreLaidOutInsideTheSurface() =>
            RunSta(() =>
            {
                var view = new DisplayCardView { DataContext = MultiViewDisplay("matrix2x2", fill: false) };
                Layout(view);

                // One panel child per window, all with a real size - the fractional rects reached the panel.
                var panel = FindDescendant<Controls.FractionalLayoutPanel>(Named(view, "PreviewSurface").Single());
                Assert.NotNull(panel);
                Assert.Equal(4, panel!.Children.Count);
                foreach (System.Windows.UIElement child in panel.Children)
                {
                    var fe = (FrameworkElement)child;
                    Assert.True(fe.ActualWidth > 20 && fe.ActualHeight > 10, $"window rendered {fe.ActualWidth}x{fe.ActualHeight}");
                }
            });

        [Fact]
        public void DisplayCardView_VeryLongDeviceName_EllipsizesAndKeepsModeBadgeInsideTheCard() =>
            RunSta(() =>
            {
                var device = Rx();
                device.DeviceName = new string('W', 300);
                var vm = new DisplayCardViewModel(device);
                vm.ApplyLayout(Sdvoe.MultiView.MultiViewPresets.Build("matrix2x2", 1920, 1080), "MATRIX2X2");
                var view = new DisplayCardView { Width = 480, DataContext = vm };
                Layout(view);

                var name = (FrameworkElement)view.FindName("DeviceNameText");
                var badge = (FrameworkElement)view.FindName("ModeBadge");
                Assert.True(name.ActualWidth < 480, $"name rendered {name.ActualWidth}px wide");
                var right = badge.TranslatePoint(new Point(badge.ActualWidth, 0), view).X;
                Assert.True(badge.ActualWidth > 0, "mode badge not rendered");
                Assert.True(right <= view.ActualWidth + 0.5, $"mode badge right edge {right} exceeds card width {view.ActualWidth}");
            });

        [Fact]
        public void DisplayCardView_ExternalLayout_RendersTheNoticeAndHidesTheMisleadingText() =>
            RunSta(() =>
            {
                var vm = new DisplayCardViewModel(Rx());
                vm.SetExternalLayout("compatibility_4k_2x2", new[] { "TX2", "VEGA_1100" }, "OUT 3840x2160@60 (running)");
                var view = new DisplayCardView { DataContext = vm };

                // IsVisible needs the window to be open (ActualWidth can be stale on a collapsed element's children).
                var window = new Window { Content = view, Width = 1920, Height = 1080, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -10000 };
                window.Show();
                try
                {
                    view.UpdateLayout();
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

                    var notice = FindTextBlock(view, "Running layout 'compatibility_4k_2x2'");
                    Assert.NotNull(notice);
                    Assert.True(notice!.IsVisible, "external layout notice is not shown");

                    var routedToSource = FindTextBlock(view, "Routed to Source");
                    Assert.True(routedToSource == null || !routedToSource.IsVisible, "'Routed to Source' must be hidden for an external layout");
                    var readyText = FindTextBlock(view, "Ready to route a source");
                    Assert.True(readyText == null || !readyText.IsVisible, "unrouted placeholder must be hidden for an external layout");
                }
                finally
                {
                    window.Close();
                }
            });

        [Fact]
        public void DisplayCard_BadgeShowsFill() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                Assert.Equal("MATRIX 2X2 · 3/4", rig.Rx1.ModeBadge);
                var view = rig.View(rig.Rx1, out var host);
                Showing(host, () => AssertVisibleText(view, "MATRIX 2X2 · 3/4"));
            });

        [Fact]
        public void DisplayCard_LayoutMenu_OpensWithSevenSchematicTiles_AndHeaderButtonIsAtLeast48High() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host);
                Showing(host, () =>
                {
                    var button = (Button)Named(view, "LayoutButton").Single();
                    Assert.True(button.IsVisible && button.ActualHeight >= 48, $"Layout button is {button.ActualHeight} high");
                    Assert.True(BoundsIn(button, view).Top < 80, "the Layout button is not in the card header");

                    Click(button);
                    Assert.True(rig.Rx1.IsLayoutMenuOpen);
                    var popup = (Popup)Named(view, "LayoutMenuPopup").Single();
                    Assert.True(popup.IsOpen);
                    var menu = (FrameworkElement)popup.Child;
                    Relayout(menu);

                    var tiles = Named(menu, "LayoutMenuTile").Cast<Button>().ToList();
                    Assert.Equal(new[] { "single", "matrix2x2", "pip", "pip_large", "side_by_side", "quad_1_3", "off" },
                        tiles.Select(t => ((LayoutItemViewModel)t.DataContext).Id));
                    Assert.All(tiles, t => Assert.True(t.IsVisible && t.ActualHeight >= 56 && t.ActualWidth >= 56, $"tile {t.ActualWidth}x{t.ActualHeight}"));
                    Assert.Equal(new[] { 1, 4, 2, 2, 2, 4, 0 }, tiles.Select(t => FindAll<LayoutSchematic>(t).Single().CellCount));
                    Assert.Equal(new[] { "Single View", "Matrix 2x2", "PIP", "PIP Large", "Side-by-Side", "1+3 Quad", "Off" },
                        tiles.Select(t => FindAll<TextBlock>(t).First(tb => tb.Name == "LayoutMenuTitle").Text));

                    // The layout on screen is marked, in words (never colour only).
                    var marked = tiles.Where(t => VisibleTexts(t, "On screen").Count > 0).ToList();
                    Assert.Equal("matrix2x2", ((LayoutItemViewModel)Assert.Single(marked).DataContext).Id);

                    // A MultiView preset opens the Composer with it staged (nothing reaches the screen); the menu closes.
                    Click(tiles[2]);
                    Until(() => rig.Vm.ActiveComposer != null, "the Composer did not open");
                    Assert.Equal("pip", rig.Vm.ActiveComposer!.Draft!.Plan.Id);
                    Assert.False(rig.Rx1.IsLayoutMenuOpen);
                    Assert.True(rig.Rx1.IsMultiView); // still the Matrix on screen
                });
            });

        [Fact]
        public void DisplayCard_DropChoice_AndDraftChip_Render() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host);
                Showing(host, () =>
                {
                    var panel = Named(view, "DropChoicePanel").Single();
                    Assert.False(panel.IsVisible);

                    // A source dropped on a MultiView card asks first: the MultiView is not replaced.
                    Pump(() => view.DropOnCardAsync(new DataObject(typeof(AvasDevice), rig.Tx2.Device)));
                    Relayout(view);
                    Assert.True(rig.Rx1.IsDropChoiceOpen);
                    Assert.True(rig.Rx1.IsMultiView);
                    Assert.True(panel.IsVisible, "the drop choice is not shown");
                    AssertVisibleText(view, "Where should TX2_DUAL LINK_SFPA go?");
                    var full = (Button)Named(view, "FullScreenButton").Single();
                    var into = (Button)Named(view, "IntoBlockButton").Single();
                    Assert.True(full.ActualHeight >= 56 && into.ActualHeight >= 56, $"{full.ActualHeight} / {into.ActualHeight}");
                    Assert.Contains(FindAll<TextBlock>(full), t => t.Text.StartsWith("Full screen", StringComparison.Ordinal));
                    Assert.Contains(FindAll<TextBlock>(into), t => t.Text.StartsWith("Into a block", StringComparison.Ordinal));

                    // Into a block...: the Composer opens on what is on screen with the source armed.
                    Click(into);
                    Until(() => rig.Vm.ActiveComposer != null, "Into a block did not open the Composer");
                    Assert.Same(rig.Tx2, rig.Vm.ActiveComposer!.ArmedSource);
                    Relayout(view);
                    Assert.False(panel.IsVisible);
                    rig.Vm.CloseComposer();

                    // The draft chip: a corner chip "DRAFT · Resume" (its tooltip says "not on screen"); the stale notice
                    // reads on a line of its own outside the picture; a tap resumes.
                    Assert.False(Named(view, "DraftChip").Single().IsVisible);
                    rig.Rx1.HasDraft = true;
                    rig.Rx1.DraftNotice = MainViewModel.StaleDraftNotice;
                    Relayout(view);
                    var chip = (Button)Named(view, "DraftChip").Single();
                    Assert.True(chip.IsVisible && chip.ActualHeight >= 48, $"draft chip is {chip.ActualHeight} high");
                    AssertVisibleText(chip, "DRAFT · Resume");
                    Assert.Contains("not on screen", chip.ToolTip as string ?? string.Empty);
                    var notice = (FrameworkElement)Named(view, "DraftNoticeLine").Single();
                    Assert.True(notice.IsVisible);
                    AssertVisibleText(view, MainViewModel.StaleDraftNotice);
                    var noticeBounds = BoundsIn(notice, view);
                    Assert.False(noticeBounds.IntersectsWith(BoundsIn((FrameworkElement)Named(view, "PreviewSurface").Single(), view)), "the notice lies over the picture");
                    Click(chip);
                    Until(() => rig.Vm.ActiveComposer != null, "the draft chip did not open the Composer");
                    Assert.False(rig.Rx1.HasDraft);
                    rig.Vm.CloseComposer();

                    // Full screen (Single View): routes the source now.
                    rig.Vm.OpenDropChoice(rig.Rx1, rig.Tx2);
                    Relayout(view);
                    Click((Button)Named(view, "FullScreenButton").Single());
                    Until(() => !rig.Rx1.IsMultiView && rig.Client.LastVideoRx == CopyRig.Rx1Mac, "Full screen did not route");
                    Assert.False(rig.Rx1.IsDropChoiceOpen);
                });
            });

        [Fact]
        public void DisplayCard_CopyHandle_IsAtLeast48High_AndDisabledTextShowsWhenNothingToCopy() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var rx1 = rig.View(rig.Rx1, out var h1);
                var rx3 = rig.View(rig.Rx3, out var h3);
                var host = new StackPanel { Children = { h1, h3 } };
                Showing(host, () =>
                {
                    var handle = (Button)Named(rx1, "CopyLayoutHandle").Single();
                    Assert.True(handle.IsVisible && handle.IsEnabled && handle.ActualHeight >= 48, $"handle is {handle.ActualHeight} high");
                    Assert.Contains(FindAll<TextBlock>(handle), t => t.Text == "Copy layout");
                    Assert.Empty(VisibleTexts(rx1, "RX1_c059e_SFPA shows nothing to copy"));

                    // RX3 shows nothing: the handle stays enabled (focusable, a tap shows the reason) but looks off.
                    var blocked = (Button)Named(rx3, "CopyLayoutHandle").Single();
                    Assert.True(blocked.IsEnabled);
                    Assert.True(blocked.ActualHeight >= 48);
                    // The reason is said once, on the button's tooltip and help text (DisplayCard_CopyButton_ExplainsWhyDisabled_ByTooltipAndHelpText), not on the card.
                    Assert.Empty(VisibleTexts(rx3, "RX3_1080P_SFPA shows nothing to copy"));

                    // Tap alternative: a tap on the handle arms RX1 and every other card shows its target state; again stops.
                    Click(handle);
                    Until(() => rig.Vm.ArmedDisplay == rig.Rx1 && rig.Rx3.IsCopyTargetBlocked, "a tap did not arm RX1");
                    Assert.True(rig.Rx1.IsCopySource);
                    Relayout(host);
                    AssertVisibleText(rx1, "COPYING FROM HERE");
                    Click(handle);
                    Until(() => rig.Vm.ArmedDisplay == null, "a second tap did not stop");
                    Relayout(host);
                    Assert.Empty(VisibleTexts(rx1, "COPYING FROM HERE"));
                });
            });

        [Fact]
        public void DisplayCard_CopyHandleDrag_ArmsForTheDrag_ShowsTheGhost_DropsOnTheTarget_AndDisarmsAfter() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var source = rig.View(rig.Rx1, out var h1);
                var target = rig.View(rig.Pax, out var h2);
                Showing(new StackPanel { Children = { h1, h2 } }, () =>
                {
                    bool ghostShown = false, ghostHitTestable = true, ghostCoversTarget = false, targetUnderPointer = false, armedDuringDrag = false;
                    Point ghostAt = default;
                    UIElement? ghostRoot = null;
                    var overTarget = DragDropEffects.None;
                    var allowedEffects = DragDropEffects.None;
                    source.IsPointerDown = () => true;
                    source.DragStarter = (from, data, allowed) =>
                    {
                        allowedEffects = allowed;
                        armedDuringDrag = ReferenceEquals(rig.Vm.ArmedDisplay, rig.Rx1) && rig.Pax.IsCopyTargetAllowed;

                        // The ghost is an adorner in the window's own layer (no window of its own), and it is not hit-testable.
                        var ghost = source.Ghost!;
                        ghostRoot = source.GhostRoot!;
                        var layer = AdornerLayer.GetAdornerLayer(ghostRoot)!;
                        ghostShown = layer.GetAdorners(ghostRoot)?.Contains(ghost) == true;
                        ghostHitTestable = ghost.IsHitTestVisible;

                        // Put the pointer on the target with the ghost lying right over that point: the target, never
                        // the ghost, is what the pointer is over (a ghost window here would take the drop).
                        var window = Window.GetWindow(source)!;
                        var pointer = target.TranslatePoint(new Point(target.ActualWidth / 2, target.ActualHeight / 2), ghostRoot);
                        source.PlaceGhostAt(new Point(pointer.X - 40, pointer.Y - 40));
                        ghostRoot.UpdateLayout();
                        layer.UpdateLayout();
                        ghostAt = ghost.Position;
                        ghostCoversTarget = ghost.Bounds.Contains(pointer);
                        var hit = window.InputHitTest(ghostRoot.TranslatePoint(pointer, window)) as DependencyObject;
                        for (var d = hit; d != null; d = VisualTreeHelper.GetParent(d))
                        {
                            if (ReferenceEquals(d, target)) targetUnderPointer = true;
                        }

                        overTarget = target.DragOverCard(data);
                        Pump(() => target.DropOnCardAsync(data));
                        return DragDropEffects.Copy;
                    };

                    Pump(() => source.StartCopyDragAsync((UIElement)Named(source, "CopyLayoutHandle").Single()));

                    Assert.Equal(DragDropEffects.Copy, allowedEffects);
                    Assert.True(armedDuringDrag, "the targets were not shown while dragging");
                    Assert.True(ghostShown, "no ghost of the source's picture followed the drag");
                    Assert.False(ghostHitTestable, "the ghost can be hit: it could become the drop target");
                    Assert.True(ghostCoversTarget, $"the ghost at {ghostAt} does not cover the probe point");
                    Assert.True(targetUnderPointer, "the ghost, not the target card, is under the pointer");
                    Assert.Equal(DragDropEffects.Copy, overTarget);
                    Assert.True(rig.Pax.IsMultiView, "the drop did not copy");
                    Assert.True(rig.Pax.CanUndoScreen);
                    Assert.Null(rig.Vm.ArmedDisplay); // disarmed after the drag, whatever happened
                    Assert.Null(source.Ghost);
                    var after = AdornerLayer.GetAdornerLayer(ghostRoot!)!.GetAdorners(ghostRoot!);
                    Assert.True(after == null || !after.OfType<CopyGhostAdorner>().Any(), "the ghost was left in the adorner layer");
                    Assert.False(rig.Pax.IsCopyTargetAllowed);
                });
            });

        [Fact]
        public void DisplayCard_CopyTargetAllowed_ShowsSummary_Blocked_ShowsLockAndReason() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var pax = rig.View(rig.Pax, out var h1);
                var rx3 = rig.View(rig.Rx3, out var h2);
                var host = new StackPanel { Children = { h1, h2 } };
                Showing(host, () =>
                {
                    Assert.False(Named(pax, "CopyTargetRing").Single().IsVisible);
                    Pump(() => rig.Vm.ArmDisplayAsync(rig.Rx1));
                    Relayout(host);

                    // Allowed: cyan ring + "Release to copy here" + the view model's summary of what is copied.
                    Assert.True(rig.Pax.IsCopyTargetAllowed);
                    Assert.True(Named(pax, "CopyTargetRing").Single().IsVisible);
                    AssertVisibleText(pax, "Release to copy here");
                    Assert.False(string.IsNullOrEmpty(rig.Pax.CopyTargetText));
                    AssertVisibleText(pax, rig.Pax.CopyTargetText);
                    Assert.False(Named(pax, "CopyTargetLock").Single().IsVisible);

                    // Blocked: lock + the reason, no ring.
                    Assert.True(rig.Rx3.IsCopyTargetBlocked);
                    Assert.Contains("1920x1080", rig.Rx3.CopyTargetText);
                    Assert.True(Named(rx3, "CopyTargetLock").Single().IsVisible);
                    Assert.False(Named(rx3, "CopyTargetRing").Single().IsVisible);
                    AssertVisibleText(rx3, rig.Rx3.CopyTargetText);
                    Assert.Empty(VisibleTexts(rx3, "Release to copy here"));

                    // Mouse users: the cursor (effects) and a tooltip repeat it, both from CopyDragFeedback.
                    var data = new DataObject(typeof(DisplayCardViewModel), rig.Rx1);
                    Assert.Equal(DragDropEffects.Copy, pax.DragOverCard(data));
                    Assert.Equal(CopyDragFeedback.Tooltip(rig.Vm.CopyDecisionFor(rig.Pax)), pax.CopyTipText);
                    Assert.Equal(DragDropEffects.None, rx3.DragOverCard(data));
                    Assert.Equal(rig.Rx3.CopyTargetText, rx3.CopyTipText);
                    rx3.DragLeaveCard();
                    Assert.Null(rx3.CopyTipText);

                    // A release on the blocked card sends nothing.
                    rig.Client.MultiViewCalls.Clear();
                    Pump(() => rx3.DropOnCardAsync(data));
                    Assert.Empty(rig.Client.MultiViewCalls);
                    Assert.False(rig.Rx3.IsMultiView);
                    Assert.Null(rx3.CopyTipText);
                });
            });

        [Fact]
        public void DisplayCard_Tap_GoesThroughTapDisplay_CopiesWhileArmed_AndRoutesAnArmedSource() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var pax = rig.View(rig.Pax, out var h1);
                var rx3 = rig.View(rig.Rx3, out var h2);
                Showing(new StackPanel { Children = { h1, h2 } }, () =>
                {
                    // Copy armed: a tap on a blocked card changes nothing; a tap on an allowed card copies now.
                    Pump(() => rig.Vm.ArmDisplayAsync(rig.Rx1));
                    TapCard(rx3);
                    Idle();
                    Assert.False(rig.Rx3.IsMultiView);
                    Assert.Same(rig.Rx1, rig.Vm.ArmedDisplay);
                    TapCard(pax);
                    Until(() => rig.Pax.IsMultiView, "a tap on the allowed card did not copy");
                    Assert.Null(rig.Vm.ArmedDisplay);

                    // A source armed: a tap on an unrouted card routes it now (Single View stays immediate).
                    rig.Vm.ArmSource(rig.Tx2);
                    TapCard(rx3);
                    Until(() => rig.Client.LastVideoRx == CopyRig.Rx3Mac, "the tap did not route the armed source");

                    // A tap on one of the card's own buttons is the button's, not a tap on the card.
                    rig.Vm.ArmSource(rig.Vega);
                    rig.Client.LastVideoRx = null;
                    TapCard(rx3, releasedOn: (UIElement)Named(rx3, "LayoutButton").Single());
                    Idle();
                    Assert.Null(rig.Client.LastVideoRx);
                    Assert.Same(rig.Vega, rig.Vm.ArmedSource);

                    // A bubbling release without its own press and release on the card is no tap.
                    Tap(rx3, UIElement.MouseLeftButtonUpEvent);
                    Idle();
                    Assert.Null(rig.Client.LastVideoRx);
                });
            });

        [Fact]
        public void DisplayCard_UndoToast_Renders() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var pax = rig.View(rig.Pax, out var host);
                Showing(host, () =>
                {
                    var toast = Named(pax, "UndoToast").Single();
                    Assert.False(toast.IsVisible);
                    Pump(() => rig.Vm.ArmDisplayAsync(rig.Rx1));
                    Pump(() => rig.Vm.CopyDisplayAsync(rig.Rx1, rig.Pax));
                    rig.Vm.DisarmDisplay();
                    Relayout(pax);

                    Assert.True(rig.Pax.CanUndoScreen);
                    Assert.True(toast.IsVisible, "the Undo toast is not shown");
                    AssertVisibleText(pax, rig.Pax.UndoToastText!);
                    var undo = (Button)Named(pax, "UndoScreenButton").Single();
                    Assert.True(undo.IsVisible && undo.ActualHeight >= 56, $"Undo is {undo.ActualHeight} high");

                    Click(undo);
                    Until(() => !rig.Pax.IsMultiView, "Undo did not put back what PAX showed");
                    Relayout(pax);
                    Assert.False(toast.IsVisible);
                });
            });

        [Fact]
        public void DisplayCard_LayoutTileAndSourceDrops_KeepTheirBehaviour() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var rx1 = rig.View(rig.Rx1, out var h1);
                var rx3 = rig.View(rig.Rx3, out var h2);
                Showing(new StackPanel { Children = { h1, h2 } }, () =>
                {
                    LayoutItemViewModel Tile(string id) => rig.Vm.Layouts.Single(l => l.Id == id);
                    IDataObject Data(object o) => new DataObject(o.GetType(), o);

                    // A source on an unrouted card routes it now; on a MultiView card it asks (the drop choice).
                    Assert.Equal(DragDropEffects.Link, rx3.DragOverCard(Data(rig.Tx2.Device)));
                    Pump(() => rx3.DropOnCardAsync(Data(rig.Tx2.Device)));
                    Assert.Equal(CopyRig.Rx3Mac, rig.Client.LastVideoRx);
                    Pump(() => rx1.DropOnCardAsync(Data(rig.Tx2.Device)));
                    Assert.True(rig.Rx1.IsDropChoiceOpen);
                    Assert.True(rig.Rx1.IsMultiView);

                    // The bottom bar's Off turns the display off; its Single View leaves MultiView.
                    Assert.Equal(DragDropEffects.Link, rx3.DragOverCard(Data(Tile("off"))));
                    Pump(() => rx3.DropOnCardAsync(Data(Tile("off"))));
                    Assert.Equal(CopyRig.Rx3Mac, rig.Client.LastUnroutedRx);
                    Pump(() => rx1.DropOnCardAsync(Data(Tile("single"))));
                    Assert.False(rig.Rx1.IsMultiView);

                    // A MultiView tile opens the Composer with it staged.
                    Pump(() => rx3.DropOnCardAsync(Data(Tile("matrix2x2"))));
                    Assert.Equal("matrix2x2", rig.Vm.ActiveComposer?.Draft?.Plan.Id);
                });
            });

        [Theory]
        [InlineData(1008, 615)]  // MainWindow minimum 1024x720: its content area
        [InlineData(1264, 755)]  // MainWindow default 1280x860
        [InlineData(1920, 1014)] // a maximised 1080p window
        public void DisplayCard_InTheOverview_AtRealWindowSizes_NothingInTheCardClips(int width, int height) =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                Pump(() => rig.Vm.ArmDisplayAsync(rig.Rx1));
                Pump(() => rig.Vm.CopyDisplayAsync(rig.Rx1, rig.Pax)); // PAX shows the copy and its Undo toast
                rig.Vm.DisarmDisplay();
                Pump(() => rig.Vm.ArmDisplayAsync(rig.Rx1));          // armed again: PAX allowed, RX3 blocked with its reason
                rig.Vm.OpenDropChoice(rig.Rx1, rig.Tx2);
                rig.Rx1.HasDraft = true;
                rig.Rx1.DraftNotice = MainViewModel.StaleDraftNotice;
                var view = new VisualRoutingView { DataContext = rig.Vm };

                ShowingAt(view, width, height, () =>
                {
                    var cards = FindAll<DisplayCardView>(view).Where(c => c.IsVisible).ToList();
                    Assert.Equal(3, cards.Count);
                    foreach (var card in cards)
                    {
                        var frame = (FrameworkElement)Named(card, "CardFrame").Single();
                        // The name may ellipsize (full text is its tooltip) but must stay in the card, as must the mode badge.
                        AssertInside((FrameworkElement)Named(card, "DeviceNameText").Single(), frame);
                        AssertInside((FrameworkElement)Named(card, "ModeBadge").Single(), frame);
                        AssertTextFits((TextBlock)Named(card, "ModeBadgeText").Single());
                        foreach (var name in new[] { "LayoutButton", "CopyLayoutHandle" })
                        {
                            var b = (Button)Named(card, name).Single();
                            Assert.True(b.ActualHeight >= 48, $"{name} is {b.ActualHeight} high");
                            AssertInside(b, frame);
                            foreach (var t in FindAll<TextBlock>(b).Where(x => x.IsVisible)) AssertTextFits(t);
                        }
                    }

                    // RX1: the copy source, with a draft and the drop choice open over the card body.
                    var rx1 = cards.Single(c => c.DataContext == rig.Rx1);
                    var rx1Frame = (FrameworkElement)Named(rx1, "CardFrame").Single();
                    var choice = (FrameworkElement)Named(rx1, "DropChoicePanel").Single();
                    Assert.True(choice.IsVisible);
                    AssertInside(choice, rx1Frame);
                    foreach (var name in new[] { "FullScreenButton", "IntoBlockButton" })
                    {
                        var b = (Button)Named(rx1, name).Single();
                        Assert.True(b.IsVisible && b.ActualHeight >= 48);
                        AssertInside(b, choice);
                        foreach (var t in FindAll<TextBlock>(b).Where(x => x.IsVisible)) AssertTextFits(t);
                    }
                    AssertTextFits((TextBlock)Named(rx1, "DropChoiceTitle").Single());
                    var chip = (FrameworkElement)Named(rx1, "DraftChip").Single();
                    Assert.True(chip.IsVisible && chip.ActualHeight >= 48);
                    AssertInside(chip, rx1Frame);
                    AssertVisibleText(rx1, "COPYING FROM HERE");

                    // PAX: an allowed target; the Undo toast of the copy before steps aside while it is a target.
                    var pax = cards.Single(c => c.DataContext == rig.Pax);
                    var paxPreview = (FrameworkElement)Named(pax, "PreviewSurface").Single();
                    Assert.True(paxPreview.ActualHeight >= 120, $"PAX's picture is only {paxPreview.ActualHeight} high");
                    var toast = (FrameworkElement)Named(pax, "UndoToast").Single();
                    Assert.True(rig.Pax.CanUndoScreen);
                    Assert.False(toast.IsVisible, "the Undo toast lies over PAX's copy-target words");
                    AssertVisibleText(pax, "Release to copy here");

                    var rx3 = cards.Single(c => c.DataContext == rig.Rx3);
                    Assert.True(Named(rx3, "CopyTargetLock").Single().IsVisible);
                    var rx3Handle = (Button)Named(rx3, "CopyLayoutHandle").Single();
                    Assert.Equal("No layout to copy yet", (string?)rx3Handle.ToolTip);
                    Assert.Equal(rig.Rx3.CopyFromBlockedText, AutomationProperties.GetHelpText(rx3Handle));

                    // Copying / choosing: the overview has no layout bar any more; the first row's Layout and Copy layout
                    // buttons are in view without scrolling (they sit at the top of the card).
                    var scroll = (ScrollViewer)Named(view, "DisplaysScroll").Single();
                    // Round 3: the row is one row that pans sideways (with a vertical safety net on short windows) (touch drag, drag threshold keeps a tap a tap).
                    Assert.Equal(PanningMode.Both, scroll.PanningMode);
                    foreach (var card in cards.Where(c => BoundsIn(c, scroll).Top < 1))
                    {
                        foreach (var name in new[] { "LayoutButton", "CopyLayoutHandle" })
                        {
                            var r = BoundsIn((FrameworkElement)Named(card, name).Single(), scroll);
                            Assert.True(r.Bottom <= scroll.ViewportHeight + 0.5, $"{name} ends at {r.Bottom:F0}, the displays show {scroll.ViewportHeight:F0} px");
                        }
                    }

                    // Every card's picture keeps 16:9 - unrouted (RX3), MultiView (PAX), MultiView with a draft chip (RX1):
                    // the card panel's chrome matches the card in every state.
                    foreach (var card in cards)
                    {
                        var preview = (FrameworkElement)Named(card, "PreviewSurface").Single();
                        double aspect = preview.ActualWidth / preview.ActualHeight;
                        Assert.True(aspect >= 16.0 / 9 * 0.96 && aspect <= 16.0 / 9 * 1.04,
                            $"{((DisplayCardViewModel)card.DataContext).DeviceName}: picture {preview.ActualWidth:F1}x{preview.ActualHeight:F1} ({aspect:F3})");
                    }

                    // Back to the plain overview.
                    rig.Rx1.CloseDropChoice();
                    rig.Vm.DisarmDisplay();
                    Relayout(view);
                    Assert.False(rig.Vm.IsCopyOrChoiceActive);

                    // ... and PAX's Undo toast is back, inside its picture, whole.
                    Assert.True(toast.IsVisible, "the Undo toast did not come back");
                    AssertInside(toast, (FrameworkElement)Named(pax, "CardFrame").Single());
                    var tb = BoundsIn(toast, view);
                    var pb = BoundsIn(paxPreview, view);
                    Assert.False(tb.IntersectsWith(pb), $"the toast {tb} overlaps the picture {pb}");
                    AssertTextFits((TextBlock)Named(pax, "UndoToastLabel").Single());
                    AssertInside((FrameworkElement)Named(pax, "UndoScreenButton").Single(), toast);
                });
            });

        [Theory]
        [InlineData(1264, 755)]
        [InlineData(1008, 615)]
        public void DisplayCard_CopyAndChoiceStates_ReadInsideTheDisplaysView_AtRealWindowSizes(int width, int height) =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                Pump(() => rig.Vm.ArmDisplayAsync(rig.Rx1));
                Pump(() => rig.Vm.CopyDisplayAsync(rig.Rx1, rig.Pax)); // PAX shows the copy and its Undo toast
                rig.Vm.DisarmDisplay();
                var view = new VisualRoutingView { DataContext = rig.Vm };

                ShowingAt(view, width, height, () =>
                {
                    var scroll = (ScrollViewer)Named(view, "DisplaysScroll").Single();
                    DisplayCardView CardOf(DisplayCardViewModel d) => FindAll<DisplayCardView>(view).Single(c => c.DataContext == d);
                    FrameworkElement Part(DisplayCardView card, string name) => (FrameworkElement)Named(card, name).Single();

                    // Copy armed (with the banner): the full source cards stay in place; there is no layout bar.
                    Pump(() => rig.Vm.ArmDisplayAsync(rig.Rx1));
                    Relayout(view);
                    AssertVisibleText(view, "Copying RX1_c059e_SFPA - ");
                    Assert.False(Named(view, "CompactSourcePalette").Single().IsVisible);
                    Assert.NotEmpty(FindAll<SourceCardView>(view).Where(s => s.IsVisible));

                    // The first target was scrolled to by itself: its words read without touching anything. Its Undo
                    // toast steps aside while it is a target (the target state wins; nothing lies over its words).
                    var pax = CardOf(rig.Pax);
                    AssertReadable(Part(pax, "CopyTargetAllowedContent"), scroll, "PAX: Release to copy here + the summary");
                    Assert.False(Part(pax, "UndoToast").IsVisible, "the Undo toast lies over PAX's copy-target words");

                    // Every target reads when it is the one scrolled to.
                    var rx3 = CardOf(rig.Rx3);
                    rx3.RevealState();
                    Relayout(view);
                    AssertReadable(Part(rx3, "CopyTargetBlockedContent"), scroll, "RX3: the lock and the blocked reason");
                    pax.RevealState();
                    Relayout(view);
                    AssertReadable(Part(pax, "CopyTargetAllowedContent"), scroll, "PAX: Release to copy here + the summary");

                    // Copy stopped: PAX's Undo toast is back (its 10 s are not over) and reads.
                    rig.Vm.DisarmDisplay();
                    Relayout(view);
                    pax.RevealState();
                    Relayout(view);
                    AssertReadable(Part(pax, "UndoToast"), scroll, "PAX: the Undo toast");

                    // A source armed and tapped onto the MultiView card: the drop choice reads by itself.
                    rig.Vm.ArmSource(rig.Tx2);
                    Relayout(view);
                    Pump(() => rig.Vm.TapDisplayAsync(rig.Rx1));
                    Relayout(view);
                    Assert.True(rig.Rx1.IsDropChoiceOpen);
                    AssertVisibleText(view, "Placing TX2_DUAL LINK_SFPA - ");
                    var rx1 = CardOf(rig.Rx1);
                    AssertReadable(Part(rx1, "DropChoiceTitle"), scroll, "RX1: the drop choice question");
                    AssertReadable(Part(rx1, "FullScreenButton"), scroll, "RX1: Full screen");
                    AssertReadable(Part(rx1, "IntoBlockButton"), scroll, "RX1: Into a block");
                });
            });

        [Fact]
        public void DisplayCard_LayoutMenu_OnAnUnroutedDisplay_MarksNoTile_ARoutedOneMarksSingleView() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx3, out var host);
                Showing(host, () =>
                {
                    Click((Button)Named(view, "LayoutButton").Single());
                    var menu = (FrameworkElement)((Popup)Named(view, "LayoutMenuPopup").Single()).Child;
                    Relayout(menu);
                    var accent = ((SolidColorBrush)Application.Current.FindResource("Brush.Accent.Primary")).Color;
                    Color Edge(Button tile) => ((SolidColorBrush)((Border)tile.Template.FindName("bd", tile)).BorderBrush).Color;
                    var tiles = Named(menu, "LayoutMenuTile").Cast<Button>().ToList();

                    Assert.All(tiles, t => Assert.NotEqual(accent, Edge(t)));
                    Assert.All(tiles, t => Assert.Empty(VisibleTexts(t, "✓")));

                    // Routed in Single View: the Single View tile is marked (highlight and words).
                    Pump(() => rig.Vm.RouteVideoAsync(rig.Tx2.Device, rig.Rx3.Device));
                    Relayout(menu);
                    var single = tiles.Single(t => ((LayoutItemViewModel)t.DataContext).Id == "single");
                    Assert.Equal(accent, Edge(single));
                    AssertVisibleText(single, "On screen");
                    Assert.All(tiles.Where(t => t != single), t => Assert.NotEqual(accent, Edge(t)));
                });
            });

        [Fact]
        public void DisplayCard_LayoutButton_APressThatClosesTheMenu_DoesNotReopenIt_HoweverSlowTheRelease() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host);
                Showing(host, () =>
                {
                    var button = (Button)Named(view, "LayoutButton").Single();
                    var popup = (Popup)Named(view, "LayoutMenuPopup").Single();
                    void Press(int timestamp) =>
                        button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, timestamp, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent, Source = button });
                    void ReleaseAndClick()
                    {
                        button.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent, Source = button });
                        Click(button);
                    }

                    // 1. The press reaches the button while the menu is open (the press closes it); a gloved release 0.6 s later.
                    Click(button);
                    Assert.True(popup.IsOpen);
                    Press(Environment.TickCount);
                    popup.IsOpen = false;
                    Wait(TimeSpan.FromMilliseconds(600));
                    ReleaseAndClick();
                    Assert.False(rig.Rx1.IsLayoutMenuOpen, "the slow press that closed the menu opened it again");
                    Idle();

                    // 2. The popup takes the press first and closes; the button sees that press after the close.
                    Click(button);
                    Assert.True(popup.IsOpen);
                    int down = Environment.TickCount;
                    Wait(TimeSpan.FromMilliseconds(20));
                    popup.IsOpen = false;
                    Assert.True(unchecked(view.MenuClosedTick - down) >= 0);
                    Press(down);
                    Wait(TimeSpan.FromMilliseconds(600));
                    ReleaseAndClick();
                    Assert.False(rig.Rx1.IsLayoutMenuOpen, "the press that closed the menu (popup first) opened it again");
                    Idle();

                    // 3. A new press after the menu closed opens it.
                    Wait(TimeSpan.FromMilliseconds(20));
                    Press(Environment.TickCount);
                    ReleaseAndClick();
                    Assert.True(rig.Rx1.IsLayoutMenuOpen);
                });
            });

        [Fact]
        public void DisplayCard_TapOnTheMultiViewPicture_OpensTheComposer_AndEnterOnItToo() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var rx1 = rig.View(rig.Rx1, out var h1);
                var pax = rig.View(rig.Pax, out var h2);
                Showing(new StackPanel { Children = { h1, h2 } }, () =>
                {
                    var picture = (UIElement)Named(rx1, "PreviewSurface").Single();

                    // An unrouted card's picture, nothing armed: nothing happens.
                    TapCard(pax, releasedOn: (UIElement)Named(pax, "PreviewSurface").Single());
                    Idle();
                    Assert.Null(rig.Vm.ActiveComposer);

                    // Beside the picture (the name) is a plain card tap: nothing armed, nothing happens.
                    TapCard(rx1, releasedOn: (UIElement)Named(rx1, "DeviceNameText").Single());
                    Idle();
                    Assert.Null(rig.Vm.ActiveComposer);

                    // The MultiView picture: the Composer opens on what is on screen.
                    TapCard(rx1, releasedOn: picture);
                    Until(() => rig.Vm.ActiveComposer != null, "a tap on the MultiView picture did not open the Composer");
                    Assert.Equal("matrix2x2", rig.Vm.ActiveComposer!.Draft!.Plan.Id);
                    rig.Vm.CloseComposer();

                    // Enter on the focused picture does the same.
                    Assert.True(picture.Focusable);
                    // Keyboard focus in the STA window: the style the focus adorner would use resolves to the theme ring.
                    // (An adorner cannot be rendered here, so this is a style-resolution check, not a pixel check.)
                    Assert.True(picture.Focus(), "the picture did not take focus");
                    Assert.True(picture.IsKeyboardFocused, "the picture is not keyboard focused");
                    picture.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(picture)!, 0, Key.Enter) { RoutedEvent = UIElement.KeyDownEvent });
                    Until(() => rig.Vm.ActiveComposer != null, "Enter on the picture did not open the Composer");
                    rig.Vm.CloseComposer();

                    // While a copy is armed the picture tap is a copy-target tap, never the Composer.
                    Pump(() => rig.Vm.ArmDisplayAsync(rig.Rx1));
                    TapCard(rx1, releasedOn: picture);
                    Idle();
                    Assert.Null(rig.Vm.ActiveComposer);
                });
            });

        [Theory]
        [InlineData(1264, 755)]
        [InlineData(1008, 615)]
        public void DisplayCard_DropChoiceOnACardWithAnUndoToast_TheChoiceWins_NothingLiesOverIt(int width, int height) =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                Pump(() => rig.Vm.ArmDisplayAsync(rig.Rx1));
                Pump(() => rig.Vm.CopyDisplayAsync(rig.Rx1, rig.Pax)); // PAX: a MultiView now, with its 10 s Undo toast
                rig.Vm.DisarmDisplay();
                var view = new VisualRoutingView { DataContext = rig.Vm };

                ShowingAt(view, width, height, () =>
                {
                    var pax = FindAll<DisplayCardView>(view).Single(c => c.DataContext == rig.Pax);
                    var undo = (FrameworkElement)Named(pax, "UndoScreenButton").Single();
                    Assert.True(undo.IsVisible);

                    // A source armed and tapped onto PAX (a MultiView): the drop choice opens on the card with the toast.
                    rig.Vm.ArmSource(rig.Tx2);
                    Pump(() => rig.Vm.TapDisplayAsync(rig.Pax));
                    Relayout(view);
                    Assert.True(rig.Pax.IsDropChoiceOpen);
                    Assert.True(rig.Pax.CanUndoScreen);

                    foreach (var name in new[] { "FullScreenButton", "IntoBlockButton" })
                    {
                        var choice = (FrameworkElement)Named(pax, name).Single();
                        Assert.True(choice.IsVisible);
                        Assert.True(!undo.IsVisible || !BoundsIn(undo, pax).IntersectsWith(BoundsIn(choice, pax)), $"Undo lies over {name}");
                        foreach (var t in FindAll<TextBlock>(choice)) AssertTextFits(t);
                        // The lower half of the choice is the choice's, never the toast's Undo.
                        Assert.True(HitsInside(choice, new Point(choice.ActualWidth / 2, choice.ActualHeight * 0.8), choice), $"the lower half of {name} is not {name}");
                        Assert.True(HitsInside(choice, new Point(choice.ActualWidth / 2, choice.ActualHeight * 0.2), choice), $"the upper half of {name} is not {name}");
                    }

                    // The choice gone (its 10 s, or chosen): the toast is back while its Undo window lasts.
                    rig.Pax.CloseDropChoice();
                    Relayout(view);
                    Assert.True(undo.IsVisible, "the Undo toast did not come back after the drop choice");
                });
            });

        [Theory]
        [InlineData(1264, 755)]
        [InlineData(1008, 615)]
        [InlineData(1920, 1014)]
        public void DisplayCard_DraftChip_LiesInTheHeader_NeverOnThePicture_NorOnTheErrorBanner(int width, int height) =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                rig.Rx1.HasDraft = true; // RX1 runs a Matrix 2x2
                var view = new VisualRoutingView { DataContext = rig.Vm };

                ShowingAt(view, width, height, () =>
                {
                    var rx1 = FindAll<DisplayCardView>(view).Single(c => c.DataContext == rig.Rx1);
                    var frame = (FrameworkElement)Named(rx1, "CardFrame").Single();
                    var picture = (FrameworkElement)Named(rx1, "PreviewSurface").Single();
                    var chip = (Button)Named(rx1, "DraftChip").Single();
                    Assert.True(chip.IsVisible && chip.ActualHeight >= 48, $"draft chip is {chip.ActualHeight} high");
                    Assert.Contains("not on screen", chip.ToolTip as string ?? string.Empty);
                    Assert.Contains("not on screen", System.Windows.Automation.AutomationProperties.GetName(chip));
                    var chipText = FindAll<TextBlock>(chip).Single(t => t.Text.StartsWith("DRAFT", StringComparison.Ordinal));
                    Assert.Equal("DRAFT \u00B7 Resume", chipText.Text);
                    AssertTextFits(chipText);

                    // The chip is touch-friendly and inside the card frame.
                    AssertInside(chip, frame);

                    // The header holds everything: name, badge, Layout, Copy layout and the chip, inside the card, none over another.
                    var parts = new[] { "DeviceNameText", "ModeBadgeText", "LayoutButton", "CopyLayoutHandle", "DraftChip" }
                        .Select(n => (Name: n, El: (FrameworkElement)Named(rx1, n).Single())).ToList();
                    foreach (var (name, el) in parts)
                    {
                        Assert.True(el.IsVisible, $"{name} is not visible");
                        AssertInside(el, frame);
                    }
                    // The name shows whole except on the narrowest card, where the rename pencil (a 48 px hit box) takes its room: it ellipsizes there (its tooltip is the full name).
                    if (rx1.ActualWidth > Token("Size.Card.Display.Min") + 10) AssertTextFits((TextBlock)parts[0].El);
                    AssertTextFits((TextBlock)parts[1].El);
                    AssertTextFits(FindAll<TextBlock>(parts[3].El).Single(t => t.Text.StartsWith("Copy", StringComparison.Ordinal)));
                    for (int i = 2; i < parts.Count; i++)
                    {
                        for (int j = i + 1; j < parts.Count; j++)
                        {
                            Assert.False(BoundsIn(parts[i].El, rx1).IntersectsWith(BoundsIn(parts[j].El, rx1)), $"{parts[i].Name} and {parts[j].Name} overlap");
                        }
                        Assert.True(parts[i].El.ActualHeight >= 48, $"{parts[i].Name} is {parts[i].El.ActualHeight} high");
                    }
                    AssertSixteenByNine(picture, "draft chip");

                    // A card error with the draft: the whole message reads, nothing lies over the banner.
                    rig.Rx1.LastError = "The decoder did not answer in time; the layout was not changed.";
                    Relayout(view);
                    var banner = (FrameworkElement)Named(rx1, "ErrorBanner").Single();
                    var bannerText = (TextBlock)Named(rx1, "ErrorBannerText").Single();
                    Assert.True(banner.IsVisible);
                    AssertTextFits(bannerText);
                    AssertInside(banner, picture);
                    Assert.False(BoundsIn(chip, rx1).IntersectsWith(BoundsIn(banner, rx1)), "the draft chip lies over the error banner");
                    AssertVisibleText(rx1, "The decoder did not answer in time; the layout was not changed.");
                    AssertSixteenByNine(picture, "draft chip and error");

                    // With the stale notice: one line outside the picture, the 16:9 picture unchanged.
                    rig.Rx1.DraftNotice = MainViewModel.StaleDraftNotice;
                    Relayout(view);
                    var notice = (TextBlock)Named(rx1, "DraftNoticeLine").Single();
                    Assert.True(notice.IsVisible);
                    Assert.False(BoundsIn(notice, rx1).IntersectsWith(BoundsIn(picture, rx1)));
                    Assert.True(notice.ActualHeight < 20, $"the notice is {notice.ActualHeight} high (one line)");
                    AssertSixteenByNine(picture, "draft chip with the notice");

                    // The chip keeps its click: it opens the Composer with the draft.
                    Click(chip);
                    Until(() => rig.Vm.ActiveComposer != null, "the draft chip did not open the Composer");
                });
            });

        [Theory]
        [InlineData(1264, 755)]
        [InlineData(1008, 615)]
        public void DisplayCard_DraftChip_BesideTheUndoToast_BothShow_NoneOverThePicture(int width, int height) =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                Pump(() => rig.Vm.ArmDisplayAsync(rig.Rx1));
                Pump(() => rig.Vm.CopyDisplayAsync(rig.Rx1, rig.Pax)); // PAX: a MultiView now, with its Undo toast
                rig.Vm.DisarmDisplay();
                rig.Pax.HasDraft = true;
                var view = new VisualRoutingView { DataContext = rig.Vm };

                ShowingAt(view, width, height, () =>
                {
                    var pax = FindAll<DisplayCardView>(view).Single(c => c.DataContext == rig.Pax);
                    var picture = (FrameworkElement)Named(pax, "PreviewSurface").Single();
                    var chip = (FrameworkElement)Named(pax, "DraftChip").Single();
                    var toast = (FrameworkElement)Named(pax, "UndoToast").Single();
                    Assert.True(chip.IsVisible && toast.IsVisible, "the chip and the toast do not both show");
                    Assert.True(chip.ActualHeight >= 48);
                    AssertInside(chip, (FrameworkElement)Named(pax, "CardFrame").Single());
                    Assert.False(BoundsIn(chip, pax).IntersectsWith(BoundsIn(toast, pax)), "the draft chip at top does not intersect the undo toast at bottom");
                    AssertInside((FrameworkElement)Named(pax, "UndoScreenButton").Single(), toast);
                    AssertSixteenByNine(picture, "chip beside the toast");
                });
            });

        [Theory]
        [InlineData(1264, 755)]
        [InlineData(1008, 615)]
        [InlineData(1920, 1014)]
        public void DisplayCard_CopySourceTag_LiesOutsideThePicture(int width, int height) =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                Pump(() => rig.Vm.ArmDisplayAsync(rig.Rx1)); // RX1 (Matrix 2x2) is the copy source
                var view = new VisualRoutingView { DataContext = rig.Vm };

                ShowingAt(view, width, height, () =>
                {
                    var rx1 = FindAll<DisplayCardView>(view).Single(c => c.DataContext == rig.Rx1);
                    var picture = (FrameworkElement)Named(rx1, "PreviewSurface").Single();
                    var tag = (FrameworkElement)Named(rx1, "CopySourceChip").Single();
                    Assert.True(tag.IsVisible, "the copy tag does not show");
                    AssertVisibleText(rx1, "COPYING FROM HERE");
                    // Whole, except on the narrowest card: there the rename pencil (a 48 px hit box) takes the room and the tag ellipsizes ("COPYING..."),
                    // so that the mode badge is never pushed out of the card; the arm banner above the displays says the rest.
                    var tagText = FindAll<TextBlock>(tag).Single();
                    if (rx1.ActualWidth > Token("Size.Card.Display.Min") + 10) AssertTextFits(tagText);
                    else Assert.True(tagText.ActualWidth >= 40, $"the copy tag is only {tagText.ActualWidth:F1} px wide at the minimum width");
                    AssertInside(tag, (FrameworkElement)Named(rx1, "CardFrame").Single());
                    Assert.False(BoundsIn(tag, rx1).IntersectsWith(BoundsIn(picture, rx1)), "the copy tag lies on the picture");
                    AssertSixteenByNine(picture, "copy source");

                    // With a draft too: the tag, the chip and the picture stay apart.
                    rig.Rx1.HasDraft = true;
                    rig.Rx1.DraftNotice = MainViewModel.StaleDraftNotice;
                    Relayout(view);
                    var chip = (FrameworkElement)Named(rx1, "DraftChip").Single();
                    var notice = (FrameworkElement)Named(rx1, "DraftNoticeLine").Single();
                    Assert.True(tag.IsVisible && chip.IsVisible);
                    Assert.False(notice.IsVisible, "the stale notice lies under the copy tag");
                    Assert.False(BoundsIn(tag, rx1).IntersectsWith(BoundsIn(picture, rx1)));
                    AssertInside(chip, (FrameworkElement)Named(rx1, "CardFrame").Single());
                    AssertSixteenByNine(picture, "copy source with a draft");
                });
            });
        [Fact]
        public void DisplayCard_Picture_HasThe2pxThemeFocusRing() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host);
                Showing(host, () =>
                {
                    KeyboardMode();
                    var picture = (FrameworkElement)Named(view, "PreviewSurface").Single();
                    var ring = (Border)Named(view, "PictureFocusRing").Single();
                    Assert.True(picture.Focusable);
                    Assert.False(ring.IsVisible, "the ring shows without focus");

                    // The ring is drawn by the card itself (an adorner would not render, nor show over the state panels).
                    Assert.True(picture.Focus(), "the picture did not take focus");
                    Assert.True(picture.IsKeyboardFocused, "the picture is not keyboard focused");
                    Idle();
                    Assert.True(ring.IsVisible, "the picture shows no focus ring");
                    Assert.Equal(new Thickness(2), ring.BorderThickness);
                    Assert.Equal(((SolidColorBrush)Application.Current.FindResource("Brush.Focus.Ring")).Color, ((SolidColorBrush)ring.BorderBrush).Color);
                    Assert.Null(picture.FocusVisualStyle); // no second, dotted ring
                    AssertInside((FrameworkElement)Named(view, "LayoutButton").Single(), (FrameworkElement)Named(view, "CardFrame").Single());
                });
            });

        [Fact]
        public void DisplayCard_Keyboard_EnterArmsCopy_TabReachesTargets_EnterOrSpaceCopies_BlockedDoesNothing_EscStops() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var rx1 = rig.View(rig.Rx1, out var h1);
                var pax = rig.View(rig.Pax, out var h2);
                var rx3 = rig.View(rig.Rx3, out var h3);
                Showing(new StackPanel { Children = { h1, h2, h3 } }, () =>
                {
                    var handle = (Button)Named(rx1, "CopyLayoutHandle").Single();
                    var paxPicture = (UIElement)Named(pax, "PreviewSurface").Single();
                    var rx3Picture = (UIElement)Named(rx3, "PreviewSurface").Single();

                    // Enter on the focused handle arms it (the button's own click).
                    Assert.True(handle.Focus() && handle.IsKeyboardFocused);
                    RaiseKey(handle, Key.Enter);
                    Until(() => rig.Vm.ArmedDisplay == rig.Rx1 && rig.Pax.IsCopyTargetAllowed, "Enter on the Copy layout handle did not arm");

                    // Tab walks to the target cards' pictures as ordinary tab stops.
                    var reached = new List<IInputElement>();
                    for (int i = 0; i < 30; i++)
                    {
                        Assert.True(((FrameworkElement)Keyboard.FocusedElement).MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
                        reached.Add(Keyboard.FocusedElement);
                        if (reached.Contains(paxPicture) && reached.Contains(rx3Picture)) break;
                    }
                    Assert.Contains(paxPicture, reached);
                    Assert.Contains(rx3Picture, reached);

                    // A blocked target says why (its automation name) and does nothing on Enter.
                    Assert.Contains("RX3_1080P_SFPA is 1920x1080", AutomationProperties.GetName(rx3Picture));
                    Assert.True(rx3Picture.Focus());
                    rig.Client.MultiViewCalls.Clear();
                    RaiseKey(rx3Picture, Key.Enter);
                    RaiseKey(rx3Picture, Key.Space);
                    Idle();
                    Assert.Empty(rig.Client.MultiViewCalls);
                    Assert.Same(rig.Rx1, rig.Vm.ArmedDisplay);

                    // An allowed target announces what it receives, and Space copies (Enter does too, below).
                    Assert.StartsWith("PAX355S_SFP A, copy target", AutomationProperties.GetName(paxPicture));
                    Assert.True(paxPicture.Focus());
                    RaiseKey(paxPicture, Key.Space);
                    Until(() => rig.Pax.CanUndoScreen, "Space on the target did not copy");
                    Assert.Null(rig.Vm.ArmedDisplay);
                    Assert.Contains(rig.Client.MultiViewCalls, call => call.StartsWith("multiview:" + CopyRig.PaxMac));

                    // Esc stops an armed copy, from the handle or from a target.
                    Assert.True(handle.Focus());
                    RaiseKey(handle, Key.Enter);
                    Until(() => rig.Vm.ArmedDisplay == rig.Rx1, "the copy was not armed again");
                    RaiseKey(handle, Key.Escape);
                    Assert.Null(rig.Vm.ArmedDisplay);
                    RaiseKey(handle, Key.Enter);
                    Until(() => rig.Vm.ArmedDisplay == rig.Rx1, "the copy was not armed a third time");
                    RaiseKey(rx3Picture, Key.Escape);
                    Assert.Null(rig.Vm.ArmedDisplay);
                    Assert.False(rig.Pax.IsCopyTargetAllowed);
                });
            });

        [Fact]
        public void DisplayCard_AutomationNames_AndAnnouncementRegions_AndTheTurnOffRing() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var rx1 = rig.View(rig.Rx1, out var h1);
                var pax = rig.View(rig.Pax, out var h2);
                Showing(new StackPanel { Children = { h1, h2 } }, () =>
                {
                    Assert.Equal("RX1_c059e_SFPA, picture on screen, MATRIX 2X2 · 3/4", AutomationProperties.GetName(Named(rx1, "PreviewSurface").Single()));
                    Assert.Equal("Copy layout from RX1_c059e_SFPA", AutomationProperties.GetName(Named(rx1, "CopyLayoutHandle").Single()));
                    Assert.Equal("Copy layout from PAX355S_SFP A. PAX355S_SFP A shows nothing to copy", AutomationProperties.GetName(Named(pax, "CopyLayoutHandle").Single()));

                    // The toast and the error banner are live regions: the toast polite, the error assertive.
                    Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(Named(rx1, "UndoToastLabel").Single()));
                    Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(Named(rx1, "ErrorBannerText").Single()));

                    // The Turn Off button (Phase 1 default template) gets the theme ring and a name.
                    var turnOff = FindAll<Button>(rx1).Single(b => b.Command == rig.Rx1.UnrouteCommand);
                    Assert.Same(Application.Current.FindResource("Style.FocusVisual.Ring"), turnOff.FocusVisualStyle);
                    Assert.Equal("Turn off RX1_c059e_SFPA", AutomationProperties.GetName(turnOff));
                });
            });

        [Fact]
        public void DisplayCard_FocusRing_ShowsOnTheHeaderControls() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                rig.Rx1.HasDraft = true;
                var rx1 = rig.View(rig.Rx1, out var h1);
                Showing(h1, () =>
                {
                    foreach (var name in new[] { "LayoutButton", "CopyLayoutHandle", "DraftChip" })
                    {
                        var button = Named(rx1, name).Single();
                        AssertShowsFocus(button, button, name);
                    }
                });
            });

        [Fact]
        public void DisplayCard_LayoutMenu_KeyboardGoesIn_EscCloses_AndFocusReturnsToTheButton() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var rx1 = rig.View(rig.Rx1, out var h1);
                Showing(h1, () =>
                {
                    var button = (Button)Named(rx1, "LayoutButton").Single();
                    var popup = (Popup)Named(rx1, "LayoutMenuPopup").Single();
                    Assert.True(button.Focus());
                    RaiseKey(button, Key.Enter);
                    Until(() => popup.IsOpen, "Enter on Layout did not open the menu");
                    var content = (FrameworkElement)popup.Child;
                    Until(() => content.IsKeyboardFocusWithin, "the keyboard did not go into the menu");
                    var tile = (Button)Keyboard.FocusedElement;
                    Assert.Equal("LayoutMenuTile", tile.Name);
                    Assert.Equal("Single View", AutomationProperties.GetName(tile));

                    RaiseKey(tile, Key.Escape);
                    Until(() => !popup.IsOpen, "Esc did not close the Layout menu");
                    Until(() => button.IsKeyboardFocused, "the keyboard did not return to the Layout button");
                    Assert.False(rig.Rx1.IsLayoutMenuOpen);
                });
            });

        [Fact]
        public void DisplayCard_UndoToast_FadesIn_OnlyWhenWindowsAnimationsAreOn() =>
            RunSta(() =>
            {
                foreach (bool animations in new[] { true, false })
                {
                    var rig = new CopyRig(() => animations);
                    var pax = rig.View(rig.Pax, out var host);
                    Showing(host, () =>
                    {
                        var toast = Named(pax, "UndoToast").Single();
                        Assert.False(toast.HasAnimatedProperties);

                        rig.Pax.ShowUndoScreen("PAX355S now shows RX1's Matrix 2x2");
                        Idle();

                        Assert.True(toast.IsVisible);
                        Assert.Equal(animations, toast.HasAnimatedProperties);
                        if (!animations) Assert.Equal(1.0, toast.Opacity); // off: shown at once, in full
                    });
                }
            });

        [Fact]
        public void DisplayCard_HeldEnter_OnTheCopyHandleAndOnATarget_IsOnePress() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var rx1 = rig.View(rig.Rx1, out var h1);
                var pax = rig.View(rig.Pax, out var h2);
                Showing(new StackPanel { Children = { h1, h2 } }, () =>
                {
                    var handle = (Button)Named(rx1, "CopyLayoutHandle").Single();
                    var paxPicture = (UIElement)Named(pax, "PreviewSurface").Single();
                    Assert.True(handle.Focus());
                    RaiseKey(handle, Key.Enter);
                    Until(() => rig.Vm.ArmedDisplay == rig.Rx1 && rig.Pax.IsCopyTargetAllowed, "Enter did not arm the copy");

                    RaiseRepeatedKey(handle, Key.Enter); // would disarm it again
                    RaiseRepeatedKey(handle, Key.Space);
                    Idle();
                    Assert.Same(rig.Rx1, rig.Vm.ArmedDisplay);

                    Assert.True(paxPicture.Focus());
                    rig.Client.MultiViewCalls.Clear();
                    RaiseRepeatedKey(paxPicture, Key.Enter); // a repeat on a target never copies
                    RaiseRepeatedKey(paxPicture, Key.Space);
                    Idle();
                    Assert.Empty(rig.Client.MultiViewCalls);
                    Assert.Same(rig.Rx1, rig.Vm.ArmedDisplay);

                    RaiseKey(paxPicture, Key.Enter); // the press itself does
                    Until(() => rig.Pax.CanUndoScreen, "the first Enter on the target did not copy");
                });
            });

        [Fact]
        public void DisplayCard_AutomationNames_NameTheDisplayAndTheSource_SoTwoCardsAreNeverTheSame() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var rx1 = rig.View(rig.Rx1, out var h1);
                var pax = rig.View(rig.Pax, out var h2);
                rig.Rx1.HasDraft = true;
                rig.Pax.HasDraft = true;
                rig.Rx1.OpenDropChoice(rig.Tx2);
                Showing(new StackPanel { Children = { h1, h2 } }, () =>
                {
                    string Name(DisplayCardView card, string part) => AutomationProperties.GetName(Named(card, part).Single());

                    Assert.Equal("More layouts for RX1_c059e_SFPA", Name(rx1, "LayoutButton"));
                    Assert.Equal("More layouts for PAX355S_SFP A", Name(pax, "LayoutButton"));
                    Assert.Equal("Resume draft for RX1_c059e_SFPA, not on screen", Name(rx1, "DraftChip"));
                    Assert.Equal("Resume draft for PAX355S_SFP A, not on screen", Name(pax, "DraftChip"));

                    // The drop choice is about one source: its buttons say which.
                    Assert.Contains("TX2_DUAL LINK_SFPA", Name(rx1, "FullScreenButton"));
                    Assert.Contains("TX2_DUAL LINK_SFPA", Name(rx1, "IntoBlockButton"));
                    Assert.Contains("goes on screen now", Name(rx1, "FullScreenButton"));
                    Assert.Contains("opens the Composer", Name(rx1, "IntoBlockButton"));

                    // An unrouted display has no picture on screen.
                    Assert.Equal("PAX355S_SFP A, nothing on screen", Name(pax, "PreviewSurface"));
                    Assert.DoesNotContain("picture on screen", Name(pax, "PreviewSurface"));
                });
            });

        [Fact]
        public void DisplayCard_SingleView_ShowsTheOutputLine_FitsWithoutEllipsis_AndTheFallbackReasonReads() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                rig.Client.DisplayEdids[CopyRig.Rx3Mac] = TestEdids.Build((1920, 1080, 60, false));
                var view = rig.View(rig.Rx3, out var host); // RX3: 1080p display, unrouted
                Showing(host, () =>
                {
                    var line = (TextBlock)Named(view, "OutputLine").Single();
                    var frame = Named(view, "CardFrame").Single();
                    Assert.False(line.IsVisible, "an unrouted card shows no output line");

                    Pump(() => rig.Vm.RouteVideoAsync(rig.Vega.Device, rig.Rx3.Device));
                    Relayout(view);
                    Assert.True(line.IsVisible);
                    Assert.StartsWith("OUT 1920x1080@60", line.Text);
                    AssertTextFits(line);
                    AssertInside(line, frame);

                    // No EDID: the fallback format and its reason read in full on the card.
                    rig.Client.DisplayEdids[CopyRig.Rx3Mac] = null;
                    Pump(() => rig.Vm.RouteVideoAsync(rig.Tx2.Device, rig.Rx3.Device));
                    Relayout(view);
                    Assert.Contains("display EDID unavailable", line.Text);
                    AssertTextFits(line);
                });
            });

        [Fact]
        public void DisplayCard_ActionBar_IsAlwaysVisibleOutsideThePicture_AndTurnOffUsesVectorIcon() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host);
                Showing(host, () =>
                {
                    var dock = (Border)Named(view, "FloatingActionDock").Single();
                    var picture = (FrameworkElement)Named(view, "PreviewSurface").Single();
                    var turnOff = (Button)Named(view, "TurnOffButton").Single();
                    Assert.True(dock.IsVisible && dock.IsHitTestVisible && dock.Opacity == 1.0);
                    // The action bar fills its 56 px row (A14), so it may touch the picture edge; it must not lie over it.
                    Assert.True(BoundsIn(dock, view).Bottom <= BoundsIn(picture, view).Top + 0.5, "the action bar overlaps the 16:9 picture");
                    Assert.True(turnOff.IsVisible && turnOff.ActualHeight >= 48 && turnOff.ActualWidth >= 48);
                    Assert.NotEmpty(FindAll<System.Windows.Shapes.Path>(turnOff));
                });
            });
    }
}
