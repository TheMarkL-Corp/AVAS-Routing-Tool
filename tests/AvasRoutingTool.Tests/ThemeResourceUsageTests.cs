using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// Light/Dark switching swaps the palette dictionary at runtime. A <c>StaticResource</c> palette reference is resolved once
    /// at load and never repaints, so every brush/colour reference outside the palette files must be a <c>DynamicResource</c>.
    /// </summary>
    public class ThemeResourceUsageTests
    {
        private static readonly Regex StaticPaletteRef = new(@"\{StaticResource\s+(Brush|Color)\.[\w.]+\}", RegexOptions.Compiled);

        private static string SourceRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AvasRoutingTool.sln"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir!.FullName, "src", "AvasRoutingTool");
        }

        /// <summary>Opaque literals that were copies of palette entries (or of the preview backdrop / scrim family). Alpha tints are 8-digit and not listed.</summary>
        private static readonly Regex PaletteLiteral = new(
            "\"#(?:CBD5E1|E2E8F0|475569|334155|94A3B8|64748B|10B981|EF4444|F59E0B|FCA5A5|F87171|FECACA|A5F3FC|A7F3D0|2A1F08|082F3A|04232B|0B2A1E|0F1626|0D131F|0F1E3A|3B0D0D|1E3A8A|93C5FD|070A0F|0B0F17|050505|1E3A5F|DBEAFE|FFFFFF|F1F5F9|000000)\"",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static IEnumerable<(string Rel, string Text)> ChromeXaml()
        {
            var root = SourceRoot();
            foreach (var file in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(root, file);
                if (rel.StartsWith(Path.Combine("Resources", "Themes") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                if (rel.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    rel.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                yield return (rel, File.ReadAllText(file));
            }
        }

        [Fact]
        public void NoXamlOutsideThePaletteFiles_UsesStaticResourceForBrushOrColor()
        {
            var offenders = ChromeXaml()
                .Select(f => (f.Rel, Count: StaticPaletteRef.Matches(f.Text).Count))
                .Where(f => f.Count > 0)
                .Select(f => $"{f.Rel}: {f.Count}")
                .ToList();

            Assert.True(offenders.Count == 0, "StaticResource palette references would not repaint on a theme switch:\n" + string.Join("\n", offenders));
        }

        /// <summary>Text drawn over video / scrims is on a dark surface in BOTH themes, so it must use the On* tokens, never chrome text tokens.</summary>
        [Theory]
        [InlineData("DisplayCardView.xaml", "{Binding SourceName, Mode=OneWay}", null, "Brush.Text.OnScrim")]
        [InlineData("DisplayCardView.xaml", "Preview not available", null, "Brush.Text.OnPreviewMuted")]
        [InlineData("DisplayCardView.xaml", "ActiveSourceTagText", "name", "Brush.Text.OnScrim")]
        [InlineData("DisplayCardView.xaml", "RoutedSourceNameText", "name", "Brush.Text.OnPreview")]
        [InlineData("DisplayCardView.xaml", "Routed to Source", null, "Brush.Text.OnPreviewMuted")]
        [InlineData("DisplayCardView.xaml", "Preview Not Available (Non-AVAS Device)", null, "Brush.Text.OnPreviewMuted")]
        [InlineData("DisplayCardView.xaml", "Ready to route a source", null, "Brush.Text.OnPreview")]
        [InlineData("DisplayCardView.xaml", "Pick a source above, then tap here or drag it in.", null, "Brush.Text.OnPreviewMuted")]
        [InlineData("MultiViewComposerView.xaml", "IssueTextBlock", "name", "Brush.Text.OnAction")]
        [InlineData("MultiViewComposerView.xaml", "SameAsText", "name", "Brush.Text.OnScrim")]
        [InlineData("MultiViewComposerView.xaml", "{Binding SizeText, Mode=OneWay}", null, "Brush.Text.OnScrim")]
        [InlineData("MultiViewComposerView.xaml", "No MultiView on this screen", null, "Brush.Text.OnPreview")]
        [InlineData("MultiViewComposerView.xaml", "NoPreviewName", "name", "Brush.Text.OnScrim")]
        [InlineData("MultiViewComposerView.xaml", "NoPreviewLabel", "name", "Brush.Text.OnPreview")]
        [InlineData("MultiViewComposerView.xaml", "ReasonText", "name", "Brush.Text.OnScrim")]
        [InlineData("MultiViewComposerView.xaml", "EmptyHint", "name", "Brush.Text.OnPreview")]
        [InlineData("MultiViewComposerView.xaml", "PlaceVerbText", "name", "Brush.Text.OnScrimAccent")]
        [InlineData("MultiViewComposerView.xaml", "PlaceNameText", "name", "Brush.Text.OnScrimAccent")]
        [InlineData("MultiViewComposerView.xaml", "NamePlateText", "name", "Brush.Text.OnPreview")]
        [InlineData("SourceCardView.xaml", "Preview not supported", null, "Brush.Text.OnPreview")]
        [InlineData("SourceCardView.xaml", "Non-AVAS Device", null, "Brush.Text.OnPreviewMuted")]
        [InlineData("SourceCardView.xaml", "Preview standby", null, "Brush.Text.OnPreviewMuted")]
        public void VideoOverlayText_UsesOnPreviewOrOnScrimTokens(string file, string anchor, string? selector, string expectedBrush)
        {
            var path = Path.Combine(SourceRoot(), "Views", file);
            var doc = System.Xml.Linq.XDocument.Load(path);
            var xName = System.Xml.Linq.XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml");
            var matches = doc.Descendants()
                .Where(e => e.Name.LocalName == "TextBlock")
                .Where(e => selector == "name" ? (string?)e.Attribute(xName) == anchor : (string?)e.Attribute("Text") == anchor)
                .Where(e => selector is null or "name" || (string?)e.Attribute("FontSize") == selector)
                .ToList();

            Assert.NotEmpty(matches);
            Assert.All(matches, e => Assert.Equal("{DynamicResource " + expectedBrush + "}", (string?)e.Attribute("Foreground")));
        }

        private static readonly Regex PaletteKeyRef =new(@"\{DynamicResource\s+((?:Brush|Color)\.[\w.]+)\}", RegexOptions.Compiled);
        private static readonly Regex PaletteKeyDef = new(@"x:Key=""((?:Brush|Color)\.[\w.]+)""", RegexOptions.Compiled);

        [Fact]
        public void EveryPaletteKeyAView_ReferencesIsDefinedInBothThemes()
        {
            var themes = Path.Combine(SourceRoot(), "Resources", "Themes");
            var dark = PaletteKeyDef.Matches(File.ReadAllText(Path.Combine(themes, "Theme.Dark.xaml"))).Select(m => m.Groups[1].Value).ToHashSet();
            var light = PaletteKeyDef.Matches(File.ReadAllText(Path.Combine(themes, "Theme.Light.xaml"))).Select(m => m.Groups[1].Value).ToHashSet();

            // A DynamicResource to a missing key paints nothing and raises no exception, so only this check catches a typo.
            var missing = ChromeXaml()
                .SelectMany(f => PaletteKeyRef.Matches(f.Text).Select(m => (f.Rel, Key: m.Groups[1].Value)))
                .Where(r => !dark.Contains(r.Key) || !light.Contains(r.Key))
                .Select(r => $"{r.Rel}: {r.Key}")
                .Distinct()
                .ToList();

            Assert.True(missing.Count == 0, "Palette keys referenced but not defined in both themes:\n" + string.Join("\n", missing));
        }

        /// <summary>#RRGGBB and #AARRGGBB literals; chrome must use Brush.* / Color.* keys so both themes repaint.</summary>
        private static readonly Regex HexColorLiteral = new(
            @"#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?\b",
            RegexOptions.Compiled);

        /// <summary>
        /// All XAML views have been converted to theme tokens, so this exemption set is intentionally and
        /// permanently empty; any new raw hex literal outside the palette files fails the test below.
        /// </summary>
        private static readonly HashSet<string> HexCheckExemptFiles = new(StringComparer.OrdinalIgnoreCase);

        [Fact]
        public void NoXamlOutsideThePaletteFiles_UsesRawHexLiterals()
        {
            var offenders = ChromeXaml()
                .Where(f => !HexCheckExemptFiles.Contains(Path.GetFileName(f.Rel)))
                .Select(f => (f.Rel, Count: HexColorLiteral.Matches(f.Text).Count))
                .Where(f => f.Count > 0)
                .Select(f => $"{f.Rel}: {f.Count}")
                .ToList();

            Assert.True(offenders.Count == 0, "Raw hex literals bypass the theme palette; use a Brush.* key:\n" + string.Join("\n", offenders));
        }

        [Fact]
        public void NoXamlOutsideThePaletteFiles_HardCodesAPaletteColour()
        {
            var offenders = ChromeXaml()
                .Select(f => (f.Rel, Count: PaletteLiteral.Matches(f.Text).Count))
                .Where(f => f.Count > 0)
                .Select(f => $"{f.Rel}: {f.Count}")
                .ToList();

            Assert.True(offenders.Count == 0, "Hard-coded colours stay on the Dark palette in Light mode; use a Brush.* key:\n" + string.Join("\n", offenders));
        }
    }
}
