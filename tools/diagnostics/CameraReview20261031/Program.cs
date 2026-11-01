using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Laboratories;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis.Imaging;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.NonSequential;
using OpticalSystemDesign.Tests;

var rows = new List<object>();
using (var session = SafeHeadlessUnitTestSession.StartNew(typeof(AuditUi)))
    await session.Dispatch(async () =>
    {
        foreach (var test in new[] { ("scene3d-depth", false), ("scene3d-guard", true), ("scene3d-psf-radius", true) })
        {
            using var app = WorkbenchApplication.Create("cooke");
            var window = new CameraImagingWindow(app);
            try
            {
                window.Show(); window.UpdateLayout();
                T Find<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
                Find<ComboBox>("camera-source").SelectedIndex = 5;
                if (test.Item2) Find<ComboBox>("camera-aberration").SelectedIndex = 1;
                var number = Find<NumericUpDown>("camera-" + test.Item1);
                try
                {
                    number.Text = "not-a-number";
                    window.UpdateLayout();
                    rows.Add(new { Case = "invalid-scene-number", Control = test.Item1, EscapedException = false,
                        RunEnabled = Find<Button>("camera-run").IsEnabled });
                }
                catch (Exception e)
                { rows.Add(new { Case = "invalid-scene-number", Control = test.Item1, EscapedException = true, Error = e.ToString() }); }
            }
            finally { window.Close(); }
        }
        await Task.CompletedTask;
        return true;
    }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(45));

var lens = Optic.CreateBlank("Image geometry audit");
lens.SurfaceGroup.Replace([
    new OpticalSurface { Thickness = double.PositiveInfinity },
    new OpticalSurface { Thickness = 10, SemiDiameter = 2, IsStop = true },
    new OpticalSurface { Geometry = new PlaneGeometry() }
]);
var rectangle = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.PlaneRectangle) with
{
    LocalCoordinateSystem = new(new(0, 0, -100)),
    Parameters = new PlaneRectangleParameters(100, 100)
};
var scene = new DirectCameraScene(new NonSequentialDocument("emitter", [new("green", 550, 1, true)], [rectangle]),
    [new DirectSceneMaterial(rectangle.Id, [0], EmittedRadiance: [1])], []);
var detector = new DirectCameraRenderSettings(4, 4, 100, new(2), 4, 16, false);
var flat = DirectCameraRenderer.Render(lens, scene, detector);
lens.SurfaceGroup.Items[^1].Geometry = new StandardGeometry(1);
var curved = DirectCameraRenderer.Render(lens, scene, detector);
rows.Add(new { Case = "curved-image-surface", Accepted = true,
    EdgeSagMillimeters = lens.SurfaceGroup.Items[^1].Geometry.Sag(.15, .15),
    OutputIdentical = flat.DetectorPower.Bands[0].PixelBandPowerWatts.SequenceEqual(curved.DetectorPower.Bands[0].PixelBandPowerWatts),
    FlatTotalWatts = flat.DetectorPower.TotalPowerWatts, CurvedTotalWatts = curved.DetectorPower.TotalPowerWatts });

lens.SurfaceGroup.Items[1].InteractionModel = new ThinLensInteractionModel(50);
lens.SurfaceGroup.Items[1].SemiDiameter = .5;
lens.SurfaceGroup.Items[1].Thickness = 100;
lens.SurfaceGroup.Renumber();
lens.SurfaceGroup.Items[^1].Geometry = new PlaneGeometry();
var flatWave = DiffractionCameraRenderer.Render(lens, scene, detector, new(4, 1, 1, 2));
lens.SurfaceGroup.Items[^1].Geometry = new StandardGeometry(1);
var curvedWave = DiffractionCameraRenderer.Render(lens, scene, detector, new(4, 1, 1, 2));
rows.Add(new { Case = "curved-image-surface-diffraction", Accepted = true,
    OutputIdentical = flatWave.Image.DetectorPower.Bands[0].PixelBandPowerWatts.SequenceEqual(curvedWave.Image.DetectorPower.Bands[0].PixelBandPowerWatts),
    FlatTotalWatts = flatWave.Image.DetectorPower.TotalPowerWatts, CurvedTotalWatts = curvedWave.Image.DetectorPower.TotalPowerWatts });

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
var json = JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });
await File.WriteAllTextAsync(Path.Combine(output, "reproductions.json"), json); Console.WriteLine(json);

public sealed class AuditUi
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<global::OpticalSystemDesign.App.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseHarfBuzz();
}
