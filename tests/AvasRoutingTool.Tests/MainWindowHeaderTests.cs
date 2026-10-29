using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
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
                    var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    foreach (var path in new[] { "Themes/Theme.Dark", "Theme", "Icons" })
                    {
                        app.Resources.MergedDictionaries.Add(new ResourceDictionary
                        {
                            Source = new Uri($"pack://application:,,,/AvasRoutingTool;component/Resources/{path}.xaml")
                        });
                    }
                    var window = new MainWindow { Width = width, Height = height, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000 };
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
            new[] { "TabVisualRouting", "TabBavmConsole", "TabLogs" }.Select(n => (RadioButton)w.FindName(n)).ToArray();

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
            var right = ActionButtons(w).Max(b => b.TranslatePoint(new Point(b.ActualWidth, 0), w).X);
            var tabsLeft = Tabs(w).Min(b => b.TranslatePoint(new Point(0, 0), w).X);
            var brandRight = ((FrameworkElement)w.FindName("BrandBlock")).TranslatePoint(new Point(((FrameworkElement)w.FindName("BrandBlock")).ActualWidth, 0), w).X;
            var tabsRight = Tabs(w).Max(b => b.TranslatePoint(new Point(b.ActualWidth, 0), w).X);
            var badge = (FrameworkElement)w.FindName("ConnectionBadge");
            var actionsLeft = badge.TranslatePoint(new Point(0, 0), w).X;
            Assert.True(right <= w.ActualWidth, "actions are clipped");
            Assert.True(brandRight <= tabsLeft, $"brand overlaps tabs: brand {brandRight} tabs {tabsLeft}-{tabsRight} actions {actionsLeft}-{right} of {w.ActualWidth}");
            Assert.True(tabsRight <= actionsLeft, "tabs overlap actions");
        }, 1024, 720);

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
