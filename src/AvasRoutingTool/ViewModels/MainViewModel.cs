using System;
using System.Collections.Generic;
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
using AvasRoutingTool.Services;

namespace AvasRoutingTool.ViewModels
{
    /// <summary>What the app bar's connection dot says: the connect call is pending, the control server answered, or it refused.</summary>
    public enum ConnectionHealth
    {
        Connecting,
        Connected,
        Offline
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        /// <summary>An armed source is disarmed after this long without placing or arming (spec 5.1).</summary>
        public static readonly TimeSpan ArmIdleTimeout = TimeSpan.FromSeconds(15);

        /// <summary>The drop choice on a MultiView card closes by itself after this long (spec 5.1).</summary>
        public static readonly TimeSpan DropChoiceTimeout = TimeSpan.FromSeconds(10);

        private readonly IConfigService _configService;
        private readonly ISdvoeClient _sdvoeClient;
        private readonly IRtpReceiver _rtpReceiver;
        private readonly MulticastIpManager _ipManager;
        private readonly RoutingOrchestrator _orchestrator;
        private readonly MultiViewOrchestrator _multiView;
        private readonly Func<DateTime> _clock;
        private readonly Action<Action> _post;
        private readonly MotionPolicy _motion;

        /// <summary>The notice on a draft whose screen was replaced by a copy (spec 15, "After the drop").</summary>
        public const string StaleDraftNotice = "Screen changed - draft is based on an older picture";

        /// <summary>Subscription indexes of multiview layouts found on decoders that this tool did not create, by receiver MAC.</summary>
        private readonly Dictionary<string, IReadOnlyList<int>> _externalSubscriptions = new();

        /// <summary>
        /// The external layout each display runs as last read (or copied there), with its subscriptions and output
        /// format, by receiver MAC: what a copy of that display sends. Valid while the MAC is in <see cref="_externalSubscriptions"/>.
        /// </summary>
        private readonly Dictionary<string, (RxLayout? Layout, IReadOnlyList<RxSubscription> Subscriptions, MultiViewCanvas Canvas)> _externalScreens = new();

        /// <summary>Display EDIDs read for the current copy arm / drag (by receiver MAC; null = unreadable). Cleared on disarm.</summary>
        private readonly Dictionary<string, EdidInfo?> _copyEdids = new();

        /// <summary>Targets with a copy (or a copy Undo) in flight: a second tap is ignored, never queued.</summary>
        private readonly HashSet<string> _copiesInFlight = new();

        private DisplayCardViewModel? _armedDisplay;
        private int _armSeq;

        /// <summary>The armed display's EDID reads are still in flight: taps and drops on targets are ignored meanwhile.</summary>
        private bool _armReading;

        /// <summary>
        /// Composer drafts with changes that are not on screen, kept when the Composer was closed (by receiver MAC).
        /// The card shows "DRAFT"; opening the Composer again resumes it. A restore never overwrites one.
        /// </summary>
        private readonly Dictionary<string, MultiViewDraft> _keptDrafts = new();

        /// <summary>When each open drop choice opened, so <see cref="Tick"/> can close it on time.</summary>
        private readonly Dictionary<DisplayCardViewModel, DateTime> _dropChoiceOpenedAt = new();

        /// <summary>Limits how many receivers are read at once.</summary>
        private readonly System.Threading.SemaphoreSlim _restoreGate = new(8);

        /// <summary>Restored states are applied one at a time (the reads run in parallel).</summary>
        private readonly object _restoreApply = new();

        private AppConfig _config;

        private bool _isConnected;
        private string _connectionStatus = "Connecting...";
        private string _bavmUrl = "http://localhost:80";

        private MultiViewComposerViewModel? _composer;
        private DisplayCardViewModel? _composerDisplay;
        private SourceCardViewModel? _armed;
        private DateTime _lastArmActivity;
        private string _draftSignature = string.Empty;

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

        private ConnectionHealth _connectionHealth = ConnectionHealth.Connecting;

        /// <summary>The app bar dot: Connecting until the connect call returns, then Connected or Offline.</summary>
        public ConnectionHealth ConnectionHealth
        {
            get => _connectionHealth;
            set
            {
                _connectionHealth = value;
                OnPropertyChanged();
            }
        }

        public string BavmUrl
        {
            get => _bavmUrl;
            set { _bavmUrl = value; OnPropertyChanged(); }
        }

        public string LogFilePath => AppLogger.LogFilePath;

        private bool _showLogs;
        /// <summary>Whether the Logs tab is offered. Off by default; switched on in Settings.</summary>
        public bool ShowLogs
        {
            get => _showLogs;
            set { if (_showLogs == value) return; _showLogs = value; OnPropertyChanged(); }
        }

        private bool _showAdvanced;
        /// <summary>Whether the Advanced tab is offered. Off by default; switched on in Settings.</summary>
        public bool ShowAdvanced
        {
            get => _showAdvanced;
            set { if (_showAdvanced == value) return; _showAdvanced = value; OnPropertyChanged(); }
        }

        public int OnlineSourcesCount => Sources.Count(s => s.IsActive);
        public int OnlineDisplaysCount => Displays.Count(d => d.IsActive);

        /// <summary>The MultiView Composer that is open (one at a time), or null. It replaces the Displays row while open.</summary>
        public MultiViewComposerViewModel? ActiveComposer => _composer;

        /// <summary>A Composer is open: it replaces the Displays row and the Sources row shrinks to the compact palette.</summary>
        public bool IsComposing => _composer != null;

        /// <summary>
        /// The source tapped to place. In the overview a tap on a display routes it (Single View); while the Composer
        /// is open this is the Composer's armed source (one state, mirrored).
        /// </summary>
        public SourceCardViewModel? ArmedSource => _armed;

        /// <summary>The display whose "Copy layout" is armed (tap a display to copy onto it), or null. One armed thing at a time.</summary>
        public DisplayCardViewModel? ArmedDisplay => _armedDisplay;

        /// <summary>An arm banner shows: a source is armed (placing) or a display is armed (copying).</summary>
        public bool HasArmBanner => ArmBannerText != null || CopyArmBanner != null;

        /// <summary>
        /// The arm banner while a display is armed for copying: "Copying RX1 - Matrix 2x2. Tap a display. Tap Copy layout again to stop."
        /// Null when no display is armed. Never non-null together with <see cref="ArmBannerText"/> (one armed thing at a time).
        /// </summary>
        public string? CopyArmBanner =>
            _armedDisplay == null
                ? null
                : $"Copying {_armedDisplay.DeviceName} - {CurrentScreen(_armedDisplay).Title()}. Tap a display. Tap Copy layout again to stop.";

        /// <summary>
        /// The arm banner while a source is armed (spec 5.1): "Placing TX2 - tap a display or a block. Tap TX2 again to stop."
        /// Follows the overview's arm and the open Composer's arm (they are one state). Null when no source is armed.
        /// </summary>
        public string? ArmBannerText =>
            _armed == null
                ? null
                : $"Placing {_armed.DeviceName} - tap a display or a block. Tap {_armed.DeviceName} again to stop.";

        /// <summary>
        /// A display is armed for copying (or a copy drag runs), or a drop choice is open on a card: the overview gives
        /// the displays the room (compact sources, no layout bar) so every target can say its state in words.
        /// </summary>
        public bool IsCopyOrChoiceActive => _armedDisplay != null || Displays.Any(d => d.IsDropChoiceOpen);

        private bool _copyOrChoiceActive;

        private void RaiseCopyOrChoice()
        {
            bool now = IsCopyOrChoiceActive;
            if (now == _copyOrChoiceActive) return;
            _copyOrChoiceActive = now;
            OnPropertyChanged(nameof(IsCopyOrChoiceActive));
        }

