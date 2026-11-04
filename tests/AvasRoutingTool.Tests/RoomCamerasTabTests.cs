using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Services.Visca;
using AvasRoutingTool.Tests.Fakes;
using AvasRoutingTool.ViewModels;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>The Room Cameras tab in the main window: order, view switching, Stop on leaving, deactivate and close.</summary>
    [Collection("WpfSta")]
    public class RoomCamerasTabTests
    {
        private static readonly byte[] StopPanTilt = { 0x81, 0x01, 0x06, 0x01, 0x01, 0x01, 0x03, 0x03, 0xFF };

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
            if (failure != null) throw new Xunit.Sdk.XunitException("Room Cameras tab failure: " + failure);
        }

        private static FrameworkElement Named(MainWindow w, string name) =>
            (FrameworkElement)w.FindName(name) ?? throw new InvalidOperationException($"No element named {name}");

        private static RadioButton Tab(MainWindow w, string name) => (RadioButton)Named(w, name);

        private static RoomCamerasViewModel Rooms(MainWindow w) => w.RoomCameras;

        /// <summary>Adds a camera that talks to a loopback fake, without writing the user's config file.</summary>
        private static RoomCameraViewModel AddLoopbackCamera(MainWindow w, FakeViscaServer server)
        {
            var vm = Rooms(w);
            var config = new RoomCameraConfig
            {
                Id = "tab-test",
                Name = "TAB-CAM",
                Host = "127.0.0.1",
                Port = server.Port,
                Transport = server.Kind,
            };
            // The view model's private factory path, so the camera is the real ViscaCamera; Persist is not called.
            var create = typeof(RoomCamerasViewModel).GetMethod("CreateCameraViewModel", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var cam = (RoomCameraViewModel)create.Invoke(vm, new object[] { config })!;
            vm.Cameras.Add(cam);
            vm.Selected = cam;
            return cam;
        }

        /// <summary>Pumps the dispatcher until the condition holds or five seconds pass.</summary>
        private static void WaitFor(Func<bool> condition, string because)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) throw new Xunit.Sdk.XunitException("Timed out waiting for: " + because);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                Thread.Sleep(20);
            }
        }

        [Fact]
        public void TabOrder_IsVisualRouting_RoomCameras_BavmConsole_Logs() => RunSta(w =>
        {
            var tabs = ((StackPanel)Tab(w, "TabVisualRouting").Parent).Children.OfType<RadioButton>().Select(r => r.Name).ToArray();
            Assert.Equal(new[] { "TabVisualRouting", "TabRoomCameras", "TabBavmConsole", "TabLogs", "TabAdvanced" }, tabs);
        });

        [Fact]
        public void SelectingRoomCamerasTab_ShowsOnlyThatView() => RunSta(w =>
        {
            Tab(w, "TabRoomCameras").IsChecked = true;

            Assert.Equal(Visibility.Visible, Named(w, "ViewRoomCameras").Visibility);
            Assert.Equal(Visibility.Collapsed, Named(w, "ViewVisualRouting").Visibility);
            Assert.Equal(Visibility.Collapsed, Named(w, "ViewBavmConsole").Visibility);
            Assert.Equal(Visibility.Collapsed, Named(w, "ViewLogs").Visibility);
        });

        [Fact]
        public void LeavingRoomCamerasTab_StopsMotionAndTabStartedPreview() => RunSta(w =>
        {
            using var server = new FakeViscaServer(ViscaTransportKind.Udp);
            var cam = AddLoopbackCamera(w, server);
            Tab(w, "TabRoomCameras").IsChecked = true;
            cam.BeginMove(PanTiltDirection.Left);
            WaitFor(() => server.Received.Count > 0 && !server.Received[^1].SequenceEqual(StopPanTilt), "a pan command reaches the camera");

            Tab(w, "TabVisualRouting").IsChecked = true;

            WaitFor(() => server.Received.Count > 0 && server.Received[^1].SequenceEqual(StopPanTilt), "Stop reaches the camera after leaving the tab");
        }, 1280, 860);

        [Fact]
        public void Deactivated_CallsStopImmediately() => RunSta(w =>
        {
            using var server = new FakeViscaServer(ViscaTransportKind.Udp);
            var cam = AddLoopbackCamera(w, server);
            Tab(w, "TabRoomCameras").IsChecked = true;
            cam.BeginMove(PanTiltDirection.Right);
            WaitFor(() => server.Received.Count > 0 && !server.Received[^1].SequenceEqual(StopPanTilt), "a pan command reaches the camera");

            typeof(Window).GetMethod("OnDeactivated", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(w, new object[] { EventArgs.Empty });

            WaitFor(() => server.Received.Count > 0 && server.Received[^1].SequenceEqual(StopPanTilt), "Stop reaches the camera on window deactivate");
        });

        [Fact]
        public void Closed_DisposesViewModel_Once() => RunSta(w =>
        {
            using var server = new FakeViscaServer(ViscaTransportKind.Udp);
            var cam = AddLoopbackCamera(w, server);
            var vm = Rooms(w);
            Assert.Contains(cam, vm.Cameras);

            w.Close();

            Assert.Empty(vm.Cameras);
            vm.Dispose();   // a second dispose is a no-op
            Assert.Empty(vm.Cameras);
        });

        [Fact]
        public void ThreeTabsFitAt1024() => RunSta(w =>
        {
            var bar = (FrameworkElement)Named(w, "AppBar");
            var visible = new[] { "TabVisualRouting", "TabRoomCameras", "TabBavmConsole", "TabLogs" }
                .Select(n => Tab(w, n)).Where(t => t.Visibility == Visibility.Visible).ToArray();
            Assert.Equal(3, visible.Length);

            var actionsLeft = Named(w, "ConnectionBadge").TranslatePoint(new Point(0, 0), w).X;
            foreach (var tab in visible)
            {
                var left = tab.TranslatePoint(new Point(0, 0), w).X;
                var right = tab.TranslatePoint(new Point(tab.ActualWidth, 0), w).X;
                Assert.True(left >= 0 && right <= bar.ActualWidth, $"{tab.Name} is outside the app bar: {left}-{right} of {bar.ActualWidth}");
                Assert.True(right <= actionsLeft, $"{tab.Name} overlaps the actions: right {right} > {actionsLeft}");
            }
        }, 1024, 720);
    }
}
