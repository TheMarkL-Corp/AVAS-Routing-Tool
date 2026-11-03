using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using AvasRoutingTool.Sdvoe;

namespace AvasRoutingTool.ViewModels
{
    /// <summary>The Advanced page: one row per discovered device, rebuilt on every discovery refresh, with read-only status per row.</summary>
    public class AdvancedViewModel : INotifyPropertyChanged
    {
        private bool _isRefreshing;

        public AdvancedViewModel()
        {
            RefreshCommand = new RelayCommand(async _ => await RefreshStatusAsync(Reader), _ => !IsRefreshing && Reader != null);
        }

        public ObservableCollection<AdvancedDeviceRowViewModel> Rows { get; } = new();

        /// <summary>Where status is read from; set by the main view model once connected.</summary>
        public IDeviceReader? Reader { get; set; }

        public ICommand RefreshCommand { get; }

        public bool IsRefreshing
        {
            get => _isRefreshing;
            private set { if (_isRefreshing == value) return; _isRefreshing = value; OnPropertyChanged(); }
        }

        public bool IsEmpty => Rows.Count == 0;

        public void SetDevices(IEnumerable<AvasDevice> devices)
        {
            var list = devices.ToList();
            var ordered = list.Where(d => d.IsTransmitter).OrderBy(d => d.DeviceName)
                .Concat(list.Where(d => d.IsReceiver && !d.IsTransmitter).OrderBy(d => d.DeviceName));

            Rows.Clear();
            foreach (var d in ordered) Rows.Add(new AdvancedDeviceRowViewModel(d));
            OnPropertyChanged(nameof(IsEmpty));
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

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
