using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AvasRoutingTool.Sdvoe.MultiView
{
    /// <summary>One detailed timing descriptor from an EDID.</summary>
    public sealed record EdidTiming(int Width, int Height, double RefreshHz, bool Interlaced);

    /// <summary>A video mode a display advertises. Refresh is the nominal rate in Hz (59.94 is 60).</summary>
    public sealed record EdidMode(int Width, int Height, int RefreshHz, bool Interlaced);

    /// <summary>
    /// The parts of a display's EDID the router needs: who it is and which video timings it asks for.
    /// Reads the base block and CTA-861 extension blocks (EDID 1.3/1.4).
    /// </summary>
    public sealed class EdidInfo
    {
        private EdidInfo(string manufacturerId, string monitorName, bool checksumValid, IReadOnlyList<EdidTiming> timings,
            IReadOnlyList<EdidMode> supportedModes, EdidMode? native)
        {
            ManufacturerId = manufacturerId;
            MonitorName = monitorName;
            ChecksumValid = checksumValid;
            Timings = timings;
            SupportedModes = supportedModes;
            Native = native;
        }

        /// <summary>
        /// Every mode the display advertises (progressive and interlaced): the native mode first, then the
        /// other detailed timings, then the CTA-861 Video Data Block modes, without duplicates.
        /// </summary>
        public IReadOnlyList<EdidMode> SupportedModes { get; }

        /// <summary>The first progressive detailed timing of the base block, i.e. the display's preferred mode.</summary>
        public EdidMode? Native { get; }

        /// <summary>True when the display advertises this progressive mode (refresh compared rounded, so 59.94 counts as 60).</summary>
        public bool Supports(int width, int height, int refreshHz) =>
            SupportedModes.Any(m => !m.Interlaced && m.Width == width && m.Height == height && m.RefreshHz == refreshHz);

        public string ManufacturerId { get; }
        public string MonitorName { get; }

        /// <summary>True when every 128-byte block sums to zero modulo 256.</summary>
        public bool ChecksumValid { get; }

        /// <summary>Detailed timings in EDID order. The first one is the display's preferred (native) timing.</summary>
        public IReadOnlyList<EdidTiming> Timings { get; }

        public EdidTiming? Preferred => Timings.Count > 0 ? Timings[0] : null;

        /// <summary>Parses an EDID given as a hex string (whitespace allowed). Returns null if it is not an EDID.</summary>
        public static EdidInfo? Parse(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;

            string clean = new string(hex.Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (clean.Length < 256 || clean.Length % 2 != 0) return null;

            byte[] data;
            try { data = Convert.FromHexString(clean); }
            catch (FormatException) { return null; }

            if (data.Length < 128 || data.Length % 128 != 0) return null;
            byte[] header = { 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00 };
            if (!data.Take(8).SequenceEqual(header)) return null;

            bool checksumValid = true;
            for (int block = 0; block < data.Length / 128; block++)
            {
                int sum = 0;
                for (int i = 0; i < 128; i++) sum += data[block * 128 + i];
                if (sum % 256 != 0) checksumValid = false;
            }

            int v = (data[8] << 8) | data[9];
            string manufacturer = new string(new[]
            {
                (char)(((v >> 10) & 0x1F) + 64),
                (char)(((v >> 5) & 0x1F) + 64),
                (char)((v & 0x1F) + 64),
            });

            var timings = new List<EdidTiming>();
            string name = string.Empty;

            for (int o = 54; o + 18 <= 126; o += 18)
            {
                if (data[o] != 0 || data[o + 1] != 0)
                {
                    AddTiming(timings, data, o);
                }
                else if (data[o + 2] == 0 && data[o + 3] == 0xFC)
                {
                    name = Encoding.ASCII.GetString(data, o + 5, 13).Split('\n')[0].Trim();
                }
            }

            int baseTimingCount = timings.Count;
            var vicModes = new List<EdidMode>();

            for (int block = 1; block < data.Length / 128; block++)
            {
                int start = block * 128;
                if (data[start] != 0x02) continue; // CTA-861 extension

                int dtdOffset = data[start + 2];
                if (dtdOffset < 4) continue;

                // Data block collection runs from byte 4 up to the first DTD. Whatever is cut off or
                // malformed is ignored rather than thrown on.
                int blocksEnd = start + Math.Min(dtdOffset, 127);
                for (int p = start + 4; p < blocksEnd;)
                {
                    int tag = data[p] >> 5;
                    int len = data[p] & 0x1F;
                    if (p + 1 + len > blocksEnd) break;
                    if (tag == 2) // Video Data Block
                    {
                        for (int i = 0; i < len; i++)
                        {
                            int code = data[p + 1 + i];
                            int vic = code >= 129 && code <= 192 ? code & 0x7F : code;
                            if (CtaVic.TryGet(vic, out var vicMode)) vicModes.Add(vicMode);
                        }
                    }
                    p += 1 + len;
                }

                for (int o = start + dtdOffset; o + 18 <= start + 127; o += 18)
                {
                    if (data[o] == 0 && data[o + 1] == 0) break;
                    AddTiming(timings, data, o);
                }
            }

            return new EdidInfo(manufacturer, name, checksumValid, timings, BuildModes(timings, baseTimingCount, vicModes, out var native), native);
        }

        /// <summary>Preferred (native) mode first, then the other DTDs, then the CTA VIC modes; no repeats.</summary>
        private static IReadOnlyList<EdidMode> BuildModes(List<EdidTiming> timings, int baseTimingCount, List<EdidMode> vicModes, out EdidMode? native)
        {
            native = null;
            for (int i = 0; i < baseTimingCount; i++)
            {
                if (!timings[i].Interlaced) { native = ToMode(timings[i]); break; }
            }

            var modes = new List<EdidMode>();
            var seen = new HashSet<EdidMode>();
            void Add(EdidMode m) { if (seen.Add(m)) modes.Add(m); }

            if (native != null) Add(native);
            foreach (var t in timings) Add(ToMode(t));
            foreach (var m in vicModes) Add(m);
            return modes;
        }

        // An interlaced DTD describes one field: half the picture height at the field rate, which is
        // the rate the mode is named by (1080i60 = 540 lines per field, 60 fields per second).
        private static EdidMode ToMode(EdidTiming t) =>
            new EdidMode(t.Width, t.Interlaced ? t.Height * 2 : t.Height, (int)Math.Round(t.RefreshHz), t.Interlaced);

        private static void AddTiming(List<EdidTiming> timings, byte[] d, int o)
        {
            long pixelClockHz = (d[o] | (d[o + 1] << 8)) * 10_000L;
            int hActive = d[o + 2] | ((d[o + 4] & 0xF0) << 4);
            int hBlank = d[o + 3] | ((d[o + 4] & 0x0F) << 8);
            int vActive = d[o + 5] | ((d[o + 7] & 0xF0) << 4);
            int vBlank = d[o + 6] | ((d[o + 7] & 0x0F) << 8);
            bool interlaced = (d[o + 17] & 0x80) != 0;

            long total = (long)(hActive + hBlank) * (vActive + vBlank);
            if (hActive == 0 || vActive == 0 || total == 0) return;

            timings.Add(new EdidTiming(hActive, vActive, (double)pixelClockHz / total, interlaced));
        }
    }
}
