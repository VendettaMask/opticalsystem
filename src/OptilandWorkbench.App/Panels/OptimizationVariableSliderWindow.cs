using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Formatting;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App.Panels;

public sealed class OptimizationVariableSliderWindow : Window
{
    private readonly IPrescriptionService _prescription;
    private readonly IReadOnlyList<VariableChoice> _choices;
    private readonly ComboBox _variablePicker = new()
    {
        MinWidth = 310,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly Slider _slider = new()
    {
        Minimum = -1,
        Maximum = 1,
        TickFrequency = 0.1,
        IsSnapToTickEnabled = false,
        HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private readonly TextBlock _valueText = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        FontSize = DisplayTypography.WindowTitle
    };
    private readonly TextBlock _statusText = new()
    {
        TextWrapping = Avalonia.Media.TextWrapping.Wrap
    };
    private bool _loading;

    public OptimizationVariableSliderWindow(IPrescriptionService prescription)
    {
        _prescription = prescription;
        _choices = BuildChoices(prescription.GetSurfaces());
        Title = "优化变量滑块";
        Width = 560;
        Height = 300;
        MinWidth = 460;
        MinHeight = 270;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _variablePicker.ItemsSource = _choices;
        _variablePicker.SelectionChanged += (_, _) => LoadSelectedVariable();
        _slider.PropertyChanged += (_, args) =>
        {
            if (!_loading && args.Property == Slider.ValueProperty)
            {
                UpdateValueText();
            }
        };

        var applyButton = new Button
        {
            Content = "应用",
            MinWidth = 88
        };
        applyButton.Click += (_, _) => ApplyValue(_slider.Value);
        var resetButton = new Button
        {
            Content = "恢复初值",
            MinWidth = 88
        };
        resetButton.Click += (_, _) =>
        {
            if (_variablePicker.SelectedItem is VariableChoice choice)
            {
                _slider.Value = choice.InitialValue;
                ApplyValue(choice.InitialValue);
            }
        };
        var closeButton = new Button
        {
            Content = "关闭",
            MinWidth = 88
        };
        closeButton.Click += (_, _) => Close();

        Content = new Grid
        {
            Margin = new Thickness(20),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"),
            Children =
            {
                Row(0, "变量", _variablePicker),
                At(1, _valueText),
                At(2, _slider),
                At(3, _statusText),
                At(4, new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { resetButton, applyButton, closeButton }
                })
            }
        };

        if (_choices.Count == 0)
        {
            _variablePicker.IsEnabled = false;
            _slider.IsEnabled = false;
            applyButton.IsEnabled = false;
            resetButton.IsEnabled = false;
            _statusText.Text = "当前系统没有可手动调整的内部表面。";
        }
        else
        {
            _variablePicker.SelectedIndex = 0;
        }
    }

    private void LoadSelectedVariable()
    {
        if (_variablePicker.SelectedItem is not VariableChoice choice)
        {
            return;
        }

        _loading = true;
        try
        {
            _slider.Minimum = choice.Minimum;
            _slider.Maximum = choice.Maximum;
            _slider.TickFrequency = Math.Max(1e-6, (choice.Maximum - choice.Minimum) / 200);
            _slider.Value = CurrentValue(choice);
            _statusText.Text =
                $"范围：{NumericDisplayFormatter.Format(choice.Minimum)} ～ " +
                $"{NumericDisplayFormatter.Format(choice.Maximum)}";
            UpdateValueText();
        }
        finally
        {
            _loading = false;
        }
    }

    private void ApplyValue(double value)
    {
        if (_variablePicker.SelectedItem is not VariableChoice choice)
        {
            return;
        }

        var surface = _prescription.GetSurfaces()
            .First(item => item.Number == choice.SurfaceNumber);
        _prescription.UpdateSurface(choice.Kind == OptimizationVariableKind.Radius
            ? surface with { Radius = value }
            : surface with { Thickness = value });
        _statusText.Text =
            $"{choice.DisplayName} 已更新为 {NumericDisplayFormatter.Format(value)} mm。";
        UpdateValueText();
    }

    private double CurrentValue(VariableChoice choice)
    {
        var surface = _prescription.GetSurfaces()
            .First(item => item.Number == choice.SurfaceNumber);
        return choice.Kind == OptimizationVariableKind.Radius
            ? surface.Radius
            : surface.Thickness;
    }

    private void UpdateValueText()
    {
        _valueText.Text = $"{NumericDisplayFormatter.Format(_slider.Value)} mm";
    }

    private static IReadOnlyList<VariableChoice> BuildChoices(
        IReadOnlyList<SurfaceRowDto> surfaces)
    {
        if (surfaces.Count < 3)
        {
            return Array.Empty<VariableChoice>();
        }

        var lastSurfaceNumber = surfaces.Max(surface => surface.Number);
        var choices = new List<VariableChoice>();
        foreach (var surface in surfaces.Where(surface =>
                     surface.Number > 0 && surface.Number < lastSurfaceNumber))
        {
            if (double.IsFinite(surface.Radius))
            {
                var span = Math.Max(5, Math.Abs(surface.Radius) * 0.5);
                choices.Add(new VariableChoice(
                    surface.Number,
                    OptimizationVariableKind.Radius,
                    $"面 {surface.Number} 半径",
                    surface.Radius,
                    Math.Max(-1_000_000, surface.Radius - span),
                    Math.Min(1_000_000, surface.Radius + span)));
            }

            if (double.IsFinite(surface.Thickness))
            {
                var span = Math.Max(2, Math.Abs(surface.Thickness));
                choices.Add(new VariableChoice(
                    surface.Number,
                    OptimizationVariableKind.Thickness,
                    $"面 {surface.Number} 厚度",
                    surface.Thickness,
                    Math.Max(0.001, surface.Thickness - span),
                    Math.Min(1_000_000, surface.Thickness + span)));
            }
        }

        return choices;
    }

    private static Control Row(int row, string label, Control content)
    {
        var panel = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("80,*"),
            Margin = new Thickness(0, 0, 0, 12),
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    VerticalAlignment = VerticalAlignment.Center
                },
                content
            }
        };
        Grid.SetColumn(content, 1);
        return At(row, panel);
    }

    private static T At<T>(int row, T control)
        where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }

    private sealed record VariableChoice(
        int SurfaceNumber,
        OptimizationVariableKind Kind,
        string DisplayName,
        double InitialValue,
        double Minimum,
        double Maximum)
    {
        public override string ToString() => DisplayName;
    }
}
