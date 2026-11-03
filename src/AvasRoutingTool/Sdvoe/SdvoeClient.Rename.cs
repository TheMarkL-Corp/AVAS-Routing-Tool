using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AvasRoutingTool.Logging;

namespace AvasRoutingTool.Sdvoe
{
    /// <summary>Outcome of a device rename: Ok, or the reason it failed (the server's own message when it gave one).</summary>
    public sealed record RenameResult(bool Ok, string? Reason)
    {
        public static RenameResult Success { get; } = new(true, null);

        public static RenameResult Failed(string reason) => new(false, reason);
    }

    /// <summary>Renames a device on the Control Server. Separate from ISdvoeClient so only real clients offer it.</summary>
    public interface IDeviceRenamer
    {
        Task<RenameResult> RenameDeviceAsync(string mac, string newName);
    }

    public partial class SdvoeClient : IDeviceRenamer
    {
        /// <summary>
        /// Renames a device (REST set property <c>configuration.device_name</c>). Never throws: a rejected name
        /// comes back as the Control Server's own message, an unreachable server as a plain sentence.
        /// </summary>
        public async Task<RenameResult> RenameDeviceAsync(string mac, string newName)
        {
            string norm = NormalizeMac(mac);
            if (norm.Length == 0) return RenameResult.Failed("No device was given to rename");
            if (string.IsNullOrWhiteSpace(newName)) return RenameResult.Failed("The new name is empty");

            try
            {
                var payload = new Dictionary<string, object>
                {
                    ["op"] = "set:property",
                    ["key"] = "configuration.device_name",
                    ["value"] = newName
                };
                var result = await RequestAsync("POST", $"/api/device/{norm}", payload);
                if (result.Ok)
                {
                    AppLogger.Info("RENAME", $"{norm} renamed to '{newName}'");
                    return RenameResult.Success;
                }

                string reason = RenameFailureReason(result);
                AppLogger.Warn("RENAME", $"Renaming {norm} to '{newName}' failed: {reason}");
                return RenameResult.Failed(reason);
            }
            catch (Exception ex)
            {
                string reason = $"Couldn't reach the Control Server: {ex.Message}";
                AppLogger.Warn("RENAME", $"Renaming {norm} to '{newName}' failed: {reason}");
                return RenameResult.Failed(reason);
            }
        }

        private static string RenameFailureReason(RequestResult result)
        {
            if (string.IsNullOrWhiteSpace(result.Failure) || result.Failure is "unreadable reply" or "unreadable request reply" or "HTTP error")
                return "The Control Server gave no reply";

            // The call itself threw (no reply body at all): the failure text is the exception message.
            if (result.Body == null)
                return $"Couldn't reach the Control Server: {result.Failure}";

            // RestResponse.Inspect formats a server error as "<REASON>: <message>"; the person wants the message.
            return Regex.Replace(result.Failure, @"^[A-Z][A-Z0-9_]*: ", string.Empty);
        }
    }
}
