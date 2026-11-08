using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.Sdvoe.MultiView;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;
using Xunit;

namespace AvasRoutingTool.Tests
{
    public partial class ViewSmokeTests
    {
        [Fact]
        public void MainWindow_ThemeButton_ToolTip_BindsToActiveTheme() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var window = new MainWindow { DataContext = rig.Vm, Width = 1024, Height = 768, ShowInTaskbar = false, Left = -10000 };
                window.Show();
                try
                {
                    Relayout(window);
                    var btn = FindAll<Button>(window).First(b => AutomationProperties.GetName(b) == "Toggle theme mode");
                    Assert.Equal(rig.Vm.ThemeButtonToolTip, btn.ToolTip);
                }
                finally
                {
                    window.Close();
                }
            });

        [Fact]
        public void MainWindow_HasWindowIcon_AndAppBarLogoMark() =>
            RunSta(() =>
            {
                var rig = new CopyRig();
                var window = new MainWindow { DataContext = rig.Vm, Width = 1024, Height = 768, ShowInTaskbar = false, Left = -10000 };
                window.Show();
                try
                {
                    Relayout(window);
                    Assert.NotNull(window.Icon);
                    var mark = (FrameworkElement)window.FindName("BrandLogo");
                    Assert.NotNull(mark);
                    Assert.True(mark.ActualWidth >= 28);
                }
                finally
                {
                    window.Close();
                }
            });
    }
}
