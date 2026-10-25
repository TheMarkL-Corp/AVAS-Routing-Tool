using System;
using System.Collections.Generic;
using System.Linq;

namespace AvasRoutingTool.Sdvoe.MultiView
{
    /// <summary>A source found feeding a multiview window on a receiver.</summary>
    public sealed record RestoredSource(int SlotIndex, string Mac, string Name);

    /// <summary>
    /// A multiview layout found on a receiver. <see cref="Plan"/> is set only for layouts this tool created
    /// (<c>avas_&lt;preset&gt;_&lt;W&gt;x&lt;H&gt;</c>); anything else is external and shown read-only.
    /// </summary>
    public sealed record RestoredMultiView(
        string LayoutName,
        MultiViewLayoutPlan? Plan,
        MultiViewCanvas Canvas,
        IReadOnlyList<RestoredSource> Sources,
        IReadOnlyList<int> SubscriptionIndexes)
    {
        public bool IsRecognised => Plan != null;
    }

    /// <summary>Turns a receiver's hardware state into what the cards need to show (pure).</summary>
    public static class MultiViewRestore
    {
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
                    var sources = new List<RestoredSource>();
                    foreach (var sub in routed)
                    {
                        // subscription index == the surface feeding a window
                        var window = state.Layout.Windows.FirstOrDefault(w => w.Surface == sub.Index);
                        if (window != null && plan.Slots.Any(s => s.Index == window.WindowIndex))
                            sources.Add(new RestoredSource(window.WindowIndex, sub.SourceMac, sub.SourceName));
                    }
                    return new RestoredMultiView(layoutName, plan, canvas, sources, subscriptionIndexes);
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
