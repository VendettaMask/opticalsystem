using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OptilandWorkbench.App.Panels;
using OptilandWorkbench.App.Theming;
using OptilandWorkbench.Application.Services;

namespace OptilandWorkbench.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class PolarizationSettingsPanelTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTILAND_POLARIZATION_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OptilandWorkbench.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Theory]
    [InlineData("Light", 240)]
    [InlineData("Dark", 280)]
    public async Task PolarizationControlsCommitTogetherValidateAndFitCompactSidebar(string theme, double width)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(PolarizationSettingsPanelTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create("cooke");
            using var panel = new SystemPropertiesPanel(app.Prescription, app.Materials, app.Events);
            var window = new Window { Width = width, Height = 720, Content = panel };
            try
            {
                window.Show(); Tick();
                var scroll = Assert.IsType<ScrollViewer>(panel.Content); var sections = Assert.IsType<StackPanel>(scroll.Content);
                foreach (var border in sections.Children.Cast<Border>())
                {
                    var body = Assert.IsType<StackPanel>(border.Child);
                    var header = (Button)body.Children[0];
                    if (header.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "偏振"))
                        header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                Tick();
                T Input<T>(string name) where T : Control => panel.GetVisualDescendants().OfType<T>().Single(c => AutomationProperties.GetName(c) == name);
                var apply = Input<Button>("应用系统偏振设置");
                Assert.Contains("accent", apply.Classes);
                var x = Input<TextBox>("Jones Jx"); var y = Input<TextBox>("Jones Jy");
                var xp = Input<TextBox>("Jones X 相位（度）"); var yp = Input<TextBox>("Jones Y 相位（度）");
                var unpolarized = Input<CheckBox>("系统非偏振");
                var axis = Input<ComboBox>("偏振参考轴");
                Assert.True(unpolarized.IsChecked); Assert.True(x.IsEffectivelyEnabled); Assert.True(y.IsEffectivelyEnabled);
                var initial = app.Prescription.GetPolarizationSettings();
                x.Text = "1"; y.Text = "2"; xp.Text = "30"; yp.Text = "-40"; axis.SelectedItem = "Y";
                Assert.Equal(initial, app.Prescription.GetPolarizationSettings());
                apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick();
                var changed = app.Prescription.GetPolarizationSettings(); Assert.Equal(2, changed.Jy); Assert.Equal("Y", changed.ReferenceAxis);
                var revision = app.Events.Revision;
                x.Text = "abc"; apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick();
                Assert.Equal(revision, app.Events.Revision); Assert.Equal(changed, app.Prescription.GetPolarizationSettings());
                Assert.Equal("abc", x.Text);
                Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text?.Contains("有限数值") == true);
                var error = panel.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text?.Contains("有限数值") == true);
                var position = error.TranslatePoint(new Point(), scroll);
                Assert.NotNull(position);
                Assert.True(position.Value.Y >= 0 && position.Value.Y + error.Bounds.Height <= scroll.Bounds.Height + 1);
                Capture("invalid");
                x.Text = "0"; y.Text = "0"; apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick();
                Assert.Equal(revision, app.Events.Revision);
                Assert.Contains(panel.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text?.Contains("不能全零") == true);
                x.Text = "1"; y.Text = "2"; unpolarized.IsChecked = false;
                apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Tick();
                Assert.False(app.Prescription.GetPolarizationSettings().Unpolarized);
                Assert.True(app.Documents.Undo()); Tick(); Assert.True(unpolarized.IsChecked);
                Assert.True(app.Documents.Redo()); Tick(); Assert.False(unpolarized.IsChecked);
                Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
                Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
                foreach (var input in new Control[] { x, y, xp, yp, axis })
                {
                    var row = Assert.IsType<Grid>(input.Parent);
                    Assert.True(input.Bounds.Width >= 70); Assert.True(input.Bounds.Right <= row.Bounds.Width + 1);
                }
                Capture("saved");

                void Tick() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
                void Capture(string name)
                {
                    var directory = Environment.GetEnvironmentVariable("OPTILAND_POLARIZATION_CAPTURE_DIR");
                    if (string.IsNullOrWhiteSpace(directory)) return;
                    Directory.CreateDirectory(directory);
                    unpolarized.BringIntoView(); apply.BringIntoView(); Tick();
                    using var image = window.CaptureRenderedFrame(); Assert.NotNull(image);
                    image.Save(Path.Combine(directory, $"polarization-{theme.ToLowerInvariant()}-{name}.png"), PngBitmapEncoderOptions.Default);
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
}
