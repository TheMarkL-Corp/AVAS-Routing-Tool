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
                ["KeyedStyles"] = 1, ["InlineStyles"] = 29, ["ControlTemplates"] = 3, ["NumericSizeAttrs"] = 24,
                ["GlyphIcons"] = 6, ["TargetNameSetters"] = 31, ["FontSizeLiterals"] = 31,
            },
            [Compact] = new()
            {
                ["KeyedStyles"] = 0, ["InlineStyles"] = 8, ["ControlTemplates"] = 0, ["NumericSizeAttrs"] = 4,
                ["GlyphIcons"] = 0, ["TargetNameSetters"] = 0, ["FontSizeLiterals"] = 5,
            },
            [Schematic] = new()
            {
                ["KeyedStyles"] = 0, ["InlineStyles"] = 0, ["ControlTemplates"] = 0, ["NumericSizeAttrs"] = 0,
                ["GlyphIcons"] = 0, ["TargetNameSetters"] = 0, ["FontSizeLiterals"] = 0,
            },
        };
    }

    public class ComposerUiV2GuardTests
    {
        [Fact]
        public void Metrics_MeasureASample()
        {
            // Measured on a sample, not the live files: the live numbers fall with every task (the Baseline above is what pins them).
            const string xaml = @"<UserControl><UserControl.Resources>
  <Style x:Key=""K"" TargetType=""Border""><Setter Property=""Width"" Value=""28"" /><Setter Property=""MinHeight"" Value=""9.5"" /></Style>
  <ControlTemplate x:Key=""T"" TargetType=""Button""><Border /><ControlTemplate.Triggers /></ControlTemplate>
</UserControl.Resources>
<Grid><Canvas Width=""0"" Height=""0"" /><TextBlock Text=""✓ ok"" FontSize=""14"" /><TextBlock Text=""i"" /><Border MaxWidth=""380"" Width=""{StaticResource Size.X}"">
<Border.Style><Style TargetType=""Border""><Setter Property=""FontSize"" Value=""13"" /><Style.Triggers /></Style></Border.Style></Border>
<DataTemplate><Border /><DataTemplate.Triggers><DataTrigger><Setter TargetName=""A"" Property=""Opacity"" Value=""1"" /><Setter Property=""Opacity"" Value=""1"" /></DataTrigger></DataTemplate.Triggers></DataTemplate>
</Grid></UserControl>";
            Assert.Equal(1, ComposerMetrics.KeyedStyles(xaml));
            Assert.Equal(1, ComposerMetrics.InlineStyles(xaml));
            Assert.Equal(1, ComposerMetrics.ControlTemplates(xaml));
            Assert.Equal(3, ComposerMetrics.NumericSizeAttrs(xaml)); // Width 28, MinHeight 9.5, MaxWidth 380; the Canvas anchor and a token are not sizes
            Assert.Equal(2, ComposerMetrics.GlyphIcons(xaml));
            Assert.Equal(1, ComposerMetrics.TargetNameSetters(xaml));
            Assert.Equal(2, ComposerMetrics.FontSizeLiterals(xaml));
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
