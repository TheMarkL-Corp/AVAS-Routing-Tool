using System;
using System.Linq;
using AvasRoutingTool.Services;
using AvasRoutingTool.Services.Visca;
using AvasRoutingTool.Tests.Fakes;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class PtzMotionControllerTests
    {
        private DateTime _clock = new(2026, 11, 2, 10, 0, 0);
        private readonly FakePtzCamera _camera = new();
        private readonly PtzMotionController _sut;

        public PtzMotionControllerTests()
        {
            _sut = new PtzMotionController(_camera, () => _clock);
            _sut.Speed = new PtzSpeed(5, 4, 2);
        }

        private int StopCount => _camera.Calls.Count(c => c == "StopMove");

        [Fact]
        public void Begin_SendsMove_End_SendsStop()
        {
            _sut.BeginMove(PanTiltDirection.UpLeft);

            Assert.Equal(MotionState.Moving, _sut.State);
            Assert.Equal("Moving up-left", _sut.MovingText);

            _sut.End();

            Assert.Equal(new[] { "Move:UpLeft:5:4", "StopMove" }, _camera.Calls);
            Assert.Equal(MotionState.Idle, _sut.State);
        }

        [Fact]
        public void BeginWhileMoving_SwitchesDirection_NoStopBetween()
        {
            _sut.BeginMove(PanTiltDirection.Left);
            _sut.BeginMove(PanTiltDirection.Right);
            _sut.End();

            Assert.Equal(new[] { "Move:Left:5:4", "Move:Right:5:4", "StopMove" }, _camera.Calls);
        }

        [Fact]
        public void End_WhenIdle_SendsNothing()
        {
            _sut.End();

            Assert.Empty(_camera.Calls);
            Assert.Equal(MotionState.Idle, _sut.State);
        }

        [Fact]
        public void Watchdog_StopsAfter10s_AndTextSays_Stopped()
        {
            _sut.BeginMove(PanTiltDirection.Up);

            _clock = _clock.AddSeconds(10);
            _sut.Tick();
            Assert.Equal(0, StopCount);

            _clock = _clock.AddSeconds(1);
            _sut.Tick();

            Assert.Equal(1, StopCount);
            Assert.Equal(MotionState.Idle, _sut.State);
            Assert.Equal("Stopped (10 s limit)", _sut.MovingText);
        }

        [Fact]
        public void StopFails_RetriesThreeTimes_ThenStopNotConfirmed()
        {
            _camera.FailNextStops(3);
            _sut.BeginMove(PanTiltDirection.Down);

            _sut.End();
            Assert.Equal(MotionState.Stopping, _sut.State);

            _sut.Tick();
            Assert.Equal(MotionState.Stopping, _sut.State);

            _sut.Tick();
            Assert.Equal(MotionState.StopNotConfirmed, _sut.State);
            Assert.Equal("Stop not confirmed", _sut.MovingText);
            Assert.Equal(3, StopCount);

            _sut.Tick();
            Assert.Equal(3, StopCount);
        }

        [Fact]
        public void StopNotConfirmed_ClearsWhenLaterStopSucceeds()
        {
            _camera.FailNextStops(3);
            _sut.BeginMove(PanTiltDirection.Down);
            _sut.End();
            _sut.Tick();
            _sut.Tick();
            Assert.Equal(MotionState.StopNotConfirmed, _sut.State);

            _sut.StopAll();

            Assert.Equal(MotionState.Idle, _sut.State);
            Assert.Equal(4, StopCount);
        }

        [Fact]
        public void StopAll_AfterBeginZoomAndMove_StopsBoth()
        {
            _sut.BeginZoom(ZoomDirection.Tele);
            _sut.BeginMove(PanTiltDirection.Left);
            Assert.Equal("Moving left and Zooming in", _sut.MovingText);

            _sut.StopAll();

            Assert.Contains("StopMove", _camera.Calls);
            Assert.Contains("StopZoom", _camera.Calls);
            Assert.Equal(MotionState.Idle, _sut.State);
        }

        [Fact]
        public void Begin_WhenCameraUnreachable_DoesNotEnterMoving()
        {
            _camera.LinkState = CameraLinkState.Unreachable;

            _sut.BeginMove(PanTiltDirection.Up);

            Assert.Equal(MotionState.Idle, _sut.State);
            Assert.Equal("Camera not connected", _sut.MovingText);
            Assert.Empty(_camera.Calls);
        }

        [Fact]
        public void Dispose_SendsStopOnce()
        {
            _sut.BeginMove(PanTiltDirection.Down);

            _sut.Dispose();
            _sut.Dispose();
            _sut.Tick();

            Assert.Equal(1, StopCount);
        }

        [Fact]
        public void MoveNotAcknowledged_SendsStop_AndEnds()
        {
            _camera.FailNextMoves(1);

            _sut.BeginMove(PanTiltDirection.Right);

            Assert.Equal(new[] { "Move:Right:5:4", "StopMove" }, _camera.Calls);
            Assert.Equal(MotionState.Idle, _sut.State);
            Assert.Equal("Move not acknowledged", _sut.MovingText);
        }

        [Fact]
        public void HeldStop_NoAnswerIn2s_IsResent()
        {
            _camera.HoldStops = true;
            _sut.BeginMove(PanTiltDirection.Up);
            _sut.End();

            _clock = _clock.AddSeconds(1);
            _sut.Tick();
            Assert.Equal(1, StopCount);

            _clock = _clock.AddSeconds(2);
            _sut.Tick();
            Assert.Equal(2, StopCount);
        }

        [Fact]
        public void LateAnswer_FromSupersededStop_IsIgnored()
        {
            _camera.HoldStops = true;
            _sut.BeginMove(PanTiltDirection.Up);
            _sut.End();

            _camera.HoldStops = false;
            _sut.BeginMove(PanTiltDirection.Down);
            _camera.ReleaseHeldStops(false);

            Assert.Equal(MotionState.Moving, _sut.State);
            Assert.Equal("Moving down", _sut.MovingText);
        }

        [Fact]
        public void Changed_RaisedOnlyWhenStateOrTextChanges()
        {
            int raised = 0;
            _sut.Changed += () => raised++;

            _sut.BeginMove(PanTiltDirection.Up);
            Assert.Equal(1, raised);

            _sut.Tick();
            Assert.Equal(1, raised);

            _sut.End();
            Assert.Equal(3, raised);   // Moving -> Stopping -> Idle
        }
    }
}
