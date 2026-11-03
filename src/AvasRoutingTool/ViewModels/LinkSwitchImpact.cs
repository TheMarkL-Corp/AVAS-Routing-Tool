using System.Collections.Generic;
using System.Linq;
using AvasRoutingTool.Sdvoe;

namespace AvasRoutingTool.ViewModels
{
    /// <summary>What a link switch of one transmitter touches: displays routed to it in Single View, and displays whose multi-view uses it.</summary>
    public sealed record LinkSwitchImpact(IReadOnlyList<string> SingleViewDisplays, IReadOnlyList<string> MultiViewDisplays)
    {
        public static LinkSwitchImpact None { get; } = new(new List<string>(), new List<string>());

        public bool HasAny => SingleViewDisplays.Any() || MultiViewDisplays.Any();
    }

    /// <summary>Asks the operator to confirm a link switch: names the device, the target mode and what is affected.</summary>
    public sealed record LinkSwitchRequest(string DeviceName, LinkMode Target, LinkSwitchImpact Impact);
}
