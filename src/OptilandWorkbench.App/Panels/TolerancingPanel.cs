using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.ViewModels;

namespace OptilandWorkbench.App.Panels;

public sealed class TolerancingPanel : UserControl, IDisposable
{
    private readonly IPrescriptionService _prescription;
    private readonly ITolerancingService _tolerancing;
    private readonly IWorkspaceEventStream _events;
    private readonly ObservableCollection<ToleranceOperandEditorRow> _operands = new();
    private readonly DataGrid _operandGrid = CreateGrid();
    private readonly DataGrid _sensitivityGrid = CreateGrid();
    private readonly DataGrid _monteCarloGrid = CreateGrid();
    private readonly ComboBox _kindPicker = new() { MinWidth = 180 };
    private readonly ComboBox _surfacePicker = new() { MinWidth = 180 };
    private readonly ComboBox _distributionPicker = Picker(0, "正态（±值按 3σ）", "均匀");
    private readonly CheckBox _enabled = new() { Content = "启用", IsChecked = true };
    private readonly NumericUpDown _minimum = Number(-0.1m, -1_000_000, 1_000_000, 0.01m, 150);
    private readonly NumericUpDown _maximum = Number(0.1m, -1_000_000, 1_000_000, 0.01m, 150);
    private readonly TextBox _comment = new() { MinWidth = 180 };
    private readonly ComboBox _criterion = Picker(0, "RMS 点列半径", "RMS 波前");
    private readonly NumericUpDown _trials = Number(1000, 1, 10_000, 100, 96);
    private readonly NumericUpDown _seed = Number(1234, 0, 2_000_000_000, 1, 104);
    private readonly NumericUpDown _compensationIterations = Number(20, 0, 500, 5, 96);
    private readonly NumericUpDown _yieldLimit = Number(0, 0, 1_000_000, 0.01m, 110);
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly OperationStatusBar _operationStatus = new();
    private readonly TextBlock _statistics = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12) };
    private CancellationTokenSource? _runCancellation;
    private TolerancingResultDto? _lastResult;
    private ToleranceOperandEditorRow? _selectedOperandForEditor;
    private int _generation;
    private bool _updatingEditor;
    private bool _disposed;

    public TolerancingPanel(
        IPrescriptionService prescription,
        ITolerancingService tolerancing,
        IWorkspaceEventStream events)
    {
        _prescription = prescription;
        _tolerancing = tolerancing;
        _events = events;
        ConfigureGrids();
        ConfigureEditor();

        var wizardButton = CommandButton("sparkles", "公差向导");
        wizardButton.Click += async (_, _) => await ShowWizardAsync();
        var addButton = CommandButton("plus", "添加");
        addButton.Click += (_, _) => AddOperand();
        var removeButton = CommandButton("trash", "删除");
        removeButton.Click += (_, _) => RemoveSelected();
        var validateButton = CommandButton("check", "验证");
        validateButton.Click += (_, _) => Validate(showSuccess: true);
        var saveButton = CommandButton("save", "保存公差");
        saveButton.Click += async (_, _) => await SaveToleranceFileAsync();
        var loadButton = CommandButton("folder-open", "载入公差");
        loadButton.Click += async (_, _) => await LoadToleranceFileAsync();
        var reportButton = CommandButton("file-text", "导出报告");
        reportButton.Click += async (_, _) => await ExportReportAsync();
        var runButton = CommandButton("play", "运行公差");
        runButton.Click += async (_, _) => await RunAsync();

        var toolbar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
            Children =
            {
                wizardButton, addButton, removeButton, validateButton,
                saveButton, loadButton, reportButton, runButton, _operationStatus
            }
        };

        var editor = BuildOperandEditor();
        var editorLayout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("3*,320"),
            ColumnSpacing = 10,
            Children = { _operandGrid, editor }
        };
        Grid.SetColumn(editor, 1);

        var runSettings = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8),
            Children =
            {
                Label("评价标准"), _criterion,
                Label("Monte Carlo 次数"), _trials,
                Label("随机种子"), _seed,
                Label("补偿迭代"), _compensationIterations,
                Label("合格上限（0=关闭）"), _yieldLimit
            }
        };

        var resultTabs = new TabControl
        {
            MinHeight = 250,
            ItemsSource = new object[]
            {
                new TabItem { Header = "灵敏度", Content = _sensitivityGrid },
                new TabItem { Header = "Monte Carlo", Content = _monteCarloGrid },
                new TabItem { Header = "统计摘要", Content = new ScrollViewer { Content = _statistics } }
            }
        };

        var content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,2*,Auto,2*,Auto"),
            RowSpacing = 4,
            Margin = new Thickness(12)
        };
        content.Children.Add(toolbar);
        Grid.SetRow(editorLayout, 1);
        content.Children.Add(editorLayout);
        Grid.SetRow(runSettings, 2);
        content.Children.Add(runSettings);
        Grid.SetRow(resultTabs, 3);
        content.Children.Add(resultTabs);
        var summaryBorder = new Border
        {
            Padding = new Thickness(10, 8),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = _summary
        };
        summaryBorder.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        Grid.SetRow(summaryBorder, 4);
        content.Children.Add(summaryBorder);
        Content = content;

        _events.Changed += OnWorkspaceChanged;
        RefreshSurfaces();
        AddOperand();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _generation++;
        _events.Changed -= OnWorkspaceChanged;
        TrackSelectedOperand(null);
        _runCancellation?.Cancel();
        _runCancellation?.Dispose();
        _operationStatus.Dispose();
    }

    private void ConfigureGrids()
    {
        _operandGrid.SelectionMode = DataGridSelectionMode.Single;
        _operandGrid.Columns.Add(new DataGridTextColumn { Header = "#", Binding = new Binding(nameof(ToleranceOperandEditorRow.Index)), Width = Pixels(46), IsReadOnly = true });
        _operandGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "启用", Binding = TwoWay(nameof(ToleranceOperandEditorRow.Enabled)), Width = Pixels(56) });
        _operandGrid.Columns.Add(new DataGridTextColumn { Header = "类型", Binding = TwoWay(nameof(ToleranceOperandEditorRow.Code)), Width = Pixels(82) });
        _operandGrid.Columns.Add(new DataGridTextColumn { Header = "表面", Binding = TwoWay(nameof(ToleranceOperandEditorRow.SurfaceNumber)), Width = Pixels(62) });
        _operandGrid.Columns.Add(new DataGridTextColumn { Header = "最小偏差", Binding = TwoWay(nameof(ToleranceOperandEditorRow.Minimum)), Width = Pixels(110) });
        _operandGrid.Columns.Add(new DataGridTextColumn { Header = "最大偏差", Binding = TwoWay(nameof(ToleranceOperandEditorRow.Maximum)), Width = Pixels(110) });
        _operandGrid.Columns.Add(new DataGridTextColumn { Header = "统计", Binding = TwoWay(nameof(ToleranceOperandEditorRow.DistributionText)), Width = Pixels(90) });
        _operandGrid.Columns.Add(new DataGridTextColumn { Header = "备注", Binding = TwoWay(nameof(ToleranceOperandEditorRow.Comment)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _operandGrid.SelectionChanged += (_, _) => LoadSelectedOperand();
        _operandGrid.ItemsSource = _operands;

        _sensitivityGrid.Columns.Add(new DataGridTextColumn { Header = "公差操作数", Binding = new Binding(nameof(TolerancingSensitivityRowDto.Perturbation)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _sensitivityGrid.Columns.Add(new DataGridTextColumn { Header = "负极限评价值", Binding = new Binding(nameof(TolerancingSensitivityRowDto.NegativeMerit)), Width = Pixels(140) });
        _sensitivityGrid.Columns.Add(new DataGridTextColumn { Header = "正极限评价值", Binding = new Binding(nameof(TolerancingSensitivityRowDto.PositiveMerit)), Width = Pixels(140) });
        _sensitivityGrid.Columns.Add(new DataGridTextColumn { Header = "最坏评价值", Binding = new Binding(nameof(TolerancingSensitivityRowDto.WorstMerit)), Width = Pixels(130) });
        _sensitivityGrid.Columns.Add(new DataGridTextColumn { Header = "评价值变化", Binding = new Binding(nameof(TolerancingSensitivityRowDto.DeltaMerit)), Width = Pixels(130) });

        _monteCarloGrid.Columns.Add(new DataGridTextColumn { Header = "试验", Binding = new Binding(nameof(TolerancingTrialRowDto.Trial)), Width = Pixels(72) });
        _monteCarloGrid.Columns.Add(new DataGridTextColumn { Header = "补偿前评价值", Binding = new Binding(nameof(TolerancingTrialRowDto.Merit)), Width = Pixels(150) });
        _monteCarloGrid.Columns.Add(new DataGridTextColumn { Header = "补偿后评价值", Binding = new Binding(nameof(TolerancingTrialRowDto.CompensatedMerit)), Width = Pixels(150) });
        _monteCarloGrid.Columns.Add(new DataGridTextColumn { Header = "相对名义变化", Binding = new Binding(nameof(TolerancingTrialRowDto.Degradation)), Width = Pixels(140) });
    }

    private void ConfigureEditor()
    {
        _kindPicker.ItemsSource = Enum.GetValues<ToleranceOperandKind>()
            .Select(kind => new ToleranceKindChoice(kind, ToleranceOperandEditorRow.CodeFor(kind), KindName(kind)))
            .ToArray();
        _kindPicker.DisplayMemberBinding = new Binding(nameof(ToleranceKindChoice.Display));
        _kindPicker.SelectedIndex = 0;
    }

    private Border BuildOperandEditor()
    {
        var applyButton = new Button
        {
            Content = "应用到当前行",
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 120
        };
        applyButton.Click += (_, _) => ApplySelectedOperand();
        var panel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "操作数属性", FontSize = DisplayTypography.SectionTitle, FontWeight = FontWeight.SemiBold },
                _enabled,
                Labeled("类型", _kindPicker),
                Labeled("表面", _surfacePicker),
                Labeled("最小偏差", _minimum),
                Labeled("最大偏差", _maximum),
                Labeled("统计分布", _distributionPicker),
                Labeled("备注", _comment),
                applyButton
            }
        };
        var border = new Border
        {
            Padding = new Thickness(12),
            Child = panel
        };
        SettingsPanelChrome.ApplySurfaceCardStyle(border, shadow: false);
        return border;
    }

    private async Task ShowWizardAsync()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var settings = await new ToleranceWizardWindow(_prescription).ShowDialog<ToleranceWizardSettingsDto?>(owner);
        if (settings is null)
        {
            return;
        }

        var generated = _tolerancing.GenerateWizard(settings);
        if (settings.ReplaceExisting)
        {
            _operands.Clear();
        }

        foreach (var operand in generated)
        {
            _operands.Add(new ToleranceOperandEditorRow(operand with { Index = _operands.Count + 1 }));
        }

        Renumber();
        _operandGrid.SelectedItem = _operands.FirstOrDefault();
        _summary.Text = $"公差向导已生成 {generated.Count} 个操作数。";
    }

    private void AddOperand()
    {
        var surface = (_surfacePicker.SelectedItem as SurfaceEditorRow)?.Number
            ?? Math.Min(1, Math.Max(0, _prescription.GetSurfaces().Count - 1));
        var row = new ToleranceOperandEditorRow(new ToleranceOperandDto(
            _operands.Count + 1,
            true,
            ToleranceOperandKind.Thickness,
            surface,
            -0.05,
            0.05,
            ToleranceDistribution.Normal,
            "手动添加"));
        _operands.Add(row);
        _operandGrid.SelectedItem = row;
    }

    private void RemoveSelected()
    {
        if (_operandGrid.SelectedItem is not ToleranceOperandEditorRow row)
        {
            return;
        }

        _operands.Remove(row);
        Renumber();
        _operandGrid.SelectedItem = _operands.FirstOrDefault();
    }

    private void LoadSelectedOperand()
    {
        if (_updatingEditor)
        {
            return;
        }

        if (_operandGrid.SelectedItem is not ToleranceOperandEditorRow row)
        {
            TrackSelectedOperand(null);
            return;
        }

        TrackSelectedOperand(row);
        LoadOperandIntoEditor(row);
    }

    private void TrackSelectedOperand(ToleranceOperandEditorRow? row)
    {
        if (ReferenceEquals(_selectedOperandForEditor, row))
        {
            return;
        }

        if (_selectedOperandForEditor is not null)
        {
            _selectedOperandForEditor.PropertyChanged -= OnSelectedOperandChanged;
        }

        _selectedOperandForEditor = row;
        if (_selectedOperandForEditor is not null)
        {
            _selectedOperandForEditor.PropertyChanged += OnSelectedOperandChanged;
        }
    }

    private void OnSelectedOperandChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_updatingEditor
            || _operandGrid.SelectedItem is not ToleranceOperandEditorRow row
            || !ReferenceEquals(sender, row))
        {
            return;
        }

        LoadOperandIntoEditor(row);
    }

    private void LoadOperandIntoEditor(ToleranceOperandEditorRow row)
    {
        _updatingEditor = true;
        try
        {
            _enabled.IsChecked = row.Enabled;
            _kindPicker.SelectedItem = _kindPicker.ItemsSource?
                .Cast<ToleranceKindChoice>()
                .FirstOrDefault(item => item.Kind == row.Kind);
            _surfacePicker.SelectedItem = _surfacePicker.ItemsSource?
                .Cast<SurfaceEditorRow>()
                .FirstOrDefault(item => item.Number == row.SurfaceNumber);
            _minimum.Value = ToDecimal(row.Minimum);
            _maximum.Value = ToDecimal(row.Maximum);
            _distributionPicker.SelectedIndex = row.Distribution == ToleranceDistribution.Uniform ? 1 : 0;
            _comment.Text = row.Comment;
        }
        finally
        {
            _updatingEditor = false;
        }
    }

    private void ApplySelectedOperand()
    {
        if (_operandGrid.SelectedItem is not ToleranceOperandEditorRow selected
            || _kindPicker.SelectedItem is not ToleranceKindChoice kind
            || _surfacePicker.SelectedItem is not SurfaceEditorRow surface)
        {
            return;
        }

        _updatingEditor = true;
        try
        {
            selected.Enabled = _enabled.IsChecked == true;
            selected.Kind = kind.Kind;
            selected.SurfaceNumber = surface.Number;
            selected.Minimum = DoubleValue(_minimum, -0.1);
            selected.Maximum = DoubleValue(_maximum, 0.1);
            selected.Distribution = _distributionPicker.SelectedIndex == 1
                ? ToleranceDistribution.Uniform
                : ToleranceDistribution.Normal;
            selected.Comment = _comment.Text?.Trim() ?? string.Empty;
        }
        finally
        {
            _updatingEditor = false;
        }

        LoadOperandIntoEditor(selected);
        Validate(showSuccess: false);
    }

    private bool Validate(bool showSuccess)
    {
        var result = _tolerancing.ValidateOperands(_operands.Select(row => row.ToDto()).ToArray());
        if (!result.IsValid)
        {
            _operationStatus.MarkFailed("公差数据验证失败");
            _summary.Text = "公差数据验证失败：" + Environment.NewLine + string.Join(Environment.NewLine, result.Messages);
            return false;
        }

        if (showSuccess)
        {
            _operationStatus.MarkSynced("验证通过");
            _summary.Text = $"验证通过：{_operands.Count(row => row.Enabled && row.Kind != ToleranceOperandKind.Compensator)} 个公差，"
                + $"{_operands.Count(row => row.Enabled && row.Kind == ToleranceOperandKind.Compensator)} 个补偿器。";
        }

        return true;
    }

    private async Task RunAsync()
    {
        ApplySelectedOperand();
        if (!Validate(showSuccess: false))
        {
            return;
        }

        _runCancellation?.Cancel();
        _runCancellation?.Dispose();
        _runCancellation = new CancellationTokenSource();
        var cancellationToken = _runCancellation.Token;
        var generation = ++_generation;
        _operationStatus.Start("正在运行公差分析…", () => _runCancellation?.Cancel());
        _summary.Text = "正在运行灵敏度和 Monte Carlo 公差分析…";
        try
        {
            var rows = _operands.Select(row => row.ToDto()).ToArray();
            var firstSurface = rows.FirstOrDefault(row => row.Enabled)?.SurfaceNumber ?? 1;
            var result = await _tolerancing.RunAsync(new TolerancingRequestDto(
                firstSurface,
                0,
                0,
                IntValue(_trials, 1000),
                IntValue(_seed, 1234),
                IntValue(_compensationIterations, 20),
                rows,
                _criterion.SelectedIndex == 1 ? ToleranceCriterion.RmsWavefront : ToleranceCriterion.RmsSpotRadius,
                DoubleValue(_yieldLimit, 0)), cancellationToken);
            if (_disposed || cancellationToken.IsCancellationRequested || generation != _generation)
            {
                return;
            }

            _sensitivityGrid.ItemsSource = result.SensitivityRows;
            _monteCarloGrid.ItemsSource = result.TrialRows;
            _statistics.Text = FormatStatistics(result.Statistics);
            _summary.Text = $"{result.Summary}    {result.Details}";
            _operationStatus.MarkSynced("公差分析完成");
            _lastResult = result;
        }
        catch (OperationCanceledException)
        {
            if (!_disposed && generation == _generation)
            {
                _operationStatus.MarkStale("公差分析已取消");
            }
        }
        catch (Exception exception)
        {
            if (!_disposed && generation == _generation)
            {
                _operationStatus.MarkFailed($"公差分析失败：{exception.Message}");
                _summary.Text = $"公差分析失败：{exception.Message}";
            }
        }
    }

    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs args) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed)
            {
                RefreshSurfaces();
            }
        });

    private async Task SaveToleranceFileAsync()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null)
            {
                return;
            }

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "保存公差数据",
                SuggestedFileName = "tolerances.startol.json",
                DefaultExtension = "json",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("STAR 公差数据") { Patterns = new[] { "*.startol.json", "*.json" } }
                }
            });
            if (file is null)
            {
                return;
            }

            var document = new ToleranceFileDto(
                SchemaVersion: 1,
                _operands.Select(row => row.ToDto()).ToArray(),
                _criterion.SelectedIndex == 1 ? ToleranceCriterion.RmsWavefront : ToleranceCriterion.RmsSpotRadius,
                IntValue(_trials, 1000),
                IntValue(_seed, 1234),
                IntValue(_compensationIterations, 20),
                DoubleValue(_yieldLimit, 0));
            var json = System.Text.Json.JsonSerializer.Serialize(
                document,
                new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                });
            await File.WriteAllTextAsync(file.Path.LocalPath, json);
            _summary.Text = "公差数据已保存。";
        }
        catch (Exception exception)
        {
            _summary.Text = $"保存公差数据失败：{exception.Message}";
        }
    }

    private async Task LoadToleranceFileAsync()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null)
            {
                return;
            }

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "载入公差数据",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("STAR 公差数据") { Patterns = new[] { "*.startol.json", "*.json" } }
                }
            });
            if (files.Count == 0)
            {
                return;
            }

            var json = await File.ReadAllTextAsync(files[0].Path.LocalPath);
            var document = System.Text.Json.JsonSerializer.Deserialize<ToleranceFileDto>(
                json,
                new System.Text.Json.JsonSerializerOptions
                {
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                });
            if (document is null || document.SchemaVersion != 1)
            {
                throw new InvalidDataException("不支持的公差文件版本。");
            }

            var validation = _tolerancing.ValidateOperands(document.Operands);
            if (!validation.IsValid)
            {
                throw new InvalidDataException(string.Join("；", validation.Messages));
            }

            _operands.Clear();
            foreach (var operand in document.Operands)
            {
                _operands.Add(new ToleranceOperandEditorRow(operand));
            }

            Renumber();
            _criterion.SelectedIndex = document.Criterion == ToleranceCriterion.RmsWavefront ? 1 : 0;
            _trials.Value = document.Trials;
            _seed.Value = document.Seed;
            _compensationIterations.Value = document.CompensationIterations;
            _yieldLimit.Value = ToDecimal(document.YieldLimit);
            _operandGrid.SelectedItem = _operands.FirstOrDefault();
            _summary.Text = $"已载入 {_operands.Count} 个公差操作数。";
        }
        catch (Exception exception)
        {
            _summary.Text = $"载入公差数据失败：{exception.Message}";
        }
    }

    private async Task ExportReportAsync()
    {
        if (_lastResult is null)
        {
            _summary.Text = "请先运行公差分析。";
            return;
        }

        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null)
            {
                return;
            }

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出公差分析报告",
                SuggestedFileName = "tolerance-report.txt",
                DefaultExtension = "txt",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("文本报告") { Patterns = new[] { "*.txt" } }
                }
            });
            if (file is null)
            {
                return;
            }

            var lines = new List<string>
            {
                _lastResult.Summary,
                _lastResult.Details,
                string.Empty,
                "统计摘要",
                FormatStatistics(_lastResult.Statistics),
                string.Empty,
                "灵敏度",
                "操作数\t负极限\t正极限\t最坏值\t变化"
            };
            lines.AddRange(_lastResult.SensitivityRows.Select(row =>
                $"{row.Perturbation}\t{row.NegativeMerit}\t{row.PositiveMerit}\t{row.WorstMerit}\t{row.DeltaMerit}"));
            lines.Add(string.Empty);
            lines.Add("Monte Carlo");
            lines.Add("试验\t补偿前\t补偿后\t相对名义变化");
            lines.AddRange(_lastResult.TrialRows.Select(row =>
                $"{row.Trial}\t{row.Merit}\t{row.CompensatedMerit}\t{row.Degradation}"));
            await File.WriteAllLinesAsync(file.Path.LocalPath, lines);
            _summary.Text = "公差分析报告已导出。";
        }
        catch (Exception exception)
        {
            _summary.Text = $"导出公差报告失败：{exception.Message}";
        }
    }

    private void RefreshSurfaces()
    {
        var selected = (_surfacePicker.SelectedItem as SurfaceEditorRow)?.Number;
        var surfaces = _prescription.GetSurfaces().Select(surface => new SurfaceEditorRow(surface)).ToArray();
        _surfacePicker.ItemsSource = surfaces;
        _surfacePicker.SelectedItem = surfaces.FirstOrDefault(surface => surface.Number == selected)
            ?? surfaces.ElementAtOrDefault(Math.Min(1, Math.Max(0, surfaces.Length - 1)));
    }

    private void Renumber()
    {
        for (var index = 0; index < _operands.Count; index++)
        {
            _operands[index].Index = index + 1;
        }

        _operandGrid.ItemsSource = null;
        _operandGrid.ItemsSource = _operands;
    }

    private static string FormatStatistics(TolerancingStatisticsDto? statistics)
    {
        if (statistics is null)
        {
            return "尚未运行公差分析。";
        }

        return $"名义评价：{statistics.Nominal}{Environment.NewLine}"
            + $"Monte Carlo 平均值：{statistics.Mean}{Environment.NewLine}"
            + $"标准差：{statistics.StandardDeviation}{Environment.NewLine}"
            + $"最小值 / 最大值：{statistics.Minimum} / {statistics.Maximum}{Environment.NewLine}"
            + $"P50：{statistics.Percentile50}{Environment.NewLine}"
            + $"P90：{statistics.Percentile90}{Environment.NewLine}"
            + $"P95：{statistics.Percentile95}{Environment.NewLine}"
            + $"预计合格率：{statistics.Yield}";
    }

    private static string KindName(ToleranceOperandKind kind) => kind switch
    {
        ToleranceOperandKind.Radius => "曲率半径",
        ToleranceOperandKind.Thickness => "厚度/间隔",
        ToleranceOperandKind.Conic => "圆锥系数",
        ToleranceOperandKind.DecenterX => "表面 X 偏心",
        ToleranceOperandKind.DecenterY => "表面 Y 偏心",
        ToleranceOperandKind.TiltX => "表面 X 倾斜",
        ToleranceOperandKind.TiltY => "表面 Y 倾斜",
        ToleranceOperandKind.RefractiveIndex => "折射率",
        ToleranceOperandKind.AbbeNumber => "阿贝数",
        ToleranceOperandKind.Compensator => "补偿器",
        _ => kind.ToString()
    };

    private static Button CommandButton(string icon, string text) => new()
    {
        Content = new LocalIconLabel(icon, text),
        MinWidth = 94,
        Margin = new Thickness(0, 0, 6, 0)
    };

    private static NumericUpDown Number(decimal value, decimal minimum, decimal maximum, decimal increment, double width) => new()
    {
        Value = value,
        Minimum = minimum,
        Maximum = maximum,
        Increment = increment,
        Width = width,
        ShowButtonSpinner = false
    };

    private static ComboBox Picker(int selectedIndex, params string[] values) => new()
    {
        ItemsSource = values,
        SelectedIndex = selectedIndex,
        MinWidth = 145
    };

    private static DataGrid CreateGrid()
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserReorderColumns = true,
            CanUserResizeColumns = true,
            GridLinesVisibility = DataGridGridLinesVisibility.All,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            MinHeight = 180
        };
        grid.BindThemeResource(DataGrid.RowBackgroundProperty, ThemeResourceBindings.Surface);
        return grid;
    }

    private static DataGridLength Pixels(double value) =>
        new(value, DataGridLengthUnitType.Pixel);

    private static Binding TwoWay(string propertyName) => new(propertyName)
    {
        Mode = BindingMode.TwoWay
    };

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(10, 0, 4, 0)
    };

    private static StackPanel Labeled(string label, Control input) => new()
    {
        Spacing = 3,
        Children =
        {
            new TextBlock { Text = label, FontWeight = FontWeight.SemiBold },
            input
        }
    };

    private static decimal ToDecimal(double value) =>
        (decimal)Math.Clamp(value, -1_000_000, 1_000_000);

    private static double DoubleValue(NumericUpDown input, double fallback) =>
        input.Value.HasValue ? decimal.ToDouble(input.Value.Value) : fallback;

    private static int IntValue(NumericUpDown input, int fallback) =>
        input.Value.HasValue ? decimal.ToInt32(input.Value.Value) : fallback;

    private sealed record ToleranceKindChoice(ToleranceOperandKind Kind, string Code, string Name)
    {
        public string Display => $"{Code} — {Name}";
    }

    private sealed record ToleranceFileDto(
        int SchemaVersion,
        IReadOnlyList<ToleranceOperandDto> Operands,
        ToleranceCriterion Criterion,
        int Trials,
        int Seed,
        int CompensationIterations,
        double YieldLimit);
}

