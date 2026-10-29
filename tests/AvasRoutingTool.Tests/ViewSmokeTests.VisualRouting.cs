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

        [Theory]
        [InlineData(1264, 755)]
        [InlineData(1008, 615)]
        public void VisualRoutingView_WithASourceArmed_TheSourceCardsStayFullHeight_AndTheCardPictureIsInTheDisplaysView(int width, int height) =>
            RunSta(() =>
            {
                var (vm, _, sources) = Overview();
                var view = new VisualRoutingView { DataContext = vm };

                ShowingAt(view, width, height, () =>
                {
                    var scroll = (ScrollViewer)Named(view, "DisplaysScroll").Single();
                    var picture = (FrameworkElement)Named(FindAll<DisplayCardView>(view).Single(), "PreviewSurface").Single();
                    var visibleCards = FindAll<SourceCardView>(view).Where(s => s.IsVisible).ToList();
                    Assert.NotEmpty(visibleCards);
                    Assert.Empty(FindAll<LayoutsBarView>(view));
                    double room = scroll.ViewportHeight;
                    double cardHeight = visibleCards[0].ActualHeight;

                    vm.ArmSource(sources[0]);
                    Relayout(view);

                    // Arming does not move the sources: the full cards stay at their height. (The displays only give up
                    // the room of the arm banner that appears above them.)
                    Assert.False(Named(view, "CompactSourcePalette").Single().IsVisible);
                    var armedCards = FindAll<SourceCardView>(view).Where(s => s.IsVisible).ToList();
                    Assert.Equal(visibleCards.Count, armedCards.Count);
                    Assert.Equal(cardHeight, armedCards[0].ActualHeight, 0.5);
                    Assert.True(Named(view, "ArmBanner").Single().IsVisible);
                    // The sources keep their half, so the picture may need the displays' scroll; it still starts in view.
                    Assert.True(picture.IsVisible && BoundsIn(picture, scroll).Top < scroll.ViewportHeight,
                        "the card's picture starts below the displays' view while a source is armed");

                    vm.ArmSource(sources[0]); // tap again: stopped, nothing moves
                    Relayout(view);
                    Assert.Equal(visibleCards.Count, FindAll<SourceCardView>(view).Count(s => s.IsVisible));
                    Assert.Equal(room, scroll.ViewportHeight, 0.5);
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
    }
}
