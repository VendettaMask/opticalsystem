using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OptilandWorkbench.Application.Contracts;

namespace OptilandWorkbench.App.Panels;

public sealed partial class MultiConfigurationPanel
{
    private readonly TextBox _pickupSourceRow = new() { Text = "1", Width = 64 };
    private readonly TextBox _pickupSourceConfiguration = new() { Text = "1", Width = 64 };
    private readonly TextBox _pickupScale = new() { Text = "1", Width = 88 };
    private readonly TextBox _pickupOffset = new() { Text = "0", Width = 88 };
    private bool _pickupDraft;

    private Control BuildPickupEditor()
    {
        var inputs = new[] { (_pickupSourceRow, "拾取源行号"), (_pickupSourceConfiguration, "拾取源配置号"), (_pickupScale, "拾取比例"), (_pickupOffset, "拾取偏移") };
        foreach (var (input, name) in inputs)
        {
            AutomationProperties.SetName(input, name);
            input.PropertyChanged += (_, args) => { if (args.Property == TextBox.TextProperty && !_loadingOperand) _pickupDraft = true; };
        }
        var fields = new WrapPanel { Orientation = Orientation.Horizontal };
        fields.Children.Add(Label("源行")); fields.Children.Add(_pickupSourceRow);
        fields.Children.Add(Label("源配置")); fields.Children.Add(_pickupSourceConfiguration);
        fields.Children.Add(Label("比例")); fields.Children.Add(_pickupScale);
        fields.Children.Add(Label("偏移")); fields.Children.Add(_pickupOffset);
        var apply = ActionButton("应用拾取", "ApplyMultiConfigurationPickup", () =>
        {
            var (row, configuration) = PickupTarget();
            _configurations.SetOperandPickup(row, configuration, new(
                PositiveInteger(_pickupSourceRow.Text, "源行号"), PositiveInteger(_pickupSourceConfiguration.Text, "源配置号") - 1,
                PickupNumber(_pickupScale.Text, "比例"), PickupNumber(_pickupOffset.Text, "偏移")), _operandRevision);
        });
        apply.Classes.Add("accent"); fields.Children.Add(apply);
        fields.Children.Add(ActionButton("移除拾取", "ClearMultiConfigurationPickup", () =>
        {
            var (row, configuration) = PickupTarget();
            _configurations.SetOperandPickup(row, configuration, null, _operandRevision);
        }));
        var hint = new TextBlock
        {
            Text = "当前单元格 = 源值 × 比例 + 偏移。源行号、源配置号均不能晚于目标；移除拾取保留当前数值。",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4)
        };
        return new Expander
        {
            Header = "拾取设置（P）",
            Name = "MultiConfigurationPickupEditor",
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel { Children = { fields, hint } }
        };
    }

    private (int Row, int Configuration) PickupTarget()
    {
        var selected = SelectedOperand(); var configuration = PositiveInteger(_operandConfiguration.Text, "配置号");
        if (_editingBinding != new MultiConfigurationOperandBindingDto(selected.Row.Kind, selected.Row.SurfaceNumber) || configuration != _editingConfiguration)
            throw new InvalidOperationException("输入属于之前的单元格，请还原输入后重新设置拾取。");
        return (selected.Number, configuration - 1);
    }

    private static double PickupNumber(string? text, string label) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? value : throw new ArgumentException($"拾取{label}必须是有限数值。");

    private void LoadPickupFields(MultiConfigurationPickupDto? pickup)
    {
        _pickupSourceRow.Text = (pickup?.SourceRow ?? 1).ToString(CultureInfo.InvariantCulture);
        _pickupSourceConfiguration.Text = ((pickup?.SourceConfigurationIndex ?? 0) + 1).ToString(CultureInfo.InvariantCulture);
        _pickupScale.Text = (pickup?.Scale ?? 1).ToString("G17", CultureInfo.InvariantCulture);
        _pickupOffset.Text = (pickup?.Offset ?? 0).ToString("G17", CultureInfo.InvariantCulture);
    }
}
