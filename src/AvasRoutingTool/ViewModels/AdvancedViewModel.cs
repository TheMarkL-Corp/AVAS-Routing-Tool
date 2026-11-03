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

        public ICommand RefreshCommand { get; }
        public ICommand SwitchLinkCommand { get; }

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
            var ordered = list.Where(d => d.IsTransmitter).OrderBy(d => d.DeviceName)
                .Concat(list.Where(d => d.IsReceiver && !d.IsTransmitter).OrderBy(d => d.DeviceName));

            UpdateRebootWatches(list);

            Rows.Clear();
            foreach (var d in ordered)
            {
                var row = new AdvancedDeviceRowViewModel(d);
                if (_rebooting.ContainsKey(SdvoeClient.NormalizeMac(d.DeviceId))) row.SetRebooting(true);
                Rows.Add(row);
            }
            OnPropertyChanged(nameof(IsEmpty));
        }

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

        /// <summary>Reads temperature (every AVAS device) and link mode (AVAS transmitters). Non-AVAS rows are not queried.</summary>
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
                    if (row.IsTransmitter) row.ApplyLinkMode(await link.ReadAsync(row.Mac));
                }
            }
            finally { IsRefreshing = false; }
        }

        /// <summary>
        /// Switches the transmitter to the other link mode (D-A3): the operator confirms with what is affected, the mode is set
        /// and the chip rebooted, then the transmitter is unrouted from every multi-view (Single View routes stay) and the row
        /// shows "Rebooting" until the device is rediscovered. Nothing is re-routed afterwards. Cancel changes nothing.
        /// </summary>
        public async Task SwitchLinkAsync(AdvancedDeviceRowViewModel row)
        {
            if (Switcher == null || !row.CanSwitchLink) return;

            var target = row.SwitchTarget;
            var macs = new List<string> { row.Mac };
            if (row.LinkInfo.HasCompanion) macs.Add(row.LinkInfo.CompanionMac);
            IReadOnlyList<string> chips = macs.Select(SdvoeClient.NormalizeMac).Distinct().ToList();

            var request = new LinkSwitchRequest(row.Name, target, ImpactProvider(chips));
            if (Confirm != null && !await Confirm(request)) return;

            Banner = string.Empty;
            string modeWord = target == LinkMode.Dual ? "dual" : "single";
            bool ok;
            try { ok = await Switcher.SetLinkModeAsync(row.Device, target); }
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

            _rebooting[SdvoeClient.NormalizeMac(row.Mac)] = new RebootWatch { StartedAt = Clock() };
            row.SetRebooting(true);
            Banner = $"{row.Name} is rebooting into {modeWord} link. Route it again when it is back.";

            try { await UnrouteFromMultiViews(chips); }
            catch (Exception ex) { AppLogger.Error("ADVANCED", $"Unrouting '{row.Name}' from multi-views failed", ex); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
