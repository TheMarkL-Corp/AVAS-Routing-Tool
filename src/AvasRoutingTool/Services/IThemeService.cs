using System;

namespace AvasRoutingTool.Services
{
    public enum ThemeMode
    {
        Dark,
        Light,
        /// <summary>Follow the Windows "app mode" setting; <see cref="IThemeService.EffectiveTheme"/> is what is shown.</summary>
        System
    }

    public interface IThemeService
    {
        /// <summary>The user's choice (can be <see cref="ThemeMode.System"/>).</summary>
        ThemeMode CurrentTheme { get; }

        /// <summary>The palette on screen: always <see cref="ThemeMode.Dark"/> or <see cref="ThemeMode.Light"/>.</summary>
        ThemeMode EffectiveTheme { get; }

        void SetTheme(ThemeMode mode);

        /// <summary>Switches to an explicit palette opposite to the one shown.</summary>
        void ToggleTheme();

        event EventHandler<ThemeMode>? ThemeChanged;
    }
}
