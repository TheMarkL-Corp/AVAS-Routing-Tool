using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using AvasRoutingTool.Rtsp;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Tests.Fakes;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public partial class ViewSmokeTests
    {
        private const string FakeRtsp = "rtsp://user:pass-fake@example.test:554/live/1?token=abc";
        private const string BadgeText = "View only - cannot be routed to a display";

        private static (MainViewModel Vm, List<FakeRtspPlayer> Players) RtspRig()
        {
            var client = new MockSdvoeClient();
            var vm = new MainViewModel(new Configuration.ConfigService(), client, new MockRtpReceiver(),
                new MulticastIpManager(), new RoutingOrchestrator(client));
            var players = new List<FakeRtspPlayer>();
            vm.RtspPlayerFactory = () => { var p = new FakeRtspPlayer(); players.Add(p); return p; };
            vm.RtspDelay = (_, _) => System.Threading.Tasks.Task.CompletedTask;
            return (vm, players);
        }

        private static IEnumerable<string> EveryString(DependencyObject root)
        {
            foreach (var fe in FindAll<FrameworkElement>(root))
            {
                if (fe is TextBlock t) yield return t.Text;
                if (fe is ContentControl c && c.Content is string s) yield return s;
                if (fe is TextBox tb) yield return tb.Text;
                if (fe.ToolTip is string tip) yield return tip;
                else if (fe.ToolTip is ToolTip tt && tt.Content is string tts) yield return tts;
                yield return AutomationProperties.GetName(fe) ?? "";
                yield return AutomationProperties.GetHelpText(fe) ?? "";
            }
        }

        private static TextBox AddressBox(RtspSourceCardView v) => (TextBox)Named(v, "AddressBox").Single();
        private static Button StartAdd(RtspSourceCardView v) => (Button)Named(v, "StartAddButton").Single();

        [Fact]
        public void RtspTile_Empty_ShowsTheBox_AndStartWaitsForAParsableAddress() =>
            RunSta(() =>
            {
                var (vm, _) = RtspRig();
                var view = new RtspSourceCardView { DataContext = vm };
                Showing(view, () =>
                {
                    AssertVisibleText(view, "Add RTSP stream");
                    var box = AddressBox(view);
                    Assert.True(box.IsVisible);
                    Assert.Equal("RTSP address", AutomationProperties.GetName(box));
                    Assert.True(box.ActualHeight >= 47.5, $"box height {box.ActualHeight}");
                    AssertVisibleText(view, "rtsp://...");
                    Assert.False(StartAdd(view).IsEnabled);

                    box.Text = "http://example.test/x";
                    Relayout(view);
                    Assert.False(StartAdd(view).IsEnabled);
                    Assert.False(string.IsNullOrWhiteSpace(((TextBlock)Named(view, "ReasonText").Single()).Text));

                    box.Text = FakeRtsp;
                    Relayout(view);
                    Assert.True(StartAdd(view).IsEnabled);
                    Assert.Equal("", ((TextBlock)Named(view, "ReasonText").Single()).Text);
                });
            });

        [Fact]
        public void RtspTile_EnterInTheBox_Starts_AndTheBoxIsGone() =>
            RunSta(() =>
            {
                var (vm, players) = RtspRig();
                var view = new RtspSourceCardView { DataContext = vm };
                Showing(view, () =>
                {
                    var box = AddressBox(view);
                    box.Text = FakeRtsp;
                    box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(box), 0, Key.Enter) { RoutedEvent = UIElement.KeyDownEvent });
                    Relayout(view);
                    Assert.NotNull(vm.ViewOnlySource);
                    Assert.Single(players);
                    Assert.False(Named(view, "EmptyLayout").Single().IsVisible);
                    Assert.Equal("", box.Text);
                });
            });

        [Fact]
        public void RtspTile_RendersEachStateWithItsWord() =>
            RunSta(() =>
            {
                var (vm, players) = RtspRig();
                vm.RtspDelay = (_, ct) => System.Threading.Tasks.Task.Delay(-1, ct); // a Lost tile stays Lost
                Assert.True(vm.TrySetRtspUrl(FakeRtsp, out _));
                var view = new RtspSourceCardView { DataContext = vm };
                var fake = players.Single();
                Showing(view, () =>
                {
                    string Word() => ((TextBlock)Named(view, "StateWord").Single()).Text;
                    Assert.Equal("Connecting", Word());
                    AssertVisibleText(view, "Connecting...");

                    fake.Bitmap = new WriteableBitmap(64, 36, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null);
                    fake.Raise(RtspPlayerEvent.Playing);
                    Relayout(view);
                    Assert.Equal("Live", Word());
                    Assert.True(Named(view, "LiveImage").Single().IsVisible);
                    Assert.False(Named(view, "PictureMessage").Single().IsVisible);

                    var other = new WriteableBitmap(32, 18, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null);
                    fake.SwapBitmap(other);
                    Relayout(view);
                    Assert.Same(other, ((System.Windows.Controls.Image)Named(view, "LiveImage").Single()).Source);

                    fake.Raise(RtspPlayerEvent.Dropped);
                    Relayout(view);
                    Assert.Equal("Lost", Word());
                    Assert.Contains("retrying", ((TextBlock)Named(view, "StatusLine").Single()).Text);
                    Assert.True(Named(view, "PictureMessage").Single().IsVisible);

                    vm.ViewOnlySource!.Stop();
                    Relayout(view);
                    Assert.Equal("Stopped", Word());
                    Assert.True(Named(view, "StartButton").Single().IsVisible);
                    Assert.False(Named(view, "StopButton").Single().IsVisible);

                    vm.ViewOnlySource.StartCommand.Execute(null);
                    fake.Raise(RtspPlayerEvent.Failed(RtspFailure.AuthFailed));
                    Relayout(view);
                    Assert.Equal("Error", Word());
                    Assert.True(Named(view, "RetryButton").Single().IsVisible);
                    Assert.Contains("Cannot open stream", ((TextBlock)Named(view, "StatusLine").Single()).Text);
                });
            });

        [Fact]
        public void RtspTile_ActionButtons_AreIconOnly_48px_WithNames() =>
            RunSta(() =>
            {
                var (vm, _) = RtspRig();
                vm.TrySetRtspUrl(FakeRtsp, out _);
                var view = new RtspSourceCardView { DataContext = vm };
                Showing(view, () =>
                {
                    foreach (var name in new[] { "RemoveButton", "StopButton" })
                    {
                        var b = (Button)Named(view, name).Single();
                        Assert.True(b.IsVisible, name);
                        Assert.True(b.ActualHeight >= 47.5 && b.ActualWidth >= 47.5, $"{name} {b.ActualWidth}x{b.ActualHeight}");
                        Assert.False(string.IsNullOrEmpty(AutomationProperties.GetName(b)));
                        Assert.NotNull(b.ToolTip);
                    }
                });
            });

        [Fact]
        public void RtspTile_BadgeIsAlwaysThere_AndInHelpText() =>
            RunSta(() =>
            {
                var (vm, players) = RtspRig();
                vm.TrySetRtspUrl(FakeRtsp, out _);
                var view = new RtspSourceCardView { DataContext = vm };
                Showing(view, () =>
                {
                    var badge = (TextBlock)Named(view, "ViewOnlyBadge").Single();
                    foreach (var ev in new[] { RtspPlayerEvent.Playing, RtspPlayerEvent.Dropped, RtspPlayerEvent.Failed(RtspFailure.NotFound) })
                    {
                        players.Single().Raise(ev);
                        Relayout(view);
                        Assert.True(badge.IsVisible);
                        Assert.Equal(BadgeText, badge.Text);
                        Assert.Equal(BadgeText, AutomationProperties.GetHelpText(badge));
                    }
                });
            });

        [Fact]
        public void RtspTile_PressingIt_ArmsNothing_AndRoutesNothing() =>
            RunSta(() =>
            {
                var (vm, _) = RtspRig();
                vm.Sources.Add(new SourceCardViewModel(new AvasDevice { DeviceId = "74fe488b07bb", DeviceName = "TX2", VendorId = 105, IsTransmitter = true, IsActive = true }));
                vm.TrySetRtspUrl(FakeRtsp, out _);
                var view = new RtspSourceCardView { DataContext = vm };
                Showing(view, () =>
                {
                    var card = FindAll<Wpf.Ui.Controls.Card>(view).First();
                    card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
                    card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
                    card.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
                    Assert.False(vm.Sources.Any(s => s.IsArmed));
                    Assert.Null(vm.ArmedSource);
                    Assert.False(view.Focusable);
                    Assert.False(view.IsTabStop);
                });
            });

        [Fact]
        public void RtspSource_IsNotASource_SourcesAndOnlineCountUnchanged() =>
            RunSta(() =>
            {
                var (vm, _) = RtspRig();
                vm.Sources.Add(new SourceCardViewModel(new AvasDevice { DeviceId = "74fe488b07bb", DeviceName = "TX2", VendorId = 105, IsTransmitter = true, IsActive = true }));
                int count = vm.Sources.Count, online = vm.OnlineSourcesCount;
                Assert.True(vm.TrySetRtspUrl(FakeRtsp, out _));
                Assert.Equal(count, vm.Sources.Count);
                Assert.Equal(online, vm.OnlineSourcesCount);
                vm.RemoveRtsp();
                Assert.Equal(count, vm.Sources.Count);
                Assert.Equal(online, vm.OnlineSourcesCount);
            });

        [Fact]
        public void RtspTile_ShowsNoCredentialsAnywhere() =>
            RunSta(() =>
            {
                var (vm, players) = RtspRig();
                vm.TrySetRtspUrl(FakeRtsp, out _);
                var view = new RtspSourceCardView { DataContext = vm };
                Showing(view, () =>
                {
                    foreach (var ev in new[] { RtspPlayerEvent.Playing, RtspPlayerEvent.Dropped, RtspPlayerEvent.Failed(RtspFailure.AuthFailed, FakeRtsp) })
                    {
                        players.Single().Raise(ev);
                        Relayout(view);
                        foreach (var text in EveryString(view))
                        {
                            Assert.DoesNotContain("pass-fake", text);
                            Assert.DoesNotContain("user:", text);
                            Assert.DoesNotContain("abc", text);
                        }
                    }
                    Assert.Contains(EveryString(view), t => t.Contains("example.test"));
                    Assert.Contains(EveryString(view), t => t.Contains("***"));
                });
            });

        [Fact]
        public void RtspSource_SecondUrl_StopsAndDisposesTheFirstBeforeTheSecondStarts() =>
            RunSta(() =>
            {
                var (vm, players) = RtspRig();
                Assert.True(vm.TrySetRtspUrl(FakeRtsp, out _));
                var first = players.Single();
                bool? firstGoneWhenSecondStarted = null;
                var baseFactory = vm.RtspPlayerFactory;
                vm.RtspPlayerFactory = () =>
                {
                    var p = baseFactory();
                    ((FakeRtspPlayer)p).OnStart = _ => firstGoneWhenSecondStarted = first.Disposed && first.StopCount > 0;
                    return p;
                };
                Assert.True(vm.TrySetRtspUrl("rtsp://127.0.0.1/other", out _));
                Assert.Equal(2, players.Count);
                Assert.True(firstGoneWhenSecondStarted);
                Assert.Single(players[1].Started);
            });

        [Fact]
        public void RtspSource_Remove_DisposesThePlayer_AndTheEmptyTileReturns() =>
            RunSta(() =>
            {
                var (vm, players) = RtspRig();
                vm.TrySetRtspUrl(FakeRtsp, out _);
                var view = new RtspSourceCardView { DataContext = vm };
                Showing(view, () =>
                {
                    Assert.True(Named(view, "ActiveLayout").Single().IsVisible);
                    var remove = (Button)Named(view, "RemoveButton").Single();
                    remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Relayout(view);
                    Assert.True(players.Single().Disposed);
                    Assert.Null(vm.ViewOnlySource);
                    Assert.True(Named(view, "EmptyLayout").Single().IsVisible);
                    Assert.False(Named(view, "ActiveLayout").Single().IsVisible);
                });
            });

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("http://example.test/x")]
        public void RtspSource_BadText_IsRejectedWithAReason_AndChangesNothing(string text) =>
            RunSta(() =>
            {
                var (vm, players) = RtspRig();
                Assert.False(vm.TrySetRtspUrl(text, out var reason));
                Assert.False(string.IsNullOrWhiteSpace(reason));
                Assert.Null(vm.ViewOnlySource);
                Assert.Empty(players);

                Assert.True(vm.TrySetRtspUrl(FakeRtsp, out _));
                var current = vm.ViewOnlySource;
                Assert.False(vm.TrySetRtspUrl(text, out _));
                Assert.Same(current, vm.ViewOnlySource);
                Assert.False(players.Single().Disposed);
            });

        [Fact]
        public void VisualRoutingStrip_HasTheRtspTile_AndTheCompactPaletteDoesNot() =>
            RunSta(() =>
            {
                var (vm, _) = RtspRig();
                vm.Sources.Add(new SourceCardViewModel(new AvasDevice { DeviceId = "74fe488b07bb", DeviceName = "TX2", VendorId = 105, IsTransmitter = true, IsActive = true }));
                var view = new VisualRoutingView { DataContext = vm };
                Showing(view, () =>
                {
                    Assert.Single(FindAll<RtspSourceCardView>(view));
                    Assert.True(FindAll<RtspSourceCardView>(view).Single().IsVisible);
                    Assert.Single(FindAll<SourceCardView>(view).Where(c => c.IsVisible));
                });
            });
    }
}
