using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.ZemaxComparison.Workbench;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Backend;
using Xunit.Abstractions;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class AfocalPupilConditioningTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(.001, 0)]
    [InlineData(.0001, 0)]
    [InlineData(.00001, 0)]
    [InlineData(.001, .7)]
    [InlineData(.0001, .7)]
    [InlineData(.00001, .7)]
    public void NarrowIntersectingPencilsRecoverKnownConjugate(double angle, double rotation)
    {
        Vector3D Rotate(Vector3D p) => new(Math.Cos(rotation) * p.X + Math.Sin(rotation) * p.Z,
            p.Y, -Math.Sin(rotation) * p.X + Math.Cos(rotation) * p.Z);
        var target = Rotate(new(3, -2, 7));
        var directions = new[] { new Vector3D(0, 0, 1), new(angle, 0, 1), new(-angle, 0, 1), new(0, angle, 1), new(0, -angle, 1) };
        var lines = directions.Select(d =>
        {
            var unit = d / d.Length;
            return (Rotate(new Vector3D(3, -2, 7) + unit * 100), Rotate(unit));
        }).ToArray();
        var method = typeof(ExitPupilGeometryEngine).GetMethod("FitLines", BindingFlags.NonPublic | BindingFlags.Static)!;
        var fit = ((Vector3D Point, double Residual))method.Invoke(null, [lines])!;
        var error = (fit.Point - target).Length;
        output.WriteLine($"angle={angle:R}; rotation={rotation:R}; conjugateErrorMm={error:R}; residualMm={fit.Residual:R}");
        // 0.1 nm over a 100 mm path. This is a synthetic algebraic regression,
        // not a replacement optical comparison gate.
        Assert.InRange(error, 0, 1e-7);
    }
    [Fact]
    public async Task RecordFirstSurfaceWithProbeDependentResponse()
    {
        var optic = (await WorkbenchExecutor.Import(Path.Combine(AppContext.BaseDirectory,
            "Fixtures/afocal-repair/sources/Afocal/Binocular_System.ZMX"))).Configurations[0];
        var wave = optic.Wavelengths.First(w => w.IsPrimary);
        foreach (var h in new[] { .01, .001, .0001 })
            for (var i = 1; i < optic.SurfaceGroup.Items.Count; i++)
            {
                var a = optic.SequentialRayTracer.TraceGenericSurfaceSample(0, 0, -h, 0, wave.Micrometers, i)!;
                var b = optic.SequentialRayTracer.TraceGenericSurfaceSample(0, 0, h, 0, wave.Micrometers, i)!;
                Assert.NotNull(a); Assert.NotNull(b);
                Assert.False(a.Vignetted); Assert.False(b.Vignetted);
                var dp = (b.Position - a.Position) / (2 * h);
                var dd = (b.Direction - a.Direction) / (2 * h);
                output.WriteLine(JsonSerializer.Serialize(new
                {
                    kind = "surface-derivative",
                    h,
                    surface = i,
                    geometry = optic.SurfaceGroup.Items[i].Geometry.Kind,
                    positionDerivative = new[] { dp.X, dp.Y, dp.Z },
                    directionDerivative = new[] { dd.X, dd.Y, dd.Z }
                }));
            }
    }

}
