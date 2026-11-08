using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Rtsp;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Services;
using AvasRoutingTool.Services.Visca;
using AvasRoutingTool.Tests.Fakes;
using AvasRoutingTool.ViewModels;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class RoomCameraRtspPreviewTests : IDisposable
    {
        private const string FakeUrl = "rtsp://user:pass-fake@example.test:554/live/1?token=abc";
        private const string Mac = "aa:bb:cc:00:00:01";

        private sealed class MemoryConfig : IConfigService
        {
            public AppConfig Stored { get; set; } = new();
            public AppConfig Load() => JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(Stored))!;
            public void Save(AppConfig config) => Stored = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(config))!;
        }

        private sealed class CardCommands : ICommand
        {
            private readonly Func<bool> _can;
            private readonly Action _run;
            public event EventHandler? CanExecuteChanged { add { } remove { } }
            public CardCommands(Func<bool> can, Action run) { _can = can; _run = run; }
            public bool CanExecute(object? parameter) => _can();
            public void Execute(object? parameter) => _run();
        }

        private readonly MemoryConfig _config = new();
        private readonly FakePtzCameraFactory _factory = new();
        private readonly ObservableCollection<SourceCardViewModel> _sources = new();
        private readonly List<FakeRtspPlayer> _players = new();
        private readonly List<TaskCompletionSource<bool>> _delays = new();
        private DateTime _clock = new(2026, 11, 2, 10, 0, 0);
        private RoomCamerasViewModel? _sut;

        public void Dispose() => _sut?.Dispose();

        private static RoomCameraConfig Rtsp(string id, string url = FakeUrl, string name = "ROOM-CAM") =>
            new() { Id = id, Name = name, Host = "192.0.2.10", Port = 1259, PreviewMode = RoomCameraPreviewMode.Rtsp, RtspUrl = url };

        private RoomCamerasViewModel Build(params RoomCameraConfig[] cams)
        {
            _config.Stored.RoomCameras = cams.ToList();
            _sut = new RoomCamerasViewModel(_config, _factory, _sources, () => _clock,
                () => { var p = new FakeRtspPlayer(); _players.Add(p); return p; },
                (t, ct) =>
                {
                    var tcs = new TaskCompletionSource<bool>();
                    _delays.Add(tcs);
                    ct.Register(() => tcs.TrySetCanceled());
                    return tcs.Task;
                });
            return _sut;
        }

        [Fact]
        public void OldConfig_WithoutNewFields_LoadsAsTx()
        {
            var cfg = JsonSerializer.Deserialize<RoomCameraConfig>("{\"Id\":\"a\",\"Name\":\"N\",\"Host\":\"h\",\"Port\":52381}")!;
            Assert.Equal(RoomCameraPreviewMode.Tx, cfg.PreviewMode);
            Assert.Equal("", cfg.RtspUrl);
        }

        [Fact]
        public void ConfigJsonRoundTrip_KeepsModeAndUrl()
        {
            var json = JsonSerializer.Serialize(Rtsp("a"));
            Assert.Contains("\"Rtsp\"", json);
            var back = JsonSerializer.Deserialize<RoomCameraConfig>(json)!;
            Assert.Equal(RoomCameraPreviewMode.Rtsp, back.PreviewMode);
            Assert.Equal(FakeUrl, back.RtspUrl);
        }

        [Fact]
        public void Rtsp_StartPreview_GoesStartingThenLive()
        {
            var cam = Build(Rtsp("a")).Cameras[0];
            Assert.True(cam.IsRtsp);
            Assert.Equal(RoomCameraPreviewMode.Rtsp, cam.PreviewMode);
            Assert.Equal(CameraPreviewState.Standby, cam.Preview);
            Assert.True(cam.CanStartPreview);

            cam.StartPreview();
            Assert.Equal(CameraPreviewState.Starting, cam.Preview);
            Assert.Single(_players);
            Assert.Equal(FakeUrl, _players[0].Started.Single());

            _players[0].Raise(RtspPlayerEvent.Playing);
            Assert.Equal(CameraPreviewState.Live, cam.Preview);
            Assert.True(cam.ShowStopPreview);
        }

        [Fact]
        public void Rtsp_Dropped_ShowsReconnecting_ThenLive()
        {
            var cam = Build(Rtsp("a")).Cameras[0];
            cam.StartPreview();
            _players[0].Raise(RtspPlayerEvent.Playing);

            _players[0].Raise(RtspPlayerEvent.Dropped);
            Assert.Equal(CameraPreviewState.Reconnecting, cam.Preview);
            Assert.True(cam.RetryPreviewCommand.CanExecute(null));

            _delays[0].SetResult(true);
            for (int i = 0; i < 200 && cam.Preview != CameraPreviewState.Starting; i++) System.Threading.Thread.Sleep(10);
            Assert.Equal(CameraPreviewState.Starting, cam.Preview);
            _players[0].Raise(RtspPlayerEvent.Playing);
            Assert.Equal(CameraPreviewState.Live, cam.Preview);
            Assert.False(cam.RetryPreviewCommand.CanExecute(null));
        }

        [Fact]
        public void Rtsp_AuthFailed_ShowsFailed_RetryRestarts()
        {
            var cam = Build(Rtsp("a")).Cameras[0];
            cam.StartPreview();
            _players[0].Raise(RtspPlayerEvent.Failed(RtspFailure.AuthFailed));
            Assert.Equal(CameraPreviewState.Failed, cam.Preview);
            Assert.NotEmpty(cam.PreviewMessage);
            Assert.True(cam.RetryPreviewCommand.CanExecute(null));

            cam.RetryPreviewCommand.Execute(null);
            Assert.Equal(CameraPreviewState.Starting, cam.Preview);
            Assert.Equal(2, _players.Sum(p => p.Started.Count));
        }

        [Fact]
        public void Rtsp_EmptyUrl_NoSourceBound_StartDisabled()
        {
            var cam = Build(Rtsp("a", "")).Cameras[0];
            Assert.Equal(CameraPreviewState.NoSourceBound, cam.Preview);
            Assert.Equal("No RTSP address", cam.PreviewMessage);
            Assert.False(string.IsNullOrEmpty(cam.StartDisabledReason));
            Assert.False(cam.CanStartPreview);
            cam.StartPreview();
            Assert.Empty(_players);
        }

        [Fact]
        public void Rtsp_InvalidUrl_StartDisabledWithReason()
        {
            var cam = Build(Rtsp("a", "http://example.test/x")).Cameras[0];
            Assert.Equal(CameraPreviewState.NoSourceBound, cam.Preview);
            RtspUrl.TryParse("http://example.test/x", out _, out var reason);
            Assert.Equal(reason, cam.StartDisabledReason);
            Assert.Equal(reason, cam.PreviewMessage);
            Assert.False(cam.CanStartPreview);
        }

        [Fact]
        public void Rtsp_StopAll_StopsAndLateEventIgnored()
        {
            var sut = Build(Rtsp("a"));
            var cam = sut.Cameras[0];
            cam.StartPreview();
            _players[0].Raise(RtspPlayerEvent.Playing);

            sut.StopAll();
            Assert.Equal(CameraPreviewState.Standby, cam.Preview);
            Assert.True(_players[0].StopCount > 0);
            Assert.True(_players[0].Disposed);

            _players[0].Raise(RtspPlayerEvent.Playing);
            Assert.Equal(CameraPreviewState.Standby, cam.Preview);
        }

        [Fact]
        public void Rtsp_StopWhileConnecting_LatePlayingDoesNotRevive()
        {
            var cam = Build(Rtsp("a")).Cameras[0];
            cam.StartPreview();
            cam.StopPreview();
            _players[0].Raise(RtspPlayerEvent.Playing);
            Assert.Equal(CameraPreviewState.Standby, cam.Preview);
        }

        [Fact]
        public void Rtsp_Dispose_DisposesPlayer()
        {
            var sut = Build(Rtsp("a"));
            sut.Cameras[0].StartPreview();
            sut.Dispose();
            Assert.True(_players[0].Disposed);
        }

        [Fact]
        public void Rtsp_CaptionAndMessagesNeverContainCredentialsOrQuery()
        {
            var cam = Build(Rtsp("a")).Cameras[0];
            var seen = new List<string>();
            void Snap()
            {
                seen.Add(cam.CaptionText);
                seen.Add(cam.PreviewMessage);
                seen.Add(cam.StartDisabledReason ?? "");
                seen.Add(cam.AutomationName);
            }

            Snap();
            cam.StartPreview(); Snap();
            _players[0].Raise(RtspPlayerEvent.Playing); Snap();
            _players[0].Raise(RtspPlayerEvent.Dropped); Snap();
            _delays[0].SetResult(true); Snap();
            _players[0].Raise(RtspPlayerEvent.Failed(RtspFailure.AuthFailed, FakeUrl)); Snap();

            Assert.Contains("via RTSP", cam.CaptionText);
            foreach (var s in seen)
            {
                Assert.DoesNotContain("pass-fake", s);
                Assert.DoesNotContain("abc", s);
                Assert.DoesNotContain("user:", s);
            }
            foreach (var l in AvasRoutingTool.Logging.AppLogger.RecentLogs)
            {
                var text = l.ToString() ?? "";
                Assert.DoesNotContain("pass-fake", text);
                Assert.DoesNotContain("abc", text);
            }
        }

        [Fact]
        public void Rtsp_PtzControlsWorkWhileLostOrFailed()
        {
            var cam = Build(Rtsp("a")).Cameras[0];
            cam.StartPreview();
            _players[0].Raise(RtspPlayerEvent.Playing);
            _players[0].Raise(RtspPlayerEvent.Dropped);
            Assert.Equal(CameraPreviewState.Reconnecting, cam.Preview);
            Assert.True(cam.MotionEnabled);
            Assert.True(cam.HomeCommand.CanExecute(null));
            Assert.True(cam.MoveStartCommand.CanExecute(PanTiltDirection.Left));
            Assert.True(cam.ZoomStartCommand.CanExecute(ZoomDirection.Tele));
            Assert.True(cam.PowerCommand.CanExecute(null));

            cam.StopPreview();
            cam.StartPreview();
            _players[^1].Raise(RtspPlayerEvent.Failed(RtspFailure.AuthFailed));
            Assert.Equal(CameraPreviewState.Failed, cam.Preview);
            Assert.True(cam.MotionEnabled);
            Assert.True(cam.HomeCommand.CanExecute(null));
            Assert.True(cam.MoveStartCommand.CanExecute(PanTiltDirection.Up));
            cam.MoveStartCommand.Execute(PanTiltDirection.Up);
            Assert.NotEqual(MotionState.Idle, cam.MotionState);
        }

        [Fact]
        public void SwitchingMode_StopsTheOldPreviewFirst()
        {
            // RTSP -> TX
            var sut = Build(Rtsp("a"));
            sut.Cameras[0].StartPreview();
            _players[0].Raise(RtspPlayerEvent.Playing);
            sut.UpdateCamera(new RoomCameraConfig { Id = "a", Name = "ROOM-CAM", Host = "192.0.2.10", Port = 1259, BoundTxMac = Mac });
            Assert.True(_players[0].Disposed);
            Assert.False(sut.Cameras[0].IsRtsp);
            Assert.Equal(CameraPreviewState.SourceOffline, sut.Cameras[0].Preview);
            sut.Dispose();

            // TX -> RTSP
            _config.Stored = new AppConfig();
            var card = new SourceCardViewModel(new AvasDevice { DeviceId = Mac, DeviceName = "TX1", IsTransmitter = true, IsActive = true, MulticastIp = "239.1.1.1" });
            _sources.Add(card);
            int stops = 0;
            sut = Build(new RoomCameraConfig { Id = "b", Name = "T", Host = "192.0.2.11", Port = 1259, BoundTxMac = Mac });
            var tx = sut.Cameras[0];
            tx.StartCommandOf = c => new CardCommands(() => true, () => c.IsStreaming = true);
            tx.StopCommandOf = c => new CardCommands(() => c.IsStreaming, () => { stops++; c.IsStreaming = false; });
            tx.StartPreview();
            Assert.Equal(CameraPreviewState.Live, tx.Preview);

            sut.UpdateCamera(Rtsp("b"));
            Assert.Equal(1, stops);
            Assert.False(card.IsStreaming);
            Assert.True(sut.Cameras[0].IsRtsp);
        }

        [Fact]
        public void TwoRtspCameras_AreIndependent()
        {
            var sut = Build(Rtsp("a", name: "A"), Rtsp("b", name: "B"));
            sut.Cameras[0].StartPreview();
            sut.Cameras[1].StartPreview();
            Assert.Equal(2, _players.Count);
            _players[0].Raise(RtspPlayerEvent.Playing);
            _players[1].Raise(RtspPlayerEvent.Playing);

            _players[0].Raise(RtspPlayerEvent.Failed(RtspFailure.AuthFailed));
            Assert.Equal(CameraPreviewState.Failed, sut.Cameras[0].Preview);
            Assert.Equal(CameraPreviewState.Live, sut.Cameras[1].Preview);
            Assert.False(_players[1].Disposed);
        }
    }
}
