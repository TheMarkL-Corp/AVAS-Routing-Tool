using System;
using System.Collections.Generic;
using System.Linq;

namespace AvasRoutingTool.Sdvoe.MultiView
{
    /// <summary>
    /// Builds the four Phase 2 layouts for a given decoder output canvas.
    /// Every window is 16:9 so the TX scaler (window size = scaler size) never distorts the picture;
    /// leftover canvas becomes black margin. Limits come from the SDVoE API Reference 3.9 §5.1.1 / §5.3.
    /// </summary>
    public static class MultiViewPresets
    {
        private const int MaxWindows = 32;
        private const int MinWindowSize = 8;
        private static readonly int[] ForbiddenWidths = { 1366, 1368, 2732, 2734, 4098, 4100 };

        public static readonly IReadOnlyList<string> Ids = new[] { "matrix2x2", "pip", "side_by_side", "quad_1_3" };

        public static bool IsMultiView(string? id) =>
            id != null && Ids.Contains(id, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Recognises a layout name this tool created (<c>avas_&lt;preset&gt;_&lt;W&gt;x&lt;H&gt;</c>), so a layout found
        /// on a decoder can be adopted. Layouts made elsewhere (BAVM, <c>compatibility_4k_2x2</c>) are not ours.
        /// </summary>
        public static bool TryParseName(string? name, out string id, out int width, out int height)
        {
            id = string.Empty;
            width = height = 0;
            if (string.IsNullOrEmpty(name)) return false;

            foreach (var candidate in Ids)
            {
                string prefix = $"avas_{candidate}_";
                if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;

                var parts = name.Substring(prefix.Length).Split('x');
                if (parts.Length == 2 && int.TryParse(parts[0], out width) && int.TryParse(parts[1], out height) && width > 0 && height > 0)
                {
                    id = candidate;
                    return true;
                }
            }

            width = height = 0;
            return false;
        }

        public static MultiViewLayoutPlan Build(string id, int canvasWidth, int canvasHeight)
        {
            if (!IsMultiView(id))
                throw new ArgumentException($"'{id}' is not a multiview layout.", nameof(id));

            string key = Ids.First(i => i.Equals(id, StringComparison.OrdinalIgnoreCase));
            var rects = key switch
            {
                "matrix2x2" => Matrix(canvasWidth, canvasHeight),
                "side_by_side" => SideBySide(canvasWidth, canvasHeight),
                "pip" => Pip(canvasWidth, canvasHeight),
                _ => Quad(canvasWidth, canvasHeight),
            };

            Validate(key, rects, canvasWidth, canvasHeight);

            var surfaces = AllocateSurfaces(rects);
            var slots = rects.Select((r, i) => new WindowSlot(
                r.Index, r.Label, r.X, r.Y, r.W, r.H, surfaces[i],
                (double)r.X / canvasWidth, (double)r.Y / canvasHeight,
                (double)r.W / canvasWidth, (double)r.H / canvasHeight)).ToList();

            return new MultiViewLayoutPlan(key, $"avas_{key}_{canvasWidth}x{canvasHeight}", canvasWidth, canvasHeight, slots);
        }

        private readonly record struct Rect(int Index, string Label, int X, int Y, int W, int H);

        private static int Even(double v) => ((int)v) & ~1;

        /// <summary>Largest 16:9 cell that fits a grid of <paramref name="cols"/> x <paramref name="rows"/> cells.</summary>
        private static (int cw, int ch, int ox, int oy) Fit(int canvasW, int canvasH, int cols, int rows)
        {
            if (canvasW <= 0 || canvasH <= 0)
                throw new ArgumentOutOfRangeException(nameof(canvasW), "Canvas must have a positive size.");
            int cw = Even(Math.Min((double)canvasW / cols, (double)canvasH / rows * 16.0 / 9.0));
            int ch = Even(cw * 9.0 / 16.0);
            int ox = Even((canvasW - cols * cw) / 2.0);
            int oy = Even((canvasH - rows * ch) / 2.0);
            return (cw, ch, ox, oy);
        }

        private static Rect[] Matrix(int w, int h)
        {
            var (cw, ch, ox, oy) = Fit(w, h, 2, 2);
            return new[]
            {
                new Rect(0, "1", ox, oy, cw, ch),
                new Rect(1, "2", ox + cw, oy, cw, ch),
                new Rect(2, "3", ox, oy + ch, cw, ch),
                new Rect(3, "4", ox + cw, oy + ch, cw, ch),
            };
        }

        private static Rect[] SideBySide(int w, int h)
        {
            var (cw, ch, ox, oy) = Fit(w, h, 2, 1);
            return new[]
            {
                new Rect(0, "Left", ox, oy, cw, ch),
                new Rect(1, "Right", ox + cw, oy, cw, ch),
            };
        }

        private static Rect[] Pip(int w, int h)
        {
            var (cw, ch, ox, oy) = Fit(w, h, 1, 1);
            int iw = Even(cw / 4.0);
            int ih = Even(iw * 9.0 / 16.0);
            int margin = Even(cw / 48.0);
            // Reference PiP: background is window 1, inset is window 0 (lower index paints on top).
            return new[]
            {
                new Rect(0, "Inset", ox + cw - iw - margin, oy + ch - ih - margin, iw, ih),
                new Rect(1, "Main", ox, oy, cw, ch),
            };
        }

        private static Rect[] Quad(int w, int h)
        {
            var (cw, ch, ox, oy) = Fit(w, h, 3, 3);
            return new[]
            {
                new Rect(0, "Focus", ox, oy + Even(ch / 2.0), 2 * cw, 2 * ch),
                new Rect(1, "2", ox + 2 * cw, oy, cw, ch),
                new Rect(2, "3", ox + 2 * cw, oy + ch, cw, ch),
                new Rect(3, "4", ox + 2 * cw, oy + 2 * ch, cw, ch),
            };
        }

        private static void Validate(string id, Rect[] rects, int canvasW, int canvasH)
        {
            if (rects.Length > MaxWindows)
                throw new ArgumentOutOfRangeException(nameof(id), $"{id} needs more than {MaxWindows} windows.");

            foreach (var r in rects)
            {
                if (r.W < MinWindowSize || r.H < MinWindowSize)
                    throw new ArgumentOutOfRangeException(nameof(canvasW), $"{canvasW}x{canvasH} is too small for '{id}' (window {r.Index} would be {r.W}x{r.H}).");
                if (ForbiddenWidths.Contains(r.W))
                    throw new ArgumentOutOfRangeException(nameof(canvasW), $"{canvasW}x{canvasH} gives '{id}' a window width of {r.W}, which the multiview API forbids.");
                if (r.X < 0 || r.Y < 0 || r.X + r.W > canvasW || r.Y + r.H > canvasH)
                    throw new ArgumentOutOfRangeException(nameof(canvasW), $"'{id}' window {r.Index} does not fit {canvasW}x{canvasH}.");
            }
        }

        /// <summary>
        /// Default surfaces (API §5.3): 0 takes up to 4096x2160, 1-11 take up to 2048x1080.
        /// Surfaces 17-20 alias surface 0, so they are not used.
        /// </summary>
        private static int[] AllocateSurfaces(Rect[] rects)
        {
            var result = new int[rects.Length];
            bool surfaceZeroTaken = false;
            int next = 1;
            for (int i = 0; i < rects.Length; i++)
            {
                bool oversized = rects[i].W > 2048 || rects[i].H > 1080;
                if (oversized)
                {
                    if (surfaceZeroTaken || rects[i].W > 4096 || rects[i].H > 2160)
                        throw new ArgumentOutOfRangeException(nameof(rects), "Layout needs more large surfaces than the decoder frame buffer offers.");
                    result[i] = 0;
                    surfaceZeroTaken = true;
                }
                else
                {
                    if (next > 11)
                        throw new ArgumentOutOfRangeException(nameof(rects), "Layout needs more standard surfaces than the decoder frame buffer offers.");
                    result[i] = next++;
                }
            }
            return result;
        }
    }
}
