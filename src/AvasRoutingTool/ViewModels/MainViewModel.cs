using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Logging;
using AvasRoutingTool.Rtp;
using AvasRoutingTool.Sdvoe;

namespace AvasRoutingTool.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly IConfigService _configService;
        private readonly ISdvoeClient _sdvoeClient;
        private readonly IRtpReceiver _rtpReceiver;
        private readonly MulticastIpManager _ipManager;
        private readonly RoutingOrchestrator _orchestrator;
        private AppConfig _config;

        private bool _isConnected;
        private string _connectionStatus = "Connecting...";
        private string _bavmUrl = "http://localhost:80";

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<SourceCardViewModel> Sources { get; } = new();
        public ObservableCollection<DisplayCardViewModel> Displays { get; } = new();
        public ObservableCollection<LayoutItemViewModel> Layouts { get; } = new();
        public ObservableCollection<LogEntry> Logs { get; } = new();

        public bool IsConnected
        {
            get => _isConnected;
            set { _isConnected = value; OnPropertyChanged(); }
        }

        public string ConnectionStatus
        {
            get => _connectionStatus;
            set { _connectionStatus = value; OnPropertyChanged(); }
        }

        public string BavmUrl
        {
            get => _bavmUrl;
            set { _bavmUrl = value; OnPropertyChanged(); }
        }

        public string LogFilePath => AppLogger.LogFilePath;

        public int OnlineSourcesCount => Sources.Count(s => s.IsActive);
        public int OnlineDisplaysCount => Displays.Count(d => d.IsActive);

        public ICommand RefreshCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand ClearLogsCommand { get; }
        public ICommand OpenLogFolderCommand { get; }

        public Action? RequestOpenSettings { get; set; }

        public MainViewModel(
            IConfigService configService,
            ISdvoeClient sdvoeClient,
            IRtpReceiver rtpReceiver,
            MulticastIpManager ipManager,
            RoutingOrchestrator orchestrator)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _sdvoeClient = sdvoeClient ?? throw new ArgumentNullException(nameof(sdvoeClient));
            _rtpReceiver = rtpReceiver ?? throw new ArgumentNullException(nameof(rtpReceiver));
            _ipManager = ipManager ?? throw new ArgumentNullException(nameof(ipManager));
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));

            _config = _configService.Load();
            BavmUrl = _config.BavmUrl;

            RefreshCommand = new RelayCommand(async _ => await RefreshDevicesAsync());
            OpenSettingsCommand = new RelayCommand(_ => RequestOpenSettings?.Invoke());
            ClearLogsCommand = new RelayCommand(_ =>
            {
                Logs.Clear();
                AppLogger.ClearMemoryBuffer();
            });
            OpenLogFolderCommand = new RelayCommand(_ =>
            {
                try
                {
                    string dir = Path.GetDirectoryName(AppLogger.LogFilePath) ?? AppDomain.CurrentDomain.BaseDirectory;
                    Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    AppLogger.Error("UI", "Failed to open log folder", ex);
                }
            });

            _sdvoeClient.DeviceStateChanged += OnDeviceStateChanged;

            // Load existing logs into memory buffer
            foreach (var log in AppLogger.RecentLogs)
            {
                Logs.Add(log);
            }

            // Hook up live log streaming
            AppLogger.LogEntryAdded += entry =>
            {
                if (Application.Current != null && Application.Current.Dispatcher != null)
                {
                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        Logs.Add(entry);
                        if (Logs.Count > 1000) Logs.RemoveAt(0);
                    });
                }
            };

            InitializeLayouts();
        }

        public async Task InitializeAsync()
        {
            _config = _configService.Load();
            BavmUrl = _config.BavmUrl;
            string host = _config.DerivedControlServerIp;

            AppLogger.Info("APP", $"Initializing AVAS Routing Tool with BAVM URL: {BavmUrl}, Control Server: {host}:{_config.TelnetPort} (REST: {_config.RestPort})");

            _rtpReceiver.StartListening(new[] { _config.BasePort, 5000 }, _config.LocalNetworkInterfaceIp);

            ConnectionStatus = $"Connecting to {host}:{_config.TelnetPort}...";
            bool connected = await _sdvoeClient.ConnectAsync(host, _config.TelnetPort, _config.RestPort);

            IsConnected = connected;
            ConnectionStatus = connected ? $"Connected: {host}:{_config.TelnetPort}" : $"Offline: {host}:{_config.TelnetPort}";

            await RefreshDevicesAsync();
        }

        private void InitializeLayouts()
        {
            Layouts.Clear();
            Layouts.Add(new LayoutItemViewModel { Id = "off", Title = "Off", Subtitle = "Mute video", IsPhase1Supported = true });
            Layouts.Add(new LayoutItemViewModel { Id = "single", Title = "Single View", Subtitle = "Full screen (1:1)", IsPhase1Supported = true, IsSelected = true });
            Layouts.Add(new LayoutItemViewModel { Id = "matrix2x2", Title = "Matrix 2x2", Subtitle = "4-quad grid", IsPhase1Supported = false });
            Layouts.Add(new LayoutItemViewModel { Id = "pip", Title = "PIP", Subtitle = "Picture-in-picture", IsPhase1Supported = false });
            Layouts.Add(new LayoutItemViewModel { Id = "side_by_side", Title = "Side-by-Side", Subtitle = "Split dual view", IsPhase1Supported = false });
            Layouts.Add(new LayoutItemViewModel { Id = "quad_1_3", Title = "1+3 Quad", Subtitle = "1 Master + 3 sub", IsPhase1Supported = false });
        }

        public async Task RefreshDevicesAsync()
        {
            AppLogger.Info("DISCOVERY", "Starting transceiver refresh cycle...");
            var devices = await _sdvoeClient.DiscoverDevicesAsync();

            // Dynamic Cards: populate TX Sources and RX Displays in deterministic order
            Sources.Clear();
            Displays.Clear();

            var transmitters = devices.Where(d => d.IsTransmitter).OrderBy(d => d.DeviceName).ToList();
            var receivers = devices.Where(d => d.IsReceiver).OrderBy(d => d.DeviceName).ToList();

            foreach (var dev in transmitters)
            {
                var sourceVm = new SourceCardViewModel(dev);
                Sources.Add(sourceVm);

                if (dev.IsActive && dev.IsPreviewSupported)
                {
                    // Allocate sequential multicast IP in order
                    string mcastIp = _ipManager.AllocateIp(dev.DeviceId);
                    
                    // Delay 0.5s upon startup after handshake before starting preview
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(500);
                        await sourceVm.StartPreviewAsync(_rtpReceiver, _sdvoeClient, mcastIp, 5000);
                    });
                }
                else if (!dev.IsPreviewSupported)
                {
                    AppLogger.Info("PREVIEW", $"Skipping preview auto-start for non-AVAS device: '{dev.DeviceName}' (VendorId: {dev.VendorId})");
                }
            }

            foreach (var dev in receivers)
            {
                var displayVm = new DisplayCardViewModel(dev, UnrouteVideoAsync);
                Displays.Add(displayVm);
            }

            AppLogger.Info("DISCOVERY", $"Transceivers populated: {Sources.Count} Sources (TX), {Displays.Count} Displays (RX).");

            OnPropertyChanged(nameof(OnlineSourcesCount));
            OnPropertyChanged(nameof(OnlineDisplaysCount));
        }

        public async Task<bool> RouteVideoAsync(AvasDevice sourceTx, AvasDevice displayRx)
        {
            if (sourceTx == null || displayRx == null) return false;

            AppLogger.Info("ROUTING", $"Requesting route: '{sourceTx.DeviceName}' ({sourceTx.DeviceId}) -> '{displayRx.DeviceName}' ({displayRx.DeviceId})");
            bool ok = await _orchestrator.RouteSingleViewVideoAsync(sourceTx, displayRx);
            if (ok)
            {
                var displayVm = Displays.FirstOrDefault(d => d.MacAddress == displayRx.DeviceId);
                displayVm?.NotifyRoutingChanged();
                AppLogger.Info("ROUTING", $"Successfully routed video feed to '{displayRx.DeviceName}'.");
            }
            else
            {
                AppLogger.Warn("ROUTING", $"Failed to route video feed to '{displayRx.DeviceName}'.");
            }
            return ok;
        }

        public async Task<bool> UnrouteVideoAsync(AvasDevice displayRx)
        {
            if (displayRx == null) return false;

            AppLogger.Info("ROUTING", $"Requesting unroute on '{displayRx.DeviceName}' ({displayRx.DeviceId})");
            bool ok = await _orchestrator.UnrouteDisplayAsync(displayRx);
            if (ok)
            {
                var displayVm = Displays.FirstOrDefault(d => d.MacAddress == displayRx.DeviceId);
                displayVm?.NotifyRoutingChanged();
                AppLogger.Info("ROUTING", $"Successfully unrouted '{displayRx.DeviceName}'.");
            }
            else
            {
                AppLogger.Warn("ROUTING", $"Failed to unroute '{displayRx.DeviceName}'.");
            }
            return ok;
        }

        private void OnDeviceStateChanged(string mac, bool isOnline)
        {
            AppLogger.Info("DEVICE-STATUS", $"Device {mac} changed status: {(isOnline ? "ONLINE" : "OFFLINE")}");

            var source = Sources.FirstOrDefault(s => s.MacAddress == mac);
            if (source != null)
            {
                source.SetOnlineState(isOnline);
            }

            var display = Displays.FirstOrDefault(d => d.MacAddress == mac);
            if (display != null)
            {
                display.SetOnlineState(isOnline);
            }

            OnPropertyChanged(nameof(OnlineSourcesCount));
            OnPropertyChanged(nameof(OnlineDisplaysCount));
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
