using System.Reflection;

namespace AvasRoutingTool
{
    /// <summary>Single source for the app version shown in the UI; it comes from the csproj &lt;Version&gt; so the .exe details match.</summary>
    public static class AppInfo
    {
        public static string Version { get; } = ReadVersion();

        public static string DisplayVersion => "v" + Version;

        private static string ReadVersion()
        {
            var asm = typeof(AppInfo).Assembly;
            var informational = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(informational))
            {
                var plus = informational.IndexOf('+');
                return plus >= 0 ? informational.Substring(0, plus) : informational;
            }
            return asm.GetName().Version?.ToString(3) ?? "0.0.0";
        }
    }
}
