using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AvasRoutingTool.Services;
using AvasRoutingTool.Views;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// The compact source tile (A11) in a column of states, both themes (on demand): set AVAS_UIV2_SHOTS to the output folder and run
    /// <c>--filter FullyQualifiedName~UiV2_CompactSourceTileShots</c>. Without the variable the test does nothing.
    /// </summary>
    public partial class ViewSmokeTests
    {
        [Fact]
        public void UiV2_CompactSourceTileShots()
        {
            var folder = Environment.GetEnvironmentVariable("AVAS_UIV2_SHOTS");
            if (string.IsNullOrWhiteSpace(folder)) return;
            Directory.CreateDirectory(folder);

            RunSta(() =>
            {
                var theme = new ThemeService();
                try
                {
                    foreach (var light in new[] { false, true })
                    {
                        theme.SetTheme(light ? ThemeMode.Light : ThemeMode.Dark);
                        var column = new StackPanel { Margin = new Thickness(16) };

                        void Add(AvasRoutingTool.ViewModels.SourceCardViewModel vm)
                        {
                            column.Children.Add(new SourceCardView { IsCompact = true, DataContext = vm, Margin = new Thickness(0, 0, 0, 10) });
                        }

                        Add(UiV2Source());
                        var armed = UiV2Source(link: "Dual Link");
                        armed.IsArmed = true;
                        armed.UsedInBlocks = 2;
                        Add(armed);
                        Add(UiV2Source(online: false));
                        Add(UiV2Source(avas: false));
                        var streaming = UiV2Source();
                        streaming.IsStreaming = true;
                        streaming.UsedInBlocks = 1;
                        Add(streaming);

                        var host = new Border { Child = column, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
                        host.SetResourceReference(Border.BackgroundProperty, "Brush.Background.Dark");
                        Showing(host, () =>
                        {
                            Relayout(host);
                            SavePng(host, Path.Combine(folder, $"10-compact-source-tile-{(light ? "light" : "dark")}.png"));
                        });
                    }
                }
                finally { theme.SetTheme(ThemeMode.Dark); }
            });
        }
    }
}
