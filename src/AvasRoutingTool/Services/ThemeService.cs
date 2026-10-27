using System;
using System.Linq;
using System.Windows;
using AvasRoutingTool.Configuration;

namespace AvasRoutingTool.Services
{
    public class ThemeService : IThemeService
    {
        private readonly IConfigService? _configService;
        private ThemeMode _currentTheme = ThemeMode.Dark;

        public event EventHandler<ThemeMode>? ThemeChanged;

        public ThemeMode CurrentTheme => _currentTheme;

        public ThemeService(IConfigService? configService = null)
        {
            _configService = configService;
            if (_configService != null)
            {
                var cfg = _configService.Load();
                if (string.Equals(cfg.Theme, "Light", StringComparison.OrdinalIgnoreCase))
                {
                    _currentTheme = ThemeMode.Light;
                }
            }
            ApplyThemeResources(_currentTheme);
        }

        public void SetTheme(ThemeMode mode)
        {
            _currentTheme = mode;
            ApplyThemeResources(mode);

            if (_configService != null)
            {
                try
                {
                    var cfg = _configService.Load();
                    cfg.Theme = mode.ToString();
                    _configService.Save(cfg);
                }
                catch { }
            }

            ThemeChanged?.Invoke(this, mode);
        }

        public void ToggleTheme()
        {
            SetTheme(_currentTheme == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark);
        }

        private void ApplyThemeResources(ThemeMode mode)
        {
            var app = Application.Current;
            if (app == null) return;

            string themeFile = mode == ThemeMode.Light ? "Themes/Theme.Light.xaml" : "Themes/Theme.Dark.xaml";
            var packUri = new Uri($"pack://application:,,,/AvasRoutingTool;component/Resources/{themeFile}", UriKind.Absolute);
            var newDict = new ResourceDictionary { Source = packUri };

            void Apply()
            {
                // Remove existing theme dictionaries to avoid conflicts
                for (int i = app.Resources.MergedDictionaries.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        var d = app.Resources.MergedDictionaries[i];
                        var src = d?.Source?.OriginalString ?? "";
                        if (src.Contains("Theme.Dark.xaml", StringComparison.OrdinalIgnoreCase) ||
                            src.Contains("Theme.Light.xaml", StringComparison.OrdinalIgnoreCase))
                        {
                            app.Resources.MergedDictionaries.RemoveAt(i);
                        }
                    }
                    catch { }
                }

                foreach (var md in app.Resources.MergedDictionaries.ToList())
                {
                    if (md == null) continue;
                    for (int j = md.MergedDictionaries.Count - 1; j >= 0; j--)
                    {
                        try
                        {
                            var src = md.MergedDictionaries[j]?.Source?.OriginalString ?? "";
                            if (src.Contains("Theme.Dark.xaml", StringComparison.OrdinalIgnoreCase) ||
                                src.Contains("Theme.Light.xaml", StringComparison.OrdinalIgnoreCase))
                            {
                                md.MergedDictionaries.RemoveAt(j);
                            }
                        }
                        catch { }
                    }
                }

                // Add the new palette dictionary
                try
                {
                    app.Resources.MergedDictionaries.Add(newDict);
                }
                catch { }
            }

            try
            {
                Apply();
            }
            catch
            {
                if (app.Dispatcher != null && !app.Dispatcher.CheckAccess() && !app.Dispatcher.HasShutdownStarted)
                {
                    try
                    {
                        app.Dispatcher.BeginInvoke(Apply);
                    }
                    catch { }
                }
            }
        }
    }
}
