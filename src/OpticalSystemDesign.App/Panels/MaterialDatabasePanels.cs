using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Formatting;

namespace OpticalSystemDesign.App.Panels;

public sealed class MaterialLibraryPanel : UserControl
{
    private readonly IMaterialCatalogService _materials;
    private IReadOnlyList<MaterialCatalogDto> _catalogs = Array.Empty<MaterialCatalogDto>();
    private IReadOnlyList<GlassMaterialDto> _glasses = Array.Empty<GlassMaterialDto>();
    private IReadOnlyList<GlassMaterialDto> _visibleGlasses = Array.Empty<GlassMaterialDto>();
    private readonly ComboBox _catalog = new() { Name = "MaterialCatalogFamily", MinHeight = 34 };
    private readonly TextBox _search = new() { MinHeight = 34, PlaceholderText = "输入玻璃名称" };
    private readonly ListBox _glassList = new() { Name = "MaterialGlassList" };
    private readonly TextBox _name = ReadOnlyField();
    private readonly TextBox _formula = ReadOnlyField();
    private readonly TextBox _state = ReadOnlyField();
    private readonly TextBlock _nd = MetricValue();
    private readonly TextBlock _vd = MetricValue();
    private readonly TextBlock _constantsSource = new() { Name = "MaterialConstantsSource", TextWrapping = TextWrapping.Wrap };
    private Button _calculateConstants = null!;
    private GlassConstantsDto? _calculatedConstants;
    private readonly TextBlock _selectionSummary = new()
    {
        VerticalAlignment = VerticalAlignment.Center
    };
    private TextBlock[] _coefficientLabels = new TextBlock[10];
    private TextBox[] _coefficientValues = new TextBox[10];
    private Control[] _coefficientRows = new Control[10];
    private readonly Grid _coefficientGrid = new();
    private readonly TextBlock _coefficientNote = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _dataQuality = new() { Name = "MaterialDataQuality", TextWrapping = TextWrapping.Wrap };
    private readonly Dictionary<string, TextBox> _properties = new(StringComparer.Ordinal);
    private static readonly string[] CatalogPropertyNames = ["熔融频率", "标注", "参考成本", "CR", "FR", "SR", "AR", "PR"];

    public MaterialLibraryPanel(IMaterialCatalogService materials)
    {
        _materials = materials;
        UiLocalization.Preserve(_catalog);
        UiLocalization.Preserve(_glassList);
        _selectionSummary.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        _constantsSource.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        _coefficientNote.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        _dataQuality.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        UiLocalization.BindLanguage(this, () => _state.Text = UiLocalization.Text(SelectedGlass()?.Status ?? ""));
        _nd.Name = "MaterialNd";
        _vd.Name = "MaterialVd";
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
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(14, 10),
            Child = new TextBlock
            {
                Text = "材料库",
                FontSize = DisplayTypography.WindowTitle,
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
            ColumnDefinitions = new ColumnDefinitions("Auto,*")
        };
        Add(catalogRow, FieldLabel("分类："), 0);
        Add(catalogRow, _catalog, 1);
        Grid.SetRow(catalogRow, 1);
        root.Children.Add(catalogRow);

        var body = new ResponsiveTwoPaneGrid(
            BuildGlassSelector(),
            BuildDetails(),
            "5*,18,5.5*",
            "2*,18,3*",
            breakpoint: 900)
        {
            Margin = new Thickness(18, 0, 18, 12)
        };
        Grid.SetRow(body, 2);
        root.Children.Add(body);

        var commandBar = BuildCommandBar();
        Grid.SetRow(commandBar, 3);
        root.Children.Add(commandBar);
        return root;
    }

    private Control BuildGlassSelector()
    {
        var panel = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto,Auto,Auto,Auto"),
            RowSpacing = 8
        };

        var glassHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Add(glassHeader, FieldLabel("玻璃："), 0);
        Add(glassHeader, _selectionSummary, 2);
        Grid.SetRow(glassHeader, 0);
        panel.Children.Add(glassHeader);

