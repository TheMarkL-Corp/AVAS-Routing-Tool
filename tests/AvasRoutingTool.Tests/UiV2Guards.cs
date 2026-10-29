using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// The hard UI v2 guards (D3, D6, D8, D11, G2-G5) over every view file: <c>MainWindow.xaml</c> and <c>Views/*.xaml</c>.
    /// A file that is still in the <c>pending</c> section of <c>UiV2Guards.allowlist.json</c> is not enforced yet; each migration task
    /// removes its file from <c>pending</c> first (the suite goes red) and migrates until it is green again.
    /// The central dictionaries (<c>Resources/**</c>) are the place for templates, keyed styles and sizes, so they are not scanned here.
    /// The rule engine itself is covered by <see cref="UiV2GuardScannerTests"/>.
    /// </summary>
    public class UiV2Guards
    {
        private static readonly UiV2Allowlist Allowlist = UiV2Allowlist.Load();

        private static IEnumerable<string> ViewFilePaths()
        {
            var root = UiV2Allowlist.SourceRoot();
            yield return "MainWindow.xaml";
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "Views"), "*.xaml").OrderBy(f => f, StringComparer.Ordinal))
            {
                yield return "Views/" + Path.GetFileName(file);
            }
        }

        public static IEnumerable<object[]> ViewFiles() => ViewFilePaths().Select(f => new object[] { f });

        private static string Text(string file) => File.ReadAllText(Path.Combine(UiV2Allowlist.SourceRoot(), file));

        private static List<GuardViolation> Violations(string file) => UiV2GuardScanner.Scan(file, Text(file), Allowlist);

        [Theory]
        [MemberData(nameof(ViewFiles))]
        public void ViewFile_FollowsTheUiV2Rules_OrIsStillPending(string file)
        {
            if (Allowlist.Pending.ContainsKey(file)) return;

            var violations = Violations(file);

            Assert.True(violations.Count == 0,
                $"{file} breaks the UI v2 rules ({violations.Count}):\n" +
                string.Join("\n", violations.GroupBy(v => v.Rule).Select(g => $"  [{g.Key}] x{g.Count()}: {g.First().Detail}")));
        }

        [Fact]
        public void EveryViewFile_IsListedInPending_WithItsOwnerAndTask_UntilItIsMigrated()
        {
            // The foundation starts with every view file pending; later tasks only remove entries. A file with no violation needs no entry.
            var unlisted = ViewFilePaths().Where(f => !Allowlist.Pending.ContainsKey(f) && Violations(f).Count > 0).ToList();
            Assert.True(unlisted.Count == 0, "view files with violations that are not pending: " + string.Join(", ", unlisted));
        }

        [Fact]
        public void PendingEntries_NameAnExistingViewFile_AndAnOwner()
        {
            var views = ViewFilePaths().ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var (file, owner) in Allowlist.Pending)
            {
                Assert.True(views.Contains(file), $"pending names {file}, which is not a view file (a deleted view must leave the list)");
                Assert.False(string.IsNullOrWhiteSpace(owner), $"pending {file} has no owner/task");
            }
        }

        [Fact]
        public void PendingEntries_AreNotStale_EachPendingFileStillBreaksARule()
        {
            // This is also the proof that the guard goes red when a file is removed from pending.
            var clean = Allowlist.Pending.Keys.Where(f => Violations(f).Count == 0).ToList();
            Assert.True(clean.Count == 0, "these pending files are already clean, remove them from pending: " + string.Join(", ", clean));
        }

        [Fact]
        public void AllowlistEntries_HaveAReason_AndPointAtSomethingInTheirFile()
        {
            foreach (var (section, entry) in Allowlist.AllEntries())
            {
                Assert.False(string.IsNullOrWhiteSpace(entry.Reason), $"{section}: {entry.File} / {entry.Contains} has no reason");
                Assert.False(string.IsNullOrWhiteSpace(entry.Contains), $"{section}: {entry.File} has an entry with no match text");
                var path = Path.Combine(UiV2Allowlist.SourceRoot(), entry.File);
                Assert.True(File.Exists(path), $"{section}: {entry.File} does not exist");
                var squashed = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(path), @"\s+", " ");
                var needle = System.Text.RegularExpressions.Regex.Replace(entry.Contains, @"\s+", " ").Trim();
                Assert.True(squashed.Contains(needle, StringComparison.Ordinal), $"{section}: \"{entry.Contains}\" is not in {entry.File} (stale entry)");
            }
        }

        [Fact]
        public void TheCentralDictionaries_AreWhereTemplatesAndKeyedStylesLive()
        {
            // The guard scans views only; make sure the scope is what the rules say: nothing in Views/ is a dictionary.
            Assert.All(ViewFilePaths(), f => Assert.DoesNotContain("<ResourceDictionary", Text(f)));
        }
    }
}
