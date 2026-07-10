using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using OptilandWorkbench.App.Connectors;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.App.Panels;

public sealed class SystemPropertiesPanel : UserControl
{
    private readonly OptilandConnector _connector;
    private readonly DataGrid _fieldsGrid;
    private readonly DataGrid _wavelengthsGrid;
    private readonly ComboBox _backendPicker = new() { MinWidth = 150 };
    private readonly ComboBox _apertureKindPicker = new() { MinWidth = 190 };
    private readonly NumericUpDown _apertureValue = new()
    {
        Minimum = 0.001m,
        Maximum = 1_000_000m,
        Increment = 1m,
        Value = 14m,
        Width = 110
    };

    public SystemPropertiesPanel(OptilandConnector connector)
    {
        _connector = connector;
        _fieldsGrid = CreateFieldsGrid();
        _wavelengthsGrid = CreateWavelengthsGrid();

        var addField = new Button { Content = "添加视场", MinWidth = 96 };
        addField.Click += (_, _) => _connector.AddField();

        var addWavelength = new Button { Content = "添加波长", MinWidth = 112 };
        addWavelength.Click += (_, _) => _connector.AddWavelength();

        var root = new Grid
        {
            Margin = new Avalonia.Thickness(12),
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,*")
        };

        var systemControls = BuildSystemControls();
        var fieldHeader = BuildHeader("视场", addField);
        var wavelengthHeader = BuildHeader("波长", addWavelength);

        Grid.SetRow(systemControls, 0);
        Grid.SetRow(fieldHeader, 1);
        Grid.SetRow(_fieldsGrid, 2);
        Grid.SetRow(wavelengthHeader, 3);
        Grid.SetRow(_wavelengthsGrid, 4);

        root.Children.Add(systemControls);
        root.Children.Add(fieldHeader);
        root.Children.Add(_fieldsGrid);
        root.Children.Add(wavelengthHeader);
        root.Children.Add(_wavelengthsGrid);
        Content = root;

        _connector.OpticLoaded += (_, _) => Refresh();
        _connector.OpticChanged += (_, _) => Refresh();
        Refresh();
    }

    private WrapPanel BuildSystemControls()
    {
        _backendPicker.ItemsSource = _connector.BackendNames;
        _apertureKindPicker.ItemsSource = _connector.ApertureKindNames;

        var applyButton = new Button { Content = "应用", MinWidth = 74 };
        applyButton.Click += (_, _) => ApplySystemControls();

        return new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Avalonia.Thickness(0, 0, 0, 10),
            Children =
            {
                new TextBlock
                {
                    Text = "计算后端",
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Avalonia.Thickness(0, 0, 8, 0)
                },
                _backendPicker,
                new TextBlock
                {
                    Text = "系统孔径",
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Avalonia.Thickness(16, 0, 8, 0)
                },
                _apertureKindPicker,
                _apertureValue,
                applyButton
            }
        };
    }

    private static StackPanel BuildHeader(string title, Button button)
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 0, 0, 6),
            Children =
            {
                new TextBlock
                {
                    Text = title,
                    FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center
                },
                button
            }
        };
    }

    private DataGrid CreateFieldsGrid()
    {
        var grid = BaseGrid();
        grid.Columns.Add(new DataGridTextColumn { Header = "标签", Binding = new Binding(nameof(FieldPoint.Label)), Width = new DataGridLength(110) });
        grid.Columns.Add(new DataGridTextColumn { Header = "X 角度", Binding = new Binding(nameof(FieldPoint.XAngleDegrees)), Width = new DataGridLength(76) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Y 角度", Binding = new Binding(nameof(FieldPoint.YAngleDegrees)), Width = new DataGridLength(76) });
        grid.Columns.Add(new DataGridTextColumn { Header = "权重", Binding = new Binding(nameof(FieldPoint.Weight)), Width = new DataGridLength(76) });
        return grid;
    }

    private DataGrid CreateWavelengthsGrid()
    {
        var grid = BaseGrid();
        grid.Columns.Add(new DataGridTextColumn { Header = "标签", Binding = new Binding(nameof(Wavelength.Label)), Width = new DataGridLength(84) });
        grid.Columns.Add(new DataGridTextColumn { Header = "nm", Binding = new Binding(nameof(Wavelength.Nanometers)), Width = new DataGridLength(88) });
        grid.Columns.Add(new DataGridTextColumn { Header = "权重", Binding = new Binding(nameof(Wavelength.Weight)), Width = new DataGridLength(76) });
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "主波长", Binding = new Binding(nameof(Wavelength.IsPrimary)), Width = new DataGridLength(84) });
        return grid;
    }

    private DataGrid BaseGrid()
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserReorderColumns = true,
            CanUserResizeColumns = true,
            GridLinesVisibility = DataGridGridLinesVisibility.All,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowBackground = Brushes.White
        };

        grid.BeginningEdit += (_, _) => _connector.CaptureCurrentState();
        grid.CellEditEnded += (_, _) => _connector.CommitSystemEdit();
        return grid;
    }

    private void Refresh()
    {
        _backendPicker.ItemsSource = _connector.BackendNames;
        _backendPicker.SelectedItem = _connector.CurrentOptic.Backend.Current.Name;
        _apertureKindPicker.SelectedIndex = _connector.CurrentOptic.Aperture.Kind switch
        {
            ApertureKind.FNumber => 1,
            ApertureKind.NumericalAperture => 2,
            _ => 0
        };
        _apertureValue.Value = (decimal)_connector.CurrentOptic.Aperture.Value;
        _fieldsGrid.ItemsSource = _connector.Fields;
        _wavelengthsGrid.ItemsSource = _connector.Wavelengths;
    }

    private void ApplySystemControls()
    {
        var backendName = _backendPicker.SelectedItem as string;
        var apertureKind = _apertureKindPicker.SelectedItem as string
            ?? _connector.CurrentOptic.Aperture.Kind.ToString();
        var value = _apertureValue.Value.HasValue
            ? decimal.ToDouble(_apertureValue.Value.Value)
            : _connector.CurrentOptic.Aperture.Value;

        if (backendName is not null)
        {
            _connector.SetBackend(backendName);
        }

        _connector.SetSystemAperture(apertureKind, value);
    }
}
