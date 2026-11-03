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

    public partial class SdvoeClient : IDeviceReader
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
    }
}
