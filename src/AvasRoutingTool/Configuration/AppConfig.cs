using System;
using System.Collections.Generic;

namespace AvasRoutingTool.Configuration
{
    public class AppConfig
    {
        public string BavmUrl { get; set; } = "http://localhost:80";
        public int TelnetPort { get; set; } = 6970;
        public int RestPort { get; set; } = 8080;
        public string MulticastStartIp { get; set; } = "225.0.0.1";
        public string MulticastEndIp { get; set; } = "225.0.1.254";
        public int BasePort { get; set; } = 5004;
        public string LocalNetworkInterfaceIp { get; set; } = "";
        public string Theme { get; set; } = "Dark";
        /// <summary>Logs tab/section is hidden until this is switched on in Settings.</summary>
        public bool ShowLogs { get; set; } = false;
        public List<RoomCameraConfig> RoomCameras { get; set; } = new();
        /// <summary>Advanced (maintenance) tab is hidden until this is switched on in Settings.</summary>
        public bool ShowAdvanced { get; set; } = false;

        public string DerivedControlServerIp
        {
            get
            {
                try
                {
                    if (Uri.TryCreate(BavmUrl, UriKind.Absolute, out var uri))
                    {
                        string host = uri.Host;
                        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
                        {
                            return "127.0.0.1";
                        }
                        return host;
                    }
                }
                catch { }
                return "127.0.0.1";
            }
        }
    }
}
