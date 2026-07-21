using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Manufacturing;

namespace OptilandWorkbench.App.Panels;

public sealed class ManufacturabilityPanel : UserControl, IDisposable
{
    private readonly IPrescriptionService _prescription;
    private readonly IWorkspaceEventStream _events;
    private readonly NumericUpDown _minimumCenterThickness = Number(1, 0.1m, 20, 0.1m);
    private readonly NumericUpDown _minimumEdgeThickness = Number(0.8m, 0.1m, 20, 0.1m);
    private readonly NumericUpDown _maximumAspectRatio = Number(25, 1, 200, 1);
    private readonly NumericUpDown _minimumRadiusRatio = Number(0.55m, 0.05m, 5, 0.05m);
    private readonly NumericUpDown _maximumEdgeSlope = Number(60, 1, 89, 1);
    private readonly TextBlock _summary = new()
    {
        FontWeight = FontWeight.SemiBold,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock _basis = new()
    {
        Text = "规则用于设计阶段筛查，最终结论应由加工和检验人员审核。",
        Foreground = new SolidColorBrush(Color.FromRgb(99, 99, 102)),
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly DataGrid _grid;
    private bool _disposed;

    public ManufacturabilityPanel(
        IPrescriptionService prescription,
        IWorkspaceEventStream events)
    {
        _prescription = prescription;
        _events = events;
        _grid = BuildGrid();

        var evaluate = new Button
        {
            Content = new LocalIconLabel("clipboard-check", "重新评估"),
            MinWidth = 108,
            Height = 32
        };
        evaluate.Click += (_, _) => Refresh();

        var settings = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 6),
            Children =
            {
                Label("最小中心厚度 (mm)"), _minimumCenterThickness,
                Label("最小边厚 (mm)"), _minimumEdgeThickness,
                Label("最大 D/CT"), _maximumAspectRatio,
                Label("最小 |R|/D"), _minimumRadiusRatio,
                Label("最大边缘斜率 (°)"), _maximumEdgeSlope,
                evaluate
            }
        };
        var settingsBand = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(248, 248, 250)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(209, 209, 214)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = settings
        };
        var summaryBand = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(250, 250, 252)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(209, 209, 214)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 7),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 20,
                Children = { _summary, _basis }
            }
        };

        var root = new DockPanel();
        DockPanel.SetDock(settingsBand, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(summaryBand, Avalonia.Controls.Dock.Bottom);
        root.Children.Add(settingsBand);
        root.Children.Add(summaryBand);
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
        _events.Changed -= OnWorkspaceChanged;
    }

    private DataGrid BuildGrid()
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            GridLinesVisibility = DataGridGridLinesVisibility.All,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowHeight = 30,
            ColumnHeaderHeight = 32,
            BorderThickness = new Thickness(0),
            HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(229, 229, 234)),
            VerticalGridLinesBrush = new SolidColorBrush(Color.FromRgb(229, 229, 234))
        };
        grid.Columns.Add(Column("元件", nameof(ManufacturabilityFinding.ElementNumber), 64));
        grid.Columns.Add(Column("表面", nameof(ManufacturabilityFinding.Surfaces), 86));
        grid.Columns.Add(Column("结论", nameof(ManufacturabilityFinding.SeverityText), 92));
        grid.Columns.Add(Column("检查项目", nameof(ManufacturabilityFinding.Check), 150));
        grid.Columns.Add(Column("实测/计算值", nameof(ManufacturabilityFinding.MeasuredValue), 180));
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "工艺建议",
            Binding = new Binding(nameof(ManufacturabilityFinding.Recommendation)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        grid.LoadingRow += (_, args) =>
        {
            if (args.Row.DataContext is not ManufacturabilityFinding finding)
            {
                return;
            }

            args.Row.Background = finding.Severity switch
            {
                ManufacturabilitySeverity.Error => new SolidColorBrush(Color.FromRgb(255, 232, 230)),
                ManufacturabilitySeverity.Warning => new SolidColorBrush(Color.FromRgb(255, 247, 220)),
                _ => new SolidColorBrush(Color.FromRgb(232, 247, 237))
            };
        };
        return grid;
    }

    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs args)
    {
        Dispatcher.UIThread.Post(Refresh);
    }

    private void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        var settings = new ManufacturabilitySettings(
            (double)(_minimumCenterThickness.Value ?? 1),
            (double)(_minimumEdgeThickness.Value ?? 0.8m),
            (double)(_maximumAspectRatio.Value ?? 25),
            (double)(_minimumRadiusRatio.Value ?? 0.55m),
            (double)(_maximumEdgeSlope.Value ?? 60));
        var report = OpticalManufacturingModel.Evaluate(_prescription.GetSurfaces(), settings);
        _grid.ItemsSource = report.Findings;
        _summary.Text = report.Elements.Count == 0
            ? "没有识别到可评估的玻璃元件"
            : $"元件 {report.Elements.Count} · 不可加工 {report.ErrorCount} · 需评审 {report.WarningCount} · 通过 {report.PassCount}";
    }

    private static DataGridTextColumn Column(string header, string property, double width) => new()
    {
        Header = header,
        Binding = new Binding(property),
        Width = new DataGridLength(width)
    };

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        Margin = new Thickness(8, 0, 4, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    private static NumericUpDown Number(decimal value, decimal minimum, decimal maximum, decimal increment) => new()
    {
        Value = value,
        Minimum = minimum,
        Maximum = maximum,
        Increment = increment,
        Width = 86,
        Height = 30,
        FormatString = "0.###"
    };
}
