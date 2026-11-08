using System;
using System.Globalization;
using System.Linq;
using AvasRoutingTool.Services.Visca;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class ViscaResponseTests
    {
        private static byte[] Hex(string hex)
        {
            var parts = hex.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var bytes = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                bytes[i] = byte.Parse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return bytes;
        }

        // ---- Reply parsing: 90 4y FF ACK, 90 5y FF completion, 90 6y cc FF error ----

        [Fact]
        public void Ack_90_41_FF_IsAckOnSocket1()
        {
            var response = ViscaResponse.TryParse(Hex("90 41 FF"));

            Assert.NotNull(response);
            Assert.Equal(ViscaResponseKind.Ack, response!.Kind);
            Assert.Equal(1, response.Socket);
            Assert.Empty(response.Data);
            Assert.Null(response.ErrorCode);
        }

        [Fact]
        public void Completion_90_51_FF_IsCompletionOnSocket1()
        {
            var response = ViscaResponse.TryParse(Hex("90 51 FF"));

            Assert.NotNull(response);
            Assert.Equal(ViscaResponseKind.Completion, response!.Kind);
            Assert.Equal(1, response.Socket);
            Assert.Empty(response.Data);
            Assert.Null(response.ErrorCode);
        }

        [Fact]
        public void Error_90_60_02_FF_IsSyntaxErrorCode2()
        {
            var response = ViscaResponse.TryParse(Hex("90 60 02 FF"));

            Assert.NotNull(response);
            Assert.Equal(ViscaResponseKind.Error, response!.Kind);
            Assert.Equal(0, response.Socket);
            Assert.Equal(0x02, response.ErrorCode);
        }

        [Fact]
        public void InquiryReply_90_50_02_FF_CarriesDataByte02()
        {
            var response = ViscaResponse.TryParse(Hex("90 50 02 FF"));

            Assert.NotNull(response);
            Assert.Equal(ViscaResponseKind.InquiryReply, response!.Kind);
            Assert.Equal(0, response.Socket);
            Assert.Equal(new byte[] { 0x02 }, response.Data);
            Assert.Null(response.ErrorCode);
        }

        [Theory]
        [InlineData("")]
        [InlineData("90 41")]           // no terminator
        [InlineData("A0 41 FF")]        // not a reply header
        [InlineData("90 71 FF")]        // unknown reply type nibble
        [InlineData("90 61 FF")]        // error without its code byte
        [InlineData("90 41 02 FF")]     // ACK with trailing data
        public void TryParse_MalformedMessage_ReturnsNull(string hex)
        {
            Assert.Null(ViscaResponse.TryParse(Hex(hex)));
        }

        // ---- Stream re-assembly on the FF terminator ----

        [Fact]
        public void Feed_ReplySplitAcrossTwoChunks_YieldsOneMessage()
        {
            var reader = new ViscaStreamReader();

            Assert.Empty(reader.Feed(Hex("90 41")));
            var messages = reader.Feed(Hex("FF")).ToList();

            Assert.Single(messages);
            Assert.Equal(Hex("90 41 FF"), messages[0]);
        }

        [Fact]
        public void Feed_TwoRepliesInOneChunk_YieldsTwoMessages()
        {
            var reader = new ViscaStreamReader();

            var messages = reader.Feed(Hex("90 41 FF 90 51 FF")).ToList();

            Assert.Equal(2, messages.Count);
            Assert.Equal(Hex("90 41 FF"), messages[0]);
            Assert.Equal(Hex("90 51 FF"), messages[1]);
        }

        [Fact]
        public void Feed_GarbageWithoutTerminator_IsDroppedAndBufferStaysBounded()
        {
            var reader = new ViscaStreamReader();
            var garbage = Enumerable.Repeat((byte)0x11, 1000).ToArray();

            Assert.Empty(reader.Feed(garbage));

            // The next terminator ends the junk; the reply after it is intact.
            var messages = reader.Feed(Hex("FF 90 41 FF")).ToList();

            Assert.Single(messages);
            Assert.Equal(Hex("90 41 FF"), messages[0]);
        }
    }
}
