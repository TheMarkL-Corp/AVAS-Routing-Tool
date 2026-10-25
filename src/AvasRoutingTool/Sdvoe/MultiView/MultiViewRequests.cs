using System.Collections.Generic;
using System.Linq;

namespace AvasRoutingTool.Sdvoe.MultiView
{
    /// <summary>One HTTP call against the BlueRiver Control Server REST API.</summary>
    public sealed record RestCall(string Method, string Path, object? Body);

    /// <summary>
    /// A source feeding one multiview surface. The subscription index equals the target surface (API §5.4 examples).
    /// <see cref="StreamIndex"/> is the encoder's HDMI stream: 1 (the scaled stream) for every layout this tool makes;
    /// a copied external layout keeps the stream it was found subscribing.
    /// </summary>
    public sealed record MultiViewSubscription(string SourceMac, int SubscriptionIndex, int StreamIndex = 1);

    /// <summary>
    /// Pure builders for the multiview REST calls, shaped after the SDVoE API Reference 3.9
    /// (§5.2 layout/window/set multiview, §4.19.7 set scaler, §4.21 start, §4.19.12 set video).
    /// </summary>
    public static class MultiViewRequests
    {
        public static RestCall CreateLayout(string layoutName, int width, int height) =>
            new("POST", $"/api/multiview/layout/{layoutName}", new { op = "create", width, height });

        /// <summary>
        /// A window on <paramref name="surface"/>; null leaves <c>target_surface</c> out, which the decoder
        /// shows as black (lab-verified). The offsets crop into the surface (0 for every layout this tool makes;
        /// a copied external layout keeps its own).
        /// </summary>
        public static RestCall CreateWindow(string layoutName, WindowSlot slot, int? surface, int horizontalOffset = 0, int verticalOffset = 0)
        {
            var body = new Dictionary<string, object>
            {
                ["op"] = "create",
                ["horizontal_position"] = slot.X,
                ["vertical_position"] = slot.Y,
                ["width"] = slot.Width,
                ["height"] = slot.Height,
                ["horizontal_offset"] = horizontalOffset,
                ["vertical_offset"] = verticalOffset,
            };
            if (surface.HasValue) body["target_surface"] = surface.Value;
            return new("POST", $"/api/multiview/layout/{layoutName}/window/{slot.Index}", body);
        }

        public static RestCall SetScaler(string txMac, int width, int height) =>
            new("POST", DevicePath(txMac), new { op = "set:scaler", width, height });

        public static RestCall StartScaledStream(string txMac) =>
            new("POST", DevicePath(txMac), new { op = "start", stream_type = "HDMI", stream_index = 1 });

        public static RestCall SetMultiView(string rxMac, string layoutName, int width, int height, int fps,
            IEnumerable<MultiViewSubscription> subscriptions) =>
            new("POST", DevicePath(rxMac), new
            {
                op = "set:multiview",
                layout = layoutName,
                video = new { width, height, fps },
                subscriptions = subscriptions.Select(s => new
                {
                    source_device = SdvoeClient.NormalizeMac(s.SourceMac),
                    stream_index = s.StreamIndex,
                    subscription_index = s.SubscriptionIndex,
                }).ToArray(),
            });

        /// <summary>
        /// Routes a TX's scaled stream (stream 1) through the frame-rate converter (divide by 2) or straight from
        /// the HDMI input. Property key and values: API Reference §7.15 and the §5.4 multiview examples.
        /// </summary>
        public static RestCall SetScalerFrameDivider(string txMac, bool halve) =>
            new("POST", DevicePath(txMac), new
            {
                op = "set:property",
                key = "nodes[SCALER:0].inputs[main:0].configuration.source.value",
                value = halve ? 1 : 0,
            });

        public static RestCall SetGenlocked(string rxMac) =>
            new("POST", DevicePath(rxMac), new { op = "set:video", display_mode = "GENLOCKED" });

        private static string DevicePath(string mac) => $"/api/device/{SdvoeClient.NormalizeMac(mac)}";
    }
}
