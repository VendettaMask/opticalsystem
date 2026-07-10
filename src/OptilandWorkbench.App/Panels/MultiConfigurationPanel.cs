using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using OptilandWorkbench.App.Connectors;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.App.Panels;

public sealed class MultiConfigurationPanel : UserControl
{
    private readonly OptilandConnector _connector;
    private readonly DataGrid _configGrid = new()
    {
        AutoGenerateColumns = false,
        CanUserReorderColumns = true,
        CanUserResizeColumns = true,
        GridLinesVisibility = DataGridGridLinesVisibility.All,
        HeadersVisibility = DataGridHeadersVisibility.Column,
        RowBackground = Brushes.White,
        MinHeight = 240
    };
    private readonly ComboBox _surfacePicker = new() { MinWidth = 220 };
    private readonly NumericUpDown _thicknessInput = new()
    {
        Minimum = 0,
        Maximum = 1_000_000,
        Increment = 1,
        Value = 5,
        Width = 110
    };
    private readonly TextBlock _summary = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Avalonia.Thickness(0, 8, 0, 0)
    };

    public MultiConfigurationPanel(OptilandConnector connector)
    {
        _connector = connector;
        ConfigureGrid();

        var addButton = new Button { Content = "新增配置", MinWidth = 96 };
        addButton.Click += (_, _) =>
        {
            _connector.AddMultiConfiguration();
            Refresh();
        };

        var activateButton = new Button { Content = "激活配置", MinWidth = 96 };
        activateButton.Click += (_, _) =>
        {
            if (_configGrid.SelectedItem is MultiConfigurationRow row)
            {
                _connector.ActivateMultiConfiguration(row.Index);
                Refresh();
            }
        };

        var applyThicknessButton = new Button { Content = "应用厚度", MinWidth = 96 };
        applyThicknessButton.Click += (_, _) => ApplyThickness();

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
                applyThicknessButton
            }
        };

        var root = new DockPanel { Margin = new Avalonia.Thickness(12) };
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(_summary, Dock.Bottom);
        root.Children.Add(toolbar);
        root.Children.Add(_summary);
        root.Children.Add(_configGrid);
        Content = root;

        _connector.OpticLoaded += (_, _) => Refresh();
        _connector.OpticChanged += (_, _) => Refresh();
        Refresh();
    }

    private void ConfigureGrid()
    {
        _configGrid.Columns.Add(new DataGridTextColumn { Header = "#", Binding = new Binding(nameof(MultiConfigurationRow.Index)), Width = new DataGridLength(56) });
        _configGrid.Columns.Add(new DataGridTextColumn { Header = "名称", Binding = new Binding(nameof(MultiConfigurationRow.Name)), Width = new DataGridLength(120) });
        _configGrid.Columns.Add(new DataGridCheckBoxColumn { Header = "当前", Binding = new Binding(nameof(MultiConfigurationRow.Active)), IsReadOnly = true, Width = new DataGridLength(70) });
        _configGrid.Columns.Add(new DataGridTextColumn { Header = "表面数", Binding = new Binding(nameof(MultiConfigurationRow.SurfaceCount)), Width = new DataGridLength(86) });
        _configGrid.Columns.Add(new DataGridTextColumn { Header = "总长", Binding = new Binding(nameof(MultiConfigurationRow.TotalTrack)), Width = new DataGridLength(100) });
        _configGrid.Columns.Add(new DataGridTextColumn { Header = "有效焦距", Binding = new Binding(nameof(MultiConfigurationRow.EffectiveFocalLength)), Width = new DataGridLength(110) });
    }

    private void Refresh()
    {
        var rows = _connector.GetMultiConfigurationRows();
        _configGrid.ItemsSource = rows;
        _surfacePicker.ItemsSource = _connector.Surfaces;
        if (_configGrid.SelectedItem is null && rows.Count > 0)
        {
            _configGrid.SelectedIndex = rows.ToList().FindIndex(row => row.Active);
            if (_configGrid.SelectedIndex < 0)
            {
                _configGrid.SelectedIndex = 0;
            }
        }

        if (_surfacePicker.SelectedItem is null && _connector.Surfaces.Count > 0)
        {
            _surfacePicker.SelectedIndex = Math.Min(2, _connector.Surfaces.Count - 1);
        }

        _summary.Text = $"配置数：{rows.Count}    当前系统表面数：{_connector.Surfaces.Count}";
    }

    private void ApplyThickness()
    {
        if (_configGrid.SelectedItem is not MultiConfigurationRow config || _surfacePicker.SelectedItem is not OpticalSurface surface)
        {
            _summary.Text = "请先选择配置和表面。";
            return;
        }

        var thickness = _thicknessInput.Value.HasValue
            ? decimal.ToDouble(_thicknessInput.Value.Value)
            : surface.Thickness;
        _connector.SetMultiConfigurationThickness(config.Index, surface.Number, thickness);
        Refresh();
    }

    private static TextBlock Label(string text)
    {
        return new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(12, 0, 4, 0)
        };
    }
}
