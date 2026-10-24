using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AvasRoutingTool.Sdvoe;

namespace AvasRoutingTool.ViewModels
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Predicate<object?>? _canExecute;

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute(parameter);
        public void Execute(object? parameter) => _execute(parameter);
    }

    public class DisplayCardViewModel : INotifyPropertyChanged
    {
        private readonly AvasDevice _device;
        private readonly Func<AvasDevice, System.Threading.Tasks.Task>? _unrouteAction;
        private bool _isDragOver;

        public event PropertyChangedEventHandler? PropertyChanged;

        public AvasDevice Device => _device;
        public string DeviceName => _device.DeviceName;
        public string IpAddress => _device.IpAddress;
        public string MacAddress => _device.DeviceId;
        public bool IsActive => _device.IsActive;

        public string ActiveSourceId => _device.ActiveSourceId;
        public string ActiveSourceName => _device.ActiveSourceName;
        public bool IsRouted => !string.IsNullOrEmpty(_device.ActiveSourceId);

        public bool IsDragOver
        {
            get => _isDragOver;
            set { _isDragOver = value; OnPropertyChanged(); }
        }

        public ICommand UnrouteCommand { get; }

        public DisplayCardViewModel(AvasDevice device, Func<AvasDevice, System.Threading.Tasks.Task>? unrouteAction = null)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            _unrouteAction = unrouteAction;

            UnrouteCommand = new RelayCommand(async _ =>
            {
                if (_unrouteAction != null)
                {
                    await _unrouteAction(_device);
                    NotifyRoutingChanged();
                }
            }, _ => IsRouted);
        }

        public void SetOnlineState(bool isOnline)
        {
            _device.IsActive = isOnline;
            OnPropertyChanged(nameof(IsActive));
        }

        public void NotifyRoutingChanged()
        {
            OnPropertyChanged(nameof(ActiveSourceId));
            OnPropertyChanged(nameof(ActiveSourceName));
            OnPropertyChanged(nameof(IsRouted));
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
