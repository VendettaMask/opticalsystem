using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Serialization;
using Xunit.Abstractions;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class AfocalParabasalScaleTests(ITestOutputHelper output)
{
    private static JsonElement Controls() => JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures/afocal-parabasal-controls.json")));
    public static IEnumerable<object[]> Cases() => Controls().GetProperty("cases").EnumerateArray().Select(p => new object[] { p.GetProperty("id").GetString()! });
    internal static OpticSnapshot Snapshot()
    {
        using var zip = ZipFile.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures/afocal-psf-contract/source-evidence.zip"));
        using var stream = zip.GetEntry("artifacts/validation/afocal-recompare-20261026/baseline-v4/models/001-afocal-riflescope-c1.json")!.Open();
        return JsonSerializer.Deserialize<OpticSnapshot>(stream, OpticalSystemDesign.ZemaxComparison.JsonFiles.Options)!;
    }
    private static (double D, double Z) Scale(Optic optic)
    {
        var method = typeof(Optic).Assembly.GetType("OpticalSystemDesign.Core.Services.AfocalParabasalPupilScale")!
            .GetMethod("TryCompute", BindingFlags.NonPublic | BindingFlags.Static)!;
        object?[] parameters = [optic, 0d, 0d];
        Assert.True((bool)method.Invoke(null, parameters)!);
        return ((double)parameters[1]!, (double)parameters[2]!);
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void FiniteScaleAndItsLaunchesMatchNativeWithoutChangingPreciseRays(string id)
    {
        var row = Controls().GetProperty("cases").EnumerateArray().Single(p => p.GetProperty("id").GetString() == id);
        var optic = Optic.FromSnapshot(Snapshot());
        optic.Aperture.Value = row.GetProperty("apertureDiameter").GetDouble();
        optic.RayAimingEnabled = row.GetProperty("rayAimingEnabled").GetBoolean();
        var primary = row.GetProperty("primaryWavelength").GetInt32() - 1;
        for (var i = 0; i < optic.Wavelengths.Count; i++) optic.Wavelengths[i].IsPrimary = i == primary;
        var wave = optic.Wavelengths[primary].Micrometers;
        RealRay Precise() => optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, 0, .001, wave, true).Rays[0];
        var before = Precise();
        var paraxialBefore = optic.Paraxial.EstimateExitPupilDiameter();
        var scale = Scale(optic);
        Assert.InRange(Math.Abs(scale.D - row.GetProperty("nativeExitDiameter").GetDouble()), 0, 1e-9);
        Assert.InRange(Math.Abs(scale.Z - row.GetProperty("nativeExitPosition").GetDouble()), 0, 1e-9);
        var method = optic.SequentialRayTracer.RayGenerator.GetType().GetMethod("TryGenerateAfocalScaleProbes", BindingFlags.NonPublic | BindingFlags.Instance)!;
        object?[] arguments = [null];
        Assert.True((bool)method.Invoke(optic.SequentialRayTracer.RayGenerator, arguments)!);
        var bundle = (RealRayBundle)arguments[0]!;
        var errors = new List<double>();
        for (var i = 0; i < 3; i++)
        {
            var ray = bundle.Rays[i]; var expected = row.GetProperty("nativeProbeLaunches")[i];
            var launch = ray.Origin + ray.Direction * ((optic.SurfaceGroup.Items[1].CoordinateSystem.Origin.Z - ray.Origin.Z) / ray.Direction.Z);
            var error = Math.Max(Math.Abs(launch.X - expected.GetProperty("x").GetDouble()), Math.Abs(launch.Y - expected.GetProperty("y").GetDouble()));
            errors.Add(error);
            Assert.InRange(error, 0, 1e-11);
        }
        var after = Precise();
        Assert.Equal(before.Origin, after.Origin);
        Assert.Equal(before.Direction, after.Direction);
        Assert.Equal(paraxialBefore, optic.Paraxial.EstimateExitPupilDiameter());
        output.WriteLine(JsonSerializer.Serialize(new { id, scale.D, scale.Z, launchErrorsMillimeters = errors }));
    }
    [Fact]
    public void FixedPhysicalProbeDoesNotDependOnFieldTableExtentOrImageAxisRotation()
    {
        var snapshot = Snapshot();
        var original = Scale(Optic.FromSnapshot(snapshot));
        var optic = Optic.FromSnapshot(snapshot with { Fields = [snapshot.Fields[0]] });
        var axisOnly = Scale(optic);
        optic.SurfaceGroup.Items[^1].CoordinateSystem = optic.SurfaceGroup.Items[^1].CoordinateSystem with { RotationZDegrees = 90 };
        var rotated = Scale(optic);
        Assert.InRange(Math.Abs(axisOnly.D - original.D), 0, 1e-12);
        Assert.InRange(Math.Abs(rotated.D - original.D), 0, 1e-12);
        Assert.InRange(Math.Abs(axisOnly.Z - original.Z), 0, 1e-12);
        Assert.InRange(Math.Abs(rotated.Z - original.Z), 0, 1e-12);
    }
}
