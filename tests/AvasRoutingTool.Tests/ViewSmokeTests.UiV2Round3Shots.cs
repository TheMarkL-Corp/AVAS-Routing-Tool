using System;
using System.IO;
using System.Linq;
using System.Windows;
using AvasRoutingTool.Services;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// Round 3 screenshots of the Main Window displays carousel (on demand): set AVAS_UIV2_SHOTS to the output folder and run
    /// <c>--filter FullyQualifiedName~UiV2Round3Shots</c>. Without the variable the test does nothing. Dark theme only.
    /// </summary>
    public partial class ViewSmokeTests
    {
        [Fact]
        public void UiV2Round3Shots_MainWindowCarousel()
        {
            var folder = Environment.GetEnvironmentVariable("AVAS_UIV2_SHOTS");
            if (string.IsNullOrWhiteSpace(folder)) return;
            Directory.CreateDirectory(folder);

            RunSta(() =>
            {
                var themeService = new ThemeService();
                try
                {
                    void Shot(string name, double w, double h, int displays, bool arm, bool scrollToEnd)
                    {
                        var (_, vm) = FullHdOverview(3, displays);
                        var window = new MainWindow { DataContext = vm, WindowState = WindowState.Normal, Width = w, Height = h, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000 };
                        window.Show();
                        try
                        {
                            themeService.SetTheme(ThemeMode.Dark);
                            if (arm) vm.ArmSource(vm.Sources[1]);
                            Relayout(window);
                            var view = FindAll<VisualRoutingView>(window).First();
                            if (scrollToEnd) view.ScrollDisplaysBy(100000);
                            Relayout(window);
                            SavePng(ClientRoot(window), Path.Combine(folder, name));
                        }
                        finally { window.Close(); }
                    }

                    Shot("after-main-1920-4displays.png", 1920, 1080, 4, arm: false, scrollToEnd: false);
                    Shot("after-main-1920-5displays.png", 1920, 1080, 5, arm: true, scrollToEnd: false);
                    Shot("after-main-1920-5displays-scrolled-end.png", 1920, 1080, 5, arm: true, scrollToEnd: true);
                    Shot("after-main-1920-12displays.png", 1920, 1080, 12, arm: false, scrollToEnd: false);
                    Shot("after-main-1024-5displays.png", 1024, 720, 5, arm: false, scrollToEnd: false);
                }
                finally { themeService.SetTheme(ThemeMode.Dark); }
            });
        }
    }
}
