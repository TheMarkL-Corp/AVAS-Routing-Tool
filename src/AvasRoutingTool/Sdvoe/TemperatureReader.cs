using System.Text.Json;
using System.Threading.Tasks;

namespace AvasRoutingTool.Sdvoe
{
    /// <summary>Reads a device's temperature (status.temperature of the <c>device</c> subset), as the wrapper's TemperatureService does.</summary>
    public class TemperatureReader
    {
        private readonly IDeviceReader _reader;

        public TemperatureReader(IDeviceReader reader) => _reader = reader;

        /// <summary>Degrees Celsius, or null when the device reports none or the read failed. Never 0 for "unknown".</summary>
        public async Task<int?> ReadAsync(string mac) => Parse(await _reader.ReadDeviceSubsetAsync(mac, "device"));

        public static int? Parse(string? json)
        {
            var device = DeviceReplyReader.FirstDevice(json, out var doc);
            using (doc)
            {
                if (device == null) return null;
                if (device.Value.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Object &&
                    status.TryGetProperty("temperature", out var temp) && temp.ValueKind == JsonValueKind.Number &&
                    temp.TryGetInt32(out int value))
                {
                    return value;
                }
                return null;
            }
        }
    }

    public static class TemperatureFormat
    {
        /// <summary>Warning from this temperature (wrapper threshold).</summary>
        public const int WarningFrom = 61;
        /// <summary>Critical above this temperature (wrapper threshold).</summary>
        public const int CriticalAbove = 70;

        public static (string Text, string State) Describe(int? degrees)
        {
            if (degrees == null) return ("n/a", "Unknown");
            string state = degrees > CriticalAbove ? "Hot" : degrees >= WarningFrom ? "Warm" : "Normal";
            return ($"{degrees} °C", state);
        }
    }
}
