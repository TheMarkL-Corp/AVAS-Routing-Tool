using System.ComponentModel;
using System.Runtime.CompilerServices;
using AvasRoutingTool.Sdvoe;

namespace AvasRoutingTool.ViewModels
{
    /// <summary>One discovered device on the Advanced page: identity plus the read-only status read from the Control Server.</summary>
    public class AdvancedDeviceRowViewModel : INotifyPropertyChanged
    {
        private string _linkMode = "n/a";
        private string _temperatureText = "n/a";
        private string _temperatureState = "Unknown";

        public AdvancedDeviceRowViewModel(AvasDevice device)
        {
            Device = device;
            Name = device.DeviceName;
            Mac = device.DeviceId;
            Role = device.IsTransmitter ? "TX" : "RX";
            IsAvas = device.IsAvasDevice;
        }

        public AvasDevice Device { get; }
        public string Name { get; }
        public string Mac { get; }
        public string Role { get; }
        public bool IsAvas { get; }
        public bool IsTransmitter => Device.IsTransmitter;

        /// <summary>"Single link", "Dual link" or "n/a" (receivers, non-AVAS devices, unreadable).</summary>
        public string LinkMode { get => _linkMode; set => Set(ref _linkMode, value); }

        /// <summary>"61 °C", or "n/a" when the device reports no temperature (never 0).</summary>
        public string TemperatureText { get => _temperatureText; set => Set(ref _temperatureText, value); }

        /// <summary>The word beside the status dot: Normal, Warm, Hot or Unknown.</summary>
        public string TemperatureState { get => _temperatureState; set => Set(ref _temperatureState, value); }

        /// <summary>Shows a temperature read; null (nothing reported) shows "n/a", never 0.</summary>
        public void ApplyTemperature(int? degrees)
        {
            var (text, state) = TemperatureFormat.Describe(degrees);
            TemperatureText = text;
            TemperatureState = state;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
