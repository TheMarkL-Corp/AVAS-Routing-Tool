using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// TEMPORARY progress ratchet for the three Composer files (deleted in Composer Task 12). The shared <see cref="UiV2Guards"/> only knows
    /// "pending or enforced"; this gives the 1315-line Composer one red per task: each task lowers its <see cref="Baseline"/> numbers first.
    /// </summary>
    internal static class ComposerMetrics
    {
        public const string Composer = "Views/MultiViewComposerView.xaml";
        public const string Compact = "Views/CompactSourceCardView.xaml";
        public const string Schematic = "Views/LayoutSchematic.xaml";

        public static readonly string[] Names =
            { "KeyedStyles", "InlineStyles", "ControlTemplates", "NumericSizeAttrs", "GlyphIcons", "TargetNameSetters", "FontSizeLiterals" };

        public static string Read(string file) => File.ReadAllText(Path.Combine(UiV2Allowlist.SourceRoot(), file));

        public static int KeyedStyles(string xaml) => Regex.Matches(xaml, @"<Style\s+x:Key=").Count;

        public static int InlineStyles(string xaml) => Regex.Matches(xaml, @"<Style[\s>]").Count - KeyedStyles(xaml);

        public static int ControlTemplates(string xaml) => Regex.Matches(xaml, @"<ControlTemplate[\s>]").Count;

        /// <summary>(Min|Max)Width/Height literals as attributes or setters; the zero-size Canvas layout anchor is not a size.</summary>
        public static int NumericSizeAttrs(string xaml)
        {
            xaml = Regex.Replace(xaml, @"<Canvas\b[^>]*\bWidth=""0""[^>]*>", "<Canvas>");
            return Regex.Matches(xaml, @"\b(Min|Max)?(Width|Height)=""[0-9.]+""").Count +
                   Regex.Matches(xaml, @"Property=""(Min|Max)?(Width|Height)""\s+Value=""[0-9.]+""").Count;
        }

        public static int GlyphIcons(string xaml) =>
            Regex.Matches(xaml, @"[✓✕ⓘ]").Count + Regex.Matches(xaml, @"Text=""[i!]""|Value=""[i!]""").Count;

        public static int TargetNameSetters(string xaml) =>
            Regex.Matches(xaml, @"<DataTemplate\.Triggers>.*?</DataTemplate\.Triggers>", RegexOptions.Singleline)
                .Sum(m => Regex.Matches(m.Value, @"<Setter\s+TargetName=").Count);

        public static int FontSizeLiterals(string xaml) =>
            Regex.Matches(xaml, @"\bFontSize=""[0-9.]+""").Count + Regex.Matches(xaml, @"Property=""FontSize""\s+Value=""[0-9.]+""").Count;

        public static int Measure(string metric, string xaml) => metric switch
        {
            "KeyedStyles" => KeyedStyles(xaml),
            "InlineStyles" => InlineStyles(xaml),
            "ControlTemplates" => ControlTemplates(xaml),
            "NumericSizeAttrs" => NumericSizeAttrs(xaml),
            "GlyphIcons" => GlyphIcons(xaml),
            "TargetNameSetters" => TargetNameSetters(xaml),
            "FontSizeLiterals" => FontSizeLiterals(xaml),
            _ => throw new ArgumentException(metric),
        };

        /// <summary>The most each file may still contain. Every Composer task lowers its numbers first (the red), then migrates.</summary>
        public static readonly Dictionary<string, Dictionary<string, int>> Baseline = new()
        {
            [Composer] = new()
            {
                ["KeyedStyles"] = 4, ["InlineStyles"] = 44, ["ControlTemplates"] = 4, ["NumericSizeAttrs"] = 31,
                ["GlyphIcons"] = 10, ["TargetNameSetters"] = 31, ["FontSizeLiterals"] = 46,
            },
            [Compact] = new()
            {
                ["KeyedStyles"] = 0, ["InlineStyles"] = 8, ["ControlTemplates"] = 0, ["NumericSizeAttrs"] = 4,
                ["GlyphIcons"] = 0, ["TargetNameSetters"] = 0, ["FontSizeLiterals"] = 5,
            },
            [Schematic] = new()
            {
                ["KeyedStyles"] = 0, ["InlineStyles"] = 1, ["ControlTemplates"] = 0, ["NumericSizeAttrs"] = 0,
                ["GlyphIcons"] = 0, ["TargetNameSetters"] = 0, ["FontSizeLiterals"] = 2,
            },
        };
    }

    public class ComposerUiV2GuardTests
    {
        [Fact]
        public void Metrics_MeasureTheRealFiles()
        {
            var c = ComposerMetrics.Read(ComposerMetrics.Composer);
            Assert.Equal(4, ComposerMetrics.KeyedStyles(c));
            Assert.Equal(44, ComposerMetrics.InlineStyles(c));
            Assert.Equal(4, ComposerMetrics.ControlTemplates(c));
            Assert.Equal(10, ComposerMetrics.GlyphIcons(c));
            Assert.Equal(31, ComposerMetrics.NumericSizeAttrs(c));
            Assert.Equal(31, ComposerMetrics.TargetNameSetters(c));
            Assert.Equal(46, ComposerMetrics.FontSizeLiterals(c));

            var p = ComposerMetrics.Read(ComposerMetrics.Compact);
            Assert.Equal(8, ComposerMetrics.InlineStyles(p));
            Assert.Equal(4, ComposerMetrics.NumericSizeAttrs(p));
            Assert.Equal(5, ComposerMetrics.FontSizeLiterals(p));

            var s = ComposerMetrics.Read(ComposerMetrics.Schematic);
            Assert.Equal(1, ComposerMetrics.InlineStyles(s));
            Assert.Equal(2, ComposerMetrics.FontSizeLiterals(s));
        }

        public static IEnumerable<object[]> Cases() =>
            ComposerMetrics.Baseline.SelectMany(f => ComposerMetrics.Names.Select(n => new object[] { f.Key, n }));

        [Theory]
        [MemberData(nameof(Cases))]
        public void Guard_NeverExceedsBaseline(string file, string metric)
        {
            int actual = ComposerMetrics.Measure(metric, ComposerMetrics.Read(file));
            int max = ComposerMetrics.Baseline[file][metric];
            Assert.True(actual <= max, $"{file}: {metric} is {actual}, the baseline allows {max} (migrate it, or lower the baseline only when it went down)");
        }
    }
}
