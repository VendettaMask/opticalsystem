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
public sealed class CoatingLayerEditorTests
{
    public static AppBuilder BuildAvaloniaApp()
    {
        var capture = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPTILAND_COATING_LAYER_CAPTURE_DIR"));
        var builder = AppBuilder.Configure<global::OptilandWorkbench.App.App>();
        if (capture) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !capture });
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task PhysicalLayerEditorKeepsInvalidDraftVisibleAndCommitsOneUndoableChange(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CoatingLayerEditorTests));
        await session.Dispatch(() =>
        {
            ThemeApplicationService.Apply(global::Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create("cooke");
            var window = new CoatingLayerEditorWindow(app.Prescription, app.Events, 1);
            try
            {
                window.Show(); Tick();
                Button Button(string name) => window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
                T Input<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => AutomationProperties.GetName(c) == name);
                Button("AddCoatingLayer").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); Tick();
                var revision = app.Events.Revision;
                Input<TextBox>("膜层材料").Text = "N-BK7";
                Input<TextBox>("基础厚度（nm）").Text = "100";
                Input<TextBox>("厚度倍率").Text = "1.25";
                Input<TextBox>("折射率偏移").Text = "0.1";
                Input<TextBox>("消光系数偏移").Text = "0.02";
                Input<CheckBox>("厚度倍率变量").IsChecked = true;
                Assert.Equal(revision, app.Events.Revision); Assert.Empty(app.Prescription.GetCoatingLayers(1));
                Assert.Contains("accent", Button("ApplyCoatingLayers").Classes);
                Tick(); Capture("draft");
                Input<TextBox>("厚度倍率").Text = "abc";
                Button("ApplyCoatingLayers").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); Tick();
                Assert.Equal(revision, app.Events.Revision); Assert.Equal("abc", Input<TextBox>("厚度倍率").Text);
                var error = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text?.Contains("有限数值") == true);
                Assert.True(error.IsEffectivelyVisible); var position = error.TranslatePoint(default, window);
                Assert.NotNull(position); Assert.True(position.Value.Y + error.Bounds.Height < window.Bounds.Height);
                Capture("invalid");
                Input<TextBox>("厚度倍率").Text = "1.25";
                Button("ApplyCoatingLayers").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); Tick();
                Assert.False(window.IsVisible); Assert.Equal(revision + 1, app.Events.Revision);
                var layer = Assert.Single(app.Prescription.GetCoatingLayers(1));
                Assert.Equal(1.25, layer.Multiplier); Assert.Equal(.1, layer.IndexOffset); Assert.True(layer.MultiplierVariable);
                Assert.True(app.Documents.Undo()); Assert.Empty(app.Prescription.GetCoatingLayers(1));
                Assert.True(app.Documents.Redo()); Assert.Single(app.Prescription.GetCoatingLayers(1));

                void Tick() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
                void Capture(string name)
                {
                    var directory = Environment.GetEnvironmentVariable("OPTILAND_COATING_LAYER_CAPTURE_DIR");
                    if (string.IsNullOrWhiteSpace(directory)) return;
                    Directory.CreateDirectory(directory); using var image = window.CaptureRenderedFrame(); Assert.NotNull(image);
                    image.Save(Path.Combine(directory, $"coating-{theme.ToLowerInvariant()}-{name}.png"), PngBitmapEncoderOptions.Default);
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
}