        // ---- reduced motion ----

        /// <summary>Windows animations are on. Off (Show animations unchecked): every transition is skipped.</summary>
        public bool AnimationsEnabled => _motion.AnimationsEnabled;

        /// <summary>How long a toast or banner fades in (150 ms; zero when animations are off).</summary>
        public TimeSpan FadeDuration => _motion.Fade;

        /// <summary><paramref name="normal"/> when animations are on, <see cref="TimeSpan.Zero"/> when they are off.</summary>
        public TimeSpan TransitionDuration(TimeSpan normal) => _motion.Resolve(normal);

        public IThemeService ThemeService { get; }
        public ICommand ToggleThemeCommand { get; }
        public ThemeMode CurrentTheme => ThemeService.CurrentTheme;
        public bool IsLightMode => ThemeService.EffectiveTheme == ThemeMode.Light;

        public string ThemeButtonToolTip => IsLightMode
            ? "Current theme: Clinical Light. Click to switch to Slate Dark."
            : "Current theme: Slate Dark. Click to switch to Clinical Light.";

        public ICommand RefreshCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand ClearLogsCommand { get; }
        public ICommand OpenLogFolderCommand { get; }

        public Action? RequestOpenSettings { get; set; }

        /// <param name="clock">Time source for the arm idle timeout, the drop choice timeout and the Composer's Undo window; tests inject one.</param>
        /// <param name="animationsEnabled">Whether Windows animations are on (default <see cref="SystemParameters.ClientAreaAnimation"/>); off skips every transition, in the overview and in its Composers.</param>
        /// <param name="post">
        /// Runs an action on the UI thread. Device events arrive on the client's read loop thread and go through it
        /// before they touch any view model. Default: the WPF dispatcher (begin-invoke) when there is one, else inline;
        /// tests inject an inline or a recording poster.
        /// </param>
        public MainViewModel(
            IConfigService configService,
            ISdvoeClient sdvoeClient,
            IRtpReceiver rtpReceiver,
            MulticastIpManager ipManager,
            RoutingOrchestrator orchestrator,
            MultiViewOrchestrator? multiView = null,
            Func<DateTime>? clock = null,
            Action<Action>? post = null,
            Func<bool>? animationsEnabled = null,
            IThemeService? themeService = null)
        {
            _motion = new MotionPolicy(animationsEnabled);
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _sdvoeClient = sdvoeClient ?? throw new ArgumentNullException(nameof(sdvoeClient));
            _rtpReceiver = rtpReceiver ?? throw new ArgumentNullException(nameof(rtpReceiver));
            _ipManager = ipManager ?? throw new ArgumentNullException(nameof(ipManager));
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _multiView = multiView ?? new MultiViewOrchestrator(sdvoeClient);
            _clock = clock ?? (() => DateTime.UtcNow);
            _post = post ?? PostToDispatcher;
            _multiView.Clock = _clock; // one time source: the copy Undo window runs on this view model's clock

            // Every card shown gets the copy commands (cards are added by discovery, or directly by tests).
            Displays.CollectionChanged += (_, e) =>
            {
                if (e.NewItems == null) return;
                foreach (DisplayCardViewModel d in e.NewItems) d.UseCopyActions(ToggleCopyArmAsync, UndoScreenAsync);
            };

            _config = _configService.Load();
            BavmUrl = _config.BavmUrl;
            ShowLogs = _config.ShowLogs;
            ShowAdvanced = _config.ShowAdvanced;

            ThemeService = themeService ?? new ThemeService(_configService);
            ThemeService.ThemeChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(CurrentTheme));
                OnPropertyChanged(nameof(IsLightMode));
                OnPropertyChanged(nameof(ThemeButtonToolTip));
            };
            ToggleThemeCommand = new RelayCommand(_ => ThemeService.ToggleTheme());

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
            ShowLogs = _config.ShowLogs;
            ShowAdvanced = _config.ShowAdvanced;
            string host = _config.DerivedControlServerIp;

            AppLogger.Info("APP", $"Initializing AVAS Routing Tool with BAVM URL: {BavmUrl}, Control Server: {host}:{_config.TelnetPort} (REST: {_config.RestPort})");

            _rtpReceiver.StartListening(new[] { _config.BasePort, 5000 }, _config.LocalNetworkInterfaceIp);

            ConnectionStatus = $"Connecting to {host}:{_config.TelnetPort}...";
            ConnectionHealth = ConnectionHealth.Connecting;
            bool connected = await _sdvoeClient.ConnectAsync(host, _config.TelnetPort, _config.RestPort);

            IsConnected = connected;
            ConnectionStatus = connected ? $"Connected: {host}:{_config.TelnetPort}" : $"Offline: {host}:{_config.TelnetPort}";
            ConnectionHealth = connected ? ConnectionHealth.Connected : ConnectionHealth.Offline;

