using System;
using System.Globalization;
using AvasRoutingTool.Services.Visca;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class ViscaFrameTests
    {
        private static byte[] Hex(string hex)
        {
            var parts = hex.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var bytes = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                bytes[i] = byte.Parse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return bytes;
        }

        [Fact]
        public void Encode_CommandWithSequence1_WritesHeaderThenVisca()
        {
            var frame = ViscaFrame.Encode(ViscaPayloadType.Command, 1, Hex("81 01 06 04 FF"));

            Assert.Equal(Hex("01 00 00 05 00 00 00 01 81 01 06 04 FF"), frame);
        }

        [Fact]
        public void Encode_SequenceAndLengthAreBigEndian()
        {
            var frame = ViscaFrame.Encode(ViscaPayloadType.Inquiry, 0x01020304, Hex("81 09 04 00 FF"));

            Assert.Equal(Hex("01 10 00 05 01 02 03 04"), frame.AsSpan(0, 8).ToArray());
        }

        [Fact]
        public void TryDecode_RoundTrip_ReturnsTypeSequenceAndVisca()
        {
            var frame = ViscaFrame.Encode(ViscaPayloadType.Command, 77, Hex("81 01 04 00 02 FF"));

            bool ok = ViscaFrame.TryDecode(frame, out var type, out var sequence, out var visca);

            Assert.True(ok);
            Assert.Equal(ViscaPayloadType.Command, type);
            Assert.Equal(77u, sequence);
            Assert.Equal(Hex("81 01 04 00 02 FF"), visca);
        }

        [Fact]
        public void TryDecode_ShortDatagram_ReturnsFalse()
        {
            bool ok = ViscaFrame.TryDecode(Hex("01 00 00 05 00 00 00"), out _, out _, out _);

            Assert.False(ok);
        }

        [Fact]
        public void TryDecode_LengthFieldShorterThanPayload_ReturnsFalse()
        {
            bool ok = ViscaFrame.TryDecode(Hex("01 00 00 04 00 00 00 01 81 01 06 04 FF"), out _, out _, out _);

            Assert.False(ok);
        }

        [Fact]
        public void TryDecode_LengthFieldLongerThanPayload_ReturnsFalse()
        {
            bool ok = ViscaFrame.TryDecode(Hex("01 00 00 05 00 00 00 01 81 01 06 04"), out _, out _, out _);

            Assert.False(ok);
        }

        [Fact]
        public void TryDecode_UnknownPayloadType_ReturnsFalse()
        {
            bool ok = ViscaFrame.TryDecode(Hex("01 23 00 05 00 00 00 01 81 01 06 04 FF"), out _, out _, out _);

            Assert.False(ok);
        }

        [Fact]
        public void SequenceReset_IsControlPacketWithPayload01()
        {
            Assert.Equal(Hex("02 00 00 01 00 00 00 00 01"), ViscaFrame.SequenceReset());
        }
    }
}
