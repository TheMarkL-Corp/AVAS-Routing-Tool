using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AvasRoutingTool.Sdvoe.MultiView
{
    /// <summary>One detailed timing descriptor from an EDID.</summary>
    public sealed record EdidTiming(int Width, int Height, double RefreshHz, bool Interlaced);

    /// <summary>
    /// The parts of a display's EDID the router needs: who it is and which video timings it asks for.
    /// Reads the base block and CTA-861 extension blocks (EDID 1.3/1.4).
    /// </summary>
    public sealed class EdidInfo
    {
        private EdidInfo(string manufacturerId, string monitorName, bool checksumValid, IReadOnlyList<EdidTiming> timings)
        {
            ManufacturerId = manufacturerId;
            MonitorName = monitorName;
            ChecksumValid = checksumValid;
            Timings = timings;
        }

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

            for (int block = 1; block < data.Length / 128; block++)
            {
                int start = block * 128;
                if (data[start] != 0x02) continue; // CTA-861 extension

                int dtdOffset = data[start + 2];
                if (dtdOffset < 4) continue;
                for (int o = start + dtdOffset; o + 18 <= start + 127; o += 18)
                {
                    if (data[o] == 0 && data[o + 1] == 0) break;
                    AddTiming(timings, data, o);
                }
            }

            return new EdidInfo(manufacturer, name, checksumValid, timings);
        }

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
