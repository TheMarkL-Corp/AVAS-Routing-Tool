using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// A loopback stand-in for the Control Server's Telnet port: answers every line with one JSON line and records the lines.
    /// Destructive commands are only ever sent here, never to a real device.
    /// </summary>
    public sealed class FakeTelnetServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();

        /// <summary>Every command line received, in order (the handshake included).</summary>
        public ConcurrentQueue<string> Lines { get; } = new();

        /// <summary>The JSON status answered to a command line other than the handshake.</summary>
        public Func<string, string> Status { get; set; } = _ => "SUCCESS";

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public FakeTelnetServer()
        {
            _listener.Start();
            _ = Task.Run(AcceptAsync);
        }

        private async Task AcceptAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    _ = Task.Run(() => ServeAsync(client));
                }
            }
            catch (Exception) { }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII);
                using var writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true, NewLine = "\r\n" };
                try
                {
                    while (!_cts.IsCancellationRequested)
                    {
                        string? line = await reader.ReadLineAsync(_cts.Token);
                        if (line == null) break;
                        Lines.Enqueue(line);
                        string status = line.StartsWith("require api", StringComparison.Ordinal) ? "SUCCESS" : Status(line);
                        await writer.WriteLineAsync($"{{\"status\":\"{status}\",\"request_id\":null,\"result\":null,\"error\":null}}");
                    }
                }
                catch (Exception) { }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
        }
    }
}
