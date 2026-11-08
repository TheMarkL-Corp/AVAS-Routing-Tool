using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using AvasRoutingTool.ViewModels;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>Operating-room ergonomics of the app bar: touch targets, keyboard focus, readable text, no clipping at the minimum window size.</summary>
    [Collection("WpfSta")]
    public class MainWindowHeaderTests
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
            if (failure != null) throw new Xunit.Sdk.XunitException("MainWindow header failure: " + failure);
        }

        private static Button[] ActionButtons(MainWindow w) =>
            new[] { "ThemeToggleButton", "RefreshButton", "SettingsButton" }.Select(n => (Button)w.FindName(n)).ToArray();

        private static RadioButton[] Tabs(MainWindow w) =>
            new[] { "TabVisualRouting", "TabRoomCameras", "TabBavmConsole", "TabLogs" }.Select(n => (RadioButton)w.FindName(n)).Where(t => t.Visibility == Visibility.Visible).ToArray();

        [Fact]
        public void Tabs_And_Actions_AreTouchSized() => RunSta(w =>
        {
            foreach (FrameworkElement el in Tabs(w).Cast<FrameworkElement>().Concat(ActionButtons(w)))
                Assert.True(el.ActualHeight >= 48, $"{el.Name} is {el.ActualHeight}px high, below the 48px touch minimum");
        });

        [Fact]
        public void Tabs_And_Actions_ShowAKeyboardFocusRing() => RunSta(w =>
        {
            foreach (Control c in Tabs(w).Cast<Control>().Concat(ActionButtons(w)))
                Assert.NotNull(c.FocusVisualStyle);
        });

        [Fact]
        public void Tabs_And_Actions_HaveSpokenNamesWithoutEmoji() => RunSta(w =>
        {
            foreach (Control c in Tabs(w).Cast<Control>().Concat(ActionButtons(w)))
            {
                var name = AutomationProperties.GetName(c);
                Assert.False(string.IsNullOrWhiteSpace(name), $"{c.Name} has no automation name");
                Assert.DoesNotContain(name, ch => char.IsSurrogate(ch));
            }
        });

        [Fact]
        public void AppBarText_IsAtLeast12px() => RunSta(w =>
        {
            foreach (var tb in Descendants<TextBlock>(w).Where(t => t.IsVisible && !string.IsNullOrWhiteSpace(t.Text)
                         && t.TemplatedParent == null && IsInAppBar(w, t)))
                Assert.True(tb.FontSize >= 12, $"'{tb.Text}' is {tb.FontSize}px");
        });

        [Fact]
        public void AppBar_FitsAtMinimumWindowWidth() => RunSta(w =>
        {
            // The longest connected readout (dot + host:port) must still fit beside the tabs at the floor.
            var vm = (MainViewModel)w.DataContext;
            vm.ConnectionStatus = "Connected: 192.168.100.100:6970";
            vm.ConnectionHealth = ConnectionHealth.Connected;
            w.UpdateLayout();
            var right = ActionButtons(w).Max(b => b.TranslatePoint(new Point(b.ActualWidth, 0), w).X);
            var tabsLeft = Tabs(w).Min(b => b.TranslatePoint(new Point(0, 0), w).X);
            var brandRight = ((FrameworkElement)w.FindName("BrandBlock")).TranslatePoint(new Point(((FrameworkElement)w.FindName("BrandBlock")).ActualWidth, 0), w).X;
            var tabsRight = Tabs(w).Max(b => b.TranslatePoint(new Point(b.ActualWidth, 0), w).X);
            var badge = (FrameworkElement)w.FindName("ConnectionBadge");
            var actionsLeft = badge.TranslatePoint(new Point(0, 0), w).X;
            Assert.True(right <= w.ActualWidth, "actions are clipped");
            Assert.True(brandRight <= tabsLeft, $"brand overlaps tabs: brand {brandRight} tabs {tabsLeft}-{tabsRight} actions {actionsLeft}-{right} of {w.ActualWidth}");
            Assert.True(tabsRight <= actionsLeft, $"tabs overlap actions {tabsRight} > {actionsLeft}");
        }, 1024, 720);

        private static bool IsTransparent(Brush? b) => b == null || (b is SolidColorBrush s && s.Color.A == 0);

        private static Border TabChrome(RadioButton tab) => (Border)tab.Template.FindName("bd", tab);

        [Fact]
        public void Tabs_AreFlatUnderlineTabs_NotFilledChips() => RunSta(w =>
        {
            var accent = ((SolidColorBrush)Application.Current.Resources["Brush.Accent.Primary"]).Color;
            foreach (var tab in Tabs(w))
            {
                var bd = TabChrome(tab);
                Assert.True(IsTransparent(bd.Background) || tab.IsMouseOver, $"{tab.Name} has a filled background");
                Assert.Equal(0, bd.BorderThickness.Top + bd.BorderThickness.Left + bd.BorderThickness.Right);
                Assert.Equal(3, bd.BorderThickness.Bottom);
                if (tab.IsChecked == true)
                    Assert.Equal(accent, ((SolidColorBrush)bd.BorderBrush).Color);
                else
                    Assert.True(IsTransparent(bd.BorderBrush), $"{tab.Name} shows an underline while unselected");
            }
        });

        [Fact]
        public void Tabs_HaveEqualMinimumWidth_AndSitFlushOnTheBarBottom() => RunSta(w =>
        {
            var bar = (FrameworkElement)w.FindName("AppBar");
            foreach (var tab in Tabs(w))
            {
                Assert.True(tab.ActualWidth >= 120, $"{tab.Name} is only {tab.ActualWidth}px wide");
                var bottom = tab.TranslatePoint(new Point(0, tab.ActualHeight), w).Y;
                Assert.True(Math.Abs(bottom - (bar.ActualHeight - 1)) <= 1.5, $"{tab.Name} bottom {bottom} is not flush with the bar bottom {bar.ActualHeight}");
            }
        });

        [Fact]
        public void Actions_AreGhostButtons_WithVectorIcons() => RunSta(w =>
        {
            foreach (var b in ActionButtons(w))
            {
                Assert.True(IsTransparent(b.Background), $"{b.Name} has a filled background at rest");
                Assert.Equal(0, b.BorderThickness.Left + b.BorderThickness.Top + b.BorderThickness.Right + b.BorderThickness.Bottom);
                Assert.True(Descendants<System.Windows.Shapes.Path>(b).Any() || Descendants<AvasRoutingTool.Controls.MaterialIcon>(b).Any(), $"{b.Name} has no vector icon");
                Assert.DoesNotContain(Descendants<TextBlock>(b), t => t.Text.Any(char.IsSurrogate) || t.Text.Any(c => c >= '☀' && c <= '➿'));
            }
        });

        [Fact]
        public void ConnectionBadge_IsAFlatStatusReadout_SeparatedFromTheActions() => RunSta(w =>
        {
            var badge = (Border)w.FindName("ConnectionBadge");
            Assert.Equal(0, badge.BorderThickness.Left + badge.BorderThickness.Top + badge.BorderThickness.Right + badge.BorderThickness.Bottom);
            var divider = (FrameworkElement)w.FindName("AppBarDivider");
            Assert.NotNull(divider);
            Assert.True(divider.ActualWidth >= 1 && divider.ActualHeight >= 24);
            var dx = divider.TranslatePoint(new Point(0, 0), w).X;
            var bx = badge.TranslatePoint(new Point(badge.ActualWidth, 0), w).X;
            var tx = ActionButtons(w).Min(b => b.TranslatePoint(new Point(0, 0), w).X);
            Assert.True(bx <= dx && dx < tx, "divider is not between the status and the actions");
        });

        private static bool IsInAppBar(MainWindow w, DependencyObject d)
        {
            var bar = (DependencyObject)w.FindName("AppBar");
            for (var cur = d; cur != null; cur = VisualTreeHelper.GetParent(cur)) if (cur == bar) return true;
            return false;
        }

        private static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var c = VisualTreeHelper.GetChild(root, i);
                if (c is T t) yield return t;
                foreach (var d in Descendants<T>(c)) yield return d;
            }
        }
    }
}
