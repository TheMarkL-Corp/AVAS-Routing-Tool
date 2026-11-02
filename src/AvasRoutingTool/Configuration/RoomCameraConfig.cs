using System;
using System.Text.Json.Serialization;

namespace AvasRoutingTool.Configuration
{
    public enum ViscaTransportKind { Udp, Tcp }

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
    }
}
