using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AvasRoutingTool.Tests;

public enum FakeRtspMode { Normal, Require401, NotFound404, RefuseConnection }

/// <summary>
/// A loopback RTSP server for tests: OPTIONS, DESCRIBE (H.264 SDP), SETUP (TCP interleaved only), PLAY, TEARDOWN.
/// After PLAY it loops Fixtures/rtsp-testpattern.h264 as RTP (RFC 6184) at 25 fps. Fake credentials only: user / pass-fake.
/// </summary>
public sealed class FakeRtspServer : IDisposable
{
    public const string FakeUser = "user";
    public const string FakePassword = "pass-fake";
    private const string Realm = "fake-rtsp";
    private const int MaxPayload = 1400;
    private const int Fps = 25;

    private readonly FakeRtspMode _mode;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<TcpClient, byte> _clients = new();
    private readonly string _nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
    private readonly List<List<byte[]>> _frames = new();   // each frame: leading non-VCL NALs + one VCL NAL
    private readonly string _sprop;
    private readonly string _profileLevelId;

    public int Port { get; }

    public FakeRtspServer(FakeRtspMode mode)
    {
        _mode = mode;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        if (mode == FakeRtspMode.RefuseConnection)
        {
            _listener.Stop();   // port stays reserved by nobody: connects are refused
            _sprop = _profileLevelId = "";
            return;
        }

        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "rtsp-testpattern.h264");
        byte[]? sps = null, pps = null;
        var pending = new List<byte[]>();
        foreach (var nal in SplitAnnexB(File.ReadAllBytes(path)))
        {
            int type = nal[0] & 0x1F;
            if (type == 7) sps ??= nal;
            if (type == 8) pps ??= nal;
            if (type == 9) continue;                         // access unit delimiter: not needed over RTP
            pending.Add(nal);
            if (type == 1 || type == 5) { _frames.Add(pending); pending = new List<byte[]>(); }
        }
        if (sps == null || pps == null || _frames.Count == 0) throw new InvalidOperationException("fixture has no SPS/PPS/frames");
        _sprop = Convert.ToBase64String(sps) + "," + Convert.ToBase64String(pps);
        _profileLevelId = Convert.ToHexString(sps, 1, 3);

