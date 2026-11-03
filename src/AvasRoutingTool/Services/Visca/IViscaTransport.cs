using System;
using System.Threading;
using System.Threading.Tasks;

namespace AvasRoutingTool.Services.Visca
{
    /// <summary>
    /// Moves raw VISCA bytes to one camera and back. Implementations own their socket
    /// and release it in DisposeAsync. MessageReceived carries one complete VISCA
    /// message including its FF terminator, on a background thread.
    /// </summary>
    public interface IViscaTransport : IAsyncDisposable
    {
        Task ConnectAsync(CancellationToken ct);

        /// <summary>isInquiry tells a datagram transport which packets need a reply; TCP ignores it.</summary>
        Task SendAsync(byte[] visca, bool isInquiry, CancellationToken ct);

        event Action<byte[]> MessageReceived;

        /// <summary>Raised once when the connection is lost unexpectedly (null for a clean close by the camera). Never raised by DisposeAsync.</summary>
        event Action<Exception?> Disconnected;

        bool IsConnected { get; }
    }
}
