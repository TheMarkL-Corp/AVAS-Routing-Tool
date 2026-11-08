using System;
using System.Collections.Generic;
using System.Linq;

namespace AvasRoutingTool.Sdvoe.MultiView
{
    /// <summary>A source's incoming video as the transmitter sees it (active pixels, no blanking).</summary>
    public sealed record SourceVideo(int Width, int Height, double Fps, int BitsPerPixel = 24);

    /// <summary>One transmitter in a layout: what it receives and the scaler size its tile(s) need.</summary>
    public sealed record SourcePlan(SourceVideo Video, int ScalerWidth, int ScalerHeight);

    /// <summary>What one transmitter would put on its 10G link: stream 0 (native) plus stream 1 (scaled).</summary>
    /// <param name="Stream0Compressed">Stream 0 exceeds the limit uncompressed, so the TX compresses it; a compressed stream 0 cannot be a multiview tile.</param>
    public sealed record TxLoad(double Stream0Gbps, bool Stream0Compressed, double Stream1Gbps)
    {
        public double TotalGbps => Stream0Gbps + Stream1Gbps;
    }

    /// <summary>
    /// Pure bandwidth arithmetic for multiview admission (see docs/studies/2026-11-07-multiview-bandwidth-study.md).
    /// Used only for a warning log on Save &amp; Apply (MultiViewOrchestrator.WarnIfOverBudgetAsync); it never blocks. This class itself talks to no device.
    /// wire rate = active pixels x fps x bits per pixel / 0.93; light compression is about 1.5:1.
    /// Calibrated against vendor figures, field notes and live netstat (within about 2 %, slightly pessimistic).
    /// </summary>
    public static class MultiViewBandwidthBudget
    {
        public const double Efficiency = 0.93;
        public const double CompressionRatio = 1.5;

        /// <summary>Per TX and per RX ceiling (SDVoE API Reference §5.1.1).</summary>
        public const double LimitGbps = MultiViewCanvasPolicy.RecommendedGbps;

        public static double WireGbps(int width, int height, double fps, int bitsPerPixel = 24) =>
            (double)width * height * fps * bitsPerPixel / Efficiency / 1e9;

        /// <summary>
        /// Stream 0 as the transmitter sends it. The frame-rate divider halves it; if it still exceeds the limit
        /// the TX compresses it automatically.
        /// </summary>
        public static (double Gbps, bool Compressed) Stream0(SourceVideo source, bool halved)
        {
            double fps = halved ? source.Fps / 2 : source.Fps;
            double raw = WireGbps(source.Width, source.Height, fps, source.BitsPerPixel);
            return raw > LimitGbps ? (raw / CompressionRatio, true) : (raw, false);
        }

        /// <summary>A scaled stream is always uncompressed, progressive, 24 bits per pixel; the divider halves its rate.</summary>
        public static double Stream1(int scalerWidth, int scalerHeight, double sourceFps, bool halved) =>
            WireGbps(scalerWidth, scalerHeight, halved ? sourceFps / 2 : sourceFps);

        /// <summary>Egress of one transmitter; <paramref name="scaler"/> is null when it sends no scaled stream.</summary>
        public static TxLoad TxEgress(SourceVideo source, bool stream0Halved, (int Width, int Height)? scaler, bool stream1Halved)
        {
            var (s0, compressed) = Stream0(source, stream0Halved);
            double s1 = scaler is { } z ? Stream1(z.Width, z.Height, source.Fps, stream1Halved) : 0;
            return new TxLoad(s0, compressed, s1);
        }

        /// <summary>Ingress of a multiview receiver: every distinct subscribed stream arrives once, whatever its window count.</summary>
        public static double RxIngress(IEnumerable<double> distinctStreamGbps) => distinctStreamGbps.Sum();

        /// <summary>
        /// True when the layout, sent at full frame rate, would put more than the limit on a transmitter (stream 0 plus
        /// scaled stream 1) or on the receiver (every distinct scaled stream). Uses each source's own frame rate, not the
        /// canvas rate. Stream 0 is assumed undivided, as the app only divides stream 1.
        /// </summary>
        public static bool NeedsFrameDivider(IReadOnlyList<SourcePlan> sources)
        {
            double rx = 0;
            foreach (var s in sources)
            {
                var load = TxEgress(s.Video, false, (s.ScalerWidth, s.ScalerHeight), false);
                if (IsOverLimit(load.TotalGbps)) return true;
                rx += load.Stream1Gbps;
            }
            return IsOverLimit(rx);
        }

        public static bool IsOverLimit(double gbps) => gbps > LimitGbps;

        /// <summary>One line an operator can act on, or null when the load is within the limit.</summary>
        public static string? Describe(string deviceName, string direction, double gbps) =>
            IsOverLimit(gbps)
                ? $"{deviceName}: {direction} ~{gbps:0.0} Gbps is over the {LimitGbps:0} Gbps limit; pictures may break up."
                : null;
    }
}
