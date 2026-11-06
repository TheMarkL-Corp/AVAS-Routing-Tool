using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AvasRoutingTool.Sdvoe
{
    public interface ISdvoeClient : IDisposable
    {
        bool IsConnected { get; }
        event Action<string, bool>? DeviceStateChanged;

        Task<bool> ConnectAsync(string host, int telnetPort = 6970, int restPort = 8080);
        Task DisconnectAsync();

        Task<bool> JoinStreamAsync(string rxMac, string txMac, string streamType = "HDMI", int streamIndex = 0);
        Task<bool> LeaveStreamAsync(string rxMac, string streamType = "HDMI", int streamIndex = 0);

        Task<bool> StartAvasPreviewAsync(string mac, string multicastIp);
        Task<bool> StopAvasPreviewAsync(string mac);

        Task<bool> StartStream1ScalerAsync(string txMac, string multicastIp, int port = 5004);
        Task<bool> StopStream1ScalerAsync(string txMac);

        /// <summary>
        /// Creates (or overwrites) the layout <see cref="MultiView.SurfacePlan.LayoutName"/> on the Control Server at
        /// the plan's canvas size, with one window per planned window (a window without a surface is black).
        /// </summary>
        Task<bool> CreateMultiViewLayoutAsync(MultiView.SurfacePlan surfacePlan, MultiView.MultiViewLayoutPlan plan);

        /// <summary>Sizes a transmitter's scaled stream to its multiview window and starts HDMI stream 1.</summary>
        Task<bool> PrepareSourceScalerAsync(string txMac, int width, int height);

        /// <summary>
        /// Runs a TX's scaled stream (stream 1) at half frame rate (<c>true</c>) or full rate (<c>false</c>) via the
        /// scaler's frame-rate divider. Fails on devices without <c>capabilities.frame_rate_divider</c>.
        /// </summary>
        Task<bool> SetStream1FrameDividerAsync(string txMac, bool halve);

        /// <summary>
        /// Whether a TX's scaled stream currently runs through the frame-rate divider:
        /// true = half rate, false = full rate, null = could not be determined.
        /// </summary>
        Task<bool?> ReadStream1FrameDividerAsync(string txMac);

        /// <summary>
        /// The video a transmitter's HDMI input currently sees (for the bandwidth check), or null when unknown or no signal.
        /// Read-only. Default: unknown.
        /// </summary>
        Task<MultiView.SourceVideo?> ReadSourceVideoAsync(string txMac) => Task.FromResult<MultiView.SourceVideo?>(null);

        /// <summary>Puts a receiver into multiview, loads the layout and applies the subscriptions, in one call.</summary>
        Task<bool> SetMultiViewAsync(string rxMac, string layoutName, int width, int height, int fps,
            IReadOnlyList<MultiView.MultiViewSubscription> subscriptions);

        /// <summary>
        /// Reads the EDID the decoder got from its attached display (hex), or null when no display is
        /// connected or the read failed.
        /// </summary>
        Task<string?> ReadDisplayEdidAsync(string rxMac);

        /// <summary>
        /// Reads what a receiver is really doing (display mode, HDMI subscriptions and, in multiview, the loaded layout).
        /// Read-only. Returns null when the receiver cannot be read; never throws.
        /// </summary>
        Task<RxState?> ReadReceiverStateAsync(string rxMac);

        /// <summary>Puts a receiver into Fast Switch (aspect kept, black bars) at the given output format: the mode Single View runs in.</summary>
        Task<bool> SetFastSwitchedAsync(string rxMac, int width, int height, int fps);

        Task<List<AvasDevice>> DiscoverDevicesAsync();
    }
}
