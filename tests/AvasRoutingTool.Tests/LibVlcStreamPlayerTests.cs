using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AvasRoutingTool.Rtsp;
using Xunit;

namespace AvasRoutingTool.Tests
{
    [Collection("WpfSta")]
    public class LibVlcStreamPlayerTests
    {
        private static bool RealEnabled => Environment.GetEnvironmentVariable("AVAS_RTSP_REAL") == "1";

        private static void RunSta(Action body)
        {
            Exception? failure = null;
            var t = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { failure = ex; }
                finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("STA failure: " + failure);
        }

        /// <summary>Pumps the current thread's dispatcher until the condition holds or the timeout passes.</summary>
        private static bool PumpUntil(Func<bool> cond, double seconds)
        {
            var end = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < end)
            {
                if (cond()) return true;
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Thread.Sleep(10);
            }
            return cond();
        }

        // ---------- frame sink (always runs) ----------

        private static IntPtr Frame(int w, int h, byte fill)
        {
            var p = Marshal.AllocHGlobal(w * h * 4);
            var bytes = new byte[w * h * 4];
            Array.Fill(bytes, fill);
            Marshal.Copy(bytes, 0, p, bytes.Length);
            return p;
        }

        [Fact]
        public void FrameSink_FirstFrame_CreatesBitmapOfThatSize()
        {
            RunSta(() =>
            {
                var sink = new RtspFrameSink();
                var buf = Frame(64, 48, 0x80);
                try
                {
                    Assert.Null(sink.Bitmap);
                    Assert.True(sink.Present(64, 48, buf, 64 * 4));
                    Assert.NotNull(sink.Bitmap);
                    Assert.Equal(64, sink.Bitmap!.PixelWidth);
                    Assert.Equal(48, sink.Bitmap.PixelHeight);
                    var px = new byte[4];
                    sink.Bitmap.CopyPixels(new System.Windows.Int32Rect(10, 10, 1, 1), px, 4, 0);
                    Assert.Equal(0x80, px[0]);
                }
                finally { Marshal.FreeHGlobal(buf); }
            });
        }

