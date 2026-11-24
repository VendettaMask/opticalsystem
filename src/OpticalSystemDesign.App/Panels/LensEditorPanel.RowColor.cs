using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.ViewModels;

namespace OpticalSystemDesign.App.Panels;

public sealed partial class LensEditorPanel
{
    private readonly ComboBox _rowColor = new() { Name = "SurfaceRowColor" };
    private readonly TextBox _rowColorHex = new()
    {
        Name = "SurfaceRowColorHex",
        PlaceholderText = "#RRGGBB",
        MaxLength = 7
    };
    private readonly Border _rowColorSwatch = new()
    {
        Name = "SurfaceRowColorSwatch",
        Width = 20,
        Height = 20,
        BorderThickness = new Thickness(1),
        VerticalAlignment = VerticalAlignment.Center
    };

    private Control BuildRowColorPicker()
    {
        // These are user-selected annotation colours, independent of optical/material identity.
        var choices = new (string Label, string Value)[]
        {
            ("默认颜色", ""), ("蓝色", "#DAE7FF"), ("红色", "#F6D6D6"),
            ("绿色", "#D8EEDB"), ("青色", "#D6EFEF"), ("紫色", "#E8DCF3"),
            ("橙色", "#F8E2CC"), ("黄色", "#F6EFCC"), ("灰色", "#E2E5EA"),
            ("自定义颜色…", "custom")
        };
        _rowColor.ItemsSource = choices.Select(choice => new ComboBoxItem
        {
            Content = choice.Label,
            Tag = choice.Value
        }).ToArray();
        _rowColorSwatch.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        var preview = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
        preview.Children.Add(_rowColorHex);
        preview.Children.Add(_rowColorSwatch);
        Grid.SetColumn(_rowColorSwatch, 1);
        void UpdatePreview()
        {
            var custom = (_rowColor.SelectedItem as ComboBoxItem)?.Tag as string == "custom";
            preview.IsVisible = custom;
            _rowColorSwatch.Background = Color.TryParse(_rowColorHex.Text, out var color)
                ? new SolidColorBrush(color) : Brushes.Transparent;
        }
        _rowColor.SelectionChanged += (_, _) => UpdatePreview();
        _rowColorHex.TextChanged += (_, _) => UpdatePreview();
        AutomationProperties.SetName(_rowColor, "行颜色");
        AutomationProperties.SetName(_rowColorHex, "自定义行颜色");
        ToolTip.SetTip(_rowColor, "设置当前表面的镜头表行颜色，点击应用后生效；默认颜色恢复材料和表面类型配色。");
        ToolTip.SetTip(_rowColorHex, "输入 #RRGGBB，例如 #E8DCF3。");
        _rowColor.SelectedIndex = 0;
        return new StackPanel { Spacing = 6, Children = { _rowColor, preview } };
    }

    private void LoadRowColor(SurfaceEditorRow row)
    {
        var value = row.Drawing.RowColor;
        _rowColor.SelectedItem = _rowColor.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => Equals(item.Tag, value ?? "")) ?? _rowColor.Items[^1];
        _rowColorHex.Text = value ?? "#E8DCF3";
    }

    private string? SelectedRowColor()
    {
        var value = (_rowColor.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        if (value != "custom") return string.IsNullOrEmpty(value) ? null : value;
        var hex = _rowColorHex.Text?.Trim() ?? "";
        if (hex.Length != 7 || hex[0] != '#' || !hex.Skip(1).All(Uri.IsHexDigit))
            throw new ArgumentException("行颜色请输入 #RRGGBB 格式的六位十六进制颜色。");
        return hex.ToUpperInvariant();
    }
}
