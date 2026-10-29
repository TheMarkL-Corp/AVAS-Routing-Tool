using System;
using System.Linq;
using System.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

namespace AvasRoutingTool.Tests
{
    /// <summary>
    /// The one bootstrap for view tests: gives the process-wide test <see cref="Application"/> exactly the resources App.xaml merges
    /// (WPF-UI theme and controls, the Dark palette, the shared styles, Theme.xaml, Icons.xaml), in the same order.
    /// Call it at the start of every STA test body. Keep <see cref="Merged"/> in step with App.xaml (a test compares them).
    /// </summary>
    internal static class TestResources
    {
        private static readonly object Gate = new();

        private const string Pack = "pack://application:,,,/AvasRoutingTool;component/";

        /// <summary>What App.xaml merges, in order (the two WPF-UI dictionaries by name, the rest by their Resources path).</summary>
        private static readonly string[] Merged =
        {
            "ThemesDictionary",
            "ControlsDictionary",
            "Resources/Themes/Theme.Dark.xaml",
            "Resources/Styles/WpfUiOverrides.xaml",
            "Resources/Theme.xaml",
            "Resources/Icons.xaml",
        };

        /// <summary>Idempotent. Returns the Application, creating it on the calling STA thread when none exists.</summary>
        public static Application Ensure()
        {
            lock (Gate)
            {
                var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var merged = app.Resources.MergedDictionaries;
                if (!IsCurrent(merged))
                {
                    // Also resets a theme a previous test switched to Light: every test starts as the app does, in Dark.
                    merged.Clear();
                    foreach (var entry in Merged) merged.Add(Create(entry));
                }
                return app;
            }
        }

        private static ResourceDictionary Create(string entry) => entry switch
        {
            "ThemesDictionary" => new ThemesDictionary { Theme = ApplicationTheme.Dark },
            "ControlsDictionary" => new ControlsDictionary(),
            _ => new ResourceDictionary { Source = new Uri(Pack + entry, UriKind.Absolute) },
        };

        private static bool IsCurrent(System.Collections.ObjectModel.Collection<ResourceDictionary> merged)
        {
            if (merged.Count != Merged.Length) return false;
            for (var i = 0; i < Merged.Length; i++)
            {
                var d = merged[i];
                var ok = Merged[i] switch
                {
                    "ThemesDictionary" => d is ThemesDictionary t && t.Source!.OriginalString.EndsWith("/Dark.xaml", StringComparison.OrdinalIgnoreCase),
                    "ControlsDictionary" => d is ControlsDictionary,
                    var path => d.Source != null && d.Source.OriginalString.EndsWith(path, StringComparison.OrdinalIgnoreCase),
                };
                if (!ok) return false;
            }
            return true;
        }
    }
}
