using System.Text.Json;

namespace AvasRoutingTool.Sdvoe
{
    /// <summary>
    /// Interprets Semtech BlueRiver Control Server REST replies.
    /// Parses JSON structurally so formatting (pretty-printed spacing) never matters.
    /// </summary>
    public static class RestResponse
    {
        /// <summary>True when the server accepted the request (status SUCCESS or PROCESSING).</summary>
        public static bool IsAccepted(string? body)
        {
            if (string.IsNullOrWhiteSpace(body)) return false;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("status", out var status) &&
                    status.ValueKind == JsonValueKind.String)
                {
                    var s = status.GetString();
                    return string.Equals(s, "SUCCESS", System.StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(s, "PROCESSING", System.StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (JsonException)
            {
            }
            return false;
        }
    }
}
