using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App.Panels;

internal sealed class CommercialLensCatalogPanel : UserControl
{
    private readonly ILensLibraryService _lenses;
    private readonly Func<string, Task>? _openLensProject;
    private readonly ComboBox _vendor = Selector(150);
    private readonly CheckBox _useEfl = new() { Content = "有效焦距", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _eflMinimum = NumberBox("最小 mm");
    private readonly TextBox _eflMaximum = NumberBox("最大 mm");
    private readonly CheckBox _useDiameter = new() { Content = "入瞳直径 EPD", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _diameterMinimum = NumberBox("最小 mm");
    private readonly TextBox _diameterMaximum = NumberBox("最大 mm");
    private readonly ComboBox _shape = Selector(130);
    private readonly ComboBox _surfaceType = Selector(130);
    private readonly ComboBox _elementCount = Selector(110);
    private readonly TextBox _search = new()
    {
        MinWidth = 210,
        MinHeight = 34,
        PlaceholderText = "料号、名称或类型"
    };
    private readonly DataGrid _results = new()
    {
        AutoGenerateColumns = false,
        IsReadOnly = true,
        CanUserResizeColumns = true,
        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        SelectionMode = DataGridSelectionMode.Single
    };
    private readonly TextBlock _count = ValueText();
    private readonly TextBlock _title = ValueText(18, FontWeight.SemiBold);
    private readonly TextBlock _identity = ValueText();
    private readonly TextBlock _optical = ValueText();
    private readonly TextBlock _classification = ValueText();
    private readonly TextBlock _availability = ValueText();
    private readonly TextBlock _source = ValueText();
    private readonly Button _productPage = CommandButton("external-link", "厂商页面");
    private readonly Button _dataSheet = CommandButton("file-text", "数据表");
    private readonly Button _openModel = CommandButton("folder-open", "载入模型");
    private readonly DispatcherTimer _filterTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private IReadOnlyList<CommercialLensEntryDto> _all = Array.Empty<CommercialLensEntryDto>();
    private IReadOnlyList<CommercialLensRow> _visible = Array.Empty<CommercialLensRow>();
    private bool _suppressFilter;

    public CommercialLensCatalogPanel(
        ILensLibraryService lenses,
        Func<string, Task>? openLensProject = null)
    {
        _lenses = lenses;
        _openLensProject = openLensProject;
        ConfigureSelectors();
        ConfigureGrid();
        WireFilters();
        _results.SelectionChanged += (_, _) => ShowSelection();
        _productPage.Click += async (_, _) => await OpenSelectedUriAsync(dataSheet: false);
        _dataSheet.Click += async (_, _) => await OpenSelectedUriAsync(dataSheet: true);
        _openModel.Click += async (_, _) => await OpenSelectedModelAsync();
        Content = BuildPage();
        Reload();
    }

    private Control BuildPage()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        root.BindThemeResource(Panel.BackgroundProperty, ThemeResourceBindings.Workspace);

        var filterCard = new Border
        {
            Margin = new Thickness(16, 14, 16, 10),
            Padding = new Thickness(14, 12),
            Child = BuildFilters()
        };
        SettingsPanelChrome.ApplySurfaceCardStyle(filterCard);
        root.Children.Add(filterCard);

        var resultsFrame = new Border { Child = _results };
        SettingsPanelChrome.ApplyControlFrameStyle(resultsFrame);

        var detailFrame = new Border
        {
            Padding = new Thickness(16),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = BuildDetails()
            }
        };
        SettingsPanelChrome.ApplySurfaceCardStyle(detailFrame);
        var body = new ResponsiveTwoPaneGrid(
            resultsFrame,
            detailFrame,
            "3*,16,2*",
            "2*,16,3*",
            breakpoint: 850)
        {
            Margin = new Thickness(16, 0, 16, 14)
        };

        Grid.SetRow(body, 1);
        root.Children.Add(body);
        return root;
    }

    private Control BuildFilters()
    {
        var firstRow = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            ItemHeight = double.NaN
        };
        firstRow.Children.Add(Labeled("厂商", _vendor));
        firstRow.Children.Add(Labeled("形状", _shape));
        firstRow.Children.Add(Labeled("曲面", _surfaceType));
        firstRow.Children.Add(Labeled("元件数", _elementCount));
        firstRow.Children.Add(Labeled("搜索", _search));

