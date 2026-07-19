using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.ViewModels;

namespace OptilandWorkbench.App.Panels;

public sealed class SystemPropertiesPanel : UserControl, IDisposable
{
    private readonly IPrescriptionService _prescription;
    private readonly IWorkspaceEventStream _events;
    private readonly DataGrid _fieldsGrid;
    private readonly DataGrid _wavelengthsGrid;
    private readonly ComboBox _backendPicker = new() { MinWidth = 150 };
    private readonly ComboBox _apertureKindPicker = new() { MinWidth = 190 };
    private readonly ComboBox _fieldDefinitionPicker = new() { MinWidth = 116 };
    private readonly CheckBox _objectSpaceTelecentric = new()
    {
        Content = "物方远心",
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly ComboBox _apodizationPicker = new() { MinWidth = 128 };
    private readonly TextBlock _firstApodizationLabel = ParameterLabel("σ");
    private readonly TextBlock _secondApodizationLabel = ParameterLabel("p");
    private readonly NumericUpDown _firstApodizationParameter = ParameterInput(1m);
    private readonly NumericUpDown _secondApodizationParameter = ParameterInput(1m);
    private StackPanel? _apodizationParameterRow;
    private bool _refreshing;
    private readonly NumericUpDown _apertureValue = new()
    {
        Minimum = 0.001m,
        Maximum = 1_000_000m,
        Increment = 1m,
        Value = 14m,
        Width = 110
    };

    private bool _disposed;

    public SystemPropertiesPanel(IPrescriptionService prescription, IWorkspaceEventStream events)
    {
        _prescription = prescription;
        _events = events;
        _fieldsGrid = CreateFieldsGrid();
        _wavelengthsGrid = CreateWavelengthsGrid();
        ConfigurePickers();

        var addField = CommandButton("plus", "添加视场", 96);
        addField.Click += (_, _) => _prescription.AddField();
        var removeField = CommandButton("trash-2", "删除", 72);
        removeField.Click += (_, _) =>
        {
            if (_fieldsGrid.SelectedItem is FieldEditorRow row)
            {
                _prescription.RemoveField(row.Index);
            }
        };

        var addWavelength = CommandButton("plus", "添加波长", 112);
        addWavelength.Click += (_, _) => _prescription.AddWavelength();
        var removeWavelength = CommandButton("trash-2", "删除", 72);
        removeWavelength.Click += (_, _) =>
        {
            if (_wavelengthsGrid.SelectedItem is WavelengthEditorRow row)
            {
                _prescription.RemoveWavelength(row.Index);
            }
        };

        var sections = new StackPanel
        {
            Spacing = 1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                Section("系统孔径", BuildApertureSection(), expanded: true),
                Section("视场", BuildFieldSection(addField, removeField), expanded: true),
                Section("波长", BuildWavelengthSection(addWavelength, removeWavelength)),
                Section("高级", BuildAdvancedSection())
            }
        };
        Content = new ScrollViewer
        {
            Background = new SolidColorBrush(Color.FromRgb(245, 245, 247)),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = sections
        };

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

    private void ConfigurePickers()
    {
        var options = _prescription.GetOptions();
        _backendPicker.ItemsSource = options.Backends;
        _apertureKindPicker.ItemsSource = options.ApertureKinds;
        _fieldDefinitionPicker.ItemsSource = options.FieldDefinitions;
        _fieldDefinitionPicker.SelectionChanged += (_, _) =>
        {
            _objectSpaceTelecentric.IsEnabled = _fieldDefinitionPicker.SelectedIndex != 0;
            UpdateFieldCoordinateHeaders();
            if (!_objectSpaceTelecentric.IsEnabled)
            {
                _objectSpaceTelecentric.IsChecked = false;
            }
        };
        _apodizationPicker.ItemsSource = options.ApodizationKinds;
        _apodizationPicker.SelectionChanged += (_, _) =>
        {
            if (!_refreshing)
            {
                ConfigureApodizationParameters(_apodizationPicker.SelectedItem as string, useDefaults: true);
            }
        };
    }

    private Control BuildApertureSection()
    {
        _apertureValue.Width = double.NaN;
        _apertureValue.HorizontalAlignment = HorizontalAlignment.Stretch;
        _apertureKindPicker.HorizontalAlignment = HorizontalAlignment.Stretch;
        _apodizationPicker.HorizontalAlignment = HorizontalAlignment.Stretch;

        var applyButton = CommandButton("check", "应用系统设置", 116);
        applyButton.Height = 32;
        applyButton.HorizontalAlignment = HorizontalAlignment.Left;
        applyButton.Click += (_, _) => ApplySystemControls();

        var apodizationParameters = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                _firstApodizationLabel,
                _firstApodizationParameter,
                _secondApodizationLabel,
                _secondApodizationParameter
            }
        };