        [Fact]
        public void FrameSink_SizeChange_RecreatesBitmap_AndDoesNotThrow()
        {
            RunSta(() =>
            {
                var sink = new RtspFrameSink();
                var a = Frame(64, 48, 1);
                var b = Frame(128, 96, 2);
                try
                {
                    sink.Present(64, 48, a, 64 * 4);
                    var first = sink.Bitmap;
                    var ex = Record.Exception(() => sink.Present(128, 96, b, 128 * 4));
                    Assert.Null(ex);
                    Assert.NotSame(first, sink.Bitmap);
                    Assert.Equal(128, sink.Bitmap!.PixelWidth);
                    Assert.Equal(96, sink.Bitmap.PixelHeight);
                    var second = sink.Bitmap;
                    sink.Present(128, 96, b, 128 * 4);
                    Assert.Same(second, sink.Bitmap);
                }
                finally { Marshal.FreeHGlobal(a); Marshal.FreeHGlobal(b); }
            });
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 1)]
        [InlineData(4, 4)]
        [InlineData(640, 0)]
        [InlineData(-5, 480)]
        public void FrameSink_TinyAndZeroSizedFrames_AreIgnored(int w, int h)
        {
            RunSta(() =>
            {
                var sink = new RtspFrameSink();
                var buf = Frame(16, 16, 9);
                var big = Frame(64, 48, 3);
                try
                {
                    Assert.False(sink.Present(w, h, buf, Math.Max(w, 1) * 4));
                    Assert.Null(sink.Bitmap);
                    sink.Present(64, 48, big, 64 * 4);
                    var good = sink.Bitmap;
                    Assert.False(sink.Present(w, h, buf, Math.Max(w, 1) * 4));
                    Assert.Same(good, sink.Bitmap);
                }
                finally { Marshal.FreeHGlobal(buf); Marshal.FreeHGlobal(big); }
            });
        }

        // ---------- real decoder (gated by AVAS_RTSP_REAL=1) ----------

        private sealed class Probe
        {
            public readonly ConcurrentQueue<RtspPlayerEvent> Events = new();
            public void On(RtspPlayerEvent e) => Events.Enqueue(e);
            public bool Has(RtspPlayerEventKind k) => Events.Any(e => e.Kind == k);
            public int Count(RtspPlayerEventKind k) => Events.Count(e => e.Kind == k);
            public RtspPlayerEvent? FirstFailed => Events.FirstOrDefault(e => e.Kind == RtspPlayerEventKind.Failed);
        }

        private static string Url(FakeRtspServer s, bool creds = false)
            => creds ? $"rtsp://{FakeRtspServer.FakeUser}:{FakeRtspServer.FakePassword}@127.0.0.1:{s.Port}/stream"
                     : $"rtsp://127.0.0.1:{s.Port}/stream";

        private static byte[] Snapshot(WriteableBitmap b)
        {
            var px = new byte[b.PixelWidth * b.PixelHeight * 4];
            b.CopyPixels(px, b.PixelWidth * 4, 0);
            return px;
        }

        [Fact]
        public void Plays_FakeServer_RaisesPlaying_AndBitmapChanges()
        {
            if (!RealEnabled) return;
            RunSta(() =>
            {
                using var server = new FakeRtspServer(FakeRtspMode.Normal);
                using var player = new LibVlcStreamPlayer(Dispatcher.CurrentDispatcher);
                var probe = new Probe();
                player.Event += probe.On;
                player.Start(Url(server, creds: true));
                Assert.True(PumpUntil(() => probe.Has(RtspPlayerEventKind.Playing), 15), "no Playing event");
                Assert.NotNull(player.Bitmap);
                var first = Snapshot(player.Bitmap!);
                Assert.True(PumpUntil(() => player.Bitmap != null && !Snapshot(player.Bitmap).SequenceEqual(first), 10), "bitmap never changed");
                player.Stop();
                player.Stop();   // idempotent
            });
        }

        [Fact]
        public void ServerDrop_RaisesDropped()
        {
            if (!RealEnabled) return;
            RunSta(() =>
            {
                using var server = new FakeRtspServer(FakeRtspMode.Normal);
                using var player = new LibVlcStreamPlayer(Dispatcher.CurrentDispatcher);
                var probe = new Probe();
                player.Event += probe.On;
                player.Start(Url(server));
                Assert.True(PumpUntil(() => probe.Has(RtspPlayerEventKind.Playing), 15));
                server.Drop();
                Assert.True(PumpUntil(() => probe.Has(RtspPlayerEventKind.Dropped), 20), "no Dropped event");
                Assert.False(probe.Has(RtspPlayerEventKind.Failed));
            });
        }

        [Fact]
        public void Restart_AfterDrop_PlaysAgain()
        {
            if (!RealEnabled) return;
            RunSta(() =>
            {
                using var server = new FakeRtspServer(FakeRtspMode.Normal);
                using var player = new LibVlcStreamPlayer(Dispatcher.CurrentDispatcher);
                var probe = new Probe();
                player.Event += probe.On;
                player.Start(Url(server));
                Assert.True(PumpUntil(() => probe.Count(RtspPlayerEventKind.Playing) >= 1, 15));
                server.Drop();
                Assert.True(PumpUntil(() => probe.Has(RtspPlayerEventKind.Dropped), 20));
                player.Start(Url(server));
                Assert.True(PumpUntil(() => probe.Count(RtspPlayerEventKind.Playing) >= 2, 15), "did not play again");
            });
        }

        private static RtspPlayerEvent? FailedFor(FakeRtspMode mode)
        {
            RtspPlayerEvent? result = null;
            RunSta(() =>
            {
                using var server = new FakeRtspServer(mode);
                using var player = new LibVlcStreamPlayer(Dispatcher.CurrentDispatcher);
                var probe = new Probe();
                player.Event += probe.On;
                player.Start(Url(server));
                PumpUntil(() => probe.FirstFailed != null, 20);
                result = probe.FirstFailed;
                Assert.False(probe.Has(RtspPlayerEventKind.Playing));
            });
            return result;
        }

        [Fact]
        public void Auth401_RaisesFailedAuth()
        {
            if (!RealEnabled) return;
            var e = FailedFor(FakeRtspMode.Require401);
            Assert.NotNull(e);
            Assert.Equal(RtspFailure.AuthFailed, e!.Failure);
        }

        [Fact]
        public void NotFound404_RaisesFailedNotFound()
        {
            if (!RealEnabled) return;
            var e = FailedFor(FakeRtspMode.NotFound404);
            Assert.NotNull(e);
            Assert.Equal(RtspFailure.NotFound, e!.Failure);
        }

        [Fact]
        public void Refused_RaisesFailedUnreachable()
        {
            if (!RealEnabled) return;
            var e = FailedFor(FakeRtspMode.RefuseConnection);
            Assert.NotNull(e);
            Assert.Equal(RtspFailure.Unreachable, e!.Failure);
        }

        [Fact]
        public void DecoderLogs_NeverContainFakePassword()
        {
            if (!RealEnabled) return;
            var lines = new ConcurrentQueue<string>();
            var events = new ConcurrentQueue<RtspPlayerEvent>();
            RunSta(() =>
            {
                foreach (var mode in new[] { FakeRtspMode.Normal, FakeRtspMode.Require401, FakeRtspMode.NotFound404 })
                {
                    using var server = new FakeRtspServer(mode);
                    using var player = new LibVlcStreamPlayer(Dispatcher.CurrentDispatcher);
                    player.MaskedLogSink = l => lines.Enqueue(l);
                    var probe = new Probe();
                    player.Event += probe.On;
                    player.Event += e => events.Enqueue(e);
                    player.Start(Url(server, creds: true));
                    PumpUntil(() => probe.Has(RtspPlayerEventKind.Playing) || probe.FirstFailed != null, 20);
                    PumpUntil(() => false, 1);
                }
            });
            Assert.True(!lines.Any(l => l.Contains(FakeRtspServer.FakePassword, StringComparison.OrdinalIgnoreCase)), "leaked: " + string.Join(" || ", lines.Where(l => l.Contains(FakeRtspServer.FakePassword, StringComparison.OrdinalIgnoreCase)).Take(3)));
            Assert.DoesNotContain(events, e => (e.Detail ?? "").Contains(FakeRtspServer.FakePassword, StringComparison.OrdinalIgnoreCase));
        }
    }
}

