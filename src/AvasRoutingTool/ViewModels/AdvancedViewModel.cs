using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using AvasRoutingTool.Logging;
using AvasRoutingTool.Sdvoe;

namespace AvasRoutingTool.ViewModels
{
    /// <summary>The Advanced page: one row per discovered device with read-only status, plus the link switch.</summary>
    public class AdvancedViewModel : INotifyPropertyChanged
    {
        /// <summary>How long a row keeps saying "Rebooting" if the device is never seen to leave and return.</summary>
        public static readonly TimeSpan RebootTimeout = TimeSpan.FromSeconds(90);

        private sealed class RebootWatch
        {
            public DateTime StartedAt;
            public bool SeenGone;
        }

        private readonly Dictionary<string, RebootWatch> _rebooting = new();
        private bool _isRefreshing;
        private string _banner = string.Empty;

        public AdvancedViewModel()
        {
            RefreshCommand = new RelayCommand(async _ => await RefreshStatusAsync(Reader), _ => !IsRefreshing && Reader != null);
            DangerCommand = new RelayCommand(async p => { if (p is DangerAction a && SelectedDangerRow != null) await RunDangerAsync(a, SelectedDangerRow); },
                p => p is DangerAction && SelectedDangerRow is { CanRunDangerActions: true } && Maintenance != null);
            SwitchLinkCommand = new RelayCommand(async p => { if (p is AdvancedDeviceRowViewModel row) await SwitchLinkAsync(row); },
                p => p is AdvancedDeviceRowViewModel row && row.CanSwitchLink && Switcher != null);
        }

        public ObservableCollection<AdvancedDeviceRowViewModel> Rows { get; } = new();

        /// <summary>Where status is read from; set by the main view model once connected.</summary>
        public IDeviceReader? Reader { get; set; }

        /// <summary>Applies a link switch (the Control Server client).</summary>
        public ILinkSwitcher? Switcher { get; set; }

        public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

        /// <summary>What a switch of the given transmitter chips touches (routes and multi-view layouts); set by the main view model.</summary>
        public Func<IReadOnlyList<string>, LinkSwitchImpact> ImpactProvider { get; set; } = _ => LinkSwitchImpact.None;

        /// <summary>Unroutes the given transmitter chips from every multi-view (Single View routes stay); set by the main view model.</summary>
        public Func<IReadOnlyList<string>, Task> UnrouteFromMultiViews { get; set; } = _ => Task.CompletedTask;

        /// <summary>Asks the operator; the view shows the dialog. Without one the switch goes ahead unasked (tests only).</summary>
        public Func<LinkSwitchRequest, Task<bool>>? Confirm { get; set; }

        /// <summary>The destructive actions (Danger zone); null until connected.</summary>
        public IMaintenanceActions? Maintenance { get; set; }

        /// <summary>Asks the operator to confirm a destructive action; the view shows the dialog.</summary>
        public Func<DangerRequest, Task<bool>>? ConfirmDanger { get; set; }

        /// <summary>AVAS devices the Danger zone can act on (D-A4), in list order.</summary>
        public ObservableCollection<AdvancedDeviceRowViewModel> DangerRows { get; } = new();

        private AdvancedDeviceRowViewModel? _selectedDangerRow;

        /// <summary>The device the Danger zone buttons act on.</summary>
        public AdvancedDeviceRowViewModel? SelectedDangerRow
        {
            get => _selectedDangerRow;
            set { if (ReferenceEquals(_selectedDangerRow, value)) return; _selectedDangerRow = value; OnPropertyChanged(); }
        }

        public ICommand RefreshCommand { get; }
        public ICommand SwitchLinkCommand { get; }

        /// <summary>Runs the <see cref="DangerAction"/> given as the parameter on <see cref="SelectedDangerRow"/>.</summary>
        public ICommand DangerCommand { get; }

