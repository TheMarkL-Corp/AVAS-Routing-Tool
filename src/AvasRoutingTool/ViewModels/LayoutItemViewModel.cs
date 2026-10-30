using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AvasRoutingTool.ViewModels
{
    public class LayoutItemViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _isOnScreen;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public bool IsAvailable { get; set; }
        public string BadgeText => IsAvailable ? (IsSelected ? "ACTIVE" : "READY") : "SOON";

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BadgeText));
            }
        }

        /// <summary>This layout is what the display shows right now: selected AND the display is routed (an unrouted display marks no tile). Set by the owning card.</summary>
        public bool IsOnScreen
        {
            get => _isOnScreen;
            set
            {
                if (_isOnScreen == value) return;
                _isOnScreen = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// New instances of every layout the tool offers, in the bottom bar's order (Off, Single View, the four
        /// MultiView presets). One list per owner: selection state is not shared between the bar and the cards.
        /// </summary>
        public static System.Collections.Generic.IReadOnlyList<LayoutItemViewModel> CreateCatalog() => new[]
        {
            new LayoutItemViewModel { Id = "off", Title = "Off", Subtitle = "Mute video", IsAvailable = true },
            new LayoutItemViewModel { Id = "single", Title = "Single View", Subtitle = "Full screen (1:1)", IsAvailable = true },
            new LayoutItemViewModel { Id = "matrix2x2", Title = "Matrix 2x2", Subtitle = "4-quad grid", IsAvailable = true },
            new LayoutItemViewModel { Id = "pip", Title = "PIP", Subtitle = "Picture-in-picture", IsAvailable = true },
            new LayoutItemViewModel { Id = "side_by_side", Title = "Side-by-Side", Subtitle = "Split dual view", IsAvailable = true },
            new LayoutItemViewModel { Id = "quad_1_3", Title = "1+3 Quad", Subtitle = "1 Master + 3 sub", IsAvailable = true },
        };

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
