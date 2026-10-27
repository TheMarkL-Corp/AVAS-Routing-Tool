using System;

namespace AvasRoutingTool.Services
{
    public enum ThemeMode
    {
        Dark,
        Light
    }

    public interface IThemeService
    {
        ThemeMode CurrentTheme { get; }
        void SetTheme(ThemeMode mode);
        void ToggleTheme();
        event EventHandler<ThemeMode>? ThemeChanged;
    }
}
