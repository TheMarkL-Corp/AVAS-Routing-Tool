using System;
using System.Text.Json;

namespace AvasRoutingTool.Sdvoe
{
    /// <summary>Shared parsing of a <c>get device</c> reply: result.devices[0], tolerant of anything unexpected.</summary>
    internal static class DeviceReplyReader
    {
        /// <summary>Parses the reply and returns its first device. The caller disposes the document.</summary>
        public static JsonElement? FirstDevice(string? json, out JsonDocument? doc)
        {
            doc = null;
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object &&
                    result.TryGetProperty("devices", out var devices) && devices.ValueKind == JsonValueKind.Array &&
                    devices.GetArrayLength() > 0)
                {
                    return devices[0];
                }
            }
            catch (JsonException) { }
            doc?.Dispose();
            doc = null;
            return null;
        }
    }
}
