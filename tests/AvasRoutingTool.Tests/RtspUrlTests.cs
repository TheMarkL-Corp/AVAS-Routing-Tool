using AvasRoutingTool.Rtsp;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class RtspUrlTests
    {
        [Fact]
        public void Parse_SplitsUserAndPassword()
        {
            Assert.True(RtspUrl.TryParse("rtsp://user:pass-fake@example.test:8554/live/1", out var u, out _));
            Assert.Equal("example.test", u.Host);
            Assert.Equal(8554, u.Port);
            Assert.Equal("/live/1", u.PathAndQuery);
            Assert.Equal("user", u.User);
            Assert.Equal("pass-fake", u.Password);
        }

        [Fact]
        public void Parse_PasswordWithAtAndColon()
        {
            Assert.True(RtspUrl.TryParse("rtsp://user:p@ss:fake@127.0.0.1:554/x", out var u, out _));
            Assert.Equal("user", u.User);
            Assert.Equal("p@ss:fake", u.Password);
            Assert.Equal("127.0.0.1", u.Host);
            Assert.Equal(554, u.Port);
            Assert.Equal("/x", u.PathAndQuery);
        }

        [Fact]
        public void Parse_NoPort_Uses554()
        {
            Assert.True(RtspUrl.TryParse("rtsp://example.test/a", out var u, out _));
            Assert.Equal(554, u.Port);
            Assert.Null(u.User);
            Assert.Null(u.Password);
        }

        [Fact]
        public void Parse_Ipv6Host()
        {
            Assert.True(RtspUrl.TryParse("rtsp://user:pass-fake@[::1]:8554/a?token=abc", out var u, out _));
            Assert.Equal("::1", u.Host);
            Assert.Equal(8554, u.Port);
            Assert.Equal("/a?token=abc", u.PathAndQuery);
            Assert.Contains("[::1]:8554", u.ToFullUrl());
        }

        [Fact]
        public void Parse_TrimsWhitespace()
        {
            Assert.True(RtspUrl.TryParse("  \trtsp://example.test/a \r\n", out var u, out _));
            Assert.Equal("example.test", u.Host);
            Assert.Equal("/a", u.PathAndQuery);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Parse_Empty_ReturnsReason(string text)
        {
            Assert.False(RtspUrl.TryParse(text, out _, out var reason));
            Assert.False(string.IsNullOrWhiteSpace(reason));
        }

        [Fact]
        public void Parse_RejectsHttp_WithReason()
        {
            Assert.False(RtspUrl.TryParse("http://example.test/a", out _, out var reason));
            Assert.False(string.IsNullOrWhiteSpace(reason));
            Assert.DoesNotContain("example.test", reason);
        }

        [Fact]
        public void ToFullUrl_RoundTripsCredentials()
        {
            Assert.True(RtspUrl.TryParse("rtsp://user:pass-fake@127.0.0.1:554/a?token=abc", out var u, out _));
            Assert.Equal("rtsp://user:pass-fake@127.0.0.1:554/a?token=abc", u.ToFullUrl());
        }

        [Fact]
        public void Mask_HidesUserPasswordAndQueryValues()
        {
            Assert.True(RtspUrl.TryParse("rtsp://user:pass-fake@127.0.0.1:554/path?token=abc", out var u, out _));
            var masked = u.ToMasked();
            Assert.Equal("rtsp://***:***@127.0.0.1:554/path?token=***", masked);
            Assert.DoesNotContain("pass-fake", masked);
            Assert.DoesNotContain("abc", masked);
        }

        [Fact]
        public void MaskInText_ReplacesUrlInsideAnExceptionMessage()
        {
            var text = "Cannot open rtsp://user:pass-fake@example.test:554/p?token=abc: connection refused";
            var masked = RtspUrl.MaskInText(text);
            Assert.DoesNotContain("pass-fake", masked);
            Assert.DoesNotContain("abc", masked);
            Assert.Contains("rtsp://***:***@example.test:554/p?token=***", masked);
            Assert.Contains("connection refused", masked);
        }
    }
}
