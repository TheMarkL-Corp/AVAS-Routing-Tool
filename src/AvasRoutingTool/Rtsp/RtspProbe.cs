using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AvasRoutingTool.Rtsp
{
    /// <summary>
    /// Classifies why a stream did not start by asking the camera itself over a private TCP connection
    /// (OPTIONS, then DESCRIBE, answering one 401 with the URL's credentials). Independent of any decoder state,
    /// so several players can fail at once with their own result. Never logs, never throws, returns no text.
    /// </summary>
    public static class RtspProbe
    {
        private const int OverallLimitMs = 3000;
        private const int MaxHeaderBytes = 16 * 1024;

        public static async Task<RtspFailure> ClassifyAsync(RtspUrl url, CancellationToken token)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(OverallLimitMs);
                return await Run(url, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return RtspFailure.Unreachable; }
            catch (SocketException) { return RtspFailure.Unreachable; }
            catch (IOException) { return RtspFailure.Unreachable; }
            catch (ObjectDisposedException) { return RtspFailure.Unreachable; }
            catch (Exception) { return RtspFailure.Other; }
        }

        private static async Task<RtspFailure> Run(RtspUrl url, CancellationToken ct)
        {
            using var client = new TcpClient { NoDelay = true };
            await client.ConnectAsync(url.Host, url.Port, ct).ConfigureAwait(false);
            var stream = client.GetStream();
            var uri = new RtspUrl(url.Host, url.Port, url.PathAndQuery, null, null).ToFullUrl();

            var options = await Exchange(stream, "OPTIONS", uri, 1, null, ct).ConfigureAwait(false);
            if (options == null) return RtspFailure.Other;

            var first = await Exchange(stream, "DESCRIBE", uri, 2, null, ct).ConfigureAwait(false);
            if (first == null) return RtspFailure.Other;
            if (first.Status == 404) return RtspFailure.NotFound;
            if (first.Status != 401) return RtspFailure.Other;
            if (string.IsNullOrEmpty(url.User)) return RtspFailure.AuthFailed;

            var auth = BuildAuthorization(url, "DESCRIBE", uri, first.Challenges);
            if (auth == null) return RtspFailure.AuthFailed;
            var second = await Exchange(stream, "DESCRIBE", uri, 3, auth, ct).ConfigureAwait(false);
            if (second == null) return RtspFailure.Other;
            if (second.Status == 401) return RtspFailure.AuthFailed;
            if (second.Status == 404) return RtspFailure.NotFound;
            return RtspFailure.Other;
        }

        private sealed class Reply
        {
            public int Status;
            public List<string> Challenges = new();
        }

        private static async Task<Reply?> Exchange(NetworkStream s, string method, string uri, int cseq, string? authorization, CancellationToken ct)
        {
            var sb = new StringBuilder();
            sb.Append(method).Append(' ').Append(uri).Append(" RTSP/1.0\r\nCSeq: ").Append(cseq).Append("\r\nUser-Agent: AvasRoutingTool-Probe\r\n");
            if (method == "DESCRIBE") sb.Append("Accept: application/sdp\r\n");
            if (authorization != null) sb.Append("Authorization: ").Append(authorization).Append("\r\n");
            sb.Append("\r\n");
            var bytes = Encoding.ASCII.GetBytes(sb.ToString());
            await s.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);

            var head = new StringBuilder();
            var one = new byte[1];
            while (!EndsWithBlankLine(head))
            {
                int n = await s.ReadAsync(one, 0, 1, ct).ConfigureAwait(false);
                if (n == 0) throw new IOException();   // closed without a full reply: treated as unreachable
                head.Append((char)one[0]);
                if (head.Length > MaxHeaderBytes) return null;
            }

            var lines = head.ToString().Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            var parts = lines[0].Split(' ');
            if (parts.Length < 2 || !parts[0].StartsWith("RTSP/", StringComparison.OrdinalIgnoreCase) || !int.TryParse(parts[1], out var status))
                return null;

            var reply = new Reply { Status = status };
            int contentLength = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                var c = lines[i].IndexOf(':');
                if (c <= 0) continue;
                var name = lines[i].Substring(0, c).Trim();
                var value = lines[i].Substring(c + 1).Trim();
                if (name.Equals("WWW-Authenticate", StringComparison.OrdinalIgnoreCase)) reply.Challenges.Add(value);
                else if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) int.TryParse(value, out contentLength);
            }
            // Drain a body so the next reply on this connection starts cleanly.
            var buf = new byte[256];
            while (contentLength > 0 && contentLength < 1_000_000)
            {
                int n = await s.ReadAsync(buf, 0, Math.Min(buf.Length, contentLength), ct).ConfigureAwait(false);
                if (n == 0) throw new IOException();
                contentLength -= n;
            }
            return reply;
        }

        private static bool EndsWithBlankLine(StringBuilder b)
        {
            int n = b.Length;
            return n >= 4 && b[n - 4] == '\r' && b[n - 3] == '\n' && b[n - 2] == '\r' && b[n - 1] == '\n';
        }

        private static string? BuildAuthorization(RtspUrl url, string method, string uri, List<string> challenges)
        {
            var user = url.User ?? "";
            var pass = url.Password ?? "";
            foreach (var ch in challenges)
            {
                if (!ch.StartsWith("Digest", StringComparison.OrdinalIgnoreCase)) continue;
                string P(string n)
                {
                    var m = Regex.Match(ch, "(?:^|[\\s,])" + n + "=(?:\"([^\"]*)\"|([^\\s,]*))", RegexOptions.IgnoreCase);
                    return m.Success ? (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value) : "";
                }
                var realm = P("realm");
                var nonce = P("nonce");
                var ha1 = Md5($"{user}:{realm}:{pass}");
                var ha2 = Md5($"{method}:{uri}");
                var response = Md5($"{ha1}:{nonce}:{ha2}");
                return $"Digest username=\"{user}\", realm=\"{realm}\", nonce=\"{nonce}\", uri=\"{uri}\", response=\"{response}\"";
            }
            foreach (var ch in challenges)
            {
                if (ch.StartsWith("Basic", StringComparison.OrdinalIgnoreCase))
                    return "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}"));
            }
            return null;
        }

        private static string Md5(string s)
        {
#pragma warning disable CA5351 // RTSP Digest authentication is defined with MD5
            return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
#pragma warning restore CA5351
        }
    }
}
