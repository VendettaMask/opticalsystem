using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.Theming;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class NumericInputChromeTests
{
    [Fact]
    public async Task NumericEditorsHaveNoArrowButtonsIncludingLateCreatedControlsInEveryTheme()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OptilandWorkbench.App.App));
        await session.Dispatch(() =>
        {
            var panel = new StackPanel();
            var window = new Window { Width = 450, Height = 220, Content = panel };
            try
            {
                window.Show();
                foreach (var theme in ThemeRegistry.ConcreteThemes)
                {
                    ThemeApplicationService.Apply(global::Avalonia.Application.Current!, theme.SettingsValue);
                    panel.Children.Clear();
                    // No local ShowButtonSpinner setting: the application-wide rule must apply.
                    var number = new NumericUpDown { Width = 360, Minimum = -10, Maximum = 20, Value = 16.4798m };
                    panel.Children.Add(number);
                    window.UpdateLayout();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    AssertNoArrows(number);
                    var text = Assert.Single(number.GetVisualDescendants().OfType<TextBox>());
                    var right = text.TranslatePoint(new Point(text.Bounds.Width, 0), number);
                    Assert.NotNull(right);
                    Assert.InRange(right.Value.X, number.Bounds.Width - 8, number.Bounds.Width);
                    Assert.Equal(16.4798m, number.Value);
                    number.Text = "-3.75";
                    Assert.Equal(-3.75m, number.Value);
                    Assert.Equal(-10, number.Minimum);
                    Assert.Equal(20, number.Maximum);
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SystemApertureAndDisplayDialogFollowTheSameNumericRule()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(global::OptilandWorkbench.App.App));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, "Light");
            using var app = WorkbenchApplication.Create("cooke");
            using var system = new SystemPropertiesPanel(app.Prescription, app.Materials, app.Events);
            var window = new Window { Width = 500, Height = 700, Content = system };
            var display = new DisplaySettingsWindow(new AppSettings());
            try
            {
                window.Show();
                window.UpdateLayout();
                var aperture = Assert.IsType<NumericUpDown>(typeof(SystemPropertiesPanel)
                    .GetField("_apertureValue", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(system));
                Assert.False(aperture.ShowButtonSpinner);
                display.Show();
                display.UpdateLayout();
                foreach (var root in new Control[] { system, display })
                {
                    var inputs = root.GetLogicalDescendants().OfType<NumericUpDown>().ToArray();
                    Assert.NotEmpty(inputs);
                    Assert.All(inputs, input => Assert.False(input.ShowButtonSpinner));
                    foreach (var input in inputs.Where(input => input.IsEffectivelyVisible))
                        AssertNoArrows(input);
                }
            }
            finally { display.Close(); window.Close(); }
        }, CancellationToken.None);
    }

    private static void AssertNoArrows(NumericUpDown input)
    {
        Assert.False(input.ShowButtonSpinner);
        var spinner = Assert.Single(input.GetVisualDescendants().OfType<ButtonSpinner>());
        Assert.False(spinner.ShowButtonSpinner);
        Assert.DoesNotContain(spinner.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible);
    }
}
