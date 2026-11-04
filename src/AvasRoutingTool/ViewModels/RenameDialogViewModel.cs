using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace AvasRoutingTool.ViewModels
{
    /// <summary>
    /// The rename dialog's state: the name being typed, the one-line reason it cannot be used yet (empty when it can),
    /// and whether Rename is allowed (valid AND different from the current name). Updates on every keystroke.
    /// </summary>
    public class RenameDialogViewModel : INotifyPropertyChanged
    {
        /// <summary>Shown while the typed name is valid but equal to the current one: nothing to rename.</summary>
        public const string UnchangedReason = "Type a different name";

        private readonly string[] _otherNames;
        private string _name;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <param name="deviceName">The device's current name.</param>
        /// <param name="otherNames">The names of every OTHER device (the device itself is left out).</param>
        public RenameDialogViewModel(string deviceName, IEnumerable<string> otherNames)
        {
            CurrentName = deviceName ?? string.Empty;
            _otherNames = (otherNames ?? Array.Empty<string>()).ToArray();
            _name = CurrentName;
        }

        /// <summary>"Rename TX2": the dialog's heading.</summary>
        public string Title => "Rename " + CurrentName;

        public string CurrentName { get; }

        /// <summary>The text in the field (two-way); starts as the current name.</summary>
        public string Name
        {
            get => _name;
            set
            {
                value ??= string.Empty;
                if (_name == value) return;
                _name = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Reason));
                OnPropertyChanged(nameof(HasReason));
                OnPropertyChanged(nameof(IsError));
                OnPropertyChanged(nameof(CanRename));
                OnPropertyChanged(nameof(CountText));
            }
        }

        private string? Problem => NameRules.Validate(_name, CurrentName, _otherNames);

        private bool IsUnchanged => string.Equals(_name, CurrentName, StringComparison.Ordinal);

        /// <summary>One line saying why Rename is off; empty string when the name is fine.</summary>
        public string Reason => Problem ?? (IsUnchanged ? UnchangedReason : string.Empty);

        public bool HasReason => Reason.Length > 0;

        /// <summary>The name breaks a rule (the reason is an error); false for the plain "Type a different name" hint.</summary>
        public bool IsError => Problem != null;

        /// <summary>The name is valid and different from the current one.</summary>
        public bool CanRename => Problem == null && !IsUnchanged;

        /// <summary>"12 / 19": the typed length over the Control Server's limit.</summary>
        public string CountText => $"{_name.Length} / {NameRules.MaxLength}";

        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
