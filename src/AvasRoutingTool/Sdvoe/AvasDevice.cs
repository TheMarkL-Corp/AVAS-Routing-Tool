using System.Collections.Generic;

namespace AvasRoutingTool.Sdvoe
{
    public class AvasDevice
    {
        public string DeviceId { get; set; } = string.Empty; // MAC Address
        public string DeviceName { get; set; } = string.Empty; // Strict BAVM Device Name
        public string IpAddress { get; set; } = string.Empty;
        public string Model { get; set; } = "AVAS-223";
        public int VendorId { get; set; } = 105;
        public int ProductId { get; set; } = 81;
        public int ChipId { get; set; } = 0;
        public bool IsTransmitter { get; set; }
        public bool IsReceiver { get; set; }
        public bool IsActive { get; set; } = true;
        public string ActiveSourceId { get; set; } = string.Empty; // For RX: currently routed TX MAC
        public string ActiveSourceName { get; set; } = string.Empty; // For RX: currently routed TX DeviceName
        public string MulticastIp { get; set; } = string.Empty;
        public int MulticastPort { get; set; } = 5000;
        public double FramesPerSecond { get; set; }
        public string Resolution { get; set; } = "1080p60";

        public string LinkMode { get; set; } = "Single Link";
        public bool IsAvasDevice => VendorId == 105;
        public bool IsPreviewSupported => IsAvasDevice && IsTransmitter;
        public List<string> NodeIps { get; set; } = new();
    }
}
