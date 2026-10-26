using System;
using System.Collections.Generic;
using System.Linq;

namespace AvasRoutingTool.Sdvoe.MultiView
{
    /// <summary>A source found feeding a multiview window on a receiver.</summary>
    public sealed record RestoredSource(int SlotIndex, string Mac, string Name);

    /// <summary>
    /// A multiview layout found on a receiver. <see cref="Plan"/> and <see cref="Draft"/> are set only for layouts
    /// this tool created (<see cref="MultiViewPresets.TryParseName"/>); anything else is external and shown read-only.
    /// </summary>
    /// <param name="Draft">What is on the screen, as a draft: a subscription read by several windows fills each of their blocks.</param>
    public sealed record RestoredMultiView(
        string LayoutName,
        MultiViewLayoutPlan? Plan,
        MultiViewCanvas Canvas,
        IReadOnlyList<RestoredSource> Sources,
        IReadOnlyList<int> SubscriptionIndexes,
        MultiViewDraft? Draft = null)
    {
        public bool IsRecognised => Plan != null;
    }

    /// <summary>Turns a receiver's hardware state into what the cards need to show (pure).</summary>
    public static class MultiViewRestore
    {
        /// <summary>
        /// The encoders an external layout's windows show, at their window sizes: a window shows the routed
        /// subscription whose index is its surface. Windows without a known size (read back as 0) or without a routed
        /// subscription are left out (they constrain no scaler).
        /// </summary>
        public static IReadOnlyList<OtherDisplayUse> ExternalUses(string displayName, RxLayout? layout, IEnumerable<RxSubscription> subscriptions)
        {
            if (layout == null) return Array.Empty<OtherDisplayUse>();

            var routed = subscriptions.Where(s => s.IsRouted).GroupBy(s => s.Index).ToDictionary(g => g.Key, g => g.First());
            return layout.Windows
                .OrderBy(w => w.WindowIndex)
                .Where(w => w.Width > 0 && w.Height > 0 && routed.ContainsKey(w.Surface))
                .Select(w => new OtherDisplayUse(displayName, routed[w.Surface].SourceMac, w.Width, w.Height))
                .ToList();
        }

        /// <summary>Returns null when the receiver is not in multiview.</summary>
        public static RestoredMultiView? Analyze(RxState state)
        {
            if (!state.IsMultiView) return null;

            var routed = state.RoutedSubscriptions.OrderBy(s => s.Index).ToList();
            var subscriptionIndexes = routed.Select(s => s.Index).ToList();
            string layoutName = state.Layout?.Name ?? string.Empty;

            int width = state.Layout is { Width: > 0 } l ? l.Width : state.Width;
            int height = state.Layout is { Height: > 0 } lh ? lh.Height : state.Height;
            var canvas = new MultiViewCanvas(width, height, state.Fps, false, $"{width}x{height}@{state.Fps} (running)");

            if (state.Layout != null && MultiViewPresets.TryParseName(layoutName, out string id, out int pw, out int ph))
            {
                try
                {
                    var plan = MultiViewPresets.Build(id, pw, ph);
                    var draft = new MultiViewDraft(plan);
                    var sources = new List<RestoredSource>();
                    foreach (var window in state.Layout.Windows.OrderBy(w => w.WindowIndex))
                    {
                        // A window shows the subscription whose index is its surface. A surface nobody subscribes
                        // (255 = a black window read back from the decoder) leaves the block empty.
                        var sub = routed.FirstOrDefault(s => s.Index == window.Surface);
                        if (sub == null) continue;
                        // refused: not a block of the plan, or a second size for one encoder
                        if (!draft.Place(window.WindowIndex, sub.SourceMac, sub.SourceName).Ok) continue;

                        sources.Add(new RestoredSource(window.WindowIndex, sub.SourceMac, sub.SourceName));
                    }
                    return new RestoredMultiView(layoutName, plan, canvas, sources, subscriptionIndexes, draft);
                }
                catch (ArgumentException)
                {
                    // canvas no longer buildable: fall through and treat it as external
                }
            }

            var external = routed.Select(s => new RestoredSource(s.Index, s.SourceMac, s.SourceName)).ToList();
            return new RestoredMultiView(layoutName, null, canvas, external, subscriptionIndexes);
        }
    }
}
