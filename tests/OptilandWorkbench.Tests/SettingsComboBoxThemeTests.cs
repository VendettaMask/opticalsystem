using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.Theming;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class SettingsComboBoxThemeTests
{
    [Fact]
    public async Task SettingsScopeShadesNewDropdownsWithoutChangingOtherInputs()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OptilandWorkbench.App.App));
        await session.Dispatch(() =>
        {
            var app = Assert.IsType<global::OptilandWorkbench.App.App>(global::Avalonia.Application.Current);
            var text = new TextBox { Text = "plain" };
            var number = new NumericUpDown { Value = 7 };
            var content = new StackPanel { Children = { text, number } };
            var card = new Border { Child = content };
            var outside = new ComboBox { ItemsSource = new[] { "outside" }, SelectedIndex = 0 };
            var window = new Window
            {
                Width = 400,
                Height = 320,
                Content = new StackPanel { Children = { card, outside } }
            };
            try
            {
                ThemeApplicationService.Apply(app, "Light");
                window.Show();
                window.UpdateLayout();
                var originalText = text.Background;
                var originalNumber = number.Background;
                var originalOutside = outside.Background;
                SettingsPanelChrome.ApplyInputStyles(card);
                SettingsPanelChrome.ApplyInputStyles(card);
                Assert.Single(card.Styles);
                var choice = new ComboBox { ItemsSource = new[] { "First", "Second" }, SelectedIndex = 0 };
                content.Children.Add(choice);
                window.UpdateLayout();
                Assert.Equal(originalText, text.Background);
                Assert.Equal(originalNumber, number.Background);
                Assert.Equal(originalOutside, outside.Background);

                foreach (var theme in ThemeRegistry.ConcreteThemes)
                {
                    ThemeApplicationService.Apply(app, theme.SettingsValue);
                    window.MouseMove(new Point(395, 315));
                    window.UpdateLayout();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    var normal = theme.Palette!.SubtleSurface;
                    Assert.Equal(normal, BrushColor(choice.Background));
                    var background = choice.GetVisualDescendants().OfType<Border>()
                        .Single(border => border.Name == "Background");
                    Assert.Equal(normal, BrushColor(background.Background));
                    var center = choice.TranslatePoint(new Rect(choice.Bounds.Size).Center, window);
                    Assert.NotNull(center);
                    window.MouseMove(center.Value);
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Assert.True(choice.IsPointerOver);
                    Assert.NotEqual(normal, BrushColor(background.Background));
                    window.MouseMove(new Point(395, 315));
                    choice.SelectedIndex = 1;
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Assert.Equal("Second", choice.SelectedItem);
                    Assert.Equal(normal, BrushColor(background.Background));
                }
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AnalysisSystemAndDisplaySettingsAllUseShadedDropdowns()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OptilandWorkbench.App.App));
        await session.Dispatch(() =>
        {
            var app = Assert.IsType<global::OptilandWorkbench.App.App>(global::Avalonia.Application.Current);
            ThemeApplicationService.Apply(app, "Light");
            using var application = WorkbenchApplication.Create("cooke");
            using var analysis = new AnalysisPanel(application.Analyses, application.Visualization,
                application.Documents, application.Events, new AppSettings(), "Spot Diagram")
            { IsLocked = true };
            var settings = Assert.IsType<Border>(typeof(AnalysisPanel)
                .GetField("_settingsHost", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(analysis));
            settings.IsVisible = true;
            using var system = new SystemPropertiesPanel(application.Prescription, application.Materials, application.Events);
            var host = new Window { Width = 900, Height = 700, Content = analysis };
            var display = new DisplaySettingsWindow(new AppSettings());
            try
            {
                host.Show();
                host.UpdateLayout();
                AssertShaded(settings);
                host.Content = system;
                host.UpdateLayout();
                AssertShaded(system);
                display.Show();
                display.UpdateLayout();
                AssertShaded(display);
            }
            finally
            {
                display.Close();
                host.Close();
            }
        }, CancellationToken.None);
    }

    private static void AssertShaded(Control root)
    {
        var choices = root.GetLogicalDescendants().OfType<ComboBox>().ToArray();
        Assert.NotEmpty(choices);
        Assert.All(choices, choice => Assert.Equal(ThemePalette.Light.SubtleSurface, BrushColor(choice.Background)));
    }

    private static Color BrushColor(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
}
