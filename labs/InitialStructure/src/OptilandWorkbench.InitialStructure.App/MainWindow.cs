using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;
using OptilandWorkbench.InitialStructure.Persistence;

namespace OptilandWorkbench.InitialStructure.App;

public sealed class MainWindow : Window
{
    private readonly NumericUpDown _effectiveFocalLength = Number(50, 1, 1000, 1);
    private readonly NumericUpDown _fNumber = Number(4, 0.5m, 64, 0.1m);
    private readonly NumericUpDown _fieldAngle = Number(10, 0, 89, 0.5m);
    private readonly NumericUpDown _minimumElements = Number(3, 3, 8, 1);
    private readonly NumericUpDown _maximumElements = Number(3, 3, 8, 1);
    private readonly NumericUpDown _maximumTrack = Number(100, 10, 2000, 1);
    private readonly NumericUpDown _rmsLimit = Number(0.25m, 0.000001m, 100, 0.01m);
    private readonly NumericUpDown _maximumSpotLimit = Number(1, 0.000001m, 200, 0.05m);
    private readonly NumericUpDown _seedCount = Number(24, 1, 10_000, 1);
    private readonly NumericUpDown _maximumEvaluations = Number(2000, 1, 100_000, 10);
    private readonly NumericUpDown _parallelism = Number(
        Math.Max(1, Environment.ProcessorCount / 2),
        1,
        256,
        1);
    private readonly NumericUpDown _randomSeed = Number(1, 0, 1_000_000_000, 1);
    private readonly Button _validateButton = Command("预检查");
    private readonly Button _runButton = Command("开始");
    private readonly Button _resumeButton = Command("恢复", false);
    private readonly Button _cancelButton = Command("取消", false);
    private readonly Button _compareAButton = Command("设为 A", false);
    private readonly Button _compareBButton = Command("设为 B", false);
    private readonly Button _exportButton = Command("导出 STAROPT", false);
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, Height = 4 };
    private readonly TextBlock _status = new() { Text = "就绪", TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _selectionDetails = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _comparisonDetails = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ObservableCollection<CandidateRow> _rows = [];
    private readonly CandidatePreviewControl _preview = new() { MinHeight = 220 };
    private readonly DataGrid _dataGrid;
    private readonly Grid _workspace = new();
    private readonly Border _inspector;
    private CancellationTokenSource? _runCancellation;
    private CandidateRow? _comparisonA;
    private CandidateRow? _comparisonB;
    private int _resumeRefreshGeneration;
    private int _runGeneration;
    private bool _resumeLoading;
    private bool _running;

    public MainWindow()
    {
        Title = "智能初始结构实验室";
        Width = 1180;
        Height = 760;
        MinWidth = 480;
        MinHeight = 520;
        _dataGrid = BuildDataGrid();
        _inspector = BuildInspector();
        Content = BuildContent();
        WireEvents();
        ApplyResponsiveLayout(Width);
        _ = RefreshResumeAvailabilityAsync();
    }

    private Control BuildContent()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto") };
        var titleBand = new Border
        {
            Padding = new Thickness(16, 12),
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = Brushes.Gray,
            Child = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock
                    {
                        Text = "智能初始结构实验室",
                        FontSize = 20,
                        FontWeight = FontWeight.SemiBold
                    },
                    new TextBlock
                    {
                        Text = "实验版本 · flat-to-usable-hybrid/v3",
                        Opacity = 0.7
                    }
                }
            }
        };
        root.Children.Add(titleBand);

        var parameterBand = new Border
        {
            Padding = new Thickness(12, 8),
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = Brushes.Gray,
            Child = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    Field("焦距 mm", _effectiveFocalLength),
                    Field("F/#", _fNumber),
                    Field("最大视场 °", _fieldAngle),
                    Field("最少片数", _minimumElements),
                    Field("最多片数", _maximumElements),
                    Field("最大总长 mm", _maximumTrack),
                    Field("RMS 上限 mm", _rmsLimit),
                    Field("最大光斑 mm", _maximumSpotLimit),
                    Field("种子数", _seedCount),
                    Field("评价上限", _maximumEvaluations),
                    Field("并发数", _parallelism),
                    Field("随机种子", _randomSeed)
                }
            }
        };
        Grid.SetRow(parameterBand, 1);
        root.Children.Add(parameterBand);

        _workspace.Children.Add(_dataGrid);
        _workspace.Children.Add(_inspector);
        Grid.SetRow(_workspace, 2);
        root.Children.Add(_workspace);

        var commands = new WrapPanel
        {
            Margin = new Thickness(12, 8, 12, 4),
            Orientation = Orientation.Horizontal,
            Children =
            {
                _validateButton,
                _runButton,
                _resumeButton,
                _cancelButton,
                _compareAButton,
                _compareBButton,
                _exportButton
            }
        };
        foreach (var button in commands.Children.OfType<Button>())
        {
            button.Margin = new Thickness(0, 0, 8, 4);
        }
        Grid.SetRow(commands, 3);
        root.Children.Add(commands);

        var footer = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            Margin = new Thickness(12, 0, 12, 12),
            RowSpacing = 6
        };
        footer.Children.Add(_status);
        Grid.SetRow(_progress, 1);
        footer.Children.Add(_progress);
        Grid.SetRow(footer, 4);
        root.Children.Add(footer);
        return root;
    }

    private DataGrid BuildDataGrid()
    {
        var dataGrid = new DataGrid
        {
            ItemsSource = _rows,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserResizeColumns = true,
            CanUserSortColumns = true,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            SelectionMode = DataGridSelectionMode.Single,
            Margin = new Thickness(12, 10),
            MinHeight = 180,
            Columns =
            {
                TextColumn("候选", nameof(CandidateRow.CandidateId), 184),
                TextColumn("代", nameof(CandidateRow.Generation), 44),
                TextColumn("片数", nameof(CandidateRow.ElementCount), 58),
                TextColumn("光阑", nameof(CandidateRow.StopVariant), 58),
                TextColumn("状态", nameof(CandidateRow.Status), 92),
                TextColumn("焦距 mm", nameof(CandidateRow.EffectiveFocalLength), 92),
                TextColumn("F/#", nameof(CandidateRow.FNumber), 70),
                TextColumn("有效光线", nameof(CandidateRow.ValidRayFraction), 88),
                TextColumn("RMS mm", nameof(CandidateRow.RmsSpotRadius), 96),
                TextColumn("最大 mm", nameof(CandidateRow.MaximumSpotRadius), 96),
                TextColumn("硬约束", nameof(CandidateRow.HardViolationCount), 72)
            }
        };
        SetAccessibleName(dataGrid, "初始结构候选列表");
        return dataGrid;
    }

    private Border BuildInspector()
    {
        SetAccessibleName(_preview, "候选镜头剖面比较图");
        _preview.SetValue(
            AutomationProperties.HelpTextProperty,
            "绿色为候选 A 或当前候选，红色为候选 B；图中只显示保存的表面几何。");
        var content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 8
        };
        content.Children.Add(new TextBlock
        {
            Text = "候选结构与比较",
            FontSize = 15,
            FontWeight = FontWeight.SemiBold
        });
        Grid.SetRow(_preview, 1);
        content.Children.Add(_preview);
        var details = new StackPanel
        {
            Spacing = 4,
            Children = { _comparisonDetails, _selectionDetails }
        };
        Grid.SetRow(details, 2);
        content.Children.Add(details);
        return new Border
        {
            Padding = new Thickness(12, 10),
            BorderThickness = new Thickness(1, 0, 0, 0),
            BorderBrush = Brushes.Gray,
            Child = content
        };
    }

    private void WireEvents()
    {
        _validateButton.Click += ValidateClicked;
        _runButton.Click += RunClicked;
        _resumeButton.Click += ResumeClicked;
        _cancelButton.Click += (_, _) => _runCancellation?.Cancel();
        _compareAButton.Click += (_, _) => SetComparison(isPrimary: true);
        _compareBButton.Click += (_, _) => SetComparison(isPrimary: false);
        _exportButton.Click += ExportClicked;
        _dataGrid.SelectionChanged += (_, _) => SelectionChanged();
        SizeChanged += (_, _) => ApplyResponsiveLayout(Bounds.Width);
        Closing += (_, _) => _runCancellation?.Cancel();
        SetAccessibleName(_validateButton, "预检查初始结构规格");
        SetAccessibleName(_runButton, "开始初始结构实验");
        SetAccessibleName(_resumeButton, "恢复最近的初始结构检查点");
        SetAccessibleName(_cancelButton, "取消当前实验");
        SetAccessibleName(_compareAButton, "将当前候选设为比较 A");
        SetAccessibleName(_compareBButton, "将当前候选设为比较 B");
        SetAccessibleName(_exportButton, "导出当前候选为 STAROPT");
    }

    private void ValidateClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        try
        {
            SpecificationValidator.Validate(BuildSpecification());
            _status.Text = "规格预检查通过";
        }
        catch (InitialStructureSpecificationException exception)
        {
            _status.Text = string.Join("；", exception.Errors);
        }
    }

    private async void RunClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (_running || _resumeLoading)
        {
            return;
        }

        await RunSearchAsync(BuildSpecification(), checkpoint: null);
    }

    private async void ResumeClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (_running || _resumeLoading)
        {
            return;
        }

        _resumeLoading = true;
        Interlocked.Increment(ref _resumeRefreshGeneration);
        _resumeButton.IsEnabled = false;
        try
        {
            var checkpoint = await new SearchCheckpointStore().LoadLatestAsync(CheckpointRootDirectory());
            if (checkpoint is null)
            {
                _status.Text = "没有可恢复的检查点";
                return;
            }

            ApplySpecification(checkpoint.Specification);
            await RunSearchAsync(checkpoint.Specification, checkpoint);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or ArgumentException)
        {
            _status.Text = exception.Message;
        }
        finally
        {
            _resumeLoading = false;
            await RefreshResumeAvailabilityAsync();
        }
    }

    private async Task RunSearchAsync(
        InitialStructureSpecification specification,
        SearchCheckpoint? checkpoint)
    {
        if (_running)
        {
            return;
        }

        _runCancellation?.Dispose();
        _runCancellation = new CancellationTokenSource();
        var runCancellation = _runCancellation;
        var runGeneration = Interlocked.Increment(ref _runGeneration);
        _rows.Clear();
        _comparisonA = null;
        _comparisonB = null;
        RefreshComparison();
        SetRunning(true);
        var checkpointStore = new SearchCheckpointStore();

        try
        {
            var progress = new Progress<SearchProgress>(value => Dispatcher.UIThread.Post(() =>
            {
                if (!_running || runGeneration != Volatile.Read(ref _runGeneration))
                {
                    return;
                }

                _progress.Maximum = Math.Max(1, value.Total);
                _progress.Value = Math.Clamp(value.Completed, 0, _progress.Maximum);
                _status.Text = $"{value.Stage} {value.Completed}/{value.Total}";
            }));
            var manifest = await new InitialStructureSearchService().RunAsync(
                specification,
                progress,
                runCancellation.Token,
                checkpoint,
                (value, token) => checkpointStore.SaveAsync(
                    value,
                    CheckpointRootDirectory(),
                    token));
            foreach (var candidate in manifest.Candidates)
            {
                _rows.Add(new CandidateRow(candidate));
            }

            var manifestPath = await new RunDirectoryStore().SaveAsync(
                manifest,
                RunRootDirectory(),
                runCancellation.Token);
            if (manifest.State == SearchRunState.Completed)
            {
                checkpointStore.Delete(CheckpointRootDirectory(), manifest.RunId);
                _status.Text = $"完成 {_rows.Count} 个候选 · {manifestPath}";
            }
            else
            {
                _status.Text = $"运行未完成，检查点可恢复 · {manifestPath}";
            }
        }
        catch (InitialStructureSpecificationException exception)
        {
            _status.Text = string.Join("；", exception.Errors);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "已取消；已完成种子可从最近检查点恢复";
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or InvalidOperationException
            or ArgumentException)
        {
            _status.Text = exception.Message;
        }
        finally
        {
            if (runGeneration == Volatile.Read(ref _runGeneration))
            {
                SetRunning(false);
                await RefreshResumeAvailabilityAsync();
            }
        }
    }

    private async void ExportClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (_dataGrid.SelectedItem is not CandidateRow selected)
        {
            return;
        }

        var target = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出初始结构候选",
            SuggestedFileName = selected.CandidateId + ".staropt",
            DefaultExtension = "staropt",
            FileTypeChoices =
            [
                new FilePickerFileType("STAROPT 工程") { Patterns = ["*.staropt"] }
            ]
        });
        var path = target?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            _exportButton.IsEnabled = false;
            var exported = await new CandidateExportService().ExportStarOptAsync(
                selected.Candidate,
                path,
                _runCancellation?.Token ?? CancellationToken.None);
            _status.Text = $"已导出并回读验证 · {exported}";
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or InvalidOperationException
            or ArgumentException)
        {
            _status.Text = exception.Message;
        }
        finally
        {
            _exportButton.IsEnabled = !_running && _dataGrid.SelectedItem is CandidateRow;
        }
    }

    private void SelectionChanged()
    {
        var selected = _dataGrid.SelectedItem as CandidateRow;
        _selectionDetails.Text = selected?.Details ?? string.Empty;
        _compareAButton.IsEnabled = !_running && selected is not null;
        _compareBButton.IsEnabled = !_running && selected is not null;
        _exportButton.IsEnabled = !_running && selected is not null;
        if (_comparisonA is null)
        {
            _preview.Primary = selected?.Candidate;
        }
    }

    private void SetComparison(bool isPrimary)
    {
        if (_dataGrid.SelectedItem is not CandidateRow selected)
        {
            return;
        }

        if (isPrimary)
        {
            _comparisonA = selected;
        }
        else
        {
            _comparisonB = selected;
        }
        RefreshComparison();
    }

    private void RefreshComparison()
    {
        _preview.Primary = _comparisonA?.Candidate
            ?? (_dataGrid.SelectedItem as CandidateRow)?.Candidate;
        _preview.Secondary = _comparisonB?.Candidate;
        _comparisonDetails.Text = (_comparisonA, _comparisonB) switch
        {
            ({ } a, { } b) =>
                $"A {a.CandidateId}: RMS {a.RmsSpotRadius} mm · B {b.CandidateId}: RMS {b.RmsSpotRadius} mm",
            ({ } a, null) => $"A {a.CandidateId} · 请选择候选 B",
            (null, { } b) => $"B {b.CandidateId} · 请选择候选 A",
            _ => "选择候选后可设为 A/B 比较"
        };
    }

    private InitialStructureSpecification BuildSpecification()
    {
        return new InitialStructureSpecification
        {
            Name = "Lab fixed-focus experiment",
            EffectiveFocalLengthMillimeters = Value(_effectiveFocalLength),
            FNumber = Value(_fNumber),
            MaximumFieldAngleDegrees = Value(_fieldAngle),
            MinimumElementCount = (int)Value(_minimumElements),
            MaximumElementCount = (int)Value(_maximumElements),
            MaximumTrackLengthMillimeters = Value(_maximumTrack),
            MaximumRmsSpotRadiusMillimeters = Value(_rmsLimit),
            MaximumSpotRadiusMillimeters = Value(_maximumSpotLimit),
            Budget = new SearchBudget
            {
                InitialSeedCount = (int)Value(_seedCount),
                MaximumEvaluations = (int)Value(_maximumEvaluations),
                MaximumParallelism = (int)Value(_parallelism),
                RandomSeed = (long)Value(_randomSeed),
                TimeLimit = TimeSpan.FromMinutes(5)
            }
        };
    }

    private void ApplySpecification(InitialStructureSpecification specification)
    {
        _effectiveFocalLength.Value = (decimal)specification.EffectiveFocalLengthMillimeters;
        _fNumber.Value = (decimal)specification.FNumber;
        _fieldAngle.Value = (decimal)specification.MaximumFieldAngleDegrees;
        _minimumElements.Value = specification.MinimumElementCount;
        _maximumElements.Value = specification.MaximumElementCount;
        _maximumTrack.Value = (decimal)specification.MaximumTrackLengthMillimeters;
        _rmsLimit.Value = (decimal)specification.MaximumRmsSpotRadiusMillimeters;
        _maximumSpotLimit.Value = (decimal)specification.MaximumSpotRadiusMillimeters;
        _seedCount.Value = specification.Budget.InitialSeedCount;
        _maximumEvaluations.Value = specification.Budget.MaximumEvaluations;
        _parallelism.Value = specification.Budget.MaximumParallelism;
        _randomSeed.Value = specification.Budget.RandomSeed;
    }

    private void SetRunning(bool running)
    {
        _running = running;
        Interlocked.Increment(ref _resumeRefreshGeneration);
        _validateButton.IsEnabled = !running;
        _runButton.IsEnabled = !running;
        _resumeButton.IsEnabled = false;
        _cancelButton.IsEnabled = running;
        _compareAButton.IsEnabled = !running && _dataGrid.SelectedItem is CandidateRow;
        _compareBButton.IsEnabled = !running && _dataGrid.SelectedItem is CandidateRow;
        _exportButton.IsEnabled = !running && _dataGrid.SelectedItem is CandidateRow;
        if (running)
        {
            _progress.Value = 0;
            _status.Text = "准备候选";
        }
    }

    private async Task RefreshResumeAvailabilityAsync()
    {
        var generation = Interlocked.Increment(ref _resumeRefreshGeneration);
        if (_running || _resumeLoading)
        {
            return;
        }

        try
        {
            var available = await new SearchCheckpointStore().LoadLatestAsync(
                CheckpointRootDirectory()) is not null;
            if (generation == Volatile.Read(ref _resumeRefreshGeneration)
                && !_running
                && !_resumeLoading)
            {
                _resumeButton.IsEnabled = available;
            }
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or ArgumentException)
        {
            if (generation == Volatile.Read(ref _resumeRefreshGeneration))
            {
                _resumeButton.IsEnabled = false;
                _status.Text = exception.Message;
            }
        }
    }

    private void ApplyResponsiveLayout(double width)
    {
        var compact = width < 820;
        _workspace.ColumnDefinitions = compact
            ? new ColumnDefinitions("*")
            : new ColumnDefinitions("3*,2*");
        _workspace.RowDefinitions = compact
            ? new RowDefinitions("*,260")
            : new RowDefinitions("*");
        Grid.SetColumn(_dataGrid, 0);
        Grid.SetRow(_dataGrid, 0);
        Grid.SetColumn(_inspector, compact ? 0 : 1);
        Grid.SetRow(_inspector, compact ? 1 : 0);
        _inspector.BorderThickness = compact
            ? new Thickness(0, 1, 0, 0)
            : new Thickness(1, 0, 0, 0);
    }

    private static string RunRootDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpticalSystemDesign",
        "Labs",
        "InitialStructure",
        "runs");

    private static string CheckpointRootDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpticalSystemDesign",
        "Labs",
        "InitialStructure",
        "checkpoints");

    private static StackPanel Field(string label, Control control)
    {
        SetAccessibleName(control, label);
        return new StackPanel
        {
            Width = 126,
            Margin = new Thickness(4, 2),
            Spacing = 3,
            Children =
            {
                new TextBlock { Text = label, FontSize = 12, Opacity = 0.75 },
                control
            }
        };
    }

    private static Button Command(string label, bool enabled = true) => new()
    {
        Content = label,
        MinWidth = 84,
        IsEnabled = enabled
    };

    private static NumericUpDown Number(
        decimal value,
        decimal minimum,
        decimal maximum,
        decimal increment) => new()
        {
            Value = value,
            Minimum = minimum,
            Maximum = maximum,
            Increment = increment,
            MinWidth = 110,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

    private static DataGridTextColumn TextColumn(
        string header,
        string property,
        double width) => new()
        {
            Header = header,
            Binding = new Binding(property),
            Width = new DataGridLength(width)
        };

    private static double Value(NumericUpDown input) => (double)(input.Value ?? 0);

    private static void SetAccessibleName(AvaloniaObject control, string name)
    {
        control.SetValue(AutomationProperties.NameProperty, name);
    }

    private sealed class CandidateRow
    {
        public CandidateRow(CandidateSnapshot candidate)
        {
            Candidate = candidate;
            CandidateId = candidate.CandidateId;
            Generation = candidate.Lineage.Generation;
            ElementCount = candidate.Lineage.ElementCount;
            StopVariant = candidate.Lineage.StopVariant;
            Status = candidate.Status.ToString();
            EffectiveFocalLength = Format(candidate.Evaluation.EffectiveFocalLengthMillimeters);
            FNumber = Format(candidate.Evaluation.FNumber);
            ValidRayFraction = candidate.Evaluation.ValidRayFraction.ToString("P0");
            RmsSpotRadius = Format(candidate.Evaluation.RmsSpotRadiusMillimeters, "0.######");
            MaximumSpotRadius = Format(candidate.Evaluation.MaximumSpotRadiusMillimeters, "0.######");
            HardViolationCount = candidate.Violations.Count(
                violation => violation.Severity == ConstraintSeverity.Hard);
            Details = candidate.Violations.Count == 0
                ? $"{candidate.Lineage.Operation} · 种子 {candidate.Lineage.SeedIndex}"
                : string.Join("；", candidate.Violations.Select(violation => violation.Message));
        }

        public CandidateSnapshot Candidate { get; }
        public string CandidateId { get; }
        public int Generation { get; }
        public int ElementCount { get; }
        public int StopVariant { get; }
        public string Status { get; }
        public string EffectiveFocalLength { get; }
        public string FNumber { get; }
        public string ValidRayFraction { get; }
        public string RmsSpotRadius { get; }
        public string MaximumSpotRadius { get; }
        public int HardViolationCount { get; }
        public string Details { get; }

        private static string Format(double? value, string format = "0.###") =>
            value?.ToString(format) ?? "-";
    }
}
