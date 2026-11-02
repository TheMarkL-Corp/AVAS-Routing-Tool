using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Windows.Input;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Tests.Fakes;
using AvasRoutingTool.ViewModels;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class RoomCamerasViewModelTests : IDisposable
    {
        private const string Mac = "aa:bb:cc:00:00:01";

        /// <summary>In-memory config file: Load returns a fresh deep copy each time, like reading the file.</summary>
        private sealed class MemoryConfig : IConfigService
        {
            public AppConfig Stored { get; set; } = new();
            public int Loads { get; private set; }
            public int Saves { get; private set; }

            public AppConfig Load()
            {
                Loads++;
                return JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(Stored))!;
            }

            public void Save(AppConfig config)
            {
                Saves++;
                Stored = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(config))!;
            }
        }

        /// <summary>Stands in for the card's real start/stop (which need a live SDVoE client).</summary>
        private sealed class CardCommands : ICommand
        {
            private readonly Func<bool> _canExecute;
            private readonly Action _execute;
            public int Executed { get; private set; }
            public event EventHandler? CanExecuteChanged { add { } remove { } }
            public CardCommands(Func<bool> canExecute, Action execute) { _canExecute = canExecute; _execute = execute; }
            public bool CanExecute(object? parameter) => _canExecute();
            public void Execute(object? parameter) { Executed++; _execute(); }
        }

        private readonly MemoryConfig _config = new();
        private readonly FakePtzCameraFactory _factory = new();
        private readonly ObservableCollection<SourceCardViewModel> _sources = new();
        private DateTime _clock = new(2026, 11, 2, 10, 0, 0);
        private readonly List<CardCommands> _starts = new();
        private readonly List<CardCommands> _stops = new();
        private RoomCamerasViewModel? _sut;

        public void Dispose() => _sut?.Dispose();

        private static RoomCameraConfig Cam(string id, string name = "ROOM-CAM", string mac = "") =>
            new() { Id = id, Name = name, Host = "192.168.1.163", Port = 1259, BoundTxMac = mac };

        private static SourceCardViewModel Card(string mac = Mac, string name = "TX1", bool multicast = true, bool active = true)
        {
            var device = new AvasDevice { DeviceId = mac, DeviceName = name, IsTransmitter = true, IsActive = active };
            if (multicast)
                device.MulticastIp = "239.1.1.1";
            return new SourceCardViewModel(device);
        }

        private RoomCamerasViewModel Build(params RoomCameraConfig[] cameras)
        {
            _config.Stored.RoomCameras = cameras.ToList();
            _sut = new RoomCamerasViewModel(_config, _factory, _sources, () => _clock);
            foreach (var cam in _sut.Cameras)
                Wire(cam);
            return _sut;
        }

        /// <summary>Routes the camera's preview start/stop to recording fakes that flip the card's IsStreaming.</summary>
        private void Wire(RoomCameraViewModel cam)
        {
            cam.StartCommandOf = card =>
            {
                var cmd = new CardCommands(() => card.StartPreviewCommand.CanExecute(null), () => card.IsStreaming = true);
                _starts.Add(cmd);
                return cmd;
            };
            cam.StopCommandOf = card =>
            {
                var cmd = new CardCommands(() => card.IsStreaming, () => card.IsStreaming = false);
                _stops.Add(cmd);
                return cmd;
            };
        }

        [Fact]
        public void Loads_CamerasFromConfig_InOrder()
        {
            var sut = Build(Cam("b", "Second"), Cam("a", "First"), Cam("c", "Third"));

            Assert.Equal(new[] { "b", "a", "c" }, sut.Cameras.Select(c => c.Id));
            Assert.Equal(3, _factory.Created.Count);
            Assert.Same(sut.Cameras[0], sut.Selected);
        }

        [Fact]
        public void Add_Edit_Remove_Persist_ThroughReadModifyWrite()
        {
            var sut = Build(Cam("a", "First"));
            int loadsBefore = _config.Loads;

            // Another code path (theme toggle / Settings) saves between our construction and our write.
            _config.Stored.Theme = "Light";
            sut.AddCamera(Cam("b", "Second"));
            Assert.Equal("Light", _config.Stored.Theme);
            Assert.Equal(new[] { "a", "b" }, _config.Stored.RoomCameras.Select(c => c.Id));
            Assert.Equal(new[] { "a", "b" }, sut.Cameras.Select(c => c.Id));
            Assert.True(_config.Loads > loadsBefore);

            _config.Stored.ShowLogs = !_config.Stored.ShowLogs;
            bool logs = _config.Stored.ShowLogs;
            var edited = Cam("b", "Renamed");
            edited.Port = 3001;
            sut.UpdateCamera(edited);
            Assert.Equal(logs, _config.Stored.ShowLogs);
            Assert.Equal("Renamed", _config.Stored.RoomCameras[1].Name);
            Assert.Equal(3001, _config.Stored.RoomCameras[1].Port);
            Assert.Equal("Renamed", sut.Cameras[1].Name);
            Assert.True(_factory.Created[1].Disposed);   // the replaced camera was disposed

            _config.Stored.Theme = "Dark";
            var removed = sut.Cameras[0].Camera as FakePtzCamera;
            sut.RemoveCamera("a");
            Assert.Equal("Dark", _config.Stored.Theme);
            Assert.Equal(new[] { "b" }, _config.Stored.RoomCameras.Select(c => c.Id));
            Assert.Equal(new[] { "b" }, sut.Cameras.Select(c => c.Id));
            Assert.True(removed!.Disposed);
        }

        [Fact]
        public void Binding_ResolvesByMac_AndFollowsSourcesRebuild()
        {
            var card = Card();
            _sources.Add(card);
            var sut = Build(Cam("a", mac: Mac));
            var cam = sut.Cameras[0];
            card.IsStreaming = true;   // the app auto-started the preview
            Assert.Equal(CameraPreviewState.Live, cam.Preview);
            Assert.False(cam.ShowStopPreview);   // not ours

            // Refresh: every card is rebuilt.
            _sources.Clear();
            Assert.Equal(CameraPreviewState.SourceOffline, cam.Preview);
            var rebuilt = Card();
            rebuilt.IsStreaming = true;
            _sources.Add(rebuilt);

            Assert.Equal(CameraPreviewState.Live, cam.Preview);
            Assert.Equal(0, _starts.Sum(s => s.Executed));   // no second stream
            // The old card is no longer listened to.
            card.IsStreaming = false;
            Assert.Equal(CameraPreviewState.Live, cam.Preview);
            // The new one is.
            rebuilt.Fps = 29.9;
            Assert.Equal("ROOM-CAM · via TX1 · 29.9 fps", cam.CaptionText);
        }

        [Fact]
        public void Binding_ToUnknownMac_IsOfflineState_NotException()
        {
            _sources.Add(Card("other-mac"));
            var sut = Build(Cam("a", mac: "gone-mac"));
            var cam = sut.Cameras[0];

            Assert.Equal(CameraPreviewState.SourceOffline, cam.Preview);
            Assert.False(cam.CanStartPreview);
            cam.StartPreview();   // harmless
            Assert.Equal(CameraPreviewState.SourceOffline, cam.Preview);

            _sources.Add(Card("gone-mac", "TX9"));
            Assert.Equal(CameraPreviewState.Standby, cam.Preview);
            Assert.Equal("TX9", cam.SourceName);
        }

        [Fact]
        public void Unbound_ShowsNoSourceBound_ControlStillWorks()
        {
            _sources.Add(Card());
            var sut = Build(Cam("a", "ROOM-CAM", mac: ""));
            var cam = sut.Cameras[0];

            Assert.Equal(CameraPreviewState.NoSourceBound, cam.Preview);
            Assert.Equal("No source bound to ROOM-CAM", cam.PreviewMessage);
            Assert.False(cam.CanStartPreview);

            var fake = (FakePtzCamera)cam.Camera;
            cam.BeginMove(AvasRoutingTool.Services.Visca.PanTiltDirection.Left);
            Assert.Contains(fake.Calls, c => c.StartsWith("Move:Left"));
        }

        [Fact]
        public void StartPreview_OnlyWhenStandby_AndDisabledWithoutMulticast()
        {
            var noMulticast = Card("m2", "TX2", multicast: false);
            var card = Card();
            _sources.Add(card);
            _sources.Add(noMulticast);
            var sut = Build(Cam("a", mac: Mac), Cam("b", mac: "m2"));
            var cam = sut.Cameras[0];
            var cam2 = sut.Cameras[1];

            Assert.Equal(CameraPreviewState.Standby, cam.Preview);
            Assert.Equal("Preview standby", cam.PreviewMessage);
            Assert.True(cam.CanStartPreview);

            Assert.Equal(CameraPreviewState.Standby, cam2.Preview);
            Assert.False(cam2.CanStartPreview);
            Assert.Equal("No multicast address", cam2.StartDisabledReason);
            cam2.StartPreview();
            Assert.Equal(0, _starts.Sum(s => s.Executed));

            cam.StartPreview();
            Assert.Equal(1, _starts.Sum(s => s.Executed));
            Assert.Equal(CameraPreviewState.Live, cam.Preview);
            Assert.True(cam.ShowStopPreview);

            // Not in standby any more: a second Start does nothing.
            cam.StartPreview();
            Assert.Equal(1, _starts.Sum(s => s.Executed));

            // Starting is shown while the card refreshes.
            card.IsStreaming = false;
            card.IsRefreshing = true;
            Assert.Equal(CameraPreviewState.Starting, cam.Preview);
            Assert.Equal("Starting preview...", cam.PreviewMessage);
            Assert.False(cam.CanStartPreview);
        }

        [Fact]
        public void TabStartedStream_IsStopped_OnStopAll_ButForeignStream_IsNot()
        {
            var ours = Card(Mac, "TX1");
            var foreign = Card("m2", "TX2");
            _sources.Add(ours);
            _sources.Add(foreign);
            var sut = Build(Cam("a", mac: Mac), Cam("b", mac: "m2"));
            foreign.IsStreaming = true;   // auto-started by the app
            sut.Cameras[0].StartPreview();
            Assert.True(ours.IsStreaming);
            Assert.True(sut.Cameras[0].ShowStopPreview);
            Assert.False(sut.Cameras[1].ShowStopPreview);

            sut.StopAll();

            Assert.False(ours.IsStreaming);
            Assert.True(foreign.IsStreaming);
            Assert.Equal(1, _stops.Sum(s => s.Executed));

            // Stopping again never touches a stream we no longer own.
            ours.IsStreaming = true;
            sut.StopAll();
            Assert.True(ours.IsStreaming);
        }

        [Fact]
        public void SelectingAnotherCamera_StopsMotionOfThePrevious()
        {
            var sut = Build(Cam("a"), Cam("b"));
            var first = sut.Cameras[0];
            var second = sut.Cameras[1];
            first.BeginMove(AvasRoutingTool.Services.Visca.PanTiltDirection.Up);

            sut.SelectCommand.Execute(second);

            Assert.Same(second, sut.Selected);
            Assert.Contains("StopMove", ((FakePtzCamera)first.Camera).Calls);
            Assert.DoesNotContain("StopMove", ((FakePtzCamera)second.Camera).Calls);
        }

        [Fact]
        public void Add_And_Edit_Commands_RaiseEvents_AndTickReachesEveryCamera()
        {
            var sut = Build(Cam("a"), Cam("b"));
            int adds = 0;
            RoomCameraViewModel? edited = null;
            sut.AddRequested += () => adds++;
            sut.EditRequested += c => edited = c;

            sut.AddCommand.Execute(null);
            sut.EditCommand.Execute(sut.Cameras[1]);
            sut.Tick();

            Assert.Equal(1, adds);
            Assert.Same(sut.Cameras[1], edited);
            Assert.All(sut.Cameras, c => Assert.Equal(1, ((FakePtzCamera)c.Camera).TickCount));
        }

        [Fact]
        public void Dispose_DisposesEveryCamera_AndUnhooksSources()
        {
            _sources.Add(Card());
            var sut = Build(Cam("a", mac: Mac));
            var fake = (FakePtzCamera)sut.Cameras[0].Camera;

            sut.Dispose();
            sut.Dispose();
            _sources.Clear();   // must not throw after dispose

            Assert.True(fake.Disposed);
        }
    }
}
