using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AvasRoutingTool.Tests
{
    /// <summary>One justified exception: it applies to the element whose opening tag contains <see cref="Contains"/>, in <see cref="File"/>.</summary>
    internal sealed record AllowEntry(string File, string Contains, string Reason);

    /// <summary>
    /// The single allowlist of the UI v2 guards (<c>UiV2Guards.allowlist.json</c>). Sections:
    /// <b>pending</b> (view file -> owner/task: not yet migrated, the rules are not enforced on it),
    /// <b>staticIcons</b> (an element with a literal Width/Height: a vector icon host),
    /// <b>unvirtualizedLists</b> (a ListBox/ListView/DataGrid that is not virtualized, with the reason),
    /// <b>inlineStyleExceptions</b> (an inline Style without BasedOn that cannot be a central style).
    /// Paths are relative to <c>src/AvasRoutingTool</c>, with forward slashes.
    /// </summary>
    internal sealed class UiV2Allowlist
    {
        public const string FileName = "UiV2Guards.allowlist.json";

        public Dictionary<string, string> Pending { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<AllowEntry> StaticIcons { get; } = new();
        public List<AllowEntry> UnvirtualizedLists { get; } = new();
        public List<AllowEntry> InlineStyleExceptions { get; } = new();

        public IEnumerable<(string Section, AllowEntry Entry)> AllEntries() =>
            StaticIcons.Select(e => ("staticIcons", e))
                .Concat(UnvirtualizedLists.Select(e => ("unvirtualizedLists", e)))
                .Concat(InlineStyleExceptions.Select(e => ("inlineStyleExceptions", e)));

        public static UiV2Allowlist FromJson(string json)
        {
            var result = new UiV2Allowlist();
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            foreach (var section in doc.RootElement.EnumerateObject())
            {
                switch (section.Name)
                {
                    case "pending":
                        foreach (var p in section.Value.EnumerateObject()) result.Pending[Normalize(p.Name)] = p.Value.GetString() ?? "";
                        break;
                    case "staticIcons": Read(section.Value, result.StaticIcons); break;
                    case "unvirtualizedLists": Read(section.Value, result.UnvirtualizedLists); break;
                    case "inlineStyleExceptions": Read(section.Value, result.InlineStyleExceptions); break;
                }
            }
            return result;
        }

        public static UiV2Allowlist Load() => FromJson(File.ReadAllText(Path.Combine(TestsDirectory(), FileName)));

        public static string TestsDirectory() => Path.Combine(RepoRoot(), "tests", "AvasRoutingTool.Tests");

        public static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AvasRoutingTool.sln"))) dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("AvasRoutingTool.sln not found above " + AppContext.BaseDirectory);
        }

        public static string SourceRoot() => Path.Combine(RepoRoot(), "src", "AvasRoutingTool");

        public static string Normalize(string path) => path.Replace('\\', '/');

        private static void Read(JsonElement array, List<AllowEntry> into)
        {
            foreach (var item in array.EnumerateArray())
            {
                string Text(string name) => item.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";
                into.Add(new AllowEntry(Normalize(Text("file")), Text("contains"), Text("reason")));
            }
        }
    }
}
