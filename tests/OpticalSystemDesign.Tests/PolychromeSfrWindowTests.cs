using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Laboratories;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class PolychromeSfrWindowTests
{
    public static AppBuilder BuildAvaloniaApp() => CameraSfrWindowTests.BuildAvaloniaApp();

    [Theory]
    [InlineData("Light")] [InlineData("Dark")]
    public async Task LensSpectrumModeDisplaysFrozenWeightsExportsCompletePlotAndClearsOnModeChange(string theme)
    {
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(PolychromeSfrWindowTests));
        await session.Dispatch<bool>(async () =>
        {
            ThemeApplicationService.Apply(Avalonia.Application.Current!, theme);
            using var app = WorkbenchApplication.Create(); await app.Documents.OpenAsync(DoubleGaussSfrTests.StandardLens);
            var window = new CameraImagingWindow(app);
            try
            {
                window.Show();
                var tabs = window.GetVisualDescendants().OfType<TabControl>().Single(c => c.Name == "CameraResults");
                tabs.SelectedItem = ((IEnumerable<object>)tabs.ItemsSource!).OfType<TabItem>().Single(t => t.Name == "camera-sfr-tab");
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var mode = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "camera-sfr-spectrum-mode");
                var wavelength = window.GetVisualDescendants().OfType<NumericUpDown>().Single(c => c.Name == "camera-sfr-wavelength");
                Assert.Equal(1, mode.SelectedIndex); Assert.False(wavelength.IsEnabled);
                if (theme == "Light")
                {
                    mode.SelectedIndex = 0; wavelength.Text = "invalid";
                    Assert.False(window.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "camera-sfr-run").IsEnabled);
                    mode.SelectedIndex = 1;
                    Assert.True(window.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "camera-sfr-run").IsEnabled);
                    await window.RunSfrAsync(); Assert.True(window.SfrResult?.SamplingConverged);
                }
                else window.PresentSfr(await app.CameraImaging.SimulateSlantedEdgeAsync(CameraSfrRequest.FromLensSpectrum() with { FieldNumber = 3, CheckSampling = false }));
                var result = window.SfrResult!;
                Assert.Equal(CameraSfrResult.PolychromeAlgorithm, result.Algorithm); Assert.True(result.Request.UseLensWavelengths);
                Assert.Equal(app.Prescription.GetWavelengths().Select(w => w.Weight), result.Baseline.Spectrum!.Select(b => b.Weight));
                var info = window.GetVisualDescendants().OfType<TextBlock>().Single(c => c.Name == "camera-sfr-info").Text;
                Assert.Contains("镜头多波长", info); Assert.Contains("587.6", info); Assert.Contains("完整瞳孔对照：通过", info);
                Assert.DoesNotContain("099999999", info);
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var graph = window.GetVisualDescendants().OfType<ContentControl>().Single(c => c.Name == "camera-sfr-graph");
                Assert.Equal(5, graph.GetVisualDescendants().OfType<CheckBox>().Count());
                using var bitmap = window.CaptureSfrImage(); Assert.Equal(new PixelSize(2404, 1804), bitmap.PixelSize);
                var dir = Environment.GetEnvironmentVariable("OPTICAL_POLY_SFR_EVIDENCE_DIR");
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir); bitmap.Save(Path.Combine(dir, $"polychrome-{theme}.png"), PngBitmapEncoderOptions.Default);
                    Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var page = window.CaptureRenderedFrame(); page!.Save(Path.Combine(dir, $"polychrome-page-{theme}.png"), PngBitmapEncoderOptions.Default);
                }
                var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".sfr-experiment.zip");
                try
                {
                    await app.CameraImaging.ExportSlantedEdgeAsync(result, path);
                    if (theme == "Light" && !string.IsNullOrWhiteSpace(dir))
                        File.Copy(path, Path.Combine(dir, "double-gauss-3wave.sfr-experiment.zip"), true);
                    mode.SelectedIndex = 0; Assert.Null(window.SfrResult);
                    Assert.True(await window.OpenSfrFromPathAsync(path)); Assert.Equal(1, mode.SelectedIndex); Assert.False(wavelength.IsEnabled);
                    Assert.Equal(result.Baseline.Spectrum, window.SfrResult!.Baseline.Spectrum);
                    app.Prescription.UpdateWavelength(app.Prescription.GetWavelengths()[0] with { Weight = 4 });
                    Assert.Equal(result.Baseline.Spectrum, window.SfrResult!.Baseline.Spectrum); // Explicitly opened record stays frozen.
                    mode.SelectedIndex = 0; Assert.Null(window.SfrResult); Assert.Null(graph.Content);
                }
                finally { File.Delete(path); }
                return true;
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
}
