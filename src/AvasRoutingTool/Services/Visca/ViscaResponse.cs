using System;
using System.Collections.Generic;

namespace AvasRoutingTool.Services.Visca
{
    public enum ViscaResponseKind { Ack, Completion, InquiryReply, Error }

    /// <summary>
    /// A parsed camera reply. Socket is the low nibble of the reply's second byte.
    /// Data holds inquiry bytes (empty otherwise); ErrorCode is set only for Error.
    /// </summary>
    public record ViscaResponse(ViscaResponseKind Kind, int Socket, byte[] Data, int? ErrorCode)
    {
        private const byte ReplyHeaderMask = 0xF0;
        private const byte ReplyHeader = 0x90;
        private const byte AckNibble = 0x40;
        private const byte CompletionNibble = 0x50;
        private const byte ErrorNibble = 0x60;

        /// <summary>
        /// Parses one complete reply (including its FF terminator). Returns null for
        /// anything that is not a well-formed ACK, completion, inquiry reply or error.
        /// </summary>
        public static ViscaResponse? TryParse(ReadOnlySpan<byte> message)
        {
            if (message.Length < 3 || message[^1] != ViscaCommands.Terminator)
                return null;
            if ((message[0] & ReplyHeaderMask) != ReplyHeader)
                return null;

            int socket = message[1] & 0x0F;
            switch (message[1] & ReplyHeaderMask)
            {
                case AckNibble:
                    return message.Length == 3
                        ? new ViscaResponse(ViscaResponseKind.Ack, socket, Array.Empty<byte>(), null)
                        : null;

                case CompletionNibble:
                    if (message.Length == 3)
                        return new ViscaResponse(ViscaResponseKind.Completion, socket, Array.Empty<byte>(), null);
                    return new ViscaResponse(ViscaResponseKind.InquiryReply, socket, message[2..^1].ToArray(), null);

                case ErrorNibble:
                    return message.Length == 4
                        ? new ViscaResponse(ViscaResponseKind.Error, socket, Array.Empty<byte>(), message[2])
                        : null;

                default:
                    return null;
            }
        }
    }

    /// <summary>
    /// Re-assembles VISCA messages from a TCP byte stream (or any byte source) on the
    /// FF terminator. Each yielded message includes its terminator. A message longer
    /// than 256 bytes is dropped and the reader skips to the next terminator, so the
    /// buffer never grows without bound.
    /// </summary>
    public sealed class ViscaStreamReader
    {
        public const int MaxMessageLength = 256;

        private readonly List<byte> _buffer = new();
        private bool _discarding;

        public IEnumerable<byte[]> Feed(ReadOnlySpan<byte> chunk)
        {
            var messages = new List<byte[]>();
            foreach (byte b in chunk)
            {
                if (_discarding)
                {
                    if (b == ViscaCommands.Terminator)
                        _discarding = false;
                    continue;
                }

                if (b == ViscaCommands.Terminator)
                {
                    if (_buffer.Count > 0)
                    {
                        _buffer.Add(b);
                        messages.Add(_buffer.ToArray());
                        _buffer.Clear();
                    }
                    continue;
                }

                // Keep room for the terminator so a complete message stays within the cap.
                if (_buffer.Count >= MaxMessageLength - 1)
                {
                    _buffer.Clear();
                    _discarding = true;
                    continue;
                }

                _buffer.Add(b);
            }
            return messages;
        }
    }
}
