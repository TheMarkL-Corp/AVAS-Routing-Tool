using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AvasRoutingTool.Rtsp;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Services;
using AvasRoutingTool.Tests.Fakes;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// Interactive preview of the RTSP view-only tile (inside the real VisualRoutingView) decoding a FakeRtspServer through the
    /// real LibVlcStreamPlayer, plus a screenshot mode. Both are gated and return at once (passing) otherwise.
    ///
    /// Preview (PowerShell, repo root):
    ///   $env:AVAS_RTSP_PREVIEW='1'; $env:AVAS_RTSP_REAL='1'; dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj -p:BaseOutputPath=bin/rtsp/ --filter FullyQualifiedName~RtspSourcePreview -- RunConfiguration.DisableParallelization=true
    ///   AVAS_RTSP_PREVIEW_THEME=light|dark, AVAS_RTSP_PREVIEW_AUTOCLOSE_SECONDS=N
    ///   AVAS_RTSP_URL=...  interactive only: the tile uses that address instead of the fake server (never printed, logged or shown;
    ///   the window title is fixed text). The server buttons do nothing then.
    ///
    /// Shots: AVAS_RTSP_SHOTS=1 (and AVAS_RTSP_REAL=1) writes ui_screenshots/UI-v2-rtsp/theme-size-state.png from the fake
    /// server only; AVAS_RTSP_URL is ignored in that mode.
    /// </summary>
    [Collection("WpfSta")]
    public class RtspSourcePreviewHarness
    {
        [Fact]
        public void RtspSourcePreview_Interactive()
        {
            if (Environment.GetEnvironmentVariable("AVAS_RTSP_PREVIEW") != "1") return;
            var light = string.Equals(Environment.GetEnvironmentVariable("AVAS_RTSP_PREVIEW_THEME"), "light", StringComparison.OrdinalIgnoreCase);
            double autoClose = double.TryParse(Environment.GetEnvironmentVariable("AVAS_RTSP_PREVIEW_AUTOCLOSE_SECONDS"),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 0;
            RunSta(() => RunPreview(light, autoClose), "RTSP preview");
        }

        [Fact]
        public void RtspSourcePreview_Screenshots()
        {
            if (Environment.GetEnvironmentVariable("AVAS_RTSP_SHOTS") != "1") return;
            RunSta(RunShots, "RTSP shots");
        }

        private static void RunSta(Action body, string what)
        {
            Exception? failure = null;
            var t = new Thread(() =>
            {
                try { InputModality.IsKeyboard = false; TestResources.Ensure(); body(); }
                catch (Exception ex) { failure = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException(what + " failure: " + failure);
        }

        private static MainViewModel NewVm(Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            var client = new MockSdvoeClient();
            var vm = new MainViewModel(new Configuration.ConfigService(), client, new MockRtpReceiver(),
                new MulticastIpManager(), new RoutingOrchestrator(client));
            vm.Sources.Clear();
            for (var i = 1; i <= 3; i++)
                vm.Sources.Add(new SourceCardViewModel(new AvasDevice
                {
                    DeviceId = "aa:bb:cc:00:00:0" + i,
                    DeviceName = "TX" + i + "_fake",
                    VendorId = 105,
                    IsTransmitter = true,
                    IsActive = i != 3,
                    MulticastIp = "239.1.1." + i,
                }));
            vm.RtspPlayerFactory = () => new LibVlcStreamPlayer();   // the real decoder
            if (delay != null) vm.RtspDelay = delay;
            return vm;
        }

        private static string FakeUrl(FakeRtspServer s) => "rtsp://127.0.0.1:" + s.Port + "/stream";

        private static void Pump(TimeSpan span) => PumpUntil(() => false, span);

        private static bool PumpUntil(Func<bool> cond, TimeSpan timeout)
        {
            var end = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < end)
            {
                if (cond()) return true;
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Thread.Sleep(15);
            }
            return cond();
        }

        // ---------------------------------------------------------------- interactive

        private static void RunPreview(bool light, double autoCloseSeconds)
        {
            var themes = new ThemeService();
            if (light) themes.SetTheme(ThemeMode.Light);

            var realUrl = Environment.GetEnvironmentVariable("AVAS_RTSP_URL");   // read inside the process only
            var useReal = !string.IsNullOrWhiteSpace(realUrl);
            var vm = NewVm();
            FakeRtspServer? server = null;

            void Serve(FakeRtspMode mode)
            {
                if (useReal) return;
                server?.Dispose();
                server = new FakeRtspServer(mode);
                vm.TrySetRtspUrl(FakeUrl(server), out _);   // new port: enter the address again
            }

            if (useReal) vm.TrySetRtspUrl(realUrl!, out _);
            else Serve(FakeRtspMode.Normal);

            var view = new VisualRoutingView { DataContext = vm };
            var label = new TextBlock { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 14 };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");

            Button Btn(string text, Action click)
            {
                var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 6, 12, 6) };
                b.Click += (_, _) => click();
                return b;
            }

            var strip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 8, 16, 8) };
            strip.Children.Add(Btn("Server: Normal", () => Serve(FakeRtspMode.Normal)));
            strip.Children.Add(Btn("Server: 401", () => Serve(FakeRtspMode.Require401)));
            strip.Children.Add(Btn("Server: Refuse", () => Serve(FakeRtspMode.RefuseConnection)));
            strip.Children.Add(Btn("Drop connection", () => server?.Drop()));
            strip.Children.Add(Btn("Stop", () => vm.ViewOnlySource?.Stop()));
            strip.Children.Add(label);

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(strip, 1);
            root.Children.Add(view);
            root.Children.Add(strip);

            var window = new Window
            {
                Title = "RTSP view source preview",
                Width = 1600,
                Height = 900,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = root,
            };
            window.SetResourceReference(Window.BackgroundProperty, "Brush.Background.Dark");

            string lastState = "";
            var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            poll.Tick += (_, _) =>
            {
                lastState = vm.ViewOnlySource == null ? "no tile source" : vm.ViewOnlySource.State.ToString();
                label.Text = "Tile state: " + lastState;
            };
            poll.Start();

            DispatcherTimer? closer = null;
            if (autoCloseSeconds > 0)
            {
                closer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(autoCloseSeconds) };
                closer.Tick += (_, _) =>
                {
                    closer.Stop();
                    Console.WriteLine("RTSP preview smoke: tile state at close: " + lastState);
                    window.Close();
                };
                closer.Start();
            }

            try { window.ShowDialog(); }
            finally
            {
                poll.Stop();
                closer?.Stop();
                try { vm.RemoveRtsp(); } catch (Exception) { }
                server?.Dispose();
                if (light) themes.SetTheme(ThemeMode.Dark);
            }
        }

        // ---------------------------------------------------------------- screenshots (fake server only)

        private static string ShotsFolder()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "docs")))
                dir = dir.Parent;
            var folder = Path.Combine(dir?.FullName ?? Directory.GetCurrentDirectory(), "ui_screenshots", "UI-v2-rtsp");
            Directory.CreateDirectory(folder);
            return folder;
        }

        /// <summary>Brings the RTSP tile into view (at 1024x720 the strip scrolls) and lets a render pass finish.</summary>
        private static void ShowTile(FrameworkElement view)
        {
            var tile = Find<RtspSourceCardView>(view);
            tile?.BringIntoView();
            for (var i = 0; i < 3; i++)
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                view.UpdateLayout();
            }
        }

        private static T? Find<T>(DependencyObject root) where T : DependencyObject
        {
            if (root is T hit) return hit;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var found = Find<T>(VisualTreeHelper.GetChild(root, i));
                if (found != null) return found;
            }
            return null;
        }

        private static void SavePng(FrameworkElement element, string path)
        {
            element.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(element);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }

        private static void RunShots()
        {
            var folder = ShotsFolder();   // AVAS_RTSP_URL is deliberately not read here
            var themes = new ThemeService();
            try
            {
                foreach (var light in new[] { false, true })
                foreach (var (w, h) in new[] { (1920.0, 1080.0), (1024.0, 720.0) })
                foreach (var state in new[] { "Empty", "Live", "Lost", "Error" })
                {
                    themes.SetTheme(light ? ThemeMode.Light : ThemeMode.Dark);
                    var name = (light ? "light" : "dark") + "-" + (int)w + "x" + (int)h + "-" + state + ".png";

                    // Lost must stay Lost (no reconnect while the picture is taken).
                    var vm = NewVm(state == "Lost" ? (_, ct) => Task.Delay(-1, ct) : null);
                    using var server = new FakeRtspServer(state == "Error" ? FakeRtspMode.Require401 : FakeRtspMode.Normal);
                    var view = new VisualRoutingView { DataContext = vm };
                    var window = new Window { Content = view, Width = w, Height = h, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000 };
                    window.SetResourceReference(Window.BackgroundProperty, "Brush.Background.Dark");
                    window.Show();
                    try
                    {
                        themes.SetTheme(light ? ThemeMode.Light : ThemeMode.Dark);
                        Pump(TimeSpan.FromMilliseconds(300));
                        if (state != "Empty")
                        {
                            Assert.True(vm.TrySetRtspUrl(FakeUrl(server), out _));
                            var src = vm.ViewOnlySource!;
                            var want = state == "Error" ? RtspTileState.Error : RtspTileState.Live;
                            Assert.True(PumpUntil(() => src.State == want, TimeSpan.FromSeconds(20)), name + ": tile never reached " + want + ", is " + src.State);
                            if (want == RtspTileState.Live) Pump(TimeSpan.FromSeconds(1));   // a few frames
                            if (state == "Lost")
                            {
                                server.Drop();
                                Assert.True(PumpUntil(() => src.State == RtspTileState.Lost, TimeSpan.FromSeconds(15)), name + ": tile never reached Lost, is " + src.State);
                            }
                        }
                        Pump(TimeSpan.FromMilliseconds(300));
                        view.UpdateLayout();
                        ShowTile(view);
                        SavePng((FrameworkElement)VisualTreeHelper.GetChild(window, 0), Path.Combine(folder, name));
                    }
                    finally
                    {
                        try { vm.RemoveRtsp(); } catch (Exception) { }
                        window.Close();
                    }
                }
            }
            finally { themes.SetTheme(ThemeMode.Dark); }
        }
    }
}
