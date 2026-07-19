using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.App.Controls;

namespace OptilandWorkbench.App.Panels;

public sealed class SystemPropertiesPanel : UserControl, IDisposable
{
    private readonly IPrescriptionService _prescription;
    private readonly IWorkspaceEventStream _events;
    private readonly StackPanel _fieldsHost = new() { Spacing = 5 };
    private readonly StackPanel _wavelengthsHost = new() { Spacing = 5 };
    private readonly HashSet<int> _expandedFields = new();
    private readonly HashSet<int> _expandedWavelengths = new();
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
    private readonly DispatcherTimer _systemUpdateTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _environmentUpdateTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly CheckBox _matchRefractiveIndexData = new()
    {
        Content = "折射率数据与环境匹配",
        IsChecked = true
    };
    private readonly NumericUpDown _temperatureCelsius = NumberInput(20.0, -273.15, 10_000, 0.1);
    private readonly NumericUpDown _pressureAtmospheres = NumberInput(1.0, 0.0001, 10_000, 0.1);
    private StackPanel? _apodizationParameterRow;
    private bool _refreshing;
    private bool _applyingLocalChange;
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
        _systemUpdateTimer.Tick += OnSystemUpdateTimerTick;
        _environmentUpdateTimer.Tick += OnEnvironmentUpdateTimerTick;
        ConfigurePickers();
        ConfigureEnvironmentControls();

        var addField = CommandButton("plus", "添加视场", 96);
        addField.Click += (_, _) => _prescription.AddField();
        var addWavelength = CommandButton("plus", "添加波长", 112);
        addWavelength.Click += (_, _) => _prescription.AddWavelength();

        var sections = new StackPanel
        {
            Spacing = 1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                Section("系统孔径", BuildApertureSection(), expanded: true),
                Section("视场", BuildFieldSection(addField), expanded: true),
                Section("波长", BuildWavelengthSection(addWavelength)),
                Section("环境", BuildEnvironmentSection()),
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
        _systemUpdateTimer.Stop();
        _systemUpdateTimer.Tick -= OnSystemUpdateTimerTick;
        _environmentUpdateTimer.Stop();
        _environmentUpdateTimer.Tick -= OnEnvironmentUpdateTimerTick;
        _events.Changed -= OnWorkspaceChanged;
    }

    private void ConfigureEnvironmentControls()
    {
        _matchRefractiveIndexData.IsCheckedChanged += (_, _) => ScheduleEnvironmentUpdate();
        _temperatureCelsius.ValueChanged += (_, _) => ScheduleEnvironmentUpdate();
        _pressureAtmospheres.ValueChanged += (_, _) => ScheduleEnvironmentUpdate();
    }

    private void ConfigurePickers()
    {
        var options = _prescription.GetOptions();
        _backendPicker.ItemsSource = options.Backends;
        _backendPicker.SelectionChanged += (_, _) => ScheduleSystemUpdate();
        _apertureKindPicker.ItemsSource = options.ApertureKinds;
        _apertureKindPicker.SelectionChanged += (_, _) =>
        {
            UpdateApertureValueState();
            ScheduleSystemUpdate();
        };
        _apertureValue.ValueChanged += (_, _) => ScheduleSystemUpdate();
        _fieldDefinitionPicker.ItemsSource = options.FieldDefinitions;
        _fieldDefinitionPicker.SelectionChanged += (_, _) =>
        {
            _objectSpaceTelecentric.IsEnabled = _fieldDefinitionPicker.SelectedIndex != 0;
            RebuildFieldCards();
            if (!_objectSpaceTelecentric.IsEnabled)
            {
                _objectSpaceTelecentric.IsChecked = false;
            }

            ScheduleSystemUpdate();
        };
        _objectSpaceTelecentric.IsCheckedChanged += (_, _) => ScheduleSystemUpdate();
        _apodizationPicker.ItemsSource = options.ApodizationKinds;
        _apodizationPicker.SelectionChanged += (_, _) =>
        {
            if (!_refreshing)
            {
                ConfigureApodizationParameters(_apodizationPicker.SelectedItem as string, useDefaults: true);
            }

            ScheduleSystemUpdate();
        };
        _firstApodizationParameter.ValueChanged += (_, _) => ScheduleSystemUpdate();
        _secondApodizationParameter.ValueChanged += (_, _) => ScheduleSystemUpdate();
    }

