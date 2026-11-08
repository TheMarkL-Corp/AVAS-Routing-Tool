using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using AvasRoutingTool.Configuration;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

namespace AvasRoutingTool.Services
{
    public class ThemeService : IThemeService
    {
        private const string DarkFile = "Theme.Dark.xaml";
        private const string LightFile = "Theme.Light.xaml";

        private readonly IConfigService? _configService;
        private readonly Func<bool> _systemPrefersLight;
        private ThemeMode _currentTheme = ThemeMode.Dark;

        public event EventHandler<ThemeMode>? ThemeChanged;

        public ThemeMode CurrentTheme => _currentTheme;

        public ThemeMode EffectiveTheme =>
            _currentTheme != ThemeMode.System ? _currentTheme
            : _systemPrefersLight() ? ThemeMode.Light : ThemeMode.Dark;

        /// <param name="systemPrefersLight">Test seam for the Windows app-mode setting; defaults to the registry value.</param>
        public ThemeService(IConfigService? configService = null, Func<bool>? systemPrefersLight = null)
        {
            _configService = configService;
            _systemPrefersLight = systemPrefersLight ?? WindowsAppsUseLightTheme;
            if (_configService != null &&
                Enum.TryParse<ThemeMode>(_configService.Load().Theme, ignoreCase: true, out var saved))
            {
                _currentTheme = saved;
            }
            ApplyThemeResources(EffectiveTheme);
        }

        public void SetTheme(ThemeMode mode)
        {
            _currentTheme = mode;
            ApplyThemeResources(EffectiveTheme);
            Persist(mode);
            ThemeChanged?.Invoke(this, mode);
        }

        public void ToggleTheme() => SetTheme(EffectiveTheme == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark);

        /// <summary>Windows "Choose your default app mode"; a machine without the value (or with an unreadable registry) shows Dark.</summary>
        private static bool WindowsAppsUseLightTheme()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int v && v != 0;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
            {
                return false;
            }
        }

        /// <summary>The choice is a convenience: a config that cannot be written must not stop the switch itself.</summary>
        private void Persist(ThemeMode mode)
        {
            if (_configService == null) return;
            try
            {
                var cfg = _configService.Load();
                cfg.Theme = mode.ToString();
                _configService.Save(cfg);
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine($"Theme preference not saved: {ex.Message}");
            }
        }

        private static void ApplyThemeResources(ThemeMode effective)
        {
            var app = Application.Current;
            if (app == null) return;

            var file = effective == ThemeMode.Light ? LightFile : DarkFile;
            var palette = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/AvasRoutingTool;component/Resources/Themes/{file}", UriKind.Absolute)
            };

            // No dispatcher hop: the STA view tests drive the service from their own thread while the app dispatcher
            // is blocked, so a synchronous Invoke deadlocks. Callers (the toggle command) already run on the UI thread.
            Swap(app, palette);
            ApplyWpfUi(app, effective, palette);
        }

        /// <summary>
        /// WPF-UI (theme and control styles only, D1) follows the app theme: its <see cref="ThemesDictionary"/> is swapped directly and
        /// the accent comes from the app token. <c>ApplicationThemeManager.Apply</c> is deliberately never called: it restyles
        /// <c>Application.MainWindow</c> as a WPF-UI window, which strips the native caption text, icon and system menu from our plain <c>Window</c>.
        /// </summary>
        private static void ApplyWpfUi(Application app, ThemeMode effective, ResourceDictionary palette)
        {
            var themes = app.Resources.MergedDictionaries.OfType<ThemesDictionary>().ToList();
            if (themes.Count == 0) return;

            var theme = effective == ThemeMode.Light ? ApplicationTheme.Light : ApplicationTheme.Dark;
            foreach (var dictionary in themes) dictionary.Theme = theme;
            if (palette["Color.Accent.Primary"] is Color accent)
            {
                ApplicationAccentColorManager.Apply(accent, theme);
            }
        }

        /// <summary>Replaces the palette dictionary in place, so it keeps its slot ahead of the shared styles.</summary>
        private static void Swap(Application app, ResourceDictionary palette)
        {
            var merged = app.Resources.MergedDictionaries;
            var slot = 0;
            for (var i = merged.Count - 1; i >= 0; i--)
            {
                var source = merged[i].Source?.OriginalString ?? "";
                if (source.EndsWith(DarkFile, StringComparison.OrdinalIgnoreCase) ||
                    source.EndsWith(LightFile, StringComparison.OrdinalIgnoreCase))
                {
                    merged.RemoveAt(i);
                    slot = i;
                }
            }
            merged.Insert(slot, palette);
        }
    }
}