        var searchButton = CommandButton("search", "搜索");
        searchButton.Click += (_, _) => ApplyFilterImmediately();
        ToolTip.SetTip(searchButton, "立即应用当前筛选；筛选控件本身也会自动更新结果。");
        var resetButton = CommandButton("rotate-ccw", "重置");
        resetButton.Click += (_, _) => ResetFilters();
        ToolTip.SetTip(resetButton, "清除全部筛选条件并显示完整目录。");
        firstRow.Children.Add(searchButton);
        firstRow.Children.Add(resetButton);
        firstRow.Children.Add(_count);

        var secondRow = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0)
        };
        secondRow.Children.Add(_useEfl);
        secondRow.Children.Add(_eflMinimum);
        secondRow.Children.Add(_eflMaximum);
        secondRow.Children.Add(Spacer());
        secondRow.Children.Add(_useDiameter);
        secondRow.Children.Add(_diameterMinimum);
        secondRow.Children.Add(_diameterMaximum);

        return new StackPanel { Children = { firstRow, secondRow } };
    }

    private Control BuildDetails()
    {
        var buttonRow = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 12, 0, 0)
        };
        buttonRow.Children.Add(_productPage);
        buttonRow.Children.Add(_dataSheet);
        buttonRow.Children.Add(_openModel);
        return new StackPanel
        {
            Spacing = 10,
            Children =
            {
                _title,
                DetailSection("厂商与料号", _identity),
                DetailSection("光学规格", _optical),
                DetailSection("目录分类", _classification),
                DetailSection("可用性", _availability),
                DetailSection("数据来源", _source),
                buttonRow
            }
        };
    }

    private void ConfigureSelectors()
    {
        _shape.ItemsSource = new[] { "全部形状", "E · 等曲率", "B · 双面曲率", "P · 平面型", "M · 弯月型", "? · 其他" };
        _surfaceType.ItemsSource = new[] { "全部曲面", "S · 球面", "G · GRIN", "A · 非球面", "T · 环曲面" };
        _elementCount.ItemsSource = new[] { "任意", "1", "2", "3+" };
        _shape.SelectedIndex = 0;
        _surfaceType.SelectedIndex = 0;
        _elementCount.SelectedIndex = 0;
    }

    private void WireFilters()
    {
        _filterTimer.Tick += (_, _) =>
        {
            _filterTimer.Stop();
            if (!_suppressFilter)
            {
                ApplyFilter(selectFirst: true);
            }
        };

        _vendor.SelectionChanged += (_, _) => ApplyFilterImmediately();
        _shape.SelectionChanged += (_, _) => ApplyFilterImmediately();
        _surfaceType.SelectionChanged += (_, _) => ApplyFilterImmediately();
        _elementCount.SelectionChanged += (_, _) => ApplyFilterImmediately();
        _search.TextChanged += (_, _) => ScheduleFilter();
        _eflMinimum.TextChanged += (_, _) => ScheduleRangeFilter(_useEfl);
        _eflMaximum.TextChanged += (_, _) => ScheduleRangeFilter(_useEfl);
        _diameterMinimum.TextChanged += (_, _) => ScheduleRangeFilter(_useDiameter);
        _diameterMaximum.TextChanged += (_, _) => ScheduleRangeFilter(_useDiameter);
        _useEfl.IsCheckedChanged += (_, _) => OnRangeFilterChanged();
        _useDiameter.IsCheckedChanged += (_, _) => OnRangeFilterChanged();
        UpdateRangeInputState();
    }

    private void OnRangeFilterChanged()
    {
        UpdateRangeInputState();
        ApplyFilterImmediately();
    }

    private void UpdateRangeInputState()
    {
        _eflMinimum.IsEnabled = _useEfl.IsChecked == true;
        _eflMaximum.IsEnabled = _useEfl.IsChecked == true;
        _diameterMinimum.IsEnabled = _useDiameter.IsChecked == true;
        _diameterMaximum.IsEnabled = _useDiameter.IsChecked == true;
    }

    private void ScheduleRangeFilter(CheckBox rangeToggle)
    {
        if (rangeToggle.IsChecked == true)
        {
            ScheduleFilter();
        }
    }

    private void ScheduleFilter()
    {
        if (_suppressFilter)
        {
            return;
        }

        _filterTimer.Stop();
        _filterTimer.Start();
    }

    private void ApplyFilterImmediately()
    {
        if (_suppressFilter)
        {
            return;
        }

        _filterTimer.Stop();
        ApplyFilter(selectFirst: true);
    }

    private void ConfigureGrid()
    {
        _results.Columns.Add(Column("厂商", nameof(CommercialLensRow.Manufacturer), 135));
        _results.Columns.Add(Column("料号", nameof(CommercialLensRow.PartNumber), 130));
        _results.Columns.Add(Column("名称", nameof(CommercialLensRow.Name), 260));
        _results.Columns.Add(Column("EFL (mm)", nameof(CommercialLensRow.EffectiveFocalLength), 95));
        _results.Columns.Add(Column("EPD (mm)", nameof(CommercialLensRow.EntrancePupilDiameter), 95));
        _results.Columns.Add(Column("分类", nameof(CommercialLensRow.Classification), 135));
        _results.Columns.Add(Column("元件", nameof(CommercialLensRow.ElementCount), 65));
        _results.Columns.Add(Column("模型", nameof(CommercialLensRow.ModelAvailability), 110));
    }

    private void Reload()
    {
        _all = _lenses.GetCommercialLenses();
        _vendor.ItemsSource = new[] { "全部厂商" }
            .Concat(_all.Select(entry => entry.Manufacturer).Distinct(StringComparer.OrdinalIgnoreCase))
            .ToArray();
        _vendor.SelectedIndex = 0;
        ApplyFilter(selectFirst: true);
    }

    private void ApplyFilter(bool selectFirst)
    {
        var selectedId = SelectedEntry()?.Id;
        var vendor = _vendor.SelectedItem as string;
        var query = _search.Text?.Trim() ?? string.Empty;
        var eflMinimum = Parse(_eflMinimum.Text, double.NegativeInfinity);
        var eflMaximum = Parse(_eflMaximum.Text, double.PositiveInfinity);
        var diameterMinimum = Parse(_diameterMinimum.Text, double.NegativeInfinity);
        var diameterMaximum = Parse(_diameterMaximum.Text, double.PositiveInfinity);
        var shapeCode = (_shape.SelectedItem as string)?.Split('·')[0].Trim();
        var surfaceType = (_surfaceType.SelectedItem as string)?.Split('·')[0].Trim();
        var elements = _elementCount.SelectedItem as string;

        _visible = _all
            .Where(entry => vendor is null or "全部厂商"
                || entry.Manufacturer.Equals(vendor, StringComparison.OrdinalIgnoreCase))
            .Where(entry => string.IsNullOrEmpty(query)
                || entry.PartNumber.Contains(query, StringComparison.OrdinalIgnoreCase)
                || entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || entry.LensType.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Where(entry => _useEfl.IsChecked != true
                || entry.EffectiveFocalLength >= eflMinimum
                && entry.EffectiveFocalLength <= eflMaximum)
            .Where(entry => _useDiameter.IsChecked != true
                || entry.EntrancePupilDiameter >= diameterMinimum
                && entry.EntrancePupilDiameter <= diameterMaximum)
            .Where(entry => shapeCode is null or "全部形状"
                || entry.ShapeCode.Equals(shapeCode, StringComparison.OrdinalIgnoreCase))
            .Where(entry => surfaceType is null or "全部曲面"
                || entry.SurfaceType.Equals(surfaceType, StringComparison.OrdinalIgnoreCase))
            .Where(entry => elements switch
            {
                "1" => entry.ElementCount == 1,
                "2" => entry.ElementCount == 2,
                "3+" => entry.ElementCount >= 3,
                _ => true
            })
            .Select(entry => new CommercialLensRow(entry))
            .ToArray();
        _results.ItemsSource = _visible;
        var vendorCount = _all
            .Select(entry => entry.Manufacturer)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        _count.Text = $"{_visible.Count} / {_all.Count} 项 · {vendorCount} 家厂商";
        var selected = _visible.FirstOrDefault(row => row.Entry.Id == selectedId)
            ?? (selectFirst ? _visible.FirstOrDefault() : null);
        _results.SelectedItem = selected;
        if (selected is null)
        {
            ClearDetails();
        }
    }

    private void ResetFilters()
    {
        _filterTimer.Stop();
        _suppressFilter = true;
        try
        {
            _vendor.SelectedIndex = 0;
            _shape.SelectedIndex = 0;
            _surfaceType.SelectedIndex = 0;
            _elementCount.SelectedIndex = 0;
            _search.Text = string.Empty;
            _useEfl.IsChecked = false;
            _useDiameter.IsChecked = false;
            _eflMinimum.Text = string.Empty;
            _eflMaximum.Text = string.Empty;
            _diameterMinimum.Text = string.Empty;
            _diameterMaximum.Text = string.Empty;
            UpdateRangeInputState();
        }
        finally
        {
            _suppressFilter = false;
        }

        ApplyFilter(selectFirst: true);
    }

    private void ShowSelection()
    {
        var entry = SelectedEntry();
        if (entry is null)
        {
            ClearDetails();
            return;
        }

        _title.Text = entry.Name;
        _identity.Text = $"{entry.Manufacturer} · {entry.PartNumber}\n{entry.ProductStatus}";
        _optical.Text = string.Join(
            Environment.NewLine,
            $"有效焦距：{Millimeters(entry.EffectiveFocalLength)}",
            $"入瞳直径：{Millimeters(entry.EntrancePupilDiameter)}",
            $"目录直径：{Millimeters(entry.CatalogDiameter)}",
            $"清口径：{Millimeters(entry.ClearAperture)}",
            $"后焦距：{Millimeters(entry.BackFocalLength)}",
            $"NA：{Number(entry.NumericalAperture)}",
            $"波长：{Range(entry.MinimumWavelengthNanometers, entry.MaximumWavelengthNanometers, "nm")}",
            $"工作距离：{Range(entry.MinimumWorkingDistance, entry.MaximumWorkingDistance, "mm")}");
        _classification.Text = $"{entry.LensType}\n形状 {entry.ShapeCode} · 曲面 {SurfaceTypeLabel(entry.SurfaceType)} · {entry.ElementCount} 个元件";
        _availability.Text = $"{entry.ModelStatus}\n核验日期：{entry.VerifiedAt:yyyy-MM-dd}";
        _source.Text = entry.SourceNote;
        _productPage.IsEnabled = Uri.IsWellFormedUriString(entry.ProductUrl, UriKind.Absolute);
        _dataSheet.IsEnabled = Uri.IsWellFormedUriString(entry.DataSheetUrl, UriKind.Absolute);
        _openModel.IsEnabled = _openLensProject is not null
            && _lenses.GetCommercialNativeProjectPath(entry.Id) is not null;
        ToolTip.SetTip(
            _openModel,
            _openModel.IsEnabled
                ? "将随目录提供的 STAROPT 模型载入为独立设计。"
                : "此目录条目没有获得许可并经过校验的本地光学处方。官方产品页仍可查看。");
        ToolTip.SetTip(
            _productPage,
            _productPage.IsEnabled
                ? "在系统浏览器中打开厂商产品页面。"
                : "此目录条目没有可验证的厂商产品页面地址。");
        ToolTip.SetTip(
            _dataSheet,
            _dataSheet.IsEnabled
                ? "在系统浏览器中打开厂商数据表。"
                : "此目录条目没有可验证的直接数据表地址，可先查看厂商页面。");
    }

    private async Task OpenSelectedUriAsync(bool dataSheet)
    {
        var entry = SelectedEntry();
        var value = dataSheet ? entry?.DataSheetUrl : entry?.ProductUrl;
        if (value is null || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return;
        }

        var launcher = TopLevel.GetTopLevel(this)?.Launcher;
        if (launcher is not null)
        {
            await launcher.LaunchUriAsync(uri);
        }
    }

    private async Task OpenSelectedModelAsync()
    {
        var entry = SelectedEntry();
        if (entry is null || _openLensProject is null)
        {
            return;
        }

        var path = _lenses.GetCommercialNativeProjectPath(entry.Id);
        if (path is not null)
        {
            await _openLensProject(path);
        }
    }

    private CommercialLensEntryDto? SelectedEntry() =>
        (_results.SelectedItem as CommercialLensRow)?.Entry;

    private void ClearDetails()
    {
        foreach (var text in new[] { _title, _identity, _optical, _classification, _availability, _source })
        {
            text.Text = "—";
        }

        _productPage.IsEnabled = false;
        _dataSheet.IsEnabled = false;
        _openModel.IsEnabled = false;
        ToolTip.SetTip(_productPage, "请先选择目录条目。");
        ToolTip.SetTip(_dataSheet, "请先选择目录条目。");
        ToolTip.SetTip(_openModel, "请先选择目录条目。");
    }

    private static Border DetailSection(string title, Control value)
    {
        var card = new Border
        {
            Padding = new Thickness(12, 10),
            Child = new StackPanel
            {
                Spacing = 5,
                Children =
                {
                    new TextBlock { Text = title, FontWeight = FontWeight.SemiBold },
                    value
                }
            }
        };
        SettingsPanelChrome.ApplyControlFrameStyle(card);
        return card;
    }

    private static Control Labeled(string label, Control control) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 7,
        Margin = new Thickness(0, 0, 14, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Children =
        {
            new TextBlock { Text = $"{label}：", VerticalAlignment = VerticalAlignment.Center },
            control
        }
    };

    private static Border Spacer() => new() { Width = 20 };

    private static ComboBox Selector(double minWidth) => new() { MinWidth = minWidth, MinHeight = 34 };

    private static TextBox NumberBox(string placeholder) => new()
    {
        Width = 98,
        MinHeight = 34,
        Margin = new Thickness(8, 0, 0, 0),
        PlaceholderText = placeholder
    };

    private static Button CommandButton(string icon, string text) => new()
    {
        Content = new LocalIconLabel(icon, text),
        MinHeight = 34,
        Margin = new Thickness(8, 0, 0, 0),
        Padding = new Thickness(11, 5)
    };

    private static TextBlock ValueText(double? size = null, FontWeight? weight = null) => new()
    {
        FontSize = size ?? DisplayTypography.Scale(14),
        FontWeight = weight ?? FontWeight.Normal,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static DataGridTextColumn Column(string header, string property, double width) => new()
    {
        Header = header,
        Binding = new Binding(property),
        Width = new DataGridLength(width, DataGridLengthUnitType.Pixel),
        IsReadOnly = true
    };

    private static double Parse(string? value, double fallback) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var result)
            || double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
            ? result
            : fallback;

    private static string Number(double value) =>
        double.IsFinite(value) && value != 0
            ? NumericDisplayFormatter.Format(value, CultureInfo.InvariantCulture)
            : "—";

    private static string Millimeters(double value)
    {
        var number = Number(value);
        return number == "—" ? number : $"{number} mm";
    }

    private static string Range(double minimum, double maximum, string unit)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum)
            || minimum == 0 && maximum == 0)
        {
            return "—";
        }

        if (minimum == maximum || maximum == 0)
        {
            return $"{Number(minimum)} {unit}";
        }

        return $"{Number(minimum)}–{Number(maximum)} {unit}";
    }

    private static string SurfaceTypeLabel(string code) => code switch
    {
        "S" => "S · 球面",
        "G" => "G · GRIN",
        "A" => "A · 非球面",
        "T" => "T · 环曲面",
        _ => code
    };

    private sealed record CommercialLensRow(CommercialLensEntryDto Entry)
    {
        public string Manufacturer => Entry.Manufacturer;

        public string PartNumber => Entry.PartNumber;

        public string Name => Entry.Name;

        public string EffectiveFocalLength => Number(Entry.EffectiveFocalLength);

        public string EntrancePupilDiameter => Number(Entry.EntrancePupilDiameter);

        public string Classification => $"{Entry.ShapeCode}/{Entry.SurfaceType}";

        public int ElementCount => Entry.ElementCount;

        public string ModelAvailability => string.IsNullOrWhiteSpace(Entry.NativePath) ? "仅目录" : "可载入";
    }
}
