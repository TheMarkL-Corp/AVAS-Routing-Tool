using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace AvasRoutingTool.Sdvoe
{
    /// <summary>
    /// Pure parser for a receiver's <c>get settings</c> reply (display mode, output, HDMI subscriptions) and
    /// <c>get multiview</c> reply (loaded layout). Tolerant: anything unexpected yields <c>null</c>, never an exception.
    /// </summary>
    public static class RxStateReader
    {
        public static RxState? Parse(string? settingsJson, string? layoutJson = null)
        {
            var device = FirstDevice(settingsJson, out var doc);
            if (device == null) return null;

            using (doc)
            {
                string id = Str(device.Value, "device_id");
                string name = Str(device.Value, "device_name");

                string mode = string.Empty;
                int width = 0, height = 0, fps = 0;
                if (device.Value.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array)
                {
                    foreach (var node in nodes.EnumerateArray())
                    {
                        if (Str(node, "type") != "FRAME_BUFFER" || !node.TryGetProperty("configuration", out var cfg)) continue;
                        mode = Str(cfg, "display_mode");
                        width = Int(cfg, "width");
                        height = Int(cfg, "height");
                        fps = Int(cfg, "frames_per_second");
                        break;
                    }
                }

                var subs = new List<RxSubscription>();
                if (device.Value.TryGetProperty("subscriptions", out var subscriptions) && subscriptions.ValueKind == JsonValueKind.Array)
                {
                    foreach (var sub in subscriptions.EnumerateArray())
                    {
                        if (Str(sub, "type") != "HDMI") continue;

                        bool enabled = sub.TryGetProperty("configuration", out var cfg) &&
                                       cfg.TryGetProperty("enable", out var en) && en.ValueKind == JsonValueKind.True;

                        string state = string.Empty, srcMac = string.Empty, srcName = string.Empty;
                        int srcStream = 0;
                        if (sub.TryGetProperty("status", out var status))
                        {
                            state = Str(status, "state");
                            if (status.TryGetProperty("source", out var src) && src.ValueKind == JsonValueKind.Object)
                            {
                                srcMac = Str(src, "device_id");
                                srcName = Str(src, "device_name");
                                srcStream = Int(src, "stream_index");
                            }
                        }

                        subs.Add(new RxSubscription(Int(sub, "index"), enabled, state, srcMac, srcName, srcStream));
                    }
                }

                var state0 = new RxState(id, name, mode, width, height, fps, subs, null);
                return state0.IsMultiView && layoutJson != null
                    ? state0 with { Layout = ParseLayout(layoutJson) }
                    : state0;
            }
        }

        /// <summary>Parses a <c>get multiview</c> reply. Returns null for the INVALID_STATE error a non-multiview receiver gives.</summary>
        public static RxLayout? ParseLayout(string? json)
        {
            var device = FirstDevice(json, out var doc);
            if (device == null) return null;

            using (doc)
            {
                if (!device.Value.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array) return null;

                foreach (var node in nodes.EnumerateArray())
                {
                    if (Str(node, "type") != "FRAME_BUFFER" ||
                        !node.TryGetProperty("configuration", out var cfg) ||
                        !cfg.TryGetProperty("multiview_layout", out var layout) ||
                        layout.ValueKind != JsonValueKind.Object) continue;

                    var windows = new List<RxWindow>();
                    if (layout.TryGetProperty("windows", out var ws) && ws.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var w in ws.EnumerateArray())
                        {
                            string content = w.TryGetProperty("content", out _) ? Str(w, "content") : "VIDEO";
                            if (content != "VIDEO" || !w.TryGetProperty("target_surface", out _)) continue;
                            windows.Add(new RxWindow(Int(w, "index"), Int(w, "target_surface")));
                        }
                    }

                    return new RxLayout(Str(layout, "name"), Int(layout, "width"), Int(layout, "height"), windows);
                }
            }
            return null;
        }

        private static JsonElement? FirstDevice(string? json, out JsonDocument? doc)
        {
            doc = null;
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object &&
                    string.Equals(Str(root, "status"), "SUCCESS", StringComparison.OrdinalIgnoreCase) &&
                    root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object &&
                    result.TryGetProperty("devices", out var devices) && devices.ValueKind == JsonValueKind.Array &&
                    devices.GetArrayLength() > 0)
                {
                    return devices[0];
                }
            }
            catch (JsonException)
            {
            }

            doc?.Dispose();
            doc = null;
            return null;
        }

        private static string Str(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? string.Empty
                : string.Empty;

        private static int Int(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i)
                ? i
                : 0;
    }
}
