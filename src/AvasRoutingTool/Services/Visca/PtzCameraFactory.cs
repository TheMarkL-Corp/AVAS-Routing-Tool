using System;
using AvasRoutingTool.Configuration;

namespace AvasRoutingTool.Services.Visca
{
    /// <summary>Builds a ViscaCamera with the real transport for the config's Transport, Host and Port.</summary>
    public sealed class PtzCameraFactory : IPtzCameraFactory
    {
        public IPtzCamera Create(RoomCameraConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            return new ViscaCamera(config, () => DateTime.UtcNow);
        }

        public static IViscaTransport CreateTransport(RoomCameraConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            return config.Transport switch
            {
                ViscaTransportKind.Tcp => new TcpViscaTransport(config.Host, config.Port),
                ViscaTransportKind.Udp => new UdpViscaTransport(config.Host, config.Port),
                _ => throw new ArgumentOutOfRangeException(nameof(config), config.Transport, "Unknown VISCA transport.")
            };
        }
    }
}
