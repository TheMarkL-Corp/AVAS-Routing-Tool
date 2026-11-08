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

        [Fact]
        public void LogsTab_IsVirtualized_Tokenised_TouchSafe_AndColoursTheLevels() => RunSta((w, vm) =>
        {
            Check_ThousandEntries_RealizeOnlyTheVisibleRows(w, vm);
            Check_TheList_IsVirtualized_Recycling_ByPixel(w, vm);
            Check_TheList_HasNoScrollViewerAncestor_WhichWouldSwitchVirtualizationOff(w, vm);
            Check_TheToolbar_HasTouchSizedButtons_WithVectorIcons_AndReadableText(w, vm);
            Check_TheLogFilePath_IsTruncatedByTheColumn_NotByAFixedMaxWidth(w, vm);
            Check_TheRowColumns_ComeFromTheLogColumnTokens(w, vm);
            Check_TheLevelColours_FollowWarnErrorDebug_InBothThemes(w, light: false);
            Check_TheLevelColours_FollowWarnErrorDebug_InBothThemes(w, light: true);
        });

        private static ListBox Logs(MainWindow w) => (ListBox)w.FindName("LogList");

        private static void Check_ThousandEntries_RealizeOnlyTheVisibleRows(MainWindow w, MainViewModel vm)
        {
                var list = Logs(w);
                Assert.Equal(1000, vm.Logs.Count);
                var realized = Descendants<ListBoxItem>(list).Count();
                Assert.InRange(realized, 1, 99);
        }

        private static void Check_TheList_IsVirtualized_Recycling_ByPixel(MainWindow w, MainViewModel _)
        {
                var list = Logs(w);
                Assert.True(VirtualizingPanel.GetIsVirtualizing(list));
                Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(list));
                Assert.Equal(ScrollUnit.Pixel, VirtualizingPanel.GetScrollUnit(list));
                Assert.True(ScrollViewer.GetCanContentScroll(list));
                Assert.Same(Application.Current.FindResource("Style.ListBox.Virtualized"), list.Style);
        }

        private static void Check_TheList_HasNoScrollViewerAncestor_WhichWouldSwitchVirtualizationOff(MainWindow w, MainViewModel _)
        {
                for (DependencyObject? d = VisualTreeHelper.GetParent(Logs(w)); d != null; d = VisualTreeHelper.GetParent(d))
                    Assert.False(d is ScrollViewer, "the Logs list sits inside a ScrollViewer");
        }

        private static void Check_TheToolbar_HasTouchSizedButtons_WithVectorIcons_AndReadableText(MainWindow w, MainViewModel _)
        {
                foreach (var (name, kind) in new[] { ("ClearLogsButton", "cleaning_services"), ("OpenLogFolderButton", "folder_open") })
                {
                    var button = (Button)w.FindName(name);
                    Assert.NotNull(button);
                    Assert.True(button.ActualHeight >= 48, $"{name} is {button.ActualHeight}px high");
                    var icon = Descendants<AvasRoutingTool.Controls.MaterialIcon>(button).Single();
                    Assert.Equal(kind, icon.Kind);
                    Assert.Empty(Descendants<SymbolIcon>(button));
                    foreach (var tb in Descendants<TextBlock>(button).Where(t => !string.IsNullOrWhiteSpace(t.Text)))
                    {
                        Assert.True(tb.FontSize >= 12, $"{name}: '{tb.Text}' is {tb.FontSize}px");
                        Assert.DoesNotContain(tb.Text, ch => char.IsSurrogate(ch) || ch >= '←');
                    }
                    Assert.False(string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(button)) && Descendants<TextBlock>(button).All(t => string.IsNullOrWhiteSpace(t.Text)));
                }
        }

        private static void Check_TheLogFilePath_IsTruncatedByTheColumn_NotByAFixedMaxWidth(MainWindow w, MainViewModel vm)
        {
                var path = Descendants<TextBlock>((FrameworkElement)w.FindName("ViewLogs")).First(t => t.Text == vm.LogFilePath);
                Assert.True(double.IsPositiveInfinity(path.MaxWidth), $"MaxWidth {path.MaxWidth}");
                Assert.Equal(TextTrimming.CharacterEllipsis, path.TextTrimming);
                Assert.True(path.FontSize >= 12);
        }

        private static void Check_TheRowColumns_ComeFromTheLogColumnTokens(MainWindow w, MainViewModel _)
        {
                var grid = Descendants<Grid>(Logs(w)).First(g => g.ColumnDefinitions.Count == 4);
                Assert.Equal(((GridLength)Application.Current.FindResource("Size.Log.Col.Time")).Value, grid.ColumnDefinitions[0].Width.Value);
                Assert.Equal(((GridLength)Application.Current.FindResource("Size.Log.Col.Level")).Value, grid.ColumnDefinitions[1].Width.Value);
                Assert.Equal(((GridLength)Application.Current.FindResource("Size.Log.Col.Category")).Value, grid.ColumnDefinitions[2].Width.Value);
                Assert.True(grid.ColumnDefinitions[3].Width.IsStar);
        }

        private static void Check_TheLevelColours_FollowWarnErrorDebug_InBothThemes(MainWindow w, bool light)
        {
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
            }
        }
    }
}
