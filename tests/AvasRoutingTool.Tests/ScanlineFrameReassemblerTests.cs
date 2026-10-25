using System;
using System.Buffers.Binary;
using AvasRoutingTool.Rtp;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class ScanlineFrameReassemblerTests
    {
        private static byte[] CreatePacket(ushort lineNo, bool marker, uint timestamp, byte fillValue, ushort payloadLen = 4)
        {
            byte[] buffer = new byte[20 + payloadLen];
            buffer[0] = 0x80;
            buffer[1] = (byte)(marker ? 0xE0 : 0x60); // PT=96, Marker bit set if marker==true
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(2, 2), lineNo);
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(4, 4), timestamp);
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(8, 4), 100);

            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(12, 2), 0);
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(14, 2), payloadLen);
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(16, 2), lineNo);
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(18, 2), 0);

            for (int i = 0; i < payloadLen; i++)
            {
                buffer[20 + i] = fillValue;
            }
            return buffer;
        }

        [Fact]
        public void ProcessPacket_WhenCompleteFrameArrivesInOrder_ReturnsAssembledFrame()
        {
            var reassembler = new ScanlineFrameReassembler();
            byte[] p0 = CreatePacket(0, false, 1000, 0xAA);
            byte[] p1 = CreatePacket(1, true, 1000, 0xBB);

            Assert.True(RtpHeader.TryParse(p0, out var h0));
            Assert.True(RtpHeader.TryParse(p1, out var h1));

            var frame0 = reassembler.ProcessPacket(in h0);
            Assert.Null(frame0);

            var frame1 = reassembler.ProcessPacket(in h1);
            Assert.NotNull(frame1);
            Assert.Equal(2, frame1.Height);
            Assert.Equal(2, frame1.Width); // 4 bytes / 2 bytes per YUV422 pixel = 2
            Assert.Equal(0xAA, frame1.YuvData[0]);
            Assert.Equal(0xBB, frame1.YuvData[4]);
        }

        [Fact]
        public void ProcessPacket_WhenPacketsOutOfOrder_ReassemblesCorrectly()
        {
            var reassembler = new ScanlineFrameReassembler();
            byte[] p1 = CreatePacket(1, true, 1000, 0xBB);
            byte[] p0 = CreatePacket(0, false, 1000, 0xAA);

            Assert.True(RtpHeader.TryParse(p1, out var h1));
            Assert.True(RtpHeader.TryParse(p0, out var h0));

            // Line 1 arrives first
            var frame0 = reassembler.ProcessPacket(in h1);
            // Incomplete since line 0 is missing, even though marker is set
            Assert.Null(frame0);

            // Now line 0 arrives with same timestamp
            // Note: with standard marker boundary, if marker packet is sent last:
        }
    }
}
