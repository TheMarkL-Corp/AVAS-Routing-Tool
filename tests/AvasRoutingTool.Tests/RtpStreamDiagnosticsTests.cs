using AvasRoutingTool.Rtp;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class RtpStreamDiagnosticsTests
    {
        // Feeds `frames` frames of `pktsPerFrame` packets, one frame every `frameMs`, timestamps `tsStep` apart.
        private static string? Feed(RtpStreamDiagnostics d, int frames, int pktsPerFrame, uint tsStep, int frameMs, int skipEvery = 0)
        {
            string? last = null;
            ushort seq = 0;
            uint ts = 1000;
            long now = 0;
            int n = 0;
            for (int f = 0; f < frames; f++)
            {
                for (int p = 0; p < pktsPerFrame; p++)
                {
                    n++;
                    seq++;
                    if (skipEvery > 0 && n % skipEvery == 0) seq++; // simulate one lost packet
                    string? s = d.Observe(seq, ts, p == pktsPerFrame - 1, p, 1440, 1460, now + p, f);
                    if (s != null) last = s;
                }
                ts += tsStep;
                now += frameMs;
            }
            return last;
        }

        [Fact]
        public void SenderAt2Fps_ReportsAbout2Fps()
        {
            var d = new RtpStreamDiagnostics();
            string? line = Feed(d, frames: 5, pktsPerFrame: 20, tsStep: 45000, frameMs: 500);
            Assert.NotNull(line);
            Assert.Contains("sender≈2.0fps", line);
            Assert.Contains("lost 0", line);
        }

        [Fact]
        public void SenderAt30Fps_ReportsAbout30Fps()
        {
            var d = new RtpStreamDiagnostics();
            string? line = Feed(d, frames: 40, pktsPerFrame: 20, tsStep: 3000, frameMs: 33);
            Assert.NotNull(line);
            Assert.Contains("sender≈30.0fps", line);
        }

        [Fact]
        public void SequenceGaps_AreCountedAsLoss()
        {
            var d = new RtpStreamDiagnostics();
            string? line = Feed(d, frames: 5, pktsPerFrame: 20, tsStep: 45000, frameMs: 500, skipEvery: 10);
            Assert.NotNull(line);
            Assert.DoesNotContain("lost 0", line);
        }

        [Fact]
        public void BeforeTheWindowElapses_NothingIsLogged()
        {
            var d = new RtpStreamDiagnostics(windowMs: 1000);
            Assert.Null(d.Observe(1, 1000, false, 0, 1440, 1460, 0, 0));
            Assert.Null(d.Observe(2, 1000, false, 1, 1440, 1460, 500, 0));
        }
    }
}
