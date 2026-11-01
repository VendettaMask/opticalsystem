using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.App.Laboratories.CoatingDesign;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, IBrush> _materialBrushes = new(StringComparer.Ordinal);
    private static readonly string[] MaterialMarkerColors =
        ["#2BA653", "#00A5BF", "#3479D8", "#A06ACD", "#DF8D2D", "#DB647A", "#518BA0", "#899E34"];

    private IBrush MaterialBrushFor(string id)
    {
        // Register the experiment's film materials in a stable order, independent of layer order.
        // Keep previous assignments while editing; these are identity markers, never optical properties.
        foreach (var material in _session.Current.Materials
                     .Where(m => m.Id != _session.Current.IncidentId && m.Id != _session.Current.SubstrateId)
                     .OrderBy(m => m.Id, StringComparer.Ordinal))
            Register(material.Id);
        return Register(id);

        IBrush Register(string materialId)
        {
            if (_session.Current.Materials.FirstOrDefault(m => m.Id == materialId)?.DisplayColor is { } declared
                && Color.TryParse(declared, out var declaredColor))
                return _materialBrushes[materialId] = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(declaredColor);
            if (_materialBrushes.TryGetValue(materialId, out var brush)) return brush;
            var index = _materialBrushes.Count;
            var color = index < MaterialMarkerColors.Length ? Color.Parse(MaterialMarkerColors[index])
                : new HsvColor(1, ((index - MaterialMarkerColors.Length) * 137.507764 + 24) % 360, .65, .78).ToRgb();
            return _materialBrushes[materialId] = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(color);
        }
    }

    private DataGrid BuildLayerTable()
    {
        var table = new DataGrid
        {
            Name = "CoatingLayerTable",
            ItemsSource = _rows,
            AutoGenerateColumns = false,
            CanUserSortColumns = false,
            CanUserReorderColumns = false,
            SelectionMode = DataGridSelectionMode.Single,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            BorderThickness = new Thickness(0),
            MinHeight = 120
        };
        table.BindThemeResource(DataGrid.HorizontalGridLinesBrushProperty, "DataGridGridLinesBrush");
        var headers = new Style(s => s.OfType<DataGridColumnHeader>().Not(x => x.Class(":disabled")));
        headers.Setters.Add(new Setter(BackgroundProperty, new DynamicResourceExtension(ThemeResourceBindings.SubtleSurface)));
        headers.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension(ThemeResourceBindings.TextSecondary)));
        headers.Setters.Add(new Setter(BorderBrushProperty, new DynamicResourceExtension("DataGridGridLinesBrush")));
        headers.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0, 1, 0, 1)));
        headers.Setters.Add(new Setter(PaddingProperty, new Thickness(12, 0)));
        headers.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        table.Styles.Add(headers);

        table.Columns.Add(new DataGridTextColumn
        {
            Header = LayerHeader("层序", TextAlignment.Center), Binding = new Binding(nameof(LayerRow.Number)), IsReadOnly = true,
            Width = new DataGridLength(52), CellTheme = LayerCellTheme(HorizontalAlignment.Center, numeric: true)
        });
        table.Columns.Add(new DataGridTemplateColumn
        {
            Header = "材料", IsReadOnly = true, MinWidth = 120,
            ClipboardContentBinding = new Binding(nameof(LayerRow.MaterialName)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            CellTheme = LayerCellTheme(HorizontalAlignment.Stretch),
            CellTemplate = new FuncDataTemplate<LayerRow>((_, _) =>
            {
                var row = new Grid { ColumnDefinitions = new("Auto,Auto,*"), ColumnSpacing = 9, ClipToBounds = true };
                var marker = new Ellipse
                {
                    Name = "CoatingMaterialMarker", Width = 9, Height = 9,
                    VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
                };
                marker.Bind(Shape.FillProperty, new Binding(nameof(LayerRow.MaterialBrush)));
                var name = new TextBlock
                {
                    Name = "CoatingMaterialLabel", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.Medium,
                    TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 220
                };
                name.Bind(TextBlock.TextProperty, new Binding(nameof(LayerRow.MaterialLabel)));
                var annotation = new TextBlock
                {
                    Name = "CoatingMaterialAnnotation", VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                annotation.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextMuted);
                annotation.Bind(TextBlock.TextProperty, new Binding(nameof(LayerRow.MaterialAnnotation)));
                row.Bind(ToolTip.TipProperty, new Binding(nameof(LayerRow.MaterialName)));
                row.Children.Add(marker); Grid.SetColumn(name, 1); row.Children.Add(name);
                Grid.SetColumn(annotation, 2); row.Children.Add(annotation);
                row.SizeChanged += (_, _) => name.MaxWidth = Math.Max(16, row.Bounds.Width - 27);
                return row;
            }, supportsRecycling: true)
        });
        table.Columns.Add(new DataGridTextColumn
        {
            Header = LayerHeader("厚度 (nm)", TextAlignment.Right), Binding = new Binding(nameof(LayerRow.ThicknessText)) { Mode = BindingMode.TwoWay },
            Width = new DataGridLength(124), CellTheme = LayerCellTheme(HorizontalAlignment.Right, numeric: true)
        });
        table.Columns.Add(new DataGridTemplateColumn
        {
            Header = LayerHeader("变量", TextAlignment.Center), Width = new DataGridLength(68),
            ClipboardContentBinding = new Binding(nameof(LayerRow.Variable)),
            CellTheme = LayerCellTheme(HorizontalAlignment.Stretch),
            CellTemplate = new FuncDataTemplate<LayerRow>((_, _) =>
            {
                var check = new CheckBox
                {
                    Name = "CoatingLayerVariable", HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center,
                    Width = 32, Height = 32, MinWidth = 32, MinHeight = 32, Padding = new Thickness(0)
                };
                check.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(LayerRow.Variable)) { Mode = BindingMode.TwoWay });
                AutomationProperties.SetName(check, "参与膜厚优化");
                ToolTip.SetTip(check, "勾选：优化膜厚；取消：固定膜厚");
                return check;
            }, supportsRecycling: true)
        });
        void UpdateDensity()
        {
            // Leave the user's font size intact, including larger accessibility settings.
            var scale = Math.Max(1, table.FontSize / 13);
            table.RowHeight = 38 * scale;
            table.ColumnHeaderHeight = 32 * scale;
            table.Columns[0].Width = new DataGridLength(52 * scale);
            table.Columns[2].Width = new DataGridLength(124 * scale);
            table.Columns[3].Width = new DataGridLength(68 * scale);
        }
        table.PropertyChanged += (_, e) => { if (e.Property == FontSizeProperty) UpdateDensity(); };
        UpdateDensity();
        return table;
    }

    private static ControlTheme LayerCellTheme(HorizontalAlignment alignment, bool numeric = false)
    {
        var theme = new ControlTheme(typeof(DataGridCell))
        {
            BasedOn = global::Avalonia.Application.Current?.FindResource(typeof(DataGridCell)) as ControlTheme,
            Setters =
            {
                new Setter(DataGridCell.HorizontalContentAlignmentProperty, alignment),
                new Setter(DataGridCell.VerticalContentAlignmentProperty, VerticalAlignment.Center),
                // Text columns already give their display/editor a native horizontal inset.
                new Setter(DataGridCell.PaddingProperty, numeric ? new Thickness(0) : new Thickness(12, 0))
            }
        };
        if (numeric) theme.Setters.Add(new Setter(FontFamilyProperty, new DynamicResourceExtension(ProductFonts.NumericResource)));
        return theme;
    }

    private static TextBlock LayerHeader(string text, TextAlignment alignment) => new()
    {
        Text = text, TextAlignment = alignment, HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Center
    };
}