        _ = Task.Run(AcceptLoop);
    }

    /// <summary>Closes every client connection (the listener keeps accepting).</summary>
    public void Drop()
    {
        foreach (var c in _clients.Keys)
        {
            try { c.Client.Shutdown(SocketShutdown.Both); } catch { }
            try { c.Close(); } catch { }
        }
        _clients.Clear();
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        Drop();
    }

    private static IEnumerable<byte[]> SplitAnnexB(byte[] d)
    {
        var starts = new List<int>();
        for (int i = 0; i + 2 < d.Length; i++)
            if (d[i] == 0 && d[i + 1] == 0 && d[i + 2] == 1) { starts.Add(i); i += 2; }
        for (int k = 0; k < starts.Count; k++)
        {
            int s = starts[k] + 3;
            int e = k + 1 < starts.Count ? starts[k + 1] : d.Length;
            while (k + 1 < starts.Count && e > s && d[e - 1] == 0) e--;   // zero of a 4-byte start code
            yield return d[s..e];
        }
    }

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient c;
            try { c = await _listener.AcceptTcpClientAsync(_cts.Token); }
            catch { return; }
            c.NoDelay = true;
            _clients[c] = 0;
            _ = Task.Run(() => Serve(c));
        }
    }

    private async Task Serve(TcpClient client)
    {
        var stream = client.GetStream();
        var writeLock = new object();
        var playCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        string session = Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
        try
        {
            while (!playCts.IsCancellationRequested)
            {
                var req = await ReadRequest(stream, playCts.Token);
                if (req == null) break;
                var (method, uri, headers) = req.Value;
                headers.TryGetValue("cseq", out var cseq);
                cseq ??= "0";

                void Reply(string status, string extra = "", string body = "")
                {
                    var sb = new StringBuilder();
                    sb.Append($"RTSP/1.0 {status}\r\nCSeq: {cseq}\r\n{extra}");
                    if (body.Length > 0) sb.Append($"Content-Type: application/sdp\r\nContent-Length: {body.Length}\r\n");
                    sb.Append("\r\n").Append(body);
                    lock (writeLock) { var b = Encoding.ASCII.GetBytes(sb.ToString()); stream.Write(b, 0, b.Length); }
                }

                if (method == "OPTIONS") { Reply("200 OK", "Public: OPTIONS, DESCRIBE, SETUP, PLAY, TEARDOWN\r\n"); continue; }

                if (_mode == FakeRtspMode.Require401 && !Authorized(method, headers))
                {
                    Reply("401 Unauthorized",
                        $"WWW-Authenticate: Digest realm=\"{Realm}\", nonce=\"{_nonce}\"\r\nWWW-Authenticate: Basic realm=\"{Realm}\"\r\n");
                    continue;
                }

                switch (method)
                {
                    case "DESCRIBE":
                        if (_mode == FakeRtspMode.NotFound404) { Reply("404 Not Found"); break; }
                        Reply("200 OK", $"Content-Base: rtsp://127.0.0.1:{Port}/stream/\r\n", Sdp());
                        break;
                    case "SETUP":
                        if (!headers.TryGetValue("transport", out var tr) || !tr.Contains("RTP/AVP/TCP") || !tr.Contains("interleaved="))
                        { Reply("461 Unsupported Transport"); break; }
                        Reply("200 OK", $"Session: {session};timeout=60\r\nTransport: RTP/AVP/TCP;unicast;interleaved=0-1\r\n");
                        break;
                    case "PLAY":
                        Reply("200 OK", $"Session: {session}\r\nRange: npt=0.000-\r\n");
                        _ = Task.Run(() => SendLoop(stream, writeLock, playCts.Token));
                        break;
                    case "TEARDOWN":
                        Reply("200 OK", $"Session: {session}\r\n");
                        playCts.Cancel();
                        break;
                    default:
                        Reply("405 Method Not Allowed");
                        break;
                }
            }
        }
        catch { /* client gone or server disposed */ }
        finally
        {
            playCts.Cancel();
            _clients.TryRemove(client, out _);
            try { client.Close(); } catch { }
        }
    }

    private static async Task<(string Method, string Uri, Dictionary<string, string> Headers)?> ReadRequest(NetworkStream s, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var one = new byte[1];
        while (!sb.ToString().EndsWith("\r\n\r\n"))
        {
            int n = await s.ReadAsync(one, ct);
            if (n == 0) return null;
            if (one[0] == (byte)'$') continue;   // stray interleaved data from a client: ignore
            sb.Append((char)one[0]);
        }
        var lines = sb.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var first = lines[0].Split(' ');
        var h = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in lines.Skip(1))
        {
            int i = l.IndexOf(':');
            if (i > 0) h[l[..i].Trim().ToLowerInvariant()] = l[(i + 1)..].Trim();
        }
        return (first[0].ToUpperInvariant(), first.Length > 1 ? first[1] : "", h);
    }

    private bool Authorized(string method, Dictionary<string, string> headers)
    {
        if (!headers.TryGetValue("authorization", out var a)) return false;
        if (a.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            try { return Encoding.ASCII.GetString(Convert.FromBase64String(a[6..].Trim())) == $"{FakeUser}:{FakePassword}"; }
            catch { return false; }
        }
        if (a.StartsWith("Digest ", StringComparison.OrdinalIgnoreCase))
        {
            string P(string n) => Regex.Match(a, n + "=\"?([^\",]*)\"?").Groups[1].Value;
            if (P("username") != FakeUser || P("nonce") != _nonce) return false;
            var ha1 = Md5($"{FakeUser}:{Realm}:{FakePassword}");
            var ha2 = Md5($"{method}:{P("uri")}");
            return string.Equals(P("response"), Md5($"{ha1}:{_nonce}:{ha2}"), StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    private static string Md5(string s) => Convert.ToHexString(MD5.HashData(Encoding.ASCII.GetBytes(s))).ToLowerInvariant();

    private string Sdp() =>
        "v=0\r\no=- 0 0 IN IP4 127.0.0.1\r\ns=fake\r\nc=IN IP4 0.0.0.0\r\nt=0 0\r\n" +
        "m=video 0 RTP/AVP 96\r\na=rtpmap:96 H264/90000\r\n" +
        $"a=fmtp:96 packetization-mode=1;profile-level-id={_profileLevelId};sprop-parameter-sets={_sprop}\r\n" +
        "a=control:trackID=0\r\n";

    private async Task SendLoop(NetworkStream stream, object writeLock, CancellationToken ct)
    {
        ushort seq = 0;
        uint ts = 0;
        uint ssrc = 0x1234ABCD;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long frameNo = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                foreach (var frame in _frames)
                {
                    foreach (var nal in frame)
                    {
                        bool vcl = (nal[0] & 0x1F) is 1 or 5;
                        if (nal.Length <= MaxPayload)
                            Send(stream, writeLock, Rtp(ref seq, ts, ssrc, vcl, nal));
                        else
                        {
                            byte indicator = (byte)((nal[0] & 0xE0) | 28);
                            int off = 1;
                            while (off < nal.Length)
                            {
                                int len = Math.Min(MaxPayload - 2, nal.Length - off);
                                bool start = off == 1, end = off + len >= nal.Length;
                                var pl = new byte[len + 2];
                                pl[0] = indicator;
                                pl[1] = (byte)((start ? 0x80 : 0) | (end ? 0x40 : 0) | (nal[0] & 0x1F));
                                Array.Copy(nal, off, pl, 2, len);
                                Send(stream, writeLock, Rtp(ref seq, ts, ssrc, vcl && end, pl));
                                off += len;
                            }
                        }
                    }
                    ts += 90000 / Fps;
                    frameNo++;
                    long due = frameNo * 1000 / Fps;
                    int wait = (int)(due - clock.ElapsedMilliseconds);
                    if (wait > 0) await Task.Delay(wait, ct);
                }
            }
        }
        catch { /* stopped, dropped or client gone */ }
    }

    private static byte[] Rtp(ref ushort seq, uint ts, uint ssrc, bool marker, byte[] payload)
    {
        var p = new byte[12 + payload.Length];
        p[0] = 0x80;
        p[1] = (byte)(96 | (marker ? 0x80 : 0));
        p[2] = (byte)(seq >> 8); p[3] = (byte)seq;
        p[4] = (byte)(ts >> 24); p[5] = (byte)(ts >> 16); p[6] = (byte)(ts >> 8); p[7] = (byte)ts;
        p[8] = (byte)(ssrc >> 24); p[9] = (byte)(ssrc >> 16); p[10] = (byte)(ssrc >> 8); p[11] = (byte)ssrc;
        Array.Copy(payload, 0, p, 12, payload.Length);
        seq++;
        return p;
    }

    private static void Send(NetworkStream stream, object writeLock, byte[] rtp)
    {
        var f = new byte[4 + rtp.Length];
        f[0] = (byte)'$'; f[1] = 0; f[2] = (byte)(rtp.Length >> 8); f[3] = (byte)rtp.Length;
        Array.Copy(rtp, 0, f, 4, rtp.Length);
        lock (writeLock) stream.Write(f, 0, f.Length);
    }
}
