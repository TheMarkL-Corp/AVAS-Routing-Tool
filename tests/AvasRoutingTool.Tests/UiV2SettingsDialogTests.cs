using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Views;
using Wpf.Ui.Controls;
using Xunit;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace AvasRoutingTool.Tests
{
    /// <summary>UI v2 settings dialog (plan task A12): ui:TextBox fields and ui:ToggleSwitch on central styles, tokens instead of literals, normal chrome.</summary>
    [Collection("WpfSta")]
    public class UiV2SettingsDialogTests
    {
        private sealed class StubConfig : IConfigService
        {
            public AppConfig Saved { get; private set; } = new();
            public AppConfig Load() => new AppConfig { ShowLogs = false };
            public void Save(AppConfig config) => Saved = config;
        }

        private static void RunSta(Action<SettingsDialog, StubConfig> body)
        {
            Exception? failure = null;
            var t = new Thread(() =>
            {
                try
                {
                    TestResources.Ensure();
                    var cfg = new StubConfig();
                    var dialog = new SettingsDialog(cfg) { ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = 0 };
                    dialog.Show();
                    dialog.UpdateLayout();
                    try { body(dialog, cfg); } finally { dialog.Close(); }
                }
                catch (Exception ex) { failure = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (failure != null) throw new Xunit.Sdk.XunitException("Settings dialog failure: " + failure);
        }

        private static readonly string[] Fields = { "TxtBavmUrl", "TxtControlServerIp", "TxtTelnetPort", "TxtRestPort", "TxtMcastStart", "TxtMcastEnd", "TxtBasePort" };

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var c = VisualTreeHelper.GetChild(root, i);
                if (c is T t) yield return t;
                foreach (var d in Descendants<T>(c)) yield return d;
            }
        }

        [Fact]
        public void Fields_AreUiTextBoxes_OnTheFieldStyle_TouchSized_AndNamed() => RunSta((d, _) =>
        {
            var style = Application.Current.FindResource("Style.TextBox.Field");
            foreach (var name in Fields)
            {
                var box = Assert.IsType<TextBox>(d.FindName(name));
                Assert.Same(style, box.Style);
                Assert.True(box.ActualHeight >= 48, $"{name} is {box.ActualHeight}px high");
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(box)), $"{name} has no automation name");
            }
            Assert.True(((TextBox)d.FindName("TxtControlServerIp")).IsReadOnly);
        });

        [Fact]
        public void ShowLogs_IsAToggleSwitch_ThatPersists() => RunSta((d, cfg) =>
        {
            var toggle = Assert.IsType<ToggleSwitch>(d.FindName("ChkShowLogs"));
            Assert.IsAssignableFrom<ToggleButton>(toggle);
            Assert.False(toggle.IsChecked);
            Assert.Equal("Show Logs tab", AutomationProperties.GetName(toggle));
            Assert.True(toggle.ActualHeight >= 48, $"toggle {toggle.ActualHeight}");
            toggle.IsChecked = true;
            typeof(SettingsDialog).GetMethod("SaveSettings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(d, null);
            Assert.True(cfg.Saved.ShowLogs);
        });

        [Fact]
        public void TheDialog_KeepsNormalChrome_TakesItsWidthFromTheToken_AndFitsTheScreen() => RunSta((d, _) =>
        {
            Assert.Equal(WindowStyle.SingleBorderWindow, d.WindowStyle);
            Assert.Equal(ResizeMode.NoResize, d.ResizeMode);
            Assert.Equal(SizeToContent.Height, d.SizeToContent);
            Assert.Equal((double)Application.Current.FindResource("Size.Dialog.Settings.Width"), d.Width);
            Assert.True(d.ActualHeight <= SystemParameters.PrimaryScreenHeight + 1, $"dialog {d.ActualHeight}px high");
            Assert.True(d.MaxHeight <= SystemParameters.PrimaryScreenHeight + 1);
            Assert.Equal("System Settings", d.Title);
        });

        [Fact]
        public void TheActions_AreTouchSized_WithTheDialogMinimumWidth_AndAlwaysInTheWindow() => RunSta((d, _) =>
        {
            var min = (double)Application.Current.FindResource("Size.Button.Dialog.Min");
            var buttons = Descendants<Button>(d).Where(b => b.Content is string).ToList();
            Assert.Equal(new[] { "Cancel", "Save & Apply" }, buttons.Select(b => (string)b.Content));
            foreach (var b in buttons)
            {
                Assert.True(b.ActualHeight >= 48, $"{b.Content} {b.ActualHeight}");
                Assert.True(b.ActualWidth >= min, $"{b.Content} {b.ActualWidth}");
                Assert.True(b.MinWidth >= min);
                var bottom = b.TranslatePoint(new Point(0, b.ActualHeight), d).Y;
                Assert.True(bottom <= d.ActualHeight, $"{b.Content} is below the window ({bottom} > {d.ActualHeight})");
            }
        });

        [Fact]
        public void TheText_UsesTheRoles_AtLeast12px() => RunSta((d, _) =>
        {
            var texts = Descendants<TextBlock>(d).Where(t => t.IsVisible && !string.IsNullOrWhiteSpace(t.Text) && t.TemplatedParent == null).ToList();
            Assert.NotEmpty(texts);
            Assert.All(texts, t => Assert.True(t.FontSize >= 12, $"'{t.Text}' is {t.FontSize}px"));
            var title = texts.First(t => t.Text.StartsWith("Configuration"));
            Assert.Equal((double)Application.Current.FindResource("Font.Size.Lead"), title.FontSize);
        });
    }
}
