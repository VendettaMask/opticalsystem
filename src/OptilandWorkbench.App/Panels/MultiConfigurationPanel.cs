using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.ViewModels;

namespace OptilandWorkbench.App.Panels;

public sealed class MultiConfigurationPanel : UserControl, IDisposable
{
    private readonly IPrescriptionService _prescription;
    private readonly IMultiConfigurationService _configurations;
    private readonly IWorkspaceEventStream _events;
    private readonly DataGrid _configGrid = new()
    {
        AutoGenerateColumns = false,
        CanUserReorderColumns = true,
        CanUserResizeColumns = true,
        GridLinesVisibility = DataGridGridLinesVisibility.All,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        MinHeight = 240
    };
    private readonly ComboBox _surfacePicker = new() { MinWidth = 220 };
    private readonly NumericUpDown _thicknessInput = new()
    {
        Minimum = 0,
        Maximum = 1_000_000,
        Increment = 1,
        Value = 5,
        Width = 110,
        ShowButtonSpinner = false
    };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, Margin = new Avalonia.Thickness(0, 8, 0, 0) };
    private bool _disposed;

    public MultiConfigurationPanel(
        IPrescriptionService prescription,
        IMultiConfigurationService configurations,
        IWorkspaceEventStream events)
    {
        _prescription = prescription;
        _configurations = configurations;
        _events = events;
        _configGrid.BindThemeResource(DataGrid.RowBackgroundProperty, ThemeResourceBindings.Surface);
        ConfigureGrid();
        var addButton = new Button { Content = new LocalIconLabel("plus", "新增配置"), MinWidth = 96 };
        addButton.Click += (_, _) => _configurations.Add();
        var activateButton = new Button { Content = new LocalIconLabel("circle-check", "激活配置"), MinWidth = 96 };
        activateButton.Click += (_, _) =>
        {
            if (_configGrid.SelectedItem is MultiConfigurationRowDto row)
            {
                _configurations.Activate(row.Index);
            }
        };
        var applyButton = new Button { Content = new LocalIconLabel("check", "应用厚度"), MinWidth = 96 };
        applyButton.Click += (_, _) => ApplyThickness();
        var toolbar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Avalonia.Thickness(0, 0, 0, 8),
            Children =
            {
                addButton,
                activateButton,
                Label("表面"),
                _surfacePicker,
                Label("厚度"),
                _thicknessInput,
                applyButton
            }
        };
        var root = new DockPanel { Margin = new Avalonia.Thickness(12) };
        DockPanel.SetDock(toolbar, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(_summary, Avalonia.Controls.Dock.Bottom);
        root.Children.Add(toolbar);
        root.Children.Add(_summary);
        root.Children.Add(_configGrid);
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

    private void ConfigureGrid()
    {
        _configGrid.Columns.Add(new DataGridTextColumn { Header = "#", Binding = new Binding(nameof(MultiConfigurationRowDto.Index)), Width = new DataGridLength(56) });
        _configGrid.Columns.Add(new DataGridTextColumn { Header = "名称", Binding = new Binding(nameof(MultiConfigurationRowDto.Name)), Width = new DataGridLength(120) });
        _configGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "当前", Binding = new Binding(nameof(MultiConfigurationRowDto.Active)), IsReadOnly = true, Width = new DataGridLength(70) });
        _configGrid.Columns.Add(new DataGridTextColumn { Header = "表面数", Binding = new Binding(nameof(MultiConfigurationRowDto.SurfaceCount)), Width = new DataGridLength(86) });
        _configGrid.Columns.Add(new DataGridTextColumn { Header = "总长", Binding = new Binding(nameof(MultiConfigurationRowDto.TotalTrack)), Width = new DataGridLength(100) });
        _configGrid.Columns.Add(new DataGridTextColumn { Header = "有效焦距", Binding = new Binding(nameof(MultiConfigurationRowDto.EffectiveFocalLength)), Width = new DataGridLength(110) });
    }

    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs args) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed)
            {
                Refresh();
            }
        });

    private void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        var rows = _configurations.GetRows();
        _configGrid.ItemsSource = rows;
        if (_configGrid.SelectedItem is null && rows.Count > 0)
        {
            _configGrid.SelectedItem = rows.FirstOrDefault(row => row.Active) ?? rows[0];
        }

        var selectedSurface = (_surfacePicker.SelectedItem as SurfaceEditorRow)?.Number;
        var surfaces = _prescription.GetSurfaces().Select(surface => new SurfaceEditorRow(surface)).ToArray();
        _surfacePicker.ItemsSource = surfaces;
        _surfacePicker.SelectedItem = surfaces.FirstOrDefault(surface => surface.Number == selectedSurface)
            ?? surfaces.ElementAtOrDefault(Math.Min(2, Math.Max(0, surfaces.Length - 1)));
        _summary.Text = $"配置数：{rows.Count}    当前系统表面数：{surfaces.Length}";
    }

    private void ApplyThickness()
    {
        if (_configGrid.SelectedItem is not MultiConfigurationRowDto configuration
            || _surfacePicker.SelectedItem is not SurfaceEditorRow surface)
        {
            _summary.Text = "请先选择配置和表面。";
            return;
        }

        var thickness = _thicknessInput.Value.HasValue
            ? decimal.ToDouble(_thicknessInput.Value.Value)
            : surface.Thickness;
        _configurations.SetThickness(configuration.Index, surface.Number, thickness);
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Avalonia.Thickness(12, 0, 4, 0)
    };
}
