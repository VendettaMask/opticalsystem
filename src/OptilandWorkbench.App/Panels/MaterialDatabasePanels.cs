using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App.Panels;

public sealed class MaterialLibraryPanel : UserControl
{
    private readonly IMaterialCatalogService _materials;
    private IReadOnlyList<MaterialCatalogDto> _catalogs = Array.Empty<MaterialCatalogDto>();
    private IReadOnlyList<GlassMaterialDto> _glasses = Array.Empty<GlassMaterialDto>();
    private IReadOnlyList<GlassMaterialDto> _visibleGlasses = Array.Empty<GlassMaterialDto>();
    private readonly ComboBox _catalog = new() { MinHeight = 34 };
    private readonly TextBox _search = new() { MinHeight = 34, PlaceholderText = "输入玻璃名称" };
    private readonly ListBox _glassList = new() { MinHeight = 270 };
    private readonly TextBox _name = ReadOnlyField();
    private readonly TextBox _formula = ReadOnlyField();
    private readonly TextBox _state = ReadOnlyField();
    private readonly TextBlock _nd = MetricValue();
    private readonly TextBlock _vd = MetricValue();
    private readonly TextBlock _selectionSummary = new()
    {
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock[] _coefficientLabels = new TextBlock[10];
    private readonly TextBox[] _coefficientValues = new TextBox[10];
    private readonly Dictionary<string, TextBox> _properties = new(StringComparer.Ordinal);

    public MaterialLibraryPanel(IMaterialCatalogService materials)
    {
        _materials = materials;
        _selectionSummary.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        for (var index = 0; index < _coefficientLabels.Length; index++)
        {
            _coefficientLabels[index] = FieldLabel($"A{index}:");
            _coefficientValues[index] = ReadOnlyField();
        }

        _catalog.SelectionChanged += (_, _) => RefreshGlassList(selectFirst: true);
        _search.TextChanged += (_, _) => RefreshGlassList(selectFirst: true);
        _glassList.SelectionChanged += (_, _) => ShowSelectedGlass();

        Content = BuildPage();
        ReloadCatalogs();
    }

    private Control BuildPage()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto")
        };
        root.BindThemeResource(Panel.BackgroundProperty, ThemeResourceBindings.Workspace);

