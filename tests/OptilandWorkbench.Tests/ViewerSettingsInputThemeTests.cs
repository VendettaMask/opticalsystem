using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.Theming;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class ViewerSettingsInputThemeTests
{
    private const string CaptureDirectory = "OPTILAND_VIEWER_INPUTS_CAPTURE_DIR";

    // Capture this fixture in isolation so Skia glyphs are not mixed with fake headless glyphs.
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(CaptureDirectory));
        var builder = AppBuilder.Configure<global::OptilandWorkbench.App.App>();
        if (capture)
        {
            builder.UseSkia();
        }
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Fact]
    public async Task NumericFramesFitCompactInputsAndOnlyPickersUseShadedSurfaces()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(ViewerSettingsInputThemeTests));
        await session.Dispatch(() =>
        {
            var app = Assert.IsType<global::OptilandWorkbench.App.App>(global::Avalonia.Application.Current);
            ThemeApplicationService.Apply(app, "Light");
            using var application = WorkbenchApplication.Create("cooke");
            using var viewer = new ViewerPanel(application.Visualization, application.Events,
                new SurfaceSelectionService(), SceneDimension.TwoDimensional)
            { IsLocked = true };
            var window = new Window { Width = 600, Height = 740, Content = viewer };
            try
            {
                window.Show();
                window.UpdateLayout();
                var refresh = viewer.GetVisualDescendants().OfType<Button>()
                    .Single(button => button.Content is LocalIcon { IconName: "refresh-cw" });
                var toggle = Assert.IsType<Button>(Assert.IsType<StackPanel>(refresh.Parent).Children[0]);
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.UpdateLayout();
                var numbers = viewer.GetVisualDescendants().OfType<NumericUpDown>().ToArray();
                var pickers = viewer.GetVisualDescendants().OfType<ComboBox>().ToArray();
                Assert.Equal(4, numbers.Length);
                Assert.Equal(7, pickers.Length);

                foreach (var theme in new[] { "Light", "Dark", IsekaiTheme.SettingsValue, PixelTheme.SettingsValue })
                {
                    ThemeApplicationService.Apply(app, theme);
                    window.UpdateLayout();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    var editors = numbers.Cast<Control>().Concat(pickers).ToArray();
                    var form = Assert.IsAssignableFrom<Grid>(editors[0].Parent);
                    Assert.Equal(22, form.Children.Count);
                    Assert.True(form.Bounds.Height <= 210);
                    Assert.All(editors, editor =>
                    {
                        var label = form.Children.OfType<TextBlock>().Single(candidate =>
                            Grid.GetRow(candidate) == Grid.GetRow(editor)
                            && Grid.GetColumn(candidate) == Grid.GetColumn(editor) - 2);
                        Assert.True(label.Bounds.Right < editor.Bounds.Left);
                        Assert.InRange(Math.Abs(label.Bounds.Center.Y - editor.Bounds.Center.Y), 0, 1);
                        Assert.Equal(editors.First(other => Grid.GetColumn(other) == Grid.GetColumn(editor)).Bounds.Left,
                            editor.Bounds.Left);
                    });
                    var palette = ThemeRegistry.FromSettings(theme).Palette!;
                    foreach (var number in numbers)
                    {
                        Assert.Equal(palette.SettingsSurface, Assert.IsAssignableFrom<ISolidColorBrush>(number.Background).Color);
                        Assert.False(number.ShowButtonSpinner);
                        var spinner = number.GetVisualDescendants().OfType<ButtonSpinner>().Single();
                        var editor = number.GetVisualDescendants().OfType<TextBox>().Single();
                        Assert.Equal(0, spinner.MinHeight);
                        Assert.Equal(0, editor.MinHeight);
                        Assert.True(spinner.Bounds.Height <= number.Bounds.Height);
                        Assert.True(editor.Bounds.Height <= number.Bounds.Height);
                        Assert.Equal(new Thickness(1), spinner.BorderThickness);
                    }
                    Assert.All(pickers, picker => Assert.Equal(palette.SubtleSurface,
                        Assert.IsAssignableFrom<ISolidColorBrush>(picker.Background).Color));

                    var directory = Environment.GetEnvironmentVariable(CaptureDirectory);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                        using var bitmap = new RenderTargetBitmap(new PixelSize(600, 740));
                        bitmap.Render(window);
                        bitmap.Save(Path.Combine(directory, $"{theme.ToLowerInvariant()}.png"), PngBitmapEncoderOptions.Default);
                    }
                }

                window.Width = 360;
                window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                window.UpdateLayout();
                var narrowForm = Assert.IsAssignableFrom<Grid>(numbers[0].Parent);
                Assert.Equal(3, narrowForm.ColumnDefinitions.Count);
                Assert.Equal(11, narrowForm.RowDefinitions.Count);
                foreach (var editor in numbers.Cast<Control>().Concat(pickers))
                {
                    Assert.Equal(2, Grid.GetColumn(editor));
                    Assert.True(editor.Bounds.Right <= narrowForm.Bounds.Width);
                    var label = narrowForm.Children.OfType<TextBlock>()
                        .Single(candidate => Grid.GetRow(candidate) == Grid.GetRow(editor));
                    Assert.True(label.Bounds.Right < editor.Bounds.Left);
                }

                var rayCount = numbers.Single(number => number.Maximum == 101);
                var text = rayCount.GetVisualDescendants().OfType<TextBox>().Single();
                text.Focus();
                text.Text = "9";
                toggle.Focus();
                Assert.Equal(9m, rayCount.Value);
                Assert.Equal(1m, rayCount.Minimum);
                Assert.Equal(101m, rayCount.Maximum);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }
}
