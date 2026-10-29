using System;
using System.IO;
using AvasRoutingTool.Services;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public class WebViewRuntimeLocatorTests
    {
        [Fact]
        public void Find_ReturnsNull_WhenNoBundledRuntime()
        {
            var dir = Directory.CreateTempSubdirectory().FullName;
            try { Assert.Null(WebViewRuntimeLocator.Find(dir)); }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void Find_ReturnsBundledFolder_WhenWebViewExeIsPresent()
        {
            var dir = Directory.CreateTempSubdirectory().FullName;
            try
            {
                var rt = Path.Combine(dir, "WebView2Runtime");
                Directory.CreateDirectory(rt);
                File.WriteAllText(Path.Combine(rt, "msedgewebview2.exe"), "");
                Assert.Equal(rt, WebViewRuntimeLocator.Find(dir));
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
