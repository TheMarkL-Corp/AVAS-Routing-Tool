using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Controls;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>UI v2 Full HD pass (plan task A13): the real card and picture sizes at a maximized 1920x1080 window, and the raised caps.</summary>
    public partial class ViewSmokeTests
    {
        // A maximized 1920x1080 window at 100%: work area 1920x1040, caption 31, app bar 57 -> the content is about 1920x952.
        private const double FullHdContentWidth = 1920;
        private const double FullHdContentHeight = 952;

        private sealed record CardMeasure(double CardWidth, double CardHeight, double PictureWidth, double PictureHeight, bool Scrolls);

        private static (VisualRoutingView View, MainViewModel Vm) FullHdOverview(int sources, int displays)
        {
            SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher));
            var client = new MockSdvoeClient();
            var vm = new MainViewModel(new ConfigService(), client, new MockRtpReceiver(), new MulticastIpManager(),
                new RoutingOrchestrator(client), new Sdvoe.MultiView.MultiViewOrchestrator(client), post: a => a());
            for (var i = 1; i <= sources; i++)
                vm.Sources.Add(new SourceCardViewModel(new AvasDevice { DeviceId = $"74fe488b07{i:00}", DeviceName = $"TX{i}", VendorId = 105, IsTransmitter = true, IsActive = true }));
            for (var i = 1; i <= displays; i++)
                vm.Displays.Add(new DisplayCardViewModel(new AvasDevice { DeviceId = $"74fe483c05{i:00}", DeviceName = $"RX{i}", IsReceiver = true, IsActive = true }, vm.UnrouteVideoAsync));
            return (new VisualRoutingView { DataContext = vm }, vm);
        }

        private static T WithFullHdWindow<T>(VisualRoutingView view, Func<T> measure)
        {
            var window = new Window { Content = view, Width = FullHdContentWidth, Height = FullHdContentHeight, ShowInTaskbar = false, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Left = -10000 };
            window.Show();
            try
            {
                view.UpdateLayout();
                Idle();
                view.UpdateLayout();
                return measure();
            }
            finally { window.Close(); }
        }

        private static CardMeasure MeasureFullHd(int sources, int displays, bool displayCards)
        {
            var (view, _) = FullHdOverview(sources, displays);
            return WithFullHdWindow(view, () =>
            {
                if (displayCards)
                {
                    var card = FindAll<DisplayCardView>(view).First(c => c.IsVisible);
                    var picture = (FrameworkElement)Named(card, "PreviewSurface").Single();
                    var scroll = (ScrollViewer)Named(view, "DisplaysScroll").Single();
                    return new CardMeasure(card.ActualWidth, card.ActualHeight, picture.ActualWidth, picture.ActualHeight, scroll.ScrollableHeight > 0.5);
                }
                var source = FindAll<SourceCardView>(view).First(c => c.IsVisible);
                var preview = FindAll<System.Windows.Controls.Image>(source).First();
                var sourceScroll = FindAll<ScrollViewer>(view).First(s => s.Content is ItemsControl ic && ic.Items.Count == sources);
                return new CardMeasure(source.ActualWidth, source.ActualHeight, ((FrameworkElement)preview.Parent).ActualWidth, ((FrameworkElement)preview.Parent).ActualHeight, sourceScroll.ScrollableHeight > 0.5);
            });
        }

        [Fact]
        public void FullHd_MeasuresTheCards_AndWritesTheTable() =>
            RunSta(() =>
            {
                var sb = new StringBuilder();
                sb.AppendLine($"# Full HD measurements (content {FullHdContentWidth}x{FullHdContentHeight}, 100%, maximized)");
                sb.AppendLine();
                sb.AppendLine("| Case | Card (w x h) | Picture (w x h) | Scrolls |");
                sb.AppendLine("|---|---|---|---|");
                foreach (var n in Enumerable.Range(1, 6))
                {
                    var m = MeasureFullHd(3, n, displayCards: true);
                    sb.AppendLine($"| {n} display(s) | {m.CardWidth:F0} x {m.CardHeight:F0} | {m.PictureWidth:F0} x {m.PictureHeight:F0} | {(m.Scrolls ? "yes" : "no")} |");
                }
                foreach (var n in Enumerable.Range(3, 4))
                {
                    var m = MeasureFullHd(n, 2, displayCards: false);
                    sb.AppendLine($"| {n} source(s) | {m.CardWidth:F0} x {m.CardHeight:F0} | {m.PictureWidth:F0} x {m.PictureHeight:F0} | {(m.Scrolls ? "yes" : "no")} |");
                }
                var dir = Path.Combine(UiV2Allowlist.RepoRoot(), "ui_screenshots", "UI-v2", "main-window");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "fullhd-measurements.md"), sb.ToString());
                Assert.Contains("1 display(s)", sb.ToString());
            });

        [Fact]
        public void CardGrids_AreNotVirtualized_EveryCardIsRealized() =>
            RunSta(() =>
            {
                var (view, vm) = FullHdOverview(sources: 8, displays: 6);
                WithFullHdWindow(view, () =>
                {
                    // Each card owns a live preview (socket + multicast membership): every one must exist, scrolled into view or not.
                    Assert.Equal(8, FindAll<SourceCardView>(view).Count);
                    Assert.Equal(6, FindAll<DisplayCardView>(view).Count);
                    Assert.Empty(FindAll<System.Windows.Controls.VirtualizingPanel>(view).Where(p => FindAll<SourceCardView>(p).Any() || FindAll<DisplayCardView>(p).Any()));
                    Assert.Equal(8, FindAll<AdaptiveCardPanel>(view).Where(p => p.Children.OfType<SourceCardView>().Any() || FindAll<SourceCardView>(p).Any()).SelectMany(p => FindAll<SourceCardView>(p)).Count());
                    return 0;
                });
            });

        // Recorded at 1920x952 before the cap change (the overview is height-bound there): the pictures must never get smaller than this.
        [Theory]
        [InlineData(1, 673, 378)]
        [InlineData(2, 673, 378)]
        [InlineData(3, 587, 330)]
        [InlineData(4, 430, 242)]
        [InlineData(5, 336, 189)]
        public void FullHd_DisplayPictures_AreAtLeastTheRecordedBaseline(int displays, double width, double height) =>
            RunSta(() =>
            {
                var m = MeasureFullHd(3, displays, displayCards: true);
                Assert.True(m.PictureWidth >= width - 1.5 && m.PictureHeight >= height - 1.5, $"{displays} displays: {m.PictureWidth:F0}x{m.PictureHeight:F0} < {width}x{height}");
                Assert.False(m.Scrolls);
                Assert.Equal(16.0 / 9.0, m.PictureWidth / m.PictureHeight, 1);
            });

        [Theory]
        [InlineData(3, 370, 203)]
        [InlineData(4, 370, 203)]
        [InlineData(5, 334, 183)]
        public void FullHd_SourcePictures_AreAtLeastTheRecordedBaseline(int sources, double width, double height) =>
            RunSta(() =>
            {
                var m = MeasureFullHd(sources, 2, displayCards: false);
                Assert.True(m.PictureWidth >= width - 1.5 && m.PictureHeight >= height - 1.5, $"{sources} sources: {m.PictureWidth:F0}x{m.PictureHeight:F0} < {width}x{height}");
            });
    }
}