        var titleBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(236, 247, 251)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(14, 10),
            Child = new TextBlock
            {
                Text = "材料库",
                FontSize = 18,
                FontWeight = FontWeight.SemiBold
            }
        };
        titleBar.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.SubtleSurface);
        titleBar.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        Grid.SetRow(titleBar, 0);
        root.Children.Add(titleBar);

        var catalogRow = new Grid
        {
            Margin = new Thickness(18, 14, 18, 10),
            MinWidth = 520,
            ColumnDefinitions = new ColumnDefinitions("Auto,420,*")
        };
        Add(catalogRow, FieldLabel("分类："), 0);
        Add(catalogRow, _catalog, 1);
        var catalogScroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = catalogRow
        };
        Grid.SetRow(catalogScroller, 1);
        root.Children.Add(catalogScroller);

        var body = new Grid
        {
            Margin = new Thickness(18, 0, 18, 12),
            MinWidth = 1040,
            ColumnDefinitions = new ColumnDefinitions("5*,18,5.5*")
        };
        Add(body, BuildGlassSelector(), 0);
        Add(body, BuildDetails(), 2);
        var bodyScroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = body
        };
        Grid.SetRow(bodyScroller, 2);
        root.Children.Add(bodyScroller);

        var commandBar = BuildCommandBar();
        Grid.SetRow(commandBar, 3);
        root.Children.Add(commandBar);
        return root;
    }

    private Control BuildGlassSelector()
    {
        var panel = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,280,Auto,Auto,Auto,Auto,Auto"),
            RowSpacing = 8
        };

        var glassHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Add(glassHeader, FieldLabel("玻璃："), 0);
        Add(glassHeader, _selectionSummary, 2);
        Grid.SetRow(glassHeader, 0);
        panel.Children.Add(glassHeader);

        var listFrame = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = _glassList
        };
        listFrame.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        listFrame.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        Grid.SetRow(listFrame, 1);
        panel.Children.Add(listFrame);

        AddRow(panel, LabeledControl("搜索：", _search), 2);
        AddRow(panel, LabeledControl("名称：", _name), 3);
        AddRow(panel, LabeledControl("公式：", _formula), 4);
        AddRow(panel, LabeledControl("状态：", _state), 5);

        var metrics = new Grid
        {
            Margin = new Thickness(0, 2, 0, 0),
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,*")
        };
        Add(metrics, FieldLabel("Nd："), 0);
        Add(metrics, _nd, 1);
        Add(metrics, FieldLabel("Vd："), 2);
        Add(metrics, _vd, 3);
        Grid.SetRow(metrics, 6);
        panel.Children.Add(metrics);
        return panel;
    }

    private Control BuildDetails()
    {
        var coefficients = new StackPanel { Spacing = 8 };
        coefficients.Children.Add(SectionTitle("色散系数"));
        for (var index = 0; index < _coefficientValues.Length; index++)
        {
            coefficients.Children.Add(LabeledControl(_coefficientLabels[index], _coefficientValues[index]));
        }

        var properties = new StackPanel { Spacing = 8 };
        properties.Children.Add(SectionTitle("材料参数"));
        foreach (var name in new[] { "D0", "D1", "D2", "E0", "E1", "Ltk", "TCE", "Temp", "ρ", "dPgF", "最小波长", "最大波长" })
        {
            var field = ReadOnlyField();
            _properties[name] = field;
            properties.Children.Add(LabeledControl($"{name}：", field));
        }

        var detailGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,24,*"),
            Margin = new Thickness(6, 0, 6, 0)
        };
        Add(detailGrid, coefficients, 0);
        Add(detailGrid, properties, 2);

        var details = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(16, 14),
            BoxShadow = BoxShadows.Parse("0 3 12 0 #12000000"),
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = detailGrid
            }
        };
        details.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        details.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        return details;
    }

    private Control BuildCommandBar()
    {
        var reload = CommandButton("refresh-cw", "重载玻璃库");
        var copyName = CommandButton("copy", "复制玻璃");
        var copyReport = CommandButton("file-text", "复制玻璃报告");
        var calculate = CommandButton("calculator", "计算 Nd/Vd");
        reload.Click += (_, _) => ReloadCatalogs();
        copyName.Click += async (_, _) => await CopySelectedNameAsync();
        copyReport.Click += async (_, _) => await CopySelectedReportAsync();
        calculate.Click += (_, _) =>
        {
            ShowSelectedGlass();
            _selectionSummary.Text = "Nd/Vd 已按 Fraunhofer F-d-C 谱线计算";
        };

        var commandBar = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(18, 10),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { reload, copyName, copyReport, calculate }
            }
        };
        commandBar.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        commandBar.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        return commandBar;
    }

    private void ReloadCatalogs()
    {
        var selectedManufacturer = SelectedManufacturer();
        _catalogs = _materials.GetCatalogs();
        _glasses = _materials.GetGlasses();
        _catalog.ItemsSource = _catalogs.Select(catalog => catalog.Manufacturer).ToArray();
        var selectedIndex = _catalogs
            .Select((catalog, index) => (catalog, index))
            .FirstOrDefault(item => item.catalog.Manufacturer.Equals(
                selectedManufacturer,
                StringComparison.OrdinalIgnoreCase)).index;
        _catalog.SelectedIndex = _catalogs.Count == 0 ? -1 : Math.Clamp(selectedIndex, 0, _catalogs.Count - 1);
        RefreshGlassList(selectFirst: true);
    }

    private void RefreshGlassList(bool selectFirst)
    {
        var manufacturer = SelectedManufacturer();
        var query = _search.Text?.Trim() ?? string.Empty;
        var selectedName = SelectedGlass()?.Name;
        _visibleGlasses = _glasses
            .Where(glass => string.IsNullOrEmpty(manufacturer) ||
                glass.Manufacturer.Equals(manufacturer, StringComparison.OrdinalIgnoreCase))
            .Where(glass => string.IsNullOrEmpty(query) ||
                glass.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        _glassList.ItemsSource = _visibleGlasses.Select(glass => glass.Name).ToArray();

        _selectionSummary.Text = $"{_visibleGlasses.Count} 种";

        var selectedIndex = string.IsNullOrEmpty(selectedName)
            ? -1
            : Array.FindIndex(_visibleGlasses.ToArray(), glass =>
                glass.Name.Equals(selectedName, StringComparison.OrdinalIgnoreCase));
        _glassList.SelectedIndex = selectedIndex >= 0
            ? selectedIndex
            : selectFirst && _visibleGlasses.Count > 0 ? 0 : -1;
        ShowSelectedGlass();
    }

    private void ShowSelectedGlass()
    {
        var glass = SelectedGlass();
        if (glass is null)
        {
            ClearDetails();
            return;
        }

        _name.Text = glass.Name;
        _formula.Text = DisplayFormula(glass.Formula);
        _state.Text = glass.Status;
        _nd.Text = FormatNumber(glass.RefractiveIndexD, "0.000000");
        _vd.Text = FormatNumber(glass.AbbeNumber, "0.000");

        var coefficients = DisplayCoefficients(glass);
        for (var index = 0; index < _coefficientValues.Length; index++)
        {
            if (index < coefficients.Count)
            {
                _coefficientLabels[index].Text = $"{coefficients[index].Label}：";
                _coefficientValues[index].Text = NumericDisplayFormatter.Format(coefficients[index].Value, CultureInfo.InvariantCulture);
            }
            else
            {
                _coefficientLabels[index].Text = $"A{index}：";
                _coefficientValues[index].Text = "—";
            }
        }

        SetProperty("D0", CoefficientValue(glass.ThermalCoefficients, 0));
        SetProperty("D1", CoefficientValue(glass.ThermalCoefficients, 1));
        SetProperty("D2", CoefficientValue(glass.ThermalCoefficients, 2));
        SetProperty("E0", CoefficientValue(glass.ThermalCoefficients, 3));
        SetProperty("E1", CoefficientValue(glass.ThermalCoefficients, 4));
        SetProperty("Ltk", CoefficientValue(glass.ThermalCoefficients, 5));
        SetProperty("TCE", OptionalValue(glass.ThermalExpansionLow));
        SetProperty("Temp", CoefficientValue(glass.ThermalCoefficients, 6, "0.###"));
        SetProperty("ρ", OptionalValue(glass.Density, "0.#####"));
        SetProperty("dPgF", OptionalValue(glass.RelativePartialDispersionDeviation, "0.000000"));
        SetProperty("最小波长", $"{NumericDisplayFormatter.Format(glass.MinimumWavelengthMicrometers)} μm");
        SetProperty("最大波长", $"{NumericDisplayFormatter.Format(glass.MaximumWavelengthMicrometers)} μm");
        _selectionSummary.Text = glass.ExtinctionSampleCount > 0
            ? $"{glass.ExtinctionSampleCount} 个消光样本"
            : glass.RefractiveIndexSampleCount > 0
                ? $"{glass.RefractiveIndexSampleCount} 个折射率样本"
                : "公式型玻璃";
    }

    private void ClearDetails()
    {
        _name.Text = string.Empty;
        _formula.Text = string.Empty;
        _state.Text = string.Empty;
        _nd.Text = "—";
        _vd.Text = "—";
        for (var index = 0; index < _coefficientValues.Length; index++)
        {
            _coefficientLabels[index].Text = $"A{index}：";
            _coefficientValues[index].Text = "—";
        }

        foreach (var field in _properties.Values)
        {
            field.Text = "—";
        }
    }

    private GlassMaterialDto? SelectedGlass()
    {
        var index = _glassList.SelectedIndex;
        return index >= 0 && index < _visibleGlasses.Count ? _visibleGlasses[index] : null;
    }

    private string SelectedManufacturer()
    {
        var index = _catalog.SelectedIndex;
        return index >= 0 && index < _catalogs.Count ? _catalogs[index].Manufacturer : string.Empty;
    }

    private async Task CopySelectedNameAsync()
    {
        var glass = SelectedGlass();
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (glass is not null && clipboard is not null)
        {
            await clipboard.SetTextAsync(glass.Name);
            _selectionSummary.Text = "玻璃名称已复制";
        }
    }

    private async Task CopySelectedReportAsync()
    {
        var glass = SelectedGlass();
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (glass is null || clipboard is null)
        {
            return;
        }

        var report = new StringBuilder()
            .AppendLine($"材料库：{glass.Manufacturer}")
            .AppendLine($"玻璃：{glass.Name}")
            .AppendLine($"公式：{DisplayFormula(glass.Formula)}")
            .AppendLine($"Nd：{FormatNumber(glass.RefractiveIndexD, "0.000000")}")
            .AppendLine($"Vd：{FormatNumber(glass.AbbeNumber, "0.000")}")
            .AppendLine($"有效波长：{NumericDisplayFormatter.Format(glass.MinimumWavelengthMicrometers)}–{NumericDisplayFormatter.Format(glass.MaximumWavelengthMicrometers)} μm");
        foreach (var coefficient in DisplayCoefficients(glass))
        {
            report.AppendLine($"{coefficient.Label}：{NumericDisplayFormatter.Format(coefficient.Value, CultureInfo.InvariantCulture)}");
        }

        await clipboard.SetTextAsync(report.ToString());
        _selectionSummary.Text = "玻璃报告已复制";
    }

    private static IReadOnlyList<(string Label, double Value)> DisplayCoefficients(GlassMaterialDto glass)
    {
        var source = glass.DispersionCoefficients;
        if (glass.ZemaxFormulaNumber is not null)
        {
            return source.Take(10).Select((value, index) => ($"A{index}", value)).ToArray();
        }

        if (glass.Formula is "formula 3" or "formula 5")
        {
            var indices = new[] { 0, 1, 3, 5, 7, 9 };
            return indices
                .Where(index => index < source.Count)
                .Select((index, labelIndex) => ($"A{labelIndex}", source[index]))
                .ToArray();
        }

        if (glass.Formula is "formula 1" or "formula 2")
        {
            var labels = new[] { "B1", "C1", "B2", "C2", "B3", "C3" };
            return source
                .Skip(source.Count % 2)
                .Take(labels.Length)
                .Select((value, index) => (labels[index], value))
                .ToArray();
        }

        return source.Take(6).Select((value, index) => ($"C{index}", value)).ToArray();
    }

    private static string DisplayFormula(string formula) => formula switch
    {
        "formula 1" => "Sellmeier 1",
        "formula 2" => "Sellmeier 2",
        "formula 3" => "Schott",
        "formula 5" => "Cauchy",
        "tabulated n" => "折射率表",
        "tabulated nk" => "复折射率表",
        _ when formula.StartsWith("zemax formula ", StringComparison.Ordinal) &&
            int.TryParse(formula.AsSpan("zemax formula ".Length), out var number) => ZemaxFormulaName(number),
        _ => formula
    };

    private static string ZemaxFormulaName(int number) => number switch
    {
        1 => "Schott",
        2 => "Sellmeier 1",
        3 => "Herzberger",
        4 => "Sellmeier 2",
        5 => "Conrady",
        6 => "Sellmeier 3",
        7 => "Handbook of Optics 1",
        8 => "Handbook of Optics 2",
        9 => "Sellmeier 4",
        10 => "Extended",
        11 => "Sellmeier 5",
        12 => "Extended 2",
        13 => "Extended 3",
        _ => $"Zemax {number}"
    };

    private static string CoefficientValue(IReadOnlyList<double> values, int index, string format = "0.0000000E+000") =>
        index < values.Count ? values[index].ToString(format, CultureInfo.InvariantCulture) : "—";

    private static string OptionalValue(double? value, string format = "0.0000000E+000") =>
        value is { } actual ? actual.ToString(format, CultureInfo.InvariantCulture) : "—";

    private static string FormatNumber(double value, string format) => double.IsFinite(value)
        ? value.ToString(format, CultureInfo.InvariantCulture)
        : "—";

    private void SetProperty(string name, string value) => _properties[name].Text = value;

    private static TextBox ReadOnlyField()
    {
        var field = new TextBox
        {
            IsReadOnly = true,
            MinHeight = 34,
            Padding = new Thickness(8, 4),
            BorderThickness = new Thickness(1)
        };
        field.BindThemeResource(TextBox.BackgroundProperty, ThemeResourceBindings.SubtleSurface);
        field.BindThemeResource(TextBox.BorderBrushProperty, ThemeResourceBindings.Border);
        return field;
    }

    private static TextBlock FieldLabel(string text) => new()
    {
        Text = text,
        MinWidth = 66,
        Margin = new Thickness(0, 0, 8, 0),
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Right
    };

    private static TextBlock MetricValue() => new()
    {
        MinWidth = 90,
        FontSize = 16,
        FontWeight = FontWeight.SemiBold,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        FontSize = 15,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 0, 0, 4)
    };

    private static Control LabeledControl(string label, Control control) => LabeledControl(FieldLabel(label), control);

    private static Control LabeledControl(TextBlock label, Control control)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Add(row, label, 0);
        Add(row, control, 1);
        return row;
    }

    private static Button CommandButton(string iconName, string text) => new()
    {
        Content = new LocalIconLabel(iconName, text),
        MinWidth = 132,
        MinHeight = 36,
        Padding = new Thickness(12, 6)
    };

    private static void Add(Grid grid, Control control, int column)
    {
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    private static void AddRow(Grid grid, Control control, int row)
    {
        Grid.SetRow(control, row);
        grid.Children.Add(control);
    }

    internal static DataGrid DatabaseGrid()
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserReorderColumns = true,
            CanUserResizeColumns = true,
            IsReadOnly = true,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            BorderThickness = new Thickness(1)
        };
        grid.BindThemeResource(DataGrid.RowBackgroundProperty, ThemeResourceBindings.Surface);
        grid.BindThemeResource(DataGrid.BorderBrushProperty, ThemeResourceBindings.Border);
        return grid;
    }

    internal static Control DatabasePage(string title, string summary, Control content)
    {
        var summaryText = new TextBlock { Text = summary };
        summaryText.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        var header = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(16, 12),
            Child = new StackPanel
            {
                Spacing = 3,
                Children =
                {
                    new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeight.SemiBold },
                    summaryText
                }
            }
        };
        header.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        header.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        var root = new DockPanel();
        root.BindThemeResource(Panel.BackgroundProperty, ThemeResourceBindings.Workspace);
        DockPanel.SetDock(header, Avalonia.Controls.Dock.Top);
        root.Children.Add(header);
        root.Children.Add(new Border
        {
            Margin = new Thickness(16),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            BoxShadow = BoxShadows.Parse("0 3 12 0 #16000000"),
            Child = content
        });
        return root;
    }
}

