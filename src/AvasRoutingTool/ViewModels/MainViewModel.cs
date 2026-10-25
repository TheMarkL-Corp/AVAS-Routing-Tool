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
using AvasRoutingTool.Sdvoe.MultiView;

namespace AvasRoutingTool.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly IConfigService _configService;
        private readonly ISdvoeClient _sdvoeClient;
        private readonly IRtpReceiver _rtpReceiver;
        private readonly MulticastIpManager _ipManager;
        private readonly RoutingOrchestrator _orchestrator;
        private readonly MultiViewOrchestrator _multiView;

        /// <summary>
        /// The multiview arrangement each card shows, by receiver MAC (interim until the Composer: every drop is
        /// taken at once, so this is the live draft, or an empty one on a layout chosen but not yet filled).
        /// </summary>
        private readonly System.Collections.Generic.Dictionary<string, MultiViewDraft> _drafts = new();

        /// <summary>
        /// One at a time per display (by receiver MAC): a drop reads the card's draft, takes it and writes it back, so
        /// a second drop or an Off must not run in between. Not re-entrant.
        /// </summary>
        private readonly System.Collections.Generic.Dictionary<string, System.Threading.SemaphoreSlim> _displayLocks = new();

        /// <summary>Subscription indexes of multiview layouts found on decoders that this tool did not create, by receiver MAC.</summary>
        private readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<int>> _externalSubscriptions = new();

        /// <summary>Limits how many receivers are read at once.</summary>
        private readonly System.Threading.SemaphoreSlim _restoreGate = new(8);
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
            RoutingOrchestrator orchestrator,
            MultiViewOrchestrator? multiView = null)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _sdvoeClient = sdvoeClient ?? throw new ArgumentNullException(nameof(sdvoeClient));
            _rtpReceiver = rtpReceiver ?? throw new ArgumentNullException(nameof(rtpReceiver));
            _ipManager = ipManager ?? throw new ArgumentNullException(nameof(ipManager));
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _multiView = multiView ?? new MultiViewOrchestrator(sdvoeClient);

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
            Layouts.Add(new LayoutItemViewModel { Id = "off", Title = "Off", Subtitle = "Mute video", IsAvailable = true });
            Layouts.Add(new LayoutItemViewModel { Id = "single", Title = "Single View", Subtitle = "Full screen (1:1)", IsAvailable = true, IsSelected = true });
            Layouts.Add(new LayoutItemViewModel { Id = "matrix2x2", Title = "Matrix 2x2", Subtitle = "4-quad grid", IsAvailable = true });
            Layouts.Add(new LayoutItemViewModel { Id = "pip", Title = "PIP", Subtitle = "Picture-in-picture", IsAvailable = true });
            Layouts.Add(new LayoutItemViewModel { Id = "side_by_side", Title = "Side-by-Side", Subtitle = "Split dual view", IsAvailable = true });
            Layouts.Add(new LayoutItemViewModel { Id = "quad_1_3", Title = "1+3 Quad", Subtitle = "1 Master + 3 sub", IsAvailable = true });
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
                if (!string.IsNullOrEmpty(dev.ActiveSourceId))
                {
                    var sourceVm = Sources.FirstOrDefault(s => s.MacAddress == dev.ActiveSourceId);
                    displayVm.SetPreviewSource(sourceVm);
                }
                Displays.Add(displayVm);
            }

            AppLogger.Info("DISCOVERY", $"Transceivers populated: {Sources.Count} Sources (TX), {Displays.Count} Displays (RX).");

            OnPropertyChanged(nameof(OnlineSourcesCount));
            OnPropertyChanged(nameof(OnlineDisplaysCount));

            // Show what the decoders are really doing instead of assuming "unrouted".
            await RestoreStateAsync();
        }

        /// <summary>
        /// Reads every online receiver (in parallel, read-only) and makes its card show the hardware's truth:
        /// the Single View source, a multiview layout this tool can drive, or a read-only external layout.
        /// A receiver that cannot be read keeps its card as it is.
        /// </summary>
        public async Task RestoreStateAsync()
        {
            var targets = Displays.Where(d => d.IsActive).ToList();
            if (targets.Count == 0) return;

            var reads = await Task.WhenAll(targets.Select(async d => (Display: d, State: await ReadStateAsync(d))));
            foreach (var (display, state) in reads)
            {
                if (Displays.Contains(display)) ApplyRestoredState(display, state);
            }
        }

        /// <summary>Re-reads one receiver (for example when it comes back online).</summary>
        public async Task RestoreDisplayAsync(DisplayCardViewModel display)
        {
            var state = await ReadStateAsync(display);
            if (Displays.Contains(display)) ApplyRestoredState(display, state);
        }

        private async Task<RxState?> ReadStateAsync(DisplayCardViewModel display)
        {
            await _restoreGate.WaitAsync();
            try
            {
                return await _sdvoeClient.ReadReceiverStateAsync(display.MacAddress);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("RESTORE", $"Reading '{display.DeviceName}' failed: {ex.Message}");
                return null;
            }
            finally
            {
                _restoreGate.Release();
            }
        }

        private void ApplyRestoredState(DisplayCardViewModel display, RxState? state)
        {
            var rx = display.Device;
            string key = SdvoeClient.NormalizeMac(rx.DeviceId);

            if (state == null)
            {
                AppLogger.Info("RESTORE", $"'{rx.DeviceName}' could not be read; leaving its card as it is.");
                return;
            }

            _externalSubscriptions.Remove(key);
            _drafts.Remove(key);

            if (!state.IsMultiView)
            {
                _multiView.Forget(rx);
                display.ClearLayout();
                display.ClearExternalLayout();

                var route = state.SingleViewSource;
                if (route == null)
                {
                    rx.ActiveSourceId = string.Empty;
                    rx.ActiveSourceName = string.Empty;
                    display.SetPreviewSource(null);
                    AppLogger.Info("RESTORE", $"'{rx.DeviceName}' is not routed.");
                }
                else
                {
                    var sourceVm = FindSource(route.SourceMac);
                    rx.ActiveSourceId = route.SourceMac;
                    rx.ActiveSourceName = sourceVm?.DeviceName ?? route.SourceName;
                    display.SetPreviewSource(sourceVm);
                    AppLogger.Info("RESTORE", $"'{rx.DeviceName}' is routed to '{rx.ActiveSourceName}'.");
                }
                display.NotifyRoutingChanged();
                return;
            }

            var restored = MultiViewRestore.Analyze(state)!;
            rx.ActiveSourceId = string.Empty;
            rx.ActiveSourceName = string.Empty;
            display.SetPreviewSource(null);

            if (restored.IsRecognised)
            {
                var draft = restored.Draft!;
                _multiView.Adopt(rx, draft, restored.Canvas, restored.SubscriptionIndexes);
                _drafts[key] = draft.Clone();
                display.ApplyLayout(draft.Plan, LayoutTitle(draft.Plan.Id), _multiView.DescribeCanvas(rx));
                ShowDraft(display, draft);
                AppLogger.Info("RESTORE", $"'{rx.DeviceName}' runs {draft.Plan.Id} with {draft.Placements.Count} filled block(s).");
            }
            else
            {
                _multiView.Forget(rx);
                _externalSubscriptions[key] = restored.SubscriptionIndexes;
                display.SetExternalLayout(restored.LayoutName, restored.Sources.Select(s => s.Name).ToList(), $"OUT {restored.Canvas.Description}");
                AppLogger.Info("RESTORE", $"'{rx.DeviceName}' runs an external multiview layout '{restored.LayoutName}'.");
            }
            display.NotifyRoutingChanged();
        }

        private SourceCardViewModel? FindSource(string mac) =>
            Sources.FirstOrDefault(s => SdvoeClient.NormalizeMac(s.MacAddress) == SdvoeClient.NormalizeMac(mac));

        /// <summary>A source the app has not discovered: known only by what the decoder reports (no preview).</summary>
        private static AvasDevice UnknownSource(string mac, string name) =>
            new() { DeviceId = mac, DeviceName = string.IsNullOrEmpty(name) ? mac : name, IsTransmitter = true, IsActive = true, VendorId = 0 };

        public async Task<bool> RouteVideoAsync(AvasDevice sourceTx, AvasDevice displayRx)
        {
            if (sourceTx == null || displayRx == null) return false;

            // A multiview decoder must go back to genlocked full screen before a plain join takes effect.
            if (_multiView.IsMultiView(displayRx) && !await _multiView.ReturnToSingleViewAsync(displayRx))
            {
                AppLogger.Warn("ROUTING", $"Could not return '{displayRx.DeviceName}' to single view; route not sent.");
                return false;
            }

            string rxKey = SdvoeClient.NormalizeMac(displayRx.DeviceId);
            if (_drafts.Remove(rxKey))
                Displays.FirstOrDefault(d => d.MacAddress == displayRx.DeviceId)?.ClearLayout();
            if (_externalSubscriptions.ContainsKey(rxKey))
            {
                // Found running a layout set elsewhere: leave multiview first or the join would not take effect.
                if (!await _multiView.ExitExternalAsync(displayRx, Array.Empty<int>()))
                {
                    AppLogger.Warn("ROUTING", $"Could not return '{displayRx.DeviceName}' to single view; route not sent.");
                    return false;
                }
                _externalSubscriptions.Remove(rxKey);
                Displays.FirstOrDefault(d => d.MacAddress == displayRx.DeviceId)?.ClearExternalLayout();
            }

            AppLogger.Info("ROUTING", $"Requesting route: '{sourceTx.DeviceName}' ({sourceTx.DeviceId}) -> '{displayRx.DeviceName}' ({displayRx.DeviceId})");
            bool ok = await _orchestrator.RouteSingleViewVideoAsync(sourceTx, displayRx);
            if (ok)
            {
                var displayVm = Displays.FirstOrDefault(d => d.MacAddress == displayRx.DeviceId);
                var sourceVm = Sources.FirstOrDefault(s => s.MacAddress == sourceTx.DeviceId);
                displayVm?.SetPreviewSource(sourceVm);
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

            string rxKey = SdvoeClient.NormalizeMac(displayRx.DeviceId);
            var gate = DisplayLock(rxKey); // an Off must not interleave with a drop on the same display
            await gate.WaitAsync();
            try
            {
                return await UnrouteLockedAsync(displayRx, rxKey);
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task<bool> UnrouteLockedAsync(AvasDevice displayRx, string rxKey)
        {
            AppLogger.Info("ROUTING", $"Requesting unroute on '{displayRx.DeviceName}' ({displayRx.DeviceId})");
            bool wasMultiView = _multiView.IsMultiView(displayRx);
            bool wasExternal = _externalSubscriptions.TryGetValue(rxKey, out var externalIndexes);
            bool ok = wasExternal
                ? await _multiView.ExitExternalAsync(displayRx, externalIndexes!)
                : wasMultiView
                    ? await _multiView.TurnOffAsync(displayRx)
                    : await _orchestrator.UnrouteDisplayAsync(displayRx);
            if (ok)
            {
                var displayVm = Displays.FirstOrDefault(d => d.MacAddress == displayRx.DeviceId);
                displayVm?.SetPreviewSource(null);
                if (_drafts.Remove(rxKey)) displayVm?.ClearLayout(); // the card showed a layout, live or only chosen
                if (wasExternal)
                {
                    _externalSubscriptions.Remove(rxKey);
                    displayVm?.ClearExternalLayout();
                }
                displayVm?.NotifyRoutingChanged();
                AppLogger.Info("ROUTING", $"Successfully unrouted '{displayRx.DeviceName}'.");
            }
            else
            {
                AppLogger.Warn("ROUTING", $"Failed to unroute '{displayRx.DeviceName}'.");
            }
            return ok;
        }

        /// <summary>
        /// Applies a layout tile to a display. <c>single</c> returns it to full screen; a multiview preset only
        /// prepares empty drop zones (hardware is touched when the first source lands). A source already routed
        /// in Single View is carried into the first window so the picture does not disappear.
        /// </summary>
        public async Task<bool> ApplyLayoutAsync(string layoutId, AvasDevice displayRx)
        {
            if (displayRx == null) return false;

            var displayVm = Displays.FirstOrDefault(d => d.MacAddress == displayRx.DeviceId);
            if (displayVm == null || !displayVm.IsActive)
            {
                AppLogger.Warn("MULTIVIEW", $"Layout '{layoutId}' ignored: display '{displayRx.DeviceName}' is not available.");
                return false;
            }

            string rxKey = SdvoeClient.NormalizeMac(displayRx.DeviceId);

            if (string.Equals(layoutId, "single", StringComparison.OrdinalIgnoreCase))
            {
                if (_externalSubscriptions.ContainsKey(rxKey))
                {
                    if (!await _multiView.ExitExternalAsync(displayRx, Array.Empty<int>()))
                    {
                        displayVm.LastError = $"'{displayRx.DeviceName}' did not accept the switch back to Single View.";
                        return false;
                    }
                    _externalSubscriptions.Remove(rxKey);
                    displayVm.ClearExternalLayout();
                    return true;
                }

                // A layout chosen but never filled changed no device: only the card goes back.
                if (_multiView.IsMultiView(displayRx) && !await _multiView.ReturnToSingleViewAsync(displayRx))
                {
                    displayVm.LastError = $"'{displayRx.DeviceName}' did not accept the switch back to Single View.";
                    return false;
                }
                if (_drafts.Remove(rxKey)) displayVm.ClearLayout();
                return true;
            }

            if (!MultiViewPresets.IsMultiView(layoutId))
            {
                AppLogger.Warn("MULTIVIEW", $"'{layoutId}' is not a multiview layout.");
                return false;
            }

            AvasDevice? carried = string.IsNullOrEmpty(displayRx.ActiveSourceId)
                ? null
                : Sources.FirstOrDefault(s => s.MacAddress == displayRx.ActiveSourceId)?.Device;

            MultiViewLayoutPlan plan;
            MultiViewCanvas canvas;
            try
            {
                (plan, canvas) = await _multiView.PlanForDisplayAsync(displayRx, layoutId);
            }
            catch (ArgumentException ex)
            {
                displayVm.LastError = ex.Message;
                AppLogger.Warn("MULTIVIEW", $"Layout '{layoutId}' cannot be built: {ex.Message}");
                return false;
            }

            _externalSubscriptions.Remove(rxKey);
            displayRx.ActiveSourceId = string.Empty;
            displayRx.ActiveSourceName = string.Empty;

            _drafts[rxKey] = new MultiViewDraft(plan);
            displayVm.ApplyLayout(plan, LayoutTitle(layoutId), MultiViewCanvasPolicy.Summarize(canvas, plan));
            displayVm.NotifyRoutingChanged();
            AppLogger.Info("MULTIVIEW", $"'{displayRx.DeviceName}' switched to {plan.Id} ({plan.Slots.Count} windows).");

            return carried == null || await AssignSlotAsync(displayRx, plan.Slots[0].Index, carried);
        }

        /// <summary>
        /// Places a source in one block of the card's multiview layout and takes the result at once (interim until
        /// the Composer stages drops). A source already shown in a block of the same size is added, not moved.
        /// </summary>
        public async Task<bool> AssignSlotAsync(AvasDevice displayRx, int slotIndex, AvasDevice sourceTx)
        {
            if (displayRx == null || sourceTx == null) return false;

            string rxKey = SdvoeClient.NormalizeMac(displayRx.DeviceId);
            var gate = DisplayLock(rxKey); // the next drop must start from the draft this one leaves behind
            await gate.WaitAsync();
            try
            {
                return await AssignSlotLockedAsync(displayRx, rxKey, slotIndex, sourceTx);
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task<bool> AssignSlotLockedAsync(AvasDevice displayRx, string rxKey, int slotIndex, AvasDevice sourceTx)
        {
            var displayVm = Displays.FirstOrDefault(d => d.MacAddress == displayRx.DeviceId);
            if (displayVm == null || !_drafts.TryGetValue(rxKey, out var shown)) return false;

            bool Refuse(string? reason)
            {
                displayVm.LastError = reason;
                AppLogger.Warn("MULTIVIEW", $"Block {slotIndex} on '{displayRx.DeviceName}' refused '{sourceTx.DeviceName}': {reason}");
                return false;
            }

            if (!sourceTx.IsTransmitter) return Refuse($"'{sourceTx.DeviceName}' is not a transmitter.");

            var draft = shown.Clone();
            var placed = DraftValidator.CanPlace(draft, slotIndex, sourceTx.DeviceId, sourceTx.DeviceName,
                _multiView.UsesExcept(displayRx), isOnline: _ => sourceTx.IsActive);
            if (!placed.Ok) return Refuse(placed.Reason);
            draft.Place(slotIndex, sourceTx.DeviceId, sourceTx.DeviceName);

            var result = await _multiView.TakeAsync(displayRx, draft);

            // The display was swapped since the layout was chosen: redraw the blocks for its real output and take again.
            bool redrawn = false;
            if (result.Outcome == TakeOutcome.DisplayChanged && result.NewPlan != null)
            {
                draft = draft.WithLayout(result.NewPlan);
                redrawn = true;
                result = await _multiView.TakeAsync(displayRx, draft);
            }

            bool applied = result.Outcome is TakeOutcome.Applied or TakeOutcome.NothingToTake;
            if (redrawn)
            {
                displayVm.ApplyLayout(draft.Plan, LayoutTitle(draft.Plan.Id), applied ? _multiView.DescribeCanvas(displayRx) : string.Empty);
                if (!applied) _drafts[rxKey] = new MultiViewDraft(draft.Plan); // the card now shows the redrawn, empty blocks
            }

            if (!applied) return Refuse(result.Error);

            _drafts[rxKey] = draft;
            ShowDraft(displayVm, draft);
            displayVm.LastError = null;
            displayVm.NotifyRoutingChanged();
            return true;
        }

        /// <summary>Makes the card's blocks show the draft's sources (only the blocks that differ are touched).</summary>
        private void ShowDraft(DisplayCardViewModel display, MultiViewDraft draft)
        {
            foreach (var slot in display.Slots.ToList())
            {
                draft.Placements.TryGetValue(slot.Index, out var placement);
                string want = placement == null ? string.Empty : SdvoeClient.NormalizeMac(placement.SourceMac);
                if (SdvoeClient.NormalizeMac(slot.SourceMac) == want) continue;

                display.AssignSlot(slot.Index, placement == null
                    ? null
                    : FindSource(placement.SourceMac) ?? new SourceCardViewModel(UnknownSource(placement.SourceMac, placement.SourceName)));
            }
        }

        private System.Threading.SemaphoreSlim DisplayLock(string rxKey)
        {
            lock (_displayLocks)
            {
                if (!_displayLocks.TryGetValue(rxKey, out var gate))
                    _displayLocks[rxKey] = gate = new System.Threading.SemaphoreSlim(1, 1);
                return gate;
            }
        }

        private string LayoutTitle(string layoutId) =>
            (Layouts.FirstOrDefault(l => l.Id == layoutId)?.Title ?? layoutId).ToUpperInvariant();

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
                if (isOnline) _ = RestoreDisplayAsync(display); // a decoder that came back may be showing something
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
