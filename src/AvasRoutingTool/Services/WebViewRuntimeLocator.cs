using System.IO;

namespace AvasRoutingTool.Services
{
    /// <summary>Finds a Fixed Version WebView2 runtime bundled next to the exe, so the app runs without an installed runtime.</summary>
    public static class WebViewRuntimeLocator
    {
        public const string FolderName = "WebView2Runtime";

        public static string? Find(string baseDirectory)
        {
            string folder = Path.Combine(baseDirectory, FolderName);
            return File.Exists(Path.Combine(folder, "msedgewebview2.exe")) ? folder : null;
        }
    }
}
