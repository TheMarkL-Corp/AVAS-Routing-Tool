using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Wpf.Ui.Controls;
using Xunit;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AvasRoutingTool.Tests
{
    /// <summary>UI v2 app bar and brand (plan task A5): labels at Full HD, vector icons, the brand mark view, tokens instead of literals.</summary>
    [Collection("WpfSta")]
    public class UiV2AppBarTests
    {
        private static void RunSta(Action<MainWindow> body, double width = 1280, double height = 860)
        {
            Exception? failure = null;
            var t = new Thread(() =>
            {
                try
                {
                    TestResources.Ensure();
                    var window = new MainWindow { WindowState = WindowState.Normal, Width = width, Height = height, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000 };
                    window.Show();
                    window.UpdateLayout();
                    try { body(window); } finally { window.Close(); }
                }
                catch (Exception ex) { failure = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("App bar failure: " + failure);
        }

        public sealed record ThemeContext(bool IsLightMode);

        [Fact]
        public void AppBar_AtFullHd_ShowsLabels_MaterialIcons_BrandMark_AndTokenSizes() => RunSta(w =>
        {
            Check_AppBar_ShowsLabelsAtFullHd(w);
            Check_ActionIcons_AreMaterialIcons_WithAKind_AndTheButtonColour(w);
            Check_TheBrandMark_IsItsOwnView_InsideTheBrandLogoHost(w);
            Check_TheWindow_TakesItsSizesFromTokens(w);
            Check_TheBrandNameIsALeadRole_AndTheVersionACaption(w);
            Check_TheConnectionDot_IsAStatusDot_FromTheCentralStyle(w);
        }, 1920, 1040);

        [Fact]
        public void AppBar_AtTheFloor_HidesTheLabels_AndStillFits() => RunSta(w => Check_AppBar_HidesTheLabelsAtTheFloor_AndStillFits(w), 1024, 720);

        /// <summary>Only Settings keeps a label (M8): Theme and Refresh are icon-only at every width.</summary>
        private static readonly string[] LabelNames = { "SettingsLabel" };

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var c = VisualTreeHelper.GetChild(root, i);
                if (c is T t) yield return t;
                foreach (var d in Descendants<T>(c)) yield return d;
            }
        }

        private static void Check_AppBar_ShowsLabelsAtFullHd(MainWindow w)
        {
            foreach (var name in LabelNames)
            {
                var label = (TextBlock)w.FindName(name);
                Assert.NotNull(label);
                Assert.True(label.IsVisible, $"{name} is hidden at 1920 wide");
                Assert.True(label.FontSize >= 12, $"{name} is {label.FontSize}px");
                Assert.False(string.IsNullOrWhiteSpace(label.Text));
                Assert.DoesNotContain(label.Text, ch => char.IsSurrogate(ch) || ch >= '←');
            }
        }

        [Fact]
        public void AppBar_ThemeAndRefresh_AreIconOnly_AtEveryWidth_AndTouchSized()
        {
            RunSta(w => Check_ThemeAndRefresh_AreIconOnly_AndTouchSized(w, settingsLabelShown: true), 1920, 1040);
            RunSta(w => Check_ThemeAndRefresh_AreIconOnly_AndTouchSized(w, settingsLabelShown: false), 1024, 720);
        }

        private static void Check_ThemeAndRefresh_AreIconOnly_AndTouchSized(MainWindow w, bool settingsLabelShown)
        {
            Assert.Null(w.FindName("ThemeToggleLabel"));
            Assert.Null(w.FindName("RefreshLabel"));

            foreach (var name in new[] { "ThemeToggleButton", "RefreshButton" })
            {
                var button = (Button)w.FindName(name);
                Assert.True(button.ActualWidth >= 48 && button.ActualHeight >= 48, $"{name} is {button.ActualWidth}x{button.ActualHeight}, below the 48px touch minimum");
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)), $"{name} has no automation name");
                Assert.False(string.IsNullOrWhiteSpace(button.ToolTip as string), $"{name} has no tooltip");
            }

            var settings = (TextBlock)w.FindName("SettingsLabel");
            Assert.Equal(settingsLabelShown, settings.IsVisible);
        }

        private static bool IsTransparentOrNull(Brush? b) => b == null || (b is SolidColorBrush s && s.Color.A == 0);

        [Fact]
        public void ConnectionDot_TellsOfflineFromConnecting_AndConnectedIsSolid() => RunSta(w =>
        {
            var vm = (MainViewModel)w.DataContext;
            var badge = (Border)w.FindName("ConnectionBadge");
            var dot = Descendants<System.Windows.Shapes.Ellipse>(badge).Single();
            var muted = ((SolidColorBrush)Application.Current.FindResource("Brush.Text.Muted")).Color;
            var online = ((SolidColorBrush)Application.Current.FindResource("Brush.Status.Online")).Color;
            var danger = ((SolidColorBrush)Application.Current.FindResource("Brush.Action.Danger")).Color;
            vm.ConnectionStatus = "Offline: 127.0.0.1:6970";

            vm.ConnectionHealth = ConnectionHealth.Connecting;
            w.UpdateLayout();
            Assert.True(IsTransparentOrNull(dot.Fill), "connecting: the dot is filled");
            Assert.Equal(muted, ((SolidColorBrush)dot.Stroke).Color);

            vm.ConnectionHealth = ConnectionHealth.Connected;
            w.UpdateLayout();
            Assert.Equal(online, ((SolidColorBrush)dot.Fill).Color);
            Assert.True(IsTransparentOrNull(dot.Stroke), "connected: the dot has a ring (it should be solid)");

            vm.ConnectionHealth = ConnectionHealth.Offline;
            w.UpdateLayout();
            Assert.Equal(danger, ((SolidColorBrush)dot.Fill).Color);
            Assert.True(IsTransparentOrNull(dot.Stroke), "offline: the dot has a ring (it should be solid)");

            Assert.Equal(vm.ConnectionStatus, (string)badge.ToolTip);
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(badge));
        });

        /// <summary>The app bar shows the dot and the word, always: the word is the connection status in every state, at 12px or more.</summary>
        [Fact]
        public void ConnectionWord_IsShownInEveryState_AtLeast12px() => RunSta(w =>
        {
            var vm = (MainViewModel)w.DataContext;
            var word = (TextBlock)w.FindName("ConnectionText");
            foreach (var (health, status) in ConnectionStates)
            {
                vm.ConnectionStatus = status;
                vm.ConnectionHealth = health;
                w.UpdateLayout();
                Assert.True(word.IsVisible, $"{health}: the status word is hidden");
                Assert.False(string.IsNullOrWhiteSpace(word.Text), $"{health}: the status word is empty");
                Assert.Equal(status, word.Text);
                Assert.True(word.FontSize >= 12, $"{health}: the status word is {word.FontSize}px");
            }
        });

        /// <summary>Narrator reads the badge name: it carries the state and changes with it (Polite live region).</summary>
        [Fact]
        public void ConnectionBadge_AutomationName_CarriesTheStatus_AndChangesWithIt() => RunSta(w =>
        {
            var vm = (MainViewModel)w.DataContext;
            var badge = (Border)w.FindName("ConnectionBadge");
            var names = new List<string>();
            foreach (var (health, status) in ConnectionStates)
            {
                vm.ConnectionStatus = status;
                vm.ConnectionHealth = health;
                w.UpdateLayout();
                var name = AutomationProperties.GetName(badge);
                Assert.Equal("Connection status: " + status, name);
                names.Add(name);
            }
            Assert.Equal(names.Count, names.Distinct().Count());
        });

        [Fact]
        public void ConnectionWord_UsesTheSecondaryTextBrush_InBothThemes() => RunSta(w =>
        {
            var vm = (MainViewModel)w.DataContext;
            var word = (TextBlock)w.FindName("ConnectionText");
            var theme = new AvasRoutingTool.Services.ThemeService();
            try
            {
                foreach (var mode in new[] { AvasRoutingTool.Services.ThemeMode.Light, AvasRoutingTool.Services.ThemeMode.Dark })
                {
                    theme.SetTheme(mode);
                    vm.ConnectionHealth = ConnectionHealth.Connected;
                    w.UpdateLayout();
                    var secondary = ((SolidColorBrush)Application.Current.FindResource("Brush.Text.Secondary")).Color;
                    Assert.Equal(secondary, ((SolidColorBrush)word.Foreground).Color);
                }
            }
            finally { theme.SetTheme(AvasRoutingTool.Services.ThemeMode.Dark); }
        });

        private static readonly (ConnectionHealth Health, string Status)[] ConnectionStates =
        {
            (ConnectionHealth.Connecting, "Connecting to 127.0.0.1:6970..."),
            (ConnectionHealth.Connected, "Connected: 127.0.0.1:6970"),
            (ConnectionHealth.Offline, "Offline: 127.0.0.1:6970"),
        };

        private static void Check_AppBar_HidesTheLabelsAtTheFloor_AndStillFits(MainWindow w)
        {
            foreach (var name in LabelNames)
                Assert.False(((TextBlock)w.FindName(name)).IsVisible, $"{name} is visible at 1024 wide");

            var buttons = new[] { "ThemeToggleButton", "RefreshButton", "SettingsButton" }.Select(n => (Button)w.FindName(n)).ToArray();
            Assert.True(buttons.Max(b => b.TranslatePoint(new Point(b.ActualWidth, 0), w).X) <= w.ActualWidth, "actions are clipped");
            Assert.All(buttons, b => Assert.True(b.ActualWidth >= 48 && b.ActualHeight >= 48));
        }

        private static void Check_ActionIcons_AreMaterialIcons_WithAKind_AndTheButtonColour(MainWindow w)
        {
            foreach (var name in new[] { "ThemeToggleButton", "RefreshButton", "SettingsButton" })
            {
                var button = (Button)w.FindName(name);
                var icon = Descendants<AvasRoutingTool.Controls.MaterialIcon>(button).Single();
                Assert.False(string.IsNullOrEmpty(icon.Kind), $"{name}: no Kind");
                Assert.True(Application.Current.TryFindResource("MIcon." + icon.Kind) is Geometry, $"{name}: MIcon.{icon.Kind} is missing");
                Assert.Empty(Descendants<SymbolIcon>(button));
                Assert.True(icon.ActualWidth >= 16 && icon.ActualHeight >= 16);
                Assert.Same(button.Foreground, icon.Foreground);
            }
        }

        [Fact]
        public void TheThemeIcon_NamesTheActiveTheme_SunInLightMoonInDark() => RunSta(_ =>
        {
            var style = (Style)Application.Current.FindResource("Style.Icon.ThemeToggle");
            var icon = new AvasRoutingTool.Controls.MaterialIcon { Style = style };
            var host = new Window { Content = icon, DataContext = new ThemeContext(false), Width = 200, Height = 200, ShowInTaskbar = false, Left = -10000 };
            try
            {
                host.Show();
                host.UpdateLayout();
                Assert.Equal("dark_mode", icon.Kind);
                host.DataContext = new ThemeContext(true);
                host.UpdateLayout();
                Assert.Equal("light_mode", icon.Kind);
            }
            finally { host.Close(); }
        });

        private static void Check_TheBrandMark_IsItsOwnView_InsideTheBrandLogoHost(MainWindow w)
        {
            var logo = (FrameworkElement)w.FindName("BrandLogo");
            Assert.True(logo.ActualWidth >= 28 && logo.ActualHeight >= 28);
            Assert.Equal((double)Application.Current.FindResource("Size.Icon.Brand"), logo.ActualWidth);
            var mark = Descendants<BrandMark>(logo).Single();
            Assert.True(mark.ActualWidth > 0 && mark.ActualHeight > 0);
        }

        private static void Check_TheWindow_TakesItsSizesFromTokens(MainWindow w)
        {
            Assert.Equal((double)Application.Current.FindResource("Size.Window.Min.Width"), w.MinWidth);
            Assert.Equal((double)Application.Current.FindResource("Size.Window.Min.Height"), w.MinHeight);
            Assert.Equal(1024, w.MinWidth);
            Assert.Equal(720, w.MinHeight);
        }

        private static void Check_TheBrandNameIsALeadRole_AndTheVersionACaption(MainWindow w)
        {
            var name = Descendants<TextBlock>((FrameworkElement)w.FindName("BrandBlock")).First(t => t.Text == "AVAS ROUTING TOOL");
            Assert.Equal((double)Application.Current.FindResource("Font.Size.Lead"), name.FontSize);
            var version = (TextBlock)w.FindName("AppVersionText");
            Assert.Equal((double)Application.Current.FindResource("Font.Size.Caption"), version.FontSize);
        }

        private static void Check_TheConnectionDot_IsAStatusDot_FromTheCentralStyle(MainWindow w)
        {
            var badge = (FrameworkElement)w.FindName("ConnectionBadge");
            var dot = Descendants<System.Windows.Shapes.Ellipse>(badge).Single();
            Assert.Equal((double)Application.Current.FindResource("Size.Dot.M"), dot.ActualWidth);
            Assert.Equal(dot.ActualWidth, dot.ActualHeight);
            Assert.Equal((double)Application.Current.FindResource("Size.Touch.Min"), badge.MinHeight);
        }
    }
}
