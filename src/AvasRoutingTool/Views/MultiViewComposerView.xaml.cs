using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AvasRoutingTool.Logging;
using AvasRoutingTool.Sdvoe.MultiView;
using AvasRoutingTool.ViewModels;

namespace AvasRoutingTool.Views
{
    /// <summary>A source tile of the block picker: the source, and whether the block can take it (with the reason when not).</summary>
    public sealed class PickerTile
    {
        public PickerTile(SourceCardViewModel source, bool isEnabled, string note)
        {
            Source = source;
            IsEnabled = isEnabled;
            Note = note;
        }

        public SourceCardViewModel Source { get; }
        public bool IsEnabled { get; }
        public string Note { get; }
    }

    /// <summary>
    /// The MultiView Composer (spec 5.2). DataContext = <see cref="MultiViewComposerViewModel"/>. Every handler here
    /// forwards one tap to one view-model call; the code-behind otherwise only places the picker popup on its block.
    /// </summary>
    public partial class MultiViewComposerView : UserControl
    {
        /// <summary>The overview's source cards: what the picker offers.</summary>
        public static readonly DependencyProperty SourcesProperty = DependencyProperty.Register(
            nameof(Sources), typeof(IEnumerable<SourceCardViewModel>), typeof(MultiViewComposerView),
            new PropertyMetadata(null, (d, _) => ((MultiViewComposerView)d).SchedulePicker()));

        private MultiViewComposerViewModel? _watched;
        private bool _closingPicker;

        public MultiViewComposerView()
        {
            InitializeComponent();
            DataContextChanged += (_, _) => Watch(DataContext as MultiViewComposerViewModel);
            Loaded += (_, _) => SchedulePicker();
            Unloaded += (_, _) => ClosePicker();
            IsVisibleChanged += (_, _) => SchedulePicker();
        }

        public IEnumerable<SourceCardViewModel>? Sources
        {
            get => (IEnumerable<SourceCardViewModel>?)GetValue(SourcesProperty);
            set => SetValue(SourcesProperty, value);
        }

        private MultiViewComposerViewModel? Composer => DataContext as MultiViewComposerViewModel;

        // ---- taps -> view model ----

        private async void LayoutTile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: ComposerLayoutOptionViewModel option } && Composer is { } c)
                await Run(() => c.SelectLayoutAsync(option.Id), "Layout selection");
        }

        private void Block_Click(object sender, RoutedEventArgs e)
        {
            // Block commands pass the API window index (ComposerBlockViewModel.Index), never the displayed Number / Label.
            if (sender is FrameworkElement { DataContext: ComposerBlockViewModel block }) Composer?.TapBlock(block.Index);
        }

        private void Replace_Click(object sender, RoutedEventArgs e)
        {
            // API window index (ComposerBlockViewModel.Index), never Number / Label.
            if (sender is FrameworkElement { DataContext: ComposerBlockViewModel block }) Composer?.OpenPicker(block.Index);
        }

        private void Move_Click(object sender, RoutedEventArgs e)
        {
            // API window index (ComposerBlockViewModel.Index), never Number / Label.
            if (sender is FrameworkElement { DataContext: ComposerBlockViewModel block }) Composer?.BeginMove(block.Index);
        }

        private void PickerTile_Click(object sender, RoutedEventArgs e)
        {
            // PickerBlockIndex is the API window index the picker was opened on.
            if (sender is FrameworkElement { DataContext: PickerTile tile } && Composer is { PickerBlockIndex: int block } c)
                c.Pick(block, tile.Source);
        }

        private void Unplaced_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: DraftPlacement unplaced }) Composer?.ArmUnplaced(unplaced);
        }

        private void Disarm_Click(object sender, RoutedEventArgs e) => Composer?.Disarm();

        private void PickerCancel_Click(object sender, RoutedEventArgs e) => Composer?.Disarm();

        private async void Take_Click(object sender, RoutedEventArgs e)
        {
            if (Composer is { } c) await Run(c.TakeAsync, "Take");
        }

        private async void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (Composer is { } c) await Run(c.UndoAsync, "Undo");
        }

        private void Discard_Click(object sender, RoutedEventArgs e) => Composer?.Discard();

        private void ClearAll_Click(object sender, RoutedEventArgs e) => Composer?.ClearAll();

        /// <summary>Back to displays: the overview closes the Composer (a draft with changes is kept on the card).</summary>
        private void Close_Click(object sender, RoutedEventArgs e) => ViewLookup.FindMainViewModel(this)?.CloseComposer();

        private static async Task Run(Func<Task> action, string what)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                AppLogger.Error("COMPOSER", $"{what} failed in the view", ex);
            }
        }

        // ---- the picker popup: placement only ----

        private void Watch(MultiViewComposerViewModel? composer)
        {
            if (_watched != null) _watched.PropertyChanged -= OnComposerChanged;
            _watched = composer;
            if (_watched != null) _watched.PropertyChanged += OnComposerChanged;
            SchedulePicker();
        }

        private void OnComposerChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(MultiViewComposerViewModel.PickerBlockIndex) or nameof(MultiViewComposerViewModel.Draft))
                SchedulePicker();
        }

        /// <summary>After layout, so the block's container exists to anchor to.</summary>
        private void SchedulePicker() => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdatePicker));

        private void UpdatePicker()
        {
            var c = Composer;
            var block = c?.PickerBlockIndex is int index ? c.Blocks.FirstOrDefault(b => b.Index == index) : null;
            if (c == null || block == null || !IsLoaded || !IsVisible)
            {
                ClosePicker();
                return;
            }

            var tiles = (Sources ?? Enumerable.Empty<SourceCardViewModel>())
                .Select(s =>
                {
                    var check = c.CheckPlace(block.Index, s);
                    string note = check.Ok
                        ? (s.IsPreviewSupported ? "Live preview" : "No preview - the picture still goes to the screen")
                        : check.Reason ?? "This block cannot take it";
                    return new PickerTile(s, check.Ok, note);
                })
                .ToList();

            PickerTitle.Text = $"Choose a source for block {block.Number} ({block.SizeText})";
            PickerItems.ItemsSource = tiles;
            PickerEmpty.Visibility = tiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            PickerPopup.PlacementTarget = BlocksHost.ItemContainerGenerator.ContainerFromItem(block) as UIElement ?? CanvasHost;
            PickerPopup.IsOpen = true;
        }

        private void ClosePicker()
        {
            if (!PickerPopup.IsOpen) return;
            _closingPicker = true;
            try { PickerPopup.IsOpen = false; }
            finally { _closingPicker = false; }
        }

        /// <summary>A tap outside closed the picker: the view model closes it too.</summary>
        private void PickerPopup_Closed(object? sender, EventArgs e)
        {
            if (!_closingPicker && Composer?.PickerBlockIndex != null) Composer.Disarm();
        }
    }
}
