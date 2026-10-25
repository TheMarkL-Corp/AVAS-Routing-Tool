using System;
using System.Collections.Generic;
using System.Linq;

namespace AvasRoutingTool.Sdvoe
{
    /// <summary>One HDMI subscription of a receiver as the Control Server reports it.</summary>
    public sealed record RxSubscription(
        int Index,
        bool Enabled,
        string State,
        string SourceMac,
        string SourceName,
        int SourceStream)
    {
        /// <summary>True when the subscription is enabled and bound to a source device.</summary>
        public bool IsRouted => Enabled && !string.IsNullOrEmpty(SourceMac);
    }

    /// <summary>A window of the layout currently loaded on a receiver, and the surface (subscription) feeding it.</summary>
    public sealed record RxWindow(int WindowIndex, int Surface);

    /// <summary>The multiview layout currently loaded on a receiver (<c>get multiview</c>).</summary>
    public sealed record RxLayout(string Name, int Width, int Height, IReadOnlyList<RxWindow> Windows);

    /// <summary>What a receiver is really doing, read from the hardware.</summary>
    public sealed record RxState(
        string DeviceId,
        string DeviceName,
        string DisplayMode,
        int Width,
        int Height,
        int Fps,
        IReadOnlyList<RxSubscription> Subscriptions,
        RxLayout? Layout)
    {
        public bool IsMultiView => string.Equals(DisplayMode, "MULTIVIEW", StringComparison.OrdinalIgnoreCase);

        public IEnumerable<RxSubscription> RoutedSubscriptions => Subscriptions.Where(s => s.IsRouted);

        /// <summary>The Single View source (HDMI subscription 0), or null when unrouted or in multiview.</summary>
        public RxSubscription? SingleViewSource =>
            IsMultiView ? null : Subscriptions.FirstOrDefault(s => s.Index == 0 && s.IsRouted);
    }
}
