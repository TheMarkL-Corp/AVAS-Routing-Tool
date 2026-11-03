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
        private bool _isRebooting;

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

        /// <summary>"Single link", "Dual link", "Rebooting" or "n/a" (receivers, non-AVAS devices, unreadable).</summary>
        public string LinkMode { get => _linkMode; private set => Set(ref _linkMode, value); }

        /// <summary>"61 °C", or "n/a" when the device reports no temperature (never 0).</summary>
        public string TemperatureText { get => _temperatureText; private set => Set(ref _temperatureText, value); }

        /// <summary>The word beside the status dot: Normal, Warm, Hot or Unknown.</summary>
        public string TemperatureState { get => _temperatureState; private set => Set(ref _temperatureState, value); }

        public LinkModeInfo LinkInfo { get; private set; } = LinkModeInfo.Unknown;

        /// <summary>The device is rebooting after a link switch; the link-mode column says so until it is rediscovered.</summary>
        public bool IsRebooting => _isRebooting;

        /// <summary>Shows a temperature read; null (nothing reported) shows "n/a", never 0.</summary>
        public void ApplyTemperature(int? degrees)
        {
            var (text, state) = TemperatureFormat.Describe(degrees);
            TemperatureText = text;
            TemperatureState = state;
        }

        public void ApplyLinkMode(LinkModeInfo info)
        {
            LinkInfo = info;
            ShowLinkMode();
            Raise(nameof(CanSwitchLink));
            Raise(nameof(SwitchLinkLabel));
        }

        public void SetRebooting(bool value)
        {
            if (_isRebooting == value) return;
            _isRebooting = value;
            Raise(nameof(IsRebooting));
            Raise(nameof(CanSwitchLink));
            Raise(nameof(CanRunDangerActions));
            ShowLinkMode();
        }

        private void ShowLinkMode()
        {
            LinkMode = _isRebooting ? "Rebooting" : LinkInfo.Mode switch
            {
                Sdvoe.LinkMode.Single => "Single link",
                Sdvoe.LinkMode.Dual => "Dual link",
                _ => "n/a"
            };
        }

        /// <summary>The mode a switch would go to: the other one. Unknown when the current mode is not known.</summary>
        public Sdvoe.LinkMode SwitchTarget => LinkInfo.Mode switch
        {
            Sdvoe.LinkMode.Single => Sdvoe.LinkMode.Dual,
            Sdvoe.LinkMode.Dual => Sdvoe.LinkMode.Single,
            _ => Sdvoe.LinkMode.Unknown
        };

        /// <summary>Only an AVAS primary transmitter chip (chip 0) whose mode is known and that is not already rebooting can be switched.</summary>
        public bool CanSwitchLink => IsAvas && IsTransmitter && Device.ChipId == 0 && !_isRebooting &&
                                     SwitchTarget != Sdvoe.LinkMode.Unknown;

        /// <summary>Reboots and resets are offered for AVAS devices only (D-A4), and not while the device is already rebooting.</summary>
        public bool CanRunDangerActions => IsAvas && !_isRebooting;

        /// <summary>MCU commands go through chip 0, so they are offered on the primary chip only.</summary>
        public bool CanRunMcuActions => CanRunDangerActions && Device.ChipId == 0;

        public string SwitchLinkLabel => SwitchTarget switch
        {
            Sdvoe.LinkMode.Dual => "Switch to dual link",
            Sdvoe.LinkMode.Single => "Switch to single link",
            _ => "Switch link mode"
        };

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
