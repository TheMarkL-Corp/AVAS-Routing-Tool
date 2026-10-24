using System;
using System.Threading.Tasks;

namespace AvasRoutingTool.Sdvoe
{
    public class RoutingOrchestrator
    {
        private readonly ISdvoeClient _sdvoeClient;

        public RoutingOrchestrator(ISdvoeClient sdvoeClient)
        {
            _sdvoeClient = sdvoeClient ?? throw new ArgumentNullException(nameof(sdvoeClient));
        }

        public async Task<bool> RouteSingleViewVideoAsync(AvasDevice sourceTx, AvasDevice displayRx)
        {
            if (sourceTx == null || displayRx == null) return false;

            // Phase 1: Video Routing Only (HDMI:0)
            bool videoOk = await _sdvoeClient.JoinStreamAsync(displayRx.DeviceId, sourceTx.DeviceId, "HDMI", 0);

            if (videoOk)
            {
                displayRx.ActiveSourceId = sourceTx.DeviceId;
                displayRx.ActiveSourceName = sourceTx.DeviceName;
            }

            return videoOk;
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
