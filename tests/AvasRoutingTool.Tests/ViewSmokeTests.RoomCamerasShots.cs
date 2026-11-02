using System;
using System.IO;
using System.Windows;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Services;
using AvasRoutingTool.Services.Visca;
using AvasRoutingTool.ViewModels;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// Screenshots of the Room Cameras view (on demand): set UIV2_SHOTS (AVAS_UIV2_SHOTS also works) to the output folder and run
    /// <c>--filter FullyQualifiedName~UiV2_RoomCamerasScreenshots</c>. Without the variable the test does nothing.
    /// States: picture live with the camera moving, standby, no source bound, source offline, locked, Stop not confirmed, empty config;
    /// each at 1920x1080 and 1024x720 in Dark and Light.
    /// </summary>
    public partial class ViewSmokeTests
    {
        [Fact]
        public void UiV2_RoomCamerasScreenshots()
        {
            var folder = Environment.GetEnvironmentVariable("UIV2_SHOTS") ?? Environment.GetEnvironmentVariable("AVAS_UIV2_SHOTS");
            if (string.IsNullOrWhiteSpace(folder)) return;
            Directory.CreateDirectory(folder);

            RunSta(() =>
            {
                var themeService = new ThemeService();
                try
                {
                    foreach (var light in new[] { false, true })
                    foreach (var (w, h) in new[] { (1920.0, 1080.0), (1024.0, 720.0) })
                    {
                        themeService.SetTheme(light ? ThemeMode.Light : ThemeMode.Dark);
                        var suffix = $"{(int)w}x{(int)h}-{(light ? "light" : "dark")}";

                        void Scene(string name, Action<RoomCamerasRig> arrange, bool empty = false)
                        {
                            var cameras = empty
                                ? Array.Empty<RoomCameraConfig>()
                                : new[] { RoomCamerasRig.Cam("a", "ROOM-CAM", RoomCamerasRig.Mac), RoomCamerasRig.Cam("b", "OR-2 ceiling"), RoomCamerasRig.Cam("c", "Anaesthesia corner", "aa:bb:cc:00:00:99") };
                            using var rig = new RoomCamerasRig(w, h, cameras, new[] { RoomCamerasRig.Card() });
                            themeService.SetTheme(light ? ThemeMode.Light : ThemeMode.Dark);
                            arrange(rig);
                            rig.Relayout();
                            SavePng(ClientRoot(rig.Window), Path.Combine(folder, $"{name}-{suffix}.png"));
                        }

                        Scene("rc-01-live-moving", rig =>
                        {
                            var card = rig.Sources[0];
                            card.PreviewBitmap = new System.Windows.Media.Imaging.WriteableBitmap(64, 36, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null);
                            card.IsStreaming = true;
                            card.Fps = 29.9;
                            rig.Vm.Selected!.BeginMove(PanTiltDirection.UpLeft);
                        });
                        Scene("rc-02-standby", rig => { });
                        Scene("rc-03-no-source", rig => rig.Vm.Selected = rig.Vm.Cameras[1]);
                        Scene("rc-04-source-offline", rig => rig.Vm.Selected = rig.Vm.Cameras[2]);
                        Scene("rc-05-locked", rig => rig.Vm.Selected!.IsLocked = true);
                        Scene("rc-06-stop-not-confirmed", rig =>
                        {
                            var camera = rig.Vm.Selected!;
                            rig.Fake().FailNextStops(20);
                            camera.BeginMove(PanTiltDirection.Up);
                            camera.EndMove();
                            for (var i = 0; i < 12 && camera.MotionState != MotionState.StopNotConfirmed; i++)
                            {
                                rig.Clock = rig.Clock.AddSeconds(3);
                                camera.Tick();
                                RoomCamerasRig.Idle();
                            }
                        });
                        Scene("rc-07-empty", rig => { }, empty: true);
                    }
                }
                finally
                {
                    themeService.SetTheme(ThemeMode.Dark);
                }
            });
        }
    }
}
