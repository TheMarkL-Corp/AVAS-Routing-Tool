using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using AvasRoutingTool.Configuration;
using AvasRoutingTool.Services.Visca;

namespace AvasRoutingTool.ViewModels
{
    /// <summary>
    /// The Room Cameras tab: the list of configured cameras, the one active selection, persistence
    /// through <see cref="IConfigService"/> and the binding of each camera to its TX source card.
    /// Owns every <see cref="IPtzCamera"/> it creates and disposes it with the view model.
    /// </summary>
    public class RoomCamerasViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly IConfigService _configService;
        private readonly IPtzCameraFactory _factory;
        private readonly ObservableCollection<SourceCardViewModel> _sources;
        private readonly Func<DateTime> _now;
        private RoomCameraViewModel? _selected;
        private readonly System.Collections.Generic.Dictionary<RoomCameraViewModel, CancellationTokenSource> _connects = new();
        private bool _disposed;

        public RoomCamerasViewModel(
            IConfigService configService,
            IPtzCameraFactory factory,
            ObservableCollection<SourceCardViewModel> sources,
            Func<DateTime> now)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _sources = sources ?? throw new ArgumentNullException(nameof(sources));
            _now = now ?? throw new ArgumentNullException(nameof(now));

            Cameras = new ObservableCollection<RoomCameraViewModel>();

            AddCommand = new RelayCommand(_ => AddRequested?.Invoke());
            EditCommand = new RelayCommand(p => RaiseEdit(p as RoomCameraViewModel ?? Selected), p => (p as RoomCameraViewModel ?? Selected) != null);
            RemoveCommand = new RelayCommand(
                p => { var cam = p as RoomCameraViewModel ?? Selected; if (cam != null) RemoveCamera(cam.Id); },
                p => (p as RoomCameraViewModel ?? Selected) != null);
            SelectCommand = new RelayCommand(p => { if (p is RoomCameraViewModel cam) Selected = cam; }, p => p is RoomCameraViewModel);

            foreach (var config in _configService.Load().RoomCameras)
                Cameras.Add(CreateCameraViewModel(config));
            _selected = Cameras.FirstOrDefault();

            _sources.CollectionChanged += OnSourcesChanged;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>The view opens the editor for a new camera.</summary>
        public event Action? AddRequested;

        /// <summary>The view opens the editor for this camera.</summary>
        public event Action<RoomCameraViewModel>? EditRequested;

        /// <summary>Cameras in config order.</summary>
        public ObservableCollection<RoomCameraViewModel> Cameras { get; }

        public ICommand AddCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand RemoveCommand { get; }
        public ICommand SelectCommand { get; }

        /// <summary>The one active camera. Selecting another stops the previous camera's motion.</summary>
        public RoomCameraViewModel? Selected
        {
            get => _selected;
            set
            {
                if (ReferenceEquals(_selected, value))
                    return;
                var previous = _selected;
                _selected = value;
                previous?.StopAll();
                OnPropertyChanged();
            }
        }

        /// <summary>Appends a camera: Load, change only RoomCameras, Save (a fresh Load every time).</summary>
        public void AddCamera(RoomCameraConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            Persist(rooms =>
            {
                int index = rooms.FindIndex(c => c.Id == config.Id);
                if (index >= 0)
                    rooms[index] = config;
                else
                    rooms.Add(config);
            });

            int existing = IndexOf(config.Id);
            if (existing >= 0)
            {
                ReplaceCamera(existing, config);
                return;
            }
            var cam = CreateCameraViewModel(config);
            Cameras.Add(cam);
            _selected ??= cam;
            if (ReferenceEquals(_selected, cam))
                OnPropertyChanged(nameof(Selected));
        }

        /// <summary>Replaces the stored camera with the same Id and rebuilds its view model and camera.</summary>
        public void UpdateCamera(RoomCameraConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            Persist(rooms =>
            {
                int index = rooms.FindIndex(c => c.Id == config.Id);
                if (index >= 0)
                    rooms[index] = config;
                else
                    rooms.Add(config);
            });

            int existing = IndexOf(config.Id);
            if (existing >= 0)
                ReplaceCamera(existing, config);
            else
                Cameras.Add(CreateCameraViewModel(config));
        }

