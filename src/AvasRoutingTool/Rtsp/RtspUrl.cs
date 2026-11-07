using System;
using System.Text;
using System.Text.RegularExpressions;

namespace AvasRoutingTool.Rtsp
{
    /// <summary>A parsed rtsp:// address. ToFullUrl is for the decoder only; everything else uses ToMasked.</summary>
    public sealed record RtspUrl(string Host, int Port, string PathAndQuery, string? User, string? Password)
    {
        private const string Prefix = "rtsp://";
        private const int DefaultPort = 554;
        private static readonly Regex UrlInText = new(@"rtsps?://[^\s""'<>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool TryParse(string text, out RtspUrl url, out string reason)
        {
            url = null!;
            var s = (text ?? string.Empty).Trim();
            if (s.Length == 0) { reason = "Enter an rtsp:// address."; return false; }
            if (!s.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) { reason = "The address must start with rtsp://"; return false; }
            if (s.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) >= 0) { reason = "The address must not contain spaces."; return false; }

            var rest = s.Substring(Prefix.Length);
            var authEnd = rest.IndexOfAny(new[] { '/', '?' });
            // A password may hold '/' or '?' only if an '@' follows; keep it simple: authority ends at the first / or ?, unless a later '@' precedes the path start.
            var authority = authEnd < 0 ? rest : rest.Substring(0, authEnd);
            var path = authEnd < 0 ? "/" : rest.Substring(authEnd);
            if (path.StartsWith("?")) path = "/" + path;
            var hash = path.IndexOf('#');
            if (hash >= 0) path = path.Substring(0, hash);

            string? user = null, pass = null;
            var at = authority.LastIndexOf('@');
            var hostPort = authority;
            if (at >= 0)
            {
                var info = authority.Substring(0, at);
                hostPort = authority.Substring(at + 1);
                var colon = info.IndexOf(':');
                if (colon >= 0) { user = Unescape(info.Substring(0, colon)); pass = Unescape(info.Substring(colon + 1)); }
                else user = Unescape(info);
            }

            string host; int port = DefaultPort;
            if (hostPort.StartsWith("["))
            {
                var close = hostPort.IndexOf(']');
                if (close < 0) { reason = "The host address is not valid."; return false; }
                host = hostPort.Substring(1, close - 1);
                var after = hostPort.Substring(close + 1);
                if (after.Length > 0 && !TryPort(after, out port)) { reason = "The port is not valid."; return false; }
            }
            else
            {
                var colon = hostPort.LastIndexOf(':');
                if (colon >= 0)
                {
                    host = hostPort.Substring(0, colon);
                    if (!TryPort(hostPort.Substring(colon), out port)) { reason = "The port is not valid."; return false; }
                }
                else host = hostPort;
            }
            if (host.Length == 0) { reason = "The address has no host."; return false; }

            url = new RtspUrl(host, port, path, user, pass);
            reason = string.Empty;
            return true;
        }

        public string ToFullUrl()
        {
            var sb = new StringBuilder(Prefix);
            if (User != null)
            {
                sb.Append(Uri.EscapeDataString(User));
                if (Password != null) sb.Append(':').Append(Uri.EscapeDataString(Password));
                sb.Append('@');
            }
            AppendHostPort(sb);
            sb.Append(PathAndQuery);
            return sb.ToString();
        }

        public string ToMasked()
        {
            var sb = new StringBuilder(Prefix);
            if (User != null || Password != null) sb.Append("***:***@");
            AppendHostPort(sb);
            var q = PathAndQuery.IndexOf('?');
            if (q < 0) { sb.Append(PathAndQuery); return sb.ToString(); }
            sb.Append(PathAndQuery, 0, q + 1);
            var parts = PathAndQuery.Substring(q + 1).Split('&');
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0) sb.Append('&');
                var eq = parts[i].IndexOf('=');
                sb.Append(eq < 0 ? parts[i] : parts[i].Substring(0, eq)).Append(eq < 0 ? string.Empty : "=***");
            }
            return sb.ToString();
        }

        public override string ToString() => ToMasked();

        public static string MaskInText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            return UrlInText.Replace(text, m =>
            {
                var raw = m.Value;
                var trimmed = raw.TrimEnd('.', ',', ';', ':', ')', ']', '!', '?');
                var tail = raw.Substring(trimmed.Length);
                var masked = TryParse(trimmed, out var u, out _) ? u.ToMasked() : "rtsp://***";
                return masked + tail;
            });
        }

        private void AppendHostPort(StringBuilder sb)
        {
            if (Host.Contains(':')) sb.Append('[').Append(Host).Append(']'); else sb.Append(Host);
            sb.Append(':').Append(Port);
        }

        private static bool TryPort(string colonPort, out int port)
        {
            port = DefaultPort;
            if (!colonPort.StartsWith(":")) return false;
            return int.TryParse(colonPort.AsSpan(1), out port) && port is > 0 and <= 65535;
        }

        private static string Unescape(string s)
        {
            try { return Uri.UnescapeDataString(s); } catch { return s; }
        }
    }
}
