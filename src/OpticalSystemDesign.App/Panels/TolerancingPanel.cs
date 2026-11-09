using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OpticalSystemDesign.App;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.ViewModels;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Formatting;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Tolerancing;

namespace OpticalSystemDesign.App.Panels;

public sealed partial class TolerancingPanel : UserControl, IDisposable
{
    private static readonly System.Text.Json.JsonSerializerOptions ToleranceJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly IOpticalDocumentService _documents;
    private readonly IPrescriptionService _prescription;
    private readonly ITolerancingService _tolerancing;
    private readonly IWorkspaceEventStream _events;
    private readonly ObservableCollection<ToleranceOperandEditorRow> _operands = new();
    private readonly HashSet<ToleranceOperandEditorRow> _trackedOperands = new();
    private readonly DataGrid _operandGrid = CreateGrid();
    private readonly OperandRowAppearance _rowAppearance;
    private readonly ComboBox _kindPicker = new() { MinWidth = 180 };
    private readonly ComboBox _surfacePicker = new() { MinWidth = 180 };
    private readonly ComboBox _endSurfacePicker = new() { MinWidth = 180 };
    private readonly NumericUpDown _testWavelength = Number(
        (decimal)SurfaceFringePerturbation.DefaultTestWavelengthMicrometers, 0.001m, 1000m, 0.0001m, 150);
    private StackPanel? _testWavelengthRow;
    private StackPanel? _endSurfaceRow;
    private StackPanel? _parameterRow;
    private StackPanel? _minimumRow;
    private StackPanel? _minimumTermRow;
    private StackPanel? _maximumTermRow;
    private StackPanel? _isoUnitRow;
    private StackPanel? _isoStatisticsRow;
    private StackPanel? _distributionRow;
    private readonly TextBlock _formUnits = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly NumericUpDown _minimumTerm = Number(2, 2, ZernikeFitEngine.MaximumStandardTerm, 1, 150);
    private readonly NumericUpDown _maximumTerm = Number(11, 3, ZernikeFitEngine.MaximumStandardTerm, 1, 150);
    private readonly ComboBox _isoUnitPicker = Picker(0, "nm", "双程条纹", "µm");
    private readonly ComboBox _isoStatisticsPicker = Picker(0, "固定最大值", "截断正态");
    private readonly NumericUpDown _parameterIndex = Number(1, 0, 256, 1, 150);
    private readonly ComboBox _distributionPicker = Picker(0, "正态（公差极限为 ±2σ）", "均匀");
    private readonly CheckBox _enabled = new() { Content = "启用", IsChecked = true };
    private readonly NumericUpDown _minimum = Number(-0.1m, -1_000_000, 1_000_000, 0.01m, 150);
    private readonly NumericUpDown _maximum = Number(0.1m, -1_000_000, 1_000_000, 0.01m, 150);
    private readonly TextBox _comment = new() { MinWidth = 180 };
    private readonly ComboBox _criterion = Picker(0, "RMS 点列半径", "RMS 波前", "指定频率 MTF", "系统评价函数");
    private readonly NumericUpDown _trials = Number(20, 0, 10_000, 20, 96);
    private readonly NumericUpDown _seed = Number(1234, 0, 2_000_000_000, 1, 104);
    private readonly NumericUpDown _compensationIterations = Number(3, 0, 500, 1, 96);
    private readonly NumericUpDown _yieldLimit = Number(0, 0, 1_000_000, 0.01m, 110);
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly OperationStatusBar _operationStatus = new();
    private CancellationTokenSource? _runCancellation;
    private TolerancingResultDto? _lastResult;