            await RefreshDevicesAsync();
        }

        private void InitializeLayouts()
        {
            Layouts.Clear();
            foreach (var layout in LayoutItemViewModel.CreateCatalog())
            {
                layout.IsSelected = layout.Id == "single";
                Layouts.Add(layout);
            }
        }

        public async Task RefreshDevicesAsync()
        {
            AppLogger.Info("DISCOVERY", "Starting transceiver refresh cycle...");
            var devices = await _sdvoeClient.DiscoverDevicesAsync();

            // The cards are rebuilt: a Composer open on an old card is closed (its draft is kept by receiver MAC).
            CloseComposer();
            SetArmed(null);
            DisarmDisplay();
            foreach (var d in _dropChoiceOpenedAt.Keys.ToList()) CloseDropChoice(d);

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
                displayVm.HasDraft = _keptDrafts.ContainsKey(Key(dev));
                Displays.Add(displayVm);
            }

            AppLogger.Info("DISCOVERY", $"Transceivers populated: {Sources.Count} Sources (TX), {Displays.Count} Displays (RX).");

            OnPropertyChanged(nameof(OnlineSourcesCount));
            OnPropertyChanged(nameof(OnlineDisplaysCount));

            // Show what the decoders are really doing instead of assuming "unrouted".
            await RestoreStateAsync();
        }

        // ---- restore from the hardware ----

        /// <summary>
        /// Reads every online receiver (in parallel, read-only) and makes its card show the hardware's truth:
        /// the Single View source, a multiview layout this tool can drive, or a read-only external layout.
        /// A receiver that cannot be read keeps its card as it is. Each read and its result run as the only operation
        /// on that display, so a Take or Route in flight is never overwritten by a state read before it finished.
        /// </summary>
        public async Task RestoreStateAsync()
        {
            var targets = Displays.Where(d => d.IsActive).ToList();
            if (targets.Count == 0) return;

            await Task.WhenAll(targets.Select(RestoreOneAsync));
        }

        /// <summary>Re-reads one receiver (for example when it comes back online).</summary>
        public Task RestoreDisplayAsync(DisplayCardViewModel display) => RestoreOneAsync(display);

        private Task RestoreOneAsync(DisplayCardViewModel display) =>
            _multiView.RunExclusiveAsync(display.Device, async () =>
            {
                var state = await ReadStateAsync(display);
                if (Displays.Contains(display))
                {
                    lock (_restoreApply) ApplyRestoredState(display, state);
                }
                return true;
            });

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
            string key = Key(rx);

            if (state == null)
            {
                AppLogger.Info("RESTORE", $"'{rx.DeviceName}' could not be read; leaving its card as it is.");
                return;
            }

            // The hardware's truth replaces what we believed: a copy's Undo (from before that) no longer applies.
            WithdrawScreenUndo(rx, display);
            _externalSubscriptions.Remove(key);
            _externalScreens.Remove(key);

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
                // Only what the hardware reports: a decoder still in another mode (nothing converts it until a route) shows no format.
                if (route != null && string.Equals(state.DisplayMode, "FAST_SWITCHED", StringComparison.OrdinalIgnoreCase) && state.Width > 0 && state.Height > 0)
                    display.SetSingleViewOutput($"OUT {state.Width}x{state.Height}@{state.Fps}");
                RefreshKeptDraft(display);
                AnnounceScreen(display);
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
                display.ApplyLayout(draft.Plan, LayoutTitle(draft.Plan.Id), _multiView.DescribeCanvas(rx));
                ShowDraft(display, draft);
                AppLogger.Info("RESTORE", $"'{rx.DeviceName}' runs {draft.Plan.Id} with {draft.Placements.Count} filled block(s).");
            }
            else
            {
                _multiView.AdoptExternal(rx, restored.SubscriptionIndexes,
                    MultiViewRestore.ExternalUses(rx.DeviceName, state.Layout, state.Subscriptions)); // its encoders' window sizes count for the other displays
                _externalSubscriptions[key] = restored.SubscriptionIndexes;
                _externalScreens[key] = (state.Layout, state.Subscriptions, restored.Canvas); // what a copy of it sends
                display.SetExternalLayout(restored.LayoutName, restored.Sources.Select(s => s.Name).ToList(), $"OUT {restored.Canvas.Description}");
                AppLogger.Info("RESTORE", $"'{rx.DeviceName}' runs an external multiview layout '{restored.LayoutName}'.");
            }
            display.NotifyRoutingChanged();
            RefreshKeptDraft(display); // a kept draft that now equals the screen drops its chip; a rebuilt card keeps no stale one
            AnnounceScreen(display);
        }

        // ---- Single View routing and Off ----

        /// <summary>
        /// Routes the source to the display in Single View (leaving a multiview first). Runs as the only operation on
        /// the display: a Take in flight there finishes first, then this route runs.
        /// </summary>
        public Task<bool> RouteVideoAsync(AvasDevice sourceTx, AvasDevice displayRx)
        {
            if (sourceTx == null || displayRx == null) return Task.FromResult(false);
            return _multiView.RunExclusiveAsync(displayRx, () => RouteLockedAsync(sourceTx, displayRx));
        }

        private async Task<bool> RouteLockedAsync(AvasDevice sourceTx, AvasDevice displayRx)
        {
            string rxKey = Key(displayRx);
            var displayVm = FindDisplay(displayRx);
            WithdrawScreenUndo(displayRx, displayVm); // a deliberate change: a copy's Undo must not overwrite it
            if (displayVm != null) displayVm.LastError = null; // a new attempt: an earlier refusal must not outlive a route that works

            // A multiview decoder must go back to Single View (Fast Switch) before a plain join takes effect.
            if (_multiView.IsMultiView(displayRx))
            {
                if (!await _multiView.ReturnToSingleViewAsync(displayRx))
                {
                    AppLogger.Warn("ROUTING", $"Could not return '{displayRx.DeviceName}' to single view; route not sent.");
                    if (displayVm != null) displayVm.LastError = $"Could not return '{displayRx.DeviceName}' to Single View; the route was not sent.";
                    return false;
                }
                displayVm?.ClearLayout();
            }
            if (_externalSubscriptions.TryGetValue(rxKey, out var externalIndexes))
            {
                // Found running a layout set elsewhere: leave multiview first or the join would not take effect.
                // Its stream-1 subscriptions are left too (Ruling R18); index 0 is reused by the join.
                if (!await _multiView.ExitExternalAsync(displayRx, MultiViewOrchestrator.StreamOneJoins(externalIndexes)))
                {
                    AppLogger.Warn("ROUTING", $"Could not return '{displayRx.DeviceName}' to single view; route not sent.");
                    if (displayVm != null) displayVm.LastError = $"Could not return '{displayRx.DeviceName}' to Single View; the route was not sent.";
                    return false;
                }
                _externalSubscriptions.Remove(rxKey);
                _externalScreens.Remove(rxKey);
                displayVm?.ClearExternalLayout();
            }

            AppLogger.Info("ROUTING", $"Requesting route: '{sourceTx.DeviceName}' ({sourceTx.DeviceId}) -> '{displayRx.DeviceName}' ({displayRx.DeviceId})");
            var route = await _orchestrator.RouteSingleViewAsync(sourceTx, displayRx);
            bool ok = route.Ok;
            if (ok)
            {
                var sourceVm = Sources.FirstOrDefault(s => s.MacAddress == sourceTx.DeviceId);
                displayVm?.SetPreviewSource(sourceVm);
                displayVm?.NotifyRoutingChanged();
                displayVm?.SetSingleViewOutput(MultiViewCanvasPolicy.OutputLabel(route.Format!)); // after the notify, which drops a line made by an earlier route
                AppLogger.Info("ROUTING", $"Successfully routed video feed to '{displayRx.DeviceName}'.");
            }
            else
            {
                AppLogger.Warn("ROUTING", $"Failed to route video feed to '{displayRx.DeviceName}': {route.Error}");
                if (displayVm != null) displayVm.LastError = route.Error;
            }
            if (displayVm != null) RefreshKeptDraft(displayVm);
            AnnounceScreen(displayVm);
            return ok;
        }

        /// <summary>Turns the display off (leaves a multiview, or the Single View route). Waits for a Take in flight there.</summary>
        public Task<bool> UnrouteVideoAsync(AvasDevice displayRx)
        {
            if (displayRx == null) return Task.FromResult(false);
            return _multiView.RunExclusiveAsync(displayRx, () => UnrouteLockedAsync(displayRx, Key(displayRx)));
        }

        private async Task<bool> UnrouteLockedAsync(AvasDevice displayRx, string rxKey)
        {
            AppLogger.Info("ROUTING", $"Requesting unroute on '{displayRx.DeviceName}' ({displayRx.DeviceId})");
            WithdrawScreenUndo(displayRx, FindDisplay(displayRx)); // a deliberate change: a copy's Undo must not overwrite it
            bool wasMultiView = _multiView.IsMultiView(displayRx);
            bool wasExternal = _externalSubscriptions.TryGetValue(rxKey, out var externalIndexes);
            bool ok = wasExternal
                ? await _multiView.ExitExternalAsync(displayRx, externalIndexes!)
                : wasMultiView
                    ? await _multiView.TurnOffAsync(displayRx)
                    : await _orchestrator.UnrouteDisplayAsync(displayRx);
            var displayVm = FindDisplay(displayRx);
            if (ok)
            {
                displayVm?.SetPreviewSource(null);
                if (displayVm?.IsMultiView == true) displayVm.ClearLayout();
                if (wasExternal)
                {
                    _externalSubscriptions.Remove(rxKey);
                    _externalScreens.Remove(rxKey);
                    displayVm?.ClearExternalLayout();
                }
                displayVm?.NotifyRoutingChanged();
                AppLogger.Info("ROUTING", $"Successfully unrouted '{displayRx.DeviceName}'.");
            }
            else
            {
                AppLogger.Warn("ROUTING", $"Failed to unroute '{displayRx.DeviceName}'.");
            }
            if (displayVm != null) RefreshKeptDraft(displayVm);
            AnnounceScreen(displayVm);
            return ok;
        }

        /// <summary>
        /// A pick from the card's "Layout" menu (or a layout tile dropped on the card): a MultiView preset opens the
        /// Composer with it staged and changes no device; <c>single</c> returns the display to Single View; <c>off</c>
        /// turns it off.
        /// </summary>
        public async Task<bool> ChooseLayoutFromCardAsync(DisplayCardViewModel display, string layoutId)
        {
            if (display == null) return false;
            display.IsLayoutMenuOpen = false;

            if (string.Equals(layoutId, "off", StringComparison.OrdinalIgnoreCase))
                return await UnrouteVideoAsync(display.Device);

            if (!display.IsActive)
            {
                AppLogger.Warn("MULTIVIEW", $"Layout '{layoutId}' ignored: display '{display.DeviceName}' is not available.");
                return false;
            }

            if (string.Equals(layoutId, "single", StringComparison.OrdinalIgnoreCase))
                return await _multiView.RunExclusiveAsync(display.Device, () => ReturnToSingleLockedAsync(display));

            if (!MultiViewPresets.IsMultiView(layoutId))
            {
                AppLogger.Warn("MULTIVIEW", $"'{layoutId}' is not a multiview layout.");
                return false;
            }

            await OpenComposerAsync(display, layoutId);
            return ReferenceEquals(_composerDisplay, display);
        }

        private async Task<bool> ReturnToSingleLockedAsync(DisplayCardViewModel display)
        {
            var rx = display.Device;
            string rxKey = Key(rx);

            WithdrawScreenUndo(rx, display); // a deliberate change: a copy's Undo must not overwrite it
            if (_externalSubscriptions.TryGetValue(rxKey, out var externalIndexes))
            {
                // The card says UNROUTED afterwards and no source follows to reuse subscription 0: leave it too.
                if (!await _multiView.ExitExternalAsync(rx, MultiViewOrchestrator.StreamOneJoins(externalIndexes), leaveIndexZero: true))
                {
                    display.LastError = $"'{rx.DeviceName}' did not accept the switch back to Single View.";
                    return false;
                }
                _externalSubscriptions.Remove(rxKey);
                _externalScreens.Remove(rxKey);
                display.ClearExternalLayout();
                display.NotifyRoutingChanged();
                AnnounceScreen(display);
                return true;
            }

            // Nothing on screen in multiview: no device to change.
            if (_multiView.IsMultiView(rx))
            {
                if (!await _multiView.ReturnToSingleViewAsync(rx, leaveIndexZero: true))
                {
                    display.LastError = $"'{rx.DeviceName}' did not accept the switch back to Single View.";
                    return false;
                }
                display.ClearLayout();
                display.NotifyRoutingChanged();
                AnnounceScreen(display);
            }
            return true;
        }

        // ---- the Composer ----

        /// <summary>
        /// Opens the Composer for the display (closing one open for another display, which keeps its draft). It
        /// resumes the display's kept draft, else what is on screen when the display shows a MultiView (and
        /// <paramref name="layoutId"/> is null or that layout), else starts at Step 1. A different
        /// <paramref name="layoutId"/> is then staged (reads the display only: no device change), and
        /// <paramref name="armed"/> (or the source armed in the overview) is armed for placing.
        /// </summary>
        public async Task OpenComposerAsync(DisplayCardViewModel d, string? layoutId = null, SourceCardViewModel? armed = null)
        {
            ArgumentNullException.ThrowIfNull(d);
            if (!d.IsActive)
            {
                AppLogger.Warn("COMPOSER", $"Composer not opened: display '{d.DeviceName}' is not available.");
                return;
            }
            if (layoutId != null && !MultiViewPresets.IsMultiView(layoutId))
            {
                AppLogger.Warn("COMPOSER", $"'{layoutId}' is not a multiview layout.");
                return;
            }

            // The source armed in the overview carries over; one armed in another display's Composer does not
            // (closing a Composer stops placing).
            var toArm = armed ?? (_composer == null ? _armed : null);
            foreach (var other in Displays.ToList()) CloseDropChoice(other); // the Composer replaces the row: no card keeps a prompt
            foreach (var other in _dropChoiceOpenedAt.Keys.ToList()) CloseDropChoice(other);
            CloseDropChoice(d);
            DisarmDisplay(); // the Composer replaces the displays row: no copy target is left to tap

            if (_composer != null && ReferenceEquals(_composerDisplay, d))
            {
                // Already open for this display: stage the layout / arm the source there.
                await StageAndArmAsync(_composer, layoutId, armed);
                return;
            }
            CloseComposer();

            string key = Key(d.Device);
            MultiViewDraft? resume;
            string? staleNotice = null;
            if (_keptDrafts.Remove(key, out var kept))
            {
                resume = kept;
                staleNotice = d.DraftNotice;
            }
            else
            {
                var live = _multiView.GetLive(d.Device);
                resume = live != null && (layoutId == null || live.Plan.Id == layoutId) ? live : null;
            }

            var composer = new MultiViewComposerViewModel(d, _multiView, () => Sources, Layouts, resume, _clock, () => AnimationsEnabled);
            _composer = composer;
            _composerDisplay = d;
            d.HasDraft = false;
            d.DraftNotice = null;
            // A draft kept from before a copy replaced the screen: the Composer says so as it resumes.
            if (staleNotice != null) composer.OnLiveChangedElsewhere(_multiView.GetLive(d.Device), staleNotice);
            // Not removed on close: a Take still in flight when the Composer closes must still update the card.
            composer.LiveChanged += _ => OnComposerTook(d);
            composer.PropertyChanged += OnComposerPropertyChanged;
            _draftSignature = Signature(composer.Draft);
            OnPropertyChanged(nameof(ActiveComposer));
            OnPropertyChanged(nameof(IsComposing));
            AppLogger.Info("COMPOSER", $"Composer opened for '{d.DeviceName}'{(resume != null ? $" on {resume.Plan.Id}" : string.Empty)}.");

            await StageAndArmAsync(composer, layoutId, toArm);
        }

        private async Task StageAndArmAsync(MultiViewComposerViewModel composer, string? layoutId, SourceCardViewModel? toArm)
        {
            if (layoutId != null && composer.Draft?.Plan.Id != layoutId)
                await composer.SelectLayoutAsync(layoutId);
            if (!ReferenceEquals(_composer, composer)) return; // closed meanwhile

            // After the layout: staging a layout resets the Composer's transient state, an arm included.
            if (toArm != null && !ReferenceEquals(composer.ArmedSource, toArm))
            {
                composer.Arm(toArm);
                TouchArm();
            }
            SyncFromComposer();
        }

        /// <summary>
        /// Closes the Composer. Changes that are not on screen are kept as the display's draft (the card shows it,
        /// opening the Composer again resumes it); nothing reaches the device. Placing stops.
        /// </summary>
        public void CloseComposer()
        {
            var composer = _composer;
            var d = _composerDisplay;
            if (composer == null || d == null) return;

            composer.PropertyChanged -= OnComposerPropertyChanged;
            string key = Key(d.Device);
            var draft = composer.Draft;
            if (draft != null && DiffersFromScreen(draft, _multiView.GetLive(d.Device)))
                _keptDrafts[key] = draft.Clone();
            else
                _keptDrafts.Remove(key);
            d.HasDraft = _keptDrafts.ContainsKey(key);
            d.DraftNotice = d.HasDraft && composer.Banner.Text == StaleDraftNotice ? StaleDraftNotice : null;

            composer.Dispose(); // it watches the source cards
            _composer = null;
            _composerDisplay = null;
            _draftSignature = string.Empty;
            foreach (var s in Sources) s.UsedInBlocks = 0;
            SetArmed(null);
            OnPropertyChanged(nameof(ActiveComposer));
            OnPropertyChanged(nameof(IsComposing));
            AppLogger.Info("COMPOSER", $"Composer closed for '{d.DeviceName}'{(d.HasDraft ? "; draft kept" : string.Empty)}.");
        }

        /// <summary>A Take or Undo from the Composer put a new picture on screen: the card shows it (one way: never fed back).</summary>
        private void OnComposerTook(DisplayCardViewModel d)
        {
            // The card is resolved by MAC: a device refresh may have rebuilt it while the Take was in flight (a Composer
            // closed meanwhile still reports here), and the card the Composer was opened on is then a stale object.
            var card = FindDisplay(d.Device);
            WithdrawScreenUndo(d.Device, card); // a deliberate change: a copy's Undo must not overwrite it
            if (card == null) return;
            ShowScreen(card);
            RefreshKeptDraft(card); // always: a kept draft that now equals the screen is no draft
            AnnounceScreen(card); // a second Composer opened for it meanwhile follows (idempotent for the one that took)
        }

        /// <summary>
        /// The display was changed deliberately (route, Off, Single View, a Composer Take, a re-read from the hardware):
        /// a copy's Undo from before would overwrite that change, so it is withdrawn with its toast.
        /// </summary>
        private void WithdrawScreenUndo(AvasDevice rx, DisplayCardViewModel? d)
        {
            _multiView.ForgetPreviousScreen(rx);
            d?.ClearUndoScreen();
        }

        /// <summary>
        /// Makes the card show the multiview that is on the display's screen now, read from the orchestrator (not from
        /// the notification): when a Route or Off already followed the Take, the screen is no longer a multiview and
        /// the card is left to them.
        /// </summary>
        private void ShowScreen(DisplayCardViewModel d)
        {
            var rx = d.Device;
            var live = _multiView.GetLive(rx);
            if (live == null) return;

            _externalSubscriptions.Remove(Key(rx));
            rx.ActiveSourceId = string.Empty;
            rx.ActiveSourceName = string.Empty;
            d.ApplyLayout(live.Plan, LayoutTitle(live.Plan.Id), _multiView.DescribeCanvas(rx)); // also clears LastError
            ShowDraft(d, live);
            d.NotifyRoutingChanged();
        }

        /// <summary>The display's picture changed outside the Composer (route, Off, restore): an open Composer follows it.</summary>
        private void AnnounceScreen(DisplayCardViewModel? d)
        {
            if (d == null || _composer == null || !ReferenceEquals(_composerDisplay, d)) return;
            _composer.OnLiveChangedElsewhere(_multiView.GetLive(d.Device));
        }

        /// <summary>A kept draft that the screen now shows (for example after a Take sent it) is no longer a draft.</summary>
        private void RefreshKeptDraft(DisplayCardViewModel d)
        {
            string key = Key(d.Device);
            if (_keptDrafts.TryGetValue(key, out var kept) && !DiffersFromScreen(kept, _multiView.GetLive(d.Device)))
                _keptDrafts.Remove(key);
            d.HasDraft = _keptDrafts.ContainsKey(key);
            if (!d.HasDraft) d.DraftNotice = null;
        }

        private static bool DiffersFromScreen(MultiViewDraft draft, MultiViewDraft? live) =>
            live == null
                ? draft.Placements.Count > 0
                : draft.Plan.Name != live.Plan.Name || draft.DiffFrom(live).Count > 0;

        private void OnComposerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!ReferenceEquals(sender, _composer)) return;
            if (e.PropertyName is nameof(MultiViewComposerViewModel.ArmedSource) or nameof(MultiViewComposerViewModel.Draft))
                SyncFromComposer();
        }

        /// <summary>Mirrors the Composer's arm onto the source cards, counts their blocks, and notes placing as activity.</summary>
        private void SyncFromComposer()
        {
            var composer = _composer;
            if (composer == null) return;

            var armed = composer.ArmedSource;
            if (!ReferenceEquals(armed, _armed))
            {
                SetArmed(armed);
                if (armed != null) TouchArm();
            }

            var draft = composer.Draft;
            var counts = draft == null
                ? new Dictionary<string, int>()
                : draft.Placements.Values.GroupBy(p => SdvoeClient.NormalizeMac(p.SourceMac)).ToDictionary(g => g.Key, g => g.Count());
            foreach (var s in Sources)
                s.UsedInBlocks = counts.TryGetValue(SdvoeClient.NormalizeMac(s.MacAddress), out int n) ? n : 0;

            string signature = Signature(draft);
            if (signature != _draftSignature)
            {
                _draftSignature = signature;
                if (_armed != null) TouchArm(); // a placement (or any edit of the picture) counts as activity
            }
        }

        private static string Signature(MultiViewDraft? draft) =>
            draft == null
                ? string.Empty
                : draft.Plan.Name + "|" + string.Join(",", draft.Placements.OrderBy(p => p.Key)
                    .Select(p => $"{p.Key}={SdvoeClient.NormalizeMac(p.Value.SourceMac)}"));

        // ---- tap-to-arm and the drop choice ----

        /// <summary>
        /// Arms the source (tap again to stop). One source at a time; while the Composer is open this arms it there.
        /// </summary>
        public void ArmSource(SourceCardViewModel s)
        {
            if (s == null) return;
            if (_composer != null)
            {
                _composer.Arm(s); // toggles; mirrored back through SyncFromComposer
                SyncFromComposer();
                TouchArm();
                return;
            }

            DisarmDisplay(); // one armed thing at a time: a source or a display to copy
            SetArmed(ReferenceEquals(_armed, s) ? null : s);
            TouchArm();
        }

        /// <summary>
        /// A tap on a display card. With a source armed: a Single View or unrouted card is routed now (Single View
        /// stays immediate) and the source is disarmed; a MultiView card asks first (the drop choice). Nothing armed:
        /// does nothing (the card's Layout menu opens the Composer). With a display armed for copying: an allowed card
        /// is copied onto now and the copy disarms; a blocked card (or the source itself) does nothing, its reason
        /// stays on the card.
        /// </summary>
        public async Task TapDisplayAsync(DisplayCardViewModel d)
        {
            if (d == null || _composer != null) return;
            if (_armedDisplay != null)
            {
                await TapCopyTargetAsync(_armedDisplay, d);
                return;
            }

            var s = _armed;
            if (s == null || !d.IsActive) return;
            TouchArm();

            if (d.IsMultiView)
            {
                OpenDropChoice(d, s);
                return;
            }

            if (await RouteVideoAsync(s.Device, d.Device) && ReferenceEquals(_armed, s))
                SetArmed(null); // one Single View route disarms
        }

        /// <summary>
        /// A source dropped on a display card (or on a block of its mini view): a MultiView card asks first (the drop
        /// choice), any other card routes it in Single View now. A non-transmitter is refused.
        /// </summary>
        public async Task<bool> DropSourceAsync(DisplayCardViewModel d, AvasDevice tx)
        {
            if (d == null || tx == null || !tx.IsTransmitter || !d.IsActive) return false;

            if (d.IsMultiView)
            {
                OpenDropChoice(d, FindSource(tx.DeviceId) ?? new SourceCardViewModel(tx));
                return true;
            }
            return await RouteVideoAsync(tx, d.Device);
        }

        /// <summary>Opens the two-choice prompt on a MultiView card: Full screen (Single View) or Into a block... It closes after 10 s.</summary>
        public void OpenDropChoice(DisplayCardViewModel d, SourceCardViewModel s)
        {
            if (d == null || s == null) return;
            foreach (var other in _dropChoiceOpenedAt.Keys.Where(o => !ReferenceEquals(o, d)).ToList()) CloseDropChoice(other);
            d.OpenDropChoice(s);
            _dropChoiceOpenedAt[d] = _clock();
            TouchArm();
            RaiseCopyOrChoice();
        }

        /// <summary>The drop choice's "Full screen (Single View)": leaves MultiView and routes the source now.</summary>
        public async Task<bool> ChooseFullScreenAsync(DisplayCardViewModel d)
        {
            var s = d?.DropChoiceSource;
            if (d == null || s == null) return false;
            CloseDropChoice(d);

            bool ok = await RouteVideoAsync(s.Device, d.Device);
            if (ok && ReferenceEquals(_armed, s)) SetArmed(null);
            return ok;
        }

        /// <summary>The drop choice's "Into a block...": opens the Composer on what is on screen with the source armed.</summary>
        public async Task ChooseIntoBlockAsync(DisplayCardViewModel d)
        {
            var s = d?.DropChoiceSource;
            if (d == null || s == null) return;
            CloseDropChoice(d);
            await OpenComposerAsync(d, null, s);
        }

        private void CloseDropChoice(DisplayCardViewModel d)
        {
            _dropChoiceOpenedAt.Remove(d);
            d.CloseDropChoice();
            RaiseCopyOrChoice();
        }

        /// <summary>
        /// A tap on a card's picture (spec 5.1: "tapping the composite opens the Composer"). With nothing armed and no
        /// drop choice open, a MultiView card opens the Composer on what is on screen (or its kept draft); otherwise it
        /// is a plain card tap (<see cref="TapDisplayAsync"/>: route an armed source, copy an armed display).
        /// </summary>
        public async Task TapPictureAsync(DisplayCardViewModel d)
        {
            if (d == null) return;
            if (_composer == null && _armed == null && _armedDisplay == null && !d.IsDropChoiceOpen && d.IsMultiView)
            {
                await OpenComposerAsync(d);
                return;
            }
            if (d.IsDropChoiceOpen) return; // the choice decides; a tap beside its buttons does not ask again
            await TapDisplayAsync(d);
        }

        /// <summary>
        /// Time-based housekeeping; the view calls it from a timer. Disarms after <see cref="ArmIdleTimeout"/> without
        /// activity (arming, placing, editing in the Composer), closes a drop choice after <see cref="DropChoiceTimeout"/>,
        /// and lets the Composer withdraw its Undo on time.
        /// </summary>
        public void Tick()
        {
            var now = _clock();

            if (_armed != null && now - _lastArmActivity >= ArmIdleTimeout)
            {
                AppLogger.Info("UI", $"'{_armed.DeviceName}' disarmed after {ArmIdleTimeout.TotalSeconds:0} s idle.");
                if (_composer != null)
                {
                    _composer.Disarm();
                    SyncFromComposer();
                }
                SetArmed(null);
            }

            // A display armed for copying (by a tap) stops after the same idle time; a drag keeps it alive (CopyDecisionFor).
            if (_armedDisplay != null && now - _lastCopyActivity >= ArmIdleTimeout)
            {
                AppLogger.Info("COPY", $"'{_armedDisplay.DeviceName}' copy disarmed after {ArmIdleTimeout.TotalSeconds:0} s idle.");
                DisarmDisplay();
            }

            foreach (var (d, openedAt) in _dropChoiceOpenedAt.ToList())
            {
                if (!d.IsDropChoiceOpen) _dropChoiceOpenedAt.Remove(d);
                else if (now - openedAt >= DropChoiceTimeout) CloseDropChoice(d);
            }
            RaiseCopyOrChoice();

            // A copy's Undo toast goes when the target's previous picture is no longer kept (10 s).
            foreach (var d in Displays.Where(x => x.CanUndoScreen).ToList())
            {
                if (_multiView.PreviousScreen(d.Device) == null) d.ClearUndoScreen();
            }

            _composer?.Tick();
        }

        private void TouchArm() => _lastArmActivity = _clock();

        /// <summary>When the copy arm last saw activity (armed, a target asked about by a drag or a tap).</summary>
        private DateTime _lastCopyActivity;

        /// <summary>Exactly one source card (or none) shows as armed.</summary>
        private void SetArmed(SourceCardViewModel? s)
        {
            var previous = _armed;
            _armed = s;
            if (previous != null && !ReferenceEquals(previous, s)) previous.IsArmed = false;
            foreach (var source in Sources) source.IsArmed = ReferenceEquals(source, s);
            if (s != null) s.IsArmed = true;
            if (!ReferenceEquals(previous, s))
            {
                OnPropertyChanged(nameof(ArmedSource));
                OnPropertyChanged(nameof(ArmBannerText));
                OnPropertyChanged(nameof(HasArmBanner));
            }
        }

        // ---- copy a display onto another (spec 15) ----

        /// <summary>
        /// What the display shows on screen now (never a draft): Nothing when it is offline or unrouted; the live
        /// multiview this tool drives (its session canvas; subscriptions as the surface planner sends them); the
        /// external layout last read from (or copied to) it, with its subscriptions and running format; or the Single
        /// View source at the display's EDID preferred mode (Ruling R13: genlocked Single View has no output format
        /// of its own in the model; the EDID read for the current copy arm is used, the 1920x1080@60 fallback when
        /// none was read).
        /// </summary>
        public ScreenState CurrentScreen(DisplayCardViewModel d)
        {
            ArgumentNullException.ThrowIfNull(d);
            var none = Array.Empty<RxSubscription>();
            if (!d.IsActive) return new ScreenState(ScreenKind.Nothing, 0, 0, 0, null, null, null, null, none);

            var rx = d.Device;
            string key = Key(rx);

            var live = _multiView.GetLive(rx);
            var canvas = _multiView.GetCanvas(rx);
            if (live != null && canvas != null)
                return new ScreenState(ScreenKind.MultiView, canvas.Width, canvas.Height, canvas.Fps, null, null, live, null,
                    LiveSubscriptions(live, rx)) { CanvasIsCopy = _multiView.CanvasIsCopy(rx) };

            if (_externalSubscriptions.ContainsKey(key))
            {
                return _externalScreens.TryGetValue(key, out var external)
                    ? new ScreenState(ScreenKind.External, external.Canvas.Width, external.Canvas.Height, external.Canvas.Fps,
                        null, null, null, external.Layout, external.Subscriptions)
                    : new ScreenState(ScreenKind.External, 0, 0, 0, null, null, null, null, none);
            }

            if (!string.IsNullOrEmpty(rx.ActiveSourceId))
            {
                _copyEdids.TryGetValue(key, out var edid);
                var format = MultiViewCanvasPolicy.Choose(edid);
                return new ScreenState(ScreenKind.Single, format.Width, format.Height, format.Fps,
                    rx.ActiveSourceId, rx.ActiveSourceName, null, null, none);
            }

            return new ScreenState(ScreenKind.Nothing, 0, 0, 0, null, null, null, null, none);
        }

        /// <summary>
        /// Arms the display's "Copy layout" (also the start of a copy drag): reads every other online display's EDID
        /// once, in parallel and read-only (and the source's own for a Single View source, for its output format),
        /// caches them for this arm, and shows every other card as an allowed or blocked target. Disarms an armed
        /// source. Does nothing for a display showing nothing, or while the Composer is open.
        /// </summary>
        public async Task ArmDisplayAsync(DisplayCardViewModel d)
        {
            if (d == null || _composer != null || ReferenceEquals(_armedDisplay, d)) return;
            if (!d.CanCopyFrom || CurrentScreen(d).Kind == ScreenKind.Nothing)
            {
                AppLogger.Info("COPY", $"'{d.DeviceName}' shows nothing to copy; not armed.");
                return;
            }

            DisarmDisplay();
            SetArmed(null);
            _armedDisplay = d;
            _lastCopyActivity = _clock();
            int token = _armSeq;
            d.SetCopySource(true);
            OnPropertyChanged(nameof(ArmedDisplay));
            OnPropertyChanged(nameof(CopyArmBanner));
            OnPropertyChanged(nameof(HasArmBanner));
            RaiseCopyOrChoice();

            var toRead = Displays.Where(o => o.IsActive && !ReferenceEquals(o, d)).ToList();
            if (CurrentScreen(d).Kind == ScreenKind.Single) toRead.Add(d);
            _armReading = true; // until the reads are in, a tap or drop is ignored (no false "Can't read" block)
            List<(string Key, EdidInfo? Edid)> read;
            try { read = await ReadEdidsAsync(toRead); }
            finally { if (token == _armSeq) _armReading = false; } // never left set by a read that threw
            if (token != _armSeq || !ReferenceEquals(_armedDisplay, d)) return; // disarmed (or re-armed) meanwhile

            foreach (var (key, edid) in read) _copyEdids[key] = edid;
            RefreshCopyTargets();
            OnPropertyChanged(nameof(CopyArmBanner));
            OnPropertyChanged(nameof(HasArmBanner));
            AppLogger.Info("COPY", $"'{d.DeviceName}' armed for copying ({read.Count} display(s) read).");
        }

        /// <summary>Stops copying: no source, no target state on any card, and the EDIDs read for the arm are dropped.</summary>
        public void DisarmDisplay()
        {
            var was = _armedDisplay;
            _armedDisplay = null;
            _armSeq++;
            _armReading = false;
            _copyEdids.Clear();
            foreach (var o in Displays)
            {
                o.SetCopyTarget(null);
                o.SetCopySource(false);
            }
            was?.SetCopySource(false);
            if (was == null) return;
            OnPropertyChanged(nameof(ArmedDisplay));
            OnPropertyChanged(nameof(CopyArmBanner));
            OnPropertyChanged(nameof(HasArmBanner));
            RaiseCopyOrChoice();
        }

        /// <summary>
        /// Whether the armed display may be copied onto <paramref name="target"/> (spec 15 table), from the EDIDs read
        /// when arming. With nothing armed: blocked.
        /// </summary>
        public CopyDecision CopyDecisionFor(DisplayCardViewModel target)
        {
            ArgumentNullException.ThrowIfNull(target);
            var source = _armedDisplay;
            if (source == null) return new CopyDecision(false, string.Empty, null, "Tap Copy layout on a display first");
            _lastCopyActivity = _clock(); // a drag over a card, or a tap on one: the copy is in use
            return Decide(source, target);
        }

        /// <summary>
        /// Copies what <paramref name="source"/> shows onto <paramref name="target"/> now (a drop, or a tap while armed)
        /// when the copy rule allows it; a blocked copy sends nothing. Uses the EDIDs of the current arm/drag, else
        /// reads what it needs once. Ignored (false) while a copy to the same target is in flight. On success the
        /// target shows a 10 s Undo toast; on failure its card says why and that nothing changed.
        /// </summary>
        public async Task<bool> CopyDisplayAsync(DisplayCardViewModel source, DisplayCardViewModel target)
        {
            if (source == null || target == null) return false;
            if (ReferenceEquals(_armedDisplay, source) && _armReading)
            {
                AppLogger.Info("COPY", $"Drop on '{target.DeviceName}' ignored: the displays are still being read.");
                return false;
            }
            string key = Key(target.Device);
            if (!_copiesInFlight.Add(key))
            {
                AppLogger.Info("COPY", $"A copy to '{target.DeviceName}' is already in flight; ignored.");
                return false;
            }

            try
            {
                if (!ReferenceEquals(_armedDisplay, source) || !_copyEdids.ContainsKey(key))
                {
                    var toRead = new List<DisplayCardViewModel>();
                    if (target.IsActive && !ReferenceEquals(source, target)) toRead.Add(target);
                    if (CurrentScreen(source).Kind == ScreenKind.Single) toRead.Add(source);
                    foreach (var (k, edid) in await ReadEdidsAsync(toRead)) _copyEdids[k] = edid;
                }
                return await ApplyCopyAsync(source, target, Decide(source, target), CurrentScreen(source));
            }
            finally
            {
                _copiesInFlight.Remove(key);
            }
        }

        /// <summary>
        /// The target's toast Undo: puts back what it showed before the copy (Single View source, its own MultiView,
        /// an external layout, or nothing). False when the 10 s are over or the device refused.
        /// </summary>
        public async Task<bool> UndoScreenAsync(DisplayCardViewModel d)
        {
            if (d == null) return false;
            var previous = _multiView.PreviousScreen(d.Device);
            if (previous == null)
            {
                d.ClearUndoScreen();
                return false;
            }

            string key = Key(d.Device);
            if (!_copiesInFlight.Add(key)) return false;
            try
            {
                d.ClearUndoScreen();
                bool ok = await _multiView.RunExclusiveAsync(d.Device, async () =>
                {
                    // A Route, Off or Take queued ahead of this click may have withdrawn (or a copy replaced) the
                    // snapshot meanwhile: only the snapshot still current is put back, else nothing is sent.
                    if (!ReferenceEquals(_multiView.PreviousScreen(d.Device), previous))
                    {
                        AppLogger.Info("COPY", $"Undo on '{d.DeviceName}' dropped: the display was changed since the copy.");
                        return false;
                    }
                    bool done = await _multiView.RestoreScreenAsync(d.Device, previous);
                    if (!Displays.Contains(d)) return done;
                    if (done)
                        ShowScreenState(d, previous.Kind == ScreenKind.External && previous.External != null
                            ? MultiViewOrchestrator.ExternalCopyOf(previous, d.MacAddress)
                            : previous);
                    else
                        d.LastError = $"{d.DeviceName} did not accept the Undo; it still shows the copy.";
                    return done;
                });
                if (ok)
                {
                    AnnounceScreen(d);
                    RefreshKeptDraft(d);
                    AppLogger.Info("COPY", $"'{d.DeviceName}' shows {previous.Describe()} again.");
                }
                return ok;
            }
            finally
            {
                _copiesInFlight.Remove(key);
            }
        }

        /// <summary>
        /// Esc, one step at a time. With the Composer open it is the Composer's ladder (and never closes it). Otherwise:
        /// stop copying, else put down the armed source, else close a drop choice. True when it did something.
        /// </summary>
        public bool Escape()
        {
            if (_composer != null) return _composer.Escape();

            if (_armedDisplay != null)
            {
                DisarmDisplay();
                return true;
            }
            if (_armed != null)
            {
                SetArmed(null);
                return true;
            }
            var choice = Displays.FirstOrDefault(d => d.IsDropChoiceOpen);
            if (choice != null)
            {
                CloseDropChoice(choice);
                return true;
            }
            return false;
        }

        /// <summary>The card's "Copy layout" handle: arms the display, or stops when it is the armed one.</summary>
        public async Task ToggleCopyArmAsync(DisplayCardViewModel d)
        {
            if (ReferenceEquals(_armedDisplay, d)) DisarmDisplay();
            else await ArmDisplayAsync(d);
        }

        /// <summary>A tap on a card while a display is armed for copying.</summary>
        private async Task TapCopyTargetAsync(DisplayCardViewModel source, DisplayCardViewModel d)
        {
            if (ReferenceEquals(source, d)) return; // tap Copy layout again to stop
            if (_armReading) return; // the displays are still being read: no decision yet

            var decision = CopyDecisionFor(d);
            if (!decision.Allowed)
            {
                d.SetCopyTarget(decision); // nothing happens; the reason stays on the card
                return;
            }

            string key = Key(d.Device);
            if (!_copiesInFlight.Add(key)) return;
            try
            {
                // The picture is taken with the arm's EDIDs (a Single View source's format), before disarming drops them.
                var screen = CurrentScreen(source);
                DisarmDisplay(); // the decision is taken: copy now
                await ApplyCopyAsync(source, d, decision, screen);
            }
            finally
            {
                _copiesInFlight.Remove(key);
            }
        }

        /// <param name="screen">The source's picture as it is now (its lock is not taken); the target's is captured under its lock.</param>
        private async Task<bool> ApplyCopyAsync(DisplayCardViewModel source, DisplayCardViewModel target, CopyDecision decision, ScreenState screen)
        {
            if (!decision.Allowed)
            {
                AppLogger.Info("COPY", $"Copy '{source.DeviceName}' -> '{target.DeviceName}' blocked: {decision.Reason}");
                return false;
            }

            var result = await _multiView.RunExclusiveAsync(target.Device, async () =>
            {
                var before = CurrentScreen(target);
                var r = await _multiView.CopyAsync(target.Device, screen, before);
                if (r.Ok && Displays.Contains(target)) ShowScreenState(target, r.Now ?? screen);
                return r;
            });

            if (!result.Ok)
            {
                string reason = (result.Error ?? "The copy failed").TrimEnd('.', ' ');
                if (result.LeftAsItWas)
                {
                    target.LastError = $"{reason}. Nothing changed on {target.DeviceName}.";
                }
                else
                {
                    // The copy changed the target and putting it back failed: show what the hardware really does now.
                    if (Displays.Contains(target)) await RestoreDisplayAsync(target);
                    target.LastError = $"{reason}. {target.DeviceName} could not be put back.";
                }
                return false;
            }

            target.ShowUndoScreen($"{target.DeviceName} now shows {source.DeviceName}'s {screen.Title()}");
            MarkDraftStale(target);
            return true;
        }

        /// <summary>A copy replaced the target's screen: its draft (open in the Composer, or kept) stays, marked as based on an older picture.</summary>
        private void MarkDraftStale(DisplayCardViewModel target)
        {
            if (_composer != null && ReferenceEquals(_composerDisplay, target))
            {
                _composer.OnLiveChangedElsewhere(_multiView.GetLive(target.Device), StaleDraftNotice);
                return;
            }
            if (!_keptDrafts.ContainsKey(Key(target.Device))) return;
            RefreshKeptDraft(target);
            if (target.HasDraft) target.DraftNotice = StaleDraftNotice;
        }

        /// <summary>Makes the card (and the external-layout bookkeeping) show a picture the target now has on screen.</summary>
        private void ShowScreenState(DisplayCardViewModel d, ScreenState now)
        {
            var rx = d.Device;
            string key = Key(rx);
            switch (now.Kind)
            {
                case ScreenKind.MultiView:
                    _externalScreens.Remove(key);
                    ShowScreen(d); // from the orchestrator's session; drops the external bookkeeping
                    return;

                case ScreenKind.External when now.External != null:
                    rx.ActiveSourceId = string.Empty;
                    rx.ActiveSourceName = string.Empty;
                    _externalSubscriptions[key] = now.Subscriptions.Select(s => s.Index).OrderBy(i => i).ToList();
                    _externalScreens[key] = (now.External, now.Subscriptions,
                        new MultiViewCanvas(now.Width, now.Height, now.Fps, false, $"{now.Width}x{now.Height}@{now.Fps}"));
                    var seen = new HashSet<string>();
                    var names = now.Subscriptions.Where(s => seen.Add(SdvoeClient.NormalizeMac(s.SourceMac))).Select(s => s.SourceName).ToList();
                    d.SetExternalLayout(now.External.Name, names, $"OUT {now.Width}x{now.Height}@{now.Fps}");
                    break;

                case ScreenKind.Single when !string.IsNullOrEmpty(now.SourceMac):
                    _externalSubscriptions.Remove(key);
                    _externalScreens.Remove(key);
                    d.ClearLayout();
                    d.SetPreviewSource(FindSource(now.SourceMac));
                    break;

                default:
                    _externalSubscriptions.Remove(key);
                    _externalScreens.Remove(key);
                    rx.ActiveSourceId = string.Empty;
                    rx.ActiveSourceName = string.Empty;
                    d.ClearLayout();
                    d.SetPreviewSource(null);
                    break;
            }
            d.NotifyRoutingChanged();
        }

        private CopyDecision Decide(DisplayCardViewModel source, DisplayCardViewModel target)
        {
            _copyEdids.TryGetValue(Key(target.Device), out var edid);
            bool same = ReferenceEquals(source, target) || Key(source.Device) == Key(target.Device);
            return DisplayCopyPlanner.Decide(source.DeviceName, CurrentScreen(source), target.DeviceName, target.IsActive, same, edid);
        }

        /// <summary>Every card but the armed source shows its copy state.</summary>
        private void RefreshCopyTargets()
        {
            foreach (var o in Displays)
                o.SetCopyTarget(_armedDisplay == null || ReferenceEquals(o, _armedDisplay) ? null : CopyDecisionFor(o));
        }

        /// <summary>Reads the displays' EDIDs in parallel (read-only). A read that fails or throws yields null ("Can't read ...").</summary>
        private async Task<List<(string Key, EdidInfo? Edid)>> ReadEdidsAsync(IEnumerable<DisplayCardViewModel> displays)
        {
            var reads = displays.Select(async o =>
            {
                string? hex = null;
                try
                {
                    hex = await _sdvoeClient.ReadDisplayEdidAsync(o.MacAddress);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("COPY", $"EDID read for '{o.DeviceName}' failed: {ex.Message}");
                }
                return (Key(o.Device), EdidInfo.Parse(hex));
            });
            return (await Task.WhenAll(reads)).ToList();
        }

        /// <summary>The subscriptions a live draft runs on: one per distinct encoder, as the surface planner sends them.</summary>
        private static IReadOnlyList<RxSubscription> LiveSubscriptions(MultiViewDraft live, AvasDevice rx)
        {
            try
            {
                return SurfacePlanner.Plan(live, rx.DeviceId).Sources
                    .Select(s => new RxSubscription(s.Surface, true, "STREAMING", s.Mac,
                        live.Placements.Values.First(p => SdvoeClient.NormalizeMac(p.SourceMac) == s.Mac).SourceName, s.Stream))
                    .ToList();
            }
            catch (InvalidOperationException)
            {
                return Array.Empty<RxSubscription>(); // not plannable (never for the presets); the draft still describes it
            }
        }

        // ---- helpers ----

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

        private SourceCardViewModel? FindSource(string mac) =>
            Sources.FirstOrDefault(s => SdvoeClient.NormalizeMac(s.MacAddress) == SdvoeClient.NormalizeMac(mac));

        private DisplayCardViewModel? FindDisplay(AvasDevice rx) => Displays.FirstOrDefault(d => d.MacAddress == rx.DeviceId);

        /// <summary>A source the app has not discovered: known only by what the decoder reports (no preview).</summary>
        private static AvasDevice UnknownSource(string mac, string name) =>
            new() { DeviceId = mac, DeviceName = string.IsNullOrEmpty(name) ? mac : name, IsTransmitter = true, IsActive = true, VendorId = 0 };

        private static string Key(AvasDevice d) => SdvoeClient.NormalizeMac(d.DeviceId);

        private string LayoutTitle(string layoutId) =>
            (Layouts.FirstOrDefault(l => l.Id == layoutId)?.Title ?? layoutId).ToUpperInvariant();

        /// <summary>The default <c>post</c>: the WPF dispatcher when there is one (begin-invoke, or inline when already on it), else inline.</summary>
        private static void PostToDispatcher(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess()) action();
            else dispatcher.BeginInvoke(action);
        }

        /// <summary>Raised from the client's read loop thread: hops to the UI thread before any view model is touched.</summary>
        private void OnDeviceStateChanged(string mac, bool isOnline)
        {
            AppLogger.Info("DEVICE-STATUS", $"Device {mac} changed status: {(isOnline ? "ONLINE" : "OFFLINE")}");
            _post(() => ApplyDeviceState(mac, isOnline));
        }

        private void ApplyDeviceState(string mac, bool isOnline)
        {
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
                if (_armedDisplay != null && !ReferenceEquals(display, _armedDisplay))
                    display.SetCopyTarget(CopyDecisionFor(display)); // e.g. "PAX355S is offline"
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
