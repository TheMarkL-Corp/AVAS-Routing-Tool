using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Services;
using AvasRoutingTool.Services.Visca;
using AvasRoutingTool.Tests.Fakes;
using AvasRoutingTool.ViewModels;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class RoomCameraViewModelTests : IDisposable
    {
        private DateTime _clock = new(2026, 11, 2, 10, 0, 0);
        private readonly RoomCameraConfig _config = new()
        {
            Id = "cam-1",
            Name = "ROOM-CAM",
            Host = "192.168.1.163",
            Port = 1259,
            Transport = ViscaTransportKind.Tcp,
        };
        private readonly FakePtzCamera _camera = new();
        private readonly RoomCameraViewModel _sut;

        public RoomCameraViewModelTests()
        {
            _sut = new RoomCameraViewModel(_config, _camera, () => _clock);
        }

        public void Dispose() => _sut.Dispose();

        private void AdvanceMs(int ms) => _clock = _clock.AddMilliseconds(ms);

        private IEnumerable<string> ZoomSends => _camera.Calls.Where(c => c.StartsWith("ZoomPos:"));

        private int PollCount => _camera.Calls.Count(c => c == "Poll");

        private static CameraStatus Status(CameraPower power = CameraPower.On, int zoom = 0, int focus = 0, bool af = false)
            => new(power, zoom, focus, af);

        [Fact]
        public void Identity_NameAndEndpoint_ComeFromConfig()
        {
            Assert.Same(_config, _sut.Config);
            Assert.Same(_camera, _sut.Camera);
            Assert.Equal("cam-1", _sut.Id);
            Assert.Equal("ROOM-CAM", _sut.Name);
            Assert.Equal("192.168.1.163:1259", _sut.Endpoint);
            Assert.Equal(_config.ZoomMax, _sut.ZoomMaximum);
            Assert.Equal(_config.FocusMax, _sut.FocusMaximum);
        }

        [Fact]
        public void Lock_DisablesAllMotionCommands_AndEndsActiveMotion()
        {
            _sut.MoveStartCommand.Execute(PanTiltDirection.Left);
            _sut.ZoomStartCommand.Execute(ZoomDirection.Tele);
            Assert.Equal(new[] { "Move:Left:12:10", "Zoom:Tele:4" }, _camera.Calls);

            _sut.IsLocked = true;

            Assert.Equal(new[] { "Move:Left:12:10", "Zoom:Tele:4", "StopMove", "StopZoom" }, _camera.Calls);
            Assert.Equal(MotionState.Idle, _sut.MotionState);
            foreach (var d in Enum.GetValues<PanTiltDirection>())
                Assert.False(_sut.MoveStartCommand.CanExecute(d));
            Assert.False(_sut.ZoomStartCommand.CanExecute(ZoomDirection.Wide));
            Assert.False(_sut.HomeCommand.CanExecute(null));
            Assert.False(_sut.PowerCommand.CanExecute(null));
            Assert.False(_sut.ZoomEnabled);
            Assert.False(_sut.FocusEnabled);
            Assert.False(_sut.AutoFocusEnabled);
            Assert.False(_sut.PowerEnabled);
            Assert.False(_sut.MotionEnabled);

            // Stop stays available while locked.
            Assert.True(_sut.MoveEndCommand.CanExecute(null));
            Assert.True(_sut.ZoomEndCommand.CanExecute(null));

            // A press while locked sends nothing.
            _sut.BeginMove(PanTiltDirection.Up);
            _sut.BeginZoom(ZoomDirection.Wide);
            Assert.Equal(4, _camera.Calls.Count);

            // Unlock restores the controls.
            _sut.IsLocked = false;
            Assert.True(_sut.MoveStartCommand.CanExecute(PanTiltDirection.Up));
            Assert.True(_sut.HomeCommand.CanExecute(null));
        }

        [Fact]
        public void Lock_WhileZoomDragging_DoesNotSendZoomPositions()
        {
            _sut.BeginZoomDrag();
            _sut.ZoomValue = 100;
            _sut.IsLocked = true;
            _sut.ZoomValue = 200;
            _sut.EndZoomDrag();

            Assert.Equal(new[] { "ZoomPos:100" }, ZoomSends.ToArray());
        }

        [Fact]
        public void LinkDrop_DuringMove_SendsStop()
        {
            _sut.BeginMove(PanTiltDirection.UpRight);

            _camera.LinkState = CameraLinkState.Unreachable;

            Assert.Equal("StopMove", _camera.Calls[^1]);
            Assert.Equal(MotionState.Idle, _sut.MotionState);
        }

        [Fact]
        public void PadPress_AndRelease_SendMoveThenStop()
        {
            _sut.MoveStartCommand.Execute(PanTiltDirection.UpLeft);
            _sut.MoveEndCommand.Execute(null);

            Assert.Equal(new[] { "Move:UpLeft:12:10", "StopMove" }, _camera.Calls);
        }

        [Fact]
        public void MotionChanged_RaisesPropertyChanged_ForMotionTextAndState()
        {
            var names = Capture(_sut);

            _sut.BeginMove(PanTiltDirection.Down);

            Assert.Contains("MotionText", names);
            Assert.Contains("MotionState", names);
            Assert.Equal("Moving down", _sut.MotionText);
        }

        [Fact]
        public void PowerOff_NeedsConfirm_AutoCancelsAfter8s()
        {
            _sut.ApplyStatus(Status(CameraPower.On));

            _sut.PowerCommand.Execute(null);

            Assert.True(_sut.IsPowerOffPending);
            Assert.Equal("Turn off ROOM-CAM?", _sut.PowerOffPrompt);
            Assert.DoesNotContain("Power:False", _camera.Calls);

            AdvanceMs(7_900);
            _sut.Tick();
            Assert.True(_sut.IsPowerOffPending);

            AdvanceMs(200);
            _sut.Tick();
            Assert.False(_sut.IsPowerOffPending);
            Assert.DoesNotContain("Power:False", _camera.Calls);
        }

        [Fact]
        public void PowerOff_ConfirmSendsPowerOff_AndClearsPending()
        {
            _sut.ApplyStatus(Status(CameraPower.On));
            _sut.PowerCommand.Execute(null);

            _sut.ConfirmPowerOffCommand.Execute(null);

            Assert.False(_sut.IsPowerOffPending);
            Assert.Equal("Power:False", _camera.Calls[^1]);
        }

        [Fact]
        public void PowerOff_CancelClearsPending_WithoutSending()
        {
            _sut.ApplyStatus(Status(CameraPower.On));
            _sut.PowerCommand.Execute(null);

            _sut.CancelPowerOffCommand.Execute(null);

            Assert.False(_sut.IsPowerOffPending);
            Assert.Empty(_camera.Calls);
        }

        [Theory]
        [InlineData(CameraPower.Off)]
        [InlineData(CameraPower.Unknown)]
        [InlineData(CameraPower.Fault)]
        public void Power_WhenNotOn_TurnsOnWithoutPrompt(CameraPower power)
        {
            _sut.ApplyStatus(Status(power));

            _sut.PowerCommand.Execute(null);

            Assert.False(_sut.IsPowerOffPending);
            Assert.Equal(new[] { "Power:True" }, _camera.Calls);
        }

        [Theory]
        [InlineData(CameraPower.On, "Camera on")]
        [InlineData(CameraPower.Off, "Camera off")]
        [InlineData(CameraPower.Fault, "Camera fault")]
        [InlineData(CameraPower.Unknown, "Power unknown")]
        public void PowerText_FollowsPowerReadback(CameraPower power, string text)
        {
            _sut.ApplyStatus(Status(power));

            Assert.Equal(text, _sut.PowerText);
        }

        [Theory]
        [InlineData(CameraLinkState.Connected, "Connected", true)]
        [InlineData(CameraLinkState.Connecting, "Connecting...", false)]
        [InlineData(CameraLinkState.Disconnected, "Not connected - retrying", false)]
        [InlineData(CameraLinkState.Unreachable, "Cannot reach 192.168.1.163:1259", false)]
        public void LinkText_FollowsLinkState(CameraLinkState state, string text, bool good)
        {
            _camera.LinkState = state;

            Assert.Equal(text, _sut.LinkText);
            Assert.Equal(good, _sut.LinkIsGood);
        }

        [Fact]
        public void LinkIsUnreachable_TracksLinkState()
        {
            var names = Capture(_sut);
            Assert.False(_sut.LinkIsUnreachable);

            _camera.LinkState = CameraLinkState.Unreachable;
            Assert.True(_sut.LinkIsUnreachable);
            Assert.Contains("LinkIsUnreachable", names);

            _camera.LinkState = CameraLinkState.Connecting;
            Assert.False(_sut.LinkIsUnreachable);
        }

        [Fact]
        public void LinkStateChange_RaisesPropertyChanged_ForLinkTextAndIsGood()
        {
            var names = Capture(_sut);

            _camera.LinkState = CameraLinkState.Unreachable;

            Assert.Contains("LinkText", names);
            Assert.Contains("LinkIsGood", names);
        }

        [Theory]
        [InlineData(SpeedStep.Slow, 5, 4, 2)]
        [InlineData(SpeedStep.Medium, 12, 10, 4)]
        [InlineData(SpeedStep.Fast, 20, 16, 6)]
        public void SpeedStep_MapsToSpeedTable(SpeedStep step, int pan, int tilt, int zoom)
        {
            _sut.SpeedStep = step;

            _sut.BeginMove(PanTiltDirection.Left);
            _sut.EndMove();
            _sut.BeginZoom(ZoomDirection.Wide);
            _sut.EndZoom();

            Assert.Equal(
                new[] { $"Move:Left:{pan}:{tilt}", "StopMove", $"Zoom:Wide:{zoom}", "StopZoom" },
                _camera.Calls);
        }

        [Fact]
        public void SpeedStep_DefaultsToMedium()
        {
            Assert.Equal(SpeedStep.Medium, _sut.SpeedStep);
        }

        [Fact]
        public void AutoFocus_On_DisablesFocus_AndSendsAutoCommand()
        {
            _sut.AutoFocus = true;

            Assert.Equal(new[] { "FocusAuto:True" }, _camera.Calls);
            Assert.False(_sut.FocusEnabled);

            _sut.FocusValue = 0x1000;
            Assert.DoesNotContain(_camera.Calls, c => c.StartsWith("FocusPos:"));

            _sut.AutoFocus = false;
            Assert.True(_sut.FocusEnabled);
        }

        [Fact]
        public void AutoFocus_FromReadback_DisablesFocus_WithoutSending()
        {
            _sut.ApplyStatus(Status(af: true));

            Assert.True(_sut.AutoFocus);
            Assert.False(_sut.FocusEnabled);
            Assert.Empty(_camera.Calls);
        }

        [Fact]
        public void Readback_UpdatesZoomFocusPowerAndAutoFocus_WithoutSending()
        {
            _sut.ApplyStatus(Status(CameraPower.Off, zoom: 0x1234, focus: 0x0800, af: false));

            Assert.Equal(0x1234, _sut.ZoomValue);
            Assert.Equal(0x0800, _sut.FocusValue);
            Assert.Equal("Camera off", _sut.PowerText);
            Assert.DoesNotContain(_camera.Calls, c => c.StartsWith("ZoomPos:") || c.StartsWith("FocusPos:"));
        }

        [Fact]
        public void Readback_UpdatesSlider_ExceptWhileDragging()
        {
            _sut.ApplyStatus(Status(zoom: 0x1234, focus: 0x0800));

            _sut.BeginZoomDrag();
            _sut.ZoomValue = 0x2000;
            _sut.ApplyStatus(Status(zoom: 0x1500, focus: 0x0900));

            Assert.Equal(0x2000, _sut.ZoomValue);   // zoom readback ignored while dragging
            Assert.Equal(0x0900, _sut.FocusValue);  // focus is not dragging: readback applies

            _sut.EndZoomDrag();
            _sut.ApplyStatus(Status(zoom: 0x1500, focus: 0x0900));

            Assert.Equal(0x1500, _sut.ZoomValue);
        }

        [Fact]
        public void ZoomSlider_ThrottlesTo5PerSecond_PlusFinal()
        {
            _sut.BeginZoomDrag();

            // One second of dragging, a change and a timer tick every 10 ms.
            for (int i = 0; i < 100; i++)
            {
                _sut.ZoomValue = 1000 + i * 10;
                _sut.Tick();
                AdvanceMs(10);
            }

            Assert.Equal(5, ZoomSends.Count());

            _sut.EndZoomDrag();

            var sends = ZoomSends.ToArray();
            Assert.Equal(6, sends.Length);
            Assert.Equal("ZoomPos:1990", sends[^1]);
        }

        [Fact]
        public void ZoomSlider_Value_IsClampedToConfiguredRange()
        {
            _sut.ZoomValue = int.MaxValue;
            Assert.Equal(_config.ZoomMax, _sut.ZoomValue);

            _sut.ZoomValue = -5;
            Assert.Equal(0, _sut.ZoomValue);
        }

        [Fact]
        public void FocusSlider_SendsSetFocusPosition_AndFinalOnRelease()
        {
            _sut.BeginFocusDrag();
            _sut.FocusValue = 0x4000;
            _sut.EndFocusDrag();

            Assert.Equal(new[] { "FocusPos:16384", "FocusPos:16384" }, _camera.Calls);
        }

        [Fact]
        public void SliderEcho_OfSameValue_DoesNotSendAgain()
        {
            _sut.ZoomValue = 500;
            _sut.ZoomValue = 500;

            Assert.Single(ZoomSends);
        }

        [Fact]
        public void Poll_RunsOnceEvery5s_AndNeverOverlaps()
        {
            _camera.HoldPolls = true;

            _sut.Tick();
            _sut.Tick();
            AdvanceMs(3000);
            _sut.Tick();
            AdvanceMs(3000);
            _sut.Tick();
            Assert.Equal(1, PollCount);

            _camera.ReleaseHeldPolls(Status(CameraPower.Off, zoom: 0x10, focus: 0x20));
            Assert.Equal("Camera off", _sut.PowerText);
            Assert.Equal(0x10, _sut.ZoomValue);
            Assert.Equal(0x20, _sut.FocusValue);

            _camera.HoldPolls = false;
            // 6 s after the first poll started: the next one is due.
            _sut.Tick();
            Assert.Equal(2, PollCount);

            AdvanceMs(4000);
            _sut.Tick();
            Assert.Equal(2, PollCount);

            AdvanceMs(1000);
            _sut.Tick();
            Assert.Equal(3, PollCount);
        }

        [Fact]
        public void Poll_NoAnswer_LeavesStateAlone()
        {
            _sut.ApplyStatus(Status(CameraPower.On, zoom: 0x40));
            _camera.PollResult = null;

            _sut.Tick();

            Assert.Equal("Camera on", _sut.PowerText);
            Assert.Equal(0x40, _sut.ZoomValue);
        }

        [Fact]
        public void Tick_DrivesCameraTick()
        {
            _sut.Tick();

            Assert.Equal(1, _camera.TickCount);
        }

        [Fact]
        public void LongName_AutomationNameStillHasState()
        {
            var name = new string('W', 45);
            var camera = new FakePtzCamera();
            using var cameraVm = new RoomCameraViewModel(
                new RoomCameraConfig { Name = name, Host = "10.0.0.5", Port = 1259 },
                camera,
                () => _clock);

            cameraVm.ApplyStatus(Status(CameraPower.On));

            Assert.Equal($"{name}, connected, camera on", cameraVm.AutomationName);

            camera.LinkState = CameraLinkState.Unreachable;

            Assert.Equal($"{name}, unreachable, camera on", cameraVm.AutomationName);
        }

        [Fact]
        public void AutomationName_IncludesLock()
        {
            _sut.ApplyStatus(Status(CameraPower.Off));
            _sut.IsLocked = true;

            Assert.Equal("ROOM-CAM, connected, camera off, locked", _sut.AutomationName);
        }

        [Fact]
        public void Dispose_SendsStop_UnhooksEvents_AndDoesNotDisposeCamera()
        {
            _sut.BeginMove(PanTiltDirection.Up);
            var names = Capture(_sut);

            _sut.Dispose();

            Assert.Equal("StopMove", _camera.Calls[^1]);
            Assert.False(_camera.Disposed);

            names.Clear();
            _camera.LinkState = CameraLinkState.Unreachable;
            _sut.BeginMove(PanTiltDirection.Up);
            Assert.Empty(names);
            Assert.Equal("StopMove", _camera.Calls[^1]);
        }

        [Fact]
        public void Dispose_TwiceIsSafe()
        {
            _sut.Dispose();
            _sut.Dispose();

            Assert.Empty(_camera.Calls);   // idle at dispose: nothing to stop
        }

        private static List<string> Capture(INotifyPropertyChanged vm)
        {
            var names = new List<string>();
            vm.PropertyChanged += (_, e) => names.Add(e.PropertyName ?? "");
            return names;
        }
    }
}
