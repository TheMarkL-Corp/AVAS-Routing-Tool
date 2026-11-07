using System;
using System.Text.Json.Serialization;

namespace AvasRoutingTool.Configuration
{
    public enum ViscaTransportKind { Udp, Tcp }

    /// <summary>Where a camera reference picture comes from on the Room Cameras page.</summary>
    public enum RoomCameraPreviewMode { Tx, Rtsp }

    public class RoomCameraConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");   // stable key
        public string Name { get; set; } = "Room camera";
        public string Host { get; set; } = "";
        public int Port { get; set; } = 52381;
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ViscaTransportKind Transport { get; set; } = ViscaTransportKind.Udp;
        public int ViscaAddress { get; set; } = 1;                       // 1-7
        public string BoundTxMac { get; set; } = "";                     // AvasDevice.DeviceId; empty = unbound
        public int ZoomMax { get; set; } = 0x4000;
        public int FocusMax { get; set; } = 0xFFFF;
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public RoomCameraPreviewMode PreviewMode { get; set; } = RoomCameraPreviewMode.Tx;
        public string RtspUrl { get; set; } = "";                        // whole URL, credentials included (plain text by decision D-3)
    }
}
