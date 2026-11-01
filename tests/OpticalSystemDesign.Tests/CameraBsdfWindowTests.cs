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
public sealed class CameraBsdfWindowTests
{
    [Fact]
    public async Task ObjectEditorRejectsInvalidAlbedoAndPublishesScatteringToCameraOffscreen()
    {
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(CameraImagingWindowTests));
        await host.Dispatch<bool>(async () =>
        {
            using var app = WorkbenchApplication.Create(); await CameraBsdfChainTests.Setup(app);
            using var panel = new NonSequentialObjectEditorPanel(app.NonSequential, app.Events);
            var editor = new Window { Content = panel, Width = 1400, Height = 900 };
            CameraImagingWindow? camera = null;
            try
            {
                editor.Show(); editor.UpdateLayout();
                var surface = app.NonSequential.GetDocument().Objects.Single(o => o.Kind == NonSequentialObjectKind.PlaneRectangle);
                var grid = editor.GetVisualDescendants().OfType<DataGrid>().Single();
                grid.SelectedItem = ((IEnumerable)grid.ItemsSource!).Cast<object>().Single(r => (Guid)r.GetType().GetProperty("Id")!.GetValue(r)! == surface.Id);
                editor.UpdateLayout();
                T Find<T>(string name) where T : Control => editor.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
                Assert.True(Find<CheckBox>("nsc-lambertian").IsChecked);
                Find<TextBox>("nsc-diffuse-reflectance").Text = "1.2";
                Find<Button>("nsc-apply").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(.5, app.NonSequential.GetDocument().Objects.Single(o => o.Id == surface.Id).SurfaceBsdf!.Reflectance);
                Find<TextBox>("nsc-diffuse-reflectance").Text = "0.6";
                Find<Button>("nsc-apply").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs(); editor.UpdateLayout();
                Assert.Equal(.6, app.NonSequential.GetDocument().Objects.Single(o => o.Id == surface.Id).SurfaceBsdf!.Reflectance);
                var dir = Environment.GetEnvironmentVariable("OPTICAL_BSDF_EVIDENCE_DIR");
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var frame = editor.CaptureRenderedFrame(); Assert.NotNull(frame);
                    frame.Save(Path.Combine(dir, "lambertian-editor.png"), PngBitmapEncoderOptions.Default);
                }
                await app.NonSequentialAnalysis.TraceAsync(new(SplittingMode: NonSequentialSplittingMode.FullFresnel));
                camera = new CameraImagingWindow(app) { Width = 1200, Height = 860 }; camera.Show(); camera.UpdateLayout();
                T Camera<T>(string name) where T : Control => camera.GetVisualDescendants().OfType<T>().Single(c => c.Name == "camera-" + name);
                Camera<ComboBox>("source").SelectedIndex = (int)CameraSource.NonSequentialDetector;
                Camera<TextBox>("nsc-filter").Text = "S2 & D3";
                Assert.True(await camera.CaptureNscAsync());
                Assert.True(await camera.ImportResponseFromPathAsync(Path.Combine(CameraSensorFileTests.Examples, "example-qe.csv")));
                Camera<CheckBox>("noise").IsChecked = false; await camera.RunAsync();
                var result = Assert.IsType<CameraImagingResult>(camera.Result);
                Assert.Equal(.6, Assert.Single(result.Request.NscDetector!.SurfaceBsdfs!).Model.Reflectance);
                Assert.True(result.MeanPhotoElectrons > 0); Assert.Contains(result.RawDn, v => v > 0);
                Assert.Contains("Lambertian", Camera<TextBlock>("nsc-info").Text);
                Assert.Contains("cosθ", Camera<TextBlock>("nsc-info").Text);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Camera<Control>("nsc-card").BringIntoView(); camera.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick(); using var frame = camera.CaptureRenderedFrame(); Assert.NotNull(frame);
                    frame.Save(Path.Combine(dir, "lambertian-camera.png"), PngBitmapEncoderOptions.Default);
                }
                var copy = app.NonSequential.PasteObject(new(surface.Id, true, true, surface.Kind, "copy", null, null,
                    0, 0, 0, 0, 0, 0, surface.Parameters, new(NonSequentialBsdfKind.LambertianReflection, .6)), 2);
                Assert.Equal(.6, app.NonSequential.GetDocument().Objects.Single(o => o.Id == copy).SurfaceBsdf!.Reflectance);
                return true;
            }
            finally { camera?.Close(); editor.Close(); }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
    }
}
