using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace AvasRoutingTool.Views
{
    /// <summary>
    /// A layout as outline cells (see <see cref="LayoutSchematic.CellsFor"/>), for the inline presets on the display card's action bar.
    /// Cells are drawn in this control's <see cref="UserControl.Foreground"/>; it is not focusable and takes no hits.
    /// </summary>
    public partial class LayoutGlyph : UserControl
    {
        public static readonly DependencyProperty LayoutIdProperty = DependencyProperty.Register(
            nameof(LayoutId), typeof(string), typeof(LayoutGlyph),
            new PropertyMetadata(null, (d, _) => ((LayoutGlyph)d).Rebuild()));

        private IReadOnlyList<SchematicCell> _cells = System.Array.Empty<SchematicCell>();

        public LayoutGlyph()
        {
            InitializeComponent();
            Rebuild();
        }

        public string? LayoutId
        {
            get => (string?)GetValue(LayoutIdProperty);
            set => SetValue(LayoutIdProperty, value);
        }

        /// <summary>How many blocks the glyph draws: the same count as the schematic of the layout.</summary>
        public int CellCount => _cells.Count;

        private void Rebuild()
        {
            _cells = LayoutSchematic.CellsFor(LayoutId);
            CellsHost.ItemsSource = _cells;
        }
    }
}
