using System;

namespace AvasRoutingTool.Rtp
{
    /// <summary>
    /// Measures what the sender actually transmits, independent of what the app manages to reassemble:
    /// sender frame rate from RTP timestamps (90 kHz), packet loss from sequence gaps, frame size and rate of
    /// packets. Used to tell a sender-side frame-rate limit from a receiver-side loss problem.
    /// Feed every parsed RTP packet to <see cref="Observe"/>; once per window it returns a one-line summary.
    /// </summary>
    public sealed class RtpStreamDiagnostics
    {
        public const double RtpClockHz = 90000.0;

        private readonly long _windowMs;
        private readonly object _lock = new();

        private long _windowStartMs = -1;
        private bool _hasSeq;
        private ushort _lastSeq;
        private bool _hasTs;
        private uint _lastTs;

        private int _packets;
        private long _bytes;
        private int _lostPackets;
        private int _reorderedPackets;
        private int _markers;
        private int _timestampChanges;
        private long _timestampDeltaSum;
        private uint _minDelta = uint.MaxValue;
        private uint _maxDelta;
        private int _maxLineNo;
        private int _packetsInFrame;
        private int _maxPacketsPerFrame;
        private int _payloadLength;

        public RtpStreamDiagnostics(int windowMs = 1000)
        {
            _windowMs = windowMs;
        }

        /// <summary>
        /// Records one packet. Returns a summary line when the window has elapsed, otherwise null.
        /// <paramref name="nowMs"/> is a monotonic millisecond clock.
        /// </summary>
        public string? Observe(ushort seq, uint timestamp, bool marker, int lineNo, int payloadLength, int datagramBytes, long nowMs, int framesCompleted)
        {
            lock (_lock)
            {
                if (_windowStartMs < 0) _windowStartMs = nowMs;

                _packets++;
                _bytes += datagramBytes;
                _payloadLength = payloadLength;
                if (lineNo > _maxLineNo) _maxLineNo = lineNo;

                if (_hasSeq)
                {
                    short gap = (short)(seq - (ushort)(_lastSeq + 1));
                    if (gap > 0) _lostPackets += gap;
                    else if (gap < 0) _reorderedPackets++;
                }
                _lastSeq = seq;
                _hasSeq = true;

                if (_hasTs && timestamp != _lastTs)
                {
                    uint delta = unchecked(timestamp - _lastTs);
                    _timestampChanges++;
                    _timestampDeltaSum += delta;
                    if (delta < _minDelta) _minDelta = delta;
                    if (delta > _maxDelta) _maxDelta = delta;
                    if (_packetsInFrame > _maxPacketsPerFrame) _maxPacketsPerFrame = _packetsInFrame;
                    _packetsInFrame = 0;
                }
                _lastTs = timestamp;
                _hasTs = true;
                _packetsInFrame++;

                if (marker) _markers++;

                if (nowMs - _windowStartMs < _windowMs) return null;

                string line = BuildSummary(nowMs - _windowStartMs, framesCompleted);
                ResetWindow(nowMs);
                return line;
            }
        }

        private string BuildSummary(long elapsedMs, int framesCompleted)
        {
            double seconds = Math.Max(elapsedMs, 1) / 1000.0;
            double avgDelta = _timestampChanges > 0 ? (double)_timestampDeltaSum / _timestampChanges : 0;
            double senderFps = avgDelta > 0 ? RtpClockHz / avgDelta : 0;
            int total = _packets + _lostPackets;
            double lossPct = total > 0 ? 100.0 * _lostPackets / total : 0;
            double mbps = _bytes * 8 / seconds / 1e6;
            int maxPackets = Math.Max(_maxPacketsPerFrame, _packetsInFrame);

            return $"sender≈{senderFps:F1}fps (ts delta avg {avgDelta:F0} min {(_minDelta == uint.MaxValue ? 0 : _minDelta)} max {_maxDelta} ticks, " +
                   $"{_timestampChanges} frame boundaries) | marker frames {_markers} | app completed {framesCompleted} (total since start) | " +
                   $"{_packets / seconds:F0} pkt/s {mbps:F1} Mbit/s | lost {_lostPackets} ({lossPct:F2}%) reordered {_reorderedPackets} | " +
                   $"lines/frame {_maxLineNo + 1} pkts/frame {maxPackets} payload {_payloadLength} B";
        }

        private void ResetWindow(long nowMs)
        {
            _windowStartMs = nowMs;
            _packets = 0;
            _bytes = 0;
            _lostPackets = 0;
            _reorderedPackets = 0;
            _markers = 0;
            _timestampChanges = 0;
            _timestampDeltaSum = 0;
            _minDelta = uint.MaxValue;
            _maxDelta = 0;
            _maxLineNo = 0;
            _maxPacketsPerFrame = 0;
            // _packetsInFrame carries over: the frame in progress continues into the next window.
        }
    }
}
