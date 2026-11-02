using System;
using System.Threading;
using System.Threading.Tasks;

namespace AvasRoutingTool.Services.Visca
{
    /// <summary>The IPtzCamera command surface. Normal commands queue; Stop takes the priority path.</summary>
    public sealed partial class ViscaCamera
    {
        private const int CommandRetries = 2;
        private const int StopRetries = 3;

        public Task<bool> MoveAsync(PanTiltDirection d, PtzSpeed s, CancellationToken ct)
            => QueuedAsync(ViscaCommands.PanTilt(_address, d, s.Pan, s.Tilt), Motion.PanTilt, ct);

        public Task<bool> HomeAsync(CancellationToken ct)
            => QueuedAsync(ViscaCommands.Home(_address), Motion.None, ct);

        public Task<bool> ZoomAsync(ZoomDirection d, PtzSpeed s, CancellationToken ct)
            => QueuedAsync(ViscaCommands.Zoom(_address, d, s.Zoom), Motion.Zoom, ct);

        public Task<bool> SetZoomPositionAsync(int pos, CancellationToken ct)
            => QueuedAsync(ViscaCommands.ZoomDirect(_address, pos), Motion.None, ct);

        public Task<bool> SetFocusAutoAsync(bool auto, CancellationToken ct)
            => QueuedAsync(ViscaCommands.FocusAuto(_address, auto), Motion.None, ct);

        public Task<bool> SetFocusPositionAsync(int pos, CancellationToken ct)
            => QueuedAsync(ViscaCommands.FocusDirect(_address, pos), Motion.None, ct);

        public Task<bool> SetPowerAsync(bool on, CancellationToken ct)
            => QueuedAsync(ViscaCommands.Power(_address, on), Motion.None, ct);

        public Task<bool> StopMoveAsync(CancellationToken ct)
            => PriorityStopAsync(ViscaCommands.StopPanTilt(_address), Motion.PanTilt, ct);

        public Task<bool> StopZoomAsync(CancellationToken ct)
            => PriorityStopAsync(ViscaCommands.StopZoom(_address), Motion.Zoom, ct);

        public async Task<CameraStatus?> PollAsync(CancellationToken ct)
        {
            if (CurrentTransport() == null)
                return null;

            try
            {
                await _queue.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                return null;
            }

            try
            {
                byte[]? power = await InquireAsync(ViscaCommands.InquirePower(_address), ct).ConfigureAwait(false);
                byte[]? zoom = power == null ? null : await InquireAsync(ViscaCommands.InquireZoom(_address), ct).ConfigureAwait(false);
                byte[]? focus = zoom == null ? null : await InquireAsync(ViscaCommands.InquireFocus(_address), ct).ConfigureAwait(false);
                byte[]? mode = focus == null ? null : await InquireAsync(ViscaCommands.InquireFocusMode(_address), ct).ConfigureAwait(false);

                if (mode == null)
                {
                    if (CurrentTransport() != null)
                        ReportPollTimeout();
                    return null;
                }

                ReportSuccess();
                if (power!.Length < 1 || mode.Length < 1 || !TryReadPosition(zoom!, out int zoomPos) || !TryReadPosition(focus!, out int focusPos))
                    return null;

                return new CameraStatus(ParsePower(power[0]), zoomPos, focusPos, mode[0] == 0x02);
            }
            finally
            {
                _queue.Release();
            }
        }

        public void StopImmediately()
        {
            try
            {
                IViscaTransport? transport = CurrentTransport();
                if (transport == null)
                    return;

                // Fire and forget: the replies are matched like any others, so they cannot shift later ones.
                Task?[] writes;
                lock (_wire)
                {
                    SupersedeLocked(Motion.PanTilt);
                    SupersedeLocked(Motion.Zoom);
                }
                writes = new[]
                {
                    BeginSend(new Exchange(false, Motion.None), ViscaCommands.StopPanTilt(_address), transport, CancellationToken.None),
                    BeginSend(new Exchange(false, Motion.None), ViscaCommands.StopZoom(_address), transport, CancellationToken.None)
                };
                foreach (Task? write in writes)
                    write?.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            }
            catch (Exception)
            {
                // Best effort: this runs while the window is closing.
            }
        }

        private async Task<bool> QueuedAsync(byte[] bytes, Motion motion, CancellationToken ct)
        {
            if (CurrentTransport() == null)
                return false;

            try
            {
                await _queue.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                return false;
            }

            try
            {
                return await ExchangeAsync(bytes, motion, CommandRetries, waitCompletion: true, ct).ConfigureAwait(false);
            }
            finally
            {
                _queue.Release();
            }
        }

        private Task<bool> PriorityStopAsync(byte[] bytes, Motion motion, CancellationToken ct)
        {
            lock (_wire) SupersedeLocked(motion);
            return ExchangeAsync(bytes, Motion.None, StopRetries, waitCompletion: false, ct);
        }

        private static bool TryReadPosition(byte[] data, out int position)
        {
            position = 0;
            if (data.Length != 4)
                return false;
            position = ((data[0] & 0x0F) << 12) | ((data[1] & 0x0F) << 8) | ((data[2] & 0x0F) << 4) | (data[3] & 0x0F);
            return true;
        }

        private static CameraPower ParsePower(byte value) => value switch
        {
            0x02 => CameraPower.On,
            0x03 => CameraPower.Off,
            0x04 => CameraPower.Fault,
            _ => CameraPower.Unknown
        };
    }
}
