using System;
using System.Collections.Generic;
using System.Linq;

namespace AvasRoutingTool.Sdvoe.MultiView
{
    /// <summary>One window as it will be created. A null <see cref="Surface"/> means no source: the window is black.</summary>
    public sealed record PlannedWindow(WindowSlot Slot, int? Surface);

    /// <summary>One encoder subscription: its scaler size and the surface every block showing it reads.</summary>
    public sealed record PlannedSource(string Mac, int Surface, int Width, int Height);

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
    }
}
