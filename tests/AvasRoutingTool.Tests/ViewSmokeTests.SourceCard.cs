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
        public void SourceCardView_RendersWithoutBindingCrash() =>
            RunSta(() => Layout(new SourceCardView
            {
                DataContext = new SourceCardViewModel(new AvasDevice { DeviceId = "74fe488b07bb", DeviceName = "TX2", VendorId = 105, IsTransmitter = true, IsActive = true })
            }));

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
        public void SourceCard_HasTouchSafeStreamControls_InfoTooltip_AndHighContrastBadgesInLightMode() =>
            RunSta(() =>
            {
              var themeService = new AvasRoutingTool.Services.ThemeService();
              try
              {
                themeService.SetTheme(AvasRoutingTool.Services.ThemeMode.Light);
                var device = new AvasDevice { DeviceId = "74fe488b07bb", DeviceName = "TX2", VendorId = 105, IsTransmitter = true, IsActive = true };
                var vm = new SourceCardViewModel(device);
                var view = new SourceCardView { DataContext = vm, Width = 340, Height = 260 };
                Showing(view, () =>
                {
                    var refresh = (Button)Named(view, "RefreshStreamButton").Single();
                    Assert.True(refresh.ActualHeight >= 36, $"refresh height {refresh.ActualHeight}");
                    Assert.True(refresh.IsTabStop);

                    var streamButton = Named(view, "StopStreamButton").Concat(Named(view, "StartStreamButton"))
                        .OfType<Button>().First(b => b.IsVisible);
                    Assert.True(streamButton.ActualHeight >= 36, $"stream button height {streamButton.ActualHeight}");
                    Assert.True(streamButton.IsTabStop);

                    var badge = Named(view, "SourceInfoBadge").Single();
                    var tip = Assert.IsType<TextBlock>(badge.ToolTip);
                    tip.DataContext = badge.DataContext; // WPF hands a tooltip its target's DataContext only when it opens
                    var text = string.Concat(tip.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text));
                    Assert.Contains("IP: " + vm.IpAddress, text);
                    Assert.Contains("MAC: " + vm.MacAddress, text);

                    Assert.True(badge.Focusable);
                    Assert.True(KeyboardNavigation.GetIsTabStop(badge));
                    Assert.Equal("Device network telemetry: IP, MAC, multicast", System.Windows.Automation.AutomationProperties.GetName(badge));
                });

                var offlineVm = new SourceCardViewModel(new AvasDevice { DeviceId = "74fe488b07bc", DeviceName = "TX3", VendorId = 105, IsTransmitter = true, IsActive = false });
                var offlineView = new SourceCardView { DataContext = offlineVm, Width = 340, Height = 260 };
                Showing(offlineView, () =>
                {
                    var secondary = ((SolidColorBrush)Application.Current.FindResource("Brush.Text.Secondary")).Color;
                    var offline = FindAll<TextBlock>(offlineView).First(t => t.Text == "Offline" && t.IsVisible);
                    Assert.Equal(secondary, ((SolidColorBrush)offline.Foreground).Color);
                });
              }
              finally
              {
                themeService.SetTheme(AvasRoutingTool.Services.ThemeMode.Dark);
              }
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
    }
}
