using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace AvasRoutingTool.Sdvoe
{
    public enum LinkMode { Unknown, Single, Dual }

    /// <summary>A transmitter's link mode as the Control Server reports it (the read side of the wrapper's MultiLinkInfo).</summary>
    public sealed record LinkModeInfo(LinkMode Mode, string CompanionMac, string Capabilities)
    {
        public static LinkModeInfo Unknown { get; } = new(LinkMode.Unknown, "NONE", "");

        public bool HasCompanion => !string.IsNullOrEmpty(CompanionMac) &&
                                    !string.Equals(CompanionMac, "NONE", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reads link mode, companion chip and capabilities from the MULTI_LINK_TRANSMITTER node of the <c>device</c> subset.</summary>
    public class LinkModeReader
    {
        private readonly IDeviceReader _reader;

        public LinkModeReader(IDeviceReader reader) => _reader = reader;

        public async Task<LinkModeInfo> ReadAsync(string mac) => Parse(await _reader.ReadDeviceSubsetAsync(mac, "device"));

        public static LinkModeInfo Parse(string? json)
        {
            var device = DeviceReplyReader.FirstDevice(json, out var doc);
            using (doc)
            {
                if (device == null || !device.Value.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
                    return LinkModeInfo.Unknown;

                foreach (var node in nodes.EnumerateArray())
                {
                    if (!node.TryGetProperty("type", out var type) ||
                        !string.Equals(type.GetString(), "MULTI_LINK_TRANSMITTER", StringComparison.OrdinalIgnoreCase)) continue;

                    var mode = LinkMode.Unknown;
                    if (node.TryGetProperty("configuration", out var cfg) && cfg.TryGetProperty("link_mode", out var lm))
                    {
                        mode = (lm.GetString() ?? "").ToUpperInvariant() switch
                        {
                            "SINGLE" => LinkMode.Single,
                            "DUAL" => LinkMode.Dual,
                            _ => LinkMode.Unknown
                        };
                    }

                    string companion = "NONE";
                    string caps = "";
                    if (node.TryGetProperty("status", out var status))
                    {
                        if (status.TryGetProperty("companions", out var comps) && comps.ValueKind == JsonValueKind.Array && comps.GetArrayLength() > 0 &&
                            comps[0].TryGetProperty("device_id", out var cid))
                        {
                            companion = cid.GetString() ?? "NONE";
                        }
                        if (status.TryGetProperty("link_capabilities", out var capArr) && capArr.ValueKind == JsonValueKind.Array)
                        {
                            var list = new List<string>();
                            foreach (var c in capArr.EnumerateArray())
                                if (c.TryGetProperty("mode", out var m) && !string.IsNullOrEmpty(m.GetString())) list.Add(m.GetString()!);
                            caps = string.Join(",", list);
                        }
                    }
                    return new LinkModeInfo(mode, companion, caps);
                }
                return LinkModeInfo.Unknown;
            }
        }
    }
}
