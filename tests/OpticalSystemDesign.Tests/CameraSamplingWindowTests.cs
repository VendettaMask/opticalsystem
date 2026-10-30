using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Laboratories;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class CameraSamplingWindowTests
{
    [Fact]
    public async Task PupilActionComputesAndDisplaysEveryLevelWithExplicitExecutionLimits()
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CameraImagingWindowTests));
        await session.Dispatch<bool>(async () =>
        {
            using var app = WorkbenchApplication.Create(); var b = await CameraSamplingTests.Source(app.CameraImaging, 4, 4);
            b = await app.CameraImaging.RecomputeAsync(b, b.Request with { Render3D = new(Diffraction: new(4, 1, 1, 2)) });
            using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("ladder.zip");
            await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier, 1, b), path);
            var window = new CameraImagingWindow(app);
            try
            {
                window.Show(); Assert.True(await window.OpenExperimentFromPathAsync(path));
                window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "camera-sampling-axis").SelectedIndex = (int)CameraSamplingAxis.Pupil;
                await window.RunSamplingAsync();
                var c = Assert.Single(window.SamplingReport!.Comparisons); Assert.Equal(3, c.FurtherRefinements!.Count);
                Assert.Equal(b.PhotoElectrons, window.Result!.PhotoElectrons);
                window.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = 5;
                Capture(window, "Light-pupil-ladder");
                var text = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "camera-sampling-info").Text;
                Assert.Contains("本级追迹", text); Assert.Contains("瞳 64", text);
                return true;
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task FrozenSamplingRegionControlsReportOpenAndCancelPreserveAB(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CameraImagingWindowTests));
        await session.Dispatch<bool>(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create();
            var b = await CameraSamplingTests.Source(app.CameraImaging);
            var a = await app.CameraImaging.RecomputeAsync(b, b.Request with
            { Aberration = CameraAberration.Geometric, Render3D = b.Request.Render3D! with { Diffraction = null } });
            var report = await app.CameraImaging.CheckSamplingAsync(b, new(1, 3), CameraSamplingAxis.Pixel);
            using var files = new CameraSensorFileTests.TemporaryFiles(); var path = files.Path("sampling.zip");
            await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier, 1, b, a, report), path);
            var window = new CameraImagingWindow(app);
            try
            {
                window.Show(); Assert.True(await window.OpenExperimentFromPathAsync(path));
                var originalB = window.Result; var originalA = window.Comparison;
                Assert.NotNull(window.SamplingReport);
                var controls = window.GetVisualDescendants().OfType<Control>().ToArray();
                var x = controls.OfType<NumericUpDown>().Single(c => c.Name == "camera-sampling-x");
                var axis = controls.OfType<ComboBox>().Single(c => c.Name == "camera-sampling-axis");
                var check = controls.OfType<Button>().Single(c => c.Name == "camera-check-sampling");
                x.Value = null; Assert.False(check.IsEnabled); x.Value = 1.5m; Assert.False(check.IsEnabled); x.Value = 2;
                axis.SelectedIndex = (int)CameraSamplingAxis.Scene;
                Assert.Same(originalB, window.Result); Assert.Same(originalA, window.Comparison);
                var previous = window.SamplingReport;
                var pending = window.RunSamplingAsync();
                controls.OfType<Button>().Single(c => c.Name == "camera-cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await pending;
                Assert.Same(originalB, window.Result); Assert.Same(previous, window.SamplingReport);
                axis.SelectedIndex = (int)CameraSamplingAxis.Pixel;
                await window.RunSamplingAsync();
                Assert.Equal(2, window.SamplingReport!.Region.X); Assert.Single(window.SamplingReport.Comparisons);
                Assert.Same(originalB, window.Result); Assert.Same(originalA, window.Comparison);
                var tabs = controls.OfType<TabControl>().Single(); tabs.SelectedIndex = 5;
                Capture(window, theme + "-sampling");
                var info = window.GetVisualDescendants().OfType<TextBlock>().Single(c => c.Name == "camera-sampling-info");
                Assert.Contains("基准复算", info.Text); Assert.Contains("相对 L1", info.Text);
                window.Width = 760; window.Height = 540; Capture(window, theme + "-sampling-narrow");
                await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier, 1, window.Result!, window.Comparison, window.SamplingReport), path);
                Assert.True(await window.OpenExperimentFromPathAsync(path)); Assert.Equal(2, window.SamplingReport!.Region.X);
                app.Documents.NewBlank(); Assert.NotNull(window.Result); Assert.NotNull(window.SamplingReport);
                controls.OfType<NumericUpDown>().Single(c => c.Name == "camera-exposure").Value = 20;
                Assert.Null(window.Result); Assert.Null(window.SamplingReport); Assert.NotNull(window.Comparison);
                Assert.False(check.IsEnabled);
                return true;
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static void Capture(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var path = Environment.GetEnvironmentVariable("OPTICAL_CAMERA_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(path); using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
        frame.Save(Path.Combine(path, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