internal sealed class LensLibraryPanel : UserControl
{
    private readonly ILensLibraryService _lenses;
    private readonly Func<string, Task>? _openLensProject;
    private readonly ComboBox _category = new() { MinWidth = 150, MinHeight = 34 };
    private readonly TextBox _search = new()
    {
        MinWidth = 220,
        MinHeight = 34,
        PlaceholderText = "搜索镜头、来源"
    };
    private readonly ListBox _list = new() { MinHeight = 300 };
    private readonly OpticSceneControl _preview = new()
    {
        ViewMode = OpticSceneViewMode.TwoDimensional,
        ShowRays = true,
        ShowScaleBar = true,
        MinHeight = 320
    };
    private readonly TextBlock _count = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap
    };
    private readonly TextBlock _name = DetailValue(18, FontWeight.SemiBold);
    private readonly TextBlock _source = DetailValue();
    private readonly TextBlock _license = DetailValue();
    private readonly TextBlock _format = DetailValue();
    private readonly TextBlock _efl = DetailValue();
    private readonly TextBlock _fNumber = DetailValue();
    private readonly TextBlock _aperture = DetailValue();
    private readonly TextBlock _track = DetailValue();
    private readonly TextBlock _surfaces = DetailValue();
    private readonly TextBlock _fields = DetailValue();
    private readonly TextBlock _wavelengths = DetailValue();
    private IReadOnlyList<LensLibraryEntryDto> _all = Array.Empty<LensLibraryEntryDto>();
    private IReadOnlyList<LensLibraryEntryDto> _visible = Array.Empty<LensLibraryEntryDto>();
    private int _previewGeneration;
    private bool _opening;

    public LensLibraryPanel(
        ILensLibraryService lenses,
        Func<string, Task>? openLensProject = null)
    {
        _lenses = lenses;
        _openLensProject = openLensProject;
        _category.ItemsSource = new[] { "全部镜头", "显微物镜", "工业镜头" };
        _category.SelectedIndex = 0;
        _category.SelectionChanged += (_, _) => RefreshList(selectFirst: true);
        _search.TextChanged += (_, _) => RefreshList(selectFirst: true);
        _list.SelectionChanged += async (_, _) => await ShowSelectedLensAsync();
        _list.DoubleTapped += async (_, _) => await OpenSelectedLensAsync();
        _status.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);

        Content = BuildPage();
        ReloadIndex();
    }

    private Control BuildPage()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        root.BindThemeResource(Panel.BackgroundProperty, ThemeResourceBindings.Workspace);

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,12,Auto,12,Auto,*,Auto"),
            Margin = new Thickness(18, 14, 18, 10)
        };
        Add(header, new LocalIcon
        {
            IconName = "search",
            Width = 18,
            Height = 18,
            VerticalAlignment = VerticalAlignment.Center
        }, 0);
        Add(header, _search, 2);
        Add(header, _category, 4);
        Add(header, _count, 6);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("330,16,*"),
            Margin = new Thickness(18, 0, 18, 14),
            MinWidth = 980
        };
        var listFrame = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Child = _list
        };
        listFrame.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        listFrame.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        Add(body, listFrame, 0);

        var details = new Grid
        {
            RowDefinitions = new RowDefinitions("3*,Auto,Auto"),
            RowSpacing = 12
        };
        var previewFrame = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            ClipToBounds = true,
            Child = _preview
        };
        previewFrame.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        previewFrame.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        Grid.SetRow(previewFrame, 0);
        details.Children.Add(previewFrame);

        var parameterGrid = BuildParameterGrid();
        Grid.SetRow(parameterGrid, 1);
        details.Children.Add(parameterGrid);

        var statusBar = new Border
        {
            Padding = new Thickness(0, 4),
            Child = _status
        };
        Grid.SetRow(statusBar, 2);
        details.Children.Add(statusBar);
        Add(body, details, 2);

        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = body
        };
        Grid.SetRow(scroller, 1);
        root.Children.Add(scroller);
        return root;
    }

    private Control BuildParameterGrid()
    {
        var panel = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,24,Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto"),
            RowSpacing = 7
        };
        AddPair(panel, "镜头：", _name, 0, 0);
        AddPair(panel, "来源：", _source, 0, 1);
        AddPair(panel, "许可证：", _license, 1, 0);
        AddPair(panel, "格式/状态：", _format, 1, 1);
        AddPair(panel, "有效焦距：", _efl, 2, 0);
        AddPair(panel, "F 数：", _fNumber, 2, 1);
        AddPair(panel, "系统孔径：", _aperture, 3, 0);
        AddPair(panel, "系统总长：", _track, 3, 1);
        AddPair(panel, "表面数：", _surfaces, 4, 0);
        AddPair(panel, "视场数：", _fields, 4, 1);
        AddPair(panel, "波长：", _wavelengths, 5, 0);
        return panel;
    }

    private void ReloadIndex()
    {
        _all = _lenses.GetLenses();
        RefreshList(selectFirst: true);
        _status.Text = _all.Count == 0
            ? "此版本没有随附可用镜头，请重新构建并打包镜头库。"
            : $"随软件加载 {_all.Count} 个本地镜头";
    }

    private void RefreshList(bool selectFirst)
    {
        var selectedId = SelectedLens()?.Id;
        var category = _category.SelectedItem as string;
        var query = _search.Text?.Trim() ?? string.Empty;
        _visible = _all
            .Where(lens => category is null or "全部镜头" ||
                lens.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .Where(lens => string.IsNullOrEmpty(query) ||
                lens.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                lens.SourceName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        _list.ItemsSource = _visible.Select(lens =>
            $"{lens.Name}\n{lens.Category} · {lens.SourceName}").ToArray();
        _count.Text = $"{_visible.Count} 个镜头";

        var selectedIndex = string.IsNullOrEmpty(selectedId)
            ? -1
            : Array.FindIndex(_visible.ToArray(), lens => lens.Id == selectedId);
        _list.SelectedIndex = selectedIndex >= 0
            ? selectedIndex
            : selectFirst && _visible.Count > 0 ? 0 : -1;
        if (_list.SelectedIndex < 0)
        {
            ClearDetails();
        }
    }

    private async Task ShowSelectedLensAsync()
    {
        var lens = SelectedLens();
        if (lens is null)
        {
            ClearDetails();
            return;
        }

        _name.Text = lens.Name;
        _source.Text = lens.SourceName;
        _license.Text = lens.License;
        _format.Text = $"{lens.SourceFormat} · {lens.ImportStatus}";
        _efl.Text = Millimeters(lens.EffectiveFocalLength);
        _fNumber.Text = Number(lens.FNumber);
        _aperture.Text = ApertureDescription(lens);
        _track.Text = Millimeters(lens.TotalTrack);
        _surfaces.Text = lens.SurfaceCount.ToString(CultureInfo.InvariantCulture);
        _fields.Text = FieldDescription(lens);
        _wavelengths.Text = lens.WavelengthCount == 0
            ? "—"
            : lens.MinimumWavelengthNanometers == lens.MaximumWavelengthNanometers
                ? $"{Number(lens.MinimumWavelengthNanometers)} nm"
                : $"{Number(lens.MinimumWavelengthNanometers)}–{Number(lens.MaximumWavelengthNanometers)} nm";
        _status.Text = string.IsNullOrWhiteSpace(lens.SourceUrl)
            ? $"库文件：{lens.NativePath}"
            : $"来源：{lens.SourceUrl}";

        var generation = ++_previewGeneration;
        try
        {
            var scene = await _lenses.BuildPreviewAsync(lens.Id);
            if (generation != _previewGeneration)
            {
                return;
            }

            _preview.Scene = scene;
            _preview.InvalidateVisual();
        }
        catch (Exception exception)
        {
            if (generation == _previewGeneration)
            {
                _preview.Scene = null;
                _preview.InvalidateVisual();
                _status.Text = $"预览失败：{exception.Message}";
            }
        }
    }

    private async Task OpenSelectedLensAsync()
    {
        var lens = SelectedLens();
        if (lens is null || _openLensProject is null || _opening)
        {
            return;
        }

        var path = _lenses.GetNativeProjectPath(lens.Id);
        if (path is null)
        {
            _status.Text = "镜头项目文件不存在或不可用。";
            return;
        }

        _opening = true;
        _status.Text = $"正在打开：{lens.Name}";
        try
        {
            await _openLensProject(path);
            _status.Text = $"已打开：{lens.Name}";
        }
        catch (Exception exception)
        {
            _status.Text = $"打开失败：{exception.Message}";
        }
        finally
        {
            _opening = false;
        }
    }

    private LensLibraryEntryDto? SelectedLens()
    {
        var index = _list.SelectedIndex;
        return index >= 0 && index < _visible.Count ? _visible[index] : null;
    }

    private void ClearDetails()
    {
        _preview.Scene = null;
        _preview.InvalidateVisual();
        foreach (var value in new[]
        {
            _name, _source, _license, _format, _efl, _fNumber,
            _aperture, _track, _surfaces, _fields, _wavelengths
        })
        {
            value.Text = "—";
        }
    }

    private static void AddPair(Grid grid, string label, Control value, int row, int group)
    {
        var labelColumn = group == 0 ? 0 : 3;
        var valueColumn = group == 0 ? 1 : 4;
        var labelControl = new TextBlock
        {
            Text = label,
            MinWidth = 78,
            Margin = new Thickness(0, 0, 8, 0),
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(labelControl, row);
        Grid.SetColumn(labelControl, labelColumn);
        grid.Children.Add(labelControl);
        Grid.SetRow(value, row);
        Grid.SetColumn(value, valueColumn);
        grid.Children.Add(value);
    }

    private static TextBlock DetailValue(
        double fontSize = 14,
        FontWeight? fontWeight = null) => new()
    {
        FontSize = fontSize,
        FontWeight = fontWeight ?? FontWeight.Normal,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap
    };

    private static Button CommandButton(string iconName, string text) => new()
    {
        Content = new LocalIconLabel(iconName, text),
        MinHeight = 36,
        Padding = new Thickness(12, 6)
    };

    private static string Number(double value) =>
        double.IsFinite(value) && value != 0
            ? NumericDisplayFormatter.Format(value, CultureInfo.InvariantCulture)
            : "—";

    private static string Millimeters(double value)
    {
        var number = Number(value);
        return number == "—" ? number : $"{number} mm";
    }

    private static string ApertureDescription(LensLibraryEntryDto lens)
    {
        var value = Number(lens.ApertureValue);
        if (value == "—")
        {
            return value;
        }

        return lens.ApertureKind switch
        {
            "EntrancePupilDiameter" => $"EPD {value} mm",
            "FNumber" => $"像方 F/{value}",
            "NumericalAperture" => $"物方 NA {value}",
            "FloatByStopSize" => $"浮动光阑 {value}",
            _ => value
        };
    }

    private static string FieldDescription(LensLibraryEntryDto lens)
    {
        var definition = lens.FieldDefinition switch
        {
            "Angle" => "角度",
            "ObjectHeight" => "物高",
            "ParaxialImageHeight" => "近轴像高",
            "RealImageHeight" => "实际像高",
            _ => lens.FieldDefinition
        };
        var maximum = Number(lens.MaximumField);
        var unit = lens.FieldDefinition == "Angle" ? "°" : " mm";
        return maximum == "—"
            ? $"{lens.FieldCount} 个"
            : $"{lens.FieldCount} 个 · {definition} {maximum}{unit}";
    }

    private static void Add(Grid grid, Control control, int column)
    {
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }
}

public sealed class GlassCatalogPanel : UserControl
{
    private readonly IReadOnlyList<GlassMaterialDto> _glasses;
    private readonly DataGrid _grid = MaterialLibraryPanel.DatabaseGrid();
    private readonly TextBox _search = new() { Width = 230, PlaceholderText = "搜索玻璃名称" };
    private readonly ComboBox _manufacturer = new() { Width = 160 };
    private readonly TextBlock _count = new() { VerticalAlignment = VerticalAlignment.Center };

    public GlassCatalogPanel(IMaterialCatalogService materials)
    {
        _glasses = materials.GetGlasses();
        ConfigureGrid();
        _manufacturer.ItemsSource = new[] { "所有材料库" }
            .Concat(_glasses.Select(glass => glass.Manufacturer)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        _manufacturer.SelectedIndex = 0;
        _search.TextChanged += (_, _) => Refresh();
        _manufacturer.SelectionChanged += (_, _) => Refresh();

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 10),
            Children =
            {
                new LocalIcon { IconName = "search", Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center },
                _search,
                _manufacturer,
                _count
            }
        };
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        Grid.SetRow(toolbar, 0);
        Grid.SetRow(_grid, 1);
        content.Children.Add(toolbar);
        content.Children.Add(_grid);
        var contentFrame = new Border { Padding = new Thickness(12), Child = content };
        contentFrame.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        Content = MaterialLibraryPanel.DatabasePage(
            "玻璃",
            "查看内置玻璃目录的折射率、阿贝数和有效波长范围",
            contentFrame);
        Refresh();
    }

    private void ConfigureGrid()
    {
        _grid.Columns.Add(Column("玻璃", nameof(GlassMaterialDto.Name), 150));
        _grid.Columns.Add(Column("材料库", nameof(GlassMaterialDto.Manufacturer), 120));
        _grid.Columns.Add(Column("色散公式", nameof(GlassMaterialDto.Formula), 120));
        _grid.Columns.Add(Column("nd", nameof(GlassMaterialDto.RefractiveIndexD), 110, "0.000000"));
        _grid.Columns.Add(Column("Vd", nameof(GlassMaterialDto.AbbeNumber), 90, "0.00"));
        _grid.Columns.Add(Column("最短波长 (μm)", nameof(GlassMaterialDto.MinimumWavelengthMicrometers), 130, "0.000"));
        _grid.Columns.Add(Column("最长波长 (μm)", nameof(GlassMaterialDto.MaximumWavelengthMicrometers), 130, "0.000"));
    }

    private void Refresh()
    {
        var query = _search.Text?.Trim() ?? string.Empty;
        var manufacturer = _manufacturer.SelectedItem as string;
        var filtered = _glasses
            .Where(glass => string.IsNullOrEmpty(query) ||
                glass.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Where(glass => string.IsNullOrEmpty(manufacturer) ||
                manufacturer == "所有材料库" ||
                glass.Manufacturer.Equals(manufacturer, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        _grid.ItemsSource = filtered;
        _count.Text = $"{filtered.Length} 种";
    }

    private static DataGridTextColumn Column(string header, string property, double width, string? format = null) => new()
    {
        Header = header,
        Binding = new Binding(property) { StringFormat = format },
        Width = new DataGridLength(width)
    };
}
