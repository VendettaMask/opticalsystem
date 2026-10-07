using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.Tests;

public sealed class GaussianPupilParityTests
{
    private static string Root => Path.Combine(AppContext.BaseDirectory, "Fixtures", "tessar-ray-audit");
    private static Optic Load(string name) => Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(
        File.ReadAllText(Path.Combine(Root, name, "snapshot.json")), new JsonSerializerOptions
        { PropertyNameCaseInsensitive = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals })!);

    [Theory]
    [InlineData("tessar-gq6-remove")]
    [InlineData("tessar-gq6-retain")]
    [InlineData("tessar-gq6-retain-orientations")]
    [InlineData("cooke-40-degree-field-gq6-remove")]
    [InlineData("double-gauss-28-degree-field-gq6-remove")]
    [InlineData("relay-lens-gq6-remove")]
    [InlineData("even-asphere-gq6-remove")]
    public void EverySurfaceAndRayMatchesActualNativeBatchWithSeparateApertureFlags(string name)
    {
        var optic = Load(name);
        using var job = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, name, "job.json")));
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, name, "native-ray-audit.json")));
        using var inputs = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, name, "inputs.json")));
        using var environment = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, name, "native-environment.json")));
        Assert.True(environment.RootElement.GetProperty("validLicense").GetBoolean());
        Assert.Equal(26, environment.RootElement.GetProperty("major").GetInt32());
        Assert.Equal(1, environment.RootElement.GetProperty("minor").GetInt32());
        Assert.Equal(optic.RayAimingEnabled, name.StartsWith("relay"));
        var clear = job.RootElement.GetProperty("removeVignettingFactors").GetBoolean();
        if (clear) foreach (var f in optic.Fields) PupilVignetting.Clear(f);
        var wave = optic.Wavelengths[job.RootElement.GetProperty("wavelength").GetInt32() - 1].Micrometers;
        var fields = job.RootElement.GetProperty("fields").EnumerateArray().ToArray();
        var pupils = ApertureSampler.GenerateGaussianQuadrature(6, 12);
        Assert.Equal(fields.Length * pupils.Count, inputs.RootElement.GetArrayLength());
        for (var fi = 0; fi < fields.Length; fi++)
        {
            var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(fields[fi][0].GetDouble(),
                fields[fi][1].GetDouble(), wave, pupils, aimAtStop: optic.RayAimingEnabled);
            var trace = optic.SequentialRayTracer.TraceGaussianPupil(bundle);
            for (var pi = 0; pi < pupils.Count; pi++)
            {
                var input = inputs.RootElement[fi * pupils.Count + pi];
                Assert.Equal(pupils[pi].X, input.GetProperty("px").GetDouble());
                Assert.Equal(pupils[pi].Y, input.GetProperty("py").GetDouble());
                Assert.Equal(pupils[pi].Weight, input.GetProperty("weight").GetDouble());
                foreach (var surface in native.RootElement.GetProperty("surfaces").EnumerateArray())
                {
                    var index = surface.GetProperty("surface").GetInt32();
                    var nr = surface.GetProperty("rays")[fi * pupils.Count + pi];
                    Assert.Equal(0, nr.GetProperty("error").GetInt32());
                    var sample = Assert.Single(trace[pi], s => s.SurfaceNumber == index);
                    Assert.False(sample.Vignetted);
                    var frame = optic.SurfaceGroup.Items[index].CoordinateSystem;
                    var point = frame.ToLocalPoint(sample.Position);
                    var direction = frame.ToLocalDirection(sample.Direction);
                    // Model-glass dispersion and finite-conjugate aiming have measured
                    // coordinate floors; none of the original RMS budgets are changed.
                    var coordinateBudget = name.StartsWith("even") || name.StartsWith("relay") ? 2e-7 : 2e-10;
                    Assert.InRange(Math.Abs(point.X - nr.GetProperty("x").GetDouble()), 0, coordinateBudget);
                    Assert.InRange(Math.Abs(point.Y - nr.GetProperty("y").GetDouble()), 0, coordinateBudget);
                    Assert.InRange(Math.Abs(point.Z - nr.GetProperty("z").GetDouble()), 0, coordinateBudget);
                    Assert.InRange(Math.Abs(direction.X - nr.GetProperty("l").GetDouble()), 0, coordinateBudget);
                    Assert.InRange(Math.Abs(direction.Y - nr.GetProperty("m").GetDouble()), 0, coordinateBudget);
                    Assert.InRange(Math.Abs(direction.Z - nr.GetProperty("n").GetDouble()), 0, coordinateBudget);
                }
            }
        }
    }

    [Fact]
    public void VirtualPupilContinuationKeepsPhysicalTraceCacheAndDisplayDataSeparate()
    {
        var optic = Load("tessar-gq6-remove");
        foreach (var f in optic.Fields) PupilVignetting.Clear(f);
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0, 1,
            optic.Wavelengths[1].Micrometers, ApertureSampler.GenerateGaussianQuadrature(6, 12));
        var cache = new RayTraceCache(16, 10000);
        optic.ConfigureRayTraceCache(cache, 10);
        var before = optic.SequentialRayTracer.Trace(bundle).RayHistories;
        var statistics = cache.Statistics;
        var display = optic.SurfaceGroup.RecordedTrace;
        var integral = optic.SequentialRayTracer.TraceGaussianPupil(bundle);
        Assert.Equal(statistics, cache.Statistics);
        Assert.Same(display, optic.SurfaceGroup.RecordedTrace);
        Assert.All(integral, h => Assert.Equal(9, h[^1].SurfaceNumber));
        Assert.Equal(9, integral.Count(h => h.Any(s => s.SegmentLength < 0)));
        Assert.Contains(integral.SelectMany(h => h), s => s.SurfaceNumber == 2 && s.SegmentLength < 0);
        Assert.Contains(integral.SelectMany(h => h), s => s.SurfaceNumber == 8 && s.SegmentLength < 0);
        var after = optic.SequentialRayTracer.Trace(bundle).RayHistories;
        Assert.Equal(before.SelectMany(h => h), after.SelectMany(h => h));
        Assert.Contains(after, h => h[^1].Vignetted || h[^1].SurfaceNumber != 9);
    }

    [Theory]
    [InlineData("GQ", 12, true)]
    [InlineData("GQ", 6, false)]
    [InlineData("RA", 128, false)]
    public void TessarRmsFieldCurveMatchesTheCapturedIntegralAndInterpolatedFactors(string method, int density, bool remove)
    {
        var name = $"tessar-lens-using-vignetting-factors-{method.ToLowerInvariant()}-{density}-{(remove ? "remove" : "retain")}";
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "rms-field-sampling", name);
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "data.json")));
        var optic = Load("tessar-gq6-remove");
        var result = new RmsVsFieldAnalysis(optic, fieldDensity: 15, numRings: density, method: method,
            wavelengthNumber: 2, removeVignetting: remove) { GaussianAzimuthalSamples = 2 * density }.GenerateData().Series!;
        Assert.Equal(16, result.Points.Count);
        for (var i = 0; i < 16; i++)
            Assert.InRange(Math.Abs(result.Points[i].Y * 1000 - native.RootElement.GetProperty("dataSeries")[0]
                .GetProperty("y")[i][0].GetDouble()), 0, method == "RA" ? .01 : density == 6 ? .0004 : 4e-8);
    }

    [Fact]
    public void NativeAuditEvidenceHashesRemainUnchanged()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "manifest.json")));
        foreach (var file in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            var path = Path.GetFullPath(Path.Combine(Root, file.GetProperty("path").GetString()!));
            Assert.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, path);
            Assert.Equal(file.GetProperty("sha256").GetString(), Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
        }
    }
}
