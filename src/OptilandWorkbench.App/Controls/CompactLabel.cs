using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace OptilandWorkbench.App.Controls;

internal static class CompactLabel
{
    internal static TextBlock Parameter(string text, string fullText, string? unit = null)
    {
        var label = new TextBlock
        {
            Text = unit is null ? text : $"{text}\n({unit})",
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 8, 4)
        };
        ToolTip.SetTip(label, fullText);
        AutomationProperties.SetName(label, fullText);
        return label;
    }

    internal static void SetColumnLabel(DataGridColumn column, string text, string fullText, string? unit = null)
    {
        // Keep the column's Header and binding identity intact; only adapt its presentation.
        column.HeaderTemplate = new FuncDataTemplate<object>((_, _) =>
        {
            var label = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var caption = new TextBlock
            {
                Text = text,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(caption, fullText);
            label.Children.Add(caption);
            if (unit is not null)
            {
                var suffix = new TextBlock { Text = $" ({unit})", VerticalAlignment = VerticalAlignment.Center };
                ToolTip.SetTip(suffix, fullText);
                Grid.SetColumn(suffix, 1);
                label.Children.Add(suffix);
            }
            ToolTip.SetTip(label, fullText);
            AutomationProperties.SetName(label, fullText);
            return label;
        });
    }
}
