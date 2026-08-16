using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App.Panels;

internal sealed class StockLensMatchingPanel : UserControl, IDisposable
{
    private readonly IOpticalDocumentService _documents;
    private readonly ILensLibraryService _lenses;
    private readonly IWorkspaceEventStream _events;
    private readonly ComboBox _surfaceScope = Selector(250);
    private readonly ComboBox _maximumResults = Selector(120);
    private readonly NumericUpDown _eflTolerance = PercentInput(25);
    private readonly NumericUpDown _epdTolerance = PercentInput(25);
    private readonly CheckBox _matchShape = new() { Content = "形状匹配", IsEnabled = false };
    private readonly CheckBox _matchDirection = new() { Content = "方向匹配", IsChecked = true };
    private readonly Dictionary<string, CheckBox> _manufacturerChecks = new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBlock _targetSummary = Text(size: 14, weight: FontWeight.SemiBold);
    private readonly TextBlock _status = Text();
    private readonly DataGrid _results = new()
    {
        AutoGenerateColumns = false,
        IsReadOnly = true,
        CanUserResizeColumns = true,
        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        SelectionMode = DataGridSelectionMode.Single,
        MinHeight = 360
    };
    private readonly Button _productPage = CommandButton("external-link", "厂商页面");
    private bool _disposed;

    public StockLensMatchingPanel(
        IOpticalDocumentService documents,
        ILensLibraryService lenses,
        IWorkspaceEventStream events)
    {
        _documents = documents;
        _lenses = lenses;
        _events = events;
        _surfaceScope.ItemsSource = new[] { "所有面（当前系统）" };
        _surfaceScope.SelectedIndex = 0;
        _maximumResults.ItemsSource = new[] { "5", "10", "20", "50" };
        _maximumResults.SelectedIndex = 0;
        ToolTip.SetTip(_matchShape, "当前目标是完整光学系统，没有唯一的单镜片形状；因此不伪造形状约束。");
        ConfigureGrid();
        _productPage.IsEnabled = false;
        _productPage.Click += async (_, _) => await OpenSelectedProductPageAsync();
        _results.SelectionChanged += (_, _) => UpdateSelectionActions();
        _events.Changed += OnWorkspaceChanged;
        Content = BuildPage();
        RunMatch();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _events.Changed -= OnWorkspaceChanged;
    }

    private Control BuildPage()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        root.BindThemeResource(Panel.BackgroundProperty, ThemeResourceBindings.Workspace);

        var settingsCard = new Border
        {
            Margin = new Thickness(16, 14, 16, 10),
            Padding = new Thickness(16, 14),
            Child = BuildSettings()
        };
        SettingsPanelChrome.ApplySurfaceCardStyle(settingsCard);
        root.Children.Add(settingsCard);

