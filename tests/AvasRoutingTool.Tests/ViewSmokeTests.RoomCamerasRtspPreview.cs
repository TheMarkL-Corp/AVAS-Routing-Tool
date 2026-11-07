using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Rtsp;
using AvasRoutingTool.Services;
using AvasRoutingTool.Tests.Fakes;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>RC3: the Room Cameras picture area for a camera whose preview is an RTSP stream (fake player, fake URL only).</summary>
    [Collection("WpfSta")]
    public class RoomCamerasRtspPreviewViewTests
    {
        private const string FakeUrl = "rtsp://user:pass-fake@example.test:554/live/1?token=abc";

        private sealed class Rig : IDisposable
        {
            public RoomCamerasMemoryConfig Config { get; } = new();
            public FakePtzCameraFactory Factory { get; } = new();
            public List<FakeRtspPlayer> Players { get; } = new();
            public RoomCamerasViewModel Vm { get; }
            public RoomCamerasView View { get; }
            public Window Window { get; }

            public Rig(params RoomCameraConfig[] cams)
            {
                Config.Stored.RoomCameras = cams.ToList();
                Vm = new RoomCamerasViewModel(Config, Factory, new ObservableCollection<SourceCardViewModel>(), () => new DateTime(2026, 11, 2, 10, 0, 0),
                    () => { var p = new FakeRtspPlayer(); Players.Add(p); return p; },
                    (_, ct) => { var tcs = new TaskCompletionSource<bool>(); ct.Register(() => tcs.TrySetCanceled()); return tcs.Task; });
                View = new RoomCamerasView { DataContext = Vm, Width = 1920, Height = 1080 };
                Window = new Window { Content = View, SizeToContent = SizeToContent.WidthAndHeight, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Left = -10000, Top = 0 };
                Window.Show();
                Relayout();
            }

            public RoomCameraViewModel Cam => Vm.Cameras[0];

            public void Relayout()
            {
                View.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                View.UpdateLayout();
            }

            public static List<T> All<T>(DependencyObject root) where T : DependencyObject
            {
                var found = new List<T>();
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                {
                    var child = VisualTreeHelper.GetChild(root, i);
                    if (child is T m) found.Add(m);
                    found.AddRange(All<T>(child));
                }
                return found;
            }

            public T Named<T>(string name) where T : FrameworkElement => All<FrameworkElement>(View).OfType<T>().First(e => e.Name == name);
            public bool Shown(string name) => Named<FrameworkElement>(name).IsVisible;
            public string Text(string panel) => string.Join(" ", All<TextBlock>(Named<FrameworkElement>(panel)).Select(t => t.Text));

            public void Dispose() { Window.Close(); Vm.Dispose(); }
        }

        private static RoomCameraConfig RtspCam() =>
            new() { Id = "a", Name = "ROOM-CAM", Host = "192.0.2.10", Port = 1259, PreviewMode = RoomCameraPreviewMode.Rtsp, RtspUrl = FakeUrl };

        private static void RunSta(Action body)
        {
            Exception? failure = null;
            var t = new Thread(() =>
            {
                try
                {
                    InputModality.IsKeyboard = false;
                    // The shared WPF-UI dictionaries belong to the thread that built them; rebuild them on this thread so ProgressRing can be created.
                    TestResources.Ensure().Resources.MergedDictionaries.Clear();
                    TestResources.Ensure();
                    body();
                }
                catch (Exception ex) { failure = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("Room cameras RTSP view failure: " + failure);
        }

        private static readonly string[] Panels = { "StateNoSource", "StateOffline", "StateStandby", "StateStarting", "StateReconnecting", "StateFailed" };

        private static void OnlyPanel(Rig rig, string expected)
        {
            foreach (var p in Panels) Assert.True(rig.Shown(p) == (p == expected), $"{p} visibility wrong while expecting {expected}");
        }

        private static WriteableBitmap Bitmap() => new(4, 4, 96, 96, PixelFormats.Bgra32, null);

        [Fact]
        public void EachState_ShowsItsOwnPanelAndMessage() =>
            RunSta(() =>
            {
                using var rig = new Rig(RtspCam());
                OnlyPanel(rig, "StateStandby");
                Assert.True(rig.Shown("StartPreviewButton"));
                Assert.False(rig.Shown("RetryPreviewButton"));
                Assert.Contains("via RTSP", rig.Named<TextBlock>("CaptionText").Text);

                rig.Cam.StartPreview();
                rig.Relayout();
                OnlyPanel(rig, "StateStarting");
                Assert.Contains(rig.Cam.PreviewMessage, rig.Text("StateStarting"));

                rig.Players[0].SwapBitmap(Bitmap());
                rig.Players[0].Raise(RtspPlayerEvent.Playing);
                rig.Relayout();
                OnlyPanel(rig, "none");
                Assert.True(rig.Shown("LiveImage"));
                Assert.Same(rig.Players[0].Bitmap, rig.Named<Image>("LiveImage").Source);
                Assert.True(rig.Shown("StopPreviewButton"));

                rig.Players[0].Raise(RtspPlayerEvent.Dropped);
                rig.Relayout();
                OnlyPanel(rig, "StateReconnecting");
                Assert.Contains("Reconnecting", rig.Text("StateReconnecting"));
                Assert.False(string.IsNullOrEmpty(rig.Cam.PreviewMessage));
                Assert.Contains(rig.Cam.PreviewMessage, rig.Text("StateReconnecting"));
                Assert.False(rig.Shown("RetryPreviewButton"));

                rig.Players[0].Raise(RtspPlayerEvent.Failed(RtspFailure.AuthFailed));
                rig.Relayout();
                OnlyPanel(rig, "StateFailed");
                Assert.False(string.IsNullOrEmpty(rig.Cam.PreviewMessage));
                Assert.Contains(rig.Cam.PreviewMessage, rig.Text("StateFailed"));
                Assert.True(rig.Shown("RetryPreviewButton"));
            });

        [Fact]
        public void RetryButton_RunsRetryPreviewCommand_AndStartsThePlayerAgain() =>
            RunSta(() =>
            {
                using var rig = new Rig(RtspCam());
                rig.Cam.StartPreview();
                rig.Players[0].Raise(RtspPlayerEvent.Failed(RtspFailure.AuthFailed));
                rig.Relayout();
                var retry = rig.Named<Button>("RetryPreviewButton");
                Assert.True(retry.IsVisible);
                Assert.Equal("Retry preview", AutomationProperties.GetName(retry));
                Assert.Equal((double)Application.Current.FindResource("Size.Touch.Min"), retry.MinHeight);
                Assert.Same(rig.Cam.RetryPreviewCommand, retry.Command);
                retry.Command.Execute(null);
                rig.Relayout();
                Assert.Equal(2, rig.Players.Sum(p => p.Started.Count));
                Assert.False(retry.IsVisible);
            });

        [Fact]
        public void PtzControls_StayEnabled_WhileReconnectingAndFailed() =>
            RunSta(() =>
            {
                using var rig = new Rig(RtspCam());
                rig.Cam.StartPreview();
                rig.Players[0].Raise(RtspPlayerEvent.Playing);
                rig.Players[0].Raise(RtspPlayerEvent.Dropped);
                rig.Relayout();
                Assert.Equal(CameraPreviewState.Reconnecting, rig.Cam.Preview);
                AssertPtzEnabled(rig);
                rig.Players[0].Raise(RtspPlayerEvent.Failed(RtspFailure.NotFound));
                rig.Relayout();
                Assert.Equal(CameraPreviewState.Failed, rig.Cam.Preview);
                AssertPtzEnabled(rig);
            });

        private static void AssertPtzEnabled(Rig rig)
        {
            Assert.True(rig.Cam.MotionEnabled);
            Assert.True(rig.Named<FrameworkElement>("Pad").IsEnabled);
            Assert.True(rig.Named<Slider>("ZoomSlider").IsEnabled);
            Assert.True(rig.Named<Slider>("FocusSlider").IsEnabled);
            Assert.True(rig.Named<FrameworkElement>("SpeedSegment").IsEnabled);
        }

        [Fact]
        public void PictureArea_NeverShowsCredentialsOrQuery() =>
            RunSta(() =>
            {
                using var rig = new Rig(RtspCam());
                void Check()
                {
                    var strings = new List<string>();
                    foreach (var root in new FrameworkElement[] { rig.Named<FrameworkElement>("PictureArea"), rig.Named<FrameworkElement>("CaptionRow") })
                    {
                        foreach (var e in Rig.All<FrameworkElement>(root).Append(root))
                        {
                            if (e is TextBlock tb) strings.Add(tb.Text);
                            if (e is ContentControl cc && cc.Content is string s) strings.Add(s);
                            strings.Add(AutomationProperties.GetName(e) ?? "");
                            strings.Add(e.ToolTip?.ToString() ?? "");
                        }
                    }
                    foreach (var s in strings)
                    {
                        Assert.DoesNotContain("pass-fake", s);
                        Assert.DoesNotContain("abc", s);
                    }
                }
                Check();
                rig.Cam.StartPreview(); rig.Relayout(); Check();
                rig.Players[0].Raise(RtspPlayerEvent.Playing); rig.Relayout(); Check();
                rig.Players[0].Raise(RtspPlayerEvent.Dropped); rig.Relayout(); Check();
                rig.Players[0].Raise(RtspPlayerEvent.Failed(RtspFailure.AuthFailed)); rig.Relayout(); Check();
            });

        [Fact]
        public void TxCamera_StillShowsExactlyItsOldPanels() =>
            RunSta(() =>
            {
                using var rig = new Rig(new RoomCameraConfig { Id = "t", Name = "TX-CAM", Host = "192.0.2.11", Port = 1259 });
                OnlyPanel(rig, "StateNoSource");
                Assert.False(rig.Shown("RetryPreviewButton"));
                Assert.DoesNotContain("RTSP", rig.Named<TextBlock>("CaptionText").Text);
            });
    }
}
