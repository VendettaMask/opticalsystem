using System.Collections;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Laboratories;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class BinaryZemaxBsdfWindowTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task BinaryAndZemaxImportsReachTheCameraWithExplicitUnitsOffscreen(bool zemax)
    {
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(CameraImagingWindowTests));
        await host.Dispatch<bool>(async () =>
        {
            using var app = WorkbenchApplication.Create(); using var files = new CameraSensorFileTests.TemporaryFiles(); var setup = await MeasuredBsdfChainTests.Setup(app, files);
            var path = files.Path(zemax ? "synthetic.BSDF" : "synthetic.brdf"); if (zemax) File.WriteAllText(path, BinaryZemaxBsdfTests.Zemax(true)); else File.WriteAllBytes(path, BinaryZemaxBsdfTests.BinarySpeos());
            using var panel = new NonSequentialObjectEditorPanel(app.NonSequential, app.Events); var editor = new Window { Content = panel, Width = 1400, Height = 1100 }; CameraImagingWindow? camera = null;
            try
            {
                editor.Show(); editor.UpdateLayout(); var grid = editor.GetVisualDescendants().OfType<DataGrid>().Single();
                grid.SelectedItem = ((IEnumerable)grid.ItemsSource!).Cast<object>().Single(r => (Guid)r.GetType().GetProperty("Id")!.GetValue(r)! == setup.Plane); editor.UpdateLayout();
                T Find<T>(string name) where T : Control => editor.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
                Find<ComboBox>("nsc-bsdf-import-side").SelectedIndex = 2;
                if (!zemax) Find<TextBox>("nsc-bsdf-zemax-wave").Text = "unused declaration";
                if (zemax)
                {
                    Assert.False(await panel.ImportMeasuredBsdfFromPathAsync(path)); Assert.Null(app.NonSequential.GetDocument().Objects.Single(o => o.Id == setup.Plane).SurfaceBsdf);
                    Find<CheckBox>("nsc-bsdf-zemax-radiometric").IsChecked = true; Find<TextBox>("nsc-bsdf-zemax-wave").Text = "550";
                }
                Assert.True(await panel.ImportMeasuredBsdfFromPathAsync(path)); Dispatcher.UIThread.RunJobs(); editor.UpdateLayout();
                Assert.Contains(zemax ? "ZemaxTabularRadiometric/1" : "SpeosBrdfBinary8Archive3/1", Find<TextBlock>("nsc-bsdf-info").Text);
                if (!zemax) { Assert.Equal("unused declaration", Find<TextBox>("nsc-bsdf-zemax-wave").Text); Find<TextBox>("nsc-bsdf-zemax-wave").Text = "550"; }
                await app.NonSequentialAnalysis.TraceAsync(new(SplittingMode: NonSequentialSplittingMode.FullFresnel));
                camera = new CameraImagingWindow(app) { Width = 1200, Height = 1000 }; camera.Show(); camera.UpdateLayout();
                T Camera<T>(string name) where T : Control => camera.GetVisualDescendants().OfType<T>().Single(c => c.Name == "camera-" + name);
                Camera<ComboBox>("source").SelectedIndex = (int)CameraSource.NonSequentialDetector; Camera<ComboBox>("nsc-detector").SelectedIndex = 1; Camera<TextBox>("nsc-filter").Text = "B2 & D4";
                Assert.True(await camera.CaptureNscAsync()); Assert.True(await camera.ImportResponseFromPathAsync(Path.Combine(CameraSensorFileTests.Examples, "example-qe.csv")));
                Camera<CheckBox>("noise").IsChecked = false; await camera.RunAsync(); var run = Assert.IsType<CameraImagingResult>(camera.Result);
                Assert.Equal("MeasuredBsdfGrid/3", run.Request.NscDetector!.SurfaceBsdfProtocol); Assert.True(run.MeanPhotoElectrons > 0); Assert.Contains("Measured BSDF", Camera<TextBlock>("nsc-info").Text);
                var dir = Environment.GetEnvironmentVariable("OPTICAL_BINARY_ZEMAX_BSDF_EVIDENCE_DIR");
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir); Find<TextBlock>("nsc-bsdf-info").BringIntoView(); editor.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using (var frame = editor.CaptureRenderedFrame()) { Assert.NotNull(frame); frame.Save(Path.Combine(dir, "editor-" + zemax + ".png"), PngBitmapEncoderOptions.Default); }
                    Camera<Control>("nsc-card").BringIntoView(); camera.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var image = camera.CaptureRenderedFrame(); Assert.NotNull(image); image.Save(Path.Combine(dir, "camera-" + zemax + ".png"), PngBitmapEncoderOptions.Default);
                }
                return true;
            }
            finally { camera?.Close(); editor.Close(); }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
    }
}
