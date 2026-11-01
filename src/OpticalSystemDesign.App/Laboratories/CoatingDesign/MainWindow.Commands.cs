using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.CoatingDesign.Engine;

namespace OpticalSystemDesign.App.Laboratories.CoatingDesign;

public sealed partial class MainWindow
{
    private Border BuildCommandBar()
    {
        var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        commands.Children.Add(ToolCommand("打开", "打开实验", "folder-open", OpenAsync, "O"));
        commands.Children.Add(ToolCommand("保存", "保存实验", "save", SaveAsync, "S"));
        commands.Children.Add(ToolCommand("导出", "导出结果", "download", ExportAsync));
        commands.Children.Add(CommandDivider());

        var examples = new MenuFlyout();
        foreach (var (name, automation, kind) in new[]
                 {
                     ("减反膜", "示例：减反", DesignKind.Antireflection),
                     ("高反膜", "示例：高反", DesignKind.HighReflector),
                     ("窄带滤光片", "示例：窄带", DesignKind.NarrowBand)
                 })
        {
            var item = new MenuItem { Header = UiLocalization.Text(name), Tag = name };
            AutomationProperties.SetName(item, automation);
            item.Click += (_, _) => TrackOperation(async () =>
            {
                if (await ConfirmUnsavedChangesAsync("切换镀膜示例")) Load(Examples.Create(kind), markDirty: false);
            });
            examples.Items.Add(item);
        }
        examples.Opening += (_, _) =>
        {
            foreach (var item in examples.Items.OfType<MenuItem>())
                item.Header = UiLocalization.Text((string)item.Tag!);
        };
        var exampleButton = new DropDownButton { Content = ToolContent("layers", "示例"), Flyout = examples };
        exampleButton.Classes.Add("coating-tool");
        AutomationProperties.SetName(exampleButton, "镀膜示例");
        ToolTip.SetTip(exampleButton, "选择镀膜示例");
        commands.Children.Add(exampleButton);
        commands.Children.Add(SettingsMenu("设计", "设计选项", "sliders-horizontal", [("设计目标设置", 0), ("介质与膜料设置", 1)]));
        commands.Children.Add(SettingsMenu("优化", "优化选项", "settings-2", [("优化设置", 2), ("公差设置", 3)]));
        commands.Children.Add(CommandDivider());
        commands.Children.Add(ToolCommand("材料", "材料管理", "database", ManageMaterialsAsync));

        var document = new TextBlock
        {
            Name = "CoatingDocumentName", VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 280, Margin = new Thickness(20, 0, 4, 0)
        };
        document.Bind(TextBlock.TextProperty, new Binding(nameof(TextBox.Text)) { Source = _name });
        document.Bind(ToolTip.TipProperty, new Binding(nameof(TextBox.Text)) { Source = _name });
        document.BindThemeResource(TextBlock.ForegroundProperty, ThemeResourceBindings.TextMuted);
        var row = new Grid { ColumnDefinitions = new("*,Auto") };
        row.Children.Add(new ScrollViewer
        {
            Content = commands, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        Grid.SetColumn(document, 1); row.Children.Add(document);
        row.SizeChanged += (_, _) => document.IsVisible = row.Bounds.Width >= 1000 * Math.Max(1, FontSize / 13);

        var bar = new Border
        {
            Name = "LaboratoryHeader", Child = row, Padding = new Thickness(10, 5),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        bar.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        bar.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        var tools = new Style(s => s.Is<Button>().Class("coating-tool"));
        tools.Setters.Add(new Setter(MinHeightProperty, 32d));
        tools.Setters.Add(new Setter(PaddingProperty, new Thickness(9, 5)));
        tools.Setters.Add(new Setter(CornerRadiusProperty, new CornerRadius(4)));
        bar.Styles.Add(tools);
        // Retain the native hover, pressed, open, disabled and keyboard-focus states.
        var idle = new Style(s => s.Is<Button>().Class("coating-tool")
            .Not(x => x.Class(":pointerover")).Not(x => x.Class(":pressed"))
            .Not(x => x.Class(":flyout-open")).Not(x => x.Class(":focus-visible")));
        idle.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));
        idle.Setters.Add(new Setter(BorderBrushProperty, Brushes.Transparent));
        bar.Styles.Add(idle);
        return bar;
    }

    private Button ToolCommand(string text, string name, string icon, Func<Task> action, string? shortcut = null)
    {
        var button = new Button { Content = ToolContent(icon, text) };
        button.Classes.Add("coating-tool");
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, shortcut is null ? name : name + " · " + (OperatingSystem.IsMacOS() ? "⌘" : "Ctrl+") + shortcut);
        button.Click += (_, _) => TrackOperation(action);
        return button;
    }

    private DropDownButton SettingsMenu(string text, string name, string icon, (string Label, int Page)[] pages)
    {
        var menu = new MenuFlyout();
        foreach (var (label, page) in pages)
        {
            var item = new MenuItem { Header = UiLocalization.Text(label), Tag = label };
            AutomationProperties.SetName(item, label);
            item.Click += async (_, _) => await Guard(() => ShowSettingsAsync(page));
            menu.Items.Add(item);
        }
        menu.Opening += (_, _) =>
        {
            foreach (var item in menu.Items.OfType<MenuItem>()) item.Header = UiLocalization.Text((string)item.Tag!);
        };
        var button = new DropDownButton { Content = ToolContent(icon, text), Flyout = menu };
        button.Classes.Add("coating-tool"); AutomationProperties.SetName(button, name); ToolTip.SetTip(button, name);
        return button;
    }

    private static Control ToolContent(string icon, string text)
    {
        var glyph = new LocalIcon { IconName = icon, Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        glyph.BindThemeResource(LocalIcon.StrokeProperty, ThemeResourceBindings.TextSecondary);
        return new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6,
            Children = { glyph, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } }
        };
    }

    private static Border CommandDivider()
    {
        var divider = new Border { Width = 1, Height = 18, Margin = new Thickness(8, 0), VerticalAlignment = VerticalAlignment.Center };
        divider.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Border);
        return divider;
    }
}
