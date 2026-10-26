using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    public class ViewSmokeTests
    {
        private static void RunSta(Action body)
        {
            Exception? failure = null;
            var t = new Thread(() =>
            {
                try
                {
                    var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    foreach (var name in new[] { "Theme", "Icons" })
                    {
                        app.Resources.MergedDictionaries.Add(new ResourceDictionary
                        {
                            Source = new Uri($"pack://application:,,,/AvasRoutingTool;component/Resources/{name}.xaml")
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

            public MultiViewComposerViewModel Composer(string? layoutId = null)
            {
                var c = new MultiViewComposerViewModel(Rx, Mv, () => Sources, LayoutItemViewModel.CreateCatalog(), null);
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
    }
}
