using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AvasRoutingTool.ViewModels
{
    public class LayoutItemViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;

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

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
