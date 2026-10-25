using System.Linq;
using System.Text.Json;

namespace AvasRoutingTool.Sdvoe
{
    /// <summary>
    /// Interprets Semtech BlueRiver Control Server REST replies.
    /// Parses JSON structurally so formatting (pretty-printed spacing) never matters.
    /// </summary>
    public static class RestResponse
    {
        /// <summary>What a reply says: its status, the background request id, and why it failed (if it did).</summary>
        public sealed record Inspection(string Status, long? RequestId, string? Failure);

        /// <summary>
        /// Reads status / request_id, and collects failures: an ERROR status (with its error object) or a
        /// non-empty per-device <c>result.error</c> array, which is how a background request reports that a
        /// device refused it after the call itself returned SUCCESS/PROCESSING.
        /// </summary>
        public static Inspection? Inspect(string? body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("status", out var statusEl) || statusEl.ValueKind != JsonValueKind.String)
                    return null;

                string status = statusEl.GetString() ?? string.Empty;

                long? requestId = root.TryGetProperty("request_id", out var idEl) && idEl.ValueKind == JsonValueKind.Number
                    ? idEl.GetInt64()
                    : null;

                string? failure = null;
                if (root.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.Object)
                {
                    string reason = errEl.TryGetProperty("reason", out var r) ? r.ToString() : "ERROR";
                    string message = errEl.TryGetProperty("message", out var m) ? m.ToString() : string.Empty;
                    failure = string.IsNullOrEmpty(message) ? reason : $"{reason}: {message}";
                }
                else if (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object &&
                         result.TryGetProperty("error", out var devErrs) && devErrs.ValueKind == JsonValueKind.Array &&
                         devErrs.GetArrayLength() > 0)
                {
                    failure = string.Join("; ", devErrs.EnumerateArray().Select(e => e.ToString()));
                }

                if (failure == null && string.Equals(status, "ERROR", System.StringComparison.OrdinalIgnoreCase))
                    failure = "ERROR";

                return new Inspection(status, requestId, failure);
            }
            catch (JsonException)
            {
                return null;
            }
        }

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
