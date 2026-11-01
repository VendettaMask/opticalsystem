using Avalonia;
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
public sealed class CameraNonSequentialWindowTests
{
    [Theory]
    [InlineData("Light", "zh-CN")] [InlineData("Dark", "en-US")] [InlineData("Light", "ja-JP")]
    public async Task DesktopNscSelectionCaptureRawIspABAndFrozenReopenWorkOffscreen(string theme, string language)
    {
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(CameraImagingWindowTests));
        await host.Dispatch<bool>(async () =>
        {
            var previous = UiLocalization.CurrentLanguage; UiLocalization.Apply(language);
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create(); var setup = await CameraNonSequentialServiceTests.Setup(app, true);
            using var files = new CameraSensorFileTests.TemporaryFiles();
            var window = new CameraImagingWindow(app) { Width = 1200, Height = 860 };
            try
            {
                window.Show(); window.UpdateLayout();
                T Find<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == "camera-" + name);
                Find<ComboBox>("optics").SelectedIndex = (int)CameraOptics.DetectorInput;
                Assert.False(Find<Button>("run").IsEnabled);
                Find<ComboBox>("optics").SelectedIndex = (int)CameraOptics.Ideal;
                Find<ComboBox>("source").SelectedIndex = (int)CameraSource.NonSequentialDetector;
                Assert.Equal(UiLocalization.Text("读取 NSC 结果"), Find<Button>("nsc-read").Content);
                Assert.False(Find<Button>("run").IsEnabled); Assert.True(Find<Button>("nsc-read").IsEnabled);
                Assert.True(await window.CaptureNscAsync());
                Assert.False(Find<Button>("run").IsEnabled); // An imported absolute QE is required.
                Assert.True(await window.ImportResponseFromPathAsync(Path.Combine(CameraSensorFileTests.Examples, "example-qe.csv")));
                Find<CheckBox>("noise").IsChecked = false;
                Assert.True(Find<Button>("run").IsEnabled); Assert.False(Find<ComboBox>("size").IsEnabled);
                await window.RunAsync(); var a = Assert.IsType<CameraImagingResult>(window.Result);
                Assert.Equal(4, a.Request.Width); Assert.Equal(setup.Detector, a.Request.NscDetector!.Detector.Id);
                window.PinComparison(); Find<NumericUpDown>("exposure").Value = 20;
                await window.RunAsync(); var b = Assert.IsType<CameraImagingResult>(window.Result);
                for (var i = 0; i < 16; i++) Assert.Equal(a.PhotoElectrons[i] * 2, b.PhotoElectrons[i], 9);
                Find<TextBox>("nsc-filter").Text = "bad filter";
                Assert.False(await window.CaptureNscAsync()); Assert.Null(window.Result); Assert.Same(a, window.Comparison);
                Find<TextBox>("nsc-filter").Text = ""; Assert.True(await window.CaptureNscAsync()); await window.RunAsync();
                var current = window.Result!;
                var path = files.Path("nsc.camera-experiment.zip");
                await app.CameraImaging.SaveExperimentAsync(new(CameraExperiment.FormatIdentifier, 1, current, a), path);
                await app.NonSequentialAnalysis.ClearDetectorsAsync(); Assert.Null(window.Result);
                app.Documents.NewBlank();
                Assert.True(await window.OpenExperimentFromPathAsync(path)); await window.RecomputeExperimentAsync();
                Assert.Equal(current.RawDn, window.Result!.RawDn); Assert.Equal(a.RawDn, window.Comparison!.RawDn);
                await window.RunAsync(); Assert.Equal(current.PhotoElectrons, window.Result!.PhotoElectrons);
                var dir = Environment.GetEnvironmentVariable("OPTICAL_CAMERA_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Find<Control>("nsc-card").BringIntoView(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Directory.CreateDirectory(dir);
                    using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                    frame.Save(Path.Combine(dir, $"nsc-camera-{theme}-{language}.png"), PngBitmapEncoderOptions.Default);
                }
                return true;
            }
            finally { window.Close(); UiLocalization.Apply(previous); }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
    }
}
