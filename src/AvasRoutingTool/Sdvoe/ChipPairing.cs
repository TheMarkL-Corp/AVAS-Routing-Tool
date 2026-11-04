using System;
using System.Collections.Generic;
using System.Linq;

namespace AvasRoutingTool.Sdvoe
{
    /// <summary>A transmitter as the operator sees it: its chip 0 and, when found, its chip 1 (a dual-link TX is two discovered chips).</summary>
    public sealed record ChipPair(AvasDevice Primary, AvasDevice? Companion);

    /// <summary>
    /// Groups the discovered AVAS transmitter chips into devices. The rule is the wrapper's (MultiLinkService): the chip 0's own
    /// list of companions wins; otherwise a chip 1 whose MAC is next to the chip 0's, or whose name differs only by SFPA/SFPB.
    /// A chip 1 belongs to one chip 0 only. Receivers and non-AVAS devices are never paired.
    /// </summary>
    public static class ChipPairing
    {
        /// <param name="declaredCompanions">Chip 0 MAC to the chip 1 MAC that chip 0 reports as its companion (normalized MACs), if read.</param>
        public static IReadOnlyList<ChipPair> Build(IEnumerable<AvasDevice> devices, IReadOnlyDictionary<string, string>? declaredCompanions = null)
        {
            var list = devices.ToList();
            var chip1s = list.Where(d => IsAvasTx(d) && d.ChipId == 1).ToList();
            var companionOf = new Dictionary<AvasDevice, AvasDevice>();
            var used = new HashSet<AvasDevice>();

            foreach (var primary in list.Where(d => IsAvasTx(d) && d.ChipId == 0))
            {
                string mac = SdvoeClient.NormalizeMac(primary.DeviceId);
                AvasDevice? companion = null;

                if (declaredCompanions != null && declaredCompanions.TryGetValue(mac, out var declared))
                    companion = chip1s.FirstOrDefault(c => !used.Contains(c) && SdvoeClient.NormalizeMac(c.DeviceId) == SdvoeClient.NormalizeMac(declared));

                companion ??= chip1s.FirstOrDefault(c => !used.Contains(c) &&
                    (IsConsecutiveMac(primary.DeviceId, c.DeviceId) || ArePairedNames(primary.DeviceName, c.DeviceName)));

                if (companion != null)
                {
                    companionOf[primary] = companion;
                    used.Add(companion);
                }
            }

            var result = new List<ChipPair>();
            foreach (var d in list)
            {
                if (used.Contains(d)) continue;
                result.Add(new ChipPair(d, companionOf.GetValueOrDefault(d)));
            }
            return result;
        }

        private static bool IsAvasTx(AvasDevice d) => d.IsAvasDevice && d.IsTransmitter;

        internal static bool IsConsecutiveMac(string a, string b)
        {
            a = SdvoeClient.NormalizeMac(a);
            b = SdvoeClient.NormalizeMac(b);
            if (a.Length != 12 || b.Length != 12) return false;
            return long.TryParse(a, System.Globalization.NumberStyles.HexNumber, null, out long x) &&
                   long.TryParse(b, System.Globalization.NumberStyles.HexNumber, null, out long y) &&
                   Math.Abs(x - y) == 1;
        }

        internal static bool ArePairedNames(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            static string Strip(string s) => s.Replace("SFPA", "", StringComparison.OrdinalIgnoreCase).Replace("SFPB", "", StringComparison.OrdinalIgnoreCase).Trim();
            return string.Equals(Strip(a), Strip(b), StringComparison.OrdinalIgnoreCase);
        }
    }
}