    internal event EventHandler? ResultPresentationChanged;
    internal TolerancingResultDto? CurrentResult =>
        _lastResult is { } result && result.SourceRevision == _events.Revision ? result : null;
    internal ToleranceCriterion ResultCriterion { get; private set; }
    private double _resultYieldLimit;
    internal string ResultMessage => _summary.Text ?? "";
    private ToleranceOperandEditorRow? _selectedOperandForEditor;
    private ToleranceAnalysisMode _analysisMode = ToleranceAnalysisMode.Sensitivity;
    private ToleranceDistribution? _distributionOverride;
    private int _maxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount);
    private int _worstSensitivityCount;
    private bool _showMonteCarloTrials = true;
    private double _inverseValue = 0.05;
    private ToleranceMtfSettingsDto _mtfSettings = new();
    private ToleranceCompensationAlgorithm _compensationAlgorithm;
    private readonly IMultiConfigurationService? _multiConfiguration;
    private ToleranceConfigurationSettingsDto? _configurationSettings;
    private IReadOnlyList<ToleranceStageObjectiveDto>? _stageObjectives;
    private ToleranceMeritTableDto? _meritTable;
    private IReadOnlyList<ToleranceCompensatorDto> _additionalCompensators = Array.Empty<ToleranceCompensatorDto>();
    private int _generation;
    private string? _currentTolerancePath;
    private bool _hasUnsavedChanges;
    private bool _suppressDirtyTracking;
    private bool _updatingEditor;
    private bool _disposed;

    public TolerancingPanel(
        IOpticalDocumentService documents,
        IPrescriptionService prescription,
        ITolerancingService tolerancing,
        IWorkspaceEventStream events, IMultiConfigurationService? multiConfiguration = null)
    {
        _documents = documents;
        _prescription = prescription;
        _tolerancing = tolerancing;
        _events = events;
        _multiConfiguration = multiConfiguration;
        _operands.CollectionChanged += OnOperandsCollectionChanged;
        _rowAppearance = new(_operandGrid);
        ConfigureGrids();
        ConfigureEditor();

        var wizardButton = CommandButton("tolerance-wizard", "公差向导");
        wizardButton.Click += async (_, _) => await ShowWizardAsync();
        var addButton = CommandButton("action-add", "添加");
        addButton.Click += (_, _) => AddOperand();
        var removeButton = CommandButton("action-delete", "删除");
        removeButton.Click += (_, _) => RemoveSelected();
        var validateButton = CommandButton("action-accept", "验证");
        validateButton.Click += (_, _) => Validate(showSuccess: true);
        var saveButton = CommandButton("file-save", "保存公差");
        saveButton.Click += async (_, _) => await SaveToleranceFileAsync();
        var loadButton = CommandButton("file-open", "载入公差");
        loadButton.Click += async (_, _) => await LoadToleranceFileAsync();
        var scriptButton = CommandButton("file-open", "导入 TSC");
        scriptButton.Click += async (_, _) => await ImportToleranceScriptAsync();
        var exportButton = CommandButton("file-export-report", "导出报告");
        exportButton.Click += async (_, _) => await ExportReportAsync();
        _saveWorstSample.IsEnabled = false;
        _saveWorstSample.Click += async (_, _) => await SaveWorstSampleAsync();
        var toolbar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
            Children =
            {
                wizardButton, addButton, removeButton, validateButton,
                saveButton, loadButton, scriptButton, exportButton, _saveWorstSample, CreateOpenSampleButton(), _operationStatus
            }
        };

        var editor = BuildOperandEditor();
        var editorScroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = editor
        };
        var editorLayout = new ResponsiveTwoPaneGrid(
            _operandGrid,
            editorScroller,
            "3*,10,320",
            "*,10,*",
            breakpoint: 800);

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
        AttachDirtyTracking();
        _hasUnsavedChanges = false;
    }

    internal bool HasUnsavedChanges => _hasUnsavedChanges;

    internal Task<bool> TrySaveChangesAsync(TopLevel owner) => SaveToleranceFileAsync(owner);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _generation++;
        _events.Changed -= OnWorkspaceChanged;
        _operands.CollectionChanged -= OnOperandsCollectionChanged;
        foreach (var operand in _trackedOperands)
        {
            operand.PropertyChanged -= OnOperandPropertyChanged;
        }
        _trackedOperands.Clear();

        TrackSelectedOperand(null);
        _runCancellation?.Cancel();
        _runCancellation?.Dispose();
        _operationStatus.Dispose();
        _rowAppearance.Dispose();
    }

    private void ConfigureGrids()
    {
        _operandGrid.SelectionMode = DataGridSelectionMode.Single;
        _operandGrid.Columns.Add(new DataGridTextColumn { Header = "#", Binding = new Binding(nameof(ToleranceOperandEditorRow.Index)), Width = Pixels(46), IsReadOnly = true });
        _operandGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "启用", Binding = TwoWay(nameof(ToleranceOperandEditorRow.Enabled)), Width = Pixels(56) });
        _operandGrid.Columns.Add(new DataGridTextColumn { Header = "类型", Binding = TwoWay(nameof(ToleranceOperandEditorRow.Code)), Width = Pixels(82) });
        _operandGrid.Columns.Add(new DataGridTextColumn { Header = "表面", Binding = TwoWay(nameof(ToleranceOperandEditorRow.SurfaceNumber)), Width = Pixels(62) });
        _operandGrid.Columns.Add(new DataGridTextColumn { Header = "终止面", Binding = TwoWay(nameof(ToleranceOperandEditorRow.EndSurfaceNumber)), Width = Pixels(72) });
        _operandGrid.Columns.Add(new DataGridTextColumn { Header = "参数", Binding = TwoWay(nameof(ToleranceOperandEditorRow.ParameterIndex)), Width = Pixels(62) });
        _operandGrid.Columns.Add(new DataGridTextColumn { Header = "最小偏差", Binding = TwoWay(nameof(ToleranceOperandEditorRow.Minimum)), Width = Pixels(110) });
        _operandGrid.Columns.Add(new DataGridTextColumn { Header = "最大偏差", Binding = TwoWay(nameof(ToleranceOperandEditorRow.Maximum)), Width = Pixels(110) });
        _operandGrid.Columns.Add(new DataGridTextColumn { Header = "统计", Binding = TwoWay(nameof(ToleranceOperandEditorRow.DistributionText)), Width = Pixels(90) });
        _operandGrid.Columns.Add(new DataGridTextColumn { Header = "备注", Binding = TwoWay(nameof(ToleranceOperandEditorRow.Comment)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _operandGrid.SelectionChanged += (_, _) => LoadSelectedOperand();
        _operandGrid.ItemsSource = _operands;


    }

    private void ConfigureEditor()
    {
        _kindPicker.ItemsSource = Enum.GetValues<ToleranceOperandKind>()
            .Select(kind => new ToleranceKindChoice(kind, ToleranceOperandEditorRow.CodeFor(kind), KindName(kind)))
            .ToArray();
        _kindPicker.ItemTemplate = new FuncDataTemplate<ToleranceKindChoice>((choice, _) =>
        {
            var name = new TextBlock { Text = choice.Name, TextWrapping = TextWrapping.Wrap };
            UiLocalization.LocalizeChoiceLabel(name);
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children = { new TextBlock { Text = choice.Code }, name }
            };
        });
        _kindPicker.SelectedIndex = 0;
    }

    private void AttachDirtyTracking()
    {
        _enabled.IsCheckedChanged += (_, _) => MarkDirtyFromEditor();
        _kindPicker.SelectionChanged += (_, _) =>
        {
            UpdateSurfaceFormEditor();
            MarkDirtyFromEditor();
        };
        _isoUnitPicker.SelectionChanged += (_, _) =>
        {
            UpdateSurfaceFormEditor();
            MarkDirtyFromEditor();
        };
        _minimumTerm.ValueChanged += (_, _) => MarkDirtyFromEditor();
        _maximumTerm.ValueChanged += (_, _) => MarkDirtyFromEditor();
        _isoStatisticsPicker.SelectionChanged += (_, _) => MarkDirtyFromEditor();
        _testWavelength.ValueChanged += (_, _) => MarkDirtyFromEditor();
        _surfacePicker.SelectionChanged += (_, _) => MarkDirtyFromEditor();
        _endSurfacePicker.SelectionChanged += (_, _) => MarkDirtyFromEditor();
        _parameterIndex.ValueChanged += (_, _) => MarkDirtyFromEditor();
        _distributionPicker.SelectionChanged += (_, _) => MarkDirtyFromEditor();
        _minimum.ValueChanged += (_, _) => MarkDirtyFromEditor();
        _maximum.ValueChanged += (_, _) => MarkDirtyFromEditor();
        _comment.TextChanged += (_, _) => MarkDirtyFromEditor();
        _criterion.SelectionChanged += (_, _) => MarkDirty();
        _trials.ValueChanged += (_, _) => MarkDirty();
        _seed.ValueChanged += (_, _) => MarkDirty();
        _compensationIterations.ValueChanged += (_, _) => MarkDirty();
        _yieldLimit.ValueChanged += (_, _) => MarkDirty();
    }

    private void MarkDirtyFromEditor()
    {
        if (_selectedOperandForEditor is not null)
        {
            MarkDirty();
        }
    }

    private void MarkDirty()
    {
        if (!_suppressDirtyTracking && !_updatingEditor)
        {
            _hasUnsavedChanges = true;
            if (_lastResult is not null || _operationStatus.Kind == OperationStatusKind.Running)
            {
                _runCancellation?.Cancel();
                _generation++;
                InvalidateResults("公差数据或运行设置已修改，请重新运行。");
            }
        }
    }

    private void OnOperandsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var operand in _trackedOperands)
            {
                operand.PropertyChanged -= OnOperandPropertyChanged;
            }

            _trackedOperands.Clear();
            foreach (var operand in _operands)
            {
                operand.PropertyChanged += OnOperandPropertyChanged;
                _trackedOperands.Add(operand);
            }

            MarkDirty();
            return;
        }

        if (args.OldItems is not null)
        {
            foreach (ToleranceOperandEditorRow operand in args.OldItems)
            {
                operand.PropertyChanged -= OnOperandPropertyChanged;
                _trackedOperands.Remove(operand);
            }
        }

        if (args.NewItems is not null)
        {
            foreach (ToleranceOperandEditorRow operand in args.NewItems)
            {
                operand.PropertyChanged += OnOperandPropertyChanged;
                _trackedOperands.Add(operand);
            }
        }

        MarkDirty();
    }

    private void OnOperandPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(ToleranceOperandEditorRow.Index))
        {
            MarkDirty();
        }
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
        _endSurfaceRow = Labeled("元件终止面", _endSurfacePicker);
        _parameterRow = Labeled("非球面参数序号", _parameterIndex);
        _minimumRow = Labeled("最小偏差", _minimum);
        _minimumTermRow = Labeled("Zernike 起始项", _minimumTerm);
        _maximumTermRow = Labeled("Zernike 终止项", _maximumTerm);
        _isoUnitRow = Labeled("ISO 面形单位", _isoUnitPicker);
        _isoStatisticsRow = Labeled("ISO 统计模型", _isoStatisticsPicker);
        Avalonia.Automation.AutomationProperties.SetName(_isoStatisticsPicker, "ISO 统计模型");
        ToolTip.SetTip(_isoStatisticsPicker, "截断正态的均值为最大值的一半，标准差为最大值的八分之一。");
        _distributionRow = Labeled("统计分布", _distributionPicker);
        _testWavelengthRow = Labeled("条纹检测波长 (µm)", _testWavelength);
        _testWavelengthRow.Children.Add(new TextBlock { Text = "偏差单位为双程干涉条纹数。", TextWrapping = TextWrapping.Wrap });
        UpdateSurfaceFormEditor();
        Avalonia.Automation.AutomationProperties.SetName(_testWavelength, "条纹检测波长 (µm)");
        var panel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "操作数属性", FontSize = DisplayTypography.SectionTitle, FontWeight = FontWeight.SemiBold },
                _enabled,
                Labeled("类型", _kindPicker),
                Labeled("表面", _surfacePicker),
                _endSurfaceRow,
                _parameterRow,
                _minimumTermRow,
                _maximumTermRow,
                _isoUnitRow,
                _isoStatisticsRow,
                _testWavelengthRow,
                _formUnits,
                _minimumRow,
                Labeled("最大偏差", _maximum),
                _distributionRow,
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

    private void UpdateSurfaceFormEditor()
    {
        var kind = (_kindPicker.SelectedItem as ToleranceKindChoice)?.Kind ?? ToleranceOperandKind.Radius;
        var usesFringes = ToleranceOperandEditorRow.UsesFringes(kind, (IsoSurfaceFormUnit)_isoUnitPicker.SelectedIndex);
        var usesTerms = kind.UsesZernikeTerms();
        var isIso = kind.UsesIsoUnits();
        var isForm = kind.SurfaceFormModeFor().HasValue;
        var maximumTerm = kind == ToleranceOperandKind.FringeZernikeIrregularity
            ? ZernikeFitEngine.MaximumFringeTerm : ZernikeFitEngine.MaximumStandardTerm;
        _minimumTerm.Maximum = maximumTerm;
        _maximumTerm.Maximum = maximumTerm;
        if (_testWavelengthRow is not null) _testWavelengthRow.IsVisible = usesFringes;
        if (_endSurfaceRow is not null) _endSurfaceRow.IsVisible = !isForm && !usesFringes;
        if (_parameterRow is not null) _parameterRow.IsVisible = !isForm && !usesFringes;
        if (_minimumRow is not null) _minimumRow.IsVisible = !isForm;
        if (_minimumTermRow is not null) _minimumTermRow.IsVisible = usesTerms;
        if (_maximumTermRow is not null) _maximumTermRow.IsVisible = usesTerms;
        if (_isoUnitRow is not null) _isoUnitRow.IsVisible = isIso;
        if (_isoStatisticsRow is not null) _isoStatisticsRow.IsVisible = kind.UsesIsoStatistics();
        if (_distributionRow is not null) _distributionRow.IsVisible = !kind.UsesIsoStatistics();
        _distributionPicker.IsEnabled = !isIso;
        if (isIso) _distributionPicker.SelectedIndex = 0;
        _formUnits.IsVisible = isForm;
        _formUnits.Text = kind switch
        {
            ToleranceOperandKind.StandardZernikeIrregularity => "输入面形 RMS (mm)，最小偏差自动取反。",
            ToleranceOperandKind.IsoPower => "输入面形峰谷值，最小偏差自动取反。使用截断正态分布。",
            ToleranceOperandKind.IsoIrregularityPv or ToleranceOperandKind.IsoRotationalIrregularityPv =>
                "输入面形峰谷值，最小偏差自动取反。",
            ToleranceOperandKind.IsoIrregularityRms => "输入去均值面形 RMS，最小偏差自动取反。",
            _ => "输入最大偏差，最小偏差自动取反。"
        };
    }

    private async Task ShowWizardAsync()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var settings = await new ToleranceWizardWindow(_prescription).ShowDialog<ToleranceWizardSettingsDto?>(owner);
        if (settings is not null)
        {
            ApplyWizardSettings(settings);
        }
    }

    internal void ApplyWizardSettings(ToleranceWizardSettingsDto settings)
    {
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
            _endSurfacePicker.SelectedItem = _endSurfacePicker.ItemsSource?
                .Cast<SurfaceEditorRow>()
                .FirstOrDefault(item => item.Number == row.EndSurfaceNumber);
            _parameterIndex.Value = row.ParameterIndex;
            _minimumTerm.Value = row.MinimumZernikeTerm;
            _maximumTerm.Value = row.MaximumZernikeTerm;
            _isoUnitPicker.SelectedIndex = (int)row.IsoUnit;
            _isoStatisticsPicker.SelectedIndex = (int)row.IsoStatistics;
            _testWavelength.Value = ToDecimal(row.TestWavelengthMicrometers
                ?? SurfaceFringePerturbation.DefaultTestWavelengthMicrometers);
            _minimum.Value = ToDecimal(row.Minimum);
            _maximum.Value = ToDecimal(row.Maximum);
            _distributionPicker.SelectedIndex = row.Distribution == ToleranceDistribution.Uniform ? 1 : 0;
            _comment.Text = row.Comment;
            UpdateSurfaceFormEditor();
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
            selected.EndSurfaceNumber = (_endSurfacePicker.SelectedItem as SurfaceEditorRow)?.Number ?? -1;
            selected.ParameterIndex = IntValue(_parameterIndex, 0);
            selected.MinimumZernikeTerm = IntValue(_minimumTerm, 2);
            selected.MaximumZernikeTerm = IntValue(_maximumTerm, 11);
            selected.IsoUnit = (IsoSurfaceFormUnit)_isoUnitPicker.SelectedIndex;
            selected.IsoStatistics = (IsoSurfaceFormStatistics)_isoStatisticsPicker.SelectedIndex;
            selected.TestWavelengthMicrometers = ToleranceOperandEditorRow.UsesFringes(kind.Kind, selected.IsoUnit)
                ? DoubleValue(_testWavelength, SurfaceFringePerturbation.DefaultTestWavelengthMicrometers)
                : null;
            selected.Maximum = DoubleValue(_maximum, 0.1);
            selected.Minimum = kind.Kind.SurfaceFormModeFor().HasValue ? -selected.Maximum : DoubleValue(_minimum, -0.1);
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
        var rows = _operands.Select(row => row.ToDto()).ToArray();
        var result = _configurationSettings is { Scope: not ToleranceConfigurationScope.Current } || _stageObjectives is { Count: > 0 }
            || (ToleranceCriterion)_criterion.SelectedIndex == ToleranceCriterion.SystemMerit
            ? _tolerancing.ValidateStudy(CreateRequest(CurrentRunOptions(), rows)) : _tolerancing.ValidateOperands(rows);
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
        var window = new TolerancingRunWindow(CurrentRunOptions(), _prescription.GetFields(),
            _prescription.GetSurfaces(), _prescription.GetWavelengths(), _multiConfiguration?.GetRows(),
            _multiConfiguration?.GetOperandRows(), _tolerancing.CaptureMeritTable);
        var options = await window.ShowDialog<TolerancingRunOptions?>(owner);
        if (options is null)
        {
            return false;
        }

        _analysisMode = options.Mode;
        _criterion.SelectedIndex = (int)options.Criterion;
        _trials.Value = options.MonteCarloRuns;
        _seed.Value = options.Seed;
        _compensationIterations.Value = options.CompensationIterations;
        _yieldLimit.Value = ToDecimal(options.YieldLimit);
        _distributionOverride = options.DistributionOverride;
        _maxDegreeOfParallelism = options.MaxDegreeOfParallelism;
        _worstSensitivityCount = options.WorstSensitivityCount;
        _showMonteCarloTrials = options.ShowMonteCarloTrials;
        _inverseValue = options.InverseValue;
        _mtfSettings = options.MtfSettings ?? new();
        _compensationAlgorithm = options.CompensationAlgorithm;
        _additionalCompensators = options.AdditionalCompensators ?? Array.Empty<ToleranceCompensatorDto>();
        _configurationSettings = options.ConfigurationSettings;
        _stageObjectives = options.StageObjectives;
        _meritTable = options.MeritTable;
        MarkDirty();
        return !window.ApplyOnly && await RunAsync(options);
    }

    private TolerancingRunOptions CurrentRunOptions() => new(
        _analysisMode,
        (ToleranceCriterion)_criterion.SelectedIndex,
        IntValue(_trials, 20),
        IntValue(_seed, 1234),
        IntValue(_compensationIterations, 3),
        _maxDegreeOfParallelism,
        DoubleValue(_yieldLimit, 0),
        _distributionOverride,
        _worstSensitivityCount,
        _showMonteCarloTrials,
        _inverseValue, _mtfSettings, _compensationAlgorithm, _additionalCompensators, _configurationSettings, _stageObjectives, _meritTable);

    private static TolerancingRequestDto CreateRequest(TolerancingRunOptions options, IReadOnlyList<ToleranceOperandDto> rows) => new(
        rows.FirstOrDefault(row => row.Enabled)?.SurfaceNumber ?? 1, 0, 0,
        options.MonteCarloRuns, options.Seed, options.CompensationIterations, rows,
        options.Criterion, options.YieldLimit, options.MaxDegreeOfParallelism, options.Mode,
        options.InverseValue, options.MtfSettings, options.CompensationAlgorithm, options.AdditionalCompensators,
        options.ConfigurationSettings, options.StageObjectives, options.MeritTable);

    internal async Task<bool> RunAsync(TolerancingRunOptions options)
    {
        ApplySelectedOperand();
        if (options.ConfigurationSettings is null && !Validate(showSuccess: false))
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
            : options.Mode is ToleranceAnalysisMode.InverseLimit or ToleranceAnalysisMode.InverseIncrement
                ? "正在反求并验证公差范围…"
            : options.MonteCarloRuns == 0
                ? "正在运行灵敏度公差分析…"
                : "正在运行灵敏度和 Monte Carlo 公差分析…";
        ResultPresentationChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            var rows = _operands.Select(row => row.ToDto()).ToArray();
            if (options.DistributionOverride is { } distribution)
            {
                rows = rows.Select(row => row.Kind == ToleranceOperandKind.Compensator || row.Kind.UsesIsoUnits()
                    ? row
                    : row with { Distribution = distribution }).ToArray();
            }

            var result = await _tolerancing.RunAsync(CreateRequest(options, rows), cancellationToken);
            if (_disposed || cancellationToken.IsCancellationRequested || generation != _generation)
            {
                return false;
            }

            if (result.SourceRevision != _events.Revision)
            {
                InvalidateResults(
                    "公差分析使用的系统版本已经过期，结果未显示。");
                return false;
            }

            if (options.Mode != ToleranceAnalysisMode.SkipSensitivity
                && result.SensitivityRows.Count == 0)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(result.Details)
                        ? "灵敏度分析未生成结果。"
                        : result.Details);
            }

            if (options.Mode is ToleranceAnalysisMode.InverseLimit or ToleranceAnalysisMode.InverseIncrement)
            {
                if (result.AdjustedOperands is null || result.InverseRows is not { Count: > 0 })
                {
                    throw new InvalidOperationException("反向灵敏度未返回调整后的公差范围。");
                }

                ApplyInverseOperands(result.AdjustedOperands);
                if (_configurationSettings is not null && result.AdjustedMceRows is { } mce)
                    _configurationSettings = _configurationSettings with { MceRows = mce };
            }

            if (options.MonteCarloRuns > 0 && result.TrialRows.Count == 0)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(result.Details)
                        ? "Monte Carlo 分析未生成试验结果。"
                        : result.Details);
            }

            _summary.Text = $"{result.Summary}    {result.Details}";
            _operationStatus.MarkSynced("公差分析完成");
            _lastResult = result;
            ResultCriterion = options.Criterion;
            _resultYieldLimit = options.YieldLimit;
            _saveWorstSample.IsEnabled = result.WorstSample is not null;
            ResultPresentationChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (OperationCanceledException)
        {
            if (!_disposed && generation == _generation)
            {
                _operationStatus.MarkStale("公差分析已取消");
                _summary.Text = "公差分析已取消。";
                ResultPresentationChanged?.Invoke(this, EventArgs.Empty);
            }

            return false;
        }
        catch (Exception exception)
        {
            if (!_disposed && generation == _generation)
            {
                _operationStatus.MarkFailed($"公差分析失败：{exception.Message}");
                _summary.Text = $"公差分析失败：{exception.Message}";
                ResultPresentationChanged?.Invoke(this, EventArgs.Empty);
            }

            throw;
        }
    }

    private void ApplyInverseOperands(IReadOnlyList<ToleranceOperandDto> adjustedOperands)
    {
        var adjustedByIndex = adjustedOperands.ToDictionary(operand => operand.Index);
        _suppressDirtyTracking = true;
        try
        {
            foreach (var row in _operands)
            {
                if (!adjustedByIndex.TryGetValue(row.Index, out var adjusted))
                {
                    continue;
                }

                row.Minimum = adjusted.Minimum;
                row.Maximum = adjusted.Maximum;
            }
        }
        finally
        {
            _suppressDirtyTracking = false;
        }

        _hasUnsavedChanges = true;
    }

    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs args)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            InvalidateForWorkspaceChange(args);
            return;
        }

        Dispatcher.UIThread.Post(() => InvalidateForWorkspaceChange(args));
    }

    private void InvalidateForWorkspaceChange(WorkspaceChangedEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        _generation++;
        _runCancellation?.Cancel();
        InvalidateResults("当前系统已改变，之前的公差分析结果已作废。");
        RefreshSurfaces();
        if (args.Category == WorkspaceChangeCategory.Document && args.FileSwitched)
        {
            ResetToleranceDocument();
        }
    }

    private void ResetToleranceDocument()
    {
        _suppressDirtyTracking = true;
        try
        {
            TrackSelectedOperand(null);
            _currentTolerancePath = null;
            _operands.Clear();
            AddOperand();
            _criterion.SelectedIndex = 0;
            _trials.Value = 20;
            _seed.Value = 1234;
            _compensationIterations.Value = 3;
            _yieldLimit.Value = 0;
            _mtfSettings = new();
            _compensationAlgorithm = ToleranceCompensationAlgorithm.DampedLeastSquares;
            _additionalCompensators = Array.Empty<ToleranceCompensatorDto>();
            _configurationSettings = null;
            _stageObjectives = null;
            _meritTable = null;
            _analysisMode = ToleranceAnalysisMode.Sensitivity;
            _inverseValue = 0.05;
            _distributionOverride = null;
            _operandGrid.SelectedItem = _operands.FirstOrDefault();
            _summary.Text = "已为当前光学系统创建新的公差数据。";
            _hasUnsavedChanges = false;
        }
        finally
        {
            _suppressDirtyTracking = false;
        }
    }

    private void InvalidateResults(string message)
    {
        if (_lastResult is null && _operationStatus.Kind == OperationStatusKind.Idle)
        {
            return;
        }

        _lastResult = null;
        _saveWorstSample.IsEnabled = false;
        _summary.Text = message;
        _operationStatus.MarkStale("结果已过期，请重新运行");
        ResultPresentationChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task<bool> SaveToleranceFileAsync(TopLevel? owner = null)
    {
        try
        {
            ApplySelectedOperand();
            if (!Validate(showSuccess: false))
            {
                return false;
            }

            var path = _currentTolerancePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                var topLevel = TopLevel.GetTopLevel(this) ?? owner;
                if (topLevel is null)
                {
                    return false;
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
                    return false;
                }

                path = file.Path.LocalPath;
            }

            var document = BuildStudyFile();
            var json = System.Text.Json.JsonSerializer.Serialize(
                document,
                ToleranceJsonOptions);
            var fullPath = Path.GetFullPath(path);
            await BoundedApplicationFile.WriteAllTextAtomicAsync(
                fullPath,
                json,
                BoundedApplicationFile.MaximumSettingsBytes,
                "Tolerance data");

            _currentTolerancePath = fullPath;
            _hasUnsavedChanges = false;
            _summary.Text = "公差数据已保存。";
            return true;
        }
        catch (Exception exception)
        {
            _summary.Text = $"保存公差数据失败：{exception.Message}";
            return false;
        }
    }

    private async Task LoadToleranceFileAsync()
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is not Window owner)
            {
                return;
            }

            if (!await UnsavedChangesGuard.CanContinueAsync(
                    _hasUnsavedChanges,
                    () => new UnsavedChangesWindow("载入另一份公差数据")
                        .ShowDialog<UnsavedChangesChoice>(owner),
                    () => SaveToleranceFileAsync(owner)))
            {
                return;
            }

            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
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

            var json = await BoundedApplicationFile.ReadAllTextAsync(
                files[0].Path.LocalPath,
                BoundedApplicationFile.MaximumSettingsBytes,
                "Tolerance settings");
            var document = ParseStudyFile(json);

            _suppressDirtyTracking = true;
            try
            {
                TrackSelectedOperand(null);
                _operands.Clear();
                foreach (var operand in document.Operands)
                {
                    _operands.Add(new ToleranceOperandEditorRow(operand));
                }

                Renumber();
                _criterion.SelectedIndex = (int)document.Criterion;
                _trials.Value = document.Trials;
                _seed.Value = document.Seed;
                _compensationIterations.Value = document.CompensationIterations;
                _yieldLimit.Value = ToDecimal(document.YieldLimit);
                _analysisMode = document.Mode;
                _inverseValue = document.InverseValue;
                _distributionOverride = document.DistributionOverride;
                _maxDegreeOfParallelism = Math.Clamp(
                    document.MaxDegreeOfParallelism,
                    1,
                    Math.Max(1, Environment.ProcessorCount));
                _worstSensitivityCount = document.WorstSensitivityCount;
                _showMonteCarloTrials = document.ShowMonteCarloTrials;
                _mtfSettings = document.MtfSettings ?? new();
                _compensationAlgorithm = document.CompensationAlgorithm;
                _additionalCompensators = document.AdditionalCompensators ?? Array.Empty<ToleranceCompensatorDto>();
                _configurationSettings = document.ConfigurationSettings;
                _stageObjectives = document.StageObjectives;
                _meritTable = document.MeritTable;
                _operandGrid.SelectedItem = _operands.FirstOrDefault();
                _currentTolerancePath = Path.GetFullPath(files[0].Path.LocalPath);
                _hasUnsavedChanges = false;
            }
            finally
            {
                _suppressDirtyTracking = false;
            }
            InvalidateResults("已载入新的公差定义，请重新运行。");
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

            await BoundedApplicationFile.WriteAllTextAtomicAsync(
                file.Path.LocalPath,
                BuildToleranceReportText(),
                BoundedApplicationFile.MaximumExportBytes,
                "Tolerance report");
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
        var operands = _operands.Select(row => row.ToDto()).ToArray();
        var enabled = operands.Where(row => row.Enabled).ToArray();
        var builder = new StringBuilder();

        builder.AppendLine("公差数据概要");
        builder.AppendLine();
        builder.AppendLine($"文件 : {path}");
        builder.AppendLine("题目 :");
        builder.AppendLine($"日期 : {DateTime.Now:yyyy/M/d}");
        builder.AppendLine();
        builder.AppendLine("半径和厚度数据在毫米里。");
        builder.AppendLine("TFRN/TIRR 使用双程干涉条纹数，每行单独给出检测波长。");
        builder.AppendLine("表面全跳动公差（TIR）单位是：毫米。");
        builder.AppendLine("折射率和阿贝公差是无量纲的。");
        builder.AppendLine("表面和元件偏心单位：毫米。");
        builder.AppendLine("表面和元件倾斜单位：角度。");
        builder.AppendLine();
        builder.AppendLine("表面中心公差:");
        builder.AppendLine();
        builder.AppendLine(FixedColumns("表面", "半径", "最小公差", "最大公差", "TFRN", "TIRR", "圆锥系数", "厚度", "最小公差", "最大公差"));
        foreach (var surface in surfaces)
        {
            var radius = FindOperand(enabled, ToleranceOperandKind.Radius, surface.Number);
            var thickness = FindOperand(enabled, ToleranceOperandKind.Thickness, surface.Number);
            builder.AppendLine(FixedColumns(
                surface.Number.ToString(),
                FormatRadius(surface.Radius),
                FormatOperandMinimum(radius),
                FormatOperandMaximum(radius),
                FormatOperandRange(FindOperand(enabled, ToleranceOperandKind.FringePower, surface.Number)),
                FormatOperandRange(FindOperand(enabled, ToleranceOperandKind.FringeIrregularity, surface.Number)),
                FormatOperandRange(FindOperand(enabled, ToleranceOperandKind.Conic, surface.Number)),
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
        builder.AppendLine(FixedColumns("#", "类型", "位置", "最小", "最大", "统计", "检测波长 (µm)", "面形参数", "标注"));
        foreach (var operand in operands)
        {
            builder.AppendLine(FixedColumns(
                operand.Index.ToString(),
                ToleranceOperandEditorRow.CodeFor(operand.Kind),
                ToleranceLocation(operand),
                FormatNumber(operand.Minimum),
                FormatNumber(operand.Maximum),
                operand.Kind.UsesIsoStatistics() ? ToleranceOperandEditorRow.IsoStatisticsText(operand.IsoStatistics)
                    : operand.Distribution == ToleranceDistribution.Normal ? "正态" : "均匀",
                ToleranceOperandEditorRow.UsesFringes(operand.Kind, operand.IsoUnit)
                    ? FormatNumber(operand.TestWavelengthMicrometers ?? SurfaceFringePerturbation.DefaultTestWavelengthMicrometers)
                    : "-",
                operand.Kind switch
                {
                    ToleranceOperandKind.FringeZernikeIrregularity => $"Z{operand.MinimumZernikeTerm}–Z{operand.MaximumZernikeTerm}",
                    ToleranceOperandKind.StandardZernikeIrregularity => $"Z{operand.MinimumZernikeTerm}–Z{operand.MaximumZernikeTerm} RMS (mm)",
                    _ when operand.Kind.UsesIsoUnits() => FormatIsoMagnitude(operand),
                    _ => "-"
                },
                operand.Comment));
        }

        builder.AppendLine();
        var jointSurfaces = enabled.GroupBy(row => row.SurfaceNumber)
            .Where(group => group.Any(row => row.Kind == ToleranceOperandKind.IsoIrregularityPv)
                && group.Any(row => row.Kind == ToleranceOperandKind.IsoRotationalIrregularityPv))
            .Select(group => group.Key).ToArray();
        if (jointSurfaces.Length > 0)
        {
            builder.AppendLine($"ISOB + ISOC 联合标定表面：{string.Join(", ", jointSurfaces)}");
            builder.AppendLine();
        }
        builder.AppendLine("公差分析结果:");
        builder.AppendLine();
        if (_lastResult is null)
        {
            builder.AppendLine("尚未运行灵敏度或 Monte Carlo 公差分析。");
            return builder.ToString();
        }

        if (_lastResult.SourceRevision != document.Revision)
        {
            builder.AppendLine("之前的公差分析结果对应旧系统版本，已禁止写入当前系统报告。请重新运行公差分析。");
            return builder.ToString();
        }

        builder.AppendLine(_lastResult.Summary);
        builder.AppendLine(_lastResult.Details);
        AppendMtfReport(builder, _lastResult);
        builder.AppendLine();
        if (_lastResult.InverseRows is { Count: > 0 } inverseRows)
        {
            builder.AppendLine(_analysisMode == ToleranceAnalysisMode.InverseIncrement
                ? $"反向增量结果（目标评价值 {_lastResult.InverseTarget}）"
                : $"反向极限结果（目标评价值 {_lastResult.InverseTarget}）");
            builder.AppendLine(FixedColumns(
                "操作数",
                "原最小",
                "调整最小",
                "原最大",
                "调整最大",
                "负端状态",
                "正端状态"));
            foreach (var row in inverseRows)
            {
                builder.AppendLine(FixedColumns(
                    row.Perturbation,
                    row.Minimum.OriginalTolerance,
                    row.Minimum.AdjustedTolerance,
                    row.Maximum.OriginalTolerance,
                    row.Maximum.AdjustedTolerance,
                    InverseStatusText(row.Minimum.Status),
                    InverseStatusText(row.Maximum.Status)));
                AppendLimitingConfiguration(builder, "负端", row.Minimum);
                AppendLimitingConfiguration(builder, "正端", row.Maximum);
            }

            builder.AppendLine();
        }

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
                AppendFields(builder, "负极限", row.NegativeFields);
                AppendFields(builder, "正极限", row.PositiveFields);
                AppendCompensators(builder, "负极限", row.NegativeCompensators);
                AppendCompensators(builder, "正极限", row.PositiveCompensators);
                AppendConfigurations(builder, "负极限", row.NegativeConfigurations);
                AppendConfigurations(builder, "正极限", row.PositiveConfigurations);
                AppendStages(builder, row.NegativeStages, "负极限");
                AppendStages(builder, row.PositiveStages, "正极限");
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
                if (row.Passed.HasValue) builder.AppendLine($"  验收：{(row.Passed.Value ? "合格" : "不合格")}");
                AppendFields(builder, "补偿前", row.UncompensatedFields);
                AppendFields(builder, "补偿后", row.Fields);
                AppendCompensators(builder, "试验", row.Compensators);
                AppendStages(builder, row.Stages);
                AppendConfigurations(builder, "补偿前", row.UncompensatedConfigurations);
                AppendConfigurations(builder, "补偿后", row.Configurations);
            }
        }

        return builder.ToString();
    }

    private static string InverseStatusText(ToleranceInverseEndpointStatus status) => status switch
    {
        ToleranceInverseEndpointStatus.UnchangedWithinTarget => "保留",
        ToleranceInverseEndpointStatus.Tightened => "已收紧",
        ToleranceInverseEndpointStatus.ZeroRange => "零范围",
        _ => "不支持"
    };

    private static string ToleranceLocation(ToleranceOperandDto operand) => operand.Kind switch
    {
        ToleranceOperandKind.ElementDecenterX
            or ToleranceOperandKind.ElementDecenterY
            or ToleranceOperandKind.ElementTiltX
            or ToleranceOperandKind.ElementTiltY =>
            $"{operand.SurfaceNumber}-{operand.EndSurfaceNumber}",
        ToleranceOperandKind.AsphereCoefficient =>
            $"{operand.SurfaceNumber}:P{operand.ParameterIndex}",
        _ => operand.SurfaceNumber.ToString(CultureInfo.InvariantCulture)
    };

    internal ToleranceChartView BuildHistogramChartView() => ToleranceChartBuilder.Histogram(CurrentResult, ResultCriterion);

    internal ToleranceChartView BuildYieldChartView() => ToleranceChartBuilder.Yield(CurrentResult, ResultCriterion, _resultYieldLimit);

    private static ToleranceOperandDto? FindOperand(
        IReadOnlyList<ToleranceOperandDto> operands,
        ToleranceOperandKind kind,
        int surfaceNumber) =>
        operands.FirstOrDefault(operand => operand.Kind == kind && operand.SurfaceNumber == surfaceNumber);

    private static string FormatRadius(double radius) =>
        !double.IsFinite(radius) || Math.Abs(radius) < 1e-12
            ? NumericDisplayFormatter.Format(double.PositiveInfinity)
            : FormatNumber(radius);

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
        var selectedEnd = (_endSurfacePicker.SelectedItem as SurfaceEditorRow)?.Number;
        var surfaces = _prescription.GetSurfaces().Select(surface => new SurfaceEditorRow(surface)).ToArray();
        _surfacePicker.ItemsSource = surfaces;
        _endSurfacePicker.ItemsSource = surfaces;
        _surfacePicker.SelectedItem = surfaces.FirstOrDefault(surface => surface.Number == selected)
            ?? surfaces.ElementAtOrDefault(Math.Min(1, Math.Max(0, surfaces.Length - 1)));
        _endSurfacePicker.SelectedItem = surfaces.FirstOrDefault(surface => surface.Number == selectedEnd)
            ?? surfaces.ElementAtOrDefault(Math.Min(2, Math.Max(0, surfaces.Length - 1)));
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
            + $"P05：{statistics.Percentile05}    P50：{statistics.Percentile50}{Environment.NewLine}"
            + $"P90：{statistics.Percentile90}{Environment.NewLine}"
            + $"P95：{statistics.Percentile95}{Environment.NewLine}"
            + $"预计合格率：{statistics.Yield}";
    }

    private static string FormatIsoMagnitude(ToleranceOperandDto operand)
    {
        var measure = operand.Kind == ToleranceOperandKind.IsoIrregularityRms ? "RMS" : "P–V";
        var unit = operand.IsoUnit switch
        {
            IsoSurfaceFormUnit.Nanometers => "nm",
            IsoSurfaceFormUnit.Micrometers => "µm",
            _ => "fringes"
        };
        return $"{measure} ({unit})";
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
        ToleranceOperandKind.ElementDecenterX => "元件 X 偏心",
        ToleranceOperandKind.ElementDecenterY => "元件 Y 偏心",
        ToleranceOperandKind.ElementTiltX => "元件 X 倾斜",
        ToleranceOperandKind.ElementTiltY => "元件 Y 倾斜",
        ToleranceOperandKind.AsphereCoefficient => "非球面参数",
        ToleranceOperandKind.RefractiveIndex => "折射率",
        ToleranceOperandKind.AbbeNumber => "阿贝数",
        ToleranceOperandKind.Compensator => "补偿器",
        ToleranceOperandKind.FringePower => "曲率条纹",
        ToleranceOperandKind.FringeIrregularity => "面形不规则度（球差 + 像散）",
        ToleranceOperandKind.FringeZernikeIrregularity => "Fringe Zernike 面形不规则度",
        ToleranceOperandKind.StandardZernikeIrregularity => "Standard Zernike 面形 RMS",
        ToleranceOperandKind.IsoPower => "ISO 面形峰谷光焦度",
        ToleranceOperandKind.IsoIrregularityPv => "ISO 总不规则度峰谷值",
        ToleranceOperandKind.IsoRotationalIrregularityPv => "ISO 旋转对称不规则度峰谷值",
        ToleranceOperandKind.IsoIrregularityRms => "ISO 总不规则度 RMS",
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

    private sealed record ToleranceKindChoice(ToleranceOperandKind Kind, string Code, string Name);

}
