using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Sdvoe.MultiView;
using AvasRoutingTool.Services;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Wpf.Ui.Controls;
using Xunit;
using Border = System.Windows.Controls.Border;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// UI v2 rename, the visuals (plan R4 part B): the pencil on every TX card and RX card header, and the error banner over the overview.
    /// The commands, the dialog and the flow are part A (MainViewModelRenameTests, ViewSmokeTests.UiV2RenameDialog).
    /// </summary>
    public partial class ViewSmokeTests
    {
        private static Button RenamePencilOf(DependencyObject view) => Assert.IsType<Button>(Named(view, "RenamePencil").Single());

        /// <summary>
        /// What the system's activation of a button does (a tap, Space, Enter, or a screen reader's Invoke): ButtonBase.OnClick, which raises Click AND
        /// runs the Command. (Raising the Click event alone does not run a Command, so the tests use this for command buttons.)
        /// </summary>
        private static void Activate(Button button)
        {
            var invoke = (System.Windows.Automation.Provider.IInvokeProvider)new System.Windows.Automation.Peers.ButtonAutomationPeer(button)
                .GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke);
            invoke.Invoke();
        }

        private static Func<Task> CountingRename(Action counted) => () =>
        {
            counted();
            return Task.CompletedTask;
        };

        /// <summary>The pencil is one icon-only, touch-sized tab stop, wired to the card's command, with its spoken name and tooltip.</summary>
        private static void AssertRenamePencil(DependencyObject view, ICommand command, string spokenName)
        {
            var pencil = RenamePencilOf(view);
            Assert.Same(Application.Current.FindResource("Style.Button.IconPencil"), pencil.Style);
            Assert.True(pencil.IsVisible, "the pencil is not shown");
            Assert.True(pencil.IsEnabled, "the pencil is disabled");
            Assert.True(pencil.IsTabStop && pencil.Focusable, "the pencil is not a tab stop");
            Assert.True(pencil.ActualWidth >= 48 && pencil.ActualHeight >= 48, $"the pencil is {pencil.ActualWidth:F1}x{pencil.ActualHeight:F1}");
            Assert.Same(command, pencil.Command);
            Assert.Equal(spokenName, AutomationProperties.GetName(pencil));
            Assert.Equal("Rename", pencil.ToolTip as string);

            var icon = PencilIconOf(pencil);
            Assert.Equal("edit", icon.Kind);
            Assert.Equal(Token("Size.Icon.S"), icon.Size);
            // Icon only: no words (a SymbolIcon's own glyph is its only text)
            Assert.Empty(FindAll<TextBlock>(pencil).Where(t => !string.IsNullOrEmpty(t.Text) && !icon.Element.IsAncestorOf(t)));
        }

        [Fact]
        public void RenamePencil_OnATile_IsOneTouchSizedTabStop_WiredToTheCardsRenameCommand() =>
            RunSta(() =>
            {
                var vm = UiV2Source();
                vm.RenameHandler = () => Task.CompletedTask;
                SourceCardAt(vm, view => AssertRenamePencil(view, vm.RenameCommand, "Rename TX2"));
            });

        [Fact]
        public void RenamePencil_OnARoutedAndAnUnroutedDisplayCard_IsOneTouchSizedTabStop_WiredToTheCardsRenameCommand() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                rig.Rx1.RenameHandler = () => Task.CompletedTask;
                rig.Rx3.RenameHandler = () => Task.CompletedTask;
                var routed = rig.View(rig.Rx1, out var routedHost, 613);
                Showing(routedHost, () =>
                {
                    Assert.True(rig.Rx1.IsRouted);
                    AssertRenamePencil(routed, rig.Rx1.RenameCommand, "Rename RX1_c059e_SFPA");
                });

                var unrouted = rig.View(rig.Rx3, out var unroutedHost, 613);
                Showing(unroutedHost, () =>
                {
                    Assert.False(rig.Rx3.IsRouted);
                    AssertRenamePencil(unrouted, rig.Rx3.RenameCommand, "Rename RX3_1080P_SFPA");
                });
            });

        [Fact]
        public void RenamePencil_WithoutAHandler_IsDisabled_ButStillThere() =>
            RunSta(() => SourceCardAt(UiV2Source(), view =>
            {
                var pencil = RenamePencilOf(view);
                Assert.True(pencil.IsVisible);
                Assert.False(pencil.IsEnabled);
            }));

        // ---- the hit box ----

        private static void AssertPressesLandOnThePencil(FrameworkElement view, Button pencil, bool corners, string what)
        {
            double w = pencil.ActualWidth, h = pencil.ActualHeight;
            var spots = new List<Point> { new(w / 2, h / 2) };
            if (corners) spots.AddRange(new[] { new Point(1, 1), new Point(w - 1, 1), new Point(1, h - 1), new Point(w - 1, h - 1) });
            foreach (var spot in spots)
            {
                var at = pencil.TranslatePoint(spot, view);
                var hit = PressTarget(view, at);
                string landsOn = hit == null ? "nothing" : $"{hit.GetType().Name} '{(hit as FrameworkElement)?.Name}' at {(hit is FrameworkElement hf ? BoundsIn(hf, view).ToString() : "")} (pencil at {BoundsIn(pencil, view)})";
                Assert.True(hit != null && Within(hit, pencil), $"{what}: a press at ({spot.X:F1},{spot.Y:F1}) of the pencil lands on {landsOn}");
            }
        }

        [Fact]
        public void RenamePencil_OnATile_TakesAPressOnItsWholeBox_CentreAndCorners() =>
            RunSta(() =>
            {
                var vm = UiV2Source();
                vm.RenameHandler = () => Task.CompletedTask;
                SourceCardAt(vm, view => AssertPressesLandOnThePencil(view, RenamePencilOf(view), corners: true, "the tile"));

                // Streaming and Dual Link change the rows below the header: the pencil still wins the strip it shares with the picture
                var streaming = UiV2Source(link: "Dual Link");
                streaming.IsStreaming = true;
                streaming.RenameHandler = () => Task.CompletedTask;
                SourceCardAt(streaming, view => AssertPressesLandOnThePencil(view, RenamePencilOf(view), corners: true, "the streaming tile"));
            });

        [Fact]
        public void RenamePencil_OnADisplayCard_TakesAPressOnItsWholeBox_At613_AndAtTheMinimumWidth()
        {
            RunSta(() =>
            {
                var rig = new CopyRig();
                rig.Rx1.RenameHandler = () => Task.CompletedTask;
                rig.Rx3.RenameHandler = () => Task.CompletedTask;
                foreach (var (card, label) in new[] { (rig.Rx1, "routed"), (rig.Rx3, "unrouted") })
                {
                    var wide = rig.View(card, out var wideHost, 613);
                    Showing(wideHost, () => AssertPressesLandOnThePencil(wide, RenamePencilOf(wide), corners: true, $"the {label} card at 613"));
                }

                // At the minimum width the centre must land on the pencil; the corners too (the header sits over the action bar)
                var narrow = rig.View(rig.Rx1, out var narrowHost, Token("Size.Card.Display.Min"));
                Showing(narrowHost, () =>
                {
                    var pencil = RenamePencilOf(narrow);
                    AssertPressesLandOnThePencil(narrow, pencil, corners: false, "the card at the minimum width");
                    AssertPressesLandOnThePencil(narrow, pencil, corners: true, "the card at the minimum width");
                });
            });
        }

        private static void AssertNoOverlap(Rect a, Rect b, string what)
        {
            var common = Rect.Intersect(a, b);
            Assert.True(common.IsEmpty || common.Width <= 0.5 || common.Height <= 0.5, $"{what}: {a} and {b} share {common}");
        }

        [Fact]
        public void RenamePencil_OnADisplayCard_At613_DoesNotShareAHitBoxWith_Layout_CopyLayout_Power_OrTheBadges() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                rig.Rx1.RenameHandler = () => Task.CompletedTask;
                var view = rig.View(rig.Rx1, out var host, 613);
                Showing(host, () =>
                {
                    var pencil = BoundsIn(RenamePencilOf(view), view);
                    foreach (var name in new[] { "LayoutButton", "CopyLayoutHandle", "TurnOffButton", "DeviceInfoBadge", "ModeBadge" })
                    {
                        var other = Named(view, name).Single();
                        Assert.True(other.IsVisible, $"{name} is not shown");
                        AssertNoOverlap(pencil, BoundsIn(other, view), $"the pencil and {name}");
                    }
                });
            });

        [Fact]
        public void RenamePencil_OnATile_DoesNotShareAHitBoxWithTheInfoBadge_AndStaysInsideTheCard() =>
            RunSta(() =>
            {
                var vm = UiV2Source();
                vm.RenameHandler = () => Task.CompletedTask;
                SourceCardAt(vm, view =>
                {
                    var pencil = BoundsIn(RenamePencilOf(view), view);
                    AssertNoOverlap(pencil, BoundsIn(Named(view, "SourceInfoBadge").Single(), view), "the pencil and the info badge");
                    Assert.True(pencil.Left >= 0 && pencil.Top >= 0 && pencil.Right <= view.ActualWidth, $"the pencil {pencil} leaves the card");
                });
            });

        // ---- nothing around it moves ----

        [Fact]
        public void RenamePencil_TileHeight_IsStill278_IdleAndStreaming_AndTheCompactTileHasNoPencil() =>
            RunSta(() =>
            {
                foreach (var streaming in new[] { false, true })
                {
                    var vm = UiV2Source();
                    vm.IsStreaming = streaming;
                    vm.RenameHandler = () => Task.CompletedTask;
                    SourceCardAt(vm, view =>
                    {
                        view.Measure(new Size(300, double.PositiveInfinity));
                        Assert.Equal(FullTileHeight, view.DesiredSize.Height, 0.5);
                    });
                }

                var compactVm = UiV2Source();
                compactVm.RenameHandler = () => Task.CompletedTask;
                var compact = new SourceCardView { DataContext = compactVm, IsCompact = true, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
                Showing(compact, () =>
                {
                    Assert.False(RenamePencilOf(compact).IsVisible, "the compact tile (the Composer palette) shows a pencil");
                    compact.Measure(new Size(240, double.PositiveInfinity));
                    Assert.Equal(CompactTileHeightAt240, compact.DesiredSize.Height, 0.5);
                });
            });

        [Fact]
        public void RenamePencil_DisplayCardHeader_IsStill24_TheRowsAreUnchanged_AndTheModeBadgeIsNotClipped() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                rig.Rx1.RenameHandler = () => Task.CompletedTask;
                foreach (var width in new[] { 613.0, Token("Size.Card.Display.Min") })
                {
                    var view = rig.View(rig.Rx1, out var host, width);
                    Showing(host, () =>
                    {
                        var rows = ((Grid)Named(view, "CardGrid").Single()).RowDefinitions;
                        Assert.Equal(Token("Size.Card.Row.Header"), rows[0].ActualHeight, 0.5);
                        Assert.Equal(Token("Size.Card.Row.ActionBar"), rows[1].ActualHeight, 0.5);
                        Assert.Equal(Token("Size.Card.Row.Dock"), rows[3].ActualHeight, 0.5);

                        var header = (Grid)VisualTreeHelper.GetParent(RenamePencilOf(view));
                        Assert.Equal(Token("Size.Card.Row.Header"), header.ActualHeight, 0.5);

                        // Nothing in the header is cut off: every part lies inside it, and the mode badge shows its whole text
                        AssertInside(Named(view, "ModeBadge").Single(), header);
                        AssertInside(Named(view, "DeviceInfoBadge").Single(), header);
                        AssertTextFits((TextBlock)Named(view, "ModeBadgeText").Single());
                        var pencil = BoundsIn(RenamePencilOf(view), header);
                        Assert.True(pencil.Left >= -0.5 && pencil.Right <= header.ActualWidth + 0.5, $"at {width}: the pencil {pencil} leaves the header");
                        AssertNoOverlap(pencil, BoundsIn(Named(view, "ModeBadge").Single(), header), $"at {width}: the pencil and the mode badge");
                    });
                }
            });

        // ---- paint: secondary at rest, blue under the mouse and on keyboard focus, never red ----

        private static Color IconColour(Button pencil) => ColorOf(PencilIconOf(pencil).Foreground);

        /// <summary>The pencil's icon: the Material "edit".</summary>
        private static (string Kind, double Size, System.Windows.Media.Brush Foreground, FrameworkElement Element) PencilIconOf(Button pencil)
        {
            var icon = FindAll<AvasRoutingTool.Controls.MaterialIcon>(pencil).Single();
            return (icon.Kind, icon.Width, icon.Foreground, icon);
        }

        private static void AssertPencilPaint(DependencyObject view, string what, List<Color> seen)
        {
            var pencil = RenamePencilOf(view);
            var fill = (Border)pencil.Template.FindName("HoverFill", pencil);

            // At rest: transparent, the icon is the secondary text colour
            Assert.Equal(0, ColorOf(fill.Background).A);
            Assert.Equal(PaletteColor("Brush.Text.Secondary"), IconColour(pencil));
            seen.Add(IconColour(pencil));

            // Under the mouse: the icon is the accent, the fill the neutral hover
            SetMouseOver(pencil, true);
            Idle();
            Assert.Equal(PaletteColor("Brush.Accent.Primary"), IconColour(pencil));
            Assert.Equal(PaletteColor("Brush.Surface.Hover"), ColorOf(fill.Background));
            seen.Add(IconColour(pencil));
            seen.Add(ColorOf(fill.Background));
            SetMouseOver(pencil, false);
            Idle();
            Assert.Equal(PaletteColor("Brush.Text.Secondary"), IconColour(pencil));

            // Keyboard focus: the 2 px ring, and the accent icon (no fill: hover only)
            AssertShowsFocus(pencil, pencil, $"{what}: the pencil");
            Assert.Equal(PaletteColor("Brush.Accent.Primary"), IconColour(pencil));
            Assert.Equal(0, ColorOf(fill.Background).A);
            seen.Add(IconColour(pencil));
            var ring = (Border)pencil.Template.FindName("FocusRing", pencil);
            Assert.True(ring.IsVisible);
            Assert.Equal(2.0, ring.BorderThickness.Left);
            Assert.Equal(PaletteColor("Brush.Focus.Ring"), ColorOf(ring.BorderBrush));
            Keyboard.ClearFocus();
            InputModality.IsKeyboard = false;
        }

        [Theory]
        [InlineData(ThemeMode.Dark)]
        [InlineData(ThemeMode.Light)]
        public void RenamePencil_IsSecondaryAtRest_BlueUnderTheMouseAndOnKeyboardFocus_NeverRed_InBothThemes(ThemeMode mode) =>
            RunSta(() =>
            {
                var theme = new ThemeService();
                try
                {
                    // The rig first: its MainViewModel builds a ThemeService that re-applies the saved (Dark) theme on construction.
                    var rig = new CopyRig();
                    rig.Rx1.RenameHandler = () => Task.CompletedTask;
                    var tile = UiV2Source();
                    tile.RenameHandler = () => Task.CompletedTask;
                    theme.SetTheme(mode);

                    var seen = new List<Color>();
                    var card = rig.View(rig.Rx1, out var host, 613);
                    Showing(host, () => AssertPencilPaint(card, "the display card", seen));
                    SourceCardAt(tile, view => AssertPencilPaint(view, "the tile", seen));

                    foreach (var danger in new[] { "Brush.Text.Danger", "Brush.Border.Danger", "Brush.Action.Danger", "Brush.Surface.Danger", "Brush.Surface.DangerStrong", "Brush.Text.DangerSoft", "Brush.Text.OnScrimDanger" })
                        Assert.DoesNotContain(PaletteColor(danger), seen);
                }
                finally { theme.SetTheme(ThemeMode.Dark); }
            });

        [Fact]
        public void RenamePencil_Style_HasNoDangerBrush_AndNoLiteralColour()
        {
            var path = Path.Combine(UiV2Allowlist.SourceRoot(), "Resources", "Styles", "Buttons.xaml");
            var text = File.ReadAllText(path);
            int start = text.IndexOf("x:Key=\"Style.Button.IconPencil\"", StringComparison.Ordinal);
            Assert.True(start >= 0, "Style.Button.IconPencil is not in Buttons.xaml");
            int end = text.IndexOf("</Style>", start, StringComparison.Ordinal);
            var style = text[start..end];
            Assert.DoesNotContain("Danger", style);
            Assert.DoesNotContain("Unroute", style);
            Assert.DoesNotContain("#", style);
            Assert.DoesNotContain("<ControlTemplate.Resources", style);
        }

        // ---- activating it ----

        [Fact]
        public void RenamePencil_ClickAndEnter_RunTheCardsRenameHandlerOnce_OnATileAndOnADisplayCard() =>
            RunSta(() =>
            {
                var tileCalls = 0;
                var tile = UiV2Source();
                tile.RenameHandler = CountingRename(() => tileCalls++);
                SourceCardAt(tile, view =>
                {
                    var pencil = RenamePencilOf(view);
                    Activate(pencil);
                    Until(() => tileCalls == 1, "a click on the tile's pencil did not run the rename");
                    Idle();
                    Assert.Equal(1, tileCalls);

                    // Enter on a button is its command (the click itself is the system's)
                    pencil.Command.Execute(pencil.CommandParameter);
                    Until(() => tileCalls == 2, "the pencil's command did not run the rename");
                    Assert.Equal(2, tileCalls);
                });

                var rig = new CopyRig();
                var displayCalls = 0;
                rig.Rx1.RenameHandler = CountingRename(() => displayCalls++);
                var card = rig.View(rig.Rx1, out var host, 613);
                Showing(host, () =>
                {
                    var pencil = RenamePencilOf(card);
                    Activate(pencil);
                    Until(() => displayCalls == 1, "a click on the display's pencil did not run the rename");
                    Idle();
                    Assert.Equal(1, displayCalls);
                    pencil.Command.Execute(pencil.CommandParameter);
                    Until(() => displayCalls == 2, "the display pencil's command did not run the rename");
                });
            });

        /// <summary>A tile among the sources of a real overview (its MainViewModel above it), as the strip hosts it.</summary>
        private static SourceCardView TileOf(VisualRoutingView overview, SourceCardViewModel vm) =>
            FindAll<SourceCardView>(overview).Single(c => !c.IsCompact && ReferenceEquals(c.DataContext, vm));

        [Fact]
        public void RenamePencil_OnATile_NeverArmsTheSource_NorStartsADrag_ByTapOrByKey() =>
            RunSta(() =>
            {
                var (main, _, sources) = Overview();
                var source = sources[0];
                int renames = 0;
                source.RenameHandler = CountingRename(() => renames++);
                var overview = new VisualRoutingView { DataContext = main };
                Showing(overview, () =>
                {
                    var tile = TileOf(overview, source);
                    var pencil = RenamePencilOf(tile);
                    var picture = ThumbnailOf(tile);

                    // The harness works: a tap on the picture is a tap on the tile, which arms the source (and starts tracking a possible drag)
                    Tap(picture, Mouse.PreviewMouseDownEvent);
                    Assert.True(tile.IsPressTracked, "a press on the picture is not tracked for a drag");
                    Tap(picture, Mouse.PreviewMouseUpEvent);
                    Tap(picture, Mouse.MouseUpEvent);
                    Assert.Same(source, main.ArmedSource);
                    main.ArmSource(source); // tap again: off
                    Assert.Null(main.ArmedSource);

                    // A tap on the pencil is the pencil's: it arms nothing and tracks no drag
                    Tap(pencil, Mouse.PreviewMouseDownEvent);
                    Tap(pencil, Mouse.MouseDownEvent);
                    Assert.False(tile.IsPressTracked, "a press on the pencil is tracked, a move could start a drag of the source");
                    Tap(pencil, Mouse.PreviewMouseUpEvent);
                    Tap(pencil, Mouse.MouseUpEvent);
                    Idle();
                    Assert.Null(main.ArmedSource);

                    // The activation itself (what a tap or Space does) opens the rename, once, and arms nothing
                    Activate(pencil);
                    Until(() => renames == 1, "a click on the pencil did not run the rename");
                    Assert.Null(main.ArmedSource);

                    // Enter and Space on the focused pencil are the pencil's: the card's own key handler leaves them alone.
                    // Enter opens the rename (once more); Space presses it and the release would, so here it only must not arm.
                    Assert.True(pencil.Focus());
                    RaiseKey(pencil, Key.Enter);
                    Idle();
                    Until(() => renames == 2, "Enter on the focused pencil did not open the rename");
                    Assert.Null(main.ArmedSource);
                    RaiseKey(pencil, Key.Space);
                    Idle();
                    Assert.Null(main.ArmedSource);
                    Assert.Equal(2, renames);

                    // ...while the same keys on the tile itself do arm it (the harness works)
                    Assert.True(tile.Focus());
                    RaiseKey(tile, Key.Enter);
                    Assert.Same(source, main.ArmedSource);
                    main.ArmSource(source);
                    Assert.Null(main.ArmedSource);
                });
            });

        [Fact]
        public void RenamePencil_OnADisplayCard_IsNotATapOnTheCard() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                rig.Rx3.RenameHandler = () => Task.CompletedTask;
                var rx3 = rig.View(rig.Rx3, out var host);
                Showing(host, () =>
                {
                    // A source armed: a tap on the unrouted card routes it, a tap on its pencil does not
                    rig.Vm.ArmSource(rig.Tx2);
                    TapCard(rx3, releasedOn: RenamePencilOf(rx3));
                    Idle();
                    Assert.Null(rig.Client.LastVideoRx);
                    Assert.Same(rig.Tx2, rig.Vm.ArmedSource);

                    TapCard(rx3);
                    Until(() => rig.Client.LastVideoRx == CopyRig.Rx3Mac, "the harness: a tap on the card did not route the armed source");
                });
            });

        // ---- the banner ----

        private static VisualRoutingView OverviewWithBanner(out MainViewModel main, out DisplayCardViewModel rx)
        {
            var (vm, display, _) = Overview();
            main = vm;
            rx = display;
            return new VisualRoutingView { DataContext = vm };
        }

        private static Border BannerOf(DependencyObject view) => Assert.IsType<Border>(Named(view, "RenameBanner").Single());

        private static string BannerFor(DisplayCardViewModel rx) => $"Couldn't rename {rx.DeviceName}: this connection can't rename devices";

        [Fact]
        public void RenameBanner_IsHidden_UntilARenameFails()
        {
            RunSta(() =>
            {
                var overview = OverviewWithBanner(out var main, out _);
                Showing(overview, () =>
                {
                    Assert.Null(main.RenameBanner);
                    Assert.False(BannerOf(overview).IsVisible);
                    Assert.False(Named(overview, "RenameBannerDismiss").Single().IsVisible);
                });
            });
        }

        [Fact]
        public void RenameBanner_ShowsTheReason_WithAWarningIcon_AssertiveAndReadable_AndDismissIsA48pxButtonThatClearsIt() =>
            RunSta(() =>
            {
                var overview = OverviewWithBanner(out var main, out var rx);
                Showing(overview, () =>
                {
                    Pump(() => main.RenameDeviceAsync(rx.Device)); // a client that cannot rename: a banner, no dialog
                    Relayout(overview);

                    var banner = BannerOf(overview);
                    Assert.True(banner.IsVisible);
                    var text = (TextBlock)Named(overview, "RenameBannerText").Single();
                    Assert.Equal(BannerFor(rx), text.Text);
                    Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
                    Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(text));
                    Assert.All(FindAll<TextBlock>(banner).Where(t => t.IsVisible && !string.IsNullOrEmpty(t.Text)), t => Assert.True(t.FontSize >= 12, $"'{t.Text}' is {t.FontSize}px"));
                    Assert.Contains(FindAll<AvasRoutingTool.Controls.MaterialIcon>(banner), i => i.Kind == "warning");
                    var b = BoundsIn(banner, overview);
                    Assert.True(b.Left >= 0 && b.Right <= overview.ActualWidth + 0.5 && b.Bottom <= overview.ActualHeight + 0.5, $"the banner {b} leaves the overview");

                    var dismiss = (Button)Named(overview, "RenameBannerDismiss").Single();
                    Assert.True(dismiss.IsVisible && dismiss.IsEnabled);
                    Assert.True(dismiss.ActualWidth >= 48 && dismiss.ActualHeight >= 48, $"Dismiss is {dismiss.ActualWidth:F1}x{dismiss.ActualHeight:F1}");
                    Assert.True(dismiss.IsTabStop);
                    Assert.Equal("Dismiss rename error", AutomationProperties.GetName(dismiss));
                    Assert.Same(main.DismissRenameBannerCommand, dismiss.Command);
                    Assert.Equal("close", FindAll<AvasRoutingTool.Controls.MaterialIcon>(dismiss).Single().Kind);
                    AssertPressesLandOnThePencil(overview, dismiss, corners: false, "the banner's Dismiss"); // the ghost button's corners are rounded

                    Activate(dismiss);
                    Relayout(overview);
                    Assert.Null(main.RenameBanner);
                    Assert.False(banner.IsVisible);
                });
            });

        [Fact]
        public void RenameBanner_AppearingAndGoing_MovesNothingElse() =>
            RunSta(() =>
            {
                var overview = OverviewWithBanner(out var main, out var rx);
                Showing(overview, () =>
                {
                    List<Rect> Boxes() =>
                        FindAll<DisplayCardView>(overview).Select(c => BoundsIn(c, overview))
                            .Concat(FindAll<SourceCardView>(overview).Where(c => !c.IsCompact).Select(c => BoundsIn(c, overview)))
                            .Concat(FindAll<FrameworkElement>(overview).Where(e => e.Name is "DisplaysScroll" or "SectionDivider" or "OverviewGrid").Select(e => BoundsIn(e, overview)))
                            .ToList();

                    var before = Boxes();
                    Assert.NotEmpty(before);
                    Pump(() => main.RenameDeviceAsync(rx.Device));
                    Relayout(overview);
                    Assert.True(BannerOf(overview).IsVisible);
                    Assert.Equal(before, Boxes());

                    main.DismissRenameBannerCommand.Execute(null);
                    Relayout(overview);
                    Assert.False(BannerOf(overview).IsVisible);
                    Assert.Equal(before, Boxes());
                });
            });

        [Theory]
        [InlineData(ThemeMode.Dark)]
        [InlineData(ThemeMode.Light)]
        public void RenameBanner_IsReadable_InBothThemes(ThemeMode mode) =>
            RunSta(() =>
            {
                var theme = new ThemeService();
                try
                {
                    var overview = OverviewWithBanner(out var main, out var rx);
                    theme.SetTheme(mode); // after the view model is built, as the other themed smokes do
                    Showing(overview, () =>
                    {
                        Pump(() => main.RenameDeviceAsync(rx.Device));
                        Relayout(overview);
                        var banner = BannerOf(overview);
                        Assert.True(banner.IsVisible);
                        var text = (TextBlock)Named(overview, "RenameBannerText").Single();
                        double ratio = Contrast(ColorOf(((SolidColorBrush)text.Foreground)), ColorOf(banner.Background));
                        Assert.True(ratio >= 4.5, $"{mode}: the banner text is {ratio:F2}:1 on its fill");
                        Assert.Equal(PaletteColor("Brush.Surface.ScrimDanger"), ColorOf(banner.Background));
                    });
                }
                finally { theme.SetTheme(ThemeMode.Dark); }
            });

        // ---- end to end on a display card ----

        private sealed class RenameViewRig
        {
            public const string Tx1Mac = "74fe48000001";
            public const string Rx1Mac = "74fe48000011";
            public const string Rx2Mac = "74fe48000012";

            public RenamingMockClient Client { get; } = new();
            public MainViewModel Vm { get; }

            /// <summary>What the rename dialog returns (null = cancelled).</summary>
            public string? Answer { get; set; }

            public RenameViewRig()
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                // Non-AVAS sources (vendor 192) so discovery starts no preview stream.
                Client.Server.Add(new AvasDevice { DeviceId = Tx1Mac, DeviceName = "Alpha TX", VendorId = 192, IsTransmitter = true, IsActive = true });
                Client.Server.Add(new AvasDevice { DeviceId = "74fe48000002", DeviceName = "Lobby TX", VendorId = 192, IsTransmitter = true, IsActive = true });
                Client.Server.Add(new AvasDevice { DeviceId = Rx1Mac, DeviceName = "Wall RX", IsReceiver = true, IsActive = true, ActiveSourceId = Tx1Mac, ActiveSourceName = "Alpha TX" });
                Client.Server.Add(new AvasDevice { DeviceId = Rx2Mac, DeviceName = "Bar RX", IsReceiver = true, IsActive = true });
                Vm = new MainViewModel(new Configuration.ConfigService(), Client, new MockRtpReceiver(), new MulticastIpManager(),
                    new RoutingOrchestrator(Client), new MultiViewOrchestrator(Client), post: a => a());
                Vm.AskForName = _ => Answer;
                Pump(() => Vm.RefreshDevicesAsync());
            }

            public DisplayCardView CardOf(VisualRoutingView overview, string mac) =>
                FindAll<DisplayCardView>(overview).Single(c => ((DisplayCardViewModel)c.DataContext).MacAddress == mac);

            public string NameOnCard(VisualRoutingView overview, string mac) =>
                ((TextBlock)Named(CardOf(overview, mac), "DeviceNameText").Single()).Text;
        }

        [Fact]
        public void RenamePencil_OnADisplayCard_RenamesOnlyOnceTheServerAnsweredAndRediscoveryReturnedTheName() =>
            RunSta(() =>
            {
                var rig = new RenameViewRig { Answer = "Stage left" };
                var overview = new VisualRoutingView { DataContext = rig.Vm };
                Showing(overview, () =>
                {
                    Assert.Equal("Bar RX", rig.NameOnCard(overview, RenameViewRig.Rx2Mac));
                    var pencil = RenamePencilOf(rig.CardOf(overview, RenameViewRig.Rx2Mac));
                    Assert.True(pencil.IsEnabled, "the discovered card's pencil is disabled");
                    Assert.Equal("Rename Bar RX", AutomationProperties.GetName(pencil));

                    rig.Client.DiscoverGate = new TaskCompletionSource<bool>();
                    int discovered = rig.Client.DiscoverCount;
                    Activate(pencil);
                    Until(() => rig.Client.RenameCalls.Count == 1 && rig.Client.DiscoverCount == discovered + 1, "the rename did not reach rediscovery");
                    Assert.Equal((RenameViewRig.Rx2Mac, "Stage left"), rig.Client.RenameCalls.Single());

                    // The server answered, discovery has not returned: the card still has the old name
                    Relayout(overview);
                    Assert.Equal("Bar RX", rig.NameOnCard(overview, RenameViewRig.Rx2Mac));

                    rig.Client.DiscoverGate.SetResult(true);
                    Until(() => !rig.Vm.IsRenaming, "the rename did not finish");
                    Relayout(overview);
                    Assert.Equal("Stage left", rig.NameOnCard(overview, RenameViewRig.Rx2Mac));
                    Assert.False(BannerOf(overview).IsVisible);
                    Assert.Equal("Rename Stage left", AutomationProperties.GetName(RenamePencilOf(rig.CardOf(overview, RenameViewRig.Rx2Mac))));
                });
            });

        [Fact]
        public void RenamePencil_OnADisplayCard_WhenTheServerRefuses_KeepsTheOldName_AndTheBannerSaysWhy() =>
            RunSta(() =>
            {
                var rig = new RenameViewRig { Answer = "A much too long name for the server" };
                rig.Client.Result = RenameResult.Failed("Device name too long, must be 19 characters at most");
                var overview = new VisualRoutingView { DataContext = rig.Vm };
                Showing(overview, () =>
                {
                    Activate(RenamePencilOf(rig.CardOf(overview, RenameViewRig.Rx2Mac)));
                    Until(() => rig.Vm.HasRenameBanner, "no banner after the refused rename");
                    Relayout(overview);

                    Assert.Equal("Bar RX", rig.NameOnCard(overview, RenameViewRig.Rx2Mac));
                    var banner = BannerOf(overview);
                    Assert.True(banner.IsVisible);
                    Assert.Equal("Couldn't rename Bar RX: Device name too long, must be 19 characters at most", ((TextBlock)Named(overview, "RenameBannerText").Single()).Text);
                });
            });

        // ---- shots (on demand) ----

        [Fact]
        public void UiV2_RenameShots()
        {
            var folder = Environment.GetEnvironmentVariable("AVAS_UIV2_SHOTS");
            if (string.IsNullOrWhiteSpace(folder)) return;
            Directory.CreateDirectory(folder);

            RunSta(() =>
            {
                var theme = new ThemeService();
                try
                {
                    foreach (var light in new[] { false, true })
                    {
                        var mode = light ? ThemeMode.Light : ThemeMode.Dark;
                        var tag = light ? "light" : "dark";

                        // The dialog with a valid new name, and with an invalid one (the reason shows)
                        foreach (var (kind, typed) in new[] { ("valid", "Stage left"), ("invalid", "Stage:left") })
                        {
                            var vm = new RenameDialogViewModel("TX2_DUAL LINK", RenameOthers);
                            var dialog = new RenameDialog(vm) { ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = 0 };
                            theme.SetTheme(mode);
                            dialog.Show();
                            try
                            {
                                Relayout(dialog);
                                NameBoxOf(dialog).Text = typed;
                                Relayout(dialog);
                                SavePng(ClientRoot(dialog), Path.Combine(folder, $"rename-dialog-{kind}-{tag}.png"));
                            }
                            finally { dialog.Close(); }
                        }

                        // The Main Window at 1920x1080 with a failed rename on screen
                        {
                            var rig = new RenameViewRig { Answer = "A much too long name for the server" };
                            rig.Client.Result = RenameResult.Failed("Device name too long, must be 19 characters at most");
                            var window = new MainWindow { DataContext = rig.Vm, WindowState = WindowState.Normal, Width = 1920, Height = 1080, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000 };
                            window.Show();
                            try
                            {
                                theme.SetTheme(mode); // the window's own view model resets the theme when it is built
                                Relayout(window);
                                Pump(() => rig.Vm.RenameDeviceAsync(rig.Vm.Displays.Single(d => d.MacAddress == RenameViewRig.Rx2Mac).Device));
                                Relayout(window);
                                SavePng(ClientRoot(window), Path.Combine(folder, $"rename-banner-{tag}.png"));
                            }
                            finally { window.Close(); }
                        }
                    }
                }
                finally { theme.SetTheme(ThemeMode.Dark); }
            });
        }
    }
}
