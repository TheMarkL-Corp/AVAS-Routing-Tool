using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AvasRoutingTool.Sdvoe
{
    public interface ISdvoeClient : IDisposable
    {
        bool IsConnected { get; }
        event Action<string, bool>? DeviceStateChanged;

        Task<bool> ConnectAsync(string host, int telnetPort = 6970, int restPort = 8080);
        Task DisconnectAsync();

        Task<bool> JoinStreamAsync(string rxMac, string txMac, string streamType = "HDMI", int streamIndex = 0);
        Task<bool> LeaveStreamAsync(string rxMac, string streamType = "HDMI", int streamIndex = 0);

        Task<bool> StartStream1ScalerAsync(string txMac, string multicastIp, int port = 5004);
        Task<bool> StopStream1ScalerAsync(string txMac);

        Task<List<AvasDevice>> DiscoverDevicesAsync();
    }
}
