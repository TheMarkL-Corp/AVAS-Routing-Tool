using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AvasRoutingTool.Sdvoe;

namespace AvasRoutingTool.ViewModels
{
    /// <summary>
    /// One device on the Advanced page. A dual-link transmitter is one row for its two chips: <see cref="Device"/> is chip 0 and
    /// <see cref="Companion"/> chip 1, and the row shows the state of both.
    /// </summary>
    public class AdvancedDeviceRowViewModel : INotifyPropertyChanged
    {
        private string _linkMode = "n/a";
        private string _temperatureText = "n/a";
        private string _temperatureState = "Unknown";
        private bool _isRebooting;
        private int? _temp0;
        private int? _temp1;

        public AdvancedDeviceRowViewModel(AvasDevice device, AvasDevice? companion = null)
        {
            Device = device;
            Companion = companion;
            Name = device.DeviceName;
            Mac = device.DeviceId;
            Role = device.IsTransmitter ? "TX" : "RX";
            IsAvas = device.IsAvasDevice;
        }

        public AvasDevice Device { get; }

        /// <summary>Chip 1 of the same transmitter, or null (a receiver, a single-chip device, or a chip 1 that was not found).</summary>
        public AvasDevice? Companion { get; }

        public string Name { get; }
        public string Mac { get; }
        public string Role { get; }
        public bool IsAvas { get; }
        public bool IsTransmitter => Device.IsTransmitter;
        public bool HasCompanion => Companion != null;
        public string CompanionMac => Companion?.DeviceId ?? string.Empty;

        /// <summary>"Single link", "Dual link", "Rebooting" or "n/a" (receivers, non-AVAS devices, unreadable).</summary>
        public string LinkMode { get => _linkMode; private set => Set(ref _linkMode, value); }

        /// <summary>"61 °C", "58 °C / 64 °C" for a pair (chip 0 / chip 1), or "n/a" for a chip that reports none (never 0).</summary>
        public string TemperatureText { get => _temperatureText; private set => Set(ref _temperatureText, value); }

        /// <summary>The word beside the status dot: Normal, Warm, Hot or Unknown (the worst of the two chips).</summary>
        public string TemperatureState { get => _temperatureState; private set => Set(ref _temperatureState, value); }

        public LinkModeInfo LinkInfo { get; private set; } = LinkModeInfo.Unknown;

        /// <summary>The device is rebooting after a link switch or a reboot; the link-mode column says so until it is rediscovered.</summary>
        public bool IsRebooting => _isRebooting;

        /// <summary>Shows the temperature read from chip 0; null (nothing reported) shows "n/a", never 0.</summary>
        public void ApplyTemperature(int? degrees)
        {
            _temp0 = degrees;
            ShowTemperature();
        }

        /// <summary>Shows the temperature read from chip 1 beside chip 0's (only for a pair).</summary>
        public void ApplyCompanionTemperature(int? degrees)
        {
            _temp1 = degrees;
            ShowTemperature();
        }

        private void ShowTemperature()
        {
            var (text0, state0) = TemperatureFormat.Describe(_temp0);
            if (Companion == null)
            {
                TemperatureText = text0;
                TemperatureState = state0;
                return;
            }

            var (text1, state1) = TemperatureFormat.Describe(_temp1);
            TemperatureText = $"{text0} / {text1}";
            TemperatureState = Rank(state0) >= Rank(state1) ? state0 : state1;

            static int Rank(string state) => state switch { "Hot" => 3, "Warm" => 2, "Normal" => 1, _ => 0 };
        }

        public void ApplyLinkMode(LinkModeInfo info)
        {
            LinkInfo = info;
            ShowLinkMode();
            Raise(nameof(CanSwitchLink));
            Raise(nameof(SwitchLinkLabel));
            Raise(nameof(ChipStatusText));
            Raise(nameof(ChipStatusState));
            Raise(nameof(SwitchBlockedReason));
        }

        public void SetRebooting(bool value)
        {
            if (_isRebooting == value) return;
            _isRebooting = value;
            Raise(nameof(IsRebooting));
            Raise(nameof(CanSwitchLink));
            Raise(nameof(CanRunDangerActions));
            Raise(nameof(SwitchBlockedReason));
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

        // ---- both chips ----

        private bool IsPrimaryAvasTx => IsAvas && IsTransmitter && Device.ChipId == 0;

        /// <summary>A dual-capable AVAS transmitter chip 0 is expected to have a chip 1; a link-mode read that lists only SINGLE says it has none.</summary>
        private bool ExpectsCompanion => IsPrimaryAvasTx &&
            (string.IsNullOrEmpty(LinkInfo.Capabilities) || LinkInfo.Capabilities.Contains("DUAL", StringComparison.OrdinalIgnoreCase));

        private static string Word(bool online) => online ? "online" : "offline";

        /// <summary>The state of the chips in words: "Chip 0 online · Chip 1 online", "... Chip 1 offline", "... Chip 1 not found"; "Online" or "Offline" for one chip.</summary>
        public string ChipStatusText
        {
            get
            {
                if (Companion != null) return $"Chip 0 {Word(Device.IsActive)} · Chip 1 {Word(Companion.IsActive)}";
                if (ExpectsCompanion) return $"Chip 0 {Word(Device.IsActive)} · Chip 1 not found";
                if (IsAvas && IsTransmitter && Device.ChipId == 1) return $"Chip 1 {Word(Device.IsActive)} · Chip 0 not found";
                return Device.IsActive ? "Online" : "Offline";
            }
        }

        /// <summary>The word beside the chip status dot: Online (everything is), Partly online (one chip is missing or offline) or Offline.</summary>
        public string ChipStatusState
        {
            get
            {
                bool second = Companion != null || ExpectsCompanion || (IsAvas && IsTransmitter && Device.ChipId == 1);
                if (!second) return Device.IsActive ? "Online" : "Offline";
                if (BothChipsOnline) return "Online";
                bool any = Device.IsActive || (Companion?.IsActive ?? false);
                return any ? "Partly online" : "Offline";
            }
        }

        private bool BothChipsOnline => Companion != null && Device.IsActive && Companion.IsActive;

        /// <summary>Why the link switch is not offered ("TX2: chip 1 is offline. Both chips must be online..."), or empty when it is offered or does not apply.</summary>
        public string SwitchBlockedReason
        {
            get
            {
                if (!ExpectsCompanion || _isRebooting || BothChipsOnline) return string.Empty;
                string which = Companion == null ? "chip 1 was not found" : !Device.IsActive ? "chip 0 is offline" : "chip 1 is offline";
                return $"{Name}: {which}. Both chips must be online to switch the link mode.";
            }
        }

        /// <summary>The mode a switch would go to: the other one. Unknown when the current mode is not known.</summary>
        public Sdvoe.LinkMode SwitchTarget => LinkInfo.Mode switch
        {
            Sdvoe.LinkMode.Single => Sdvoe.LinkMode.Dual,
            Sdvoe.LinkMode.Dual => Sdvoe.LinkMode.Single,
            _ => Sdvoe.LinkMode.Unknown
        };

        /// <summary>Only an AVAS transmitter whose chip 0 and chip 1 are both online, whose mode is known, and that is not rebooting can be switched.</summary>
        public bool CanSwitchLink => IsPrimaryAvasTx && !_isRebooting && SwitchTarget != Sdvoe.LinkMode.Unknown && BothChipsOnline;

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
