using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Manufacturing;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Application.Contracts;

namespace OpticalSystemDesign.App.Panels;

public sealed partial class ManufacturabilityPanel : UserControl, IDisposable
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
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap
    };
    private readonly DataGrid _metricGrid;
    private ManufacturabilityReport? _report;
    private bool _disposed;

    public ManufacturabilityPanel(
        IPrescriptionService prescription,
        IWorkspaceEventStream events)
    {
        _prescription = prescription;
        _events = events;
        _metricGrid = BuildMetricGrid();
        Content = BuildPage();

        _events.Changed += OnWorkspaceChanged;
        UiLocalization.LanguageChanged += OnLanguageChanged;
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
        UiLocalization.LanguageChanged -= OnLanguageChanged;
    }

    private static DataGrid BuildMetricGrid()
    {
        var grid = BaseGrid();
        grid.Columns.Add(Column("元件", nameof(ManufacturabilityGeometryMetric.ElementNumber), 64));
        grid.Columns.Add(Column("表面", nameof(ManufacturabilityGeometryMetric.Surfaces), 86));
        grid.Columns.Add(Column("项目", nameof(ManufacturabilityGeometryMetric.Item), 174));
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "计算值",
            Binding = new Binding(nameof(ManufacturabilityGeometryMetric.Value)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            MinWidth = 185
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "说明",
            Binding = new Binding(nameof(ManufacturabilityGeometryMetric.Note)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            MinWidth = 190
        });
        grid.Name = "ManufacturingMetrics";
        grid.Styles.Add(new Style(selector => selector.OfType<DataGridCell>().Descendant().OfType<TextBlock>())
        {
            Setters =
            {
                new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap),
                new Setter(TextBlock.MarginProperty, new Thickness(8, 6))
            }
        });
        grid.Styles.Add(new Style(selector => selector.OfType<DataGridRow>())
        {
            Setters = { new Setter(MinHeightProperty, Math.Max(38, UiDensity.CompactTableRowHeight)) }
        });
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
        ManufacturabilityReport report;
        try
        {
            report = OpticalManufacturingModel.Evaluate(_prescription.GetSurfaces(), settings);
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            _metricGrid.ItemsSource = null;
            _report = null;
            PresentFailure(exception.Message);
            return;
        }
        _report = report;
        PresentReport(report);
    }

    private static DataGrid BaseGrid() => new()
    {
        AutoGenerateColumns = false,
        IsReadOnly = true,
        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        RowHeight = double.NaN,
        ColumnHeaderHeight = UiDensity.TableHeaderHeight,
        BorderThickness = new Thickness(0)
    };

    private static DataGridTextColumn Column(string header, string property, double width) => new()
    {
        Header = header,
        Binding = new Binding(property),
        Width = new DataGridLength(width)
    };

    private static NumericUpDown Number(decimal value, decimal minimum, decimal maximum, decimal increment) => new()
    {
        Value = value,
        Minimum = minimum,
        Maximum = maximum,
        Increment = increment,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        MinHeight = 34,
        FormatString = "0.###"
    };
}
