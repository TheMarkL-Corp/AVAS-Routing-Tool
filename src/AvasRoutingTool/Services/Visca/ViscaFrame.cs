using System;
using System.Buffers.Binary;

namespace AvasRoutingTool.Services.Visca
{
    /// <summary>VISCA-over-IP payload types (the first two header bytes, big-endian).</summary>
    public enum ViscaPayloadType : ushort
    {
        Command = 0x0100,
        Inquiry = 0x0110,
        Reply = 0x0111,
        Control = 0x0200,
        ControlReply = 0x0201
    }

    /// <summary>
    /// VISCA-over-IP UDP framing: an 8-byte header (payload type, payload length,
    /// sequence number; all big-endian) followed by the VISCA bytes. Pure functions.
    /// </summary>
    public static class ViscaFrame
    {
        public const int HeaderLength = 8;

        // Payload of a control command that resets the sequence number.
        private const byte SequenceResetPayload = 0x01;

        public static byte[] Encode(ViscaPayloadType type, uint sequence, byte[] visca)
        {
            ArgumentNullException.ThrowIfNull(visca);
            if (visca.Length > ushort.MaxValue)
                throw new ArgumentException("VISCA payload exceeds the 16-bit length field.", nameof(visca));

            var frame = new byte[HeaderLength + visca.Length];
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(0, 2), (ushort)type);
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2, 2), (ushort)visca.Length);
            BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(4, 4), sequence);
            visca.CopyTo(frame, HeaderLength);
            return frame;
        }

        /// <summary>
        /// Splits a datagram into header and VISCA bytes. Returns false (never throws)
        /// for a short datagram, an unknown payload type, or a length field that does
        /// not match the bytes actually received.
        /// </summary>
        public static bool TryDecode(ReadOnlySpan<byte> datagram, out ViscaPayloadType type, out uint sequence, out byte[] visca)
        {
            type = default;
            sequence = 0;
            visca = Array.Empty<byte>();

            if (datagram.Length < HeaderLength)
                return false;

            ushort rawType = BinaryPrimitives.ReadUInt16BigEndian(datagram[0..2]);
            if (!Enum.IsDefined(typeof(ViscaPayloadType), rawType))
                return false;

            ushort payloadLength = BinaryPrimitives.ReadUInt16BigEndian(datagram[2..4]);
            if (datagram.Length != HeaderLength + payloadLength)
                return false;

            type = (ViscaPayloadType)rawType;
            sequence = BinaryPrimitives.ReadUInt32BigEndian(datagram[4..8]);
            visca = datagram[HeaderLength..].ToArray();
            return true;
        }

        /// <summary>Control command that resets the camera's sequence number: 02 00 00 01 00 00 00 00 01.</summary>
        public static byte[] SequenceReset()
            => Encode(ViscaPayloadType.Control, 0, new[] { SequenceResetPayload });
    }
}
