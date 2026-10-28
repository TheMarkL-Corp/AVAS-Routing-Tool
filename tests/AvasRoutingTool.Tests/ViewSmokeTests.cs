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
    /// <summary>
    /// Instantiates the real XAML views with real view models and forces a layout pass.
    /// Catches binding/resource crashes (e.g. TwoWay binding on a read-only property) that
    /// pure view-model tests can never see.
    /// </summary>
    [Collection("WpfSta")]
    public class ViewSmokeTests
    {
        private static void RunSta(Action body)
        {
            Exception? failure = null;
            var t = new Thread(() =>
            {
                try
                {
                    // The input mode is process-wide: every test starts as a pointer user (the start state) and sets what it needs.
                    InputModality.IsKeyboard = false;
                    var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    foreach (var path in new[] { "Themes/Theme.Dark", "Theme", "Icons" })
                    {
                        app.Resources.MergedDictionaries.Add(new ResourceDictionary
                        {
                            Source = new Uri($"pack://application:,,,/AvasRoutingTool;component/Resources/{path}.xaml")
                        });
                    }
                    body();
                }
                catch (Exception ex) { failure = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("View smoke failure: " + failure);
        }

        private static void Layout(FrameworkElement el)
        {
            var window = new Window { Content = el, Width = 1920, Height = 1080, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -10000 };
            window.Show();
            el.Measure(new Size(1920, 1080));
            el.Arrange(new Rect(0, 0, 1920, 1080));
            el.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            window.Close();
        }

        private static AvasDevice Rx(string src = "") => new()
        {
            DeviceId = "74fe483c059e", DeviceName = "RX1", IsReceiver = true, IsActive = true,
            ActiveSourceId = src, ActiveSourceName = src == "" ? "" : "TX2"
        };

        [Fact]
        public void DisplayCardView_Unrouted_RendersWithoutBindingCrash() =>
            RunSta(() => Layout(new DisplayCardView { DataContext = new DisplayCardViewModel(Rx()) }));

        [Fact]
        public void DisplayCardView_Routed_RendersWithoutBindingCrash() =>
            RunSta(() => Layout(new DisplayCardView { DataContext = new DisplayCardViewModel(Rx("74fe488b07bb")) }));

        private static DisplayCardViewModel MultiViewDisplay(string layoutId, bool fill)
        {
            var vm = new DisplayCardViewModel(Rx());
            vm.ApplyLayout(Sdvoe.MultiView.MultiViewPresets.Build(layoutId, 1920, 1080), layoutId.ToUpperInvariant());
            if (fill)
            {
                var src = new SourceCardViewModel(new AvasDevice { DeviceId = "74fe488b07bb", DeviceName = "TX2", VendorId = 105, IsTransmitter = true, IsActive = true })
                {
                    PreviewBitmap = new System.Windows.Media.Imaging.WriteableBitmap(64, 36, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null)
                };
                vm.AssignSlot(vm.Slots[0].Index, src);
                vm.AssignSlot(vm.Slots[1].Index, new SourceCardViewModel(new AvasDevice { DeviceId = "0016c0589c1e", DeviceName = "VEGA", VendorId = 192, IsTransmitter = true, IsActive = true }));
            }
            return vm;
        }

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
                var panel = FindDescendant<Controls.FractionalLayoutPanel>(view);
                Assert.NotNull(panel);
                Assert.Equal(4, panel!.Children.Count);
                foreach (System.Windows.UIElement child in panel.Children)
                {
                    var fe = (FrameworkElement)child;
                    Assert.True(fe.ActualWidth > 20 && fe.ActualHeight > 10, $"window rendered {fe.ActualWidth}x{fe.ActualHeight}");
                }
            });

        [Fact]
        public void VisualRoutingView_WithAMultiViewDisplay_RendersWithoutCrash() =>
            RunSta(() =>
            {
                var sdvoeClient = new MockSdvoeClient();
                var vm = new MainViewModel(new Configuration.ConfigService(), sdvoeClient, new MockRtpReceiver(),
                    new MulticastIpManager(), new RoutingOrchestrator(sdvoeClient));
                vm.Displays.Add(MultiViewDisplay("quad_1_3", fill: true));
                vm.Displays.Add(new DisplayCardViewModel(Rx("74fe488b07bb")));

                Layout(new VisualRoutingView { DataContext = vm });
            });

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is T match) return match;
                var nested = FindDescendant<T>(child);
                if (nested != null) return nested;
            }
            return null;
        }

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
                    var dropHere = FindTextBlock(view, "Drop Source Here to Route");
                    Assert.True(dropHere == null || !dropHere.IsVisible, "unrouted placeholder must be hidden for an external layout");
                }
                finally
                {
                    window.Close();
                }
            });

        private static System.Windows.Controls.TextBlock? FindTextBlock(DependencyObject root, string startsWith)
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is System.Windows.Controls.TextBlock tb && tb.Text.StartsWith(startsWith, StringComparison.Ordinal)) return tb;
                var nested = FindTextBlock(child, startsWith);
                if (nested != null) return nested;
            }
            return null;
        }

        [Fact]
        public void VisualRoutingView_WithARestoredExternalLayoutAndARestoredMultiView_RendersWithoutCrash() =>
            RunSta(() =>
            {
                var sdvoeClient = new MockSdvoeClient();
                var vm = new MainViewModel(new Configuration.ConfigService(), sdvoeClient, new MockRtpReceiver(),
                    new MulticastIpManager(), new RoutingOrchestrator(sdvoeClient));
                var external = new DisplayCardViewModel(Rx());
                external.SetExternalLayout("", new[] { "TX2" }, "OUT 3840x2160@60 (running)");
                vm.Displays.Add(external);
                vm.Displays.Add(MultiViewDisplay("matrix2x2", fill: true));

                Layout(new VisualRoutingView { DataContext = vm });
            });

        [Fact]
        public void SourceCardView_RendersWithoutBindingCrash() =>
            RunSta(() => Layout(new SourceCardView
            {
                DataContext = new SourceCardViewModel(new AvasDevice { DeviceId = "74fe488b07bb", DeviceName = "TX2", VendorId = 105, IsTransmitter = true, IsActive = true })
            }));

        [Fact]
        public void VisualRoutingView_WithSourcesAndDisplays_RendersWithAdaptiveCardPanelWithoutCrash() =>
            RunSta(() =>
            {
                var configService = new Configuration.ConfigService();
                var sdvoeClient = new MockSdvoeClient();
                var rtpReceiver = new MockRtpReceiver();
                var ipManager = new MulticastIpManager();
                var orchestrator = new RoutingOrchestrator(sdvoeClient);
                var vm = new MainViewModel(configService, sdvoeClient, rtpReceiver, ipManager, orchestrator);

                vm.Sources.Add(new SourceCardViewModel(new AvasDevice
                {
                    DeviceId = "74fe488b07bb", DeviceName = "TX2", VendorId = 105, IsTransmitter = true, IsActive = true
                }));
                vm.Sources.Add(new SourceCardViewModel(new AvasDevice
                {
                    DeviceId = "74fe488b07bc", DeviceName = "TX1", VendorId = 105, IsTransmitter = true, IsActive = true
                }));
                vm.Displays.Add(new DisplayCardViewModel(Rx("74fe488b07bb")));
                vm.Displays.Add(new DisplayCardViewModel(Rx("")));

                var view = new VisualRoutingView { DataContext = vm };
                Layout(view);
            });

        // ---- MultiView Composer (Task 10a) ----
        // NOTE: test names here must not contain the substring "Live" (the shared filter FullyQualifiedName!~Live).

        /// <summary>Runs an async view-model call to completion on this STA thread, pumping the dispatcher meanwhile.</summary>
        private static void Pump(Func<Task> work)
        {
            var task = work();
            if (!task.IsCompleted)
            {
                var frame = new DispatcherFrame();
                task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
                Dispatcher.PushFrame(frame);
            }
            task.GetAwaiter().GetResult();
        }

        private static void Idle()
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        }

        /// <summary>Shows the view in a 1920x1080 window, keeps it open while <paramref name="assert"/> runs (IsVisible needs it).</summary>
        private static void Showing(FrameworkElement view, Action assert)
        {
            var window = new Window { Content = view, Width = 1920, Height = 1080, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -10000 };
            window.Show();
            try
            {
                view.UpdateLayout();
                Idle();
                assert();
            }
            finally
            {
                window.Close();
            }
        }

        private static void Relayout(FrameworkElement view)
        {
            view.UpdateLayout();
            Idle();
            view.UpdateLayout();
        }

        private static List<T> FindAll<T>(DependencyObject root) where T : DependencyObject
        {
            var found = new List<T>();
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is T match) found.Add(match);
                found.AddRange(FindAll<T>(child));
            }
            return found;
        }

        private static List<FrameworkElement> Named(DependencyObject root, string name) =>
            FindAll<FrameworkElement>(root).Where(e => e.Name == name).ToList();

        private static List<TextBlock> VisibleTexts(DependencyObject root, string startsWith) =>
            FindAll<TextBlock>(root).Where(t => t.IsVisible && t.Text.StartsWith(startsWith, StringComparison.Ordinal)).ToList();

        private static void AssertVisibleText(DependencyObject root, string startsWith) =>
            Assert.True(VisibleTexts(root, startsWith).Count > 0, $"no visible text starting with '{startsWith}'");

        private static WriteableBitmap Picture() => new(64, 36, 96, 96, PixelFormats.Bgr24, null);

        private sealed class ComposerRig
        {
            public MockSdvoeClient Client { get; } = new();
            public MultiViewOrchestrator Mv { get; }
            public DisplayCardViewModel Rx { get; }
            public SourceCardViewModel Tx1 { get; } = Src("74fe488b07bb", "TX1", 105);
            public SourceCardViewModel Tx2 { get; } = Src("74fe488b07bc", "TX2", 105);
            public SourceCardViewModel Vega { get; } = Src("0016c0589c1e", "VEGA_1100", 192);
            public List<SourceCardViewModel> Sources { get; }

            public ComposerRig()
            {
                Mv = new MultiViewOrchestrator(Client);
                Rx = new DisplayCardViewModel(new AvasDevice { DeviceId = "74fe483c059e", DeviceName = "RX1_c059e_SFPA", IsReceiver = true, IsActive = true });
                Sources = new List<SourceCardViewModel> { Tx1, Tx2, Vega };
            }

            public MultiViewComposerViewModel Composer(string? layoutId = null, Func<bool>? animationsEnabled = null)
            {
                var c = new MultiViewComposerViewModel(Rx, Mv, () => Sources, LayoutItemViewModel.CreateCatalog(), null, null, animationsEnabled);
                if (layoutId != null) Pump(() => c.SelectLayoutAsync(layoutId));
                return c;
            }

            public MultiViewComposerView View(MultiViewComposerViewModel c) => new() { DataContext = c, Sources = Sources };

            private static SourceCardViewModel Src(string mac, string name, int vendor) =>
                new(new AvasDevice { DeviceId = mac, DeviceName = name, VendorId = vendor, IsTransmitter = true, IsActive = true });
        }

        [Fact]
        public void Composer_Step1_RendersFourSchematicTiles() =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var view = rig.View(rig.Composer());

                Showing(view, () =>
                {
                    var schematics = FindAll<LayoutSchematic>(view).Where(s => s.IsVisible).ToList();
                    Assert.Equal(new[] { "matrix2x2", "pip", "side_by_side", "quad_1_3" }, schematics.Select(s => s.LayoutId));
                    Assert.Equal(new[] { 4, 2, 2, 4 }, schematics.Select(s => s.CellCount));
                    Assert.All(schematics, s => Assert.True(s.ActualWidth >= 100 && s.ActualHeight >= 56, $"schematic {s.ActualWidth}x{s.ActualHeight}"));

                    var tiles = Named(view, "LayoutTile").Where(t => t.IsVisible).ToList();
                    Assert.Equal(4, tiles.Count);
                    Assert.All(tiles, t => Assert.True(t.ActualWidth >= 200 && t.ActualHeight >= 96, $"tile {t.ActualWidth}x{t.ActualHeight}"));
                    AssertVisibleText(view, "Step 1");
                });
            });

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
                    Assert.Contains(FindAll<TextBlock>(clear[0]), t => t.Text == "✕");

                    ((Button)clear[0]).Command.Execute(null);
                    Assert.Empty(c.Draft!.Placements);
                });
            });

        [Fact]
        public void SourceCard_ArmedRing_AndUsedInBlocksBadge_Render() =>
            RunSta(() =>
            {
                AvasDevice Dev() => new() { DeviceId = "74fe488b07bb", DeviceName = "TX2", VendorId = 105, IsTransmitter = true, IsActive = true };

                var armed = new SourceCardView { DataContext = new SourceCardViewModel(Dev()) { IsArmed = true, UsedInBlocks = 2 } };
                Showing(armed, () =>
                {
                    Assert.True(Named(armed, "ArmedRing").Single().IsVisible, "armed ring is not shown");
                    AssertVisibleText(armed, "Placing");
                    AssertVisibleText(armed, "in 2 blocks");
                });

                var one = new SourceCardView { DataContext = new SourceCardViewModel(Dev()) { UsedInBlocks = 1 } };
                Showing(one, () => AssertVisibleText(one, "in 1 block"));

                var plain = new SourceCardView { DataContext = new SourceCardViewModel(Dev()) };
                Showing(plain, () =>
                {
                    Assert.False(Named(plain, "ArmedRing").Single().IsVisible);
                    Assert.False(Named(plain, "UsedInBlocksBadge").Single().IsVisible);
                    Assert.Empty(VisibleTexts(plain, "Placing"));
                });
            });

        [Fact]
        public void VisualRoutingView_SwapsDisplaysForComposerWhenActive() =>
            RunSta(() =>
            {
                var client = new MockSdvoeClient();
                var vm = new MainViewModel(new Configuration.ConfigService(), client, new MockRtpReceiver(),
                    new MulticastIpManager(), new RoutingOrchestrator(client), new MultiViewOrchestrator(client), post: a => a());
                var tx = new SourceCardViewModel(new AvasDevice { DeviceId = "74fe488b07bb", DeviceName = "TX1", VendorId = 105, IsTransmitter = true, IsActive = true });
                var rx = new DisplayCardViewModel(Rx());
                vm.Sources.Add(tx);
                vm.Displays.Add(rx);
                var view = new VisualRoutingView { DataContext = vm };

                Showing(view, () =>
                {
                    var composerView = FindAll<MultiViewComposerView>(view).Single();
                    Assert.False(composerView.IsVisible);
                    Assert.True(FindAll<DisplayCardView>(view).Single().IsVisible);

                    // Overview: the arm banner is the only "Placing" line.
                    vm.ArmSource(tx);
                    Relayout(view);
                    Assert.Single(VisibleTexts(view, "Placing TX1 - tap a display or a block"));

                    Pump(() => vm.OpenComposerAsync(rx, "matrix2x2"));
                    Relayout(view);

                    Assert.True(composerView.IsVisible, "composer is not shown");
                    Assert.Same(vm.ActiveComposer, composerView.DataContext);
                    Assert.False(FindAll<DisplayCardView>(view).Single().IsVisible, "displays row still shown");
                    Assert.Single(VisibleTexts(view, "Placing TX1 - ")); // the Composer's own hint, not the overview banner
                    Assert.Empty(VisibleTexts(view, "Placing TX1 - tap a display or a block"));

                    var close = (Button)Named(composerView, "CloseComposerButton").Single();
                    Assert.True(close.ActualHeight >= 56, $"close is {close.ActualHeight} high");
                    close.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    Relayout(view);

                    Assert.Null(vm.ActiveComposer);
                    Assert.False(composerView.IsVisible);
                    Assert.True(FindAll<DisplayCardView>(view).Single().IsVisible);
                });
            });

        // ---- Task 10a fix round 1 ----

        /// <summary>Lays the view out at exactly <paramref name="width"/> x <paramref name="height"/> (the content area of a real window).</summary>
        private static void ShowingAt(FrameworkElement view, double width, double height, Action assert)
        {
            view.Width = width;
            view.Height = height;
            var window = new Window { Content = view, SizeToContent = SizeToContent.WidthAndHeight, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Left = -10000 };
            window.Show();
            try
            {
                Relayout(view);
                assert();
            }
            finally
            {
                window.Close();
            }
        }

        private static Rect BoundsIn(FrameworkElement el, Visual ancestor) =>
            el.TransformToAncestor(ancestor).TransformBounds(new Rect(el.RenderSize));

        private static void Wait(TimeSpan time)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = time };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

        private static void Tap(UIElement el, RoutedEvent routedEvent) =>
            el.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = routedEvent, Source = el });

        private static void Click(FrameworkElement button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        private static (MainViewModel Vm, DisplayCardViewModel Rx, SourceCardViewModel[] Sources) Overview(Func<DateTime>? clock = null)
        {
            var client = new MockSdvoeClient();
            var vm = new MainViewModel(new Configuration.ConfigService(), client, new MockRtpReceiver(), new MulticastIpManager(),
                new RoutingOrchestrator(client), new MultiViewOrchestrator(client), clock, post: a => a());
            var sources = new[]
            {
                new SourceCardViewModel(new AvasDevice { DeviceId = "74fe488b07bb", DeviceName = "TX1_c07bb_SFPA", VendorId = 105, IsTransmitter = true, IsActive = true }) { PreviewBitmap = Picture() },
                new SourceCardViewModel(new AvasDevice { DeviceId = "74fe488b07bc", DeviceName = "TX2_DUAL LINK_SFPA", VendorId = 105, LinkMode = "Dual Link", IsTransmitter = true, IsActive = true }) { PreviewBitmap = Picture() },
                new SourceCardViewModel(new AvasDevice { DeviceId = "0016c0589c1e", DeviceName = "VEGA_1100", VendorId = 192, IsTransmitter = true, IsActive = true }),
                new SourceCardViewModel(new AvasDevice { DeviceId = "74fe488b07bd", DeviceName = "TX3_c07bd_SFPA", VendorId = 105, IsTransmitter = true, IsActive = false }),
            };
            foreach (var s in sources) vm.Sources.Add(s);
            var rx = new DisplayCardViewModel(new AvasDevice { DeviceId = "74fe483c059e", DeviceName = "RX1_c059e_SFPA", IsReceiver = true, IsActive = true });
            vm.Displays.Add(rx);
            return (vm, rx, sources);
        }

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

        /// <summary>
        /// A ScrollViewer with the theme's implicit style, set explicitly: implicit app-level styles are not looked up
        /// from this test thread (it is not the thread that created the Application).
        /// </summary>
        private static ScrollViewer ThemedScroll(double contentHeight) => new()
        {
            Style = (Style)Application.Current.FindResource(typeof(ScrollViewer)),
            Height = 200, Width = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new Border { Height = contentHeight },
        };

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

        private static double Luminance(Color c)
        {
            static double Channel(byte v)
            {
                double s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }

        private static double Contrast(Color a, Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

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
                    Assert.True(undo.IsVisible && undo.ActualHeight >= 56);
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
        public void SourceCard_Tap_ArmsAndDisarms_OfflineIsIgnored() =>
            RunSta(() =>
            {
                var (vm, _, sources) = Overview();
                foreach (var compact in new[] { false, true })
                {
                    UserControl Card(SourceCardViewModel s) => compact ? new CompactSourceCardView { DataContext = s } : new SourceCardView { DataContext = s };
                    var online = Card(sources[0]);
                    var offline = Card(sources[3]);
                    var host = new StackPanel { DataContext = vm, Children = { online, offline } };

                    Showing(host, () =>
                    {
                        Tap(online, UIElement.PreviewMouseLeftButtonDownEvent);
                        Tap(online, UIElement.MouseLeftButtonUpEvent);
                        Assert.Same(sources[0], vm.ArmedSource);

                        Tap(online, UIElement.PreviewMouseLeftButtonDownEvent);
                        Tap(online, UIElement.MouseLeftButtonUpEvent);
                        Assert.Null(vm.ArmedSource);

                        Tap(offline, UIElement.PreviewMouseLeftButtonDownEvent);
                        Tap(offline, UIElement.MouseLeftButtonUpEvent);
                        Assert.Null(vm.ArmedSource);

                        Tap(online, UIElement.MouseLeftButtonUpEvent); // a release without a press here is no tap
                        Assert.Null(vm.ArmedSource);
                    });
                }
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
        public void VisualRoutingView_TickTimer_RunsWhileLoaded_AndCallsTick() =>
            RunSta(() =>
            {
                var now = new DateTime(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc);
                var (vm, _, sources) = Overview(() => now);
                vm.ArmSource(sources[0]);
                now += MainViewModel.ArmIdleTimeout + TimeSpan.FromSeconds(1);

                var view = new VisualRoutingView { DataContext = vm };
                Assert.False(view.IsTimerRunning);
                var window = new Window { Content = view, Width = 1280, Height = 720, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -10000 };
                window.Show();
                Idle();
                Assert.True(view.IsTimerRunning);
                // Poll with a deadline (the timer runs every 250 ms; a busy machine may be slower).
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
                while (vm.ArmedSource != null && DateTime.UtcNow < deadline) Wait(TimeSpan.FromMilliseconds(50));
                Assert.Null(vm.ArmedSource); // Tick disarmed the idle source
                window.Close();
                Idle();
                Assert.False(view.IsTimerRunning);
            });

        [Fact]
        public void VisualRoutingView_StopButton_Disarms() =>
            RunSta(() =>
            {
                var (vm, _, sources) = Overview();
                vm.ArmSource(sources[1]);
                var view = new VisualRoutingView { DataContext = vm };
                Showing(view, () =>
                {
                    var stop = Named(view, "StopPlacingButton").Single();
                    Assert.True(stop.IsVisible && stop.ActualHeight >= 56);
                    Click(stop);
                    Assert.Null(vm.ArmedSource);
                    Relayout(view);
                    Assert.False(stop.IsVisible);
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
                    Assert.All(chips, b => Assert.True(b.ActualHeight >= 56));
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
                    Assert.True(replace.ActualHeight >= 56);
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

        // ---- Task 10b: the display card (Layout menu, drop choice, draft chip, Copy layout, copy target states, Undo toast) ----

        private sealed class CopyRig
        {
            public const string Rx1Mac = "74fe483c059e";
            public const string PaxMac = "74fe4800355a";
            public const string Rx3Mac = "74fe48000003";
            public const string Tx2Mac = "74fe488b07bb";
            public const string VegaMac = "74fe48000001";

            public MockSdvoeClient Client { get; } = new();
            public MainViewModel Vm { get; }
            public DisplayCardViewModel Rx1 { get; }
            public DisplayCardViewModel Pax { get; }
            public DisplayCardViewModel Rx3 { get; }

            public SourceCardViewModel Tx2 { get; } = new(new AvasDevice { DeviceId = Tx2Mac, DeviceName = "TX2_DUAL LINK_SFPA", VendorId = 105, LinkMode = "Dual Link", IsTransmitter = true, IsActive = true }) { PreviewBitmap = Picture() };
            public SourceCardViewModel Vega { get; } = new(new AvasDevice { DeviceId = VegaMac, DeviceName = "VEGA_1100", VendorId = 192, IsTransmitter = true, IsActive = true });

            /// <summary>RX1 (4K) runs a Matrix 2x2 this tool made (TX2 in blocks 1 and 3, VEGA in 2: "3/4"); PAX355S is 4K; RX3 is 1080p and unrouted.</summary>
            public CopyRig(Func<bool>? animationsEnabled = null)
            {
                // Click handlers are async: their continuations come back to this STA thread.
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                Vm = new MainViewModel(new Configuration.ConfigService(), Client, new MockRtpReceiver(), new MulticastIpManager(),
                    new RoutingOrchestrator(Client), new MultiViewOrchestrator(Client), post: a => a(), animationsEnabled: animationsEnabled);
                Rx1 = Card(Rx1Mac, "RX1_c059e_SFPA");
                Pax = Card(PaxMac, "PAX355S_SFP A");
                Rx3 = Card(Rx3Mac, "RX3_1080P_SFPA");
                Vm.Sources.Add(Tx2);
                Vm.Sources.Add(Vega);
                Client.DisplayEdids[Rx1Mac] = TestEdids.ViewSonic4k;
                Client.DisplayEdids[PaxMac] = TestEdids.Pax355s;
                Client.DisplayEdids[Rx3Mac] = TestEdids.Build((1920, 1080, 60, false));

                Client.ReceiverStates[Rx1Mac] = new RxState(Rx1Mac, "RX1_c059e_SFPA", "MULTIVIEW", 3840, 2160, 60,
                    new[]
                    {
                        new RxSubscription(1, true, "STREAMING", Tx2Mac, "TX2_DUAL LINK_SFPA", 1),
                        new RxSubscription(2, true, "STREAMING", VegaMac, "VEGA_1100", 1),
                    },
                    new RxLayout($"avas_{Rx1Mac}_matrix2x2_3840x2160", 3840, 2160,
                        new[] { new RxWindow(0, 1), new RxWindow(1, 2), new RxWindow(2, 1) }));
                Pump(() => Vm.RestoreDisplayAsync(Rx1));
            }

            private DisplayCardViewModel Card(string mac, string name)
            {
                var d = new DisplayCardViewModel(new AvasDevice { DeviceId = mac, DeviceName = name, IsReceiver = true, IsActive = true }, Vm.UnrouteVideoAsync);
                Vm.Displays.Add(d);
                return d;
            }

            /// <summary>A display card hosted as the overview hosts it (the MainViewModel above it), at a real card size.</summary>
            public DisplayCardView View(DisplayCardViewModel d, out FrameworkElement host, double width = 420, double height = 400)
            {
                var view = new DisplayCardView { DataContext = d, Width = width, Height = height };
                host = new Border { DataContext = Vm, Child = view, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
                return view;
            }
        }

        /// <summary>Pumps the dispatcher until the condition holds (async click handlers), with a deadline.</summary>
        private static void Until(Func<bool> condition, string what)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!condition() && DateTime.UtcNow < deadline) Wait(TimeSpan.FromMilliseconds(20));
            Assert.True(condition(), what);
        }

        /// <summary>
        /// A press and release on the card, the release on <paramref name="releasedOn"/>: raised as the routed mouse
        /// events (tunnel, then bubble), from which WPF derives the direct left-button events on each element of the
        /// route, as with real input.
        /// </summary>
        private static void TapCard(DisplayCardView card, UIElement? releasedOn = null)
        {
            Tap(card, Mouse.PreviewMouseDownEvent);
            Tap(releasedOn ?? card, Mouse.PreviewMouseUpEvent);
            Tap(releasedOn ?? card, Mouse.MouseUpEvent);
        }

        /// <summary>The text is shown whole: its natural size fits the space it got (nothing clipped, no ellipsis).</summary>
        private static void AssertTextFits(TextBlock label)
        {
            var probe = new TextBlock
            {
                Text = label.Text, FontSize = label.FontSize, FontWeight = label.FontWeight, FontFamily = label.FontFamily,
                FontStyle = label.FontStyle, TextWrapping = label.TextWrapping,
            };
            probe.Measure(new Size(label.TextWrapping == TextWrapping.NoWrap ? double.PositiveInfinity : label.ActualWidth, double.PositiveInfinity));
            Assert.True(probe.DesiredSize.Width <= label.ActualWidth + 0.5, $"'{label.Text}' needs {probe.DesiredSize.Width:F1} px, has {label.ActualWidth:F1}");
            Assert.True(probe.DesiredSize.Height <= label.ActualHeight + 0.5, $"'{label.Text}' needs {probe.DesiredSize.Height:F1} px high, has {label.ActualHeight:F1}");
        }

        private static void AssertInside(FrameworkElement el, FrameworkElement container)
        {
            var r = BoundsIn(el, container);
            Assert.True(r.Left >= -0.5 && r.Top >= -0.5 && r.Right <= container.ActualWidth + 0.5 && r.Bottom <= container.ActualHeight + 0.5,
                $"{el.Name} at {r} is outside {container.Name} ({container.ActualWidth:F1}x{container.ActualHeight:F1})");
        }

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
        public void DisplayCard_LayoutMenu_OpensWithSixSchematicTiles_AndHeaderButtonIsAtLeast56High() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host);
                Showing(host, () =>
                {
                    var button = (Button)Named(view, "LayoutButton").Single();
                    Assert.True(button.IsVisible && button.ActualHeight >= 56, $"Layout button is {button.ActualHeight} high");
                    Assert.Contains(FindAll<TextBlock>(button), t => t.Text.StartsWith("Layout", StringComparison.Ordinal));
                    Assert.True(BoundsIn(button, view).Top < 80, "the Layout button is not in the card header");

                    Click(button);
                    Assert.True(rig.Rx1.IsLayoutMenuOpen);
                    var popup = (Popup)Named(view, "LayoutMenuPopup").Single();
                    Assert.True(popup.IsOpen);
                    var menu = (FrameworkElement)popup.Child;
                    Relayout(menu);

                    var tiles = Named(menu, "LayoutMenuTile").Cast<Button>().ToList();
                    Assert.Equal(new[] { "single", "matrix2x2", "pip", "side_by_side", "quad_1_3", "off" },
                        tiles.Select(t => ((LayoutItemViewModel)t.DataContext).Id));
                    Assert.All(tiles, t => Assert.True(t.IsVisible && t.ActualHeight >= 56 && t.ActualWidth >= 56, $"tile {t.ActualWidth}x{t.ActualHeight}"));
                    Assert.Equal(new[] { 1, 4, 2, 2, 4, 0 }, tiles.Select(t => FindAll<LayoutSchematic>(t).Single().CellCount));
                    Assert.Equal(new[] { "Single View", "Matrix 2x2", "PIP", "Side-by-Side", "1+3 Quad", "Off" },
                        tiles.Select(t => FindAll<TextBlock>(t).First(tb => tb.Name == "LayoutMenuTitle").Text));

                    // The layout on screen is marked, in words (never colour only).
                    var marked = tiles.Where(t => VisibleTexts(t, "✓ On screen").Count > 0).ToList();
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
                    Assert.True(chip.IsVisible && chip.ActualHeight >= 56, $"draft chip is {chip.ActualHeight} high");
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
        public void DisplayCard_CopyHandle_IsAtLeast56High_AndDisabledTextShowsWhenNothingToCopy() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var rx1 = rig.View(rig.Rx1, out var h1);
                var rx3 = rig.View(rig.Rx3, out var h3);
                var host = new StackPanel { Children = { h1, h3 } };
                Showing(host, () =>
                {
                    var handle = (Button)Named(rx1, "CopyLayoutHandle").Single();
                    Assert.True(handle.IsVisible && handle.IsEnabled && handle.ActualHeight >= 56, $"handle is {handle.ActualHeight} high");
                    Assert.Contains(FindAll<TextBlock>(handle), t => t.Text == "Copy layout");
                    Assert.Empty(VisibleTexts(rx1, "RX1_c059e_SFPA shows nothing to copy"));

                    // RX3 shows nothing: the handle is disabled and the card says why in words.
                    var blocked = (Button)Named(rx3, "CopyLayoutHandle").Single();
                    Assert.False(blocked.IsEnabled);
                    Assert.True(blocked.ActualHeight >= 56);
                    AssertVisibleText(rx3, "RX3_1080P_SFPA shows nothing to copy");

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
        public void VisualRoutingView_CopyArmBanner_OneBannerAtATime_StopDisarms() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = new VisualRoutingView { DataContext = rig.Vm };
                Showing(view, () =>
                {
                    Pump(() => rig.Vm.ArmDisplayAsync(rig.Rx1));
                    Relayout(view);
                    Assert.NotNull(rig.Vm.CopyArmBanner);
                    Assert.Equal(rig.Vm.CopyArmBanner, Assert.Single(VisibleTexts(view, "Copying RX1_c059e_SFPA - ")).Text);
                    Assert.Empty(VisibleTexts(view, "Placing "));
                    var stop = (Button)Named(view, "StopPlacingButton").Single();
                    Assert.True(stop.IsVisible && stop.ActualHeight >= 56);

                    // Arming a source replaces the copy arm: still exactly one banner.
                    rig.Vm.ArmSource(rig.Tx2);
                    Relayout(view);
                    Assert.Empty(VisibleTexts(view, "Copying "));
                    Assert.Single(VisibleTexts(view, "Placing TX2_DUAL LINK_SFPA - "));

                    Pump(() => rig.Vm.ArmDisplayAsync(rig.Rx1));
                    Relayout(view);
                    Click(stop);
                    Assert.Null(rig.Vm.ArmedDisplay);
                    Relayout(view);
                    Assert.False(stop.IsVisible);
                    Assert.Empty(VisibleTexts(view, "Copying "));
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

        [Fact]
        public void LayoutsBarView_ShowsSixSchematicTiles_InTheBarOrder_AtTheMinimumWidth() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var bar = new LayoutsBarView { DataContext = rig.Vm };
                ShowingAt(bar, 1008, 110, () =>
                {
                    var tiles = Named(bar, "LayoutTile").ToList();
                    Assert.Equal(new[] { "off", "single", "matrix2x2", "pip", "side_by_side", "quad_1_3" },
                        tiles.Select(t => ((LayoutItemViewModel)t.DataContext).Id));
                    Assert.Equal(new[] { 0, 1, 4, 2, 2, 4 }, tiles.Select(t => FindAll<LayoutSchematic>(t).Single().CellCount));
                    foreach (var t in tiles)
                    {
                        Assert.True(t.IsVisible);
                        AssertInside(t, bar);
                        AssertTextFits(FindAll<TextBlock>(t).Single(tb => tb.Name == "LayoutTileTitle"));
                    }
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
                        AssertTextFits((TextBlock)Named(card, "DeviceNameText").Single());
                        AssertTextFits((TextBlock)Named(card, "ModeBadgeText").Single());
                        foreach (var name in new[] { "LayoutButton", "CopyLayoutHandle" })
                        {
                            var b = (Button)Named(card, name).Single();
                            Assert.True(b.ActualHeight >= 56, $"{name} is {b.ActualHeight} high");
                            AssertInside(b, frame);
                            foreach (var t in FindAll<TextBlock>(b)) AssertTextFits(t);
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
                        Assert.True(b.IsVisible && b.ActualHeight >= 56);
                        AssertInside(b, choice);
                        foreach (var t in FindAll<TextBlock>(b)) AssertTextFits(t);
                    }
                    AssertTextFits((TextBlock)Named(rx1, "DropChoiceTitle").Single());
                    var chip = (FrameworkElement)Named(rx1, "DraftChip").Single();
                    Assert.True(chip.IsVisible && chip.ActualHeight >= 56);
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
                    AssertTextFits((TextBlock)Named(rx3, "CopyFromBlockedLabel").Single());

                    // Copying / choosing: the layout bar steps aside (Ruling R22); the first row's Layout and Copy layout
                    // buttons are in view without scrolling (they sit at the top of the card).
                    Assert.False(FindAll<LayoutsBarView>(view).Single().IsVisible);
                    var scroll = (ScrollViewer)Named(view, "DisplaysScroll").Single();
                    // The displays hold drag sources (Copy layout, drops): touch never pans them (the 16 px bar does).
                    Assert.Equal(PanningMode.None, scroll.PanningMode);
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

                    // Back to the plain overview: the layout bar is whole again.
                    rig.Rx1.CloseDropChoice();
                    rig.Vm.DisarmDisplay();
                    Relayout(view);
                    Assert.False(rig.Vm.IsCopyOrChoiceActive);
                    AssertInside(FindAll<LayoutsBarView>(view).Single(), view);

                    // ... and PAX's Undo toast is back, inside its picture, whole.
                    Assert.True(toast.IsVisible, "the Undo toast did not come back");
                    AssertInside(toast, (FrameworkElement)Named(pax, "CardFrame").Single());
                    var tb = BoundsIn(toast, view);
                    var pb = BoundsIn(paxPreview, view);
                    Assert.True(tb.Top >= pb.Top - 0.5 && tb.Bottom <= pb.Bottom + 0.5, $"the toast {tb} leaves the picture {pb}");
                    AssertTextFits((TextBlock)Named(pax, "UndoToastLabel").Single());
                    AssertInside((FrameworkElement)Named(pax, "UndoScreenButton").Single(), toast);
                });
            });

        // ---- Task 10b fix round 1 ----


        private static void AssertReadable(FrameworkElement el, ScrollViewer scroll, string what)
        {
            Assert.True(el.IsVisible, $"{what} is not shown");
            var r = BoundsIn(el, scroll);
            Assert.True(r.Top >= -0.5 && r.Left >= -0.5 && r.Bottom <= scroll.ViewportHeight + 0.5 && r.Right <= scroll.ViewportWidth + 0.5,
                $"{what} at {r}; the displays show {scroll.ViewportWidth:F0}x{scroll.ViewportHeight:F0}");
        }

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
                    Assert.True(FindAll<LayoutsBarView>(view).Single().IsVisible);

                    // Copy armed (with the banner): the sources shrink to the compact palette, the layout bar steps aside.
                    Pump(() => rig.Vm.ArmDisplayAsync(rig.Rx1));
                    Relayout(view);
                    AssertVisibleText(view, "Copying RX1_c059e_SFPA - ");
                    Assert.True(Named(view, "CompactSourcePalette").Single().IsVisible);
                    Assert.Empty(FindAll<SourceCardView>(view).Where(s => s.IsVisible));
                    Assert.False(FindAll<LayoutsBarView>(view).Single().IsVisible);

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
                    Assert.False(FindAll<LayoutsBarView>(view).Single().IsVisible);
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
                    AssertVisibleText(single, "✓ On screen");
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

        // ---- Task 10b fix round 2 ----

        /// <summary>The deepest element at <paramref name="point"/> (in <paramref name="from"/>'s coordinates) lies inside <paramref name="expected"/>.</summary>
        private static bool HitsInside(FrameworkElement from, Point point, DependencyObject expected)
        {
            var window = Window.GetWindow(from)!;
            var hit = window.InputHitTest(from.TranslatePoint(point, window)) as DependencyObject;
            for (var d = hit; d != null; d = VisualTreeHelper.GetParent(d))
            {
                if (ReferenceEquals(d, expected)) return true;
            }
            return false;
        }

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

        private static void AssertSixteenByNine(FrameworkElement picture, string what)
        {
            double aspect = picture.ActualWidth / picture.ActualHeight;
            Assert.True(aspect >= 16.0 / 9 * 0.96 && aspect <= 16.0 / 9 * 1.04, $"{what}: picture {picture.ActualWidth:F1}x{picture.ActualHeight:F1}");
        }

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
                    Assert.True(chip.IsVisible && chip.ActualHeight >= 56, $"draft chip is {chip.ActualHeight} high");
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
                    AssertTextFits((TextBlock)parts[0].El);
                    AssertTextFits((TextBlock)parts[1].El);
                    AssertTextFits(FindAll<TextBlock>(parts[2].El).Single(t => t.Text.StartsWith("Layout", StringComparison.Ordinal)));
                    AssertTextFits(FindAll<TextBlock>(parts[3].El).Single(t => t.Text.StartsWith("Copy", StringComparison.Ordinal)));
                    for (int i = 2; i < parts.Count; i++)
                    {
                        for (int j = i + 1; j < parts.Count; j++)
                        {
                            Assert.False(BoundsIn(parts[i].El, rx1).IntersectsWith(BoundsIn(parts[j].El, rx1)), $"{parts[i].Name} and {parts[j].Name} overlap");
                        }
                        Assert.True(parts[i].El.ActualHeight >= 56, $"{parts[i].Name} is {parts[i].El.ActualHeight} high");
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
                    Assert.True(chip.ActualHeight >= 56);
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
                    AssertTextFits(FindAll<TextBlock>(tag).Single());
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

        // ---- Task 11: keyboard path, automation names, reduced motion ----

        /// <summary>What the element draws now. A visual brush, so the picture does not depend on where the element lies in its parent.</summary>
        private static byte[] Pixels(FrameworkElement el)
        {
            int w = Math.Max(1, (int)Math.Ceiling(el.ActualWidth)), h = Math.Max(1, (int)Math.Ceiling(el.ActualHeight));
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) dc.DrawRectangle(new VisualBrush(el), null, new Rect(0, 0, w, h));
            var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var buffer = new byte[w * h * 4];
            bitmap.CopyPixels(buffer, w * 4, 0);
            return buffer;
        }

        /// <summary>
        /// Giving <paramref name="control"/> keyboard focus changes what <paramref name="paint"/> draws: a focus ring the
        /// operator can see. (Adorner rings do not render here; the controls draw theirs themselves.)
        /// </summary>
        private static void AssertShowsFocus(FrameworkElement control, FrameworkElement paint, string what)
        {
            InputModality.IsKeyboard = true; // rings are the keyboard user's
            Keyboard.ClearFocus();
            paint.UpdateLayout();
            byte[] before = Pixels(paint);
            Assert.True(control.Focus() && control.IsKeyboardFocused, $"{what} did not take keyboard focus");
            paint.UpdateLayout();
            Idle();
            byte[] after = Pixels(paint);
            Assert.False(before.SequenceEqual(after), $"{what} shows no focus ring");
        }

        /// <summary>
        /// The blocks' surfaces in block order. The visual tree lists them in paint order (the panel sorts by Z), so
        /// the list is rebuilt by API window index.
        /// </summary>
        private static List<Button> Surfaces(DependencyObject view) =>
            Named(view, "BlockSurface").Cast<Button>().OrderBy(b => ((ComposerBlockViewModel)b.DataContext).Index).ToList();

        /// <summary>The named element of the block with this API window index.</summary>
        private static FrameworkElement InBlock(DependencyObject view, string name, int index) =>
            Named(view, name).Single(e => ((ComposerBlockViewModel)e.DataContext).Index == index);

        private static T? Ancestor<T>(DependencyObject d) where T : DependencyObject
        {
            for (d = VisualTreeHelper.GetParent(d); d != null; d = VisualTreeHelper.GetParent(d))
                if (d is T found) return found;
            return null;
        }

        /// <summary>A key press on the element: the tunnelling PreviewKeyDown, then the bubbling KeyDown unless something handled the first, as real input does.</summary>
        private static void RaiseKey(UIElement target, Key key)
        {
            var preview = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, 0, key)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
                Source = target,
            };
            target.RaiseEvent(preview);
            if (preview.Handled) return;
            target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, 0, key)
            {
                RoutedEvent = Keyboard.KeyDownEvent,
                Source = target,
            });
        }

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
                        "CloseComposerButton", "LayoutTile", "LayoutTile", "LayoutTile", "LayoutTile",
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
        public void SourceCards_Keyboard_EnterAndSpaceArm_AShowFocusRing_AndHaveNames() =>
            RunSta(() =>
            {
                var (vm, rx, sources) = Overview();
                var full = new SourceCardView { DataContext = sources[0], Width = 340, Height = 260 };
                var compact = new CompactSourceCardView { DataContext = sources[1] };
                var offline = new SourceCardView { DataContext = sources[3], Width = 340, Height = 260 };
                var host = new StackPanel { DataContext = vm, Children = { full, compact, offline } };
                Showing(host, () =>
                {
                    Assert.All(new FrameworkElement[] { full, compact }, card => Assert.True(card.Focusable && ((Control)card).IsTabStop));

                    Assert.True(full.Focus() && full.IsKeyboardFocused);
                    RaiseKey(full, Key.Enter);
                    Assert.Same(sources[0], vm.ArmedSource);
                    RaiseKey(full, Key.Space); // arms again = stops
                    Assert.Null(vm.ArmedSource);

                    Assert.True(compact.Focus());
                    RaiseKey(compact, Key.Space);
                    Assert.Same(sources[1], vm.ArmedSource);
                    RaiseKey(compact, Key.Enter);
                    Assert.Null(vm.ArmedSource);

                    Assert.True(offline.Focus());
                    RaiseKey(offline, Key.Enter); // an offline source cannot be placed, as it cannot be dragged
                    Assert.Null(vm.ArmedSource);

                    AssertShowsFocus(full, full, "a source card");
                    AssertShowsFocus(compact, compact, "a compact source card");

                    vm.ArmSource(sources[0]);
                    Relayout(host);
                    Assert.Equal("TX1_c07bb_SFPA, online, placing - press Enter to stop", AutomationProperties.GetName(full));
                    Assert.Equal("TX2_DUAL LINK_SFPA, online", AutomationProperties.GetName(compact));
                    Assert.Equal("TX3_c07bd_SFPA, offline", AutomationProperties.GetName(offline));
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
        public void VisualRoutingView_Esc_StopsWhatIsArmed_AndNeverClosesTheComposer() =>
            RunSta(() =>
            {
                var (vm, rx, sources) = Overview();
                var view = new VisualRoutingView { DataContext = vm };
                Showing(view, () =>
                {
                    vm.ArmSource(sources[0]);
                    Relayout(view);
                    RaiseKey(view, Key.Escape);
                    Relayout(view);
                    Assert.Null(vm.ArmedSource);
                    Assert.Empty(VisibleTexts(view, "Placing TX1"));

                    Pump(() => vm.OpenComposerAsync(rx, "matrix2x2"));
                    Relayout(view);
                    var composer = vm.ActiveComposer!;
                    composer.Arm(sources[0]);
                    RaiseKey(view, Key.Escape);
                    Assert.Null(composer.ArmedSource);
                    Assert.Null(vm.ArmedSource);
                    RaiseKey(view, Key.Escape); // a second Esc: nothing, and the Composer stays open
                    Assert.Same(composer, vm.ActiveComposer);
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

        // ---- Task 11 fix round 1: the keyboard stays in the Composer, key repeat, keyboard-only rings, Undo transition ----

        /// <summary>The next key presses are the keyboard's (rings show). The input mode is process-wide state: each test sets what it needs.</summary>
        private static void KeyboardMode(bool keyboard = true) => InputModality.IsKeyboard = keyboard;

        /// <summary>A key press as the keyboard device delivers a held key: IsRepeat is only settable by the input system, so it is set on the event.</summary>
        private static void RaiseRepeatedKey(UIElement target, Key key)
        {
            KeyEventArgs Make(RoutedEvent routed)
            {
                var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, 0, key) { RoutedEvent = routed, Source = target };
                var field = typeof(KeyEventArgs).GetField("_isRepeat", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("KeyEventArgs has no _isRepeat field: " + string.Join(", ", typeof(KeyEventArgs).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Select(f => f.Name)));
                field.SetValue(args, true);
                Assert.True(args.IsRepeat);
                return args;
            }
            var preview = Make(Keyboard.PreviewKeyDownEvent);
            target.RaiseEvent(preview);
            if (!preview.Handled) target.RaiseEvent(Make(Keyboard.KeyDownEvent));
        }

        /// <summary>The keyboard focus is on an element of this Composer (its tree, or its picker popup).</summary>
        private static bool FocusIsIn(FrameworkElement view)
        {
            var popupChild = Named(view, "PickerPopup").OfType<Popup>().Single().Child;
            for (var d = Keyboard.FocusedElement as DependencyObject; d != null;
                 d = d is Visual ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            {
                if (ReferenceEquals(d, view) || ReferenceEquals(d, popupChild)) return true;
            }
            return false;
        }

        private static string FocusedName() => (Keyboard.FocusedElement as FrameworkElement)?.Name ?? Keyboard.FocusedElement?.GetType().Name ?? "(nothing)";

        /// <summary>A Composer on Matrix 2x2 with TX1 in block 1 (API index 0), shown, the keyboard on that block, its Replace / Move strip open.</summary>
        private static (ComposerRig Rig, MultiViewComposerViewModel C, MultiViewComposerView View) WithStripOpen()
        {
            var rig = new ComposerRig();
            var c = rig.Composer("matrix2x2");
            c.Arm(rig.Tx1);
            c.TapBlock(0);
            c.Disarm();
            return (rig, c, rig.View(c));
        }

        private static void OpenStripByKeyboard(MultiViewComposerView view, MultiViewComposerViewModel c, int index = 0)
        {
            KeyboardMode();
            var surface = Surfaces(view)[index];
            Assert.True(surface.Focus());
            Assert.True(view.HandleKey(Key.Enter, ModifierKeys.None, surface));
            Relayout(view);
            Assert.True(c.Blocks[index].IsSelected);
        }

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
        public void SourceCard_HeldEnterOrSpace_IsOnePress() =>
            RunSta(() =>
            {
                var (vm, rx, sources) = Overview();
                var full = new SourceCardView { DataContext = sources[0], Width = 340, Height = 260 };
                var compact = new CompactSourceCardView { DataContext = sources[1] };
                Showing(new StackPanel { DataContext = vm, Children = { full, compact } }, () =>
                {
                    Assert.True(full.Focus());
                    RaiseKey(full, Key.Enter);
                    Assert.Same(sources[0], vm.ArmedSource);
                    for (int i = 0; i < 5; i++) RaiseRepeatedKey(full, i % 2 == 0 ? Key.Enter : Key.Space); // an odd number: a toggle per repeat would end disarmed
                    Assert.Same(sources[0], vm.ArmedSource); // still armed: the repeats did not toggle it

                    Assert.True(compact.Focus());
                    RaiseKey(compact, Key.Space);
                    Assert.Same(sources[1], vm.ArmedSource);
                    RaiseRepeatedKey(compact, Key.Space);
                    RaiseRepeatedKey(compact, Key.Enter);
                    RaiseRepeatedKey(compact, Key.Space);
                    Assert.Same(sources[1], vm.ArmedSource);
                });
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

        [Theory]
        [InlineData(1264, 755)]
        [InlineData(1008, 615)]
        public void VisualRoutingView_WithASourceArmed_TheSourcesGoCompact_AndTheCardPictureIsInTheDisplaysView(int width, int height) =>
            RunSta(() =>
            {
                var (vm, _, sources) = Overview();
                var view = new VisualRoutingView { DataContext = vm };

                ShowingAt(view, width, height, () =>
                {
                    var scroll = (ScrollViewer)Named(view, "DisplaysScroll").Single();
                    var picture = (FrameworkElement)Named(FindAll<DisplayCardView>(view).Single(), "PreviewSurface").Single();
                    Assert.NotEmpty(FindAll<SourceCardView>(view).Where(s => s.IsVisible));
                    double room = scroll.ViewportHeight;

                    vm.ArmSource(sources[0]);
                    Relayout(view);

                    Assert.True(Named(view, "CompactSourcePalette").Single().IsVisible);
                    Assert.Empty(FindAll<SourceCardView>(view).Where(s => s.IsVisible));
                    Assert.False(FindAll<LayoutsBarView>(view).Single().IsVisible);
                    Assert.True(scroll.ViewportHeight > room, $"the displays have {scroll.ViewportHeight} px armed, {room} px otherwise");
                    AssertReadable(picture, scroll, "the card's picture while a source is armed");

                    vm.ArmSource(sources[0]); // tap again: stopped, the full cards and the layout bar are back
                    Relayout(view);
                    Assert.NotEmpty(FindAll<SourceCardView>(view).Where(s => s.IsVisible));
                    Assert.True(FindAll<LayoutsBarView>(view).Single().IsVisible);
                });
            });

        [Fact]
        public void VisualRoutingView_ArmBanners_AreAnnouncedWhenTheirTextChanges() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = new VisualRoutingView { DataContext = rig.Vm };
                var announced = new List<string>();
                Motion.AnnounceObserver = element => announced.Add(((FrameworkElement)element).Name);
                try
                {
                    Showing(view, () =>
                    {
                        rig.Vm.ArmSource(rig.Tx2);
                        Assert.Equal(new[] { "ArmBannerLabel" }, announced);

                        rig.Vm.ArmSource(rig.Vega); // another source: the text changes again
                        Assert.Equal(new[] { "ArmBannerLabel", "ArmBannerLabel" }, announced);

                        rig.Vm.ArmSource(rig.Vega); // stopped: nothing to read out
                        Assert.Equal(2, announced.Count);

                        announced.Clear();
                        Pump(() => rig.Vm.ArmDisplayAsync(rig.Rx1));
                        Assert.Equal(new[] { "CopyArmBannerLabel" }, announced);
                    });
                }
                finally
                {
                    Motion.AnnounceObserver = null;
                }
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

                    Assert.Equal("Layout for RX1_c059e_SFPA", Name(rx1, "LayoutButton"));
                    Assert.Equal("Layout for PAX355S_SFP A", Name(pax, "LayoutButton"));
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

        /// <summary>The colours the Dark palette gives to chrome: nothing painted with them may survive in Light mode.</summary>
        private static HashSet<Color> DarkChromeColours(params string[] keys)
        {
            var dark = new ResourceDictionary { Source = new Uri("pack://application:,,,/AvasRoutingTool;component/Resources/Themes/Theme.Dark.xaml") };
            return keys.Select(k => (Color)dark[k]).ToHashSet();
        }

        /// <summary>Every colour a laid-out view paints its chrome with (backgrounds, text, fills), as the elements resolved them.</summary>
        private static List<(string Where, Color Colour, bool IsText)> PaintedColours(DependencyObject root)
        {
            static IEnumerable<(Brush? Brush, bool IsText)> Brushes(DependencyObject d) => d switch
            {
                Border b => new[] { (b.Background, false) },
                Panel p => new[] { (p.Background, false) },
                TextBlock t => new[] { (t.Foreground, true) },
                Control c => new[] { (c.Background, false), (c.Foreground, true) },
                System.Windows.Shapes.Shape s => new[] { (s.Fill, false) },
                _ => Array.Empty<(Brush?, bool)>(),
            };
            var found = new List<(string, Color, bool)>();
            foreach (var d in AllVisuals(root))
                foreach (var (brush, isText) in Brushes(d))
                    if (brush is SolidColorBrush { Color.A: > 0 } solid)
                        found.Add(((d as FrameworkElement)?.Name is { Length: > 0 } n ? $"{d.GetType().Name} {n}" : d.GetType().Name, solid.Color, isText));
            return found;
        }

        private static IEnumerable<DependencyObject> AllVisuals(DependencyObject root)
        {
            yield return root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in AllVisuals(VisualTreeHelper.GetChild(root, i)))
                    yield return child;
        }

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

                        // 2. Completed StepDot checkmarks use Brush.Text.OnAction on Brush.Action.Take
                        var checks = FindAll<TextBlock>(view).Where(t => t.Text == "✓" && t.IsVisible && t.Parent is Border { Width: 28, Height: 28 }).ToList(); // StepDots only, not the Success banner glyph
                        Assert.NotEmpty(checks);
                        Assert.All(checks, t => Assert.Equal(onAction, ((SolidColorBrush)t.Foreground).Color));

                        // 3. PickerPopup resting tiles use neutral Brush.Border.Control, not cyan DropTarget
                        var popup = (Popup)Named(view, "PickerPopup").Single();
                        Assert.True(popup.IsOpen);
                        var pickerMenu = (FrameworkElement)popup.Child;
                        Relayout(pickerMenu);
                        var tile = Named(pickerMenu, "PickerTile").Cast<Button>().First(b => b.IsEnabled);
                        var bd = (Border)tile.Template.FindName("bd", tile);
                        Assert.Equal(controlBorder, ((SolidColorBrush)bd.BorderBrush).Color);
                    });
                }
                finally
                {
                    themeService.SetTheme(AvasRoutingTool.Services.ThemeMode.Dark);
                }
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

        [Fact]
        public void DisplayCard_ActionDock_IsUntouchableUntilRevealed_AndATapKeepsItUpForFourSeconds_UntilTheCardUnloads() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var view = rig.View(rig.Rx1, out var host);
                Showing(host, () =>
                {
                    var dock = (Border)Named(view, "FloatingActionDock").Single();
                    Assert.False(dock.IsHitTestVisible, "a hidden dock must not catch the first tap");
                    Assert.False(view.IsTouchRevealed);
                    Assert.Equal(TimeSpan.FromSeconds(4), DisplayCardView.TouchRevealDuration);

                    view.RevealActionsOnTouch();
                    Assert.True(view.IsTouchRevealed);
                    Assert.True(view.TouchRevealTimerRunning);
                    Assert.True(dock.IsHitTestVisible);

                    // A card that leaves the tree takes its timer with it.
                    view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                    Assert.False(view.TouchRevealTimerRunning);
                    Assert.False(view.IsTouchRevealed);
                    Assert.False(dock.IsHitTestVisible);
                });
            });
    }
}