        var resultArea = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Margin = new Thickness(16, 0, 16, 14)
        };
        var resultHeader = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(2, 0, 2, 8)
        };
        resultHeader.Children.Add(_status);
        Grid.SetColumn(_productPage, 1);
        resultHeader.Children.Add(_productPage);
        resultArea.Children.Add(resultHeader);

        var frame = new Border { Child = _results };
        SettingsPanelChrome.ApplyControlFrameStyle(frame);
        Grid.SetRow(frame, 1);
        resultArea.Children.Add(frame);
        Grid.SetRow(resultArea, 1);
        root.Children.Add(resultArea);
        return root;
    }

    private Control BuildSettings()
    {
        var manufacturers = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (var name in StockLensCatalogPolicy.Manufacturers)
        {
            var check = new CheckBox
            {
                Content = ManufacturerLabel(name),
                IsChecked = true,
                Margin = new Thickness(0, 0, 18, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _manufacturerChecks[name] = check;
            manufacturers.Children.Add(check);
        }

        var inputs = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,260,32,Auto,160,32,Auto,160"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            RowSpacing = 9,
            ColumnSpacing = 8
        };
        AddField(inputs, 0, 0, "面", _surfaceScope);
        AddField(inputs, 0, 3, "显示匹配结果", _maximumResults);
        AddField(inputs, 0, 6, "EFL 公差 (%)", _eflTolerance);
        AddField(inputs, 1, 0, "目标参数", _targetSummary);
        Grid.SetColumnSpan(_targetSummary, 5);
        AddField(inputs, 1, 6, "EPD 公差 (%)", _epdTolerance);

        var options = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _matchShape, _matchDirection }
        };
        _matchShape.Margin = new Thickness(0, 0, 22, 0);
        Grid.SetRow(options, 2);
        Grid.SetColumn(options, 0);
        Grid.SetColumnSpan(options, 5);
        inputs.Children.Add(options);

        var run = CommandButton("search", "开始匹配");
        run.MinWidth = 120;
        run.Click += (_, _) => RunMatch();
        Grid.SetRow(run, 2);
        Grid.SetColumn(run, 6);
        Grid.SetColumnSpan(run, 2);
        inputs.Children.Add(run);

        return new StackPanel
        {
            Spacing = 12,
            Children =
            {
                Text("库存镜头匹配", 18, FontWeight.SemiBold),
                Labeled("生产厂商", manufacturers),
                inputs,
                new TextBlock
                {
                    Text = "匹配使用当前系统的一阶 EFL 和入瞳直径，仅返回目录候选；ZMF 目录头不含可授权替换的处方，因此本页不会假装执行空气厚度补偿或再优化。",
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };
    }

    private void ConfigureGrid()
    {
        _results.Columns.Add(Column("排名", nameof(MatchRow.Rank), 65));
        _results.Columns.Add(Column("厂商", nameof(MatchRow.Manufacturer), 145));
        _results.Columns.Add(Column("料号", nameof(MatchRow.PartNumber), 190));
        _results.Columns.Add(Column("EFL (mm)", nameof(MatchRow.EffectiveFocalLength), 105));
        _results.Columns.Add(Column("EFL 偏差", nameof(MatchRow.EffectiveFocalLengthDeviation), 105));
        _results.Columns.Add(Column("EPD (mm)", nameof(MatchRow.EntrancePupilDiameter), 105));
        _results.Columns.Add(Column("EPD 偏差", nameof(MatchRow.EntrancePupilDiameterDeviation), 105));
        _results.Columns.Add(Column("形状/曲面", nameof(MatchRow.Classification), 120));
        _results.Columns.Add(Column("综合分数", nameof(MatchRow.Score), 105));
    }

    private void RunMatch()
    {
        var snapshot = _documents.GetSnapshot();
        var targetEfl = snapshot.EffectiveFocalLength;
        var targetEpd = snapshot.EntrancePupilDiameter;
        _targetSummary.Text = $"EFL {Number(targetEfl)} mm · EPD {Number(targetEpd)} mm";
        if (!double.IsFinite(targetEfl) || Math.Abs(targetEfl) <= 1e-12
            || !double.IsFinite(targetEpd) || targetEpd <= 0)
        {
            _results.ItemsSource = Array.Empty<MatchRow>();
            _status.Text = "当前系统无法得到有效的一阶 EFL 或入瞳直径，不能进行库存镜头匹配。";
            return;
        }

        var request = new StockLensMatchRequestDto(
            targetEfl,
            targetEpd,
            _manufacturerChecks.Where(item => item.Value.IsChecked == true).Select(item => item.Key).ToArray(),
            ParseMaximumResults(),
            decimal.ToDouble(_eflTolerance.Value ?? 25),
            decimal.ToDouble(_epdTolerance.Value ?? 25),
            MatchShape: false,
            TargetShapeCode: "?",
            MatchPowerDirection: _matchDirection.IsChecked == true);
        var matches = StockLensMatcher.Match(_lenses.GetCommercialLenses(), request);
        var rows = matches.Select((match, index) => new MatchRow(index + 1, match)).ToArray();
        _results.ItemsSource = rows;
        _results.SelectedItem = rows.FirstOrDefault();
        _status.Text = rows.Length == 0
            ? "没有候选满足当前厂商、方向和公差条件。"
            : $"找到 {rows.Length} 个候选；分数越小越接近当前系统。";
    }

    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        var snapshot = _documents.GetSnapshot();
        _targetSummary.Text = $"EFL {Number(snapshot.EffectiveFocalLength)} mm · EPD {Number(snapshot.EntrancePupilDiameter)} mm";
        _status.Text = "当前系统已改变，请重新执行匹配。";
    }

    private void UpdateSelectionActions()
    {
        var entry = (_results.SelectedItem as MatchRow)?.Match.Entry;
        _productPage.IsEnabled = entry is not null
            && Uri.IsWellFormedUriString(entry.ProductUrl, UriKind.Absolute);
    }

    private async Task OpenSelectedProductPageAsync()
    {
        var value = (_results.SelectedItem as MatchRow)?.Match.Entry.ProductUrl;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return;
        }

        var launcher = TopLevel.GetTopLevel(this)?.Launcher;
        if (launcher is not null)
        {
            await launcher.LaunchUriAsync(uri);
        }
    }

    private int ParseMaximumResults() =>
        int.TryParse(_maximumResults.SelectedItem as string, out var value) ? value : 5;

    private static void AddField(Grid grid, int row, int column, string label, Control control)
    {
        var text = Text($"{label}：");
        text.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(text, row);
        Grid.SetColumn(text, column);
        grid.Children.Add(text);
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column + 1);
        grid.Children.Add(control);
    }

    private static Control Labeled(string label, Control control) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 12,
        VerticalAlignment = VerticalAlignment.Center,
        Children = { Text($"{label}：", 14, FontWeight.SemiBold), control }
    };

    private static string ManufacturerLabel(string manufacturer) => manufacturer switch
    {
        "Thorlabs" => "Thorlabs（索雷博）",
        "Edmund Optics" => "Edmund Optics（爱特蒙特）",
        "Daheng Optics" => "Daheng Optics（大恒光电）",
        "Newport" => "Newport",
        "Sigma Koki" => "Sigma Koki",
        _ => manufacturer
    };

    private static ComboBox Selector(double minWidth) => new() { MinWidth = minWidth, MinHeight = 34 };

    private static NumericUpDown PercentInput(decimal value) => new()
    {
        Minimum = 0,
        Maximum = 1000,
        Increment = 1,
        Value = value,
        MinWidth = 140,
        MinHeight = 34,
        FormatString = "0.###"
    };

    private static Button CommandButton(string icon, string text) => new()
    {
        Content = new LocalIconLabel(icon, text),
        MinHeight = 34,
        Padding = new Thickness(11, 5)
    };

    private static TextBlock Text(
        string text = "",
        double? size = null,
        FontWeight? weight = null) => new()
        {
            Text = text,
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

    private static string Number(double value) =>
        double.IsFinite(value)
            ? NumericDisplayFormatter.Format(value, CultureInfo.InvariantCulture)
            : "—";

    private sealed record MatchRow(int Rank, StockLensMatchResultDto Match)
    {
        public string Manufacturer => Match.Entry.Manufacturer;

        public string PartNumber => Match.Entry.PartNumber;

        public string EffectiveFocalLength => Number(Match.Entry.EffectiveFocalLength);

        public string EffectiveFocalLengthDeviation => $"{Number(Match.EffectiveFocalLengthDeviationPercent)}%";

        public string EntrancePupilDiameter => Number(Match.Entry.EntrancePupilDiameter);

        public string EntrancePupilDiameterDeviation => $"{Number(Match.EntrancePupilDiameterDeviationPercent)}%";

        public string Classification => $"{Match.Entry.ShapeCode}/{Match.Entry.SurfaceType}";

        public string Score => Number(Match.NormalizedScore);
    }
}
