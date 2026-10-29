using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Rtp;
using AvasRoutingTool.Sdvoe;
using AvasRoutingTool.ViewModels;
using AvasRoutingTool.Views;

namespace AvasRoutingTool
{
    public partial class MainWindow : Window
    {
        private readonly IConfigService _configService;
        private readonly ISdvoeClient _sdvoeClient;
        private readonly IRtpReceiver _rtpReceiver;
        private readonly MulticastIpManager _ipManager;
        private readonly RoutingOrchestrator _orchestrator;
        private readonly MainViewModel _viewModel;
        private bool _isWebViewInitialized;
        private bool _leftVisualRouting;

        public MainWindow()
        {
            InitializeComponent();
            AppVersionText.Text = AppInfo.DisplayVersion;
            Title = $"AVAS Routing Tool {AppInfo.DisplayVersion} - Advantech AVAS-223 Video Management";

            _configService = new ConfigService();
            var config = _configService.Load();

            _sdvoeClient = new SdvoeClient();
            _rtpReceiver = new UnifiedRtpDemuxReceiver();
            _ipManager = new MulticastIpManager(config.MulticastStartIp, config.MulticastEndIp);
            _orchestrator = new RoutingOrchestrator(_sdvoeClient);

            _viewModel = new MainViewModel(_configService, _sdvoeClient, _rtpReceiver, _ipManager, _orchestrator)
            {
                RequestOpenSettings = ShowSettings
            };

            DataContext = _viewModel;
            _viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.ShowLogs)) ApplyLogsVisibility();
            };
            ApplyLogsVisibility();

            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await _viewModel.InitializeAsync();
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            _rtpReceiver.Dispose();
            _sdvoeClient.Dispose();
        }

        private void ApplyLogsVisibility()
        {
            TabLogs.Visibility = _viewModel.ShowLogs ? Visibility.Visible : Visibility.Collapsed;
            if (!_viewModel.ShowLogs && TabLogs.IsChecked == true) TabVisualRouting.IsChecked = true;
        }

        private async void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (ViewVisualRouting == null || ViewBavmConsole == null || ViewLogs == null) return;

            if (TabVisualRouting.IsChecked == true)
            {
                ViewVisualRouting.Visibility = Visibility.Visible;
                ViewBavmConsole.Visibility = Visibility.Collapsed;
                ViewLogs.Visibility = Visibility.Collapsed;

                // Routes may have been changed in the BAVM console meanwhile: re-read what the decoders really do.
                if (_leftVisualRouting && _viewModel != null)
                {
                    _leftVisualRouting = false;
                    await _viewModel.RestoreStateAsync();
                }
            }
            else if (TabBavmConsole.IsChecked == true)
            {
                _leftVisualRouting = true;
                ViewVisualRouting.Visibility = Visibility.Collapsed;
                ViewBavmConsole.Visibility = Visibility.Visible;
                ViewLogs.Visibility = Visibility.Collapsed;
                await EnsureBavmWebViewAsync();
            }
            else if (TabLogs.IsChecked == true)
            {
                ViewVisualRouting.Visibility = Visibility.Collapsed;
                ViewBavmConsole.Visibility = Visibility.Collapsed;
                ViewLogs.Visibility = Visibility.Visible;
            }
        }

        private async Task EnsureBavmWebViewAsync()
        {
            try
            {
                if (!_isWebViewInitialized)
                {
                    string userDataFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebView2_UserData");
                    if (!Directory.Exists(userDataFolder))
                    {
                        Directory.CreateDirectory(userDataFolder);
                    }

                    var env = await CoreWebView2Environment.CreateAsync(AvasRoutingTool.Services.WebViewRuntimeLocator.Find(AppDomain.CurrentDomain.BaseDirectory), userDataFolder);
                    await BavmWebView.EnsureCoreWebView2Async(env);
                    _isWebViewInitialized = true;
                }

                if (_isWebViewInitialized && BavmWebView.CoreWebView2 != null)
                {
                    BavmWebView.CoreWebView2.Navigate(_viewModel.BavmUrl);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to initialize BAVM embedded web view: {ex.Message}", "WebView2 Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ShowSettings()
        {
            var dialog = new SettingsDialog(_configService)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true)
            {
                // Reload configuration and reconnect
                _ = _viewModel.InitializeAsync();
            }
        }
    }
}
