using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using AvasRoutingTool.Sdvoe.MultiView;

namespace AvasRoutingTool.ViewModels
{
    /// <summary>What a Composer block shows relative to the screen: nothing, a pending change, what is on screen, or a problem.</summary>
    public enum BlockState { Empty, Staged, Live, Issue }

    /// <summary>
    /// One block of the staged (or on-screen) picture. The Composer view model owns the state; the view only binds.
    /// The preview is the placed source card's own bitmap, so no stream or socket is opened here.
    /// </summary>
    public sealed class ComposerBlockViewModel : INotifyPropertyChanged, IDisposable
    {
        private SourceCardViewModel? _card;
        private WriteableBitmap? _previewBitmap;
        private bool _isPreviewSupported;
        private string _sourceName = string.Empty;
        private string _sourceMac = string.Empty;
        private BlockState _state = BlockState.Empty;
        private bool _isAvailableForArmed = true;
        private string? _unavailableReason;
        private string _sameAsText = string.Empty;
        private bool _isSelected;
        private bool _isMoveSource;
        private string? _issueText;

        public event PropertyChangedEventHandler? PropertyChanged;

        internal ComposerBlockViewModel(WindowSlot slot, int number, Action<int>? clear)
        {
            Index = slot.Index;
            Number = number;
            Label = number.ToString();
            Width = slot.Width;
            Height = slot.Height;
            NormalizedX = slot.NormalizedX;
            NormalizedY = slot.NormalizedY;
            NormalizedWidth = slot.NormalizedWidth;
            NormalizedHeight = slot.NormalizedHeight;
            ClearCommand = new RelayCommand(_ => clear?.Invoke(Index), _ => IsPlaced && clear != null);
        }

        /// <summary>API window index of the block (what the draft is keyed by).</summary>
        public int Index { get; }

        /// <summary>1-based position in logical order: the number the operator sees.</summary>
        public int Number { get; }

        public string Label { get; }

        public int Width { get; }
        public int Height { get; }

        /// <summary>Output pixels, e.g. "960x540".</summary>
        public string SizeText => $"{Width}x{Height}";

        public double NormalizedX { get; }
        public double NormalizedY { get; }
        public double NormalizedWidth { get; }
        public double NormalizedHeight { get; }

        public string SourceName { get => _sourceName; private set => Set(ref _sourceName, value); }

        public string SourceMac { get => _sourceMac; private set => Set(ref _sourceMac, value); }

        public bool IsPlaced => _sourceMac.Length > 0;

        /// <summary>The placed encoder's own live preview (same instance as the source card's), or null.</summary>
        public WriteableBitmap? PreviewBitmap { get => _previewBitmap; private set => Set(ref _previewBitmap, value); }

        public bool IsPreviewSupported { get => _isPreviewSupported; private set => Set(ref _isPreviewSupported, value); }

        public BlockState State { get => _state; private set => Set(ref _state, value); }

        /// <summary>With a source armed: can it go in this block? True when nothing is armed.</summary>
        public bool IsAvailableForArmed { get => _isAvailableForArmed; private set => Set(ref _isAvailableForArmed, value); }

        /// <summary>Why the armed source cannot go in this block (null when it can).</summary>
        public string? UnavailableReason { get => _unavailableReason; private set => Set(ref _unavailableReason, value); }

        /// <summary>"Same as 1" when a lower-numbered block holds the same encoder, else empty.</summary>
        public string SameAsText { get => _sameAsText; private set => Set(ref _sameAsText, value); }

        /// <summary>The Replace / Move strip is open on this block.</summary>
        public bool IsSelected { get => _isSelected; internal set => Set(ref _isSelected, value); }

        /// <summary>This block is the one being moved: the next tap on a block is its destination.</summary>
        public bool IsMoveSource { get => _isMoveSource; internal set => Set(ref _isMoveSource, value); }

        /// <summary>What blocks Take on this block (offline, size conflict); null when fine.</summary>
        public string? IssueText { get => _issueText; private set => Set(ref _issueText, value); }

        /// <summary>The corner clear control: one tap, enabled only while a source is placed.</summary>
        public ICommand ClearCommand { get; }

        internal void SetSource(string? mac, string? name, SourceCardViewModel? card)
        {
            SourceMac = mac ?? string.Empty;
            SourceName = name ?? string.Empty;
            Bind(card);
            OnPropertyChanged(nameof(IsPlaced));
            CommandManager.InvalidateRequerySuggested();
        }

        internal void SetStatus(BlockState state, string? issueText, string sameAsText, bool availableForArmed, string? unavailableReason)
        {
            State = state;
            IssueText = issueText;
            SameAsText = sameAsText;
            IsAvailableForArmed = availableForArmed;
            UnavailableReason = unavailableReason;
        }

        private void Bind(SourceCardViewModel? card)
        {
            if (!ReferenceEquals(_card, card))
            {
                if (_card != null) _card.PropertyChanged -= OnCardChanged;
                _card = card;
                if (_card != null) _card.PropertyChanged += OnCardChanged;
            }
            PreviewBitmap = _card?.PreviewBitmap;
            IsPreviewSupported = _card?.IsPreviewSupported ?? false;
        }

        private void OnCardChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_card == null) return;
            if (e.PropertyName == nameof(SourceCardViewModel.PreviewBitmap)) PreviewBitmap = _card.PreviewBitmap;
            else if (e.PropertyName == nameof(SourceCardViewModel.IsPreviewSupported)) IsPreviewSupported = _card.IsPreviewSupported;
        }

        /// <summary>Stops mirroring the source card (a block that leaves the screen must not keep it alive).</summary>
        public void Dispose() => Bind(null);

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            OnPropertyChanged(name);
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
