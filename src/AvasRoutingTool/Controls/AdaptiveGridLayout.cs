using System;

namespace AvasRoutingTool.Controls
{
    /// <summary>Sizing rules for a grid of video cards.</summary>
    public sealed record GridSpec(
        double MinCardWidth,
        double MaxCardWidth,
        double Gap,
        double HorizontalPadding,
        double ChromeHeight,
        double VideoAspect)
    {
        /// <summary>
        /// Tuned for the surgical-touch card: 20px of horizontal padding, ~120px of non-video chrome
        /// (header, 44px action row, paddings) and a 16:9 video surface.
        /// </summary>
        public static GridSpec Default { get; } = new(
            MinCardWidth: 320,
            MaxCardWidth: 600,
            Gap: 16,
            HorizontalPadding: 20,
            ChromeHeight: 120,
            VideoAspect: 16.0 / 9.0);

        /// <summary>
        /// The display card: its header carries the 56 px Layout and Copy layout buttons, so its chrome is taller;
        /// with it the picture keeps 16:9 in every state (a smoke test measures the real card: Single View, MultiView,
        /// a draft chip on the picture).
        /// </summary>
        public static GridSpec DisplayCard { get; } = Default with { ChromeHeight = 175, HorizontalPadding = 26 };
    }

    /// <summary>The chosen arrangement for N cards in a viewport.</summary>
    public readonly record struct GridPlan(int Columns, int Rows, double CardWidth, double CardHeight, bool NeedsScroll);

    /// <summary>
    /// Picks the column count and card size that makes the cards as large as the viewport allows
    /// while keeping a 16:9 video surface and never exceeding the viewport width.
    /// Pure and WPF-free so the whole policy is testable at its interface.
    /// </summary>
    public static class AdaptiveGridLayout
    {
        private const double Epsilon = 0.001;

        public static GridPlan Compute(int count, double availableWidth, double availableHeight, GridSpec spec)
        {
            if (count <= 0 || availableWidth <= 0)
            {
                return new GridPlan(0, 0, 0, 0, false);
            }

            GridPlan? best = null;

            for (int cols = 1; cols <= count; cols++)
            {
                int rows = (int)Math.Ceiling(count / (double)cols);

                double byWidth = (availableWidth - spec.Gap * (cols - 1)) / cols;
                double width = Math.Min(byWidth, spec.MaxCardWidth);

                if (!double.IsInfinity(availableHeight))
                {
                    double slotHeight = (availableHeight - spec.Gap * (rows - 1)) / rows;
                    double videoHeight = slotHeight - spec.ChromeHeight;
                    double byHeight = videoHeight * spec.VideoAspect + spec.HorizontalPadding;
                    width = Math.Min(width, byHeight);
                }

                if (width + Epsilon < spec.MinCardWidth)
                {
                    continue; // this arrangement would make cards too small to read
                }

                var candidate = new GridPlan(cols, rows, width, CardHeightFor(width, spec), false);

                if (best is null ||
                    candidate.CardWidth > best.Value.CardWidth + Epsilon ||
                    (Math.Abs(candidate.CardWidth - best.Value.CardWidth) <= Epsilon && candidate.Rows < best.Value.Rows))
                {
                    best = candidate;
                }
            }

            if (best is not null)
            {
                return best.Value;
            }

            // Nothing fits at a readable size: keep cards readable and let the viewport scroll vertically.
            int fitCols = Math.Max(1, (int)Math.Floor((availableWidth + spec.Gap) / (spec.MinCardWidth + spec.Gap)));
            fitCols = Math.Min(fitCols, count);
            int fitRows = (int)Math.Ceiling(count / (double)fitCols);
            double fallbackWidth = Math.Min(spec.MinCardWidth, availableWidth);
            double fallbackHeight = CardHeightFor(fallbackWidth, spec);
            double totalHeight = fitRows * fallbackHeight + spec.Gap * (fitRows - 1);
            bool needsScroll = double.IsInfinity(availableHeight) ? false : totalHeight > availableHeight + Epsilon;

            return new GridPlan(fitCols, fitRows, fallbackWidth, fallbackHeight, needsScroll);
        }

        public static double CardHeightFor(double cardWidth, GridSpec spec)
        {
            double videoWidth = Math.Max(0, cardWidth - spec.HorizontalPadding);
            return videoWidth / spec.VideoAspect + spec.ChromeHeight;
        }
    }
}