        var listFrame = new Border
        {
            Child = _glassList
        };
        SettingsPanelChrome.ApplyControlFrameStyle(listFrame);
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
        AddRow(panel, _constantsSource, 7);
        return panel;
    }

    private Control BuildDetails()
    {
        var coefficients = new StackPanel { Spacing = 8 };
        coefficients.Children.Add(SectionTitle("色散系数"));
        var coefficientFields = _coefficientGrid;
        coefficientFields.ColumnDefinitions = new ColumnDefinitions("*,*");
        coefficientFields.RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto");
        coefficientFields.ColumnSpacing = 16;
        coefficientFields.RowSpacing = 8;
        for (var index = 0; index < _coefficientValues.Length; index++)
        {
            _coefficientLabels[index].MinWidth = 32;
            var field = LabeledControl(_coefficientLabels[index], _coefficientValues[index]);
            _coefficientRows[index] = field;
            Grid.SetRow(field, index / 2);
            Grid.SetColumn(field, index % 2);
            coefficientFields.Children.Add(field);
        }
        coefficients.Children.Add(coefficientFields);
        coefficients.Children.Add(_coefficientNote);
        coefficients.Children.Add(SectionTitle("目录资料"));
        coefficients.Children.Add(_dataQuality);
        var catalogFields = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 16,
            RowSpacing = 8
        };
        // Keep the original catalog comment selectable and wrapped, without
        // translating or interpreting the manufacturer's text.
        var positions = new[] { (0, 0), (1, 0), (0, 1), (2, 0), (2, 1), (3, 0), (3, 1), (4, 0) };
        for (var index = 0; index < CatalogPropertyNames.Length; index++)
        {
            var name = CatalogPropertyNames[index];
            var field = ReadOnlyField();
            _properties[name] = field;
            if (name == "标注")
            {
                field.TextWrapping = TextWrapping.Wrap;
                field.MaxHeight = 104;
            }
            var row = LabeledControl($"{name}：", field);
            Grid.SetRow(row, positions[index].Item1);
            Grid.SetColumn(row, positions[index].Item2);
            if (name == "标注") Grid.SetColumnSpan(row, 2);
            catalogFields.Children.Add(row);
        }
        ToolTip.SetTip(_properties["参考成本"], "相对于 BK7 的目录参考成本，不是实时报价。");
        foreach (var name in new[] { "参考成本", "CR", "FR", "SR", "AR", "PR" })
            AutomationProperties.SetHelpText(_properties[name], UiLocalization.Text("问号表示原目录未提供该参数。"));
        coefficients.Children.Add(catalogFields);

        var properties = new StackPanel { Spacing = 8 };
        properties.Children.Add(SectionTitle("材料参数"));
        foreach (var name in new[] { "D0", "D1", "D2", "E0", "E1", "Ltk", "TCE", "Temp", "ρ", "dPgF", "最小波长", "最大波长" })
        {
            var field = ReadOnlyField();
            _properties[name] = field;
            properties.Children.Add(LabeledControl($"{name}：", field));
        }

        var detailGrid = new ResponsiveTwoPaneGrid(
            coefficients,
            properties,
            "*,24,*",
            "Auto,12,Auto",
            breakpoint: 560)
        {
            Margin = new Thickness(6, 0, 6, 0)
        };

        var details = new Border
        {
            Padding = new Thickness(16, 14),
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = detailGrid
            }
        };
        SettingsPanelChrome.ApplySurfaceCardStyle(details);
        return details;
    }

    private Control BuildCommandBar()
    {
        var reload = CommandButton("action-recalculate", "重载玻璃库");
        var copyName = CommandButton("material-copy-glass", "复制玻璃");
        var copyReport = CommandButton("material-glass-report", "复制玻璃报告");
        var calculate = _calculateConstants = CommandButton("material-nd-vd", "计算 Nd/Vd");
        calculate.Name = "CalculateGlassConstants";
        reload.Click += (_, _) => ReloadCatalogs();
        copyName.Click += async (_, _) => await CopySelectedNameAsync();
        copyReport.Click += async (_, _) => await CopySelectedReportAsync();
        calculate.Click += (_, _) => CalculateSelectedConstants();

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
        _calculatedConstants = null;
        var glass = SelectedGlass();
        if (glass is null)
        {
            ClearDetails();
            return;
        }

        _name.Text = glass.Name;
        _formula.Text = DisplayFormula(glass.Formula);
        _state.Text = UiLocalization.Text(glass.Status);
        _nd.Text = FormatNumber(glass.RefractiveIndexD, "0.000000");
        _vd.Text = FormatNumber(glass.AbbeNumber, "0.000");
        _calculateConstants.IsEnabled = true;
        ToolTip.SetTip(_calculateConstants, "按材料色散模型在 F、d、C 谱线重算，不覆盖目录参考值。");
        _constantsSource.Text = BrowsedConstantsSource(glass);
        _dataQuality.Text = DataQualityText(glass);
        _dataQuality.IsVisible = _dataQuality.Text.Length > 0;
        foreach (var (name, value) in CatalogProperties(glass)) SetProperty(name, value);

        var coefficients = DisplayCoefficients(glass);
        EnsureCoefficientCapacity(coefficients.Count);
        for (var index = 0; index < _coefficientValues.Length; index++)
        {
            _coefficientRows[index].IsVisible = index < coefficients.Count;
            if (index < coefficients.Count)
            {
                _coefficientLabels[index].Text = $"{coefficients[index].Label}：";
                _coefficientValues[index].Text = NumericDisplayFormatter.Format(coefficients[index].Value, CultureInfo.InvariantCulture);
            }
            else
            {
                _coefficientLabels[index].Text = "—";
                _coefficientValues[index].Text = "—";
            }
        }
        _coefficientNote.Text = coefficients.Count == 0 ? "当前材料没有解析色散系数。"
            : coefficients.Any(item => item.Label.EndsWith('*')) ? "标记 * 的项为源记录额外系数，未参与当前公式计算。" : "";
        _coefficientNote.IsVisible = _coefficientNote.Text.Length > 0;

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
        SetProperty("最小波长", $"{NumericDisplayFormatter.Format(glass.MinimumWavelengthMicrometers)} µm");
        SetProperty("最大波长", $"{NumericDisplayFormatter.Format(glass.MaximumWavelengthMicrometers)} µm");
        _selectionSummary.Text = glass.ExtinctionSampleCount > 0
            ? $"{glass.ExtinctionSampleCount} 个消光样本"
            : glass.RefractiveIndexSampleCount > 0
                ? $"{glass.RefractiveIndexSampleCount} 个折射率样本"
                : "公式型玻璃";
    }

    private void ClearDetails()
    {
        _calculatedConstants = null;
        _calculateConstants.IsEnabled = false;
        ToolTip.SetTip(_calculateConstants, "请先选择材料。");
        _constantsSource.Text = "";
        _coefficientNote.IsVisible = false;
        _dataQuality.Text = "";
        _dataQuality.IsVisible = false;
        _name.Text = string.Empty;
        _formula.Text = string.Empty;
        _state.Text = string.Empty;
        _nd.Text = "—";
        _vd.Text = "—";
        for (var index = 0; index < _coefficientValues.Length; index++)
        {
            _coefficientRows[index].IsVisible = false;
            _coefficientLabels[index].Text = "—";
            _coefficientValues[index].Text = "—";
        }

        foreach (var field in _properties.Values)
        {
            field.Text = "—";
        }
    }

    private void CalculateSelectedConstants()
    {
        if (SelectedGlass() is not { } glass) return;
        _calculatedConstants = _materials.CalculateGlassConstants($"{glass.Manufacturer}:{glass.Name}", useCatalogDefinition: true)
            ?? new GlassConstantsDto(null, null, null, null, GlassConstantsCalculationStatus.UnsupportedModel);
        _nd.Text = FormatNumber(_calculatedConstants.RefractiveIndexD ?? double.NaN, "0.000000");
        _vd.Text = FormatNumber(_calculatedConstants.AbbeNumber ?? double.NaN, "0.000");
        _constantsSource.Text = CalculatedConstantsSource(glass, _calculatedConstants);
        _selectionSummary.Text = _calculatedConstants.Status == GlassConstantsCalculationStatus.Available
            ? "Nd/Vd 已按 Fraunhofer F-d-C 谱线计算" : "Nd/Vd 计算未完成";
    }

    private static string BrowsedConstantsSource(GlassMaterialDto glass) =>
        !double.IsFinite(glass.RefractiveIndexD) || !double.IsFinite(glass.AbbeNumber)
        ? "Nd/Vd：部分数值不可用，请查看数据检查。" :
        (glass.CatalogReferenceIndexD.HasValue, glass.CatalogReferenceAbbeNumber.HasValue) switch
        {
            (true, true) => "Nd/Vd：目录参考值",
            (true, false) => "Nd：目录参考值；Vd：色散模型计算值",
            (false, true) => "Nd：色散模型计算值；Vd：目录参考值",
            _ => "Nd/Vd：色散模型计算值（F/d/C）"
        };

    internal static string DataQualityText(GlassMaterialDto glass)
    {
        var status = glass.CalculatedConstantsStatus switch
        {
            GlassConstantsCalculationStatus.Available => "数据检查：F-d-C 模型计算可用；目录参考值保持原值。",
            GlassConstantsCalculationStatus.WavelengthOutOfRange => "数据检查：F、d、C 谱线超出材料有效波长或折射率表范围；目录参考值不代表该波段的有效计算。",
            GlassConstantsCalculationStatus.UnsupportedModel => "数据检查：当前色散模型不受支持；仅保留已有目录参考值。",
            GlassConstantsCalculationStatus.InvalidIndex => "数据检查：色散数据缺失或计算无效；仅保留已有目录参考值。",
            GlassConstantsCalculationStatus.UndefinedAbbeNumber => "数据检查：F、C 折射率差值过小，Vd 无定义。",
            _ => ""
        };
        if (glass.RequiresThermalDataReview)
            status += (status.Length > 0 ? Environment.NewLine : "")
                + $"热数据需核对：目录参考温度、d 谱线下 dn/dT={FormatNumber(glass.ReferenceThermalIndexDerivative ?? double.NaN, "0.0000000E+000")}/K；已保留原始 TD 数据，未缩放或修正。";
        return status;
    }

    private static string CalculatedConstantsSource(GlassMaterialDto glass, GlassConstantsDto calculated)
    {
        var source = "Nd/Vd：色散模型计算值（F/d/C）" + Environment.NewLine
            + $"目录参考 Nd：{FormatNumber(glass.CatalogReferenceIndexD ?? double.NaN, "0.000000")}；Vd：{FormatNumber(glass.CatalogReferenceAbbeNumber ?? double.NaN, "0.000")}";
        var reason = calculated.Status switch
        {
            GlassConstantsCalculationStatus.Available => "",
            GlassConstantsCalculationStatus.WavelengthOutOfRange => "无法计算：F、d、C 谱线超出材料有效波长或折射率表范围。",
            GlassConstantsCalculationStatus.UnsupportedModel => "无法计算：当前材料的色散模型不受支持。",
            GlassConstantsCalculationStatus.UndefinedAbbeNumber => "Vd 不可用：F、C 折射率相同或差值过小。",
            _ => "无法计算：材料色散数据缺失或产生无效折射率。"
        };
        return reason.Length == 0 ? source : source + Environment.NewLine + reason;
    }

    private void EnsureCoefficientCapacity(int count)
    {
        var previous = _coefficientValues.Length;
        if (count <= previous) return;
        Array.Resize(ref _coefficientLabels, count);
        Array.Resize(ref _coefficientValues, count);
        Array.Resize(ref _coefficientRows, count);
        while (_coefficientGrid.RowDefinitions.Count < (count + 1) / 2)
            _coefficientGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var index = previous; index < count; index++)
        {
            _coefficientLabels[index] = FieldLabel("");
            _coefficientLabels[index].MinWidth = 32;
            _coefficientValues[index] = ReadOnlyField();
            var row = _coefficientRows[index] = LabeledControl(_coefficientLabels[index], _coefficientValues[index]);
            Grid.SetRow(row, index / 2);
            Grid.SetColumn(row, index % 2);
            _coefficientGrid.Children.Add(row);
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

        await clipboard.SetTextAsync(BuildGlassReport(glass, _calculatedConstants, UiLocalization.CurrentLanguage));
        _selectionSummary.Text = "玻璃报告已复制";
    }

    internal static string BuildGlassReport(GlassMaterialDto glass, GlassConstantsDto? calculated = null, string language = UiLocalization.Chinese)
    {
        string T(string source) => UiLocalization.Text(source, language);
        string Line(string label, string value) => T(label) + (language == UiLocalization.English ? ": " : "：") + value;
        var report = new StringBuilder()
            .AppendLine(Line("材料库", glass.Manufacturer))
            .AppendLine(Line("玻璃", glass.Name))
            .AppendLine(Line("公式", T(DisplayFormula(glass.Formula))))
            .AppendLine(Line("Nd", FormatNumber(calculated is null ? glass.RefractiveIndexD : calculated.RefractiveIndexD ?? double.NaN, "0.000000")))
            .AppendLine(Line("Vd", FormatNumber(calculated is null ? glass.AbbeNumber : calculated.AbbeNumber ?? double.NaN, "0.000")))
            .AppendLine(T(calculated is null ? BrowsedConstantsSource(glass) : CalculatedConstantsSource(glass, calculated)))
            .AppendLine(Line("有效波长", $"{NumericDisplayFormatter.Format(glass.MinimumWavelengthMicrometers)}–{NumericDisplayFormatter.Format(glass.MaximumWavelengthMicrometers)} µm"));
        var quality = DataQualityText(glass);
        if (quality.Length > 0) report.AppendLine(T(quality));
        foreach (var coefficient in DisplayCoefficients(glass))
        {
            report.AppendLine(Line(coefficient.Label, NumericDisplayFormatter.Format(coefficient.Value, CultureInfo.InvariantCulture)));
        }
        if (DisplayCoefficients(glass).Any(item => item.Label.EndsWith('*')))
            report.AppendLine(T("标记 * 的项为源记录额外系数，未参与当前公式计算。"));
        foreach (var (name, value) in CatalogProperties(glass)) report.AppendLine(Line(name, value));
        var thermalSymbols = new[] { "D0", "D1", "D2", "E0", "E1", "Ltk", "Temp" };
        for (var index = 0; index < thermalSymbols.Length; index++)
            report.AppendLine(Line(thermalSymbols[index], CoefficientValue(glass.ThermalCoefficients, index, index == 6 ? "0.###" : "0.0000000E+000")));
        return report.ToString();
    }

    private static IEnumerable<(string Name, string Value)> CatalogProperties(GlassMaterialDto glass)
    {
        yield return ("熔融频率", glass.MeltFrequency.ToString(CultureInfo.InvariantCulture));
        yield return ("标注", string.IsNullOrWhiteSpace(glass.Comment) ? "—" : glass.Comment);
        for (var index = 0; index < 6; index++)
        {
            var value = index < glass.OtherData.Count ? glass.OtherData[index] : double.NaN;
            // AGF OD uses -1 for unavailable cost/resistance data. A real zero
            // remains zero, and manufacturer durability classes stay numerical.
            yield return (CatalogPropertyNames[index + 2], double.IsFinite(value) && value >= 0
                ? NumericDisplayFormatter.Format(value, CultureInfo.InvariantCulture) : "?");
        }
    }

    internal static IReadOnlyList<(string Label, double Value)> DisplayCoefficients(GlassMaterialDto glass)
    {
        var source = glass.DispersionCoefficients;
        if (glass.Formula.StartsWith("zemax formula ", StringComparison.Ordinal)
            && int.TryParse(glass.Formula.AsSpan("zemax formula ".Length), out var model))
        {
            var labels = model switch
            {
                1 => new[] { "A0", "A1", "A2", "A3", "A4", "A5" },
                2 => new[] { "K1", "L1", "K2", "L2", "K3", "L3" },
                3 => new[] { "A", "B", "C", "D", "E", "F" },
                4 => new[] { "A", "B1", "λ1", "B2", "λ2" },
                5 => new[] { "n0", "A", "B" },
                6 => new[] { "K1", "L1", "K2", "L2", "K3", "L3", "K4", "L4" },
                7 or 8 => new[] { "A", "B", "C", "D" },
                9 => new[] { "A", "B", "C", "D", "E" },
                10 or 12 => Enumerable.Range(0, 8).Select(i => $"A{i}").ToArray(),
                11 => new[] { "K1", "L1", "K2", "L2", "K3", "L3", "K4", "L4", "K5", "L5" },
                13 => Enumerable.Range(0, 9).Select(i => $"A{i}").ToArray(),
                _ => source.Select((_, i) => $"CD{i}").ToArray()
            };
            return source.Take(labels.Length).Select((value, index) => (labels[index], value))
                .Concat(source.Select((value, index) => (Label: $"CD{index}*", Value: value))
                    .Skip(labels.Length)).ToArray();
        }

        if (glass.Formula is "formula 3" or "formula 5")
        {
            return source.Select((value, index) => (value, index))
                .Where(item => item.index == 0 || item.index % 2 == 1)
                .Select(item => (item.index == 0 ? "C0" : $"C{(item.index + 1) / 2} (λ^{(item.index + 1 < source.Count ? source[item.index + 1].ToString("G", CultureInfo.InvariantCulture) : "?")})", item.value))
                .ToArray();
        }

        if (glass.Formula is "formula 1" or "formula 2" or "formula 2 pchip-k-clamped/1")
        {
            return source.Select((value, index) => (index == 0 ? "C0" : index % 2 == 1
                    ? $"B{(index + 1) / 2}" : glass.Formula == "formula 1" ? $"λ{index / 2}" : $"C{index / 2}", value))
                .ToArray();
        }

        return source.Select((value, index) => ($"C{index}", value)).ToArray();
    }

    private static string DisplayFormula(string formula) => formula switch
    {
        "formula 1" => "Sellmeier 1",
        "formula 2" => "Sellmeier 2",
        "formula 3" => "多项式 n²",
        "formula 5" => "多项式 n",
        "formula 2 pchip-k-clamped/1" => "Sellmeier 2",
        "tabulated nk pchip-clamped/1" => "复折射率表",
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
        FontSize = DisplayTypography.SectionTitle,
        FontWeight = FontWeight.SemiBold,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        FontSize = DisplayTypography.CardTitle,
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

    internal static Control DatabasePage(string title, string summary, Control content, bool showHeader = true)
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
                    new TextBlock { Text = title, FontSize = DisplayTypography.LargeTitle, FontWeight = FontWeight.SemiBold },
                    summaryText
                }
            }
        };
        header.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        header.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        var root = new DockPanel();
        root.BindThemeResource(Panel.BackgroundProperty, ThemeResourceBindings.Workspace);
        if (showHeader)
        {
            DockPanel.SetDock(header, Avalonia.Controls.Dock.Top);
            root.Children.Add(header);
        }
        var contentCard = new Border
        {
            Margin = new Thickness(16),
            ClipToBounds = true,
            Child = content
        };
        SettingsPanelChrome.ApplySurfaceCardStyle(contentCard);
        root.Children.Add(contentCard);
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
    private readonly ListBox _list = new();
    private readonly OpticSceneControl _preview = new()
    {
        ViewMode = OpticSceneViewMode.TwoDimensional,
        ShowRays = true,
        ShowScaleBar = true
    };
    private readonly TextBlock _count = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap
    };
    private readonly TextBlock _name = DetailValue(18, FontWeight.SemiBold);
    private readonly TextBlock _source = DetailValue();
    private readonly TextBlock _sourceUrl = DetailValue();
    private readonly TextBlock _license = DetailValue();
    private readonly TextBlock _format = DetailValue();
    private readonly TextBlock _efl = DetailValue();
    private readonly TextBlock _fNumber = DetailValue();
    private readonly TextBlock _numericalAperture = DetailValue();
    private readonly TextBlock _workingDistance = DetailValue();
    private readonly TextBlock _track = DetailValue();
    private readonly TextBlock _maximumAperture = DetailValue();
    private readonly TextBlock _lensElements = DetailValue();
    private readonly TextBlock _surfaces = DetailValue();
    private readonly TextBlock _fields = DetailValue();
    private readonly TextBlock _wavelengths = DetailValue();
    private readonly TextBlock _lensType = DetailValue();
    private readonly TextBlock _application = DetailValue();
    private readonly TextBlock _designOrganization = DetailValue();
    private readonly TextBlock _importedAt = DetailValue();
    private readonly TextBlock _importerVersion = DetailValue();
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
        UiLocalization.BindChoices(_category);
        _category.SelectedIndex = 0;
        _category.SelectionChanged += (_, _) => RefreshList(selectFirst: true);
        _search.TextChanged += (_, _) => RefreshList(selectFirst: true);
        _list.SelectionChanged += async (_, _) => await ShowSelectedLensAsync();
        _list.DoubleTapped += async (_, _) => await OpenSelectedLensAsync();
        _status.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);

        Content = BuildPage();
        ReloadIndex();
        UiLocalization.BindLanguage(this, () => { if (SelectedLens() is { } selected) RefreshSelectedMetadata(selected); });
    }

    private Control BuildPage()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        root.BindThemeResource(Panel.BackgroundProperty, ThemeResourceBindings.Workspace);

        var header = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(18, 14, 18, 10),
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(new LocalIcon
        {
            IconName = "action-search",
            Width = 18,
            Height = 18,
            Margin = new Thickness(0, 8, 8, 8),
            VerticalAlignment = VerticalAlignment.Center
        });
        _search.Margin = new Thickness(0, 4, 12, 4);
        _category.Margin = new Thickness(0, 4, 12, 4);
        _count.Margin = new Thickness(0, 10, 0, 4);
        header.Children.Add(_search);
        header.Children.Add(_category);
        header.Children.Add(_count);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var listFrame = new Border
        {
            Child = _list
        };
        SettingsPanelChrome.ApplyControlFrameStyle(listFrame);

        var details = new Grid
        {
            RowDefinitions = new RowDefinitions("3*,2*,Auto"),
            RowSpacing = 12
        };
        var previewFrame = new Border
        {
            ClipToBounds = true,
            Child = _preview
        };
        SettingsPanelChrome.ApplyControlFrameStyle(previewFrame);
        Grid.SetRow(previewFrame, 0);
        details.Children.Add(previewFrame);

        var parameterScroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = BuildParameterGrid()
        };
        Grid.SetRow(parameterScroller, 1);
        details.Children.Add(parameterScroller);

        var statusBar = new Border
        {
            Padding = new Thickness(0, 4),
            Child = _status
        };
        Grid.SetRow(statusBar, 2);
        details.Children.Add(statusBar);
        var body = new ResponsiveTwoPaneGrid(
            listFrame,
            details,
            "330,16,*",
            "2*,16,3*",
            breakpoint: 820)
        {
            Margin = new Thickness(18, 0, 18, 14)
        };

        Grid.SetRow(body, 1);
        root.Children.Add(body);
        return root;
    }

    private Control BuildParameterGrid()
    {
        var panel = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,24,Auto,*"),
            RowDefinitions = new RowDefinitions(
                "Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto"),
            RowSpacing = 7
        };
        AddPair(panel, "镜头：", _name, 0, 0);
        AddPair(panel, "镜头类型：", _lensType, 0, 1);
        AddPair(panel, "应用场景：", _application, 1, 0);
        AddPair(panel, "设计单位：", _designOrganization, 1, 1);
        AddPair(panel, "来源：", _source, 2, 0);
        AddPair(panel, "许可证：", _license, 2, 1);
        AddPair(panel, "有效焦距：", _efl, 3, 0);
        AddPair(panel, "F/#：", _fNumber, 3, 1);
        AddPair(panel, "数值孔径：", _numericalAperture, 4, 0);
        AddPair(panel, "工作距离：", _workingDistance, 4, 1);
        AddPair(panel, "视场：", _fields, 5, 0);
        AddPair(panel, "波长范围：", _wavelengths, 5, 1);
        AddPair(panel, "镜片数：", _lensElements, 6, 0);
        AddPair(panel, "最大口径：", _maximumAperture, 6, 1);
        AddPair(panel, "系统总长：", _track, 7, 0);
        AddPair(panel, "表面数：", _surfaces, 7, 1);
        AddPair(panel, "导入时间：", _importedAt, 8, 0);
        AddPair(panel, "导入器版本：", _importerVersion, 8, 1);
        AddPair(panel, "来源地址：", _sourceUrl, 9, 0);
        AddPair(panel, "格式/状态：", _format, 9, 1);
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
                lens.SourceName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                lens.LensType.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                lens.Application.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                lens.DesignOrganization.Contains(query, StringComparison.OrdinalIgnoreCase))
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

    private void RefreshSelectedMetadata(LensLibraryEntryDto lens)
    {
        _name.Text = lens.Name;
        _source.Text = lens.SourceName;
        _sourceUrl.Text = TextOrDash(lens.SourceUrl);
        _license.Text = lens.License;
        _format.Text = $"{lens.SourceFormat} · {UiLocalization.Text(lens.ImportStatus)}";
        _efl.Text = Millimeters(lens.EffectiveFocalLength);
        _fNumber.Text = Number(lens.FNumber);
        _numericalAperture.Text = MetadataNumber(
            lens.NumericalAperture,
            lens.NumericalApertureBasis);
        _workingDistance.Text = MetadataMillimeters(
            lens.WorkingDistance,
            lens.WorkingDistanceBasis);
        _track.Text = Millimeters(lens.TotalTrack);
        _maximumAperture.Text = Millimeters(lens.MaximumClearAperture);
        _lensElements.Text = lens.LensElementCount > 0
            ? UiLocalization.Format("{0} 片", lens.LensElementCount)
            : "—";
        _surfaces.Text = lens.SurfaceCount.ToString(CultureInfo.InvariantCulture);
        _fields.Text = FieldDescription(lens);
        _lensType.Text = KnownMetadata(lens.LensType);
        _application.Text = KnownMetadata(lens.Application);
        _designOrganization.Text = KnownMetadata(lens.DesignOrganization);
        _importedAt.Text = lens.ImportedAt is null
            ? UiLocalization.Text("历史条目未记录")
            : lens.ImportedAt.Value.ToLocalTime().ToString(
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture);
        _importerVersion.Text = KnownMetadata(lens.ImporterVersion);
        _wavelengths.Text = lens.WavelengthCount == 0
            ? "—"
            : lens.MinimumWavelengthNanometers == lens.MaximumWavelengthNanometers
                ? $"{Number(lens.MinimumWavelengthNanometers)} nm"
                : $"{Number(lens.MinimumWavelengthNanometers)}–{Number(lens.MaximumWavelengthNanometers)} nm";
        _status.Text = $"库文件：{lens.NativePath}";

    }

    private async Task ShowSelectedLensAsync()
    {
        var generation = ++_previewGeneration;
        var lens = SelectedLens();
        if (lens is null)
        {
            ClearDetails();
            return;
        }

        RefreshSelectedMetadata(lens);

        try
        {
            var scene = await _lenses.BuildPreviewAsync(lens.Id);
            if (generation != _previewGeneration)
            {
                return;
            }

            _preview.Scene = scene;
            if (scene?.Warnings is { Count: > 0 })
            {
                _status.Text = string.Join(Environment.NewLine, scene.Warnings);
            }
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
            _sourceUrl, _numericalAperture, _workingDistance, _track,
            _maximumAperture, _lensElements, _surfaces, _fields, _wavelengths,
            _lensType, _application, _designOrganization, _importedAt, _importerVersion
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
        FontWeight? fontWeight = null) => UiLocalization.Preserve(new TextBlock
        {
            FontSize = fontSize,
            FontWeight = fontWeight ?? FontWeight.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        });

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

    private static string MetadataNumber(double value, string basis)
    {
        var number = Number(value);
        return number == "—" ? number : $"{number} · {UiLocalization.Text(TextOrDash(basis))}";
    }

    private static string MetadataMillimeters(double value, string basis)
    {
        var millimeters = Millimeters(value);
        return millimeters == "—" ? millimeters : $"{millimeters} · {UiLocalization.Text(TextOrDash(basis))}";
    }

    private static string TextOrDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static string KnownMetadata(string? value) => value is "未分类光学系统" or "公开研究与教学" or "未注明" or "历史版本（未记录）"
        ? UiLocalization.Text(value) : TextOrDash(value);

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
            "ObjectConeAngle" => $"{value}°",
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
            ? UiLocalization.Format("{0} 个", lens.FieldCount)
            : UiLocalization.Format("{0} 个 · {1} {2}{3}", lens.FieldCount, UiLocalization.Text(definition), maximum, unit);
    }

    private static void Add(Grid grid, Control control, int column)
    {
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }
}
