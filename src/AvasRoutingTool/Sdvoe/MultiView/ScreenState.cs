using System.Collections.Generic;
using System.Linq;

namespace AvasRoutingTool.Sdvoe.MultiView
{
    /// <summary>What a display shows on screen right now.</summary>
    public enum ScreenKind
    {
        /// <summary>Unrouted or off.</summary>
        Nothing,

        /// <summary>One source full screen.</summary>
        Single,

        /// <summary>A layout this tool made (its <see cref="ScreenState.Draft"/> says which sources are in which blocks).</summary>
        MultiView,

        /// <summary>A layout read back from the hardware that this tool did not make.</summary>
        External,
    }

    /// <summary>
    /// A display's picture as it is on screen (never a draft being arranged): the output format, and either the
    /// Single View source, the layout this tool made, or an external layout with its subscriptions.
    /// <para>
    /// Width/Height/Fps must be the filled-in output format of the display, and Fps already rounded to a whole
    /// number (59.94 -> 60): the copy rule compares them with <c>EdidInfo.Supports</c> as they are.
    /// </para>
    /// </summary>
    public sealed record ScreenState(
        ScreenKind Kind,
        int Width,
        int Height,
        int Fps,
        string? SourceMac,
        string? SourceName,
        MultiViewDraft? Draft,
        RxLayout? External,
        IReadOnlyList<RxSubscription> Subscriptions)
    {
        /// <summary>
        /// Operator-readable summary, e.g. "Matrix 2x2 (TX2_DUAL LINK_SFPA, VEGA_1100) - 3840x2160" or
        /// "Single View (TX2_DUAL LINK_SFPA) - 3840x2160".
        /// </summary>
        public string Describe()
        {
            string size = $"{Width}x{Height}";
            return Kind switch
            {
                ScreenKind.Single => $"Single View ({SourceName}) - {size}",
                ScreenKind.MultiView => $"{PresetTitle()}{Parenthesised(DraftSources())} - {size}",
                ScreenKind.External => $"External layout{Parenthesised(ExternalSources())} - {size}",
                _ => "Nothing",
            };
        }

        private string PresetTitle()
        {
            var plan = Draft?.Plan;
            if (plan == null) return "MultiView";
            return plan.Id switch
            {
                "matrix2x2" => "Matrix 2x2",
                "pip" => "PIP",
                "side_by_side" => "Side-by-Side",
                "quad_1_3" => "1+3 Quad",
                _ => plan.Name,
            };
        }

        /// <summary>Distinct source names of the draft's blocks, in block order.</summary>
        private string DraftSources()
        {
            if (Draft == null) return string.Empty;

            var names = new List<string>();
            var seen = new HashSet<string>();
            foreach (var slot in Draft.Plan.Slots)
            {
                if (!Draft.Placements.TryGetValue(slot.Index, out var placement)) continue;
                if (seen.Add(SdvoeClient.NormalizeMac(placement.SourceMac))) names.Add(placement.SourceName);
            }
            return string.Join(", ", names);
        }

        /// <summary>
        /// Distinct sources (by MAC) the layout's windows show, in window order: a window shows the routed
        /// subscription whose index is its surface. Without a layout, every routed subscription in index order.
        /// </summary>
        private string ExternalSources()
        {
            var routed = Subscriptions.Where(s => s.IsRouted).OrderBy(s => s.Index).ToList();
            IEnumerable<RxSubscription> shown = External == null
                ? routed
                : External.Windows
                    .OrderBy(w => w.WindowIndex)
                    .Select(w => routed.FirstOrDefault(s => s.Index == w.Surface))
                    .OfType<RxSubscription>();

            var seen = new HashSet<string>();
            return string.Join(", ", shown
                .Where(s => seen.Add(SdvoeClient.NormalizeMac(s.SourceMac)))
                .Select(s => s.SourceName));
        }

        private static string Parenthesised(string names) => names.Length == 0 ? string.Empty : $" ({names})";
    }
}
