using System;
using System.Collections.Generic;
using System.Linq;

namespace AvasRoutingTool.Sdvoe.MultiView
{
    /// <summary>
    /// One window as it will be created. A null <see cref="Surface"/> means no source: the window is black.
    /// The offsets crop into the surface: 0 for the presets, an external layout's own when it is copied.
    /// </summary>
    public sealed record PlannedWindow(WindowSlot Slot, int? Surface, int HOffset = 0, int VOffset = 0);

    /// <summary>
    /// One encoder subscription: its scaler size and the surface every block showing it reads. <see cref="Stream"/>
    /// is the encoder stream subscribed (1, the scaled stream, unless a copied external layout used another).
    /// </summary>
    public sealed record PlannedSource(string Mac, int Surface, int Width, int Height, int Stream = 1);

    /// <summary>What a Take sends for one display: the layout name, every window, and one subscription per distinct encoder.</summary>
    public sealed record SurfacePlan(string LayoutName, IReadOnlyList<PlannedWindow> Windows, IReadOnlyList<PlannedSource> Sources);

    /// <summary>
    /// Turns a staged draft into surfaces. Same-size windows may read one surface, so an encoder showing in
    /// several blocks costs one surface and one subscription. Surface 0 takes up to 4096x2160 and 1-11 take up
    /// to 2048x1080 (API §5.3); 17-20 alias 0 and are never used.
    /// </summary>
    public static class SurfacePlanner
    {
        private const int StandardSurfaces = 11;

        public static SurfacePlan Plan(MultiViewDraft draft, string rxMac)
        {
            var plan = draft.Plan;
            var surfaceOf = new Dictionary<string, int>();
            var sources = new List<PlannedSource>();
            var windows = new List<PlannedWindow>();
            string? oversizedMac = null;
            int next = 1;

            foreach (var slot in plan.Slots)
            {
                if (!draft.Placements.TryGetValue(slot.Index, out var placement))
                {
                    windows.Add(new PlannedWindow(slot, null));
                    continue;
                }

                string mac = SdvoeClient.NormalizeMac(placement.SourceMac);
                if (!surfaceOf.TryGetValue(mac, out int surface))
                {
                    if (slot.Width > 2048 || slot.Height > 1080)
                    {
                        if (oversizedMac != null)
                            throw new InvalidOperationException(
                                $"This layout needs two oversized (above 2048x1080) encoders; the decoder has one large surface. ({oversizedMac}, {mac})");
                        oversizedMac = mac;
                        surface = 0;
                    }
                    else
                    {
                        if (next > StandardSurfaces)
                            throw new InvalidOperationException(
                                $"This layout needs more than {StandardSurfaces} standard surfaces; the decoder has {StandardSurfaces}.");
                        surface = next++;
                    }

                    surfaceOf[mac] = surface;
                    sources.Add(new PlannedSource(mac, surface, slot.Width, slot.Height));
                }

                windows.Add(new PlannedWindow(slot, surface));
            }

            return new SurfacePlan(MultiViewPresets.LayoutName(plan.Id, plan.Width, plan.Height, rxMac), windows, sources);
        }

        /// <summary>
        /// The surfaces of a layout read back from a decoder, to load it unchanged on another display (copy of an
        /// external layout): every window keeps its index, geometry, offsets and surface; a window whose surface
        /// has no routed subscription is created black. One source per routed subscription a window shows, with
        /// that subscription's index (= surface) and stream; routed subscriptions no window shows are not copied.
        /// Named <see cref="MultiViewPresets.CopyLayoutName"/> for <paramref name="rxMac"/>.
        /// </summary>
        public static SurfacePlan Plan(RxLayout layout, IReadOnlyList<RxSubscription> subscriptions, int width, int height, string rxMac)
        {
            ArgumentNullException.ThrowIfNull(layout);
            ArgumentNullException.ThrowIfNull(subscriptions);

            var routed = subscriptions.Where(s => s.IsRouted).GroupBy(s => s.Index).ToDictionary(g => g.Key, g => g.First());
            var windows = new List<PlannedWindow>();
            var sources = new List<PlannedSource>();
            var ordered = layout.Windows.OrderBy(w => w.WindowIndex).ToList();

            for (int i = 0; i < ordered.Count; i++)
            {
                var w = ordered[i];
                var slot = new WindowSlot(w.WindowIndex, (i + 1).ToString(), w.X, w.Y, w.Width, w.Height, w.Surface,
                    width > 0 ? (double)w.X / width : 0, height > 0 ? (double)w.Y / height : 0,
                    width > 0 ? (double)w.Width / width : 0, height > 0 ? (double)w.Height / height : 0);

                if (!routed.TryGetValue(w.Surface, out var sub))
                {
                    windows.Add(new PlannedWindow(slot, null, w.HOffset, w.VOffset));
                    continue;
                }

                windows.Add(new PlannedWindow(slot, w.Surface, w.HOffset, w.VOffset));
                if (sources.All(s => s.Surface != w.Surface))
                    sources.Add(new PlannedSource(SdvoeClient.NormalizeMac(sub.SourceMac), w.Surface, w.Width, w.Height, sub.SourceStream));
            }

            return new SurfacePlan(MultiViewPresets.CopyLayoutName(width, height, rxMac), windows, sources);
        }
    }
}