    private Control BuildApertureSection()
    {
        _apertureValue.Width = double.NaN;
        _apertureValue.HorizontalAlignment = HorizontalAlignment.Stretch;
        _apertureKindPicker.HorizontalAlignment = HorizontalAlignment.Stretch;
        _apodizationPicker.HorizontalAlignment = HorizontalAlignment.Stretch;

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
        return form;
    }

    private Control BuildFieldSection(Button addField)
    {
        _fieldDefinitionPicker.HorizontalAlignment = HorizontalAlignment.Stretch;
        return new StackPanel
        {
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                Form(
                    ("视场类型", _fieldDefinitionPicker),
                    (string.Empty, _objectSpaceTelecentric)),
                BuildHeader("视场数据", addField),
                _fieldsHost
            }
        };
    }

    private Control BuildWavelengthSection(Button addWavelength)
    {
        return new StackPanel
        {
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                BuildHeader("波长数据", addWavelength),
                _wavelengthsHost
            }
        };
    }

    private Control BuildAdvancedSection()
    {
        _backendPicker.HorizontalAlignment = HorizontalAlignment.Stretch;
        return Form(("计算后端", _backendPicker));
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
            Height = 35,
            Margin = new Avalonia.Thickness(6, 3),
            Padding = new Avalonia.Thickness(10, 0),
            Background = new SolidColorBrush(Color.FromRgb(250, 250, 252)),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(7),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = headerContent
        };

        var isExpanded = expanded;
        var isHovered = false;

        void UpdateVisuals()
        {
            var emphasized = isExpanded || isHovered;
            arrow.Stroke = new SolidColorBrush(emphasized
                ? Color.FromRgb(0, 102, 204)
                : Color.FromRgb(110, 110, 115));
            titleText.Foreground = new SolidColorBrush(emphasized
                ? Color.FromRgb(0, 82, 164)
                : Color.FromRgb(29, 29, 31));
            header.Background = new SolidColorBrush(isExpanded
                ? Color.FromRgb(228, 239, 253)
                : isHovered
                    ? Color.FromRgb(239, 246, 255)
                    : Color.FromRgb(250, 250, 252));
            header.BorderBrush = new SolidColorBrush(emphasized
                ? Color.FromRgb(174, 204, 239)
                : Color.FromArgb(0, 0, 0, 0));
        }

        void SetExpanded(bool value)
        {
            isExpanded = value;
            contentHost.IsVisible = value;
            arrow.IconName = value ? "chevron-down" : "chevron-right";
            UpdateVisuals();
        }

        SetExpanded(isExpanded);
        header.PointerEntered += (_, _) =>
        {
            isHovered = true;
            UpdateVisuals();
        };
        header.PointerExited += (_, _) =>
        {
            isHovered = false;
            UpdateVisuals();
        };
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

    private void RebuildFieldCards()
    {
        _fieldsHost.Children.Clear();
        var unit = _fieldDefinitionPicker.SelectedIndex == 0 ? "deg" : "mm";
        foreach (var field in _prescription.GetFields())
        {
            _fieldsHost.Children.Add(CreateFieldCard(field, unit));
        }
    }

    private Control CreateFieldCard(FieldRowDto field, string unit)
    {
        TextBlock? summaryDisplay = null;
        var label = new TextBox { Text = field.Label };
        var x = NumberInput(field.X, -1_000_000, 1_000_000, 0.1);
        var y = NumberInput(field.Y, -1_000_000, 1_000_000, 0.1);
        var vignetteX = NumberInput(field.VignetteFactorX, -1, 1, 0.05);
        var vignetteY = NumberInput(field.VignetteFactorY, -1, 1, 0.05);
        var weight = NumberInput(field.Weight, 0, 1_000_000, 0.1);

        void Commit()
        {
            if (_refreshing || _disposed)
            {
                return;
            }

            var nextX = DecimalValue(x, field.X);
            var nextY = DecimalValue(y, field.Y);
            var nextWeight = DecimalValue(weight, field.Weight);
            if (summaryDisplay is not null)
            {
                summaryDisplay.Text = $"X {nextX:0.###} · Y {nextY:0.###} · 权重 {nextWeight:0.###}";
            }

            ApplyLocalChange(() => _prescription.UpdateField(new FieldRowDto(
                field.Index,
                label.Text ?? string.Empty,
                nextX,
                nextY,
                DecimalValue(vignetteX, field.VignetteFactorX),
                DecimalValue(vignetteY, field.VignetteFactorY),
                nextWeight)));
        }

        label.LostFocus += (_, _) => Commit();
        x.ValueChanged += (_, _) => Commit();
        y.ValueChanged += (_, _) => Commit();
        vignetteX.ValueChanged += (_, _) => Commit();
        vignetteY.ValueChanged += (_, _) => Commit();
        weight.ValueChanged += (_, _) => Commit();
        var delete = CommandButton("trash-2", "删除此视场", 96);
        delete.Click += (_, _) => _prescription.RemoveField(field.Index);
        var content = Form(
            ("标签", label),
            ($"X ({unit})", x),
            ($"Y ({unit})", y),
            ("X 渐晕", vignetteX),
            ("Y 渐晕", vignetteY),
            ("权重", weight),
            (string.Empty, delete));
        var card = EditorCard(
            field.Index,
            $"视场 {field.Index + 1}",
            $"X {field.X:0.###} · Y {field.Y:0.###} · 权重 {field.Weight:0.###}",
            content,
            _expandedFields);
        summaryDisplay = card.Summary;
        return card.Card;
    }

    private void RebuildWavelengthCards()
    {
        _wavelengthsHost.Children.Clear();
        foreach (var wavelength in _prescription.GetWavelengths())
        {
            _wavelengthsHost.Children.Add(CreateWavelengthCard(wavelength));
        }
    }

    private Control CreateWavelengthCard(WavelengthRowDto wavelength)
    {
        TextBlock? summaryDisplay = null;
        var label = new TextBox { Text = wavelength.Label };
        var nanometers = NumberInput(wavelength.Nanometers, 0.001, 1_000_000, 1);
        var weight = NumberInput(wavelength.Weight, 0, 1_000_000, 0.1);
        var primary = new CheckBox { Content = "主波长", IsChecked = wavelength.IsPrimary };

        void Commit()
        {
            if (_refreshing || _disposed)
            {
                return;
            }

            var nextNanometers = DecimalValue(nanometers, wavelength.Nanometers);
            var nextWeight = DecimalValue(weight, wavelength.Weight);
            if (summaryDisplay is not null)
            {
                summaryDisplay.Text = $"{nextNanometers / 1000:0.0000} μm · 权重 {nextWeight:0.###}"
                    + (primary.IsChecked == true ? " · 主波长" : string.Empty);
            }

            ApplyLocalChange(() => _prescription.UpdateWavelength(new WavelengthRowDto(
                wavelength.Index,
                label.Text ?? string.Empty,
                nextNanometers,
                nextWeight,
                primary.IsChecked == true)));
        }

        label.LostFocus += (_, _) => Commit();
        nanometers.ValueChanged += (_, _) => Commit();
        weight.ValueChanged += (_, _) => Commit();
        primary.IsCheckedChanged += (_, _) => Commit();
        var delete = CommandButton("trash-2", "删除此波长", 104);
        delete.Click += (_, _) => _prescription.RemoveWavelength(wavelength.Index);
        var content = Form(
            ("标签", label),
            ("波长 (nm)", nanometers),
            ("权重", weight),
            (string.Empty, primary),
            (string.Empty, delete));
        var card = EditorCard(
            wavelength.Index,
            $"波长 {wavelength.Index + 1}",
            $"{wavelength.Nanometers / 1000:0.0000} μm · 权重 {wavelength.Weight:0.###}"
                + (wavelength.IsPrimary ? " · 主波长" : string.Empty),
            content,
            _expandedWavelengths);
        summaryDisplay = card.Summary;
        return card.Card;
    }

    private static NumericUpDown NumberInput(double value, double minimum, double maximum, double increment)
    {
        return new NumericUpDown
        {
            Minimum = (decimal)minimum,
            Maximum = (decimal)maximum,
            Increment = (decimal)increment,
            Value = (decimal)Math.Clamp(value, minimum, maximum),
            ShowButtonSpinner = false,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    private Control BuildEnvironmentSection()
    {
        _temperatureCelsius.HorizontalAlignment = HorizontalAlignment.Stretch;
        _pressureAtmospheres.HorizontalAlignment = HorizontalAlignment.Stretch;
        return new StackPanel
        {
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                _matchRefractiveIndexData,
                Form(
                    ("温度 (°C)", _temperatureCelsius),
                    ("压力 (ATM)", _pressureAtmospheres)),
                new TextBlock
                {
                    Text = "当前仅保存环境参数，暂不启用温度补偿计算。",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(110, 110, 115)),
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };
    }

    private static (Control Card, TextBlock Summary) EditorCard(
        int index,
        string title,
        string summary,
        Control content,
        HashSet<int> expandedItems)
    {
        var arrow = new LocalIcon { IconName = "chevron-right", Width = 16, Height = 16 };
        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        var summaryText = new TextBlock
        {
            Text = summary,
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(99, 99, 102)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(titleText, 1);
        Grid.SetColumn(summaryText, 2);
        var headerContent = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("18,Auto,*"),
            ColumnSpacing = 6,
            Children = { arrow, titleText, summaryText }
        };
        var contentHost = new Border
        {
            Padding = new Thickness(24, 10, 10, 12),
            Background = Brushes.White,
            Child = content
        };
        var header = new Button
        {
            Height = 42,
            Padding = new Thickness(10, 0),
            Background = new SolidColorBrush(Color.FromRgb(250, 250, 252)),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = headerContent
        };
        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(209, 209, 214)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Child = new StackPanel { Children = { header, contentHost } }
        };
        var isHovered = false;

        void UpdateVisuals(bool expanded)
        {
            var emphasized = expanded || isHovered;
            header.Background = new SolidColorBrush(expanded
                ? Color.FromRgb(228, 239, 253)
                : isHovered
                    ? Color.FromRgb(239, 246, 255)
                    : Color.FromRgb(250, 250, 252));
            header.BorderBrush = new SolidColorBrush(emphasized
                ? Color.FromRgb(174, 204, 239)
                : Color.FromArgb(0, 0, 0, 0));
            card.BorderBrush = new SolidColorBrush(emphasized
                ? Color.FromRgb(174, 204, 239)
                : Color.FromRgb(209, 209, 214));
            arrow.Stroke = new SolidColorBrush(emphasized
                ? Color.FromRgb(0, 102, 204)
                : Color.FromRgb(72, 72, 74));
            titleText.Foreground = new SolidColorBrush(emphasized
                ? Color.FromRgb(0, 82, 164)
                : Color.FromRgb(29, 29, 31));
        }

        void SetExpanded(bool expanded)
        {
            contentHost.IsVisible = expanded;
            arrow.IconName = expanded ? "chevron-down" : "chevron-right";
            if (expanded)
            {
                expandedItems.Add(index);
            }
            else
            {
                expandedItems.Remove(index);
            }

            UpdateVisuals(expanded);
        }

        SetExpanded(expandedItems.Contains(index));
        header.PointerEntered += (_, _) =>
        {
            isHovered = true;
            UpdateVisuals(contentHost.IsVisible);
        };
        header.PointerExited += (_, _) =>
        {
            isHovered = false;
            UpdateVisuals(contentHost.IsVisible);
        };
        header.Click += (_, _) => SetExpanded(!contentHost.IsVisible);
        return (card, summaryText);
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
        var environment = _prescription.GetEnvironmentSettings();
        _backendPicker.ItemsSource = options.Backends;
        _backendPicker.SelectedItem = settings.Backend;
        _apertureKindPicker.SelectedItem = settings.ApertureKind;
        _apertureValue.Value = (decimal)settings.ApertureValue;
        UpdateApertureValueState();
        _fieldDefinitionPicker.SelectedItem = settings.FieldDefinition;
        _objectSpaceTelecentric.IsEnabled = _fieldDefinitionPicker.SelectedIndex != 0;
        _objectSpaceTelecentric.IsChecked = settings.ObjectSpaceTelecentric;
        SetApodizationControls(
            settings.ApodizationKind,
            settings.FirstApodizationParameter,
            settings.SecondApodizationParameter);
        _matchRefractiveIndexData.IsChecked = environment.MatchRefractiveIndexData;
        _temperatureCelsius.Value = (decimal)environment.TemperatureCelsius;
        _pressureAtmospheres.Value = (decimal)environment.PressureAtmospheres;
        RebuildFieldCards();
        RebuildWavelengthCards();
        _refreshing = false;
    }

    private void UpdateApertureValueState()
    {
        _apertureValue.IsEnabled = !string.Equals(
            _apertureKindPicker.SelectedItem as string,
            "按光阑面尺寸浮动",
            StringComparison.Ordinal);
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

        ApplyLocalChange(() => _prescription.UpdateSystemSettings(new SystemSettingsDto(
            backendName,
            apertureKind,
            value,
            fieldDefinition,
            _objectSpaceTelecentric.IsChecked == true,
            apodizationKind,
            firstApodizationParameter,
            secondApodizationParameter)));
    }

    private void ApplyLocalChange(Action action)
    {
        _applyingLocalChange = true;
        try
        {
            action();
        }
        finally
        {
            _applyingLocalChange = false;
        }
    }

    private void ScheduleSystemUpdate()
    {
        if (_disposed || _refreshing)
        {
            return;
        }

        _systemUpdateTimer.Stop();
        _systemUpdateTimer.Start();
    }

    private void OnSystemUpdateTimerTick(object? sender, EventArgs args)
    {
        _systemUpdateTimer.Stop();
        if (!_disposed && !_refreshing)
        {
            ApplySystemControls();
        }
    }

    private void ScheduleEnvironmentUpdate()
    {
        if (_disposed || _refreshing)
        {
            return;
        }

        _environmentUpdateTimer.Stop();
        _environmentUpdateTimer.Start();
    }

    private void OnEnvironmentUpdateTimerTick(object? sender, EventArgs args)
    {
        _environmentUpdateTimer.Stop();
        if (!_disposed && !_refreshing)
        {
            ApplyEnvironmentControls();
        }
    }

    private void ApplyEnvironmentControls()
    {
        var current = _prescription.GetEnvironmentSettings();
        var temperature = _temperatureCelsius.Value.HasValue
            ? decimal.ToDouble(_temperatureCelsius.Value.Value)
            : current.TemperatureCelsius;
        var pressure = _pressureAtmospheres.Value.HasValue
            ? decimal.ToDouble(_pressureAtmospheres.Value.Value)
            : current.PressureAtmospheres;

        ApplyLocalChange(() => _prescription.UpdateEnvironmentSettings(new EnvironmentSettingsDto(
            _matchRefractiveIndexData.IsChecked == true,
            temperature,
            pressure)));
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
        if (_applyingLocalChange)
        {
            return;
        }

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
