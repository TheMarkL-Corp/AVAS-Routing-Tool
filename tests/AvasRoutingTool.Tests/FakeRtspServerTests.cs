using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace AvasRoutingTool.Tests;

public class FakeRtspServerTests
{
    private const int T = 15000;

    private sealed class Rtsp : IDisposable
    {
        public TcpClient Client = new();
        public NetworkStream S = null!;
        public async Task ConnectAsync(int port)
        {
            using var cts = new CancellationTokenSource(5000);
            await Client.ConnectAsync(IPAddress.Loopback, port, cts.Token);
            S = Client.GetStream();
        }
        public async Task<byte> ReadByteAsync()
        {
            var b = new byte[1];
            using var cts = new CancellationTokenSource(5000);
            int n = await S.ReadAsync(b, cts.Token);
            if (n == 0) throw new EndOfStreamException();
            return b[0];
        }
        public async Task ReadExactAsync(byte[] buf)
        {
            int o = 0;
            using var cts = new CancellationTokenSource(5000);
            while (o < buf.Length)
            {
                int n = await S.ReadAsync(buf.AsMemory(o), cts.Token);
                if (n == 0) throw new EndOfStreamException();
                o += n;
            }
        }
        /// <summary>Sends a request and returns (status line, headers text, body).</summary>
        public async Task<(string Status, string Headers, string Body)> RequestAsync(string method, string url, int cseq, string extra = "")
        {
            var req = $"{method} {url} RTSP/1.0\r\nCSeq: {cseq}\r\n{extra}\r\n";
            await S.WriteAsync(Encoding.ASCII.GetBytes(req));
            var sb = new StringBuilder();
            while (!sb.ToString().EndsWith("\r\n\r\n")) sb.Append((char)await ReadByteAsync());
            var head = sb.ToString();
            int len = 0;
            foreach (var l in head.Split("\r\n"))
                if (l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) len = int.Parse(l[15..].Trim());
            var body = new byte[len];
            await ReadExactAsync(body);
            int eol = head.IndexOf("\r\n");
            return (head[..eol], head[(eol + 2)..], Encoding.ASCII.GetString(body));
        }
        public void Dispose() => Client.Dispose();
    }

    private static string Url(FakeRtspServer s) => $"rtsp://127.0.0.1:{s.Port}/stream";
    private static string Md5(string s) => Convert.ToHexString(MD5.HashData(Encoding.ASCII.GetBytes(s))).ToLowerInvariant();

    [Fact(Timeout = T)]
    public async Task DescribeReturnsSdpWithH264()
    {
        using var srv = new FakeRtspServer(FakeRtspMode.Normal);
        using var c = new Rtsp(); await c.ConnectAsync(srv.Port);
        var opt = await c.RequestAsync("OPTIONS", Url(srv), 1);
        Assert.Contains("200", opt.Status);
        Assert.Contains("DESCRIBE", opt.Headers);
        var d = await c.RequestAsync("DESCRIBE", Url(srv), 2, "Accept: application/sdp\r\n");
        Assert.Contains("200", d.Status);
        Assert.Contains("m=video", d.Body);
        Assert.Contains("H264", d.Body);
        Assert.Contains("sprop-parameter-sets=", d.Body);
    }

    [Fact(Timeout = T)]
    public async Task Require401_FirstDescribeWithoutCredentials_Gets401_ThenOkWithBasicCredentials()
    {
        using var srv = new FakeRtspServer(FakeRtspMode.Require401);
        using var c = new Rtsp(); await c.ConnectAsync(srv.Port);
        var first = await c.RequestAsync("DESCRIBE", Url(srv), 1);
        Assert.Contains("401", first.Status);
        Assert.Contains("WWW-Authenticate", first.Headers);
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes("user:pass-fake"));
        var second = await c.RequestAsync("DESCRIBE", Url(srv), 2, $"Authorization: Basic {basic}\r\n");
        Assert.Contains("200", second.Status);
        Assert.Contains("H264", second.Body);

