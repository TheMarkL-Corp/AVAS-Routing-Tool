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

        [Fact]
        public void NoXamlOutsideThePaletteFiles_UsesStaticResourceForBrushOrColor()
        {
            var root = SourceRoot();
            var offenders = new List<string>();
            foreach (var file in Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(root, file);
                if (rel.StartsWith(Path.Combine("Resources", "Themes") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                if (rel.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    rel.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;

                var count = StaticPaletteRef.Matches(File.ReadAllText(file)).Count;
                if (count > 0) offenders.Add($"{rel}: {count}");
            }

            Assert.True(offenders.Count == 0, "StaticResource palette references would not repaint on a theme switch:\n" + string.Join("\n", offenders));
        }
    }
}
