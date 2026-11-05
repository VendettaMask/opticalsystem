using OpticalSystemDesign.Validation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Propagation;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Tests;

public sealed class GridGradientPhaseTests
{
    private static JsonDocument Fixture() => JsonDocument.Parse(EvidenceFiles.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "grid-gradient-phase-native-v1.json")));

    private static GridGradientIndexProfile Grid(JsonElement fixture, string key)
    {
        var m = fixture.GetProperty("materials").GetProperty(key);
        var g = m.GetProperty("bundledSample").GetBoolean()
            ? GridGradientCatalog.Resolve("Sample.GGD", null, 1)!
            : GridGradientCatalog.Read(Convert.FromBase64String(m.GetProperty("bytesBase64").GetString()!), "control.ggd", 1);
        Assert.Equal(key, g.SourceSha256);
        return g;
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
    public void IndependentGridRaysAndPhaseUseTheOriginalRayGates(int caseIndex)
    {
        using var fixture = Fixture(); var f = fixture.RootElement; var c = f.GetProperty("cases")[caseIndex];
        var source = c.GetProperty("source").GetString()!;
        Assert.Equal(c.GetProperty("sourceSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source))));
        // Resolve the portable physical grid explicitly, without writing files
        // or requiring the native machine's original absolute asset path.
        var portable = Regex.Replace(source, @"TYPE GRINSU11 [^\r\n]+", "TYPE GRINSU11 Sample.GGD");
        var optic = OpticalFormatCatalog.Import(portable, ".zmx");
        var entrance = c.GetProperty("entranceSurface").GetInt32();
        var original = Assert.IsType<GradientIndexMaterial>(optic.SurfaceGroup.Items[entrance].MaterialAfter);
        var material = new GradientIndexMaterial("Independent GGD", Grid(f, c.GetProperty("grid").GetString()!), original.MaximumPathLength,
            original.IntegrationOptions with { MaximumStep = .02 });
        optic.SurfaceGroup.Items[entrance].MaterialAfter = material;
        optic.SurfaceGroup.Items[entrance + 1].MaterialBefore = material.Clone();
        var rows = c.GetProperty("rows").EnumerateArray().ToArray();
        foreach (var beam in rows.GroupBy(r => (Px: r.GetProperty("input").GetProperty("data3").GetDouble(),
                     Py: r.GetProperty("input").GetProperty("data4").GetDouble())))
        {
            var surfaces = beam.Select(r => r.GetProperty("input").GetProperty("int1").GetInt32()).Distinct().ToArray();
            var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, beam.Key.Px, beam.Key.Py, .55, false);
            using var trace = optic.SequentialRayTracer.Trace(bundle, TraceRequest.Selected(surfaces));
            var reference = trace.GetSurfaceSamples(entrance)[0]!.Value.PhaseInclusiveOpticalPathLength!.Value;
            var nativeReference = beam.Single(r => r.GetProperty("input").GetProperty("code").GetString() == "OPTH"
                && r.GetProperty("input").GetProperty("int1").GetInt32() == entrance).GetProperty("value").GetDouble();
            foreach (var row in beam)
            {
                var p = row.GetProperty("input"); var index = p.GetProperty("int1").GetInt32();
                Assert.True(trace.TryGetSample(0, index, out var sample)); Assert.False(sample.Vignetted);
                var coordinates = optic.SurfaceGroup.Items[index].CoordinateSystem;
                var point = coordinates.ToLocalPoint(sample.Position); var direction = coordinates.ToLocalDirection(sample.Direction);
                var code = p.GetProperty("code").GetString();
                var actual = code switch
                {
                    "REAX" => point.X, "REAY" => point.Y, "REAZ" => point.Z,
                    "REAA" => direction.X, "REAB" => direction.Y, "REAC" => direction.Z,
                    "OPTH" => sample.PhaseInclusiveOpticalPathLength!.Value - reference,
                    _ => throw new InvalidDataException(code)
                };
                var expected = row.GetProperty("value").GetDouble(); if (code == "OPTH") expected -= nativeReference;
                Assert.True(Math.Abs(actual - expected) <= 1e-8 + 1e-6 * Math.Abs(expected),
                    $"{c.GetProperty("id").GetString()} ({beam.Key}) {index}/{code}: {actual:R} vs {expected:R}");
            }
        }
    }

    [Fact]
    public void DirectNativeIndexCurvesIncludeOddEvenShiftedAndUnequalGrids()
    {
        using var fixture = Fixture(); var f = fixture.RootElement;
        foreach (var curve in f.GetProperty("curves").EnumerateArray())
        {
            var g = Grid(f, curve.GetProperty("grid").GetString()!); var axis = curve.GetProperty("axis").GetString();
            var xs = curve.GetProperty("coordinates"); var ns = curve.GetProperty("indexValues");
            for (var i = 0; i < xs.GetArrayLength(); i++)
            {
                var q = xs[i].GetDouble(); var point = new Vector3D(axis == "X" ? q : curve.GetProperty("x").GetDouble(),
                    axis == "Y" ? q : curve.GetProperty("y").GetDouble(), axis == "Z" ? q : curve.GetProperty("z").GetDouble());
                Assert.InRange(Math.Abs(g.InterfaceRefractiveIndex(point, 550) - ns[i].GetDouble()), 0, 1e-12);
            }
        }
    }

    [Theory] [InlineData(1)] [InlineData(-1)]
    public void HomogeneousGridPreservesUnitDirectionsAndOpticalPath(int sign)
    {
        var values = new double[6 * 6 * 6 * 11]; for (var i = 0; i < values.Length; i += 11) values[i] = 1.7;
        var grid = new GridGradientIndexProfile(6, 6, 6, new(-.5, -.5, 0), new(.2, .2, .2), values);
        var coordinates = new CoordinateSystem(new(4, -2, 3), 12, -20, 45);
        var origin = coordinates.ToGlobalPoint(new(0, 0, .5)); var direction = coordinates.ToGlobalDirection(new Vector3D(.1, -.1, sign));
        direction /= direction.Length;
        var result = GradientIndexRayIntegrator.TraceDistance(grid, coordinates, origin, direction * 3, 550, .2,
            new(MaximumStep: .02, RetainPath: true));
        Assert.Equal(GradientIndexTermination.DistanceReached, result.Termination);
        Assert.InRange((result.End.Position - (origin + direction * .2)).Length, 0, 1e-12);
        Assert.InRange((result.End.Direction - direction).Length, 0, 1e-12);
        Assert.InRange(Math.Abs(result.End.OpticalPathLength - .34), 0, 1e-12);
        Assert.All(result.Path, p => Assert.InRange(Math.Abs(p.Direction.Length - 1), 0, 1e-12));
    }
}