        var bad = Convert.ToBase64String(Encoding.ASCII.GetBytes("user:wrong"));
        var third = await c.RequestAsync("DESCRIBE", Url(srv), 3, $"Authorization: Basic {bad}\r\n");
        Assert.Contains("401", third.Status);
    }

    [Fact(Timeout = T)]
    public async Task Require401_DigestCredentials_Accepted()
    {
        using var srv = new FakeRtspServer(FakeRtspMode.Require401);
        using var c = new Rtsp(); await c.ConnectAsync(srv.Port);
        var first = await c.RequestAsync("DESCRIBE", Url(srv), 1);
        Assert.Contains("401", first.Status);
        var line = first.Headers.Split("\r\n").First(l => l.StartsWith("WWW-Authenticate: Digest", StringComparison.OrdinalIgnoreCase));
        string Param(string n) => System.Text.RegularExpressions.Regex.Match(line, n + "=\"([^\"]*)\"").Groups[1].Value;
        var realm = Param("realm"); var nonce = Param("nonce");
        var uri = Url(srv);
        var response = Md5($"{Md5($"user:{realm}:pass-fake")}:{nonce}:{Md5($"DESCRIBE:{uri}")}");
        var auth = $"Authorization: Digest username=\"user\", realm=\"{realm}\", nonce=\"{nonce}\", uri=\"{uri}\", response=\"{response}\"\r\n";
        var second = await c.RequestAsync("DESCRIBE", uri, 2, auth);
        Assert.Contains("200", second.Status);
        Assert.Contains("H264", second.Body);
    }

    [Fact(Timeout = T)]
    public async Task NotFound404_Describe_Returns404()
    {
        using var srv = new FakeRtspServer(FakeRtspMode.NotFound404);
        using var c = new Rtsp(); await c.ConnectAsync(srv.Port);
        var d = await c.RequestAsync("DESCRIBE", Url(srv), 1);
        Assert.Contains("404", d.Status);
    }

    private static async Task<Rtsp> PlayAsync(FakeRtspServer srv)
    {
        var c = new Rtsp(); await c.ConnectAsync(srv.Port);
        var d = await c.RequestAsync("DESCRIBE", Url(srv), 1);
        Assert.Contains("200", d.Status);
        var s = await c.RequestAsync("SETUP", Url(srv) + "/trackID=0", 2, "Transport: RTP/AVP/TCP;unicast;interleaved=0-1\r\n");
        Assert.Contains("200", s.Status);
        Assert.Contains("interleaved=0-1", s.Headers);
        var session = s.Headers.Split("\r\n").First(l => l.StartsWith("Session:", StringComparison.OrdinalIgnoreCase))[8..].Split(';')[0].Trim();
        var p = await c.RequestAsync("PLAY", Url(srv), 3, $"Session: {session}\r\nRange: npt=0.000-\r\n");
        Assert.Contains("200", p.Status);
        return c;
    }

    [Fact(Timeout = T)]
    public async Task Normal_Play_SendsInterleavedRtpFrames()
    {
        using var srv = new FakeRtspServer(FakeRtspMode.Normal);
        using var c = await PlayAsync(srv);
        int packets = 0, markers = 0;
        while (markers < 3 && packets < 200)
        {
            Assert.Equal((byte)'$', await c.ReadByteAsync());
            var ch = await c.ReadByteAsync();
            var lenb = new byte[2]; await c.ReadExactAsync(lenb);
            var pkt = new byte[(lenb[0] << 8) | lenb[1]]; await c.ReadExactAsync(pkt);
            Assert.Equal(0, ch);
            Assert.True(pkt.Length > 12);
            Assert.Equal(2, pkt[0] >> 6);
            Assert.Equal(96, pkt[1] & 0x7F);
            Assert.True(pkt.Length <= 1500);
            if ((pkt[1] & 0x80) != 0) markers++;
            packets++;
        }
        Assert.Equal(3, markers);
    }

    [Fact(Timeout = T)]
    public async Task Drop_ClosesTheSocket()
    {
        using var srv = new FakeRtspServer(FakeRtspMode.Normal);
        using var c = await PlayAsync(srv);
        srv.Drop();
        using var cts = new CancellationTokenSource(5000);
        var buf = new byte[4096];
        int n = 1;
        try { while (n > 0) n = await c.S.ReadAsync(buf, cts.Token); }
        catch (IOException) { n = 0; }
        Assert.Equal(0, n);
    }

    [Fact(Timeout = T)]
    public async Task RefuseConnection_ConnectFails()
    {
        using var srv = new FakeRtspServer(FakeRtspMode.RefuseConnection);
        using var c = new TcpClient();
        await Assert.ThrowsAnyAsync<SocketException>(() => c.ConnectAsync(IPAddress.Loopback, srv.Port));
    }

    [Fact(Timeout = T)]
    public async Task Dispose_StopsListening()
    {
        var srv = new FakeRtspServer(FakeRtspMode.Normal);
        int port = srv.Port;
        srv.Dispose();
        using var c = new TcpClient();
        await Assert.ThrowsAnyAsync<SocketException>(() => c.ConnectAsync(IPAddress.Loopback, port));
    }
}
