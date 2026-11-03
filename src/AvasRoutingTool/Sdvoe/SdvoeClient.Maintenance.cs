using System;
using System.Threading.Tasks;
using AvasRoutingTool.Logging;

namespace AvasRoutingTool.Sdvoe
{
    /// <summary>Reads one subset of a device from the Control Server. Lets the Advanced readers be tested without a server.</summary>
    public interface IDeviceReader
    {
        /// <summary>The raw reply of <c>get</c> for the subset, or null when the server refused or did not answer.</summary>
        Task<string?> ReadDeviceSubsetAsync(string mac, string subset);
    }

    /// <summary>Switches a transmitter between single and dual link.</summary>
    public interface ILinkSwitcher
    {
        Task<bool> SetLinkModeAsync(AvasDevice tx, LinkMode target);
    }

    public partial class SdvoeClient : IDeviceReader, ILinkSwitcher
    {
        public async Task<string?> ReadDeviceSubsetAsync(string mac, string subset)
        {
            string norm = NormalizeMac(mac);
            var result = await RequestAsync("POST", $"/api/device/{norm}", new { op = "get", subset });
            if (!result.Ok || result.Body == null)
            {
                AppLogger.Warn("ADVANCED", $"Could not read '{subset}' of {norm}: {result.Failure ?? "no reply"}");
                return null;
            }
            return result.Body;
        }

        /// <summary>Pause between set:multi_link and the reboot, as in the wrapper (the Control Server settles first).</summary>
        public TimeSpan LinkSettleDelay { get; set; } = TimeSpan.FromMilliseconds(300);

        /// <summary>
        /// Wrapper MultiLinkService.SetMultiLinkModeAsync: set:multi_link goes to the primary chip (chip 0) only, then the
        /// primary chip is rebooted and the Control Server coordinates the companion. Refused (no call) for a non-AVAS device,
        /// a receiver, a companion chip, or an unknown target. Returns false when either step fails, so a mode that was
        /// set but never applied is not reported as done.
        /// </summary>
        public async Task<bool> SetLinkModeAsync(AvasDevice tx, LinkMode target)
        {
            if (tx == null || !tx.IsAvasDevice || !tx.IsTransmitter || tx.ChipId != 0 || target == LinkMode.Unknown)
            {
                AppLogger.Warn("ADVANCED", $"Link switch refused for '{tx?.DeviceName}' (AVAS primary transmitter chip required).");
                return false;
            }

            string norm = NormalizeMac(tx.DeviceId);
            string mode = target == LinkMode.Dual ? "DUAL" : "SINGLE";
            AppLogger.Info("ADVANCED", $"Setting '{tx.DeviceName}' ({norm}) to {mode} link...");

            var set = await RequestAsync("POST", $"/api/device/{norm}", new { op = "set:multi_link", mode });
            if (!set.Ok)
            {
                AppLogger.Warn("ADVANCED", $"set:multi_link {mode} on {norm} failed: {set.Failure ?? "no reply"}");
                return false;
            }

            if (LinkSettleDelay > TimeSpan.Zero) await Task.Delay(LinkSettleDelay);

            var reboot = await RequestAsync("POST", $"/api/device/{norm}", new { op = "reboot" });
            if (!reboot.Ok)
            {
                AppLogger.Warn("ADVANCED", $"Reboot of {norm} after the link switch failed: {reboot.Failure ?? "no reply"}");
                return false;
            }
            return true;
        }
    }
}
