using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
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
    private readonly IOpticalDocumentService _documents;
    private readonly IPrescriptionService _prescription;
    private readonly ITolerancingService _tolerancing;
    private readonly IWorkspaceEventStream _events;
    private readonly ObservableCollection<ToleranceOperandEditorRow> _operands = new();
    private readonly DataGrid _operandGrid = CreateGrid();
    private readonly DataGrid _sensitivityGrid = CreateGrid();
    private readonly DataGrid _monteCarloGrid = CreateGrid();
    private readonly ComboBox _kindPicker = new() { MinWidth = 180 };
    private readonly ComboBox _surfacePicker = new() { MinWidth = 180 };
    private readonly ComboBox _distributionPicker = Picker(0, "正态（公差极限为 ±2σ）", "均匀");
    private readonly CheckBox _enabled = new() { Content = "启用", IsChecked = true };
    private readonly NumericUpDown _minimum = Number(-0.1m, -1_000_000, 1_000_000, 0.01m, 150);
    private readonly NumericUpDown _maximum = Number(0.1m, -1_000_000, 1_000_000, 0.01m, 150);
    private readonly TextBox _comment = new() { MinWidth = 180 };
    private readonly ComboBox _criterion = Picker(0, "RMS 点列半径", "RMS 波前");
    private readonly NumericUpDown _trials = Number(20, 0, 10_000, 20, 96);
    private readonly NumericUpDown _seed = Number(1234, 0, 2_000_000_000, 1, 104);
    private readonly NumericUpDown _compensationIterations = Number(3, 0, 500, 1, 96);
    private readonly NumericUpDown _yieldLimit = Number(0, 0, 1_000_000, 0.01m, 110);
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly OperationStatusBar _operationStatus = new();
    private readonly TextBlock _statistics = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12) };
    private CancellationTokenSource? _runCancellation;
    private TolerancingResultDto? _lastResult;
    private ToleranceOperandEditorRow? _selectedOperandForEditor;
    private ToleranceAnalysisMode _analysisMode = ToleranceAnalysisMode.Sensitivity;
    private ToleranceDistribution? _distributionOverride;
    private int _maxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount);
    private int _worstSensitivityCount;
    private bool _showMonteCarloTrials = true;
    private int _generation;
    private bool _updatingEditor;
    private bool _disposed;

    public TolerancingPanel(
        IOpticalDocumentService documents,
        IPrescriptionService prescription,
        ITolerancingService tolerancing,
        IWorkspaceEventStream events)
    {
        _documents = documents;
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
        var exportButton = CommandButton("download", "导出报告");
        exportButton.Click += async (_, _) => await ExportReportAsync();
        var toolbar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
            Children =
            {
                wizardButton, addButton, removeButton, validateButton,
                saveButton, loadButton, exportButton, _operationStatus
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

        var content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 4,
            Margin = new Thickness(12)
        };
        content.Children.Add(toolbar);
        Grid.SetRow(editorLayout, 1);
        content.Children.Add(editorLayout);
        var summaryBorder = new Border
        {
            Padding = new Thickness(10, 8),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = _summary
        };
        summaryBorder.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        Grid.SetRow(summaryBorder, 2);
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

    public async Task<bool> ShowAnalysisDialogAsync(Window owner)
    {
        var options = await new TolerancingRunWindow(CurrentRunOptions())
            .ShowDialog<TolerancingRunOptions?>(owner);
        if (options is null)
        {
            return false;
        }

        _analysisMode = options.Mode;
        _criterion.SelectedIndex = options.Criterion == ToleranceCriterion.RmsWavefront ? 1 : 0;
        _trials.Value = options.MonteCarloRuns;
        _seed.Value = options.Seed;
        _compensationIterations.Value = options.CompensationIterations;
        _yieldLimit.Value = ToDecimal(options.YieldLimit);
        _distributionOverride = options.DistributionOverride;
        _maxDegreeOfParallelism = options.MaxDegreeOfParallelism;
        _worstSensitivityCount = options.WorstSensitivityCount;
        _showMonteCarloTrials = options.ShowMonteCarloTrials;
        return await RunAsync(options);
    }

    private TolerancingRunOptions CurrentRunOptions() => new(
        _analysisMode,
        _criterion.SelectedIndex == 1
            ? ToleranceCriterion.RmsWavefront
            : ToleranceCriterion.RmsSpotRadius,
        IntValue(_trials, 20),
        IntValue(_seed, 1234),
        IntValue(_compensationIterations, 3),
        _maxDegreeOfParallelism,
        DoubleValue(_yieldLimit, 0),
        _distributionOverride,
        _worstSensitivityCount,
        _showMonteCarloTrials);

    private async Task<bool> RunAsync(TolerancingRunOptions options)
    {
        ApplySelectedOperand();
        if (!Validate(showSuccess: false))
        {
            throw new InvalidOperationException(_summary.Text ?? "公差数据验证失败。");
        }

        if (options.Mode == ToleranceAnalysisMode.SkipSensitivity
            && options.MonteCarloRuns == 0)
        {
            throw new InvalidOperationException("当前设置同时跳过灵敏度且 Monte Carlo 次数为 0，没有可执行的公差分析。");
        }

        _runCancellation?.Cancel();
        _runCancellation?.Dispose();
        _runCancellation = new CancellationTokenSource();
        var cancellationToken = _runCancellation.Token;
        var generation = ++_generation;
        _operationStatus.Start("正在运行公差分析…", () => _runCancellation?.Cancel());
        _summary.Text = options.Mode == ToleranceAnalysisMode.SkipSensitivity
            ? "正在运行 Monte Carlo 公差分析…"
            : options.MonteCarloRuns == 0
                ? "正在运行灵敏度公差分析…"
                : "正在运行灵敏度和 Monte Carlo 公差分析…";
        try
        {
            var rows = _operands.Select(row => row.ToDto()).ToArray();
            if (options.DistributionOverride is { } distribution)
            {
                rows = rows.Select(row => row.Kind == ToleranceOperandKind.Compensator
                    ? row
                    : row with { Distribution = distribution }).ToArray();
            }

            var firstSurface = rows.FirstOrDefault(row => row.Enabled)?.SurfaceNumber ?? 1;
            var result = await _tolerancing.RunAsync(new TolerancingRequestDto(
                firstSurface,
                0,
                0,
                options.MonteCarloRuns,
                options.Seed,
                options.CompensationIterations,
                rows,
                options.Criterion,
                options.YieldLimit,
                options.MaxDegreeOfParallelism,
                options.Mode), cancellationToken);
            if (_disposed || cancellationToken.IsCancellationRequested || generation != _generation)
            {
                return false;
            }

            if (options.Mode == ToleranceAnalysisMode.Sensitivity
                && result.SensitivityRows.Count == 0)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(result.Details)
                        ? "灵敏度分析未生成结果。"
                        : result.Details);
            }

            if (options.MonteCarloRuns > 0 && result.TrialRows.Count == 0)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(result.Details)
                        ? "Monte Carlo 分析未生成试验结果。"
                        : result.Details);
            }

            _sensitivityGrid.ItemsSource = result.SensitivityRows;
            _monteCarloGrid.ItemsSource = result.TrialRows;
            _statistics.Text = FormatStatistics(result.Statistics);
            _summary.Text = $"{result.Summary}    {result.Details}";
            _operationStatus.MarkSynced("公差分析完成");
            _lastResult = result;
            return true;
        }
        catch (OperationCanceledException)
        {
            if (!_disposed && generation == _generation)
            {
                _operationStatus.MarkStale("公差分析已取消");
            }

            return false;
        }
        catch (Exception exception)
        {
            if (!_disposed && generation == _generation)
            {
                _operationStatus.MarkFailed($"公差分析失败：{exception.Message}");
                _summary.Text = $"公差分析失败：{exception.Message}";
            }

            throw;
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

            await File.WriteAllTextAsync(file.Path.LocalPath, BuildToleranceReportText());
            _summary.Text = "公差分析报告已导出。";
        }
        catch (Exception exception)
        {
            _summary.Text = $"导出公差报告失败：{exception.Message}";
        }
    }

    public string BuildToleranceReportText()
    {
        var document = _documents.GetSnapshot();
        var path = document.Path ?? document.Name;
        var surfaces = _prescription.GetSurfaces();
        var wavelengths = _prescription.GetWavelengths();
        var operands = _operands.Select(row => row.ToDto()).ToArray();
        var enabled = operands.Where(row => row.Enabled).ToArray();
        var measurementWavelength = wavelengths.FirstOrDefault(wave => wave.IsPrimary)
            ?? wavelengths.FirstOrDefault();
        var builder = new StringBuilder();

        builder.AppendLine("公差数据概要");
        builder.AppendLine();
        builder.AppendLine($"文件 : {path}");
        builder.AppendLine("题目 :");
        builder.AppendLine($"日期 : {DateTime.Now:yyyy/M/d}");
        builder.AppendLine();
        builder.AppendLine("半径和厚度数据在毫米里。");
        builder.AppendLine($"光焦和不规则度是通过双通干涉测量，测量波长: {FormatWavelength(measurementWavelength)}");
        builder.AppendLine("仅球差和象散不规则公差列在表面中心公差；");
        builder.AppendLine("Zernike不规则公差列在其它公差下。");
        builder.AppendLine("表面全跳动公差（TIR）单位是：毫米。");
        builder.AppendLine("折射率和阿贝公差是无量纲的。");
        builder.AppendLine("表面和元件偏心单位：毫米。");
        builder.AppendLine("表面和元件倾斜单位：角度。");
        builder.AppendLine();
        builder.AppendLine("表面中心公差:");
        builder.AppendLine();
        builder.AppendLine(FixedColumns("表面", "半径", "最小公差", "最大公差", "光焦度", "不规则", "厚度", "最小公差", "最大公差"));
        foreach (var surface in surfaces)
        {
            var radius = FindOperand(enabled, ToleranceOperandKind.Radius, surface.Number);
            var thickness = FindOperand(enabled, ToleranceOperandKind.Thickness, surface.Number);
            builder.AppendLine(FixedColumns(
                surface.Number.ToString(),
                FormatRadius(surface.Radius),
                FormatOperandMinimum(radius),
                FormatOperandMaximum(radius),
                "-",
                FormatConicTolerance(enabled, surface.Number),
                FormatNumber(surface.Thickness),
                FormatOperandMinimum(thickness),
                FormatOperandMaximum(thickness)));
        }

        builder.AppendLine();
        builder.AppendLine("表面偏心/倾斜公差:");
        builder.AppendLine();
        builder.AppendLine(FixedColumns("表面", "偏心 X", "偏心 Y", "偏心 R", "倾斜 X", "倾斜 Y", "不规则X", "不规则Y"));
        foreach (var surface in surfaces)
        {
            builder.AppendLine(FixedColumns(
                surface.Number.ToString(),
                FormatOperandRange(FindOperand(enabled, ToleranceOperandKind.DecenterX, surface.Number)),
                FormatOperandRange(FindOperand(enabled, ToleranceOperandKind.DecenterY, surface.Number)),
                "-",
                FormatOperandRange(FindOperand(enabled, ToleranceOperandKind.TiltX, surface.Number)),
                FormatOperandRange(FindOperand(enabled, ToleranceOperandKind.TiltY, surface.Number)),
                "-",
                "-"));
        }

        builder.AppendLine();
        builder.AppendLine("折射率/阿贝公差:");
        builder.AppendLine();
        builder.AppendLine(FixedColumns("表面", "材料", "折射率", "Abbe"));
        foreach (var surface in surfaces.Where(surface => !string.IsNullOrWhiteSpace(surface.Material)))
        {
            builder.AppendLine(FixedColumns(
                surface.Number.ToString(),
                surface.Material,
                FormatOperandRange(FindOperand(enabled, ToleranceOperandKind.RefractiveIndex, surface.Number)),
                FormatOperandRange(FindOperand(enabled, ToleranceOperandKind.AbbeNumber, surface.Number))));
        }

        builder.AppendLine();
        builder.AppendLine("公差操作数:");
        builder.AppendLine();
        builder.AppendLine(FixedColumns("#", "类型", "表面", "最小", "最大", "统计", "标注"));
        foreach (var operand in operands)
        {
            builder.AppendLine(FixedColumns(
                operand.Index.ToString(),
                ToleranceOperandEditorRow.CodeFor(operand.Kind),
                operand.SurfaceNumber.ToString(),
                FormatNumber(operand.Minimum),
                FormatNumber(operand.Maximum),
                operand.Distribution == ToleranceDistribution.Normal ? "正态" : "均匀",
                operand.Comment));
        }

        builder.AppendLine();
        builder.AppendLine("公差分析结果:");
        builder.AppendLine();
        if (_lastResult is null)
        {
            builder.AppendLine("尚未运行灵敏度或 Monte Carlo 公差分析。");
            return builder.ToString();
        }

        builder.AppendLine(_lastResult.Summary);
        builder.AppendLine(_lastResult.Details);
        builder.AppendLine();
        if (_lastResult.SensitivityStatistics is not null)
        {
            builder.AppendLine("灵敏度 RSS 预计性能");
            builder.AppendLine(FixedColumns(
                "名义值",
                "RSS 预计变化",
                "预计评价值"));
            builder.AppendLine(FixedColumns(
                _lastResult.SensitivityStatistics.Nominal,
                _lastResult.SensitivityStatistics.RssEstimatedChange,
                _lastResult.SensitivityStatistics.EstimatedCriterion));
            builder.AppendLine();
        }

        if (_lastResult.Statistics is not null)
        {
            builder.AppendLine("Monte Carlo 统计摘要");
            builder.AppendLine(FormatStatistics(_lastResult.Statistics));
            builder.AppendLine();
        }

        if (_lastResult.SensitivityRows.Count > 0)
        {
            builder.AppendLine("灵敏度（逐项施加最小/最大公差）");
            builder.AppendLine(FixedColumns("操作数", "负极限", "正极限", "最坏值", "变化"));
            var sensitivityRows = _worstSensitivityCount > 0
                ? _lastResult.SensitivityRows.Take(_worstSensitivityCount)
                : _lastResult.SensitivityRows;
            foreach (var row in sensitivityRows)
            {
                builder.AppendLine(FixedColumns(
                    row.Perturbation,
                    row.NegativeMerit,
                    row.PositiveMerit,
                    row.WorstMerit,
                    row.DeltaMerit));
            }

            builder.AppendLine();
        }
        else if (_analysisMode == ToleranceAnalysisMode.SkipSensitivity)
        {
            builder.AppendLine("灵敏度：已按运行设置跳过。");
            builder.AppendLine();
        }

        if (_showMonteCarloTrials && _lastResult.TrialRows.Count > 0)
        {
            builder.AppendLine("Monte Carlo（全部公差同时随机施加）");
            builder.AppendLine(FixedColumns("试验", "补偿前", "补偿后", "相对名义变化"));
            foreach (var row in _lastResult.TrialRows)
            {
                builder.AppendLine(FixedColumns(
                    row.Trial.ToString(),
                    row.Merit,
                    row.CompensatedMerit,
                    row.Degradation));
            }
        }

        return builder.ToString();
    }

    internal ToleranceChartView BuildHistogramChartView() =>
        ToleranceChartBuilder.Histogram(
            _lastResult,
            _criterion.SelectedIndex == 1
                ? ToleranceCriterion.RmsWavefront
                : ToleranceCriterion.RmsSpotRadius);

    internal ToleranceChartView BuildYieldChartView() =>
        ToleranceChartBuilder.Yield(
            _lastResult,
            _criterion.SelectedIndex == 1
                ? ToleranceCriterion.RmsWavefront
                : ToleranceCriterion.RmsSpotRadius,
            DoubleValue(_yieldLimit, 0));

    private static ToleranceOperandDto? FindOperand(
        IReadOnlyList<ToleranceOperandDto> operands,
        ToleranceOperandKind kind,
        int surfaceNumber) =>
        operands.FirstOrDefault(operand => operand.Kind == kind && operand.SurfaceNumber == surfaceNumber);

    private static string FormatWavelength(WavelengthRowDto? wavelength) =>
        wavelength is null
            ? "-"
            : $"{wavelength.Nanometers / 1000.0:0.####} μm";

    private static string FormatRadius(double radius) =>
        !double.IsFinite(radius) || Math.Abs(radius) < 1e-12
            ? "无限"
            : FormatNumber(radius);

    private static string FormatConicTolerance(IReadOnlyList<ToleranceOperandDto> operands, int surfaceNumber)
    {
        var conic = FindOperand(operands, ToleranceOperandKind.Conic, surfaceNumber);
        return conic is null ? "-" : FormatOperandRange(conic);
    }

    private static string FormatOperandMinimum(ToleranceOperandDto? operand) =>
        operand is null ? "-" : FormatNumber(operand.Minimum);

    private static string FormatOperandMaximum(ToleranceOperandDto? operand) =>
        operand is null ? "-" : FormatNumber(operand.Maximum);

    private static string FormatOperandRange(ToleranceOperandDto? operand) =>
        operand is null ? "-" : $"{FormatNumber(operand.Minimum)} / {FormatNumber(operand.Maximum)}";

    private static string FormatNumber(double value) =>
        double.IsFinite(value)
            ? value.ToString("0.#####", CultureInfo.InvariantCulture)
            : "-";

    private static string FixedColumns(params string[] values)
    {
        var widths = new[] { 8, 16, 16, 16, 16, 16, 16, 16, 16 };
        var builder = new StringBuilder();
        for (var index = 0; index < values.Length; index++)
        {
            var text = (values[index] ?? string.Empty)
                .ReplaceLineEndings(" ")
                .Trim();
            var width = widths[Math.Min(index, widths.Length - 1)];
            builder.Append(text.Length >= width ? text[..Math.Min(text.Length, width - 1)] + " " : text.PadRight(width));
        }

        return builder.ToString().TrimEnd();
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
