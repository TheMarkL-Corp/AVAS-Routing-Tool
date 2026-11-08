using System;
using System.Text.Json;

namespace AvasRoutingTool.Sdvoe.MultiView
{
    /// <summary>Reads the video a transmitter's HDMI input sees from a <c>get settings</c> reply. Never throws.</summary>
    public static class SourceVideoReader
    {
        public static SourceVideo? Parse(string? settingsBody)
        {
            if (string.IsNullOrWhiteSpace(settingsBody)) return null;
            try
            {
                using var doc = JsonDocument.Parse(settingsBody);
                foreach (var device in doc.RootElement.GetProperty("result").GetProperty("devices").EnumerateArray())
                {
                    if (!device.TryGetProperty("nodes", out var nodes)) continue;
                    foreach (var node in nodes.EnumerateArray())
                    {
                        if (!node.TryGetProperty("type", out var type) || type.GetString() != "HDMI_DECODER") continue;
                        if (!node.TryGetProperty("status", out var status) ||
                            !status.TryGetProperty("video", out var video)) continue;

                        int w = Int(video, "width"), h = Int(video, "height");
                        double fps = video.TryGetProperty("frames_per_second", out var f) && f.ValueKind == JsonValueKind.Number ? f.GetDouble() : 0;
                        if (w <= 0 || h <= 0 || fps <= 0) continue; // no signal on this input

                        string space = video.TryGetProperty("color_space", out var cs) ? cs.GetString() ?? "" : "";
                        int depth = Int(video, "bits_per_pixel"); // bits per colour component
                        if (depth <= 0) depth = 8;
                        return new SourceVideo(w, h, fps, BitsPerPixel(space, depth));
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { }
            return null;
        }

        /// <summary>RGB / 4:4:4 = 3 components, 4:2:2 = 2, 4:2:0 = 1.5. 12-bit 4:2:2 therefore matches the live 24 bpp figure.</summary>
        public static int BitsPerPixel(string colorSpace, int componentDepth)
        {
            string s = colorSpace.ToUpperInvariant();
            double components = s.Contains("420") ? 1.5 : s.Contains("422") ? 2 : 3;
            return (int)Math.Round(components * componentDepth);
        }

        private static int Int(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
    }
}
