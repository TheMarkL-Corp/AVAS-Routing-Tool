using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AvasRoutingTool.Logging;
using AvasRoutingTool.Services;
using AvasRoutingTool.ViewModels;
using Wpf.Ui.Controls;
using Xunit;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AvasRoutingTool.Tests
{
    /// <summary>UI v2 Logs tab (plan task A6): the one virtualized list, tokenised columns, a touch-safe toolbar with vector icons.</summary>
    [Collection("WpfSta")]
    public class UiV2LogsViewTests
    {
        private static void RunSta(Action<MainWindow, MainViewModel> body)
        {
            Exception? failure = null;
            var t = new Thread(() =>
            {
                try
                {
                    TestResources.Ensure();
                    var window = new MainWindow { WindowState = WindowState.Normal, Width = 1280, Height = 860, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000 };
                    window.Show();
                    var vm = (MainViewModel)window.DataContext;
                    try
                    {
                        vm.Logs.Clear();
                        foreach (var level in new[] { LogLevel.Info, LogLevel.Warn, LogLevel.Error, LogLevel.Debug })
                            vm.Logs.Add(new LogEntry(DateTime.Now, level, "TEST", $"first {level}"));
                        for (var i = 0; i < 996; i++)
                            vm.Logs.Add(new LogEntry(DateTime.Now, LogLevel.Info, "TEST", $"entry {i} " + new string('x', i % 7 * 20)));
                        ((FrameworkElement)window.FindName("ViewVisualRouting")).Visibility = Visibility.Collapsed;
                        ((FrameworkElement)window.FindName("ViewLogs")).Visibility = Visibility.Visible;
                        window.UpdateLayout();
                        body(window, vm);
                    }
                    finally { window.Close(); }
                }
                catch (Exception ex) { failure = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("Logs failure: " + failure);
        }

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var c = VisualTreeHelper.GetChild(root, i);
                if (c is T t) yield return t;
                foreach (var d in Descendants<T>(c)) yield return d;
            }
        }

        private static ListBox Logs(MainWindow w) => (ListBox)w.FindName("LogList");

        [Fact]
        public void ThousandEntries_RealizeOnlyTheVisibleRows()
        {
            RunSta((w, vm) =>
            {
                var list = Logs(w);
                Assert.Equal(1000, vm.Logs.Count);
                var realized = Descendants<ListBoxItem>(list).Count();
                Assert.InRange(realized, 1, 99);
            });
        }

        [Fact]
        public void TheList_IsVirtualized_Recycling_ByPixel()
        {
            RunSta((w, _) =>
            {
                var list = Logs(w);
                Assert.True(VirtualizingPanel.GetIsVirtualizing(list));
                Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(list));
                Assert.Equal(ScrollUnit.Pixel, VirtualizingPanel.GetScrollUnit(list));
                Assert.True(ScrollViewer.GetCanContentScroll(list));
                Assert.Same(Application.Current.FindResource("Style.ListBox.Virtualized"), list.Style);
            });
        }

        [Fact]
        public void TheList_HasNoScrollViewerAncestor_WhichWouldSwitchVirtualizationOff()
        {
            RunSta((w, _) =>
            {
                for (DependencyObject? d = VisualTreeHelper.GetParent(Logs(w)); d != null; d = VisualTreeHelper.GetParent(d))
                    Assert.False(d is ScrollViewer, "the Logs list sits inside a ScrollViewer");
            });
        }

        [Fact]
        public void TheToolbar_HasTouchSizedButtons_WithVectorIcons_AndReadableText()
        {
            RunSta((w, _) =>
            {
                foreach (var name in new[] { "ClearLogsButton", "OpenLogFolderButton" })
                {
                    var button = (Button)w.FindName(name);
                    Assert.NotNull(button);
                    Assert.True(button.ActualHeight >= 48, $"{name} is {button.ActualHeight}px high");
                    var icon = Descendants<SymbolIcon>(button).Single();
                    Assert.False(string.IsNullOrEmpty(icon.Glyph));
                    foreach (var tb in Descendants<TextBlock>(button).Where(t => !string.IsNullOrWhiteSpace(t.Text) && t.Text != icon.Glyph))
                    {
                        Assert.True(tb.FontSize >= 12, $"{name}: '{tb.Text}' is {tb.FontSize}px");
                        Assert.DoesNotContain(tb.Text, ch => char.IsSurrogate(ch) || ch >= '←');
                    }
                    Assert.False(string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(button)) && Descendants<TextBlock>(button).All(t => string.IsNullOrWhiteSpace(t.Text)));
                }
            });
        }

        [Fact]
        public void TheLogFilePath_IsTruncatedByTheColumn_NotByAFixedMaxWidth()
        {
            RunSta((w, vm) =>
            {
                var path = Descendants<TextBlock>((FrameworkElement)w.FindName("ViewLogs")).First(t => t.Text == vm.LogFilePath);
                Assert.True(double.IsPositiveInfinity(path.MaxWidth), $"MaxWidth {path.MaxWidth}");
                Assert.Equal(TextTrimming.CharacterEllipsis, path.TextTrimming);
                Assert.True(path.FontSize >= 12);
            });
        }

        [Fact]
        public void TheRowColumns_ComeFromTheLogColumnTokens()
        {
            RunSta((w, _) =>
            {
                var grid = Descendants<Grid>(Logs(w)).First(g => g.ColumnDefinitions.Count == 4);
                Assert.Equal(((GridLength)Application.Current.FindResource("Size.Log.Col.Time")).Value, grid.ColumnDefinitions[0].Width.Value);
                Assert.Equal(((GridLength)Application.Current.FindResource("Size.Log.Col.Level")).Value, grid.ColumnDefinitions[1].Width.Value);
                Assert.Equal(((GridLength)Application.Current.FindResource("Size.Log.Col.Category")).Value, grid.ColumnDefinitions[2].Width.Value);
                Assert.True(grid.ColumnDefinitions[3].Width.IsStar);
            });
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TheLevelColours_FollowWarnErrorDebug_InBothThemes(bool light)
        {
            RunSta((w, vm) =>
            {
                var theme = new ThemeService();
                try
                {
                    theme.SetTheme(light ? ThemeMode.Light : ThemeMode.Dark);
                    w.UpdateLayout();
                    var levels = Descendants<TextBlock>(Logs(w)).Where(t => t.Text is "Info" or "Warn" or "Error" or "Debug").GroupBy(t => t.Text).ToDictionary(g => g.Key, g => g.First());
                    Assert.Equal(4, levels.Count);
                    var expected = new Dictionary<string, string> { ["Info"] = "Brush.Accent.Primary", ["Warn"] = "Brush.Status.Staged", ["Error"] = "Brush.Action.Danger", ["Debug"] = "Brush.Text.Muted" };
                    foreach (var (level, key) in expected)
                    {
                        var want = ((SolidColorBrush)Application.Current.FindResource(key)).Color;
                        Assert.Equal(want, ((SolidColorBrush)levels[level].Foreground).Color);
                        Assert.True(levels[level].FontSize >= 12);
                    }
                }
                finally { theme.SetTheme(ThemeMode.Dark); }
            });
        }
    }
}
