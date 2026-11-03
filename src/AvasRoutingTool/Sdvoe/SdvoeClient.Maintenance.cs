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

    /// <summary>Switches a transmitter (both of its chips) between single and dual link.</summary>
    public interface ILinkSwitcher
    {
        /// <summary>Sets the same mode on chip 0 and chip 1 of one transmitter and reboots the primary chip.</summary>
        Task<bool> SetLinkModeAsync(AvasDevice chip0, AvasDevice? chip1, LinkMode target);
    }

    /// <summary>
    /// The destructive actions of the Advanced page, ported from the wrapper's SdvoeClient. AVAS devices only (VendorId 105);
    /// anything else is refused without a call.
    /// </summary>
    public interface IMaintenanceActions
    {
        /// <summary>Power-cycles one AVP chip: Telnet <c>reboot &lt;mac&gt;</c>, REST <c>{op: reboot}</c> as the fallback.</summary>
        Task<bool> RebootChipAsync(AvasDevice device, string mac);

        /// <summary>Soft-reboots the MCU through chip 0: UART 1 in API mode, then <c>send:rs232</c> "set system reboot".</summary>
        Task<bool> RebootMcuAsync(AvasDevice device);

        /// <summary>Restores the MCU's factory defaults through chip 0: <c>send:rs232</c> "set factory default".</summary>
        Task<bool> FactoryResetMcuAsync(AvasDevice device);

        /// <summary>Restores one AVP chip to factory defaults: Telnet <c>factory &lt;mac&gt;</c>, REST <c>{op: factory_default}</c> then <c>{op: factory}</c> as fallbacks.</summary>
        Task<bool> FactoryResetChipAsync(AvasDevice device, string mac);
    }

    public partial class SdvoeClient : IDeviceReader, ILinkSwitcher, IMaintenanceActions
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
        /// Switches a dual-link transmitter: the same <c>set:multi_link</c> mode goes to chip 0 and then to chip 1, then the primary
        /// chip is rebooted and the Control Server coordinates the pair (wrapper MultiLinkService, which sent it to chip 0 only).
        /// Refused, with no call, unless both are online AVAS transmitter chips numbered 0 and 1. When chip 1 refuses the mode,
        /// chip 0 is put back to the other mode and no reboot is sent, so the pair is never left in two different modes. Returns
        /// false when any step fails, so a mode that was set but never applied is not reported as done.
        /// </summary>
        public async Task<bool> SetLinkModeAsync(AvasDevice chip0, AvasDevice? chip1, LinkMode target)
        {
            if (!IsLinkPair(chip0, chip1) || target == LinkMode.Unknown)
            {
                AppLogger.Warn("ADVANCED", $"Link switch refused for '{chip0?.DeviceName}': both chips of an AVAS transmitter must be online.");
                return false;
            }

            string mac0 = NormalizeMac(chip0.DeviceId);
            string mac1 = NormalizeMac(chip1!.DeviceId);
            string mode = ModeWord(target);
            AppLogger.Info("ADVANCED", $"Setting '{chip0.DeviceName}' ({mac0} + {mac1}) to {mode} link...");

            var set0 = await RequestAsync("POST", $"/api/device/{mac0}", new { op = "set:multi_link", mode });
            if (!set0.Ok)
            {
                AppLogger.Warn("ADVANCED", $"set:multi_link {mode} on chip 0 {mac0} failed: {set0.Failure ?? "no reply"}");
                return false;
            }

            var set1 = await RequestAsync("POST", $"/api/device/{mac1}", new { op = "set:multi_link", mode });
            if (!set1.Ok)
            {
                AppLogger.Warn("ADVANCED", $"set:multi_link {mode} on chip 1 {mac1} failed: {set1.Failure ?? "no reply"}. Putting chip 0 back.");
                var back = await RequestAsync("POST", $"/api/device/{mac0}", new { op = "set:multi_link", mode = ModeWord(target == LinkMode.Dual ? LinkMode.Single : LinkMode.Dual) });
                if (!back.Ok) AppLogger.Error("ADVANCED", $"Chip 0 {mac0} could not be put back: the pair may be in two different modes.");
                return false;
            }

            if (LinkSettleDelay > TimeSpan.Zero) await Task.Delay(LinkSettleDelay);

            var reboot = await RequestAsync("POST", $"/api/device/{mac0}", new { op = "reboot" });
            if (!reboot.Ok)
            {
                AppLogger.Warn("ADVANCED", $"Reboot of {mac0} after the link switch failed: {reboot.Failure ?? "no reply"}");
                return false;
            }
            return true;
        }

        private static string ModeWord(LinkMode mode) => mode == LinkMode.Dual ? "DUAL" : "SINGLE";

        private static bool IsLinkPair(AvasDevice? chip0, AvasDevice? chip1) =>
            chip0 != null && chip1 != null &&
            chip0.IsAvasDevice && chip1.IsAvasDevice &&
            chip0.IsTransmitter && chip1.IsTransmitter &&
            chip0.ChipId == 0 && chip1.ChipId == 1 &&
            chip0.IsActive && chip1.IsActive &&
            NormalizeMac(chip0.DeviceId) != NormalizeMac(chip1.DeviceId);

        public async Task<bool> RebootChipAsync(AvasDevice device, string mac)
        {
            string norm = NormalizeMac(mac);
            if (!IsMaintenanceTarget(device) || norm.Length == 0) return false;

            AppLogger.Info("ADVANCED", $"Rebooting chip {norm} of '{device.DeviceName}'...");
            if (await TelnetCommandOkAsync($"reboot {norm}")) return true;

            var rest = await RequestAsync("POST", $"/api/device/{norm}", new { op = "reboot" });
            if (!rest.Ok) AppLogger.Warn("ADVANCED", $"Reboot of {norm} failed: {rest.Failure ?? "no reply"}");
            return rest.Ok;
        }

        public Task<bool> RebootMcuAsync(AvasDevice device) => SendMcuCommandAsync(device, "set system reboot");

        public async Task<bool> FactoryResetChipAsync(AvasDevice device, string mac)
        {
            string norm = NormalizeMac(mac);
            if (!IsMaintenanceTarget(device) || norm.Length == 0) return false;

            AppLogger.Info("ADVANCED", $"Factory reset of chip {norm} of '{device.DeviceName}'...");
            if (await TelnetCommandOkAsync($"factory {norm}")) return true;

            string path = $"/api/device/{norm}";
            if ((await RequestAsync("POST", path, new { op = "factory_default" })).Ok) return true;

            var alt = await RequestAsync("POST", path, new { op = "factory" });
            if (!alt.Ok) AppLogger.Warn("ADVANCED", $"Factory reset of {norm} failed: {alt.Failure ?? "no reply"}");
            return alt.Ok;
        }

        public Task<bool> FactoryResetMcuAsync(AvasDevice device) => SendMcuCommandAsync(device, "set factory default");

        /// <summary>
        /// An in-band MCU command over UART 1 of chip 0 (wrapper SendMcuCommandAsync): the UART is switched to API mode first
        /// (its result is not decisive, as in the wrapper), then <c>send:rs232</c> carries the command; when REST fails the
        /// Telnet <c>send &lt;mac&gt; rs232:1 &lt;command&gt;</c> is tried. Refused for a non-AVAS device and for any chip but chip 0.
        /// </summary>
        private async Task<bool> SendMcuCommandAsync(AvasDevice device, string command)
        {
            if (!IsMaintenanceTarget(device)) return false;
            if (device.ChipId != 0)
            {
                AppLogger.Warn("ADVANCED", $"MCU command refused for '{device.DeviceName}': MCU commands go through chip 0.");
                return false;
            }

            string norm = NormalizeMac(device.DeviceId);
            string path = $"/api/device/{norm}";
            AppLogger.Info("ADVANCED", $"MCU command '{command}' to chip 0 {norm} of '{device.DeviceName}'...");

            await RequestAsync("POST", path, new { stream_type = "rs232", stream_index = 1, op = "switch:api" });
            var send = await RequestAsync("POST", path, new { op = "send:rs232", port_index = 1, data_string = command + "\r\n" });
            if (send.Ok) return true;

            AppLogger.Warn("ADVANCED", $"send:rs232 '{command}' to {norm} failed: {send.Failure ?? "no reply"}");
            return await TelnetCommandOkAsync($"send {norm} rs232:1 {command}");
        }

        private static bool IsMaintenanceTarget(AvasDevice? device)
        {
            if (device != null && device.IsAvasDevice) return true;
            AppLogger.Warn("ADVANCED", $"Maintenance action refused for '{device?.DeviceName}': AVAS devices only.");
            return false;
        }

        /// <summary>Sends a Telnet command and reports whether the Control Server answered SUCCESS or PROCESSING without an error. False when not connected.</summary>
        private async Task<bool> TelnetCommandOkAsync(string command)
        {
            if (!IsConnected) return false;
            var inspection = RestResponse.Inspect(await SendCommandAsync(command));
            return inspection != null && inspection.Failure == null &&
                   (string.Equals(inspection.Status, "SUCCESS", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(inspection.Status, "PROCESSING", StringComparison.OrdinalIgnoreCase));
        }
    }
}
