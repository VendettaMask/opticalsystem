using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
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
    private CancellationTokenSource? _matchCancellation;
    private long _matchGeneration;
    private bool _disposed;

    internal Task MatchTask { get; private set; } = Task.CompletedTask;

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
        RefreshTargetSummary();
        _status.Text = "设置厂商和公差后点击“开始匹配”；目录扫描会在后台执行。";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _matchCancellation?.Cancel();
        _matchCancellation?.Dispose();
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

        var inputs = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 18,
            LineSpacing = 10,
            Children =
            {
                FieldBlock("面", _surfaceScope),
                FieldBlock("显示匹配结果", _maximumResults),
                FieldBlock("EFL 公差 (%)", _eflTolerance),
                FieldBlock("EPD 公差 (%)", _epdTolerance)
            }
        };

        var options = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 18,
            LineSpacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _matchShape, _matchDirection }
        };

        var run = CommandButton("search", "开始匹配");
        run.MinWidth = 120;
        run.Click += (_, _) => BeginMatch();
        var actions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 18,
            LineSpacing = 8,
            Children = { options, run }
        };

        return new StackPanel
        {
            Spacing = 12,
            Children =
            {
                Text("库存镜头匹配", 18, FontWeight.SemiBold),
                Labeled("生产厂商", manufacturers),
                inputs,
                Labeled("目标参数", _targetSummary),
                actions,
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

    private void BeginMatch()
    {
        if (_disposed)
        {
            return;
        }

        var snapshot = _documents.GetSnapshot();
        var targetEfl = snapshot.EffectiveFocalLength;
        var targetEpd = snapshot.EntrancePupilDiameter;
        RefreshTargetSummary(snapshot);
        var generation = Interlocked.Increment(ref _matchGeneration);
        _matchCancellation?.Cancel();
        _matchCancellation?.Dispose();
        _matchCancellation = new CancellationTokenSource();
        if (!double.IsFinite(targetEfl) || Math.Abs(targetEfl) <= 1e-12
            || !double.IsFinite(targetEpd) || targetEpd <= 0)
        {
            MatchTask = Task.CompletedTask;
            _results.ItemsSource = Array.Empty<MatchRow>();
            _productPage.IsEnabled = false;
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
        _results.ItemsSource = Array.Empty<MatchRow>();
        _productPage.IsEnabled = false;
        _status.Text = "正在后台扫描库存镜头目录…";
        var cancellationToken = _matchCancellation.Token;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        MatchTask = completion.Task;
        _ = RunMatchAsync(request, generation, cancellationToken, completion);
    }

    private async Task RunMatchAsync(
        StockLensMatchRequestDto request,
        long generation,
        CancellationToken cancellationToken,
        TaskCompletionSource completion)
    {
        MatchRow[] rows;
        try
        {
            rows = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var matches = StockLensMatcher.Match(_lenses.GetCommercialLenses(), request);
                cancellationToken.ThrowIfCancellationRequested();
                return matches.Select((match, index) => new MatchRow(index + 1, match)).ToArray();
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            completion.TrySetResult();
            return;
        }
        catch (Exception exception)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_disposed || generation != Interlocked.Read(ref _matchGeneration))
                {
                    completion.TrySetResult();
                    return;
                }

                try
                {
                    _results.ItemsSource = Array.Empty<MatchRow>();
                    _productPage.IsEnabled = false;
                    _status.Text = $"库存镜头匹配失败：{exception.Message}";
                }
                finally
                {
                    completion.TrySetResult();
                }
            });
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || generation != Interlocked.Read(ref _matchGeneration))
            {
                completion.TrySetResult();
                return;
            }

            try
            {
                ApplyMatchRows(rows);
            }
            finally
            {
                completion.TrySetResult();
            }
        });
    }

    private void ApplyMatchRows(IReadOnlyList<MatchRow> rows)
    {
        _results.ItemsSource = rows;
        _results.SelectedItem = rows.FirstOrDefault();
        _status.Text = rows.Count == 0
            ? "没有候选满足当前厂商、方向和公差条件。"
            : $"找到 {rows.Count} 个候选；分数越小越接近当前系统。";
        UpdateSelectionActions();
    }

    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs args)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            _matchCancellation?.Cancel();
            Interlocked.Increment(ref _matchGeneration);
            _results.ItemsSource = Array.Empty<MatchRow>();
            _productPage.IsEnabled = false;
            RefreshTargetSummary();
            _status.Text = "当前系统已改变，请重新执行匹配。";
        });
    }

    private void RefreshTargetSummary(OpticalDocumentSnapshot? snapshot = null)
    {
        snapshot ??= _documents.GetSnapshot();
        _targetSummary.Text =
            $"EFL {Number(snapshot.EffectiveFocalLength)} mm · EPD {Number(snapshot.EntrancePupilDiameter)} mm";
    }

    private void UpdateSelectionActions()
    {
        var entry = (_results.SelectedItem as MatchRow)?.Match.Entry;
        _productPage.IsEnabled = entry is not null
            && Uri.IsWellFormedUriString(entry.ProductUrl, UriKind.Absolute);
    }

    private async Task OpenSelectedProductPageAsync()
    {
        try
        {
            var value = (_results.SelectedItem as MatchRow)?.Match.Entry.ProductUrl;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                _status.Text = "当前候选没有可打开的厂商页面地址。";
                return;
            }

            var launcher = TopLevel.GetTopLevel(this)?.Launcher;
            if (launcher is null || !await launcher.LaunchUriAsync(uri))
            {
                _status.Text = $"无法打开厂商页面：{uri}";
            }
        }
        catch (Exception exception)
        {
            _status.Text = $"打开厂商页面失败：{exception.Message}";
        }
    }

    private int ParseMaximumResults() =>
        int.TryParse(_maximumResults.SelectedItem as string, out var value) ? value : 5;

    private static Control Labeled(string label, Control control) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 12,
        VerticalAlignment = VerticalAlignment.Center,
        Children = { Text($"{label}：", 14, FontWeight.SemiBold), control }
    };

    private static Control FieldBlock(string label, Control control) => new StackPanel
    {
        Spacing = 4,
        MinWidth = 150,
        Children = { Text(label, 13, FontWeight.SemiBold), control }
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
