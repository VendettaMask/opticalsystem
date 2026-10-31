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
public sealed class SpeosV11WindowTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)]
    public async Task V11AndXyzImportsReachTheCameraWithFrozenPhysicalInputsOffscreen(bool zemax, bool manual)
    {
        using var host = SafeHeadlessUnitTestSession.StartNew(typeof(CameraImagingWindowTests));
        await host.Dispatch<bool>(async () =>
        {
            using var app = WorkbenchApplication.Create(); using var files = new CameraSensorFileTests.TemporaryFiles(); var setup = await MeasuredBsdfChainTests.Setup(app, files);
            var path = files.Path(zemax ? "synthetic.BSDF" : "synthetic.anisotropicbsdf"); if (zemax) File.WriteAllText(path, SpeosV11XyzTests.XyzText(true)); else if (manual) File.WriteAllText(path, SpeosV11XyzTests.SpeosText(both: true)); else File.WriteAllBytes(path, SpeosV11BinaryTests.Binary(true, true));
            if (!zemax && !manual) app.NonSequential.UpdateObject(new(setup.Plane, true, true, NonSequentialObjectKind.PlaneRectangle, "V11 BRDF", null, null, 0, 0, 0, 0, 0, 0, new PlaneRectangleParameters(1, 1, NonSequentialSurfaceBehavior.Reflective, "AIR", "AIR")));
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
                    var calibration = files.Path("calibration.json"); File.WriteAllText(calibration, SpeosV11XyzTests.Calibration());
                    Find<TextBox>("nsc-bsdf-zemax-xyz").Text = calibration;
                    Find<TextBox>("nsc-bsdf-zemax-wave").Text = "unused XYZ wavelength";
                }
                if (manual) { Find<CheckBox>("nsc-bsdf-speos-white").IsChecked = true; Assert.False(await panel.ImportMeasuredBsdfFromPathAsync(path)); Assert.Null(app.NonSequential.GetDocument().Objects.Single(o => o.Id == setup.Plane).SurfaceBsdf); Find<CheckBox>("nsc-bsdf-speos-enhance").IsChecked = true; Find<TextBox>("nsc-bsdf-speos-angle").Text = "14"; Find<TextBox>("nsc-bsdf-speos-height").Text = "0.01"; }
                Assert.True(await panel.ImportMeasuredBsdfFromPathAsync(path)); Dispatcher.UIThread.RunJobs(); editor.UpdateLayout();
                Assert.Contains(zemax ? "ZemaxTabularCalibratedXYZ/1" : manual ? "SpeosAnisotropicBsdfText11/1" : "SpeosAnisotropicBsdfBinary11Archive3/1", Find<TextBlock>("nsc-bsdf-info").Text);
                if (!zemax) { Assert.Equal("unused declaration", Find<TextBox>("nsc-bsdf-zemax-wave").Text); Find<TextBox>("nsc-bsdf-zemax-wave").Text = "550"; }
                if (zemax) Assert.NotNull(app.NonSequential.GetDocument().Objects.Single(o => o.Id == setup.Plane).SurfaceBsdf!.MeasuredData!.ZemaxXyz);
                else Assert.NotNull(app.NonSequential.GetDocument().Objects.Single(o => o.Id == setup.Plane).SurfaceBsdf!.MeasuredData!.SpeosPeak);
                await app.NonSequentialAnalysis.TraceAsync(new(SplittingMode: NonSequentialSplittingMode.FullFresnel));
                camera = new CameraImagingWindow(app) { Width = 1200, Height = 1000 }; camera.Show(); camera.UpdateLayout();
                T Camera<T>(string name) where T : Control => camera.GetVisualDescendants().OfType<T>().Single(c => c.Name == "camera-" + name);
                Camera<ComboBox>("source").SelectedIndex = (int)CameraSource.NonSequentialDetector; Camera<ComboBox>("nsc-detector").SelectedIndex = zemax || manual ? 1 : 0; Camera<TextBox>("nsc-filter").Text = zemax || manual ? "B2 & D4" : "S2 & R2 & D3";
                Assert.True(await camera.CaptureNscAsync()); Assert.True(await camera.ImportResponseFromPathAsync(Path.Combine(CameraSensorFileTests.Examples, "example-qe.csv")));
                Camera<CheckBox>("noise").IsChecked = false; await camera.RunAsync(); var run = Assert.IsType<CameraImagingResult>(camera.Result);
                Assert.Equal("MeasuredBsdfGrid/4", run.Request.NscDetector!.SurfaceBsdfProtocol); Assert.True(run.MeanPhotoElectrons > 0); Assert.Contains("Measured BSDF", Camera<TextBlock>("nsc-info").Text);
                var dir = Environment.GetEnvironmentVariable("OPTICAL_SPEOS_V11_XYZ_EVIDENCE_DIR");
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir); Find<TextBlock>("nsc-bsdf-info").BringIntoView(); editor.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using (var frame = editor.CaptureRenderedFrame()) { Assert.NotNull(frame); frame.Save(Path.Combine(dir, "editor-" + zemax + "-" + manual + ".png"), PngBitmapEncoderOptions.Default); }
                    Camera<Control>("nsc-card").BringIntoView(); camera.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var image = camera.CaptureRenderedFrame(); Assert.NotNull(image); image.Save(Path.Combine(dir, "camera-" + zemax + "-" + manual + ".png"), PngBitmapEncoderOptions.Default);
                }
                return true;
            }
            finally { camera?.Close(); editor.Close(); }
        }, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
    }
}