        public bool IsRefreshing
        {
            get => _isRefreshing;
            private set { if (_isRefreshing == value) return; _isRefreshing = value; OnPropertyChanged(); }
        }

        /// <summary>One line about the last link switch (applied or failed); empty when there is nothing to say.</summary>
        public string Banner
        {
            get => _banner;
            private set { if (_banner == value) return; _banner = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasBanner)); }
        }

        public bool HasBanner => !string.IsNullOrEmpty(_banner);

        public bool IsEmpty => Rows.Count == 0;

        public void SetDevices(IEnumerable<AvasDevice> devices)
        {
            var list = devices.ToList();
            var pairs = ChipPairing.Build(list);
            var ordered = pairs.Where(p => p.Primary.IsTransmitter).OrderBy(p => p.Primary.DeviceName)
                .Concat(pairs.Where(p => p.Primary.IsReceiver && !p.Primary.IsTransmitter).OrderBy(p => p.Primary.DeviceName));

            UpdateRebootWatches(list);

            Rows.Clear();
            DangerRows.Clear();
            string? keep = SelectedDangerRow?.Mac;
            foreach (var pair in ordered)
            {
                var row = new AdvancedDeviceRowViewModel(pair.Primary, pair.Companion);
                if (IsWatched(pair.Primary) || (pair.Companion != null && IsWatched(pair.Companion))) row.SetRebooting(true);
                Rows.Add(row);

                // The Danger zone acts on one chip at a time, so chip 1 of a pair is listed there on its own.
                if (row.IsAvas) DangerRows.Add(row);
                if (pair.Companion is { IsAvasDevice: true } companion)
                {
                    var chip1 = new AdvancedDeviceRowViewModel(companion);
                    if (IsWatched(companion)) chip1.SetRebooting(true);
                    DangerRows.Add(chip1);
                }
            }
            SelectedDangerRow = DangerRows.FirstOrDefault(r => r.Mac == keep) ?? DangerRows.FirstOrDefault();
            OnPropertyChanged(nameof(IsEmpty));
        }

        private bool IsWatched(AvasDevice device) => _rebooting.ContainsKey(SdvoeClient.NormalizeMac(device.DeviceId));

        // A device that was switched is "Rebooting" until it has been seen to leave and come back (or the timeout passes).
        private void UpdateRebootWatches(List<AvasDevice> devices)
        {
            var present = devices.Select(d => SdvoeClient.NormalizeMac(d.DeviceId)).ToHashSet();
            var now = Clock();
            foreach (var mac in _rebooting.Keys.ToList())
            {
                var watch = _rebooting[mac];
                if (!present.Contains(mac)) watch.SeenGone = true;
                else if (watch.SeenGone) { _rebooting.Remove(mac); continue; }
                if (now - watch.StartedAt > RebootTimeout) _rebooting.Remove(mac);
            }
        }

        /// <summary>Reads temperature (both chips of a pair) and link mode (AVAS transmitters). Non-AVAS rows are not queried.</summary>
        public async Task RefreshStatusAsync(IDeviceReader? reader)
        {
            if (reader == null || IsRefreshing) return;
            IsRefreshing = true;
            try
            {
                var temperature = new TemperatureReader(reader);
                var link = new LinkModeReader(reader);
                foreach (var row in Rows.ToList().Where(r => r.IsAvas))
                {
                    row.ApplyTemperature(await temperature.ReadAsync(row.Mac));
                    if (row.HasCompanion) row.ApplyCompanionTemperature(await temperature.ReadAsync(row.CompanionMac));
                    if (row.IsTransmitter) row.ApplyLinkMode(await link.ReadAsync(row.Mac));
                }
            }
            finally { IsRefreshing = false; }
        }

        /// <summary>
        /// Checks, with fresh reads, that both chips answer, are online and are companions of each other. Returns the reason the
        /// switch must not go ahead, or null. Without a reader the discovery state the row already shows is all there is.
        /// </summary>
        private async Task<string?> CheckChipPairAsync(AdvancedDeviceRowViewModel row)
        {
            if (Reader == null) return null;

            string? json0 = await Reader.ReadDeviceSubsetAsync(row.Mac, "device");
            string? json1 = await Reader.ReadDeviceSubsetAsync(row.CompanionMac, "device");
            const string both = "Both chips must be online to switch the link mode.";

            if (json0 == null) return $"{row.Name}: chip 0 can't be reached. {both}";
            if (json1 == null) return $"{row.Name}: chip 1 can't be reached. {both}";
            if (DeviceReplyReader.IsActive(json0) == false) return $"{row.Name}: chip 0 is offline. {both}";
            if (DeviceReplyReader.IsActive(json1) == false) return $"{row.Name}: chip 1 is offline. {both}";

            var info0 = LinkModeReader.Parse(json0);
            if (info0.HasCompanion && SdvoeClient.NormalizeMac(info0.CompanionMac) != SdvoeClient.NormalizeMac(row.CompanionMac))
                return $"{row.Name}: chip 0 and chip 1 are not companions (chip 0 lists {info0.CompanionMac}).";
            var info1 = LinkModeReader.Parse(json1);
            if (info1.HasCompanion && SdvoeClient.NormalizeMac(info1.CompanionMac) != SdvoeClient.NormalizeMac(row.Mac))
                return $"{row.Name}: chip 0 and chip 1 are not companions (chip 1 lists {info1.CompanionMac}).";
            return null;
        }

        /// <summary>
        /// Switches the transmitter to the other link mode (D-A3) on both of its chips: both must be online and companions
        /// (checked again just before), the operator confirms with what is affected, the same mode is set on chip 0 and chip 1
        /// and the primary chip rebooted, then the transmitter is unrouted from every multi-view (Single View routes stay) and the
        /// row shows "Rebooting" until both chips are rediscovered. Nothing is re-routed afterwards. Cancel changes nothing.
        /// </summary>
        public async Task SwitchLinkAsync(AdvancedDeviceRowViewModel row)
        {
            if (Switcher == null) return;
            if (!row.CanSwitchLink)
            {
                if (row.SwitchBlockedReason.Length > 0) Banner = row.SwitchBlockedReason;
                return;
            }

            string? problem = await CheckChipPairAsync(row);
            if (problem != null)
            {
                Banner = problem;
                return;
            }

            var target = row.SwitchTarget;
            IReadOnlyList<string> chips = new[] { row.Mac, row.CompanionMac }.Select(SdvoeClient.NormalizeMac).ToList();

            var request = new LinkSwitchRequest(row.Name, target, ImpactProvider(chips));
            if (Confirm != null && !await Confirm(request)) return;

            Banner = string.Empty;
            string modeWord = target == LinkMode.Dual ? "dual" : "single";
            bool ok;
            try { ok = await Switcher.SetLinkModeAsync(row.Device, row.Companion, target); }
            catch (Exception ex)
            {
                AppLogger.Error("ADVANCED", $"Link switch on '{row.Name}' failed", ex);
                ok = false;
            }

            if (!ok)
            {
                Banner = $"Couldn't switch {row.Name} to {modeWord} link. Nothing was changed on screen.";
                return;
            }

            foreach (var chip in chips) _rebooting[chip] = new RebootWatch { StartedAt = Clock() };
            row.SetRebooting(true);
            Banner = $"{row.Name} is rebooting into {modeWord} link on both chips. Route it again when it is back.";

            try { await UnrouteFromMultiViews(chips); }
            catch (Exception ex) { AppLogger.Error("ADVANCED", $"Unrouting '{row.Name}' from multi-views failed", ex); }
        }

        /// <summary>
        /// Runs one destructive action on a device after the operator confirms it (a dialog whose title names the device).
        /// AVAS devices only (D-A4): anything else is refused with a banner, without asking and without a call.
        /// </summary>
        public async Task RunDangerAsync(DangerAction action, AdvancedDeviceRowViewModel row)
        {
            if (Maintenance == null) return;
            if (!row.IsAvas)
            {
                Banner = $"{row.Name} is not an AVAS device. Reboots and resets are only offered for AVAS devices.";
                return;
            }
            if (action is DangerAction.RebootMcu or DangerAction.FactoryResetMcu && row.Device.ChipId != 0)
            {
                Banner = $"MCU commands go through chip 0. Choose the primary chip of {row.Name}.";
                return;
            }
            if (!row.CanRunDangerActions) return;

            var request = DescribeDanger(action, row);
            if (ConfirmDanger != null && !await ConfirmDanger(request)) return;

            Banner = string.Empty;
            bool ok;
            try { ok = await RunAsync(action, row); }
            catch (Exception ex)
            {
                AppLogger.Error("ADVANCED", $"{request.Title} failed", ex);
                ok = false;
            }

            if (!ok)
            {
                Banner = $"Couldn't {VerbPhrase(action)} {row.Name}. Nothing was changed.";
                return;
            }

            Banner = $"{DonePhrase(action)} {row.Name}.";
            if (action is DangerAction.RebootChip or DangerAction.FactoryResetChip)
            {
                _rebooting[SdvoeClient.NormalizeMac(row.Mac)] = new RebootWatch { StartedAt = Clock() };
                row.SetRebooting(true);
            }
        }

        private Task<bool> RunAsync(DangerAction action, AdvancedDeviceRowViewModel row) => action switch
        {
            DangerAction.RebootChip => Maintenance!.RebootChipAsync(row.Device, row.Mac),
            DangerAction.RebootMcu => Maintenance!.RebootMcuAsync(row.Device),
            DangerAction.FactoryResetMcu => Maintenance!.FactoryResetMcuAsync(row.Device),
            DangerAction.FactoryResetChip => Maintenance!.FactoryResetChipAsync(row.Device, row.Mac),
            _ => Task.FromResult(false)
        };

        private static DangerRequest DescribeDanger(DangerAction action, AdvancedDeviceRowViewModel row) => action switch
        {
            DangerAction.RebootChip => new DangerRequest(action, row.Name, $"Reboot {row.Name}?",
                $"{row.Name} restarts and its picture is lost for a while. Routes to it are not changed.", "Reboot", null),
            DangerAction.RebootMcu => new DangerRequest(action, row.Name, $"Reboot the MCU of {row.Name}?",
                $"The control processor of {row.Name} restarts. Its video keeps running.", "Reboot MCU", null),
            DangerAction.FactoryResetMcu => new DangerRequest(action, row.Name, $"Factory reset the MCU of {row.Name}?",
                $"The control processor of {row.Name} goes back to its factory settings. Its own settings are lost and cannot be recovered.",
                "Factory reset", row.Name),
            DangerAction.FactoryResetChip => new DangerRequest(action, row.Name, $"Factory reset the chip of {row.Name}?",
                $"The video chip of {row.Name} goes back to its factory settings and restarts. Its name, mode and routes are lost and cannot be recovered.",
                "Factory reset", row.Name),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };

        private static string VerbPhrase(DangerAction action) => action switch
        {
            DangerAction.RebootChip => "reboot",
            DangerAction.RebootMcu => "reboot the MCU of",
            DangerAction.FactoryResetMcu => "factory reset the MCU of",
            DangerAction.FactoryResetChip => "factory reset the chip of",
            _ => "run the action on"
        };

        private static string DonePhrase(DangerAction action) => action switch
        {
            DangerAction.RebootChip => "Rebooting",
            DangerAction.RebootMcu => "Rebooting the MCU of",
            DangerAction.FactoryResetMcu => "Factory reset sent to the MCU of",
            DangerAction.FactoryResetChip => "Factory reset sent to the chip of",
            _ => "Done:"
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
