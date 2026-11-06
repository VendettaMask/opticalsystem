using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Panels;

internal sealed partial class ToleranceResultsPanel : UserControl, IDisposable
{
    private readonly TolerancingPanel _source;
    private readonly Button _histogram;
    private readonly Button _yieldChart;
    private readonly Button _worst;
    private readonly TextBlock _summary = Text();
    private readonly TextBlock _empty = Text();
    private readonly DataGrid _configurationGrid;
    private readonly DataGrid _sensitivityGrid;
    private readonly DataGrid _inverseGrid;
    private readonly DataGrid _trialGrid;
    private readonly TabControl _tabs;
    private readonly TabItem _sensitivityTab;
    private readonly TabItem _inverseTab;
    private readonly TabItem _trialsTab;
    private readonly ComboBox _filter = new() { MinWidth = 150, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ContentControl _trialDetail = new();
    private TolerancingResultDto? _result;
    private TrialItem[] _trials = [];

    internal ToleranceResultsPanel(TolerancingPanel source, Action openReport, Action openHistogram, Action openYield)
    {
        _source = source;
        _histogram = Button("tolerance-histogram", "直方图", openHistogram);
        _yieldChart = Button("tolerance-yield", "良率", openYield);
        _worst = Button("tolerance-data-viewer", "定位最差试验", SelectWorst);
        var toolbar = new WrapPanel
        {
            ItemSpacing = 6, LineSpacing = 4,
            Children = { Button("tolerance-report", "公差报告", openReport), _histogram, _yieldChart, _worst }
        };
        _summary.Classes.Add("section-title");
        _empty.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        var header = new StackPanel { Spacing = 8, Children = { toolbar, _summary } };
        _configurationGrid = Grid(
            Column("验收对象", nameof(YieldItem.Name), 110),
            Column("良率", nameof(YieldItem.Yield), 110),
            Column("合格数", nameof(YieldItem.Passed), 76),
            Column("试验数", nameof(YieldItem.Trials), 76),
            Column("名义评价值", nameof(YieldItem.Nominal), 132));
        _sensitivityGrid = Grid(
            Column("公差操作数", "Row.Perturbation", 158),
            Column("评价值变化", "Row.DeltaMerit", 110, nameof(SensitivityItem.Change)),
            Column("负极限评价值", "Row.NegativeMerit", 112),
            Column("正极限评价值", "Row.PositiveMerit", 112),
            Column("最坏评价值", "Row.WorstMerit", 110));
        _inverseGrid = Grid(
            Column("公差操作数", nameof(InverseItem.Name), 150),
            Column("端点", nameof(InverseItem.End), 64),
            Column("原偏差", "Endpoint.OriginalTolerance", 100),
            Column("调整后偏差", "Endpoint.AdjustedTolerance", 112),
            Column("限制配置／视场", nameof(InverseItem.Limiting), 150),
            Column("验收余量", nameof(InverseItem.Margin), 100),
            Column("状态", nameof(InverseItem.Status), 110));
        _trialGrid = Grid(
            Column("试验", "Row.Trial", 54),
            Column("状态", nameof(TrialItem.Status), 72),
            Column("补偿后", nameof(TrialItem.AfterText), 98, nameof(TrialItem.After), fill: true),
            Column("补偿前", nameof(TrialItem.BeforeText), 98, nameof(TrialItem.Before), fill: true),
            Column("验收余量", nameof(TrialItem.Margin), 104, "Row.AcceptanceMargin", fill: true));
        _trialGrid.SelectionChanged += (_, _) => ShowTrialDetail();
        Avalonia.Automation.AutomationProperties.SetName(_filter, UiLocalization.Text("试验筛选"));
        _filter.ItemsSource = new[]
        {
            UiLocalization.Text("全部试验"), UiLocalization.Text("不合格试验"), UiLocalization.Text("计算失效试验")
        };
        _filter.SelectedIndex = 0;
        _filter.SelectionChanged += (_, _) => UpdateTrialFilter();

        _sensitivityTab = Tab("灵敏度", Page(Text("按最坏端点变化排序，计算失效项优先。"), _sensitivityGrid));
        _inverseTab = Tab("反求", _inverseGrid);
        _trialsTab = Tab("Monte Carlo 试验", Page(_filter, new ResponsiveTwoPaneGrid(
            _trialGrid, _trialDetail, "3*,8,4*", "2*,8,3*", 850)));
        _tabs = Tabs([Tab("结果总览", _configurationGrid), _sensitivityTab, _inverseTab, _trialsTab]);
        var body = new Grid { Children = { _tabs, _empty } };
        Content = Page(header, body);
        _source.ResultPresentationChanged += OnResultChanged;
        Refresh();
    }

    private void OnResultChanged(object? sender, EventArgs args) => Refresh();

    public void Dispose() => _source.ResultPresentationChanged -= OnResultChanged;

    private void Refresh()
    {
        _result = _source.CurrentResult;
        _tabs.IsVisible = _result is not null;
        _empty.IsVisible = _result is null;
        _summary.IsVisible = _result is not null;
        _empty.Text = string.IsNullOrWhiteSpace(_source.ResultMessage)
            ? UiLocalization.Text("运行公差分析后在此查看结果。") : _source.ResultMessage;
        var hasTrials = _result?.TrialRows.Count > 0;
        var chartUnavailable = UiLocalization.Text("先运行包含 Monte Carlo 的公差分析。");
        ControlAvailability.Set(_histogram, hasTrials, chartUnavailable);
        ControlAvailability.Set(_yieldChart, hasTrials, chartUnavailable);
        ControlAvailability.Set(_worst, _result?.WorstSample is not null, UiLocalization.Text("本次没有可定位的最差试验。"));
        _trialsTab.IsVisible = hasTrials;
        _sensitivityTab.IsVisible = _result?.SensitivityRows.Count > 0;
        _inverseTab.IsVisible = _result?.InverseRows is { Count: > 0 };
        _tabs.SelectedIndex = 0;
        if (_result is { } result)
        {
            var worst = result.WorstSample?.Recorded.Trial;
            _summary.Text = UiLocalization.Format("评价准则 {0}    试验 {1} 次    最差试验 {2}",
                CriterionLabel(_source.ResultCriterion), result.TrialRows.Count,
                worst?.ToString(CultureInfo.CurrentCulture) ?? Missing);
            _configurationGrid.ItemsSource = BuildYields(result);
            _sensitivityGrid.ItemsSource = result.SensitivityRows.Select(row => new SensitivityItem(row, Number(row.DeltaMerit)))
                .OrderByDescending(item => !item.Change.HasValue).ThenByDescending(item => Math.Abs(item.Change ?? 0)).ToArray();
            _inverseGrid.ItemsSource = result.InverseRows?.SelectMany(row => new[]
            {
                new InverseItem(row.Perturbation, UiLocalization.Text("负端"), row.Minimum),
                new InverseItem(row.Perturbation, UiLocalization.Text("正端"), row.Maximum)
            }).ToArray();
            _trials = result.TrialRows.Select(row => new TrialItem(row)).ToArray();
        }
        else
        {
            _configurationGrid.ItemsSource = null;
            _sensitivityGrid.ItemsSource = null;
            _inverseGrid.ItemsSource = null;
            _trials = [];
        }
        _filter.SelectedIndex = 0;
        UpdateTrialFilter();
    }

    private static IReadOnlyList<YieldItem> BuildYields(TolerancingResultDto result)
    {
        var trials = result.TrialRows;
        var hasAcceptance = trials.Any(row => row.Passed.HasValue);
        var rows = new List<YieldItem>
        {
            new(UiLocalization.Text("整机"), hasAcceptance ? result.Statistics?.Yield ?? Missing : Missing,
                hasAcceptance ? trials.Count(row => row.Passed == true) : null, trials.Count,
                result.Statistics?.Nominal ?? result.SensitivityStatistics?.Nominal ?? Missing)
        };
        foreach (var item in result.ConfigurationYields ?? [])
        {
            var nominal = result.NominalConfigurations?.FirstOrDefault(row => row.Configuration == item.Configuration);
            rows.Add(new(UiLocalization.Format("配置 {0}", item.Configuration),
                item.Yield?.ToString("P1", CultureInfo.CurrentCulture) ?? Missing,
                item.Yield.HasValue ? item.Passed : null, item.Trials, Format(nominal?.Criterion)));
        }
        return rows;
    }

    private void UpdateTrialFilter()
    {
        var rows = _filter.SelectedIndex switch
        {
            1 => _trials.Where(item => item.Row.Passed == false).ToArray(),
            2 => _trials.Where(item => item.Invalid).ToArray(),
            _ => _trials
        };
        _trialGrid.ItemsSource = rows;
        _trialGrid.SelectedItem = rows.FirstOrDefault();
        ShowTrialDetail();
    }

    private void SelectWorst()
    {
        if (_result?.WorstSample?.Recorded.Trial is not { } trial) return;
        _tabs.SelectedItem = _trialsTab;
        _filter.SelectedIndex = 0;
        var row = _trials.FirstOrDefault(item => item.Row.Trial == trial);
        _trialGrid.SelectedItem = row;
        if (row is not null) _trialGrid.ScrollIntoView(row, null);
    }

    private void ShowTrialDetail() => _trialDetail.Content = _trialGrid.SelectedItem is TrialItem item
        ? TrialDetail(item.Row, _source.ResultCriterion)
        : Text("没有符合筛选条件的试验。");

    private const string Missing = "—";
    private static double? Number(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
        && double.IsFinite(number) ? number : null;
    private static string Format(double? value) => value is null ? Missing : double.IsFinite(value.Value)
        ? value.Value.ToString("G7", CultureInfo.InvariantCulture) : UiLocalization.Text("失效");
    private static string CriterionLabel(ToleranceCriterion criterion) => UiLocalization.Text(criterion switch
    {
        ToleranceCriterion.Mtf => "指定频率 MTF",
        ToleranceCriterion.RmsWavefront => "RMS 波前误差 (waves)",
        ToleranceCriterion.SystemMerit => "系统评价函数",
        _ => "RMS 点列半径 (mm)"
    });
    private static string Status(bool? passed, bool invalid = false) => UiLocalization.Text(invalid ? "失效"
        : passed is true ? "合格" : passed is false ? "不合格" : "未设置限值");

    private static TextBlock Text(string text = "") => new() { Text = UiLocalization.Text(text), TextWrapping = TextWrapping.Wrap };
    private static Button Button(string icon, string label, Action click)
    {
        var button = new Button { Content = new LocalIconLabel(icon, label) };
        button.Click += (_, _) => click();
        return button;
    }
    private static TabItem Tab(string title, Control content) => new() { Header = UiLocalization.Text(title), Content = content };
    private static TabControl Tabs(IEnumerable<TabItem> items) => new()
    {
        ItemsSource = items,
        ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel()),
        SelectedIndex = 0
    };

    private static Control Page(Control header, Control body)
    {
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 8, Margin = new Thickness(10), Children = { header, body } };
        Avalonia.Controls.Grid.SetRow(body, 1);
        return layout;
    }

