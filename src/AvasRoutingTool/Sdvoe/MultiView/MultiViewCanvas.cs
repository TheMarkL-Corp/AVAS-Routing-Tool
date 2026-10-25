using System;
using System.Linq;

namespace AvasRoutingTool.Sdvoe.MultiView
{
    /// <summary>The output format a multiview decoder is driven at, and where that choice came from.</summary>
    public sealed record MultiViewCanvas(int Width, int Height, int Fps, bool FromDisplay, string Description);

    /// <summary>
    /// Chooses the multiview output format from the display's EDID so a 4K panel is not driven at 1080p.
    /// When the EDID is missing or untrustworthy it falls back to 1920x1080@60 and says so.
    /// </summary>
    public static class MultiViewCanvasPolicy
    {
        public const int MaxWidth = 3840;
        public const int MaxHeight = 2160;
        public const int MinWidth = 640;
        public const int MinHeight = 360;

        /// <summary>Recommended ceiling per TX and per RX (SDVoE API Reference §5.1.1).</summary>
        public const double RecommendedGbps = 9.0;

        public static readonly MultiViewCanvas DefaultFallback = new(1920, 1080, 60, false, "1920x1080@60");

        public static MultiViewCanvas Choose(EdidInfo? edid, MultiViewCanvas? fallback = null)
        {
            var fb = fallback ?? DefaultFallback;

            if (edid == null)
                return fb with { FromDisplay = false, Description = $"{fb.Description} (display EDID unavailable)" };

            if (!edid.ChecksumValid)
                return fb with { FromDisplay = false, Description = $"{fb.Description} (display EDID checksum invalid)" };

            // Preferred timing first, then the rest in EDID order. layout create needs width % 8 == 0.
            var usable = edid.Timings.FirstOrDefault(t =>
                !t.Interlaced &&
                t.Width % 8 == 0 &&
                t.Width >= MinWidth && t.Height >= MinHeight &&
                t.Width <= MaxWidth && t.Height <= MaxHeight);

            if (usable == null)
                return fb with { FromDisplay = false, Description = $"{fb.Description} (no usable progressive timing in display EDID)" };

            int fps = (int)Math.Round(usable.RefreshHz);
            string name = string.IsNullOrWhiteSpace(edid.MonitorName) ? "display" : edid.MonitorName;
            return new MultiViewCanvas(usable.Width, usable.Height, fps, true, $"{usable.Width}x{usable.Height}@{fps} ({name})");
        }

        /// <summary>
        /// Rough stream-1 bandwidth if every window is live: scaler output is RGB 8-bit, so
        /// total window pixels x frame rate x 24 bit.
        /// </summary>
        public static double EstimateGbps(MultiViewLayoutPlan plan, int fps)
        {
            double pixels = plan.Slots.Sum(s => (double)s.Width * s.Height);
            return pixels * fps * 24 / 1e9;
        }

        /// <summary>
        /// True when the sources' stream 1 would exceed the recommended bandwidth at full frame rate, so the
        /// TX frame-rate divider (stream 1 at half rate) should be used.
        /// </summary>
        public static bool NeedsFrameDivider(MultiViewLayoutPlan plan, int fps) =>
            EstimateGbps(plan, fps) > RecommendedGbps;

        /// <summary>The output format alone, e.g. <c>OUT 3840x2160@60 (VP3268-4K)</c>.</summary>
        public static string OutputLabel(MultiViewCanvas canvas) => $"OUT {canvas.Description}";

        /// <summary>One line for the card: the output format plus what is done about high bandwidth.</summary>
        public static string Summarize(MultiViewCanvas canvas, MultiViewLayoutPlan plan)
        {
            double gbps = EstimateGbps(plan, canvas.Fps);
            string text = OutputLabel(canvas);
            if (gbps <= RecommendedGbps) return text;

            double halved = gbps / 2;
            return halved <= RecommendedGbps
                ? text + $" · sources at half frame rate (~{halved:0.0} Gbps instead of ~{gbps:0.0})"
                : text + $" · ~{halved:0.0} Gbps even at half frame rate (>{RecommendedGbps:0} Gbps recommended)";
        }
    }
}
