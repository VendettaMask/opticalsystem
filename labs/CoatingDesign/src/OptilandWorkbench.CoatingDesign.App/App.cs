using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.Theming;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Application.Formatting;

namespace OptilandWorkbench.CoatingDesign.App;

public sealed class App : Avalonia.Application
{
    public override void Initialize()
    {
        Name = "光学镀膜设计实验室";
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://Avalonia.Controls.DataGrid"))
        { Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml") });
        foreach (var theme in ThemeRegistry.ConcreteThemes)
            Resources.ThemeDictionaries[theme.RequestedVariant] = theme.BuildResources();
        ThemeApplicationService.Apply(this, LaboratoryDisplay.Read().Theme);
        Styles.Add(new Style(s => s.OfType<TextBlock>())
        {
            Setters =
        { new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension(ThemeResourceBindings.TextPrimary)) }
        });
        Styles.Add(new Style(s => s.OfType<Button>())
        {
            Setters =
        { new Setter(Button.MinHeightProperty, UiDensity.StandardControlHeight), new Setter(Button.PaddingProperty, new Thickness(12, 4)) }
        });
        Styles.Add(new Style(s => s.OfType<TextBox>())
        {
            Setters =
        { new Setter(TextBox.MinHeightProperty, UiDensity.StandardControlHeight) }
        });
        Styles.Add(new Style(s => s.OfType<DataGrid>())
        {
            Setters =
        { new Setter(DataGrid.RowHeightProperty, UiDensity.CompactTableRowHeight), new Setter(DataGrid.ColumnHeaderHeightProperty, UiDensity.TableHeaderHeight) }
        });
        Styles.Add(new StandardActionButtonStyles());
        Styles.Add(new BlueThemeStyles());
    }
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}

// Read only the formal application's display preferences. Never load its mutable settings service.
internal sealed record LaboratoryDisplay(string Theme = "Light", string FontFamily = "", double FontSize = 13, string FontShape = "Regular",
    int DecimalPlaces = 3, int UpperScientificExponent = 6, int LowerScientificExponent = -4)
{
    public static LaboratoryDisplay Read()
    {
        var directory = Environment.GetEnvironmentVariable("OPTILAND_SETTINGS_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            var data = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            directory = Path.Combine(string.IsNullOrWhiteSpace(data) ? AppContext.BaseDirectory : data, "OptilandWorkbench");
        }
        try
        {
            var path = Path.Combine(directory, "settings.json");
            if (!File.Exists(path)) return new();
            var json = System.Text.Encoding.UTF8.GetString(BoundedFile.ReadAllBytes(path, 1024 * 1024, "显示设置"));
            var display = JsonSerializer.Deserialize<LaboratoryDisplay>(json) ?? new();
            return display with
            {
                Theme = ThemeRegistry.NormalizeSettingsValue(display.Theme),
                FontSize = double.IsFinite(display.FontSize) ? Math.Clamp(display.FontSize, 9, 32) : 13
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return new(); }
    }
    public void Apply(Window window, string? theme = null)
    {
        var selected = ThemeApplicationService.Apply(Avalonia.Application.Current!, theme ?? Theme);
        var visual = selected.ResolveVisual(window.ActualThemeVariant);
        window.FontFamily = visual.SettingsValue == "Pixel" ? visual.UiFontFamily : string.IsNullOrWhiteSpace(FontFamily) ? Avalonia.Media.FontFamily.Default : new(FontFamily);
        window.FontSize = FontSize;
        window.FontWeight = FontShape is "Bold" or "BoldItalic" ? FontWeight.Bold : FontWeight.Normal;
        window.FontStyle = FontShape is "Italic" or "BoldItalic" ? FontStyle.Italic : FontStyle.Normal;
        NumericDisplayFormatter.Configure(new(DecimalPlaces, UpperScientificExponent, LowerScientificExponent));
    }
}

internal static class LaboratoryTypography
{
    public const double Body = 13;
    public const double Caption = 10.5;
    public const double Section = 16;
}
