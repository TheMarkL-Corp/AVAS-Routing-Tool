using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using AvasRoutingTool.Rtsp;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Services;
using AvasRoutingTool.Services.Visca;
using AvasRoutingTool.Tests.Fakes;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// Interactive preview of the Room Cameras view in a real window, with real ViscaCamera instances talking over loopback to
    /// FakeViscaServer instances. Gated: the test returns at once (passes) unless AVAS_RC_PREVIEW=1, so normal runs skip it.
    ///
    /// Run (PowerShell, from the repo root):
    ///   $env:AVAS_RC_PREVIEW='1'; dotnet test tests/AvasRoutingTool.Tests/AvasRoutingTool.Tests.csproj -p:BaseOutputPath=bin/rc/ --filter FullyQualifiedName~RoomCamerasPreview_Interactive -- RunConfiguration.DisableParallelization=true
    ///
    /// Optional environment variables:
    ///   AVAS_RC_PREVIEW_THEME=light|dark          (default dark)
    ///   AVAS_RC_PREVIEW_AUTOCLOSE_SECONDS=N       close the window after N seconds (smoke runs; default: stay open until closed)
    ///
    /// What you see: three cameras. "Camera A (UDP)" and "Camera B (TCP)" connect to fake servers (chips show Connected);
    /// "Camera C (closed port)" points at a closed port (Not connected - retrying, then Cannot reach). A is bound to a live TX
    /// card (moving test pattern), B to a standby card. The strip under the view lists the VISCA bytes the fake cameras receive.
    /// Add / Edit / Remove in the editor dialog work and are kept in memory only (nothing is written to disk).
    /// "Start preview" on the standby card calls the real preview path and is not expected to work without a device.
    /// </summary>
    [Collection("WpfSta")]
    public class RoomCamerasPreviewHarness
    {
        private const string MacLive = "aa:bb:cc:00:00:01";
        private const string MacStandby = "aa:bb:cc:00:00:02";

        [Fact]
        public void RoomCamerasPreview_Interactive()
        {
            if (Environment.GetEnvironmentVariable("AVAS_RC_PREVIEW") != "1") return;

            var light = string.Equals(Environment.GetEnvironmentVariable("AVAS_RC_PREVIEW_THEME"), "light", StringComparison.OrdinalIgnoreCase);
            double autoClose = double.TryParse(Environment.GetEnvironmentVariable("AVAS_RC_PREVIEW_AUTOCLOSE_SECONDS"),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 0;

            Exception? failure = null;
            var t = new Thread(() =>
            {
                try
                {
                    InputModality.IsKeyboard = false;
                    TestResources.Ensure();
                    Run(light, autoClose);
                }
                catch (Exception ex) { failure = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("Room cameras preview failure: " + failure);
        }

        private static void Run(bool light, double autoCloseSeconds)
        {
            var themes = new ThemeService();
            if (light) themes.SetTheme(ThemeMode.Light);

            var servers = new List<FakeViscaServer>();
            var recent = new Queue<string>();
            var total = 0;
            Window? window = null;
            TextBlock? logText = null;

            void Append(string line)
            {
                recent.Enqueue(line);
                while (recent.Count > 8) recent.Dequeue();
                total++;
                if (logText != null) logText.Text = string.Join(Environment.NewLine, recent.Reverse());
            }

            FakeViscaServer Start(ViscaTransportKind kind, string label)
            {
                var server = new FakeViscaServer(kind);
                server.CommandReceived += message =>
                {
                    if (message.Length > 1 && message[1] == 0x09) return;   // polls (inquiries) would drown the commands
                    var hex = BitConverter.ToString(message).Replace('-', ' ');
                    var stamp = DateTime.Now.ToString("HH:mm:ss.fff");
                    window?.Dispatcher.BeginInvoke(new Action(() => Append($"{stamp}  {label}  {hex}")));
                };
                servers.Add(server);
                return server;
            }

            var udp = Start(ViscaTransportKind.Udp, "A (UDP)");
            var tcp = Start(ViscaTransportKind.Tcp, "B (TCP)");
            int closedPort;
            {
                var probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                closedPort = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();
            }

            var config = new PreviewMemoryConfig();
            config.Stored.RoomCameras = new List<RoomCameraConfig>
            {
                new() { Id = "a", Name = "Camera A (UDP)", Host = "127.0.0.1", Port = udp.Port, Transport = ViscaTransportKind.Udp, BoundTxMac = MacLive },
                new() { Id = "b", Name = "Camera B (TCP)", Host = "127.0.0.1", Port = tcp.Port, Transport = ViscaTransportKind.Tcp, BoundTxMac = MacStandby },
                new() { Id = "c", Name = "Camera C (closed port)", Host = "127.0.0.1", Port = closedPort, Transport = ViscaTransportKind.Tcp },
            };

            // Camera D previews an RTSP stream. AVAS_RTSP_URL (if set) is read here, used in memory only and never shown, logged or stored.
            var realUrl = Environment.GetEnvironmentVariable("AVAS_RTSP_URL");
            var useReal = !string.IsNullOrWhiteSpace(realUrl);
            FakeRtspServer? rtsp = null;
            RoomCamerasViewModel? roomVm = null;

            RoomCameraConfig CameraD(string url) => new()
            {
                Id = "d", Name = "Camera D (RTSP)", Host = "127.0.0.1", Port = tcp.Port, Transport = ViscaTransportKind.Tcp,
                PreviewMode = RoomCameraPreviewMode.Rtsp, RtspUrl = url,
            };

            // A new fake server has a new port, so Camera D is rebuilt through UpdateCamera with the new address (replaces the
            // camera view model, which stops the old preview). In real-address mode the buttons do nothing.
            void ServeRtsp(FakeRtspMode mode, bool drop = false)
            {
                if (useReal || roomVm == null) return;
                if (drop) { rtsp?.Drop(); return; }
                rtsp?.Dispose();
                rtsp = new FakeRtspServer(mode);
                roomVm.UpdateCamera(CameraD(FakeUrl(rtsp)));
            }

            if (useReal) config.Stored.RoomCameras.Add(CameraD(realUrl!));
            else
            {
                rtsp = new FakeRtspServer(FakeRtspMode.Normal);
                config.Stored.RoomCameras.Add(CameraD(FakeUrl(rtsp)));
            }

            var live = Card("TX1_live", MacLive, active: true);
            var standby = Card("TX2_standby", MacStandby, active: true);
            var offline = Card("TX3_offline", "aa:bb:cc:00:00:03", active: false);
            var sources = new ObservableCollection<SourceCardViewModel> { live, standby, offline };

            var bitmap = new WriteableBitmap(320, 180, 96, 96, PixelFormats.Bgr24, null);
            live.PreviewBitmap = bitmap;
            live.IsStreaming = true;
            var frame = 0;
            var pattern = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            pattern.Tick += (_, _) => Paint(bitmap, frame++);
            pattern.Start();

            var vm = new RoomCamerasViewModel(config, new PtzCameraFactory(), sources, () => DateTime.UtcNow);
            roomVm = vm;
            var view = new RoomCamerasView { DataContext = vm };

            logText = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 12, Text = "(no VISCA commands received yet - press a pad arrow)" };
            logText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            var strip = new StackPanel { Margin = new Thickness(16, 6, 16, 8) };
            var header = new TextBlock { Text = "Fake camera servers received (newest first, inquiries hidden):", FontSize = 11, Opacity = 0.7 };
            header.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            strip.Children.Add(header);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
            Button Btn(string text, Action click)
            {
                var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 4, 12, 4) };
                b.Click += (_, _) => click();
                return b;
            }
            buttons.Children.Add(Btn("RTSP: Normal", () => ServeRtsp(FakeRtspMode.Normal)));
            buttons.Children.Add(Btn("RTSP: 401", () => ServeRtsp(FakeRtspMode.Require401)));
            buttons.Children.Add(Btn("RTSP: Drop", () => ServeRtsp(FakeRtspMode.Normal, drop: true)));
            buttons.Children.Add(Btn("RTSP: Refuse", () => ServeRtsp(FakeRtspMode.RefuseConnection)));
            strip.Children.Add(buttons);
            strip.Children.Add(logText);

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(strip, 1);
            root.Children.Add(view);
            root.Children.Add(strip);

            window = new Window
            {
                Title = "Room Cameras preview (fake cameras)",
                Width = 1600,
                Height = 900,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = root,
            };
            window.SetResourceReference(Window.BackgroundProperty, "Brush.Background.Dark");

            DispatcherTimer? closer = null;
            if (autoCloseSeconds > 0)
            {
                closer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(autoCloseSeconds) };
                closer.Tick += (_, _) =>
                {
                    closer.Stop();
                    var states = string.Join(", ", vm.Cameras.Select(c => $"{c.Name}={c.Camera.LinkState}"));
                    Console.WriteLine("RC preview smoke: link states: " + states + "; commands seen: " + total);
                    var rtspCam = vm.Cameras.FirstOrDefault(c => c.IsRtsp);
                    if (rtspCam != null) Console.WriteLine("RC preview smoke: RTSP camera preview state: " + rtspCam.Preview);   // the state word only, never an address
                    window!.Close();
                };
                closer.Start();
                // Smoke runs press "Start preview" on the RTSP camera by themselves, one second after the window opens.
                var starter = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                starter.Tick += (_, _) => { starter.Stop(); vm.Cameras.FirstOrDefault(c => c.IsRtsp)?.StartPreview(); };
                starter.Start();
            }

            try
            {
                window.ShowDialog();
            }
            finally
            {
                closer?.Stop();
                pattern.Stop();
                try { vm.Dispose(); } catch (Exception) { }
                foreach (var server in servers) server.Dispose();
                rtsp?.Dispose();
                if (light) themes.SetTheme(ThemeMode.Dark);
            }
        }

        [Fact]
        public void RoomCamerasRtspPreview_Screenshots()
        {
            if (Environment.GetEnvironmentVariable("AVAS_RTSP_SHOTS") != "1" || Environment.GetEnvironmentVariable("AVAS_RTSP_REAL") != "1") return;

            Exception? failure = null;
            var t = new Thread(() =>
            {
                try { InputModality.IsKeyboard = false; TestResources.Ensure(); RunShots(); }
                catch (Exception ex) { failure = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("Room cameras RTSP shots failure: " + failure);
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

        private static string ShotsFolder()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "docs")))
                dir = dir.Parent;
            var folder = Path.Combine(dir?.FullName ?? Directory.GetCurrentDirectory(), "ui_screenshots", "UI-v2-rtsp-room-cameras");
            Directory.CreateDirectory(folder);
            return folder;
        }

        private static void Flush(FrameworkElement view)
        {
            for (var i = 0; i < 3; i++)
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                view.UpdateLayout();
            }
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
            using var visca = new FakeViscaServer(ViscaTransportKind.Tcp);
            try
            {
                foreach (var light in new[] { false, true })
                foreach (var (w, h) in new[] { (1920.0, 1080.0), (1024.0, 720.0) })
                {
                    var prefix = (light ? "light" : "dark") + "-" + (int)w + "x" + (int)h + "-";
                    foreach (var state in new[] { "Standby", "Live", "Reconnecting", "Failed" })
                    {
                        themes.SetTheme(light ? ThemeMode.Light : ThemeMode.Dark);
                        var name = prefix + state + ".png";
                        using var server = new FakeRtspServer(state == "Failed" ? FakeRtspMode.Require401 : FakeRtspMode.Normal);
                        var config = new PreviewMemoryConfig();
                        config.Stored.RoomCameras = new List<RoomCameraConfig>
                        {
                            new() { Id = "d", Name = "Camera D (RTSP)", Host = "127.0.0.1", Port = visca.Port, Transport = ViscaTransportKind.Tcp,
                                    PreviewMode = RoomCameraPreviewMode.Rtsp, RtspUrl = FakeUrl(server) },
                        };
                        var sources = new ObservableCollection<SourceCardViewModel> { Card("TX1_live", MacLive, active: true) };
                        // Reconnecting must stay Reconnecting while the picture is taken: the retry delay never completes.
                        Func<TimeSpan, CancellationToken, Task>? delay = state == "Reconnecting" ? (_, ct) => Task.Delay(-1, ct) : null;
                        var vm = new RoomCamerasViewModel(config, new PtzCameraFactory(), sources, () => DateTime.UtcNow,
                            () => new LibVlcStreamPlayer(), delay);
                        var view = new RoomCamerasView { DataContext = vm };
                        var window = new Window { Content = view, Width = w, Height = h, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000 };
                        window.SetResourceReference(Window.BackgroundProperty, "Brush.Background.Dark");
                        window.Show();
                        try
                        {
                            themes.SetTheme(light ? ThemeMode.Light : ThemeMode.Dark);
                            vm.Selected = vm.Cameras[0];
                            Pump(TimeSpan.FromMilliseconds(300));
                            var cam = vm.Cameras[0];
                            if (state != "Standby")
                            {
                                // The page stops its preview when another window takes focus (the existing StopAll rule), which can happen
                                // while shots are taken on a busy desktop: start again, up to three times, before giving up.
                                var want = state == "Failed" ? CameraPreviewState.Failed : CameraPreviewState.Live;
                                var done = false;
                                for (var attempt = 1; attempt <= 3 && !done; attempt++)
                                {
                                    if (cam.Preview == CameraPreviewState.Standby) cam.StartPreview();
                                    if (!PumpUntil(() => cam.Preview == want, TimeSpan.FromSeconds(20))) continue;
                                    if (want == CameraPreviewState.Live) Pump(TimeSpan.FromSeconds(1));   // a few frames
                                    if (state == "Reconnecting")
                                    {
                                        server.Drop();
                                        if (!PumpUntil(() => cam.Preview == CameraPreviewState.Reconnecting, TimeSpan.FromSeconds(15))) continue;
                                    }
                                    done = true;
                                }
                                Assert.True(done, name + ": never reached the wanted state, is " + cam.Preview);
                            }
                            Pump(TimeSpan.FromMilliseconds(300));
                            view.UpdateLayout();
                            Flush(view);
                            SavePng((FrameworkElement)VisualTreeHelper.GetChild(window, 0), Path.Combine(folder, name));
                        }
                        finally
                        {
                            try { vm.Dispose(); } catch (Exception) { }
                            window.Close();
                        }
                    }

                    // The editor dialog in RTSP mode with a fake address typed.
                    {
                        themes.SetTheme(light ? ThemeMode.Light : ThemeMode.Dark);
                        var config = new PreviewMemoryConfig();
                        var sources = new ObservableCollection<SourceCardViewModel> { Card("TX1_live", MacLive, active: true) };
                        var vm = new RoomCamerasViewModel(config, new PtzCameraFactory(), sources, () => DateTime.UtcNow);
                        var editor = vm.CreateEditor(null);
                        editor.Name = "Camera D (RTSP)";
                        editor.Host = "127.0.0.1";
                        editor.PreviewMode = RoomCameraPreviewMode.Rtsp;
                        editor.RtspUrl = "rtsp://127.0.0.1:8554/stream";
                        var dialog = new CameraEditorDialog(editor) { ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = 0 };
                        dialog.SetResourceReference(Window.BackgroundProperty, "Brush.Background.Dark");
                        dialog.Show();
                        try
                        {
                            Pump(TimeSpan.FromMilliseconds(400));
                            var content = (FrameworkElement)VisualTreeHelper.GetChild(dialog, 0);
                            Flush(content);
                            SavePng(content, Path.Combine(folder, prefix + "editor-rtsp.png"));
                        }
                        finally { dialog.Close(); try { vm.Dispose(); } catch (Exception) { } }
                    }
                }
            }
            finally { themes.SetTheme(ThemeMode.Dark); }
        }

        private static SourceCardViewModel Card(string name, string mac, bool active) =>
            new(new AvasDevice { DeviceId = mac, DeviceName = name, VendorId = 105, IsTransmitter = true, IsActive = active, MulticastIp = "239.1.1.1" });

        /// <summary>A moving test pattern: colour bars scrolling sideways with a bouncing white square.</summary>
        private static void Paint(WriteableBitmap bitmap, int frame)
        {
            int w = bitmap.PixelWidth, h = bitmap.PixelHeight, stride = w * 3;
            var bars = new (byte B, byte G, byte R)[] { (255, 255, 255), (0, 255, 255), (255, 255, 0), (0, 255, 0), (255, 0, 255), (0, 0, 255), (255, 0, 0), (40, 40, 40) };
            var buffer = new byte[stride * h];
            int boxX = Math.Abs((frame * 4) % (2 * (w - 40)) - (w - 40)), boxY = Math.Abs((frame * 3) % (2 * (h - 40)) - (h - 40));
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var c = bars[((x + frame * 3) / 40) % bars.Length];
                    if (x >= boxX && x < boxX + 40 && y >= boxY && y < boxY + 40) c = (255, 255, 255);
                    var i = y * stride + x * 3;
                    buffer[i] = c.B; buffer[i + 1] = c.G; buffer[i + 2] = c.R;
                }
            bitmap.WritePixels(new Int32Rect(0, 0, w, h), buffer, stride, 0);
        }

        private sealed class PreviewMemoryConfig : IConfigService
        {
            public AppConfig Stored { get; set; } = new();
            public AppConfig Load() => Stored;
            public void Save(AppConfig config) => Stored = config;
        }
    }
}
