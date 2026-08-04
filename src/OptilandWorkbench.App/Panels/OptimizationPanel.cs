using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.Theming;
using OptilandWorkbench.App.ViewModels;

namespace OptilandWorkbench.App.Panels;

public sealed class OptimizationPanel : UserControl, IDisposable, IDisplaySettingsAware
{
    private readonly IPrescriptionService _prescription;
    private readonly IOptimizationService _optimization;
    private readonly IWorkspaceEventStream _events;
    private readonly ObservableCollection<MeritOperandEditorRow> _rows = new();
    private readonly DataGrid _grid;
    private readonly string[] _operandCodes;
    private readonly ComboBox _optimizerPicker = new() { MinWidth = 165, SelectedIndex = 0 };
    private readonly NumericUpDown _iterationsInput = new()
    {
        Minimum = 1,
        Maximum = 1000,
        Increment = 10,
        Value = 30,
        Width = 88,
        ShowButtonSpinner = false
    };
    private readonly TextBlock _summary = new()
    {
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock _variables = new()
    {
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly OperationStatusBar _operationStatus = new();
    private readonly TextBlock _result = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Avalonia.Thickness(8, 4)
    };
    private CancellationTokenSource? _runCancellation;
    private int _generation;
    private bool _refreshing;
    private bool _disposed;

    public OptimizationPanel(
        IPrescriptionService prescription,
        IOptimizationService optimization,
        IWorkspaceEventStream events)
    {
        _prescription = prescription;
        _optimization = optimization;
        _events = events;
        _operandCodes = optimization.GetMeritOperandTypes().Select(type => type.Code).ToArray();
        _optimizerPicker.ItemsSource = optimization.OptimizerNames;
        _grid = CreateGrid();

        var addButton = CommandButton("plus", "添加");
        addButton.Click += (_, _) => AddOperand();
        var removeButton = CommandButton("trash-2", "删除");
        removeButton.Click += (_, _) => RemoveSelected();
        var upButton = CommandButton("arrow-up", "上移");
        upButton.Click += (_, _) => MoveSelected(-1);
        var downButton = CommandButton("arrow-down", "下移");
        downButton.Click += (_, _) => MoveSelected(1);
        var wizardButton = CommandButton("sparkles", "优化向导");
        wizardButton.Click += async (_, _) => await ShowWizardAsync();
        var refreshButton = CommandButton("refresh-cw", "重新计算");
        refreshButton.Click += (_, _) => Refresh();
        var runButton = CommandButton("play", "执行优化");
        runButton.Click += async (_, _) => await RunAsync();

        var editToolbar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                addButton,
                removeButton,
                upButton,
                downButton,
                Separator(),
                wizardButton,
                refreshButton
            }
        };
        var runToolbar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new TextBlock { Text = "算法", VerticalAlignment = VerticalAlignment.Center },
                _optimizerPicker,
                new TextBlock { Text = "迭代", VerticalAlignment = VerticalAlignment.Center },
                _iterationsInput,
                runButton,
                _variables,
                _operationStatus
            }
        };
        var toolbar = new StackPanel
        {
            Spacing = 6,
            Children = { editToolbar, runToolbar, _summary }
        };
        var commandBar = new Border
        {
            BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
            Padding = new Avalonia.Thickness(10, 6),
            Child = toolbar
        };
        var resultBorder = new Border
        {
            BorderThickness = new Avalonia.Thickness(0, 1, 0, 0),
            Child = _result
        };
        _summary.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        _variables.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.MutedText);
        commandBar.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        commandBar.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        resultBorder.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        resultBorder.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        var root = new DockPanel();
        root.BindThemeResource(Panel.BackgroundProperty, ThemeResourceBindings.Workspace);
        DockPanel.SetDock(commandBar, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(resultBorder, Avalonia.Controls.Dock.Bottom);
        root.Children.Add(commandBar);
        root.Children.Add(resultBorder);
        root.Children.Add(_grid);
        Content = root;

        _events.Changed += OnWorkspaceChanged;
        Refresh();
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
        _runCancellation?.Cancel();
        _runCancellation?.Dispose();
        _operationStatus.Dispose();
    }

    public void RefreshDisplaySettings() => Refresh();

    private DataGrid CreateGrid()
    {
        var grid = new DataGrid
        {
            ItemsSource = _rows,
            AutoGenerateColumns = false,
            CanUserReorderColumns = true,
            CanUserResizeColumns = true,
            GridLinesVisibility = DataGridGridLinesVisibility.All,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            SelectionMode = DataGridSelectionMode.Extended,
            IsReadOnly = false,
            RowHeight = 27,
            ColumnHeaderHeight = 30,
            FrozenColumnCount = 2,
            BorderThickness = new Avalonia.Thickness(1),
            RowBackground = new SolidColorBrush(MeritOperandRowPalette.Resolve(null))
        };
        grid.BindThemeResource(DataGrid.BorderBrushProperty, ThemeResourceBindings.Border);
        grid.Styles.Add(new Style(selector => selector
            .OfType<DataGridRow>()
            .Class("merit-color-row")
            .Class(":selected"))
        {
            Setters =
            {
                new Setter(
                    DataGridRow.BorderBrushProperty,
                    new DynamicResourceExtension("AccentFillColorDefaultBrush"))
            }
        });
        grid.LoadingRow += (_, args) => ApplyRowAppearance(args.Row);
        grid.Columns.Add(TextColumn("#", nameof(MeritOperandEditorRow.Index), 44, true));
        grid.Columns.Add(TypeColumn());
        grid.Columns.Add(TextColumn("表面", nameof(MeritOperandEditorRow.Surface), 62));
        grid.Columns.Add(TextColumn("视场", nameof(MeritOperandEditorRow.Field), 62));
        grid.Columns.Add(TextColumn("波长", nameof(MeritOperandEditorRow.Wavelength), 62));
        grid.Columns.Add(TextColumn("Hx", nameof(MeritOperandEditorRow.Hx), 68));
        grid.Columns.Add(TextColumn("Hy", nameof(MeritOperandEditorRow.Hy), 68));
        grid.Columns.Add(TextColumn("Px", nameof(MeritOperandEditorRow.Px), 68));
        grid.Columns.Add(TextColumn("Py", nameof(MeritOperandEditorRow.Py), 68));
        grid.Columns.Add(TextColumn("目标", nameof(MeritOperandEditorRow.Target), 88));
        grid.Columns.Add(TextColumn("权重", nameof(MeritOperandEditorRow.Weight), 82));
        grid.Columns.Add(TextColumn("当前值", nameof(MeritOperandEditorRow.ValueDisplay), 104, true));
        grid.Columns.Add(TextColumn("贡献", nameof(MeritOperandEditorRow.ContributionDisplay), 104, true));
        grid.Columns.Add(TextColumn("注释", nameof(MeritOperandEditorRow.Comment), 260));
        grid.Columns.Add(TextColumn("状态", nameof(MeritOperandEditorRow.Error), 200, true));
        grid.CellEditEnded += (_, args) =>
        {
            if (args.EditAction == DataGridEditAction.Commit)
            {
                PersistRows();
            }
        };
        return grid;
    }

    private DataGridTemplateColumn TypeColumn() => new()
    {
        Header = "类型",
        Width = new DataGridLength(92),
        CellTemplate = new FuncDataTemplate<MeritOperandEditorRow>((row, _) =>
        {
            var picker = new ComboBox
            {
                ItemsSource = _operandCodes,
                SelectedItem = row?.Type,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            picker.SelectionChanged += (_, _) =>
            {
                if (row is null || picker.SelectedItem is not string type || row.Type == type)
                {
                    return;
                }

                row.Type = type;
                row.Enabled = !row.IsBlank;
                PersistRows();
            };
            return picker;
        })
    };

    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs args)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed)
            {
                Refresh();
            }
        });
    }

    private void Refresh()
    {
        if (_disposed || _refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            var selectedIndex = (_grid.SelectedItem as MeritOperandEditorRow)?.Index ?? -1;
            var data = _optimization.GetMeritFunction();
            _rows.Clear();
            foreach (var operand in data)
            {
                _rows.Add(new MeritOperandEditorRow(operand));
            }

            _grid.SelectedItem = _rows.FirstOrDefault(row => row.Index == selectedIndex);
            var activeRows = _rows.Where(row => row.Enabled && !row.IsBlank).ToArray();
            var merit = activeRows
                .Where(row => double.IsFinite(row.Contribution))
                .Sum(row => row.Contribution);
            var errors = activeRows.Count(row => !string.IsNullOrWhiteSpace(row.Error));
            _summary.Text = _rows.Count == 0
                ? "评价函数为空。可添加操作数，或生成默认 RMS 点列/波前评价函数。"
                : $"操作数：{activeRows.Length}　评价函数：{NumericDisplayFormatter.Format(merit)}"
                  + (errors > 0 ? $"　错误：{errors}" : string.Empty);
            UpdateVariableSummary();
            if (_optimizerPicker.SelectedItem is null && _optimization.OptimizerNames.Count > 0)
            {
                _optimizerPicker.SelectedIndex = 0;
            }
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void AddOperand()
    {
        _rows.Add(new MeritOperandEditorRow(new MeritOperandRowDto(
            _rows.Count + 1,
            true,
            "RSCE",
            0,
            1,
            0,
            0,
            0,
            0,
            0,
            0,
            1,
            0,
            0,
            string.Empty)));
        _grid.SelectedItem = _rows[^1];
        PersistRows();
    }

    private async Task ShowWizardAsync()
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        await new OptimizationWizardWindow(_prescription, _optimization).ShowDialog<bool>(owner);
    }

    private void RemoveSelected()
    {
        var selected = _grid.SelectedItems.Cast<MeritOperandEditorRow>().ToArray();
        if (selected.Length == 0 && _grid.SelectedItem is MeritOperandEditorRow row)
        {
            selected = new[] { row };
        }

        foreach (var operand in selected)
        {
            _rows.Remove(operand);
        }

        PersistRows();
    }

    private void MoveSelected(int offset)
    {
        if (_grid.SelectedItem is not MeritOperandEditorRow row)
        {
            return;
        }

        var currentIndex = _rows.IndexOf(row);
        var nextIndex = Math.Clamp(currentIndex + offset, 0, _rows.Count - 1);
        if (currentIndex == nextIndex)
        {
            return;
        }

        _rows.Move(currentIndex, nextIndex);
        _grid.SelectedItem = row;
        PersistRows();
    }

    private void PersistRows()
    {
        if (_refreshing || _disposed)
        {
            return;
        }

        for (var index = 0; index < _rows.Count; index++)
        {
            _rows[index].Index = index + 1;
        }

        _optimization.SetMeritFunction(_rows.Select(row => row.ToDto()).ToArray());
    }

    private async Task RunAsync()
    {
        if (SelectedVariables().Count == 0)
        {
            _operationStatus.MarkFailed("缺少可优化变量");
            _result.Text = "请先在镜头数据中勾选至少一个 R 变量或 T 变量。";
            return;
        }

        if (_rows.All(row => !row.Enabled || row.IsBlank))
        {
            _operationStatus.MarkFailed("缺少启用的评价函数操作数");
            _result.Text = "请先添加至少一个启用的评价函数操作数。";
            return;
        }

        _runCancellation?.Cancel();
        _runCancellation?.Dispose();
        _runCancellation = new CancellationTokenSource();
        var cancellationToken = _runCancellation.Token;
        var generation = ++_generation;
        var optimizer = _optimizerPicker.SelectedItem as string
            ?? _optimization.OptimizerNames.FirstOrDefault()
            ?? "Orthogonal Descent";
        var iterations = _iterationsInput.Value.HasValue
            ? decimal.ToInt32(_iterationsInput.Value.Value)
            : 80;
        _result.Text = "正在根据当前评价函数优化…";
        _operationStatus.Start("正在优化…", () => _runCancellation?.Cancel());

        try
        {
            var result = await _optimization.OptimizeVariablesAsync(
                optimizer,
                iterations,
                cancellationToken);
            if (_disposed || cancellationToken.IsCancellationRequested || generation != _generation)
            {
                return;
            }

            _result.Text =
                $"{result.Message}　评价函数：{NumericDisplayFormatter.Format(result.InitialMerit)} → {NumericDisplayFormatter.Format(result.FinalMerit)}　" +
                $"迭代：{result.Iterations}";
            _operationStatus.MarkSynced("优化完成");
            Refresh();
        }
        catch (OperationCanceledException)
        {
            if (!_disposed && generation == _generation)
            {
                _operationStatus.MarkStale("优化已取消");
            }
        }
        catch (Exception exception)
        {
            if (!_disposed && generation == _generation)
            {
                _operationStatus.MarkFailed($"优化失败：{exception.Message}");
                _result.Text = $"优化失败：{exception.Message}";
            }
        }
    }

    private void UpdateVariableSummary()
    {
        var selectedVariables = SelectedVariables();
        _variables.Text = selectedVariables.Count == 0
            ? "未设置变量"
            : $"变量 {selectedVariables.Count} 个";
    }

    private IReadOnlyList<string> SelectedVariables()
    {
        var surfaces = _prescription.GetSurfaces();
        var lastSurfaceNumber = surfaces.Count == 0 ? -1 : surfaces.Max(surface => surface.Number);
        return surfaces
            .Where(surface => surface.Number > 0 && surface.Number < lastSurfaceNumber)
            .SelectMany(surface => new[]
            {
                surface.RadiusVariable ? $"面 {surface.Number} 半径" : null,
                surface.ThicknessVariable ? $"面 {surface.Number} 厚度" : null
            })
            .OfType<string>()
            .ToArray();
    }

    private static DataGridTextColumn TextColumn(string header, string property, double width, bool readOnly = false) => new()
    {
        Header = header,
        Binding = new Binding(property) { Mode = readOnly ? BindingMode.OneWay : BindingMode.TwoWay },
        Width = new DataGridLength(width),
        IsReadOnly = readOnly
    };

    private static Button CommandButton(string iconName, string text) => new()
    {
        Content = new LocalIconLabel(iconName, text),
        MinWidth = 74
    };

    private static Separator Separator()
    {
        var separator = new Separator
        {
            Width = 1,
            Height = 28,
            Margin = new Avalonia.Thickness(4, 0)
        };
        separator.BindThemeResource(Avalonia.Controls.Separator.BackgroundProperty, ThemeResourceBindings.Border);
        return separator;
    }

    private static void ApplyRowAppearance(DataGridRow row)
    {
        if (row.DataContext is not MeritOperandEditorRow operand)
        {
            return;
        }

        row.Classes.Set("merit-color-row", true);
        var visual = MeritOperandRowPalette.ResolveVisual(
            operand.Type,
            !string.IsNullOrWhiteSpace(operand.Error),
            IsekaiTheme.IsDarkLike(row.ActualThemeVariant));
        row.Background = new SolidColorBrush(visual.Background);
        row.Foreground = new SolidColorBrush(visual.Foreground);
    }
}
