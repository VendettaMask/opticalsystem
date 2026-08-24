using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;
using OptilandWorkbench.InitialStructure.Persistence;

namespace OptilandWorkbench.InitialStructure.App;

public sealed class MainWindow : Window
{
    private readonly NumericUpDown _effectiveFocalLength = Number(50, 1, 1000, 1);
    private readonly NumericUpDown _fNumber = Number(4, 0.5m, 64, 0.1m);
    private readonly NumericUpDown _fieldAngle = Number(10, 0, 90, 0.5m);
    private readonly NumericUpDown _minimumElements = Number(3, 3, 8, 1);
    private readonly NumericUpDown _maximumElements = Number(3, 3, 8, 1);
    private readonly NumericUpDown _seedCount = Number(24, 1, 256, 1);
    private readonly NumericUpDown _randomSeed = Number(1, 0, 1_000_000, 1);
    private readonly Button _runButton = new() { Content = "开始", MinWidth = 84 };
    private readonly Button _cancelButton = new() { Content = "取消", MinWidth = 84, IsEnabled = false };
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, Height = 4 };
    private readonly TextBlock _status = new() { Text = "就绪", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _selectionDetails = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ObservableCollection<CandidateRow> _rows = [];
    private CancellationTokenSource? _runCancellation;

    public MainWindow()
    {
        Title = "智能初始结构实验室";
        Width = 1080;
        Height = 720;
        MinWidth = 560;
        MinHeight = 440;
        Content = BuildContent();
        _runButton.Click += RunClicked;
        _cancelButton.Click += (_, _) => _runCancellation?.Cancel();
        Closing += (_, _) => _runCancellation?.Cancel();
        SetAccessibleName(_runButton, "开始初始结构实验");
        SetAccessibleName(_cancelButton, "取消当前实验");
    }

    private Control BuildContent()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto")
        };

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
                        Text = "实验版本 · paraxial-expansion/v1",
                        Opacity = 0.7
                    }
                }
            }
        };
        root.Children.Add(titleBand);

        var parameterBand = new Border
        {
            Padding = new Thickness(12, 10),
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
                    Field("种子数", _seedCount),
                    Field("随机种子", _randomSeed)
                }
            }
        };
        Grid.SetRow(parameterBand, 1);
        root.Children.Add(parameterBand);

        var dataGrid = new DataGrid
        {
            ItemsSource = _rows,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserResizeColumns = true,
            CanUserSortColumns = true,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            SelectionMode = DataGridSelectionMode.Single,
            Margin = new Thickness(12, 10, 12, 0),
            Columns =
            {
                TextColumn("候选", nameof(CandidateRow.CandidateId), 190),
                TextColumn("片数", nameof(CandidateRow.ElementCount), 64),
                TextColumn("状态", nameof(CandidateRow.Status), 92),
                TextColumn("焦距 mm", nameof(CandidateRow.EffectiveFocalLength), 110),
                TextColumn("有效光线", nameof(CandidateRow.ValidRayFraction), 96),
                TextColumn("RMS mm", nameof(CandidateRow.RmsSpotRadius), 110),
                TextColumn("硬约束", nameof(CandidateRow.HardViolationCount), 84)
            }
        };
        dataGrid.SelectionChanged += (_, _) =>
        {
            _selectionDetails.Text = dataGrid.SelectedItem is CandidateRow selected
                ? selected.Details
                : string.Empty;
        };
        SetAccessibleName(dataGrid, "初始结构候选列表");
        Grid.SetRow(dataGrid, 2);
        root.Children.Add(dataGrid);

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            Margin = new Thickness(12, 8, 12, 12),
            ColumnSpacing = 8,
            RowSpacing = 6
        };
        footer.Children.Add(_runButton);
        Grid.SetColumn(_cancelButton, 1);
        footer.Children.Add(_cancelButton);
        Grid.SetColumn(_selectionDetails, 2);
        footer.Children.Add(_selectionDetails);
        Grid.SetColumn(_status, 3);
        footer.Children.Add(_status);
        Grid.SetRow(_progress, 1);
        Grid.SetColumnSpan(_progress, 4);
        footer.Children.Add(_progress);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        return root;
    }

    private async void RunClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        _runCancellation?.Dispose();
        _runCancellation = new CancellationTokenSource();
        _rows.Clear();
        SetRunning(true);

        try
        {
            var specification = BuildSpecification();
            var progress = new Progress<SearchProgress>(value => Dispatcher.UIThread.Post(() =>
            {
                _progress.Maximum = Math.Max(1, value.Total);
                _progress.Value = value.Completed;
                _status.Text = $"{value.Stage} {value.Completed}/{value.Total}";
            }));
            var service = new InitialStructureSearchService();
            var manifest = await service.RunAsync(
                specification,
                progress,
                _runCancellation.Token);
            foreach (var candidate in manifest.Candidates)
            {
                _rows.Add(new CandidateRow(candidate));
            }

            var store = new RunDirectoryStore();
            var manifestPath = await store.SaveAsync(
                manifest,
                RunRootDirectory(),
                _runCancellation.Token);
            _status.Text = $"完成 {_rows.Count} 个候选 · {manifestPath}";
        }
        catch (InitialStructureSpecificationException exception)
        {
            _status.Text = string.Join("；", exception.Errors);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "已取消";
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or ArgumentException)
        {
            _status.Text = exception.Message;
        }
        finally
        {
            SetRunning(false);
        }
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
            Budget = new SearchBudget
            {
                InitialSeedCount = (int)Value(_seedCount),
                MaximumEvaluations = Math.Max(256, (int)Value(_seedCount) * 20),
                MaximumParallelism = Math.Max(1, Environment.ProcessorCount / 2),
                RandomSeed = (long)Value(_randomSeed),
                TimeLimit = TimeSpan.FromMinutes(5)
            }
        };
    }

    private void SetRunning(bool running)
    {
        _runButton.IsEnabled = !running;
        _cancelButton.IsEnabled = running;
        if (running)
        {
            _progress.Value = 0;
            _status.Text = "准备候选";
        }
    }

    private static string RunRootDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpticalSystemDesign",
            "Labs",
            "InitialStructure",
            "runs");
    }

    private static StackPanel Field(string label, Control control)
    {
        return new StackPanel
        {
            Width = 132,
            Margin = new Thickness(4, 2),
            Spacing = 3,
            Children =
            {
                new TextBlock { Text = label, FontSize = 12, Opacity = 0.75 },
                control
            }
        };
    }

    private static NumericUpDown Number(
        decimal value,
        decimal minimum,
        decimal maximum,
        decimal increment)
    {
        return new NumericUpDown
        {
            Value = value,
            Minimum = minimum,
            Maximum = maximum,
            Increment = increment,
            MinWidth = 116,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    private static DataGridTextColumn TextColumn(
        string header,
        string property,
        double width)
    {
        return new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(property),
            Width = new DataGridLength(width)
        };
    }

    private static double Value(NumericUpDown input) => (double)(input.Value ?? 0);

    private static void SetAccessibleName(AvaloniaObject control, string name)
    {
        control.SetValue(AutomationProperties.NameProperty, name);
    }

    private sealed class CandidateRow
    {
        public CandidateRow(CandidateSnapshot candidate)
        {
            CandidateId = candidate.CandidateId;
            ElementCount = candidate.Lineage.ElementCount;
            Status = candidate.Status.ToString();
            EffectiveFocalLength = candidate.Evaluation.EffectiveFocalLengthMillimeters?.ToString("0.###") ?? "-";
            ValidRayFraction = candidate.Evaluation.ValidRayFraction.ToString("P0");
            RmsSpotRadius = candidate.Evaluation.RmsSpotRadiusMillimeters?.ToString("0.######") ?? "-";
            HardViolationCount = candidate.Violations.Count(
                violation => violation.Severity == ConstraintSeverity.Hard);
            Details = candidate.Violations.Count == 0
                ? $"种子 {candidate.Lineage.SeedIndex} · 光阑变体 {candidate.Lineage.StopVariant}"
                : string.Join("；", candidate.Violations.Select(violation => violation.Message));
        }

        public string CandidateId { get; }

        public int ElementCount { get; }

        public string Status { get; }

        public string EffectiveFocalLength { get; }

        public string ValidRayFraction { get; }

        public string RmsSpotRadius { get; }

        public int HardViolationCount { get; }

        public string Details { get; }
    }
}
