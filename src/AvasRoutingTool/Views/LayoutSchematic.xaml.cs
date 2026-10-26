using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AvasRoutingTool.Sdvoe.MultiView;

namespace AvasRoutingTool.Views
{
    /// <summary>One rectangle of a schematic, in fractions of the screen; <see cref="Number"/> is the block number the operator sees.</summary>
    public sealed class SchematicCell
    {
        public SchematicCell(double x, double y, double w, double h, string number)
        {
            X = x;
            Y = y;
            W = w;
            H = h;
            Number = number;
        }

        public double X { get; }
        public double Y { get; }
        public double W { get; }
        public double H { get; }
        public string Number { get; }
    }

    /// <summary>
    /// Draws a layout as proportional rectangles from <see cref="LayoutId"/>: the four MultiView presets from
    /// <see cref="MultiViewPresets.Build"/> on a 1920x1080 screen (the real block geometry, letterbox gaps
    /// included), <c>single</c> as one full block, <c>off</c> as a dark screen with "OFF".
    /// </summary>
    public partial class LayoutSchematic : UserControl
    {
        private const int SchematicWidth = 1920;
        private const int SchematicHeight = 1080;

        public static readonly DependencyProperty LayoutIdProperty = DependencyProperty.Register(
            nameof(LayoutId), typeof(string), typeof(LayoutSchematic),
            new PropertyMetadata(null, (d, _) => ((LayoutSchematic)d).Rebuild()));

        private IReadOnlyList<SchematicCell> _cells = Array.Empty<SchematicCell>();

        public LayoutSchematic()
        {
            InitializeComponent();
            Rebuild();
        }

        public string? LayoutId
        {
            get => (string?)GetValue(LayoutIdProperty);
            set => SetValue(LayoutIdProperty, value);
        }

        /// <summary>How many blocks the schematic draws.</summary>
        public int CellCount => _cells.Count;

        /// <summary>The cells for a layout id, back-to-front (the lowest API window index is painted on top, as the decoder does).</summary>
        public static IReadOnlyList<SchematicCell> CellsFor(string? id)
        {
            if (MultiViewPresets.IsMultiView(id))
            {
                var plan = MultiViewPresets.Build(id!, SchematicWidth, SchematicHeight);
                var slots = plan.Slots.ToList();
                return plan.PaintOrder
                    .Select(s => new SchematicCell(s.NormalizedX, s.NormalizedY, s.NormalizedWidth, s.NormalizedHeight,
                        (slots.IndexOf(s) + 1).ToString()))
                    .ToList();
            }
            if (string.Equals(id, "single", StringComparison.OrdinalIgnoreCase))
                return new[] { new SchematicCell(0, 0, 1, 1, "1") };
            return Array.Empty<SchematicCell>();
        }

        private void Rebuild()
        {
            _cells = CellsFor(LayoutId);
            CellsHost.ItemsSource = _cells;
            OffGlyph.Visibility = string.Equals(LayoutId, "off", StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }
}
