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
public sealed class CommercialBsdfWindowTests
{
    [Fact]
    public async Task CommercialSideDeclarationMaterialAxisAndCameraChainWorkOffscreen()
    {
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(CameraImagingWindowTests));
        await host.Dispatch<bool>(async () =>
        {
            using var app = WorkbenchApplication.Create(); using var files = new CameraSensorFileTests.TemporaryFiles();
            var setup = await MeasuredBsdfChainTests.Setup(app, files); var path = files.Path("synthetic.anisotropicbsdf"); File.WriteAllText(path, CommercialBsdfTests.Speos(true));
            using var panel = new NonSequentialObjectEditorPanel(app.NonSequential, app.Events);
            var editor = new Window { Content = panel, Width = 1400, Height = 1050 }; CameraImagingWindow? camera = null;
            try
            {
                editor.Show(); editor.UpdateLayout(); var grid = editor.GetVisualDescendants().OfType<DataGrid>().Single();
                grid.SelectedItem = ((IEnumerable)grid.ItemsSource!).Cast<object>().Single(r => (Guid)r.GetType().GetProperty("Id")!.GetValue(r)! == setup.Plane);
                editor.UpdateLayout();
                T Find<T>(string name) where T : Control => editor.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
                Find<ComboBox>("nsc-bsdf-import-side").SelectedIndex = 2;
                Assert.True(await panel.ImportMeasuredBsdfFromPathAsync(path)); Dispatcher.UIThread.RunJobs(); editor.UpdateLayout();
                Assert.Contains("SpeosAnisotropicBsdfText8/1", Find<TextBlock>("nsc-bsdf-info").Text);
                Find<TextBox>("nsc-bsdf-axis-x").Text = "0"; Find<TextBox>("nsc-bsdf-axis-y").Text = "1"; Find<TextBox>("nsc-bsdf-axis-z").Text = "0";
                Find<Button>("nsc-apply").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); editor.UpdateLayout();
                Assert.Equal(new BsdfMaterialAxisDto(0, 1, 0), app.NonSequential.GetDocument().Objects.Single(o => o.Id == setup.Plane).SurfaceBsdf!.MaterialAxis);
                await app.NonSequentialAnalysis.TraceAsync(new(SplittingMode: NonSequentialSplittingMode.FullFresnel));
                camera = new CameraImagingWindow(app) { Width = 1200, Height = 1000 }; camera.Show(); camera.UpdateLayout();
                T Camera<T>(string name) where T : Control => camera.GetVisualDescendants().OfType<T>().Single(c => c.Name == "camera-" + name);
                Camera<ComboBox>("source").SelectedIndex = (int)CameraSource.NonSequentialDetector; Camera<ComboBox>("nsc-detector").SelectedIndex = 1;
                Camera<TextBox>("nsc-filter").Text = "B2 & D4"; Assert.True(await camera.CaptureNscAsync());
                Assert.True(await camera.ImportResponseFromPathAsync(Path.Combine(CameraSensorFileTests.Examples, "example-qe.csv")));
                Camera<CheckBox>("noise").IsChecked = false; await camera.RunAsync();
                var result = Assert.IsType<CameraImagingResult>(camera.Result); Assert.True(result.MeanPhotoElectrons > 0);
                Assert.Equal("MeasuredBsdfGrid/2", result.Request.NscDetector!.SurfaceBsdfProtocol);
                Assert.True(result.Request.NscDetector.BsdfStatistics!.MaterialAxisSampleCount > 0);
                var axis = Assert.Single(result.Request.NscDetector.SurfaceBsdfs!).WorldMaterialAxisX!;
                Assert.Equal(0, axis.X, 12); Assert.Equal(1, axis.Y, 12);
                Assert.Contains("material", Camera<TextBlock>("nsc-info").Text, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("Measured BSDF", Camera<TextBlock>("nsc-info").Text);
                Assert.Equal(2, Find<ComboBox>("nsc-bsdf-import-side").SelectedIndex);
                var dir = Environment.GetEnvironmentVariable("OPTICAL_COMMERCIAL_BSDF_EVIDENCE_DIR");
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir); editor.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using (var frame = editor.CaptureRenderedFrame()) { Assert.NotNull(frame); frame.Save(Path.Combine(dir, "commercial-bsdf-editor.png"), PngBitmapEncoderOptions.Default); }
                    Camera<Control>("nsc-card").BringIntoView(); camera.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var image = camera.CaptureRenderedFrame(); Assert.NotNull(image); image.Save(Path.Combine(dir, "commercial-bsdf-camera.png"), PngBitmapEncoderOptions.Default);
                }
                return true;
            }
            finally { camera?.Close(); editor.Close(); }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
    }
}
