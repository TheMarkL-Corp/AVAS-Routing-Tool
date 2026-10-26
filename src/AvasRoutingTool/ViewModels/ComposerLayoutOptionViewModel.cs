using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AvasRoutingTool.ViewModels
{
    /// <summary>
    /// One layout tile of the Composer's Step 1. Owned by the Composer so its selected / pending marks never leak
    /// into the overview's <see cref="LayoutItemViewModel"/> list that it was built from.
    /// </summary>
    public sealed class ComposerLayoutOptionViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _isPending;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ComposerLayoutOptionViewModel(string id, string title, string subtitle, bool isAvailable)
        {
            Id = id;
            Title = title;
            Subtitle = subtitle;
            IsAvailable = isAvailable;
        }

        public string Id { get; }
        public string Title { get; }
        public string Subtitle { get; }
        public bool IsAvailable { get; }

        /// <summary>This is the layout staged now.</summary>
        public bool IsSelected
        {
            get => _isSelected;
            internal set
            {
                if (_isSelected == value) return;
                Set(ref _isSelected, value);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AutomationName)));
            }
        }

        /// <summary>What a screen reader says for the tile: "Matrix 2x2, selected" / "PIP, not selected".</summary>
        public string AutomationName => $"{Title}, {(_isSelected ? "selected" : "not selected")}";

        /// <summary>The operator tapped it and the display is still being read; it becomes selected when that finishes.</summary>
        public bool IsPending { get => _isPending; internal set => Set(ref _isPending, value); }

        private void Set(ref bool field, bool value, [CallerMemberName] string? name = null)
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
