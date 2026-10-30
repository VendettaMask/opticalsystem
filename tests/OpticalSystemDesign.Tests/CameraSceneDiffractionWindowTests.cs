using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Laboratories;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class CameraSceneDiffractionWindowTests
{
    [Theory]
    [InlineData("Light")] [InlineData("Dark")]
    public async Task DiffractionModeMixedABSavedSettingsReplayAndCancellationWorkOffscreen(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CameraImagingWindowTests));
        await session.Dispatch<bool>(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create("cooke"); using var files = new CameraSensorFileTests.TemporaryFiles();
            var a = await app.CameraImaging.SimulateAsync(CameraScene3DTests.Request(app.CameraImaging, 4, 4));
            var b = await app.CameraImaging.SimulateAsync(CameraSceneDiffractionTests.Request(app.CameraImaging) with { Width = 4, Height = 4 });
            var path = files.Path("mixed-diffraction.zip"); await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier, 1, b, a), path);
            var window = new CameraImagingWindow(app) { Width = 1080, Height = 820 };
            try
            {
                window.Show(); Assert.True(await window.OpenExperimentFromPathAsync(path));
                var controls = window.GetVisualDescendants().OfType<Control>().ToArray();
                ComboBox Choice(string name) => controls.OfType<ComboBox>().Single(c => c.Name == "camera-" + name);
                NumericUpDown Number(string name) => controls.OfType<NumericUpDown>().Single(c => c.Name == "camera-" + name);
                Assert.Equal((int)CameraAberration.Diffraction, Choice("aberration").SelectedIndex);
                Assert.Equal(8, Number("scene3d-pupil-grid").Value); Assert.False(Number("scene3d-rings").IsEnabled);
                Number("scene3d-guard").Value = 1; Assert.False(controls.OfType<Button>().Single(c => c.Name == "camera-run").IsEnabled);
                Number("scene3d-guard").Value = 12;
                await window.RunAsync(); Assert.NotNull(window.Result);
                await window.RecomputeExperimentAsync(); Assert.Equal(b.RawDn, window.Result!.RawDn); Assert.Equal(a.RawDn, window.Comparison!.RawDn);
                Number("exposure").Value = 20; await window.RunAsync();
                Assert.Equal(b.MeanPhotoElectrons * 2, window.Result!.MeanPhotoElectrons, 8); Assert.NotNull(window.Comparison);
                Choice("aberration").SelectedIndex = (int)CameraAberration.Geometric; Assert.False(Number("scene3d-pupil-grid").IsEnabled);
                await window.RunAsync(); Assert.Null(window.Result!.Scene3D!.Diffraction);
                Choice("aberration").SelectedIndex = (int)CameraAberration.Diffraction;
                var pending = window.RunAsync(); Number("scene3d-power").Value = .5m; await pending; Assert.Null(window.Result); Assert.NotNull(window.Comparison);
                Assert.True(await window.OpenExperimentFromPathAsync(path));
                var previous = window.Result; Assert.False(await window.OpenExperimentFromPathAsync(files.Path("missing.zip"))); Assert.Same(previous, window.Result);
                Assert.True(await window.OpenExperimentFromPathAsync(path));
                var directory = Environment.GetEnvironmentVariable("OPTICAL_CAMERA_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Number("scene3d-guard").BringIntoView(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Directory.CreateDirectory(directory); using var image = window.CaptureRenderedFrame(); Assert.NotNull(image);
                    image.Save(Path.Combine(directory, "diffraction-" + theme + ".png"), PngBitmapEncoderOptions.Default);
                }
                return true;
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
}
