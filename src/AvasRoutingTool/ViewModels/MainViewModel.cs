using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using AvasRoutingTool.Configuration;
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

        public int OnlineSourcesCount => Sources.Count(s => s.IsActive);
        public int OnlineDisplaysCount => Displays.Count(d => d.IsActive);

        public ICommand RefreshCommand { get; }
        public ICommand OpenSettingsCommand { get; }

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

            _sdvoeClient.DeviceStateChanged += OnDeviceStateChanged;

            InitializeLayouts();
        }

        public async Task InitializeAsync()
        {
            _config = _configService.Load();
            BavmUrl = _config.BavmUrl;
            string host = _config.DerivedControlServerIp;

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
            var devices = await _sdvoeClient.DiscoverDevicesAsync();

            // Dynamic Cards: populate TX Sources and RX Displays
            Sources.Clear();
            Displays.Clear();

            foreach (var dev in devices)
            {
                if (dev.IsTransmitter)
                {
                    var sourceVm = new SourceCardViewModel(dev);
                    Sources.Add(sourceVm);

                    if (dev.IsActive)
                    {
                        string mcastIp = _ipManager.AllocateIp(dev.DeviceId);
                        _ = sourceVm.StartPreviewAsync(_rtpReceiver, _sdvoeClient, mcastIp, _config.BasePort);
                    }
                }
                else if (dev.IsReceiver)
                {
                    var displayVm = new DisplayCardViewModel(dev, UnrouteVideoAsync);
                    Displays.Add(displayVm);
                }
            }

            OnPropertyChanged(nameof(OnlineSourcesCount));
            OnPropertyChanged(nameof(OnlineDisplaysCount));
        }

        public async Task<bool> RouteVideoAsync(AvasDevice sourceTx, AvasDevice displayRx)
        {
            if (sourceTx == null || displayRx == null) return false;

            bool ok = await _orchestrator.RouteSingleViewVideoAsync(sourceTx, displayRx);
            if (ok)
            {
                var displayVm = Displays.FirstOrDefault(d => d.MacAddress == displayRx.DeviceId);
                displayVm?.NotifyRoutingChanged();
            }
            return ok;
        }

        public async Task<bool> UnrouteVideoAsync(AvasDevice displayRx)
        {
            if (displayRx == null) return false;

            bool ok = await _orchestrator.UnrouteDisplayAsync(displayRx);
            if (ok)
            {
                var displayVm = Displays.FirstOrDefault(d => d.MacAddress == displayRx.DeviceId);
                displayVm?.NotifyRoutingChanged();
            }
            return ok;
        }

        private void OnDeviceStateChanged(string mac, bool isOnline)
        {
            // Dynamic Cards: live update status
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
