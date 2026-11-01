using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;

namespace OpticalSystemDesign.App.Laboratories.CoatingDesign;

public sealed partial class MainWindow
{
    private Window? _settingsWindow;
    private double _settingsFontSize = DisplayTypography.FontSize;

    internal async Task ShowSettingsAsync(int page)
    {
        _settingsContent.SelectedIndex = page;
        if (_settingsWindow is { } open) { open.Activate(); return; }
        DisplayTypography.ApplyRecursively(_settingsContent, _settingsFontSize);
        _settingsFontSize = DisplayTypography.FontSize;
        var scrollHost = new Border { Child = _settingsContent };
        var close = new Button { Content = "完成", MinHeight = 32, Padding = new Thickness(18, 6), HorizontalAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetName(close, "完成");
        var root = new Grid { RowDefinitions = new("*,Auto") };
        root.Children.Add(scrollHost);
        var footer = new Border { Padding = new Thickness(16, 10), Child = close };
        Grid.SetRow(footer, 1); root.Children.Add(footer);
        var dialog = new Window
        {
            Name = "CoatingSettingsWindow", Title = "镀膜设置", Width = Math.Min(760, 600 * Math.Max(1, FontSize / 13)),
            Height = Math.Min(820, 650 * Math.Max(1, FontSize / 13)), MinWidth = 500, MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            FontFamily = FontFamily, FontSize = FontSize, FontStyle = FontStyle, FontWeight = FontWeight
        };
        close.Click += (_, _) => dialog.Close();
        dialog.Content = MainWindowTitleBar.Wrap(dialog, root);
        LaboratoryPresentation.Apply(dialog);
        _settingsWindow = dialog;
        try { await dialog.ShowDialog(this); }
        finally { scrollHost.Child = null; _settingsWindow = null; }
    }

    private static Border WorkspacePane(Control content, string name)
    {
        var pane = new Border { Name = name, Child = content, BorderThickness = new Thickness(1), ClipToBounds = true };
        pane.BindThemeResource(Border.BackgroundProperty, ThemeResourceBindings.Surface);
        pane.BindThemeResource(Border.BorderBrushProperty, ThemeResourceBindings.Border);
        return pane;
    }

    private Button LayerTool(string name, string icon, Func<Task> action)
    {
        var button = ActionButton(name, action);
        button.Classes.Remove("lab-command");
        var glyph = new LocalIcon { IconName = icon, Width = 16, Height = 16 };
        glyph.BindThemeResource(LocalIcon.StrokeProperty, ThemeResourceBindings.TextSecondary);
        button.Content = glyph;
        button.Width = 30; button.Height = 30; button.MinHeight = 30;
        button.Padding = new Thickness(5); button.Margin = new Thickness(0, 0, 4, 4);
        return button;
    }
}