public sealed class ToleranceOperandEditorRow : INotifyPropertyChanged
{
    private int _index;
    private bool _enabled;
    private ToleranceOperandKind _kind;
    private int _surfaceNumber;
    private double _minimum;
    private double _maximum;
    private ToleranceDistribution _distribution;
    private string _comment = string.Empty;

    public ToleranceOperandEditorRow(ToleranceOperandDto source)
    {
        Index = source.Index;
        Enabled = source.Enabled;
        Kind = source.Kind;
        SurfaceNumber = source.SurfaceNumber;
        Minimum = source.Minimum;
        Maximum = source.Maximum;
        Distribution = source.Distribution;
        Comment = source.Comment;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Index
    {
        get => _index;
        set => SetField(ref _index, value, nameof(Index));
    }

    public bool Enabled
    {
        get => _enabled;
        set => SetField(ref _enabled, value, nameof(Enabled));
    }

    public ToleranceOperandKind Kind
    {
        get => _kind;
        set
        {
            if (SetField(ref _kind, value, nameof(Kind)))
            {
                OnPropertyChanged(nameof(Code));
            }
        }
    }

    public string Code
    {
        get => CodeFor(Kind);
        set
        {
            if (TryParseCode(value, out var kind))
            {
                Kind = kind;
                return;
            }

            OnPropertyChanged(nameof(Code));
        }
    }

    public int SurfaceNumber
    {
        get => _surfaceNumber;
        set => SetField(ref _surfaceNumber, value, nameof(SurfaceNumber));
    }

    public double Minimum
    {
        get => _minimum;
        set => SetField(ref _minimum, value, nameof(Minimum));
    }

    public double Maximum
    {
        get => _maximum;
        set => SetField(ref _maximum, value, nameof(Maximum));
    }

    public ToleranceDistribution Distribution
    {
        get => _distribution;
        set
        {
            if (SetField(ref _distribution, value, nameof(Distribution)))
            {
                OnPropertyChanged(nameof(DistributionText));
            }
        }
    }

    public string DistributionText
    {
        get => Distribution == ToleranceDistribution.Normal ? "正态" : "均匀";
        set
        {
            if (TryParseDistribution(value, out var distribution))
            {
                Distribution = distribution;
                return;
            }

            OnPropertyChanged(nameof(DistributionText));
        }
    }

    public string Comment
    {
        get => _comment;
        set => SetField(ref _comment, value ?? string.Empty, nameof(Comment));
    }

    public ToleranceOperandDto ToDto() => new(
        Index,
        Enabled,
        Kind,
        SurfaceNumber,
        Minimum,
        Maximum,
        Distribution,
        Comment);

    public static string CodeFor(ToleranceOperandKind kind) => kind switch
    {
        ToleranceOperandKind.Radius => "TRAD",
        ToleranceOperandKind.Thickness => "TTHI",
        ToleranceOperandKind.Conic => "TCON",
        ToleranceOperandKind.DecenterX => "TSDX",
        ToleranceOperandKind.DecenterY => "TSDY",
        ToleranceOperandKind.TiltX => "TSTX",
        ToleranceOperandKind.TiltY => "TSTY",
        ToleranceOperandKind.RefractiveIndex => "TIND",
        ToleranceOperandKind.AbbeNumber => "TABB",
        ToleranceOperandKind.Compensator => "COMP",
        _ => kind.ToString().ToUpperInvariant()
    };

    private static bool TryParseCode(string? text, out ToleranceOperandKind kind)
    {
        switch ((text ?? string.Empty).Trim().ToUpperInvariant())
        {
            case "TRAD":
                kind = ToleranceOperandKind.Radius;
                return true;
            case "TTHI":
                kind = ToleranceOperandKind.Thickness;
                return true;
            case "TCON":
                kind = ToleranceOperandKind.Conic;
                return true;
            case "TSDX":
                kind = ToleranceOperandKind.DecenterX;
                return true;
            case "TSDY":
                kind = ToleranceOperandKind.DecenterY;
                return true;
            case "TSTX":
                kind = ToleranceOperandKind.TiltX;
                return true;
            case "TSTY":
                kind = ToleranceOperandKind.TiltY;
                return true;
            case "TIND":
                kind = ToleranceOperandKind.RefractiveIndex;
                return true;
            case "TABB":
                kind = ToleranceOperandKind.AbbeNumber;
                return true;
            case "COMP":
                kind = ToleranceOperandKind.Compensator;
                return true;
        }

        return Enum.TryParse(text, ignoreCase: true, out kind);
    }

    private static bool TryParseDistribution(string? text, out ToleranceDistribution distribution)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Contains("均", StringComparison.Ordinal)
            || normalized.Equals("uniform", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("u", StringComparison.OrdinalIgnoreCase))
        {
            distribution = ToleranceDistribution.Uniform;
            return true;
        }

        if (normalized.Contains("正", StringComparison.Ordinal)
            || normalized.Equals("normal", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("gaussian", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("n", StringComparison.OrdinalIgnoreCase))
        {
            distribution = ToleranceDistribution.Normal;
            return true;
        }

        return Enum.TryParse(text, ignoreCase: true, out distribution);
    }

    private bool SetField<T>(ref T field, T value, string propertyName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
