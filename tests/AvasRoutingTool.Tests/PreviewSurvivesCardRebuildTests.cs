using System;
using System.Reflection;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AvasRoutingTool.Rtp;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.ViewModels;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// Refresh / Settings save rebuild every SourceCardViewModel while the RTP channel (keyed by IP) lives on.
    /// A rebuilt card must still get the channel's live bitmap, and a replaced card must stop following the channel.
    /// (No WPF Application here: the channel only renders through Application.Current's dispatcher, which another test may have
    /// left dead, so the channel's bitmap is set directly and the frame-rate event, which is synchronous, is used for the rest.)
    /// </summary>
    public class PreviewSurvivesCardRebuildTests
    {
        private static void RunSta(Action body)
        {
            Exception? failure = null;
            var t = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { failure = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("STA failure: " + failure);
        }

        private static AvasDevice NewDevice(string mac, string ip) => new AvasDevice
        {
            DeviceId = mac, DeviceName = "TX", IpAddress = ip, VendorId = 105, IsTransmitter = true, IsActive = true
        };

        /// <summary>What the channel holds once its first frame has been rendered.</summary>
        private static WriteableBitmap GiveChannelABitmap(IRtpStreamReceiver channel)
        {
            var bmp = new WriteableBitmap(8, 2, 96, 96, PixelFormats.Bgr24, null);
            channel.GetType().GetField("_bitmap", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(channel, bmp);
            return bmp;
        }

        private static byte[] Packet(ushort seq, uint ts, ushort line, bool marker)
        {
            const int lineBytes = 16; // 8 px, 4:2:2
            var p = new byte[20 + lineBytes];
            p[0] = 0x80;
            p[1] = (byte)((marker ? 0x80 : 0) | 96);
            p[2] = (byte)(seq >> 8); p[3] = (byte)seq;
            p[4] = (byte)(ts >> 24); p[5] = (byte)(ts >> 16); p[6] = (byte)(ts >> 8); p[7] = (byte)ts;
            p[11] = 1;
            p[14] = 0; p[15] = lineBytes;
            p[16] = (byte)(line >> 8); p[17] = (byte)line;
            return p;
        }

        private static void FeedFrame(IRtpStreamReceiver ch, ref ushort seq, uint ts)
        {
            ch.ProcessDatagram(Packet(seq++, ts, 0, false));
            ch.ProcessDatagram(Packet(seq++, ts, 1, true));
        }

        [Fact]
        public void RebuiltSourceCard_GetsTheBitmapTheChannelAlreadyHas() => RunSta(() =>
        {
            const string ip = "10.99.99.12";
            var rtp = UnifiedRtpDemuxReceiver.Instance;
            var client = new MockSdvoeClient();

            var first = new SourceCardViewModel(NewDevice("74fe488b07bb", ip));
            first.StartPreviewAsync(rtp, client, "225.0.0.1").GetAwaiter().GetResult();
            var bmp = GiveChannelABitmap(rtp.GetOrCreateChannel(ip, "225.0.0.1"));

            // Refresh: the cards are rebuilt (MainViewModel.RefreshDevicesAsync); the channel and its bitmap live on.
            var rebuilt = new SourceCardViewModel(NewDevice("74fe488b07bb", ip));
            rebuilt.StartPreviewAsync(rtp, client, "225.0.0.1").GetAwaiter().GetResult();

            Assert.Same(bmp, rebuilt.PreviewBitmap); // null = a black preview until the app is restarted
            rtp.RemoveChannel(ip);
        });

        [Fact]
        public void DetachedSourceCard_StopsFollowingTheChannel() => RunSta(() =>
        {
            const string ip = "10.99.99.13";
            var rtp = UnifiedRtpDemuxReceiver.Instance;
            var client = new MockSdvoeClient();

            var old = new SourceCardViewModel(NewDevice("74fe488b07bc", ip));
            old.StartPreviewAsync(rtp, client, "225.0.0.2").GetAwaiter().GetResult();
            var channel = rtp.GetOrCreateChannel(ip, "225.0.0.2");

            var rebuilt = new SourceCardViewModel(NewDevice("74fe488b07bc", ip));
            old.Detach();
            rebuilt.StartPreviewAsync(rtp, client, "225.0.0.2").GetAwaiter().GetResult();

            ushort seq = 0;
            FeedFrame(channel, ref seq, 1000);

            Assert.True(rebuilt.Fps > 0);
            Assert.Equal(0.0, old.Fps); // the replaced card no longer listens
            rtp.RemoveChannel(ip);
        });
    }
}
