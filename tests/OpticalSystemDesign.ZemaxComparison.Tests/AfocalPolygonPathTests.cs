using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.ZemaxComparison.Workbench;
using Xunit.Abstractions;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class AfocalPolygonPathTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ActualPolygonPathsAreBoundedAndObservationDoesNotChangeRays(int configuration)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures/afocal-repair/sources/Afocal/Binocular_System.ZMX");
        var optic = (await WorkbenchExecutor.Import(source)).Configurations[configuration - 1];
        var wave = optic.Wavelengths.First(w => w.IsPrimary);
        var geometry = Assert.IsType<PolygonGroupGeometry>(optic.SurfaceGroup.Items[6].Geometry);
        var observer = typeof(PolygonGroupGeometry).GetProperty("DiagnosticObserver", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.Null(observer.GetValue(null));
        output.WriteLine(JsonSerializer.Serialize(new
        {
            kind = "faces",
            configuration,
            faces = geometry.Faces.Select((f, i) => new
            {
                faceId = i,
                f.Absorbing,
                f.Reflective,
                vertices = f.Vertices.Select(v => new[] { v.X, v.Y, v.Z }).ToArray()
            }).ToArray()
        }));
        var pupils = new List<(double X, double Y)> { (0, 0) };
        foreach (var h in new[] { .01, .003, .001, .0003, .0001 })
            pupils.AddRange(new[] { (-h, 0d), (h, 0d), (0d, -h), (0d, h) });
        foreach (var pupil in pupils)
        {
            var expected = optic.SequentialRayTracer.TraceGenericSurfaceSample(0, 0, pupil.X, pupil.Y, wave.Micrometers, 6)!;
            var events = new List<JsonElement>();
            try
            {
                observer.SetValue(null, (Action<object>)(e => events.Add(JsonSerializer.SerializeToElement(e))));
                var actual = optic.SequentialRayTracer.TraceGenericSurfaceSample(0, 0, pupil.X, pupil.Y, wave.Micrometers, 6)!;
                Assert.Equal(expected, actual);
                Assert.False(actual.Vignetted);
                Assert.InRange(events.Count, 1, 128);
                Assert.Equal("port", events[^1].GetProperty("kind").GetString());
            }
            finally { observer.SetValue(null, null); }
            output.WriteLine(JsonSerializer.Serialize(new { kind = "path", configuration, px = pupil.X, py = pupil.Y, events }));
        }
        Assert.Null(observer.GetValue(null));
    }
}
