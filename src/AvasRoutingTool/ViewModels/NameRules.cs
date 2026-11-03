using System;
using System.Collections.Generic;

namespace AvasRoutingTool.ViewModels
{
    /// <summary>
    /// What a device name typed in the rename dialog must satisfy. <see cref="Validate"/> returns null for a valid
    /// name, otherwise one short sentence the dialog shows under the field.
    /// </summary>
    public static class NameRules
    {
        /// <summary>
        /// The longest name the Control Server accepts (API 3.9 Table 168). A 20-character name was rejected live with
        /// "Device name too long, must be 19 characters at most". The plan's 32 is wrong; the server limit wins.
        /// </summary>
        public const int MaxLength = 19;

        /// <summary>
        /// Checks <paramref name="name"/> in a fixed order and returns the first problem, or null when it is valid.
        /// <paramref name="otherNames"/> are the names of the OTHER devices (the caller leaves the renamed device out).
        /// A name equal to <paramref name="currentName"/> is valid unless it is also in <paramref name="otherNames"/>.
        /// </summary>
        public static string? Validate(string name, string currentName, IEnumerable<string> otherNames)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Enter a name";

            if (name != name.Trim())
                return "No space at the start or end";

            if (name.Length > MaxLength)
                return "At most " + MaxLength + " characters (it has " + name.Length + ")";

            foreach (char c in name)
            {
                if (c < 0x20 || c > 0x7E)
                    return "Use plain letters, digits and symbols only";
            }

            if (name.Contains(':'))
                return "A colon (:) is not allowed";

            if (otherNames != null)
            {
                foreach (string? other in otherNames)
                {
                    if (string.IsNullOrEmpty(other))
                        continue;
                    if (string.Equals(name, other, StringComparison.OrdinalIgnoreCase))
                        return "Another device already has this name";
                }
            }

            return null;
        }
    }
}
