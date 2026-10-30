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
public sealed class CameraScene3DWindowTests
{
    [Theory]
    [InlineData("Light", "zh-CN")] [InlineData("Dark", "en-US")]
    public async Task SceneSelectionActiveAreaABImportReplayAndCancellationWorkOffscreen(string theme, string language)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(CameraImagingWindowTests));
        await session.Dispatch<bool>(async () =>
        {
            var previous = UiLocalization.CurrentLanguage; UiLocalization.Apply(language);
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create("cooke"); using var files = new CameraSensorFileTests.TemporaryFiles();
            var window = new CameraImagingWindow(app) { Width = 1080, Height = 820 };
            try
            {
                window.Show(); var controls = window.GetVisualDescendants().OfType<Control>().ToArray();
                ComboBox Choice(string name) => controls.OfType<ComboBox>().Single(c => c.Name == "camera-" + name);
                NumericUpDown Number(string name) => controls.OfType<NumericUpDown>().Single(c => c.Name == "camera-" + name);
                Choice("spectral-model").SelectedIndex = (int)CameraSpectralModel.Continuous;
                Assert.False(Number("irradiance").IsEnabled);
                Choice("spectral-model").SelectedIndex = (int)CameraSpectralModel.ThreeBand;
                Choice("source").SelectedIndex = (int)CameraSource.Scene3D;
                Assert.Equal(0, Choice("optics").SelectedIndex); Assert.Equal(0, Choice("aberration").SelectedIndex);
                Assert.True(Choice("aberration").IsEnabled); Assert.False(Number("irradiance").IsEnabled);
                Number("scene3d-depth").Value = null;
                Assert.False(controls.OfType<Button>().Single(c => c.Name == "camera-run").IsEnabled);
                Number("scene3d-depth").Value = 0;
                Choice("size").SelectedIndex = 4; Choice("pixel-sampling").SelectedIndex = 2;
                Choice("qe-area").SelectedIndex = 1; Number("fill-factor").Value = .25m;
                await window.RunAsync(); var a = Assert.IsType<CameraImagingResult>(window.Result); Assert.NotNull(a.Scene3D);
                window.PinComparison(); Number("scene3d-power").Value = .5m; Assert.Same(a, window.Comparison);
                await window.RunAsync(); var b = Assert.IsType<CameraImagingResult>(window.Result);
                Assert.Equal(a.MeanPhotoElectrons / 2, b.MeanPhotoElectrons, 9);
                var path = files.Path("scene3d.zip"); await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier, 1, b, a), path);
                Assert.False(await window.ImportScene3DFromPathAsync(files.Path("missing.json"))); Assert.Same(b, window.Result);
                app.Documents.NewBlank(); Assert.Null(window.Result);
                Assert.True(await window.OpenExperimentFromPathAsync(path)); await window.RecomputeExperimentAsync();
                Assert.Equal(b.RawDn, window.Result!.RawDn); Assert.Equal(a.RawDn, window.Comparison!.RawDn);
                var work = window.RunAsync(); Number("scene3d-power").Value = .25m; await work;
                Assert.Null(window.Result); Assert.NotNull(window.Comparison);
                Number("scene3d-power").Value = 1; await window.RunAsync(); Assert.NotNull(window.Result);
                var dir = Environment.GetEnvironmentVariable("OPTICAL_CAMERA_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Choice("source").BringIntoView(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    Directory.CreateDirectory(dir); using var image = window.CaptureRenderedFrame(); Assert.NotNull(image);
                    image.Save(Path.Combine(dir, $"scene3d-{theme}-{language}.png"), PngBitmapEncoderOptions.Default);
                }
                return true;
            }
            finally { window.Close(); UiLocalization.Apply(previous); }
        }, CancellationToken.None);
    }
}
