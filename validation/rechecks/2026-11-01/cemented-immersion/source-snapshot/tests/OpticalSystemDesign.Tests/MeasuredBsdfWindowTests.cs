using System.Collections;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Laboratories;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;

namespace OpticalSystemDesign.Tests;

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class MeasuredBsdfWindowTests
{
    [Fact]
    public async Task ImportEditAndClearMeasuredDataAndRunBtdfRawIspOffscreen()
    {
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(CameraImagingWindowTests));
        await host.Dispatch<bool>(async () =>
        {
            using var app = WorkbenchApplication.Create(); using var files = new CameraSensorFileTests.TemporaryFiles();
            var setup = await MeasuredBsdfChainTests.Setup(app, files);
            using var panel = new NonSequentialObjectEditorPanel(app.NonSequential, app.Events);
            var editor = new Window { Content = panel, Width = 1400, Height = 950 }; CameraImagingWindow? camera = null;
            try
            {
                editor.Show(); editor.UpdateLayout(); var grid = editor.GetVisualDescendants().OfType<DataGrid>().Single();
                void SelectPlane() => grid.SelectedItem = ((IEnumerable)grid.ItemsSource!).Cast<object>()
                    .Single(r => (Guid)r.GetType().GetProperty("Id")!.GetValue(r)! == setup.Plane);
                SelectPlane(); editor.UpdateLayout(); Assert.True(await panel.ImportMeasuredBsdfFromPathAsync(setup.Path));
                Dispatcher.UIThread.RunJobs(); editor.UpdateLayout();
                T Find<T>(string name) where T : Control => editor.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
                Assert.Contains("control.bsdf.csv", Find<TextBlock>("nsc-bsdf-info").Text);
                Assert.False(Find<TextBox>("nsc-diffuse-reflectance").IsEnabled);
                Find<CheckBox>("nsc-lambertian").IsChecked = true;
                Assert.True(Find<TextBox>("nsc-diffuse-reflectance").IsEnabled);
                Find<CheckBox>("nsc-lambertian").IsChecked = false;
                Assert.False(Find<TextBox>("nsc-diffuse-reflectance").IsEnabled);
                Find<Button>("nsc-apply").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
                Assert.NotNull(app.NonSequential.GetDocument().Objects.Single(o => o.Id == setup.Plane).SurfaceBsdf!.MeasuredData);
                var bad = files.Path("bad.csv"); File.WriteAllText(bad, "unknown format");
                Assert.False(await panel.ImportMeasuredBsdfFromPathAsync(bad));
                await app.NonSequentialAnalysis.TraceAsync(new(SplittingMode: NonSequentialSplittingMode.FullFresnel));
                camera = new CameraImagingWindow(app) { Width = 1200, Height = 900 }; camera.Show(); camera.UpdateLayout();
                T Camera<T>(string name) where T : Control => camera.GetVisualDescendants().OfType<T>().Single(c => c.Name == "camera-" + name);
                Camera<ComboBox>("source").SelectedIndex = (int)CameraSource.NonSequentialDetector;
                Camera<ComboBox>("nsc-detector").SelectedIndex = 1;
                Camera<TextBox>("nsc-filter").Text = "B2 & D4"; Assert.True(await camera.CaptureNscAsync());
                Assert.True(await camera.ImportResponseFromPathAsync(Path.Combine(CameraSensorFileTests.Examples, "example-qe.csv")));
                Camera<CheckBox>("noise").IsChecked = false; await camera.RunAsync();
                var result = Assert.IsType<CameraImagingResult>(camera.Result);
                Assert.Equal("MeasuredBsdfGrid/1", result.Request.NscDetector!.SurfaceBsdfProtocol); Assert.True(result.MeanPhotoElectrons > 0);
                Assert.Contains("Measured BSDF", Camera<TextBlock>("nsc-info").Text); Assert.Contains("control.bsdf.csv", Camera<TextBlock>("nsc-info").Text);
                var dir = Environment.GetEnvironmentVariable("OPTICAL_MEASURED_BSDF_EVIDENCE_DIR");
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir); editor.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using (var frame = editor.CaptureRenderedFrame()) { Assert.NotNull(frame); frame.Save(Path.Combine(dir, "measured-bsdf-editor.png"), PngBitmapEncoderOptions.Default); }
                    Camera<Control>("nsc-card").BringIntoView(); camera.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var image = camera.CaptureRenderedFrame(); Assert.NotNull(image); image.Save(Path.Combine(dir, "measured-btdf-camera.png"), PngBitmapEncoderOptions.Default);
                }
                SelectPlane(); editor.UpdateLayout(); Find<Button>("nsc-bsdf-clear").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Null(app.NonSequential.GetDocument().Objects.Single(o => o.Id == setup.Plane).SurfaceBsdf);
                return true;
            }
            finally { camera?.Close(); editor.Close(); }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
    }
}