    private static DataGrid Grid(params DataGridColumn[] columns)
    {
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single };
        foreach (var column in columns) grid.Columns.Add(column);
        return grid;
    }

    private static DataGridTextColumn Column(string header, string property, int width, string? sort = null, bool fill = false) => new()
    {
        Header = UiLocalization.Text(header), Binding = new Binding(property), SortMemberPath = sort ?? property,
        MinWidth = fill ? width : 0,
        Width = fill ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(width)
    };

    private sealed record YieldItem(string Name, string Yield, int? Passed, int Trials, string Nominal);
    private sealed record SensitivityItem(TolerancingSensitivityRowDto Row, double? Change);
    private sealed record TrialItem(TolerancingTrialRowDto Row)
    {
        public double? Before => Number(Row.Merit);
        public double? After => Row.CriterionValue ?? Number(Row.CompensatedMerit);
        public string BeforeText => Before.HasValue ? Format(Before) : Row.Merit;
        public string AfterText => After.HasValue ? Format(After) : Row.CompensatedMerit;
        public bool Invalid => !double.IsFinite(After ?? double.NaN) || Row.AcceptanceMargin == double.NegativeInfinity;
        public string Status => ToleranceResultsPanel.Status(Row.Passed, Invalid);
        public string Margin => Format(Row.AcceptanceMargin);
    }
    private sealed record InverseItem(string Name, string End, TolerancingInverseEndpointDto Endpoint)
    {
        public string Limiting => Endpoint.LimitingConfiguration is { } configuration
            ? Endpoint.LimitingField is { } fieldNumber ? UiLocalization.Format("配置 {0} · 视场 {1}", configuration, fieldNumber)
                : UiLocalization.Format("配置 {0}", configuration) : Missing;
        public string Margin => Format(Endpoint.AcceptanceMargin);
        public string Status => UiLocalization.Text(Endpoint.Status switch
        {
            ToleranceInverseEndpointStatus.Tightened => "已收紧",
            ToleranceInverseEndpointStatus.ZeroRange => "收紧至零",
            ToleranceInverseEndpointStatus.UnsupportedPerturbation => "无法反求",
            _ => "原范围合格"
        });
    }
}
