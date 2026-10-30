using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Laboratories;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class CameraPixelAreaWindowTests
{
    [Theory]
    [InlineData("Light", "zh-CN")]
    [InlineData("Dark", "en-US")]
    [InlineData("Light", "ja-JP")]
    public async Task AreaControlsKeepABAndRestoreSavedSamplingAcrossThemesAndLanguages(string theme, string language)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CameraImagingWindowTests));
        await session.Dispatch<bool>(async () =>
        {
            var previousLanguage = UiLocalization.CurrentLanguage;
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme); UiLocalization.Apply(language);
            using var app = WorkbenchApplication.Create(); using var files = new CameraSensorFileTests.TemporaryFiles();
            var window = new CameraImagingWindow(app) { Width = 760, Height = 680 };
            try
            {
                window.Show(); var controls = window.GetVisualDescendants().OfType<Control>().ToArray();
                Assert.True(await window.ImportSensorFromPathAsync(Path.Combine(CameraSensorFileTests.Examples, "generic-sensor.camera-sensor.json")));
                var sampling = controls.OfType<ComboBox>().Single(c => c.Name == "camera-pixel-sampling");
                var qe = controls.OfType<ComboBox>().Single(c => c.Name == "camera-qe-area");
                var fill = controls.OfType<NumericUpDown>().Single(c => c.Name == "camera-fill-factor");
                Assert.False(fill.IsEnabled); Assert.False(qe.IsEnabled);
                controls.OfType<ComboBox>().Single(c => c.Name == "camera-size").SelectedIndex = 4;
                await window.RunAsync(); window.PinComparison(); var a = window.Comparison;
                sampling.SelectedIndex = 2; Assert.True(fill.IsEnabled); Assert.True(qe.IsEnabled);
                fill.Value = 1; qe.SelectedIndex = 1;
                await window.RunAsync(); var b = Assert.IsType<CameraImagingResult>(window.Result);
                Assert.Equal(new CameraPixelSamplingParameters(4, 1, false), b.Request.PixelSampling);
                Assert.False(b.Request.SensorIdentity!.Modified); // Sampling is stored with the experiment, not the sensor template.
                Assert.Same(a, window.Comparison); Assert.Equal(32, b.Raw.Width);
                var path = files.Path("pixel-area.zip");
                await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier, 1, b, a), path);
                sampling.SelectedIndex = 0; Assert.Null(window.Result); Assert.Same(a, window.Comparison);
                Assert.True(await window.OpenExperimentFromPathAsync(path));
                Assert.Equal(2, sampling.SelectedIndex); Assert.Equal(1, fill.Value); Assert.Equal(1, qe.SelectedIndex);
                await window.RecomputeExperimentAsync(); Assert.Equal(b.RawDn, window.Result!.RawDn);
                var directory = Environment.GetEnvironmentVariable("OPTICAL_CAMERA_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    qe.BringIntoView(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Directory.CreateDirectory(directory); using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                    frame.Save(Path.Combine(directory, $"pixel-area-{theme}-{language}.png"), PngBitmapEncoderOptions.Default);
                }
                return true;
            }
            finally { window.Close(); UiLocalization.Apply(previousLanguage); }
        }, CancellationToken.None);
    }
}
