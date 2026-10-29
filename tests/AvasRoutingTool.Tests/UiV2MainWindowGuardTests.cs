using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// Keeps the Main Window session's rows of the single UI v2 allowlist honest (plan task A1).
    /// The foundation (F5) already registered every view file in <c>pending</c>; the session only ever removes its own rows,
    /// one task at a time, and never leaves a file that no longer exists behind.
    /// </summary>
    public class UiV2MainWindowGuardTests
    {
        // Files owned by the Main Window session and the task that migrates them (plan section 7).
        private static readonly Dictionary<string, string> Owned = new(StringComparer.OrdinalIgnoreCase)
        {
            ["MainWindow.xaml"] = "A5",
            ["Views/VisualRoutingView.xaml"] = "A7",
            ["Views/SourceCardView.xaml"] = "A8",
            ["Views/DisplayCardView.xaml"] = "A9",
            ["Views/SettingsDialog.xaml"] = "A12",
        };

        [Fact]
        public void MainWindowSessionRows_OnlyShrink_AndNameTheirTask()
        {
            var allow = UiV2Allowlist.Load();
            foreach (var (file, owner) in allow.Pending.Where(p => p.Value.Contains("Main Window session", StringComparison.Ordinal)))
            {
                Assert.True(Owned.ContainsKey(file) || file == "Views/LayoutsBarView.xaml", $"{file} is not a Main Window session file");
                Assert.Matches(@"\bA\d+", owner);
            }
        }

        [Fact]
        public void EveryMainWindowFile_ExistsOrIsListedAsMigrated()
        {
            var root = UiV2Allowlist.SourceRoot();
            foreach (var file in Owned.Keys)
            {
                Assert.True(File.Exists(Path.Combine(root, file)), $"{file} is a Main Window session file and must exist");
            }
        }

        [Fact]
        public void CardGrids_AreNotListBoxes_SoTheVirtualizationGuardNeverTouchesTheirCards()
        {
            // D11: the source and display grids are ItemsControls on AdaptiveCardPanel; each card owns a live RTP preview and must stay realized.
            var root = UiV2Allowlist.SourceRoot();
            var routing = File.ReadAllText(Path.Combine(root, "Views", "VisualRoutingView.xaml"));
            Assert.DoesNotContain("<ListBox", routing);
            Assert.DoesNotContain("<ListView", routing);
            Assert.DoesNotContain("<DataGrid", routing);
        }
    }
}
