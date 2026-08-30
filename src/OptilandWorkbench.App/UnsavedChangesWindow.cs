using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.Theming;

namespace OptilandWorkbench.App;

internal enum UnsavedChangesChoice
{
    Cancel,
    Save,
    Discard
}

internal static class UnsavedChangesGuard
{
    internal static async Task<bool> CanContinueAsync(
        bool hasUnsavedChanges,
        Func<Task<UnsavedChangesChoice>> chooseAsync,
        Func<Task<bool>> saveAsync)
    {
        if (!hasUnsavedChanges)
        {
            return true;
        }

        return await chooseAsync() switch
        {
            UnsavedChangesChoice.Save => await saveAsync(),
            UnsavedChangesChoice.Discard => true,
            _ => false
        };
    }
}

internal sealed class UnsavedChangesWindow : Window
{
    internal UnsavedChangesWindow(string operationDescription)
    {
        Title = "未保存的修改";
        Width = 470;
        Height = 210;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var save = new Button { Content = "保存并继续", MinWidth = 104 };
        var discard = new Button { Content = "不保存", MinWidth = 88 };
        var cancel = new Button { Content = "取消", MinWidth = 88 };
        save.Click += (_, _) => Close(UnsavedChangesChoice.Save);
        discard.Click += (_, _) => Close(UnsavedChangesChoice.Discard);
        cancel.Click += (_, _) => Close(UnsavedChangesChoice.Cancel);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancel, discard, save }
        };

        Content = new Grid
        {
            Margin = new Thickness(26),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 14,
            Children =
            {
                new TextBlock
                {
                    Text = "当前项目或公差数据有未保存的修改",
                    FontSize = DisplayTypography.WindowTitle,
                    FontWeight = FontWeight.SemiBold
                },
                new TextBlock
                {
                    Text = $"是否先保存，再{operationDescription}？",
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center
                },
                buttons
            }
        };

        Grid.SetRow(((Grid)Content).Children[1], 1);
        Grid.SetRow(buttons, 2);
        ThemeChrome.ApplyDialogDecoration(this);
    }
}
