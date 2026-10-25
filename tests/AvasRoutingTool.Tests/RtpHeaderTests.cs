using System;
using System.Buffers.Binary;
using AvasRoutingTool.Rtp;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class RtpHeaderTests
    {
        [Fact]
        public void TryParse_WhenBufferTooShort_ReturnsFalse()
        {
            byte[] buffer = new byte[19];
            bool result = RtpHeader.TryParse(buffer, out _);
            Assert.False(result);
        }

        [Fact]
        public void TryParse_WhenVersionNotTwo_ReturnsFalse()
        {
            byte[] buffer = new byte[24];
            buffer[0] = 0x40; // Version 1
            bool result = RtpHeader.TryParse(buffer, out _);
            Assert.False(result);
        }

        [Fact]
        public void TryParse_ValidRfc4175Packet_ParsesCorrectly()
        {
            byte[] buffer = new byte[24];
            buffer[0] = 0x80; // V=2, P=0, X=0, CC=0
            buffer[1] = 0x60; // M=0, PT=96
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(2, 2), 1234); // Seq
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(4, 4), 56789); // Timestamp
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(8, 4), 0xABCDEF01); // SSRC

            // RFC 4175 Payload Header
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(12, 2), 0); // Extended Seq
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(14, 2), 4); // Length = 4 bytes payload
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(16, 2), 42); // LineNo = 42
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(18, 2), 0); // Offset = 0

            // 4 bytes payload
            buffer[20] = 0x11;
            buffer[21] = 0x22;
            buffer[22] = 0x33;
            buffer[23] = 0x44;

            bool success = RtpHeader.TryParse(buffer, out var header);
            Assert.True(success);
            Assert.Equal(2, header.Version);
            Assert.False(header.Marker);
            Assert.Equal(96, header.PayloadType);
            Assert.Equal(1234, header.SequenceNumber);
            Assert.Equal(56789u, header.Timestamp);
            Assert.Equal(0xABCDEF01u, header.Ssrc);
            Assert.Equal(4, header.Length);
            Assert.Equal(42, header.LineNo);
            Assert.Equal(0, header.Offset);
            Assert.Equal(4, header.Payload.Length);
            Assert.Equal(0x11, header.Payload[0]);
        }
    }
}
