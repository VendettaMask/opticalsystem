using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.App;

public sealed partial class MainWindow
{
    private void RegisterSequentialExamples()
    {
        foreach (var example in SequentialExampleCatalog.Examples)
            _actions.Register(example.ActionId, $"打开示例：{example.Name}", "示例",
                () => OpenSequentialExampleAsync(example));
    }

    private void RegisterNonSequentialExamples()
    {
        foreach (var example in NonSequentialExampleCatalog.Examples)
            _actions.Register(example.ActionId, $"打开示例：{example.Name}", "示例",
                () => OpenNonSequentialExampleAsync(example));
    }

    private Task OpenNonSequentialExampleAsync(NonSequentialExample example) =>
        NonSequentialFileImport.ImportAsync(this, _application.NonSequential, example.FilePath);

    private async Task OpenSequentialExampleAsync(SequentialExample example)
    {
        if (!File.Exists(example.FilePath))
            throw new FileNotFoundException($"找不到示例文件：{example.Name}", example.FilePath);
        if (!await ConfirmUnsavedChangesAsync("打开示例")) return;

        await _panels.SaveCurrentSessionAsync();
        await _application.Documents.OpenAsync(example.FilePath);
    }

    private DropDownButton RibbonSequentialExamplesButton() => CreateSequentialExamplesButton(
        example => _actions.ExecuteAsync(_actions.Find(example.ActionId)));

    private DropDownButton RibbonNonSequentialExamplesButton() => CreateNonSequentialExamplesButton(
        example => _actions.ExecuteAsync(_actions.Find(example.ActionId)));

    internal static DropDownButton CreateSequentialExamplesButton(Func<SequentialExample, Task> openExample)
    {
        var flyout = new MenuFlyout();
        foreach (var group in SequentialExampleCatalog.Groups)
            flyout.Items.Add(CreateExampleCategory(group.DisplayName, group.Examples.Select(example =>
                (example.Name, example.RelativePath, example.ActionId, (Func<Task>)(() => openExample(example))))));
        return CreateExamplesButton(flyout);
    }

    internal static DropDownButton CreateNonSequentialExamplesButton(Func<NonSequentialExample, Task> openExample)
    {
        var flyout = new MenuFlyout();
        foreach (var group in NonSequentialExampleCatalog.Groups)
        {
            var category = CreateExampleCategory(group.DisplayName, group.Examples.Select(example =>
                (example.Name, example.RelativePath, example.ActionId, (Func<Task>)(() => openExample(example)))));
            flyout.Items.Add(category);
        }
        return CreateExamplesButton(flyout);
    }

    private static MenuItem CreateExampleCategory(string displayName,
        IEnumerable<(string Name, string RelativePath, string ActionId, Func<Task> Open)> examples)
    {
        var category = new MenuItem { Header = displayName, MinWidth = 230, Padding = new Thickness(10, 8) };
        AutomationProperties.SetName(category, displayName);
        category.Classes.Add("ribbon-menu-item");
        foreach (var example in examples)
        {
            var header = new LocalIconLabel("optical-examples", example.Name, 20);
            var item = new MenuItem { Header = header, MinWidth = 230, Padding = new Thickness(10, 8) };
            AutomationProperties.SetName(item, example.Name);
            AutomationProperties.SetAutomationId(item, $"ribbon-{example.ActionId}");
            ToolTip.SetTip(item, example.RelativePath);
            item.Classes.Add("ribbon-menu-item");
            AttachRibbonMenuItemHover(item, header);
            item.Click += async (_, _) => await example.Open();
            category.Items.Add(item);
        }
        return category;
    }

    private static DropDownButton CreateExamplesButton(MenuFlyout flyout)
    {
        var content = RibbonDropDownCommandContent("optical-examples", "示例");
        var button = new DropDownButton
        {
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Flyout = flyout,
            Content = content
        };
        BindRibbonCommandLayout(button);
        BindRibbonCommandChrome(button);
        AutomationProperties.SetName(button, "示例");
        AutomationProperties.SetAutomationId(button, "ribbon-menu-examples");
        button.Classes.Add("ribbon-command");
        button.Classes.Add("ribbon-dropdown");
        AttachRibbonCommandHover(button, content);
        ToolTip.SetTip(button, "示例");
        return button;
    }
}
