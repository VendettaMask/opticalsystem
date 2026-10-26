using System.IO.Compression;
using System.Text.Json;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.Tests;

public sealed class ZoomWideAngleNativeParityTests
{
    [Theory]
    [InlineData("zoom-lens", 1, 1)]
    [InlineData("zoom-lens", 1, 2)]
    [InlineData("zoom-lens", 1, 3)]
    [InlineData("zoom-lens", 2, 1)]
    [InlineData("zoom-lens", 2, 2)]
    [InlineData("zoom-lens", 2, 3)]
    [InlineData("zoom-lens", 3, 1)]
    [InlineData("zoom-lens", 3, 2)]
    [InlineData("zoom-lens", 3, 3)]
    [InlineData("wide-angle-lens-100-degree-field", 1, 1)]
    [InlineData("wide-angle-lens-100-degree-field", 1, 2)]
    [InlineData("wide-angle-lens-100-degree-field", 1, 3)]
    [InlineData("wide-angle-lens-200-degree-field", 1, 1)]
    [InlineData("wide-angle-lens-200-degree-field", 1, 2)]
    [InlineData("wide-angle-lens-200-degree-field", 1, 3)]
    [InlineData("wide-angle-lens-210-degree-field", 1, 1)]
    [InlineData("wide-angle-lens-210-degree-field", 1, 2)]
    [InlineData("wide-angle-lens-210-degree-field", 1, 3)]
    public void OriginalNativeRaysMatchThroughputAndEverySurfaceInBothExecutionPaths(string slug, int configuration, int wavelength)
    {
        using var archive = OpenReference();
        var optic = new ZemaxZmxImporter().ImportConfigurationSet(Read(archive, $"run/sources/{slug}.zmx"))
            .Configurations[configuration - 1];
        using var native = JsonDocument.Parse(Read(archive, $"run/rays/{slug}-c{configuration}-w{wavelength}/native/ray-audit.json"));
        var inputs = native.RootElement.GetProperty("inputs").EnumerateArray().ToArray();
        var surfaces = native.RootElement.GetProperty("surfaces").EnumerateArray().ToArray();
        var scale = slug.Contains("100-degree", StringComparison.Ordinal) || slug.Contains("200-degree", StringComparison.Ordinal) ? 25.4 : 1;
        var image = optic.SurfaceGroup.Items.Count - 1;
        Assert.Equal(image, surfaces.Length);
        var offset = 0;
        foreach (var field in inputs.GroupBy(i => i.GetProperty("fieldIndex").GetInt32()))
        {
            var rows = field.ToArray();
            var pupils = rows.Select(r => new PupilSample(r.GetProperty("px").GetDouble(), r.GetProperty("py").GetDouble(), r.GetProperty("weight").GetDouble())).ToArray();
            var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
                rows[0].GetProperty("hx").GetDouble(), rows[0].GetProperty("hy").GetDouble(),
                optic.Wavelengths[wavelength - 1].Micrometers, pupils, aimAtStop: optic.RayAimingEnabled);
            foreach (var batched in new[] { true, false })
            {
                using var trace = optic.SequentialRayTracer.Trace(bundle, TraceRequest.FullHistory(maxDegreeOfParallelism: 1) with { UseBatchedBackend = batched });
                for (var pi = 0; pi < rows.Length; pi++)
                {
                    var ni = offset + pi;
                    var final = surfaces[^1].GetProperty("rays")[ni];
                    var nativeAccepted = final.GetProperty("error").GetInt32() == 0 && final.GetProperty("vignette").GetInt32() == 0;
                    var coreAccepted = trace.TryGetSample(pi, image, out var last) && !last.Vignetted && last.Intensity > 0;
                    Assert.Equal(nativeAccepted, coreAccepted);
                    var firstClip = final.GetProperty("vignette").GetInt32();
                    if (firstClip > 0 && final.GetProperty("error").GetInt32() == 0)
                    {
                        Assert.True(trace.TryGetSample(pi, firstClip, out var clipped));
                        Assert.True(clipped.Vignetted);
                    }
                    foreach (var surface in surfaces)
                    {
                        var number = surface.GetProperty("surface").GetInt32();
                        var expected = surface.GetProperty("rays")[ni];
                        if (expected.GetProperty("error").GetInt32() != 0 || expected.GetProperty("vignette").GetInt32() != 0) continue;
                        Assert.True(trace.TryGetSample(pi, number, out var actual));
                        Assert.False(actual.Vignetted);
                        var frame = optic.SurfaceGroup.Items[number].CoordinateSystem;
                        var p = frame.ToLocalPoint(actual.Position);
                        var d = frame.ToLocalDirection(actual.Direction);
                        Compare(p.X, expected.GetProperty("x").GetDouble() * scale, 1e-6);
                        Compare(p.Y, expected.GetProperty("y").GetDouble() * scale, 1e-6);
                        Compare(p.Z, expected.GetProperty("z").GetDouble() * scale, 1e-6);
                        Compare(d.X, expected.GetProperty("l").GetDouble(), 1e-8);
                        Compare(d.Y, expected.GetProperty("m").GetDouble(), 1e-8);
                        Compare(d.Z, expected.GetProperty("n").GetDouble(), 1e-8);
                    }
                }
            }
            offset += rows.Length;
        }
        Assert.Equal(inputs.Length, offset);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ZoomObjectDirectionDoesNotDependOnImagePlaneDefocus(int configuration)
    {
        using var archive = OpenReference();
        var optic = new ZemaxZmxImporter().ImportConfigurationSet(Read(archive, "run/sources/zoom-lens.zmx"))
            .Configurations[configuration - 1];
        var before = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 1, .3, -.2,
            optic.Wavelengths[0].Micrometers).Rays[0].Direction;
        optic.SurfaceGroup.Items[^2].Thickness += 3;
        optic.SurfaceGroup.Renumber();
        var after = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 1, .3, -.2,
            optic.Wavelengths[0].Micrometers).Rays[0].Direction;
        Assert.InRange((before - after).Length, 0, 2e-14);
    }

    [Theory]
    [InlineData(89.8)]
    [InlineData(90)]
    [InlineData(90.2)]
    [InlineData(90.3)]
    [InlineData(-89.8)]
    [InlineData(-90)]
    [InlineData(-90.2)]
    [InlineData(-90.3)]
    public void TransverseChiefKeepsRequestedAngleAndReachesPhysicalStop(double angle)
    {
        using var archive = OpenReference();
        var optic = new ZemaxZmxImporter().ImportConfigurationSet(Read(archive, "run/sources/wide-angle-lens-210-degree-field.zmx"))
            .Configurations[0];
        var ray = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, angle / 105, 0, 0,
            optic.Wavelengths[1].Micrometers, aimAtStop: true).Rays[0];
        Assert.InRange(Math.Abs(ray.Direction.Y - Math.Sin(angle * Math.PI / 180)), 0, 1e-14);
        Assert.InRange(Math.Abs(ray.Direction.Z - Math.Cos(angle * Math.PI / 180)), 0, 1e-14);
        var stop = optic.SurfaceGroup.Items.Single(s => s.IsStop);
        foreach (var batched in new[] { true, false })
        {
            using var trace = optic.SequentialRayTracer.Trace(new([ray]),
                TraceRequest.FullHistory(maxDegreeOfParallelism: 1) with { UseBatchedBackend = batched });
            Assert.True(trace.TryGetSample(0, stop.Number, out var intercept));
            Assert.False(intercept.Vignetted);
            var p = stop.CoordinateSystem.ToLocalPoint(intercept.Position);
            Assert.InRange(double.Hypot(p.X, p.Y), 0, 1e-8);
            Assert.True(trace.TryGetSample(0, optic.SurfaceGroup.Items.Count - 1, out var image));
            Assert.False(image.Vignetted);
        }
    }

    [Theory]
    [InlineData("wide-angle-lens-200-degree-field")]
    [InlineData("wide-angle-lens-210-degree-field")]
    public void TransverseFtanReferenceReportsUnavailableWithoutFoldingTheField(string slug)
    {
        using var archive = OpenReference();
        var optic = new ZemaxZmxImporter().ImportConfigurationSet(Read(archive, $"run/sources/{slug}.zmx")).Configurations[0];
        var options = new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var before = JsonSerializer.Serialize(optic.ToSnapshot(), options);
        foreach (var type in new[] { "F-Tan(Theta)", "Calibrated F-Tan(Theta)" })
        {
            var error = Assert.Throws<OpticalSystemDesign.Core.Analysis.AnalysisDataUnavailableException>(() =>
                new OpticalSystemDesign.Core.Analysis.FieldCurvatureAndDistortionAnalysis(optic, distortionType: type).GenerateData());
            Assert.Contains("select F-Theta", error.Reason);
            Assert.Equal(before, JsonSerializer.Serialize(optic.ToSnapshot(), options));
        }
    }

    private static void Compare(double actual, double expected, double absolute)
    {
        Assert.True(double.IsFinite(actual));
        // Exactly the pre-repair matrix policy; no fitted or lens-specific budget.
        Assert.InRange(Math.Abs(actual - expected), 0, absolute + 1e-6 * Math.Abs(expected));
    }

    private static string Read(ZipArchive archive, string name)
    {
        using var reader = new StreamReader((archive.GetEntry(name) ?? throw new FileNotFoundException(name)).Open());
        return reader.ReadToEnd();
    }

    private static ZipArchive OpenReference()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "OpticalSystemDesign.slnx"))) root = root.Parent;
        return ZipFile.OpenRead(Path.Combine(root?.FullName ?? throw new DirectoryNotFoundException("Repository not found"),
            "validation/zemax/2026-r1/standard-matrix-2026-10-15/native-and-core-evidence.zip"));
    }
}