        var form = Form(
            ("孔径类型", _apertureKindPicker),
            ("孔径值", _apertureValue),
            ("光瞳切趾", _apodizationPicker));
        _apodizationParameterRow = LabeledRow("切趾参数", apodizationParameters);
        form.Children.Add(_apodizationParameterRow);
        form.Children.Add(applyButton);
        return form;
    }

    private Control BuildFieldSection(Button addField, Button removeField)
    {
        _fieldDefinitionPicker.HorizontalAlignment = HorizontalAlignment.Stretch;
        _fieldsGrid.Height = 210;
        return new StackPanel
        {
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                Form(
                    ("视场类型", _fieldDefinitionPicker),
                    (string.Empty, _objectSpaceTelecentric)),
                BuildHeader("视场数据", addField, removeField),
                _fieldsGrid
            }
        };
    }

    private Control BuildWavelengthSection(Button addWavelength, Button removeWavelength)
    {
        _wavelengthsGrid.Height = 190;
        return new StackPanel
        {
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                BuildHeader("波长数据", addWavelength, removeWavelength),
                _wavelengthsGrid
            }
        };
    }

    private Control BuildAdvancedSection()
    {
        _backendPicker.HorizontalAlignment = HorizontalAlignment.Stretch;
        var applyButton = CommandButton("check", "应用高级设置", 116);
        applyButton.Height = 32;
        applyButton.HorizontalAlignment = HorizontalAlignment.Left;
        applyButton.Click += (_, _) => ApplySystemControls();
        return Form(
            ("计算后端", _backendPicker),
            (string.Empty, applyButton));
    }

    private static StackPanel Form(params (string Label, Control Input)[] rows)
    {
        var panel = new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Label))
            {
                panel.Children.Add(row.Input);
                continue;
            }

            panel.Children.Add(LabeledRow(row.Label, row.Input));
        }

        return panel;
    }

    private static StackPanel LabeledRow(string label, Control input)
    {
        input.HorizontalAlignment = HorizontalAlignment.Stretch;
        return new StackPanel
        {
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    FontSize = 12,
                    FontWeight = FontWeight.SemiBold
                },
                input
            }
        };
    }

    private static Control Section(string title, Control content, bool expanded = false)
    {
        var arrow = new LocalIcon
        {
            IconName = "chevron-right",
            Width = 18,
            Height = 18,
            VerticalAlignment = VerticalAlignment.Center
        };
        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(titleText, 1);
        var headerContent = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("18,*"),
            Children = { arrow, titleText }
        };
        var contentHost = new Border
        {
            Background = Brushes.White,
            BorderThickness = new Avalonia.Thickness(0),
            Padding = new Avalonia.Thickness(28, 11, 12, 14),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = content
        };
        var header = new Button
        {
            Height = 31,
            Padding = new Avalonia.Thickness(10, 0),
            Background = new SolidColorBrush(Color.FromRgb(250, 250, 252)),
            BorderThickness = new Avalonia.Thickness(0),
            CornerRadius = new Avalonia.CornerRadius(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = headerContent
        };

        void SetExpanded(bool value)
        {
            contentHost.IsVisible = value;
            arrow.IconName = value ? "chevron-down" : "chevron-right";
            arrow.Stroke = new SolidColorBrush(value
                ? Color.FromRgb(0, 122, 255)
                : Color.FromRgb(110, 110, 115));
            titleText.Foreground = new SolidColorBrush(value
                ? Color.FromRgb(0, 102, 204)
                : Color.FromRgb(29, 29, 31));
            header.Background = new SolidColorBrush(value
                ? Color.FromRgb(242, 247, 253)
                : Color.FromRgb(250, 250, 252));
        }

        var isExpanded = expanded;
        SetExpanded(isExpanded);
        header.Click += (_, _) =>
        {
            isExpanded = !isExpanded;
            SetExpanded(isExpanded);
        };

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(209, 209, 214)),
            BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Children = { header, contentHost }
            }
        };
    }

    private static StackPanel BuildHeader(string title, params Button[] buttons)
    {
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 0, 0, 6)
        };
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        foreach (var button in buttons)
        {
            header.Children.Add(button);
        }

        return header;
    }

    private DataGrid CreateFieldsGrid()
    {
        var grid = BaseGrid();
        grid.MaxWidth = 460;
        grid.HorizontalAlignment = HorizontalAlignment.Left;
        grid.Columns.Add(new DataGridTextColumn { Header = "标签", Binding = new Binding(nameof(FieldEditorRow.Label)), Width = new DataGridLength(110) });
        grid.Columns.Add(new DataGridTextColumn { Header = "X", Binding = new Binding(nameof(FieldEditorRow.X)), Width = new DataGridLength(72) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Y", Binding = new Binding(nameof(FieldEditorRow.Y)), Width = new DataGridLength(72) });
        grid.Columns.Add(new DataGridTextColumn { Header = "vx", Binding = new Binding(nameof(FieldEditorRow.VignetteFactorX)), Width = new DataGridLength(64) });
        grid.Columns.Add(new DataGridTextColumn { Header = "vy", Binding = new Binding(nameof(FieldEditorRow.VignetteFactorY)), Width = new DataGridLength(64) });
        grid.Columns.Add(new DataGridTextColumn { Header = "权重", Binding = new Binding(nameof(FieldEditorRow.Weight)), Width = new DataGridLength(76) });
        return grid;
    }

    private DataGrid CreateWavelengthsGrid()
    {
        var grid = BaseGrid();
        grid.MaxWidth = 334;
        grid.HorizontalAlignment = HorizontalAlignment.Left;
        grid.Columns.Add(new DataGridTextColumn { Header = "标签", Binding = new Binding(nameof(WavelengthEditorRow.Label)), Width = new DataGridLength(84) });
        grid.Columns.Add(new DataGridTextColumn { Header = "nm", Binding = new Binding(nameof(WavelengthEditorRow.Nanometers)), Width = new DataGridLength(88) });
        grid.Columns.Add(new DataGridTextColumn { Header = "权重", Binding = new Binding(nameof(WavelengthEditorRow.Weight)), Width = new DataGridLength(76) });
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "主波长", Binding = new Binding(nameof(WavelengthEditorRow.IsPrimary)), Width = new DataGridLength(84) });
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
            RowBackground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(209, 209, 214)),
            BorderThickness = new Avalonia.Thickness(1),
            HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(229, 229, 234)),
            VerticalGridLinesBrush = new SolidColorBrush(Color.FromRgb(218, 218, 223)),
            RowHeight = 28,
            ColumnHeaderHeight = 30
        };

        grid.CellEditEnded += (_, e) =>
        {
            if (e.EditAction == DataGridEditAction.Commit)
            {
                switch (e.Row.DataContext)
                {
                    case FieldEditorRow field:
                        _prescription.UpdateField(field.ToDto());
                        break;
                    case WavelengthEditorRow wavelength:
                        _prescription.UpdateWavelength(wavelength.ToDto());
                        break;
                }
            }
        };
        return grid;
    }

    private void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        _refreshing = true;
        var options = _prescription.GetOptions();
        var settings = _prescription.GetSystemSettings();
        _backendPicker.ItemsSource = options.Backends;
        _backendPicker.SelectedItem = settings.Backend;
        _apertureKindPicker.SelectedItem = settings.ApertureKind;
        _apertureValue.Value = (decimal)settings.ApertureValue;
        _fieldDefinitionPicker.SelectedItem = settings.FieldDefinition;
        UpdateFieldCoordinateHeaders();
        _objectSpaceTelecentric.IsEnabled = _fieldDefinitionPicker.SelectedIndex != 0;
        _objectSpaceTelecentric.IsChecked = settings.ObjectSpaceTelecentric;
        SetApodizationControls(
            settings.ApodizationKind,
            settings.FirstApodizationParameter,
            settings.SecondApodizationParameter);
        _fieldsGrid.ItemsSource = _prescription.GetFields().Select(field => new FieldEditorRow(field)).ToArray();
        _wavelengthsGrid.ItemsSource = _prescription.GetWavelengths().Select(wavelength => new WavelengthEditorRow(wavelength)).ToArray();
        _refreshing = false;
    }

    private void UpdateFieldCoordinateHeaders()
    {
        if (_fieldsGrid.Columns.Count < 3)
        {
            return;
        }

        var unit = _fieldDefinitionPicker.SelectedIndex == 0 ? "deg" : "mm";
        _fieldsGrid.Columns[1].Header = $"X ({unit})";
        _fieldsGrid.Columns[2].Header = $"Y ({unit})";
    }

    private void ApplySystemControls()
    {
        var current = _prescription.GetSystemSettings();
        var backendName = _backendPicker.SelectedItem as string ?? current.Backend;
        var apertureKind = _apertureKindPicker.SelectedItem as string
            ?? current.ApertureKind;
        var value = _apertureValue.Value.HasValue
            ? decimal.ToDouble(_apertureValue.Value.Value)
            : current.ApertureValue;
        var apodizationKind = _apodizationPicker.SelectedItem as string ?? "无";
        var fieldDefinition = _fieldDefinitionPicker.SelectedItem as string ?? "角度";
        var firstApodizationParameter = DecimalValue(_firstApodizationParameter, 1);
        var secondApodizationParameter = DecimalValue(_secondApodizationParameter, 1);

        _prescription.UpdateSystemSettings(new SystemSettingsDto(
            backendName,
            apertureKind,
            value,
            fieldDefinition,
            _objectSpaceTelecentric.IsChecked == true,
            apodizationKind,
            firstApodizationParameter,
            secondApodizationParameter));
    }

    private void SetApodizationControls(string kind, double first, double second)
    {
        _apodizationPicker.SelectedItem = kind;
        ConfigureApodizationParameters(kind, useDefaults: false);
        _firstApodizationParameter.Value = (decimal)first;
        _secondApodizationParameter.Value = (decimal)second;
    }

    private void ConfigureApodizationParameters(string? kind, bool useDefaults)
    {
        var configuration = kind switch
        {
            "高斯" => ("σ", string.Empty, 1.0, 1.0),
            "余弦平方" => ("R", string.Empty, 1.0, 1.0),
            "Hann" => ("D", string.Empty, 2.0, 1.0),
            "多项式" => ("R", "p", 1.0, 1.0),
            "超高斯" => ("w", "n", 1.0, 2.0),
            "Tukey" => ("R", "α", 1.0, 0.5),
            _ => (string.Empty, string.Empty, 1.0, 1.0)
        };
        var firstVisible = configuration.Item1.Length > 0;
        var secondVisible = configuration.Item2.Length > 0;
        _firstApodizationLabel.Text = configuration.Item1;
        _secondApodizationLabel.Text = configuration.Item2;
        _firstApodizationLabel.IsVisible = firstVisible;
        _firstApodizationParameter.IsVisible = firstVisible;
        _secondApodizationLabel.IsVisible = secondVisible;
        _secondApodizationParameter.IsVisible = secondVisible;
        if (_apodizationParameterRow is not null)
        {
            _apodizationParameterRow.IsVisible = firstVisible;
        }
        if (useDefaults)
        {
            _firstApodizationParameter.Value = (decimal)configuration.Item3;
            _secondApodizationParameter.Value = (decimal)configuration.Item4;
        }
    }

    private static TextBlock ParameterLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(6, 0, 2, 0)
        };
    }

    private static NumericUpDown ParameterInput(decimal value)
    {
        return new NumericUpDown
        {
            Minimum = 0m,
            Maximum = 1_000_000m,
            Increment = 0.1m,
            Value = value,
            Width = 78,
            ShowButtonSpinner = false
        };
    }

    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs args)
    {
        Dispatcher.UIThread.Post(Refresh);
    }

    private static double DecimalValue(NumericUpDown input, double fallback)
    {
        return input.Value.HasValue ? decimal.ToDouble(input.Value.Value) : fallback;
    }

    private static Button CommandButton(string iconName, string text, double minWidth) => new()
    {
        Content = new LocalIconLabel(iconName, text),
        MinWidth = minWidth
    };
}
