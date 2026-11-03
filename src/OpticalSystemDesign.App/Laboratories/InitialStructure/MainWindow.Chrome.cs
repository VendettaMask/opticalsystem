using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.App.Laboratories.InitialStructure;

public sealed partial class MainWindow
{
    private readonly List<Control> _commandPopupContents = [];
    internal IEnumerable<Control> CommandPopupContents => _commandPopupContents;

    private Control BuildCommandBar()
    {
        var commands = Buttons(_runButton, _validateButton, _cancelButton);
        commands.Name = "InitialStructureCommands";
        commands.Children.Add(CommandMenu("RunRecords", "实验记录", _resumeButton, _openButton));
        commands.Children.Add(CommandMenu("DesignTools", "设计工具", _autonomousButton));
        var bar = new Grid { ColumnDefinitions = new("*"), Margin = new(10, 6), Children = { commands } };
        return ChromeBand(bar);
    }

    private DropDownButton CommandMenu(string id, string title, params Button[] actions)
    {
        var panel = new StackPanel { Spacing = 4, MinWidth = 220, MaxWidth = 460 };
        foreach (var action in actions)
        {
            action.Margin = new(0); action.HorizontalAlignment = HorizontalAlignment.Stretch;
            action.HorizontalContentAlignment = HorizontalAlignment.Left;
            panel.Children.Add(action);
        }
        if (id == "DesignTools")
            panel.Children.Add(Hint("定义系统与目标 → 正负形式搜索 → 比较候选 → 继续优化"));
        _commandPopupContents.Add(panel);
        var popup = new Flyout { Content = panel };
        var previousFont = DisplayTypography.Body;
        popup.Opening += (_, _) =>
        {
            DisplayTypography.ApplyRecursively(panel, previousFont); previousFont = FontSize;
            TextElement.SetFontSize(panel, FontSize); TextElement.SetFontFamily(panel, FontFamily);
        };
        foreach (var action in actions) action.Click += (_, _) => popup.Hide();
        return Named(new DropDownButton
        {
            Content = title, Flyout = popup, FontSize = DisplayTypography.BodySmall,
            MinHeight = 34, Padding = new(10, 5), Margin = new(0, 0, 6, 4)
        }, id, title);
    }

    private static Border ChromeBand(Control content)
    {
        var border = new Border { Child = content, BorderThickness = new(0, 0, 0, 1) };
        border.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.SubtleSurface);
        border.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        return border;
    }
}