        /// <summary>Removes a camera from the file and the list, and disposes it.</summary>
        public void RemoveCamera(string id)
        {
            Persist(rooms => rooms.RemoveAll(c => c.Id == id));

            int index = IndexOf(id);
            if (index < 0)
                return;
            var cam = Cameras[index];
            bool wasSelected = ReferenceEquals(_selected, cam);
            Cameras.RemoveAt(index);
            DisposeCamera(cam);
            if (wasSelected)
            {
                _selected = Cameras.Count == 0 ? null : Cameras[Math.Min(index, Cameras.Count - 1)];
                OnPropertyChanged(nameof(Selected));
            }
        }

        /// <summary>Drives every camera's watchdog, throttle, power prompt and poll.</summary>
        public void Tick()
        {
            if (_disposed)
                return;
            foreach (var cam in Cameras.ToArray())
                cam.Tick();
        }

        /// <summary>Stops motion on every camera and stops only the preview streams this tab started.</summary>
        public void StopAll()
        {
            foreach (var cam in Cameras.ToArray())
            {
                cam.StopAll();
                cam.StopPreview();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _sources.CollectionChanged -= OnSourcesChanged;

            foreach (var cam in Cameras.ToArray())
            {
                cam.StopPreview();
                DisposeCamera(cam);
            }
            Cameras.Clear();
            _selected = null;
        }

        private void Persist(Action<System.Collections.Generic.List<RoomCameraConfig>> change)
        {
            var config = _configService.Load();   // fresh each time: Theme/ShowLogs may have changed since
            change(config.RoomCameras);
            _configService.Save(config);
        }

        private int IndexOf(string id)
        {
            for (int i = 0; i < Cameras.Count; i++)
                if (Cameras[i].Id == id)
                    return i;
            return -1;
        }

        private RoomCameraViewModel CreateCameraViewModel(RoomCameraConfig config)
        {
            var camera = _factory.Create(config);
            var vm = new RoomCameraViewModel(config, camera, _now);
            vm.ResolveSource(FindCard(config.BoundTxMac));
            StartConnect(vm);
            return vm;
        }

        // ViscaCamera.Tick does nothing until ConnectAsync has been called once, so connect on create.
        // Fire-and-forget with every exception observed; the token is cancelled when the camera is removed.
        private void StartConnect(RoomCameraViewModel vm)
        {
            var cts = new CancellationTokenSource();
            _connects[vm] = cts;
            _ = ConnectQuietlyAsync(vm.Camera, cts.Token);
        }

        private static async Task ConnectQuietlyAsync(IPtzCamera camera, CancellationToken token)
        {
            try
            {
                await camera.ConnectAsync(token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Cancelled, or the camera is unreachable: the link state and retry backoff report it.
            }
        }

        private void ReplaceCamera(int index, RoomCameraConfig config)
        {
            var old = Cameras[index];
            bool wasSelected = ReferenceEquals(_selected, old);
            var fresh = CreateCameraViewModel(config);
            Cameras[index] = fresh;
            DisposeCamera(old);
            if (wasSelected)
            {
                _selected = fresh;
                OnPropertyChanged(nameof(Selected));
            }
        }

        private SourceCardViewModel? FindCard(string mac) =>
            string.IsNullOrEmpty(mac) ? null : _sources.FirstOrDefault(s => s.MacAddress == mac);

        // Refresh clears and re-adds every card: re-resolve every binding on every change, hold nothing.
        private void OnSourcesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_disposed)
                return;
            foreach (var cam in Cameras.ToArray())
                cam.ResolveSource(FindCard(cam.Config.BoundTxMac));
        }

        private void RaiseEdit(RoomCameraViewModel? cam)
        {
            if (cam != null)
                EditRequested?.Invoke(cam);
        }

        private void DisposeCamera(RoomCameraViewModel cam)
        {
            if (_connects.Remove(cam, out var connect))
            {
                connect.Cancel();
                connect.Dispose();
            }
            cam.StopAll();
            cam.Dispose();
            try
            {
                var pending = cam.Camera.DisposeAsync();
                if (!pending.IsCompleted)
                    pending.AsTask().ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            }
            catch (Exception)
            {
                // A camera that fails to dispose has nothing more to release here.
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
