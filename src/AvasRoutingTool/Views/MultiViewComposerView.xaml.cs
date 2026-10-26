using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AvasRoutingTool.Logging;
using AvasRoutingTool.Sdvoe;
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
    /// forwards one tap, drop or drag to view-model calls; the code-behind otherwise only places the picker popup.
    /// </summary>
    public partial class MultiViewComposerView : UserControl
    {
        /// <summary>The overview's source cards: what the picker offers.</summary>
        public static readonly DependencyProperty SourcesProperty = DependencyProperty.Register(
            nameof(Sources), typeof(IEnumerable<SourceCardViewModel>), typeof(MultiViewComposerView),
            new PropertyMetadata(null, (d, _) => ((MultiViewComposerView)d).SchedulePicker()));

        /// <summary>Width of the layout tiles' schematics (narrower with the rails on a small window).</summary>
        public static readonly DependencyProperty SchematicWidthProperty = DependencyProperty.Register(
            nameof(SchematicWidth), typeof(double), typeof(MultiViewComposerView), new PropertyMetadata(120.0));

        public static readonly DependencyProperty SchematicHeightProperty = DependencyProperty.Register(
            nameof(SchematicHeight), typeof(double), typeof(MultiViewComposerView), new PropertyMetadata(70.0));

        /// <summary>Below this width the rails narrow (280 -> 240, 320 -> 264) so the canvas keeps usable blocks.</summary>
        internal const double NarrowWidth = 1200;

        private MultiViewComposerViewModel? _watched;
        private bool _closingPicker;

        public MultiViewComposerView()
        {
            InitializeComponent();
            SizeChanged += (_, e) => FitRails(e.NewSize.Width);
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

        public double SchematicWidth
        {
            get => (double)GetValue(SchematicWidthProperty);
            set => SetValue(SchematicWidthProperty, value);
        }

        public double SchematicHeight
        {
            get => (double)GetValue(SchematicHeightProperty);
            set => SetValue(SchematicHeightProperty, value);
        }

        private MultiViewComposerViewModel? Composer => DataContext as MultiViewComposerViewModel;

        /// <summary>Layout only: on a small window (MainWindow minimum 1024 px) the rails give the canvas room.</summary>
        private void FitRails(double width)
        {
            bool narrow = width > 0 && width < NarrowWidth;
            LeftRailColumn.Width = new GridLength(narrow ? 240 : 280);
            RightRailColumn.Width = new GridLength(narrow ? 264 : 320);
            SchematicWidth = narrow ? 96 : 120;
            SchematicHeight = narrow ? 56 : 70;
        }

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

        // ---- tap on an empty area: stop placing / moving (spec 6) ----

        private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (Composer is not { } c || IsOnAControl(e.OriginalSource as DependencyObject)) return;
            if (c.ArmedSource != null || c.MovingFromIndex != null || c.Blocks.Any(b => b.IsSelected)) c.Disarm();
        }

        /// <summary>A tap that landed on a block, a button, a scroll bar or the picker is theirs, not an "empty area".</summary>
        private bool IsOnAControl(DependencyObject? d)
        {
            for (; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            {
                if (ReferenceEquals(d, this)) return false;
                if (d is ButtonBase or ScrollBar or Thumb or TextBoxBase || ReferenceEquals(d, PickerPopup.Child)) return true;
            }
            return true; // outside this view's tree (another popup): not ours
        }

        // ---- drag and drop (spec 6, gesture 3): a source card onto a block places it; a block onto a block moves / swaps ----

        private readonly TapGesture _blockGesture = new();

        /// <summary>The block drag gesture (tests).</summary>
        internal TapGesture BlockGesture => _blockGesture;

        private void BlockSurface_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _blockGesture.Press(e.GetPosition(this));

        /// <summary>Any release in the view ends the press: a later press elsewhere can never continue an old one.</summary>
        private void Root_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _blockGesture.Release();

        /// <summary>The block lost the mouse (released, window switch, drag started): forget the press.</summary>
        private void BlockSurface_LostMouseCapture(object sender, MouseEventArgs e) => _blockGesture.Release();

        private void BlockSurface_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || sender is not FrameworkElement { DataContext: ComposerBlockViewModel block } surface) return;
            if (!block.IsPlaced || Composer is not { IsBusy: false }) return;
            if (!_blockGesture.Move(e.GetPosition(this))) return; // 8 px before a drag starts

            Mouse.Capture(null); // the block button holds the capture; the drag takes over (no Click follows)
            DragDrop.DoDragDrop(surface, new DataObject(typeof(ComposerBlockViewModel), block), DragDropEffects.Move);
            _blockGesture.Release();
        }

        private void Block_DragOver(object sender, DragEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: ComposerBlockViewModel block } root) return;
            e.Effects = DropEffectFor(block, e.Data);
            root.Tag = e.Effects == DragDropEffects.None ? "DropNo" : "DropOk";
            e.Handled = true;
        }

        private void Block_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is FrameworkElement root) root.Tag = null;
        }

        private void Block_Drop(object sender, DragEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: ComposerBlockViewModel block } root) return;
            root.Tag = null;
            DropOn(block, e.Data);
            e.Handled = true;
        }

        /// <summary>What a drop on <paramref name="target"/> would do: Link (place), Move (move / swap) or None (refused, as CheckPlace says).</summary>
        internal DragDropEffects DropEffectFor(ComposerBlockViewModel target, IDataObject data)
        {
            var c = Composer;
            if (c == null || c.IsBusy || c.Draft == null) return DragDropEffects.None;

            if (data.GetDataPresent(typeof(ComposerBlockViewModel)))
            {
                var from = data.GetData(typeof(ComposerBlockViewModel)) as ComposerBlockViewModel;
                return from != null && from.IsPlaced && !ReferenceEquals(from, target) && c.Blocks.Contains(from)
                    ? DragDropEffects.Move
                    : DragDropEffects.None;
            }

            var source = SourceIn(data);
            return source != null && c.CheckPlace(target.Index, source).Ok ? DragDropEffects.Link : DragDropEffects.None;
        }

        /// <summary>
        /// Drops on a block through the view model (API window index, never Number / Label): a source is placed (refused
        /// with the reason in the banner, as a tap), a block is moved there (occupied = swap). True when the picture changed.
        /// </summary>
        internal bool DropOn(ComposerBlockViewModel target, IDataObject data)
        {
            var c = Composer;
            if (c == null || c.Draft == null) return false;
            string before = Snapshot(c);

            if (data.GetDataPresent(typeof(ComposerBlockViewModel)))
            {
                if (data.GetData(typeof(ComposerBlockViewModel)) is ComposerBlockViewModel from && !ReferenceEquals(from, target) && from.IsPlaced)
                {
                    c.BeginMove(from.Index);
                    c.TapBlock(target.Index);
                }
            }
            else if (SourceIn(data) is { } source)
            {
                c.Pick(target.Index, source);
            }
            return Snapshot(c) != before;
        }

        private SourceCardViewModel? SourceIn(IDataObject data)
        {
            string? mac = data.GetDataPresent(typeof(AvasDevice))
                ? (data.GetData(typeof(AvasDevice)) as AvasDevice)?.DeviceId
                : data.GetDataPresent(typeof(SourceCardViewModel))
                    ? (data.GetData(typeof(SourceCardViewModel)) as SourceCardViewModel)?.MacAddress
                    : null;
            if (string.IsNullOrEmpty(mac)) return null;
            string norm = SdvoeClient.NormalizeMac(mac);
            return (Sources ?? Enumerable.Empty<SourceCardViewModel>()).FirstOrDefault(s => SdvoeClient.NormalizeMac(s.MacAddress) == norm);
        }

        private static string Snapshot(MultiViewComposerViewModel c) =>
            c.Draft == null
                ? string.Empty
                : string.Join(",", c.Draft.Placements.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value.SourceMac}"));

        // ---- the picker popup: placement only ----

        /// <summary>The picker never grows past 70 % of the work area (and not below a usable 320 px); its tiles scroll.</summary>
        internal double PickerMaxHeight => Math.Max(320, Math.Round(SystemParameters.WorkArea.Height * 0.7));

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
            PickerFrame.MaxHeight = PickerMaxHeight;
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
