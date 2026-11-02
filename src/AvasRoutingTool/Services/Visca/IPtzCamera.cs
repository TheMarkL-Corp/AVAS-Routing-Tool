using System;
using System.Threading;
using System.Threading.Tasks;
using AvasRoutingTool.Configuration;

namespace AvasRoutingTool.Services.Visca
{
    public enum CameraLinkState { Disconnected, Connecting, Connected, Unreachable }

    public enum CameraPower { Unknown, On, Off, Fault }

    /// <summary>Pan, tilt, zoom and focus speeds in camera units (see ViscaCommands for ranges).</summary>
    public readonly record struct PtzSpeed(int Pan, int Tilt, int Zoom);

    /// <summary>One poll of the camera's state. Positions are raw VISCA values.</summary>
    public sealed record CameraStatus(CameraPower Power, int ZoomPosition, int FocusPosition, bool AutoFocus);

    /// <summary>
    /// One PTZ camera. Commands return false when the camera did not acknowledge them
    /// (or, for motion, when a Stop superseded them). Stop commands take a priority path
    /// and are sent even while a move is waiting for completion.
    /// </summary>
    public interface IPtzCamera : IAsyncDisposable
    {
        CameraLinkState LinkState { get; }
        event Action<CameraLinkState> LinkStateChanged;

        Task ConnectAsync(CancellationToken ct);
        Task<bool> MoveAsync(PanTiltDirection d, PtzSpeed s, CancellationToken ct);   // false = not acknowledged
        Task<bool> StopMoveAsync(CancellationToken ct);
        Task<bool> HomeAsync(CancellationToken ct);
        Task<bool> ZoomAsync(ZoomDirection d, PtzSpeed s, CancellationToken ct);
        Task<bool> StopZoomAsync(CancellationToken ct);
        Task<bool> SetZoomPositionAsync(int pos, CancellationToken ct);
        Task<bool> SetFocusAutoAsync(bool auto, CancellationToken ct);
        Task<bool> SetFocusPositionAsync(int pos, CancellationToken ct);
        Task<bool> SetPowerAsync(bool on, CancellationToken ct);
        Task<CameraStatus?> PollAsync(CancellationToken ct);   // power, zoom, focus, af; null = no answer
        void StopImmediately();   // synchronous best-effort Stop datagram, for Closed/Deactivated
        /// <summary>Drives reconnect backoff using the injected clock. Never blocks; no internal timers.</summary>
        void Tick();
    }

    public interface IPtzCameraFactory
    {
        IPtzCamera Create(RoomCameraConfig config);
    }
}
