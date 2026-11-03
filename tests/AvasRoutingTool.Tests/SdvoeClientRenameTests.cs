using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AvasRoutingTool.Sdvoe;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class SdvoeClientRenameTests
    {
        private const string TxMac = "74fe488b07bb";

        private sealed record Call(string Method, string Path, string RawBody);

        /// <summary>Records every request and answers through a script (status code + body), or throws.</summary>
        private sealed class ScriptedHandler : HttpMessageHandler
        {
            public List<Call> Calls { get; } = new();

            public Func<string, string, (HttpStatusCode Status, string Body)>? Responder { get; set; }

            public Exception? ThrowOnSend { get; set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string raw = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
                Calls.Add(new Call(request.Method.Method, request.RequestUri!.AbsolutePath, raw));
                if (ThrowOnSend != null) throw ThrowOnSend;

                var (status, body) = Responder!(request.Method.Method, request.RequestUri.AbsolutePath);
                return new HttpResponseMessage(status) { Content = new StringContent(body) };
            }
        }

        private static string Processing(int id) =>
            $"{{\"status\":\"PROCESSING\",\"request_id\":{id},\"result\":null,\"error\":null}}";

        private static string Success(int id) =>
            $"{{\"status\":\"SUCCESS\",\"request_id\":{id},\"result\":{{\"devices\":[],\"error\":[]}},\"error\":null}}";

        private static string Rejected(string reason, string message) =>
            $"{{\"status\":\"ERROR\",\"request_id\":null,\"result\":null,\"error\":{{\"reason\":\"{reason}\",\"message\":\"{message}\"}}}}";

        private static SdvoeClient FastClient(ScriptedHandler handler) =>
            new(handler) { PollInterval = TimeSpan.FromMilliseconds(5), RequestTimeout = TimeSpan.FromMilliseconds(400) };

        private static ScriptedHandler AcceptingServer() => new()
        {
            Responder = (method, path) => method == "POST"
                ? (HttpStatusCode.Created, Processing(555))
                : (HttpStatusCode.OK, Success(555))
        };

        [Fact]
        public async Task Rename_SendsFindingsCall()
        {
            var handler = AcceptingServer();
            var client = FastClient(handler);

            RenameResult result = await client.RenameDeviceAsync("74:FE:48:8B:07:BB", "Lobby-TX1");

            Assert.True(result.Ok);
            Assert.Null(result.Reason);
            Assert.Equal(2, handler.Calls.Count);

            Assert.Equal("POST", handler.Calls[0].Method);
            Assert.Equal($"/api/device/{TxMac}", handler.Calls[0].Path);
            using var doc = JsonDocument.Parse(handler.Calls[0].RawBody);
            var body = doc.RootElement;
            Assert.Equal(3, body.EnumerateObject().Count());
            Assert.Equal("set:property", body.GetProperty("op").GetString());
            Assert.Equal("configuration.device_name", body.GetProperty("key").GetString());
            Assert.Equal("Lobby-TX1", body.GetProperty("value").GetString());

            Assert.Equal("GET", handler.Calls[1].Method);
            Assert.Equal("/api/request/555", handler.Calls[1].Path);
        }

        [Fact]
        public async Task Rename_NameIsSentVerbatim()
        {
            var handler = AcceptingServer();
            var client = FastClient(handler);

            RenameResult result = await client.RenameDeviceAsync(TxMac, "Lobby TX 1");

            Assert.True(result.Ok);
            Assert.Contains("\"value\":\"Lobby TX 1\"", handler.Calls[0].RawBody);
        }

        [Fact]
        public async Task Rename_ServerRejectsTooLong_ReturnsTheServersMessage()
        {
            var handler = new ScriptedHandler
            {
                Responder = (m, p) => (HttpStatusCode.BadRequest,
                    Rejected("ILLEGAL_ARGUMENT", "Device name too long, must be 19 characters at most"))
            };
            var client = FastClient(handler);

            RenameResult result = await client.RenameDeviceAsync(TxMac, "01234567890123456789");

            Assert.False(result.Ok);
            Assert.Equal("Device name too long, must be 19 characters at most", result.Reason);
            Assert.Single(handler.Calls);
        }

        [Fact]
        public async Task Rename_ServerRejectsColon_ReturnsTheServersMessage()
        {
            var handler = new ScriptedHandler
            {
                Responder = (m, p) => (HttpStatusCode.BadRequest,
                    Rejected("ILLEGAL_ARGUMENT", "Device name contains unsupported character ':'."))
            };
            var client = FastClient(handler);

            RenameResult result = await client.RenameDeviceAsync(TxMac, "R1:colon");

            Assert.False(result.Ok);
            Assert.Equal("Device name contains unsupported character ':'.", result.Reason);
        }

        [Fact]
        public async Task Rename_ServerUnreachable_ReturnsReason()
        {
            var handler = new ScriptedHandler { ThrowOnSend = new HttpRequestException("No connection could be made") };
            var client = FastClient(handler);

            RenameResult result = await client.RenameDeviceAsync(TxMac, "Lobby-TX1");

            Assert.False(result.Ok);
            Assert.StartsWith("Couldn't reach the Control Server", result.Reason);
            Assert.Contains("No connection could be made", result.Reason);
        }

        [Fact]
        public async Task Rename_RequestFailsLater_ReturnsReason()
        {
            var handler = new ScriptedHandler
            {
                Responder = (method, path) => method == "POST"
                    ? (HttpStatusCode.Created, Processing(556))
                    : (HttpStatusCode.OK, "{\"status\":\"ERROR\",\"request_id\":556,\"result\":null,\"error\":{\"reason\":\"DEVICE_DISCONNECTED\",\"message\":\"Device is not connected\"}}")
            };
            var client = FastClient(handler);

            RenameResult result = await client.RenameDeviceAsync(TxMac, "Lobby-TX1");

            Assert.False(result.Ok);
            Assert.Equal("Device is not connected", result.Reason);
        }

        [Theory]
        [InlineData("", "Lobby-TX1")]
        [InlineData("   ", "Lobby-TX1")]
        [InlineData(TxMac, "")]
        [InlineData(TxMac, "   ")]
        public async Task Rename_EmptyNameOrMac_MakesNoCall(string mac, string name)
        {
            var handler = AcceptingServer();
            var client = FastClient(handler);

            RenameResult result = await client.RenameDeviceAsync(mac, name);

            Assert.False(result.Ok);
            Assert.False(string.IsNullOrWhiteSpace(result.Reason));
            Assert.Empty(handler.Calls);
        }
    }
}
