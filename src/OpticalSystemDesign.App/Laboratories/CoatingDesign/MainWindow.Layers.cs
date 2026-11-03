using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.Core.Materials;

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
            ItemsSource = _displayRows,
            AutoGenerateColumns = false,
            CanUserSortColumns = false,
            CanUserReorderColumns = false,
            SelectionMode = DataGridSelectionMode.Extended,
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
        headers.Setters.Add(new Setter(PaddingProperty, new Thickness(10, 2, 10, Math.Max(24, table.FontSize * 1.5) + 2)));
        headers.Setters.Add(new Setter(FontSizeProperty, new Binding(nameof(DataGrid.FontSize)) { Source = table }));
        headers.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        table.Styles.Add(headers);

        var numberTheme = LayerCellTheme(HorizontalAlignment.Center, numeric: true);
        numberTheme.Setters.Add(new Setter(NameProperty, "CoatingLayerNumberCell"));
        table.Columns.Add(new DataGridTextColumn
        {
            Header = LayerHeader("#", TextAlignment.Center), Binding = new Binding(nameof(LayerRow.Number)), IsReadOnly = true,
            Width = new DataGridLength(52), CellTheme = numberTheme
        });
        table.Columns.Add(new DataGridTemplateColumn
        {
            Header = "材料", IsReadOnly = true, MinWidth = 220,
            ClipboardContentBinding = new Binding(nameof(LayerRow.MaterialName)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            CellTheme = LayerCellTheme(HorizontalAlignment.Stretch),
            CellTemplate = new FuncDataTemplate<LayerRow>((_, _) =>
            {
                var picker = new ComboBox { Name = "CoatingRowMaterial", ItemsSource = _rowMaterialIds, HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch, MinWidth = 0, Margin = new(0, 2), Padding = new(5, 1) };
                picker.Bind(FontSizeProperty, new Binding(nameof(DataGrid.FontSize)) { Source = table });
                picker.ItemTemplate = new FuncDataTemplate<string>((id, _) =>
                {
                    var material = _session.Current.Materials.Concat(_materials.Films).FirstOrDefault(m => m.Id == id);
                    var content = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 8, ClipToBounds = true };
                    var marker = new Ellipse { Name = "CoatingMaterialMarker", Width = 13, Height = 13, Fill = MaterialBrushFor(id ?? ""), VerticalAlignment = VerticalAlignment.Center };
                    var label = new TextBlock { Name = "CoatingMaterialLabel", Text = material?.Name ?? id, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                    label.Bind(TextBlock.FontSizeProperty, new Binding(nameof(DataGrid.FontSize)) { Source = table });
                    ToolTip.SetTip(content, material?.Name ?? id); content.Children.Add(marker); Grid.SetColumn(label, 1); content.Children.Add(label); return content;
                });
                picker.Bind(ComboBox.SelectedItemProperty, new Binding(nameof(LayerRow.MaterialId)) { Mode = BindingMode.OneWay });
                picker.SelectionChanged += (_, _) =>
                {
                    if (!_loading && picker.DataContext is LayerRow row && picker.SelectedItem is string id && id != row.MaterialId)
                        TrackOperation(() => ChangeInlineMaterialAsync(row, id));
                };
                return picker;
            })
        });
        table.Columns.Add(new DataGridTextColumn
        {
            Header = LayerHeader("d (nm)", TextAlignment.Right), Binding = new Binding(nameof(LayerRow.ThicknessText)) { Mode = BindingMode.TwoWay },
            Width = new DataGridLength(148), FontWeight = FontWeight.SemiBold, CellTheme = LayerCellTheme(HorizontalAlignment.Right, numeric: true)
        });
        var opticalTheme = LayerCellTheme(HorizontalAlignment.Right, numeric: true);
        opticalTheme.Setters.Add(new Setter(ToolTip.TipProperty, new Binding(nameof(LayerRow.OpticalThicknessHelp))));
        table.Columns.Add(new DataGridTextColumn
        {
            Header = LayerHeader("OT", TextAlignment.Right), Binding = new Binding(nameof(LayerRow.OpticalThicknessText)) { Mode = BindingMode.TwoWay },
            Width = new DataGridLength(148), CellTheme = opticalTheme
        });
        table.Columns.Add(new DataGridTextColumn
        { Header = LayerHeader("QW", TextAlignment.Right), Binding = new Binding(nameof(LayerRow.QuarterWaveText)) { Mode = BindingMode.TwoWay }, Width = new DataGridLength(100), CellTheme = opticalTheme });
        table.Columns.Add(new DataGridTextColumn
        { Header = LayerHeader("FW", TextAlignment.Right), Binding = new Binding(nameof(LayerRow.FullWaveText)) { Mode = BindingMode.TwoWay }, Width = new DataGridLength(100), CellTheme = opticalTheme });
        var rowToolsTheme = LayerCellTheme(HorizontalAlignment.Stretch, inset: false);
        table.Columns.Add(new DataGridTemplateColumn
        {
            Header = "", Width = new DataGridLength(136),
            ClipboardContentBinding = new Binding(nameof(LayerRow.Variable)),
            CellTheme = rowToolsTheme,
            CellTemplate = new FuncDataTemplate<LayerRow>((_, _) =>
            {
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
                var glyph = new LocalIcon { Width = 16, Height = 16 };
                glyph.BindThemeResource(LocalIcon.StrokeProperty, ThemeResourceBindings.TextSecondary);
                glyph.Bind(LocalIcon.IconNameProperty, new Binding(nameof(LayerRow.Locked)) { Converter = new FuncValueConverter<bool, string>(locked => locked ? "coating-layer-lock" : "coating-layer-unlock") });
                var check = new ToggleButton { Name = "CoatingLayerLock", Content = glyph, Width = 30, Height = 32, Padding = new(4), MinHeight = 0, Background = Brushes.Transparent, BorderThickness = new(0) };
                check.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(LayerRow.Locked)) { Mode = BindingMode.TwoWay });
                AutomationProperties.SetName(check, "锁定膜层"); ToolTip.SetTip(check, "锁定：固定优化膜厚；仍允许手动编辑。"); actions.Children.Add(check);
                foreach (var (name, icon, action) in new[] { ("膜层上移", "action-move-up", "up"), ("膜层下移", "action-move-down", "down"), ("删除膜层", "action-delete", "remove") })
                {
                    var rowIcon = new LocalIcon { IconName = icon, Width = 16, Height = 16 };
                    rowIcon.BindThemeResource(LocalIcon.StrokeProperty, ThemeResourceBindings.TextSecondary);
                    var button = new Button { Content = rowIcon, Width = 30, Height = 32, MinHeight = 0, Padding = new(4), Background = Brushes.Transparent, BorderThickness = new(0) };
                    AutomationProperties.SetName(button, name); ToolTip.SetTip(button, name);
                    if (action != "remove") button.Bind(IsEnabledProperty, new Binding(nameof(LayerRow.Number)) { Converter = new FuncValueConverter<int, bool>(number => action == "up" ? number > 1 : number < _rows.Count) });
                    button.Click += (_, _) => { if (button.DataContext is LayerRow row) TrackOperation(() => RowCommandAsync(row, action)); }; actions.Children.Add(button);
                }
                return actions;
            }, supportsRecycling: true)
        });
        void UpdateDensity()
        {
            // Leave the user's font size intact, including larger accessibility settings.
            var scale = Math.Max(1, table.FontSize / 13);
            foreach (var column in table.Columns.OfType<DataGridTextColumn>()) column.FontSize = table.FontSize;
            foreach (var header in table.Columns.Select(c => c.Header).OfType<TextBlock>()) header.FontSize = table.FontSize;
            var boundaryHeight = Math.Max(24, table.FontSize * 1.5); var headerHeight = Math.Max(34, table.FontSize * 1.7);
            table.RowHeight = Math.Max(36, table.FontSize * 2);
            table.ColumnHeaderHeight = headerHeight + boundaryHeight;
            _substrateBand.Margin = new(0, headerHeight, 0, 0); _substrateBand.Height = boundaryHeight;
            table.Columns[0].Width = new DataGridLength(40 * scale);
            table.Columns[2].Width = new DataGridLength(84 * scale);
            table.Columns[3].Width = new DataGridLength(84 * scale);
            table.Columns[4].Width = new DataGridLength(84 * scale);
            table.Columns[5].Width = new DataGridLength(84 * scale);
            table.Columns[6].Width = new DataGridLength(136);
        }
        table.PropertyChanged += (_, e) => { if (e.Property == FontSizeProperty) UpdateDensity(); };
        UpdateDensity();
        ToolTip.SetTip((Control)table.Columns[0].Header!, "从基底侧编号；保存及计算按外侧到基底的物理顺序。");
        ToolTip.SetTip((Control)table.Columns[2].Header!, "物理厚度，单位nm。内部保留完整精度。");
        ToolTip.SetTip((Control)table.Columns[3].Header!, "光学厚度n(λ₀)d，单位nm。");
        return table;
    }

    private static ControlTheme LayerCellTheme(HorizontalAlignment alignment, bool numeric = false, bool inset = true)
    {
        var theme = new ControlTheme(typeof(DataGridCell))
        {
            BasedOn = global::Avalonia.Application.Current?.FindResource(typeof(DataGridCell)) as ControlTheme,
            Setters =
            {
                new Setter(DataGridCell.HorizontalContentAlignmentProperty, alignment),
                new Setter(DataGridCell.VerticalContentAlignmentProperty, VerticalAlignment.Center),
                // Text columns already give their display/editor a native horizontal inset.
                new Setter(DataGridCell.PaddingProperty, numeric || !inset ? new Thickness(0) : new Thickness(12, 0))
            }
        };
        if (numeric) theme.Setters.Add(new Setter(FontFamilyProperty, new DynamicResourceExtension(ProductFonts.NumericResource)));
        return theme;
    }

    private static TextBlock LayerHeader(string text, TextAlignment alignment) => new()
    {
        Text = text, TextAlignment = alignment, HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap
    };

    private void RefreshLayerOpticalThickness()
    {
        double wavelength;
        try
        {
            wavelength = Value("reference");
            if (wavelength <= 0) throw new ArgumentException("参考波长必须大于零。");
        }
        catch (ArgumentException error)
        {
            foreach (var row in _rows) row.UpdateOpticalThickness(null, 0, error.Message);
            return;
        }
        var registry = new MaterialRegistry();
        var indices = new Dictionary<string, (double? Index, string? Notice)>();
        foreach (var row in _rows)
        {
            if (!indices.TryGetValue(row.MaterialId, out var value))
            {
                try
                {
                    var material = _session.Current.Materials.FirstOrDefault(m => m.Id == row.MaterialId)
                        ?? throw new ArgumentException($"找不到材料快照：{row.MaterialId}");
                    if (!material.HoldTabulatedEndpoints && (wavelength < material.MinimumNm || wavelength > material.MaximumNm))
                        throw new ArgumentException($"参考波长超出 {material.Name} 的有效范围 {material.MinimumNm:G7}–{material.MaximumNm:G7} nm。");
                    var index = material.Resolve(registry).RefractiveIndex(wavelength);
                    if (!double.IsFinite(index) || index <= 0) throw new ArgumentException("参考波长处的折射率必须为正有限数值。");
                    value = (index, material.RangeNotice(wavelength, wavelength));
                }
                catch (Exception error) when (error is ArgumentException or InvalidDataException or KeyNotFoundException or NotSupportedException)
                { value = (null, error.Message); }
                indices.Add(row.MaterialId, value);
            }
            row.UpdateOpticalThickness(value.Index, wavelength, value.Notice);
        }
    }
}
