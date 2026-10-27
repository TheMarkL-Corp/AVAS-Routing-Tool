using System;
using System.Windows;
using AvasRoutingTool.Configuration;

namespace AvasRoutingTool.Services
{
    public class ThemeService : IThemeService
    {
        private const string DarkFile = "Theme.Dark.xaml";
        private const string LightFile = "Theme.Light.xaml";

        private readonly IConfigService? _configService;
        private ThemeMode _currentTheme = ThemeMode.Dark;

        public event EventHandler<ThemeMode>? ThemeChanged;

        public ThemeMode CurrentTheme => _currentTheme;

        public ThemeService(IConfigService? configService = null)
        {
            _configService = configService;
            if (_configService != null &&
                string.Equals(_configService.Load().Theme, nameof(ThemeMode.Light), StringComparison.OrdinalIgnoreCase))
            {
                _currentTheme = ThemeMode.Light;
            }
            ApplyThemeResources(_currentTheme);
        }

        public void SetTheme(ThemeMode mode)
        {
            _currentTheme = mode;
            ApplyThemeResources(mode);
            Persist(mode);
            ThemeChanged?.Invoke(this, mode);
        }

        public void ToggleTheme() => SetTheme(_currentTheme == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark);

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

        private static void ApplyThemeResources(ThemeMode mode)
        {
            var app = Application.Current;
            if (app == null) return;

            var file = mode == ThemeMode.Light ? LightFile : DarkFile;
            var palette = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/AvasRoutingTool;component/Resources/Themes/{file}", UriKind.Absolute)
            };

            // No dispatcher hop: the STA view tests drive the service from their own thread while the app dispatcher
            // is blocked, so a synchronous Invoke deadlocks. Callers (the toggle command) already run on the UI thread.
            Swap(app, palette);
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
