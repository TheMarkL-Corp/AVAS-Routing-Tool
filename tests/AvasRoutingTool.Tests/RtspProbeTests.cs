using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using AvasRoutingTool.Rtsp;
using Xunit;

namespace AvasRoutingTool.Tests;

public class RtspProbeTests
{
    private static RtspUrl Url(int port, bool creds, string password = FakeRtspServer.FakePassword)
    {
        var text = creds ? $"rtsp://{FakeRtspServer.FakeUser}:{password}@127.0.0.1:{port}/stream" : $"rtsp://127.0.0.1:{port}/stream";
        Assert.True(RtspUrl.TryParse(text, out var u, out _));
        return u;
    }

    private static Task<RtspFailure> Probe(RtspUrl u) => RtspProbe.ClassifyAsync(u, CancellationToken.None);

    [Fact]
    public async Task Probe_Require401_WithoutCredentials_AuthFailed()
    {
        using var s = new FakeRtspServer(FakeRtspMode.Require401);
        Assert.Equal(RtspFailure.AuthFailed, await Probe(Url(s.Port, false)));
    }

    [Fact]
    public async Task Probe_Require401_WithRightCredentials_ReturnsOther()
    {
        using var s = new FakeRtspServer(FakeRtspMode.Require401);
        Assert.Equal(RtspFailure.Other, await Probe(Url(s.Port, true)));
    }

    [Fact]
    public async Task Probe_Require401_WithWrongCredentials_AuthFailed()
    {
        using var s = new FakeRtspServer(FakeRtspMode.Require401);
        Assert.Equal(RtspFailure.AuthFailed, await Probe(Url(s.Port, true, "wrong-fake")));
    }

    [Fact]
    public async Task Probe_NotFound404_NotFound()
    {
        using var s = new FakeRtspServer(FakeRtspMode.NotFound404);
        Assert.Equal(RtspFailure.NotFound, await Probe(Url(s.Port, false)));
    }

    [Fact]
    public async Task Probe_RefuseConnection_Unreachable()
    {
        using var s = new FakeRtspServer(FakeRtspMode.RefuseConnection);
        Assert.Equal(RtspFailure.Unreachable, await Probe(Url(s.Port, false)));
    }

    [Fact]
    public async Task Probe_Normal_ReturnsOther()
    {
        using var s = new FakeRtspServer(FakeRtspMode.Normal);
        Assert.Equal(RtspFailure.Other, await Probe(Url(s.Port, false)));
    }

    [Fact]
    public async Task Probe_ServerThatNeverAnswers_Unreachable()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var held = new List<TcpClient>();
        var accept = Task.Run(async () => { try { while (true) held.Add(await l.AcceptTcpClientAsync()); } catch { } });
        try
        {
            var sw = Stopwatch.StartNew();
            var r = await Probe(Url(((IPEndPoint)l.LocalEndpoint).Port, false));
            Assert.Equal(RtspFailure.Unreachable, r);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(4), "took " + sw.Elapsed);
        }
        finally { l.Stop(); foreach (var c in held) c.Close(); }
    }

    [Fact]
    public async Task Probe_NeverThrows_OnGarbageReply()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var serve = Task.Run(async () =>
        {
            try
            {
                using var c = await l.AcceptTcpClientAsync();
                var s = c.GetStream();
                var buf = new byte[1024];
                await s.ReadAsync(buf);
                var junk = Encoding.ASCII.GetBytes("nonsense \u0001 not rtsp\r\n\r\n");
                await s.WriteAsync(junk);
                await Task.Delay(500);
            }
            catch { }
        });
        try
        {
            Assert.Equal(RtspFailure.Other, await Probe(Url(((IPEndPoint)l.LocalEndpoint).Port, false)));
        }
        finally { l.Stop(); }
    }
}
