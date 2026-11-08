using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;
using AvasRoutingTool.Controls;
using AvasRoutingTool.ViewModels;
using Xunit;

namespace AvasRoutingTool.Tests
{
    /// <summary>The Composer draws every icon with MaterialIcon (Material Symbols Rounded); no Fluent SymbolIcon is left in its views or styles.</summary>
    public partial class ViewSmokeTests
    {
        private static string ComposerSrc(string relative)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AvasRoutingTool.sln"))) dir = dir.Parent;
            return File.ReadAllText(Path.Combine(dir!.FullName, "src", "AvasRoutingTool", relative.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static void SetBanner(MultiViewComposerViewModel c, BannerKind kind) =>
            typeof(MultiViewComposerViewModel).GetProperty(nameof(MultiViewComposerViewModel.Banner))!.SetValue(c, new ComposerBanner("Something to read", kind));

        [Fact]
        public void ComposerView_UsesNoFluentSymbolIcons() =>
            RunSta(() =>
            {
                Assert.DoesNotContain("ui:SymbolIcon", ComposerSrc("Views/MultiViewComposerView.xaml"));
                Assert.DoesNotContain("ui:SymbolIcon", ComposerSrc("Resources/Styles/Composer.xaml"));

                // a placed block (corner clear, locks on the unavailable blocks), a selected layout tile, the picker open
                var (rig, c) = PickerRig();
                var view = rig.View(c);
                Showing(view, () =>
                {
                    Assert.True(((Popup)Named(view, "PickerPopup").Single()).IsOpen);
                    var popup = (Popup)Named(view, "PickerPopup").Single();
                    Relayout((FrameworkElement)popup.Child);
                    Assert.Empty(FindAll<Wpf.Ui.Controls.SymbolIcon>(view));
                    Assert.Empty(FindAll<Wpf.Ui.Controls.SymbolIcon>(popup.Child));
                    Assert.NotEmpty(FindAll<MaterialIcon>(view));
                    Assert.NotEmpty(FindAll<MaterialIcon>(popup.Child));

                    // a banner of each kind
                    foreach (var kind in new[] { BannerKind.Info, BannerKind.Success, BannerKind.Warning, BannerKind.Error })
                    {
                        SetBanner(c, kind);
                        view.UpdateLayout();
                        Assert.Empty(FindAll<Wpf.Ui.Controls.SymbolIcon>(view));
                    }
                });
            });

        [Theory]
        [InlineData(BannerKind.Info, "info")]
        [InlineData(BannerKind.Success, "check_circle")]
        [InlineData(BannerKind.Warning, "warning")]
        [InlineData(BannerKind.Error, "error")]
        public void ComposerBanner_IconKind_FollowsTheBannerKind(BannerKind kind, string expected) =>
            RunSta(() =>
            {
                var rig = new ComposerRig();
                var c = rig.Composer("matrix2x2");
                SetBanner(c, kind);
                var view = rig.View(c);
                Showing(view, () =>
                {
                    var host = Named(view, "BannerHost").Single();
                    var icon = FindAll<MaterialIcon>(host).Single();
                    Assert.Equal(expected, icon.Kind);
                    var m = (double)Application.Current.FindResource("Size.Icon.M");
                    Assert.Equal(m, icon.ActualWidth, 1);
                    Assert.Equal(m, icon.ActualHeight, 1);
                });
            });
    }
}
