using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
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
public sealed class GradientIndexEditorPanelTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTILAND_GRIN_EDITOR_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OptilandWorkbench.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Theory]
    [InlineData("Light", 840)]
    [InlineData("Dark", 840)]
    [InlineData("Light", 660)]
    public async Task EditorValidatesDraftsExplainsLimitationsAndCommitsAllThreeTabs(string theme, int width)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(GradientIndexEditorPanelTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create("cooke");
            var window = new GradientIndexMaterialEditorWindow(app.Prescription, app.Events, 1) { Width = width };
            try
            {
                window.Show(); Tick(); var revision = app.Events.Revision;
                var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
                var profile = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "GradientIndexProfile");
                profile.SelectedIndex = 0; Tick();
                var unsupported = Input<CheckBox>("nr1 变量"); Assert.False(unsupported.IsEnabled);
                Assert.Contains("不可微", AutomationProperties.GetHelpText(unsupported)); Assert.NotNull(ToolTip.GetTip(unsupported));
                Assert.True(Input<TextBox>("nr1").IsEnabled);
                profile.SelectedIndex = 4; Tick();
                Input<TextBox>("n0").Text = "1.55"; Input<CheckBox>("n0 变量").IsChecked = true;
                Input<TextBox>("n0 下限").Text = "1"; Input<TextBox>("n0 上限").Text = "2";
                Assert.Contains("accent", Button("ApplyGradientIndexMaterial").Classes);
                Assert.DoesNotContain("accent", Button("CancelGradientIndexMaterial").Classes);
                Tick(); Capture("coefficients");
                var inputRight = Input<TextBox>("GRIN 材料名称").TranslatePoint(default, window)!.Value.X + Input<TextBox>("GRIN 材料名称").Bounds.Width;
                Assert.InRange(inputRight, 0, window.Bounds.Width - 40);
                tabs.SelectedIndex = 1; Tick();
                window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "UseGradient5Dispersion").IsChecked = true; Tick();
                Input<TextBox>("K1 系数").Text = "0.01 0.001";
                Input<TextBox>("K2 系数").Text = "0 0"; Input<TextBox>("K3 系数").Text = "0 0";
                Capture("dispersion");
                tabs.SelectedIndex = 2; Tick();
                Assert.Equal("1E-12", Input<TextBox>("最小步长").Text);
                Input<TextBox>("最大步长").Text = "0.2"; Capture("integration");
                tabs.SelectedIndex = 0; Tick(); Input<TextBox>("n0").Text = "abc";
                Button("ApplyGradientIndexMaterial").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); Tick();
                Assert.Equal(revision, app.Events.Revision); Assert.Null(app.Prescription.GetGradientIndexMaterial(1));
                Assert.Equal("abc", Input<TextBox>("n0").Text);
                var error = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "GradientIndexMessage");
                Assert.Contains("有限数值", error.Text); Assert.True(error.IsEffectivelyVisible);
                var position = error.TranslatePoint(default, window); Assert.NotNull(position);
                Assert.InRange(position.Value.Y + error.Bounds.Height, 0, window.Bounds.Height); Capture("invalid");
                Input<TextBox>("n0").Text = "1.55";
                Button("ApplyGradientIndexMaterial").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); Tick();
                Assert.False(window.IsVisible); Assert.Equal(revision + 1, app.Events.Revision);
                var actual = app.Prescription.GetGradientIndexMaterial(1)!;
                Assert.Equal(1.55, actual.Coefficients[0].Value); Assert.True(actual.Coefficients[0].Variable);
                Assert.Equal(new[] { .01, .001 }, actual.Dispersion!.K[0]); Assert.Equal(.2, actual.Integration.MaximumStep);
                Assert.True(app.Documents.Undo()); Assert.Null(app.Prescription.GetGradientIndexMaterial(1));
                Assert.True(app.Documents.Redo()); Assert.NotNull(app.Prescription.GetGradientIndexMaterial(1));

                T Input<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => AutomationProperties.GetName(c) == name);
                Button Button(string name) => window.GetVisualDescendants().OfType<Button>().Single(c => c.Name == name);
                void Tick() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
                void Capture(string name)
                {
                    Tick(); var directory = Environment.GetEnvironmentVariable("OPTILAND_GRIN_EDITOR_CAPTURE_DIR");
                    if (string.IsNullOrWhiteSpace(directory)) return;
                    Directory.CreateDirectory(directory); using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                    frame.Save(Path.Combine(directory, $"grin-editor-{theme.ToLowerInvariant()}-{width}-{name}.png"), PngBitmapEncoderOptions.Default);
                }
            }
            finally { window.Close(); }
            var cancelWindow = new GradientIndexMaterialEditorWindow(app.Prescription, app.Events, 1);
            try
            {
                var revision = app.Events.Revision; cancelWindow.Show(); Dispatcher.UIThread.RunJobs(); cancelWindow.UpdateLayout();
                cancelWindow.GetVisualDescendants().OfType<TextBox>().Single(c => AutomationProperties.GetName(c) == "n0").Text = "1.8";
                cancelWindow.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "CancelGradientIndexMaterial").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(cancelWindow.IsVisible); Assert.Equal(revision, app.Events.Revision);
                Assert.Equal(1.55, app.Prescription.GetGradientIndexMaterial(1)!.Coefficients[0].Value);
            }
            finally { cancelWindow.Close(); }
        }, CancellationToken.None);
    }
}
