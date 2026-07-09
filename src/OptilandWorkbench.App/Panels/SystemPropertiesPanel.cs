using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using OptilandWorkbench.App.Connectors;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.App.Panels;

public sealed class SystemPropertiesPanel : UserControl
{
    private readonly OptilandConnector _connector;
    private readonly DataGrid _fieldsGrid;
    private readonly DataGrid _wavelengthsGrid;

    public SystemPropertiesPanel(OptilandConnector connector)
    {
        _connector = connector;
        _fieldsGrid = CreateFieldsGrid();
        _wavelengthsGrid = CreateWavelengthsGrid();

        var addField = new Button { Content = "Add field", MinWidth = 96 };
        addField.Click += (_, _) => _connector.AddField();

        var addWavelength = new Button { Content = "Add wavelength", MinWidth = 128 };
        addWavelength.Click += (_, _) => _connector.AddWavelength();

        var root = new Grid
        {
            Margin = new Avalonia.Thickness(12),
            RowDefinitions = new RowDefinitions("Auto,*,Auto,*")
        };

        var fieldHeader = BuildHeader("Fields", addField);
        var wavelengthHeader = BuildHeader("Wavelengths", addWavelength);

        Grid.SetRow(fieldHeader, 0);
        Grid.SetRow(_fieldsGrid, 1);
        Grid.SetRow(wavelengthHeader, 2);
        Grid.SetRow(_wavelengthsGrid, 3);

        root.Children.Add(fieldHeader);
        root.Children.Add(_fieldsGrid);
        root.Children.Add(wavelengthHeader);
        root.Children.Add(_wavelengthsGrid);
        Content = root;

        _connector.OpticLoaded += (_, _) => Refresh();
        _connector.OpticChanged += (_, _) => Refresh();
        Refresh();
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
        grid.Columns.Add(new DataGridTextColumn { Header = "Label", Binding = new Binding(nameof(FieldPoint.Label)), Width = new DataGridLength(110) });
        grid.Columns.Add(new DataGridTextColumn { Header = "X deg", Binding = new Binding(nameof(FieldPoint.XAngleDegrees)), Width = new DataGridLength(76) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Y deg", Binding = new Binding(nameof(FieldPoint.YAngleDegrees)), Width = new DataGridLength(76) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Weight", Binding = new Binding(nameof(FieldPoint.Weight)), Width = new DataGridLength(76) });
        return grid;
    }

    private DataGrid CreateWavelengthsGrid()
    {
        var grid = BaseGrid();
        grid.Columns.Add(new DataGridTextColumn { Header = "Label", Binding = new Binding(nameof(Wavelength.Label)), Width = new DataGridLength(84) });
        grid.Columns.Add(new DataGridTextColumn { Header = "nm", Binding = new Binding(nameof(Wavelength.Nanometers)), Width = new DataGridLength(88) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Weight", Binding = new Binding(nameof(Wavelength.Weight)), Width = new DataGridLength(76) });
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Primary", Binding = new Binding(nameof(Wavelength.IsPrimary)), Width = new DataGridLength(84) });
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
        _fieldsGrid.ItemsSource = _connector.Fields;
        _wavelengthsGrid.ItemsSource = _connector.Wavelengths;
    }
}
