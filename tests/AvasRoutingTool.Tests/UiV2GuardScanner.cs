using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace AvasRoutingTool.Tests
{
    internal sealed record GuardViolation(string Rule, string Detail);

    /// <summary>
    /// The UI v2 rules for a view file, applied to its XAML text (comments ignored, character references decoded):
    /// no ControlTemplate; no keyed Style; an inline Style needs a BasedOn; no numeric Width/Height/Min*/Max* (a size comes from a
    /// <c>Size.*</c> token, or the element is an allowlisted static icon); no FontSize literal and nothing below 12 (a role or a
    /// <c>Font.Size.*</c> token); no bitmap, emoji or Unicode-glyph icon; every list is virtualized or allowlisted.
    /// </summary>
    internal static class UiV2GuardScanner
    {
        private static readonly Regex Comment = new("<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex CharRef = new("&#(?:x([0-9A-Fa-f]+)|([0-9]+));", RegexOptions.Compiled);
        private static readonly Regex Tag = new(@"<(?<name>[A-Za-z_][\w.:]*)(?<attrs>(?:\s[^<>]*?)?)\s*/?>", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex Attribute = new(@"(?<![\w.:])(?<name>[A-Za-z_][\w.:]*)\s*=\s*""(?<value>[^""]*)""", RegexOptions.Compiled);
        private static readonly Regex Number = new(@"^-?\d+(\.\d+)?$", RegexOptions.Compiled);
        private static readonly Regex ImageExtension = new(@"\.(png|jpe?g|bmp|gif|ico|tiff?|webp)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly HashSet<string> SizeProperties = new(StringComparer.Ordinal)
        {
            "Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight",
        };

        public static List<GuardViolation> Scan(string file, string xaml, UiV2Allowlist allow)
        {
            var violations = new List<GuardViolation>();
            file = UiV2Allowlist.Normalize(file);
            var text = Decode(Comment.Replace(xaml, " "));

            foreach (Match tag in Tag.Matches(text))
            {
                var name = LocalName(tag.Groups["name"].Value);
                var attributes = Attribute.Matches(tag.Groups["attrs"].Value).ToDictionary(m => m.Groups["name"].Value, m => m.Groups["value"].Value, StringComparer.Ordinal);
                var tagText = Squash(tag.Value);

                if (name == "ControlTemplate")
                {
                    violations.Add(new GuardViolation("ControlTemplate", "a ControlTemplate in a view: move it to Resources/Styles/*"));
                }

                if (name == "Style")
                {
                    if (attributes.ContainsKey("x:Key"))
                    {
                        violations.Add(new GuardViolation("KeyedStyle", $"keyed Style {attributes["x:Key"]} in a view: move it to Resources/Styles/*"));
                    }
                    else if (!attributes.ContainsKey("BasedOn") && !Allowed(allow.InlineStyleExceptions, file, tagText))
                    {
                        violations.Add(new GuardViolation("InlineStyleWithoutBasedOn", $"inline {tagText}: base it on a central style or allowlist it"));
                    }
                }

                if (name is "ListBox" or "ListView" or "DataGrid" && !IsVirtualized(attributes) && !Allowed(allow.UnvirtualizedLists, file, tagText))
                {
                    violations.Add(new GuardViolation("UnvirtualizedList", $"{name} without Style.ListBox.Virtualized (or the three virtualizing properties): {Shorten(tagText)}"));
                }

                if (name is "ImageBrush" or "BitmapImage")
                {
                    violations.Add(new GuardViolation("BitmapIcon", $"{name} in a view: icons are vectors"));
                }
                if (name == "Image" && !(attributes.TryGetValue("Source", out var source) && source.StartsWith("{") && source.Contains("PreviewBitmap")))
                {
                    violations.Add(new GuardViolation("BitmapIcon", $"Image that is not the live PreviewBitmap: {Shorten(tagText)}"));
                }
                foreach (var (attribute, value) in attributes)
                {
                    if (name == "Image" && attribute == "Source") continue; // judged above
                    if (ImageExtension.IsMatch(value) && !(attribute == "Icon" && value.EndsWith("Assets/AppIcon.ico", StringComparison.OrdinalIgnoreCase)))
                    {
                        violations.Add(new GuardViolation("BitmapIcon", $"{attribute}=\"{value}\": icons are vectors (only Assets/AppIcon.ico is exempt)"));
                    }
                }

                CheckSizeAndFont(name, attributes, tagText, file, allow, violations);
            }

            foreach (var line in GlyphLines(text))
            {
                violations.Add(new GuardViolation("GlyphIcon", "emoji or Unicode glyph used as an icon: " + line));
            }
            return violations;
        }

        private static void CheckSizeAndFont(string name, Dictionary<string, string> attributes, string tagText, string file, UiV2Allowlist allow, List<GuardViolation> violations)
        {
            var pairs = attributes.Select(a => (Property: a.Key, Value: a.Value)).ToList();
            if (name == "Setter" && attributes.TryGetValue("Property", out var property) && attributes.TryGetValue("Value", out var setterValue))
            {
                pairs.Add((property, setterValue));
            }

            foreach (var (propertyName, value) in pairs)
            {
                var simple = propertyName.StartsWith("TextElement.", StringComparison.Ordinal) ? propertyName["TextElement.".Length..]
                    : propertyName.Contains('.') ? "" : propertyName; // Grid.Row, Canvas.Left ... are not sizes
                if (SizeProperties.Contains(simple))
                {
                    var literal = Number.IsMatch(value);
                    var foreignToken = (value.StartsWith("{StaticResource", StringComparison.Ordinal) || value.StartsWith("{DynamicResource", StringComparison.Ordinal)) &&
                                       !Regex.IsMatch(value, @"^\{(Static|Dynamic)Resource\s+Size\.");
                    if ((literal || foreignToken) && !Allowed(allow.StaticIcons, file, tagText))
                    {
                        violations.Add(new GuardViolation("NumericSize", $"{simple}=\"{value}\" on {Shorten(tagText)}: use a Size.* token, * / Auto, or allowlist a static icon"));
                    }
                }
                else if (simple == "FontSize" && Number.IsMatch(value))
                {
                    violations.Add(new GuardViolation("FontSizeLiteral", $"FontSize=\"{value}\" on {Shorten(tagText)}: use a text role or a Font.Size.* token"));
                    if (double.Parse(value, CultureInfo.InvariantCulture) < 12)
                    {
                        violations.Add(new GuardViolation("FontBelow12", $"FontSize=\"{value}\" is below the 12 px floor (D8)"));
                    }
                }
            }
        }

        private static bool IsVirtualized(Dictionary<string, string> attributes)
        {
            if (attributes.TryGetValue("Style", out var style) && style.Contains("Style.ListBox.Virtualized")) return true;

            bool Has(string property, string value) =>
                (attributes.TryGetValue("VirtualizingPanel." + property, out var v) || attributes.TryGetValue("VirtualizingStackPanel." + property, out v)) &&
                string.Equals(v, value, StringComparison.OrdinalIgnoreCase);
            return Has("IsVirtualizing", "True") && Has("VirtualizationMode", "Recycling") && Has("ScrollUnit", "Pixel");
        }

        private static bool Allowed(List<AllowEntry> entries, string file, string tagText) =>
            entries.Any(e => e.File.Equals(file, StringComparison.OrdinalIgnoreCase) && e.Contains.Length > 0 && tagText.Contains(Squash(e.Contains), StringComparison.Ordinal));

        /// <summary>Lines carrying an emoji or a Unicode symbol/arrow/shape/dingbat (U+2190-27BF, U+2B00-2BFF, anything astral).</summary>
        private static IEnumerable<string> GlyphLines(string text)
        {
            foreach (var line in text.Split('\n'))
            {
                for (var i = 0; i < line.Length; i++)
                {
                    var code = char.IsHighSurrogate(line[i]) && i + 1 < line.Length ? char.ConvertToUtf32(line[i], line[i + 1]) : line[i];
                    if (code > 0xFFFF) i++;
                    if (code is >= 0x2190 and <= 0x27BF or >= 0x2B00 and <= 0x2BFF or > 0xFFFF)
                    {
                        yield return $"U+{code:X4} in \"{Shorten(Squash(line))}\"";
                        break;
                    }
                }
            }
        }

        private static string Decode(string text) =>
            CharRef.Replace(text, m =>
            {
                var code = m.Groups[1].Success ? int.Parse(m.Groups[1].Value, NumberStyles.HexNumber) : int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                return code is > 0 and <= 0x10FFFF && !(code is >= 0xD800 and <= 0xDFFF) ? char.ConvertFromUtf32(code) : "";
            });

        private static string LocalName(string name) => name.Contains(':') ? name[(name.LastIndexOf(':') + 1)..] : name;

        private static string Squash(string text) => Regex.Replace(text, @"\s+", " ").Trim();

        private static string Shorten(string text) => text.Length <= 110 ? text : text[..107] + "...";
    }
}
