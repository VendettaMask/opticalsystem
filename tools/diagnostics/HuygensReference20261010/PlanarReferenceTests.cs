using System.Numerics;
using System.Text.Json;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Raytrace;
using Xunit;
using Xunit.Abstractions;

// Independent physical reference, tests only. Never supplies product results.
public sealed class PlanarReferenceTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("endpoints", false, false)]
    [InlineData("centers", true, false)]
    [InlineData("zemax", false, true)]
    [InlineData("halfopen", false, false)]
    [InlineData("closed32intervals", false, false)]
    public void MeasureNativePlaneWaveReference(string nodes, bool cellCentered, bool zemaxCentered)
    {
        var root = Environment.GetEnvironmentVariable("OPTICAL_REPOSITORY_ROOT")
            ?? throw new InvalidOperationException("Set OPTICAL_REPOSITORY_ROOT.");
        var source = Path.Combine(root, "validation/zemax/2026-r1/huygens-method-controls-2026-10-09/grid/source.ZMX");
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(source), ".zmx");
        optic.RayAimingEnabled = true;
        var wave = optic.Wavelengths[0];
        var wf = WavefrontEngine.GenerateChiefRayUniform(optic, (0, 0), wave, 32,
            cellCentered: cellCentered, aimAtStop: true, zemaxCentered: zemaxCentered);
        var pupils = nodes is "halfopen" or "closed32intervals"
            ? Enumerable.Range(0, nodes == "halfopen" ? 32 : 33).SelectMany(y => Enumerable.Range(0, nodes == "halfopen" ? 32 : 33).Select(x =>
                new PupilSample((x - 16) / 16d, (y - 16) / 16d, 1)))
                .Where(p => p.X * p.X + p.Y * p.Y <= 1).ToArray()
            : wf.Samples.Select(s => new PupilSample(s.NormalizedPupilX, s.NormalizedPupilY, 1)).ToArray();
        var rays = optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator
            .GenerateNormalizedPupilSamples(0, 0, wave.Micrometers, pupils, true));
        var chief = optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator
            .GenerateGeneric(0, 0, 0, 0, wave.Micrometers, true)).Single()!;
        var frame = DiffractionEngine.CreateHuygensImageFrame(optic, (0, 0), wave, true);
        var k = 2 * Math.PI / (wave.Micrometers * 1e-3);
        var idealAmplitude = rays.Where(r => r is not null).Sum(r => Math.Sqrt(Math.Max(0, r!.Intensity)));
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "validation/zemax/2026-r1/huygens-method-controls-2026-10-09/grid/Auto/data.json")));
        var grid = native.RootElement.GetProperty("dataGrids")[0];
        var dx = grid.GetProperty("dx").GetDouble(); var dy = grid.GetProperty("dy").GetDouble();
        var minX = grid.GetProperty("minX").GetDouble(); var minY = grid.GetProperty("minY").GetDouble();
        var values = new double[32][];
        var squared = 0d; var referenceSquared = 0d; var maximum = 0d;
        for (var row = 0; row < 32; row++)
        {
            values[row] = new double[32];
            for (var column = 0; column < 32; column++)
            {
                var point = frame.Center + frame.TangentX * ((minX + column * dx) / 1000)
                    + frame.TangentY * ((minY + row * dy) / 1000);
                var amplitude = Complex.Zero;
                foreach (var ray in rays)
                {
                    if (ray is null || ray.Intensity <= 0) continue;
                    var offset = point - ray.Position;
                    var geometric = ray.Direction.X * offset.X + ray.Direction.Y * offset.Y + ray.Direction.Z * offset.Z;
                    var path = ray.CumulativeOpticalPathLength - chief.CumulativeOpticalPathLength
                        + wf.ImageRefractiveIndex * geometric;
                    amplitude += Complex.FromPolarCoordinates(Math.Sqrt(ray.Intensity), k * path);
                }
                var actual = amplitude.Magnitude * amplitude.Magnitude / (idealAmplitude * idealAmplitude);
                var expected = grid.GetProperty("values")[row][column].GetDouble();
                values[row][column] = actual;
                squared += Math.Pow(actual - expected, 2); referenceSquared += expected * expected;
                maximum = Math.Max(maximum, Math.Abs(actual - expected));
            }
        }
        var directory = Environment.GetEnvironmentVariable("OPTICAL_REFERENCE_OUTPUT")
            ?? throw new InvalidOperationException("Set OPTICAL_REFERENCE_OUTPUT.");
        Directory.CreateDirectory(directory);
        var report = new { nodes, pupilCount = pupils.Length, illuminated = rays.Count(r => r is not null),
            relativeL2 = Math.Sqrt(squared / referenceSquared), maximumAbsolute = maximum, values,
            semantics = "Independent test-only plane-wave reference from full formal final ray XYZ/direction/optical path, uniform amplitude weights; native coordinates and no fitted normalization." };
        File.WriteAllText(Path.Combine(directory, nodes + ".json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        output.WriteLine($"{nodes}: L2={report.relativeL2:R}, max={maximum:R}, points={pupils.Length}");
        Assert.True(double.IsFinite(report.relativeL2));
    }
}
