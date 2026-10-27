using System;
using System.Threading.Tasks;
using AvasRoutingTool.Logging;
using AvasRoutingTool.Sdvoe.MultiView;

namespace AvasRoutingTool.Sdvoe
{
    /// <summary>What <see cref="RoutingOrchestrator.EnsureSingleViewModeAsync"/> settled on: the output format, and whether a mode change was sent.</summary>
    public sealed record SingleViewMode(bool Ok, MultiViewCanvas Format, bool Changed);

    /// <summary>The outcome of a Single View route. <see cref="Format"/> is the output format the receiver runs at; <see cref="Error"/> says why nothing was joined.</summary>
    public sealed record SingleViewRoute(bool Ok, MultiViewCanvas? Format, string? Error);

    public class RoutingOrchestrator
    {
        /// <summary>The only display mode Single View runs in: Fast Switch with black bars (never the _CROP / _STRETCH variants).</summary>
        private const string FastSwitched = "FAST_SWITCHED";

        private readonly ISdvoeClient _sdvoeClient;
        private readonly MultiViewCanvas? _fallback;

        public RoutingOrchestrator(ISdvoeClient sdvoeClient, MultiViewCanvas? fallback = null)
        {
            _sdvoeClient = sdvoeClient ?? throw new ArgumentNullException(nameof(sdvoeClient));
            _fallback = fallback;
        }

        public async Task<bool> RouteSingleViewVideoAsync(AvasDevice sourceTx, AvasDevice displayRx) =>
            (await RouteSingleViewAsync(sourceTx, displayRx)).Ok;

        /// <summary>
        /// Routes the source to the display in Single View: the receiver is first put into Fast Switch at its display's
        /// EDID format, then HDMI:0 is joined. When the mode cannot be set nothing is joined and the active source is untouched.
        /// </summary>
        public async Task<SingleViewRoute> RouteSingleViewAsync(AvasDevice sourceTx, AvasDevice displayRx)
        {
            if (sourceTx == null || displayRx == null) return new SingleViewRoute(false, null, "No source or display.");

            var mode = await EnsureSingleViewModeAsync(displayRx);
            if (!mode.Ok)
            {
                string error = $"Could not set Fast Switch {mode.Format.Width}x{mode.Format.Height}@{mode.Format.Fps} on '{displayRx.DeviceName}'; the route was not sent.";
                return new SingleViewRoute(false, mode.Format, error);
            }

            // Video Routing Only (HDMI:0)
            bool videoOk = await _sdvoeClient.JoinStreamAsync(displayRx.DeviceId, sourceTx.DeviceId, "HDMI", 0);
            if (!videoOk) return new SingleViewRoute(false, mode.Format, $"The join to '{displayRx.DeviceName}' was refused.");

            displayRx.ActiveSourceId = sourceTx.DeviceId;
            displayRx.ActiveSourceName = sourceTx.DeviceName;
            return new SingleViewRoute(true, mode.Format, null);
        }

        /// <summary>
        /// Puts the receiver into Fast Switch at the format of its display's EDID (the multiview format policy; 1920x1080@60
        /// with a stated reason when the EDID cannot be used). Nothing is sent when the receiver already runs exactly that.
        /// </summary>
        public async Task<SingleViewMode> EnsureSingleViewModeAsync(AvasDevice displayRx)
        {
            var format = MultiViewCanvasPolicy.Choose(await ReadEdidAsync(displayRx), _fallback);
            AppLogger.Info("ROUTING", $"'{displayRx.DeviceName}' single view output: {format.Description}");

            var state = await ReadStateAsync(displayRx);
            if (state != null &&
                string.Equals(state.DisplayMode, FastSwitched, StringComparison.OrdinalIgnoreCase) &&
                state.Width == format.Width && state.Height == format.Height && state.Fps == format.Fps)
            {
                return new SingleViewMode(true, format, false);
            }

            bool ok;
            try
            {
                ok = await _sdvoeClient.SetFastSwitchedAsync(displayRx.DeviceId, format.Width, format.Height, format.Fps);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AppLogger.Warn("ROUTING", $"Fast Switch for '{displayRx.DeviceName}' threw: {ex.Message}");
                ok = false;
            }
            if (!ok) AppLogger.Warn("ROUTING", $"'{displayRx.DeviceName}' did not accept Fast Switch {format.Width}x{format.Height}@{format.Fps}.");
            return new SingleViewMode(ok, format, true);
        }

        /// <summary>The receiver's reported state, or null when it cannot be read; an unreadable receiver gets the mode sent.</summary>
        private async Task<RxState?> ReadStateAsync(AvasDevice displayRx)
        {
            try
            {
                return await _sdvoeClient.ReadReceiverStateAsync(displayRx.DeviceId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AppLogger.Warn("ROUTING", $"State read for '{displayRx.DeviceName}' threw: {ex.Message}");
                return null;
            }
        }

        /// <summary>The display's EDID, or null when there is none or it cannot be read or parsed.</summary>
        private async Task<EdidInfo?> ReadEdidAsync(AvasDevice displayRx)
        {
            try
            {
                return EdidInfo.Parse(await _sdvoeClient.ReadDisplayEdidAsync(displayRx.DeviceId));
            }
            catch (Exception ex)
            {
                AppLogger.Warn("EDID", $"EDID read for '{displayRx.DeviceName}' threw: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> UnrouteDisplayAsync(AvasDevice displayRx)
        {
            if (displayRx == null) return false;

            bool leaveOk = await _sdvoeClient.LeaveStreamAsync(displayRx.DeviceId, "HDMI", 0);
            displayRx.ActiveSourceId = string.Empty;
            displayRx.ActiveSourceName = string.Empty;
            return leaveOk;
        }
    }
}
