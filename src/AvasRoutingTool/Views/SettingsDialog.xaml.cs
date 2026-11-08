using System;
using System.Windows;
using System.Windows.Controls;
using AvasRoutingTool.Configuration;

namespace AvasRoutingTool.Views
{
    public partial class SettingsDialog : Window
    {
        private readonly IConfigService _configService;
        private readonly AppConfig _config;

        public SettingsDialog(IConfigService configService)
        {
            InitializeComponent();
            MaxHeight = SystemParameters.PrimaryScreenHeight; // the form scrolls when the content is taller than the screen
            VersionText.Text = "AVAS Routing Tool " + AppInfo.DisplayVersion;
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _config = _configService.Load();

            // Populate form
            TxtBavmUrl.Text = _config.BavmUrl;
            TxtControlServerIp.Text = _config.DerivedControlServerIp;
            TxtTelnetPort.Text = _config.TelnetPort.ToString();
            TxtRestPort.Text = _config.RestPort.ToString();
            TxtMcastStart.Text = _config.MulticastStartIp;
            TxtMcastEnd.Text = _config.MulticastEndIp;
            TxtBasePort.Text = _config.BasePort.ToString();
            ChkShowLogs.IsChecked = _config.ShowLogs;
            ChkShowAdvanced.IsChecked = _config.ShowAdvanced;
        }

        private void TxtBavmUrl_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtControlServerIp == null) return;

            try
            {
                if (Uri.TryCreate(TxtBavmUrl.Text, UriKind.Absolute, out var uri))
                {
                    TxtControlServerIp.Text = uri.Host;
                    return;
                }
            }
            catch { }
            TxtControlServerIp.Text = "127.0.0.1";
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            DialogResult = true;
            Close();
        }

        private void SaveSettings()
        {
            _config.BavmUrl = TxtBavmUrl.Text.Trim();

            if (int.TryParse(TxtTelnetPort.Text, out int telnetPort))
                _config.TelnetPort = telnetPort;

            if (int.TryParse(TxtRestPort.Text, out int restPort))
                _config.RestPort = restPort;

            _config.MulticastStartIp = TxtMcastStart.Text.Trim();
            _config.MulticastEndIp = TxtMcastEnd.Text.Trim();

            if (int.TryParse(TxtBasePort.Text, out int basePort))
                _config.BasePort = basePort;

            _config.ShowLogs = ChkShowLogs.IsChecked == true;
            _config.ShowAdvanced = ChkShowAdvanced.IsChecked == true;

            _configService.Save(_config);
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
