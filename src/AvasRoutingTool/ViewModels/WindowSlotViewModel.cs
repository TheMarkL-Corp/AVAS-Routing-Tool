using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using AvasRoutingTool.Sdvoe.MultiView;

namespace AvasRoutingTool.ViewModels
{
    /// <summary>
    /// One multiview window on a decoder card: a drop zone that mirrors its source's live preview
    /// (same bitmap reference, no extra sockets) once a source is assigned.
    /// </summary>
    public class WindowSlotViewModel : INotifyPropertyChanged
    {
        private SourceCardViewModel? _source;
        private WriteableBitmap? _previewBitmap;
        private bool _isPreviewSupported;
        private bool _isDragOver;

        public event PropertyChangedEventHandler? PropertyChanged;

        public WindowSlotViewModel(WindowSlot slot)
        {
            Index = slot.Index;
            Label = slot.Label;
            NormalizedX = slot.NormalizedX;
            NormalizedY = slot.NormalizedY;
            NormalizedWidth = slot.NormalizedWidth;
            NormalizedHeight = slot.NormalizedHeight;
        }

        public int Index { get; }
        public string Label { get; }
        public double NormalizedX { get; }
        public double NormalizedY { get; }
        public double NormalizedWidth { get; }
        public double NormalizedHeight { get; }

        public bool IsAssigned => _source != null;

        /// <summary>An assigned source without a preview stream (non-AVAS): the window says so instead of showing a picture.</summary>
        public bool ShowPreviewUnavailable => IsAssigned && !_isPreviewSupported;
        public string SourceName => _source?.DeviceName ?? string.Empty;
        public string SourceMac => _source?.MacAddress ?? string.Empty;

        public WriteableBitmap? PreviewBitmap
        {
            get => _previewBitmap;
            private set { _previewBitmap = value; OnPropertyChanged(); }
        }

        public bool IsPreviewSupported
        {
            get => _isPreviewSupported;
            private set { _isPreviewSupported = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowPreviewUnavailable)); }
        }

        public bool IsDragOver
        {
            get => _isDragOver;
            set { _isDragOver = value; OnPropertyChanged(); }
        }

        public void SetSource(SourceCardViewModel? source)
        {
            if (_source != null) _source.PropertyChanged -= OnSourcePropertyChanged;

            _source = source;

            if (_source != null)
            {
                _source.PropertyChanged += OnSourcePropertyChanged;
                IsPreviewSupported = _source.IsPreviewSupported;
                PreviewBitmap = _source.PreviewBitmap;
            }
            else
            {
                IsPreviewSupported = false;
                PreviewBitmap = null;
            }

            OnPropertyChanged(nameof(IsAssigned));
            OnPropertyChanged(nameof(ShowPreviewUnavailable));
            OnPropertyChanged(nameof(SourceName));
            OnPropertyChanged(nameof(SourceMac));
        }

        private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_source == null) return;

            if (e.PropertyName == nameof(SourceCardViewModel.PreviewBitmap))
                PreviewBitmap = _source.PreviewBitmap;
            else if (e.PropertyName == nameof(SourceCardViewModel.IsPreviewSupported))
                IsPreviewSupported = _source.IsPreviewSupported;
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
