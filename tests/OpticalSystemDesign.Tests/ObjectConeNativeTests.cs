using System.Globalization;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Validation;
using Xunit.Abstractions;

namespace OpticalSystemDesign.Tests;

public sealed class ObjectConeNativeTests(ITestOutputHelper output)
{
    private static string Repository => ProductDependencyArchitectureTests.RepositoryRoot();
    private static string Prescription => EvidenceFiles.ReadAllText(Path.Combine(Repository,
        "src/OpticalSystemDesign.App/Assets/SequentialExamples/Miscellaneous/Elliptical reflector 320 degree light cone .zmx"));

    [Theory]
    [InlineData("uniform-rays")]
    [InlineData("uniform-angle")]
    [InlineData("small-cone")]
    [InlineData("off-axis")]
    [InlineData("gaussian-angle")]
    public void NativeLaunchAndBothSequentialSurfacesAgree(string name)
    {
        using var document = JsonDocument.Parse(EvidenceFiles.ReadAllText(Path.Combine(Repository,
            "validation/rechecks/2026-11-07/object-cone/rays.json")));
        var reference = document.RootElement.GetProperty("cases").EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == name);
        var text = Prescription;
        if (name == "uniform-angle") text = text.Replace("GFAC 0 2", "GFAC 0 0");
        if (name == "gaussian-angle") text = text.Replace("GFAC 0 2", "GFAC 1.5 1");
        if (name == "small-cone") text = text.Replace("OBNA 160 1", "OBNA 50 1");
        if (name == "off-axis") text = text.Replace("YFLN 0", "YFLN 0.25");
        var optic = OpticalFormatCatalog.Import(text, ".zmx");
        var tracer = optic.SequentialRayTracer;
        var rays = reference.GetProperty("inputs").EnumerateArray().Select(point => tracer.RayGenerator.GenerateGeneric(
            Number(point, "hx"), Number(point, "hy"), Number(point, "px"), Number(point, "py"), .55).Rays.Single()).ToArray();
        var launchError = 0d;
        var positionError = 0d;
        var directionError = 0d;
        var intensityError = 0d;
        var pathError = 0d;
        var imagePathError = 0d;
        using var trace = tracer.Trace(new RealRayBundle(rays), TraceRequest.Selected([1, 2]));
        for (var i = 0; i < rays.Length; i++)
        {
            var launch = reference.GetProperty("launches")[i];
            var launchPosition = Position(launch) + optic.SurfaceGroup.Items[0].CoordinateSystem.Origin;
            launchError = Math.Max(launchError, (rays[i].Direction - Direction(launch)).Length);
            Assert.InRange((rays[i].Origin - launchPosition).Length, 0, 1e-10);
            var mirrorReference = reference.GetProperty("surfaces")[0].GetProperty("rays")[i];
            var mirrorPosition = optic.SurfaceGroup.Items[1].CoordinateSystem.ToGlobalPoint(Position(mirrorReference));
            for (var surface = 1; surface <= 2; surface++)
            {
                var expected = reference.GetProperty("surfaces")[surface - 1].GetProperty("rays")[i];
                var actual = trace.GetSurfaceSamples(surface)[i]!.Value;
                Assert.Equal(0, expected.GetProperty("error").GetInt32());
                Assert.Equal(0, expected.GetProperty("vignette").GetInt32());
                Assert.False(actual.Vignetted);
                var coordinates = optic.SurfaceGroup.Items[surface].CoordinateSystem;
                positionError = Math.Max(positionError, (coordinates.ToLocalPoint(actual.Position) - Position(expected)).Length);
                directionError = Math.Max(directionError, (coordinates.ToLocalDirection(actual.Direction) + Direction(expected)).Length);
                intensityError = Math.Max(intensityError, Math.Abs(actual.Intensity - Number(expected, "intensity")));
                if (surface == 1)
                    pathError = Math.Max(pathError, Math.Abs(actual.CumulativeOpticalPathLength - Number(expected, "opdWaves")));
                if (surface == 2)
                {
                    var imagePosition = coordinates.ToGlobalPoint(Position(expected));
                    var nativePath = Number(mirrorReference, "opdWaves") + (imagePosition - mirrorPosition).Length;
                    imagePathError = Math.Max(imagePathError, Math.Abs(actual.CumulativeOpticalPathLength - nativePath));
                }
            }
        }
        output.WriteLine(FormattableString.Invariant(
            $"{name} ({rays.Length} rays): launch {launchError:G17}, position mm {positionError:G17}, direction {directionError:G17}, intensity {intensityError:G17}, mirror OPL mm {pathError:G17}, image path error mm {imagePathError:G17}"));
        Assert.InRange(launchError, 0, 2e-12);
        Assert.InRange(positionError, 0, 2e-9);
        Assert.InRange(directionError, 0, 2e-12);
        Assert.InRange(intensityError, 0, 2e-12);
        Assert.InRange(pathError, 0, 2e-9);
        Assert.InRange(imagePathError, 0, 2e-9);
    }

    [Theory]
    [InlineData(160, 72.79404685324042)]
    [InlineData(50, 238.35071851884203)]
    public void ApertureRoundTripsAndRetainsTheNativeParaxialPupil(double angle, double diameter)
    {
        var text = Prescription.Replace("OBNA 160 1", $"OBNA {angle.ToString(CultureInfo.InvariantCulture)} 1");
        var imported = OpticalFormatCatalog.Import(text, ".zmx");
        var snapshot = Optic.FromSnapshot(imported.ToSnapshot());
        var exported = OpticalFormatCatalog.Import(OpticalFormatCatalog.Export(snapshot, ".zmx"), ".zmx");
        foreach (var optic in new[] { imported, snapshot, exported })
        {
            Assert.Equal(ApertureKind.ObjectConeAngle, optic.Aperture.Kind);
            Assert.Equal(angle, optic.Aperture.Value);
            Assert.Equal(diameter, optic.Paraxial.EstimateEntrancePupilDiameter(), 10);
            Assert.Equal(88.88888888888889, optic.Paraxial.EstimateEffectiveFocalLength(), 10);
            var ray = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, 1, 0, .55).Rays.Single();
            Assert.Equal(Math.Cos(angle * Math.PI / 180), ray.Direction.Z, 12);
            var final = optic.SequentialRayTracer.TraceFinalSamples(new RealRayBundle([ray])).Single();
            Assert.NotNull(final);
            Assert.False(final.Vignetted);
        }
    }

    [Fact]
    public void TransverseRayLeavesTheFiniteObjectPlane()
    {
        var optic = OpticalFormatCatalog.Import(Prescription.Replace("GFAC 0 2", "GFAC 0 0"), ".zmx");
        var tracer = optic.SequentialRayTracer;
        var ray = tracer.RayGenerator.GenerateGeneric(0, 0, 90d / 160, 0, .55).Rays.Single();
        Assert.InRange(Math.Abs(ray.Direction.Z), 0, 1e-15);
        var image = tracer.TraceFinalSamples(new RealRayBundle([ray])).Single();
        Assert.NotNull(image);
        Assert.False(image.Vignetted);
        Assert.InRange(Math.Abs(image.Position.X), 0, 1e-8);
    }

    [Theory]
    [InlineData("r100")]
    [InlineData("r225")]
    [InlineData("r240")]
    [InlineData("r280")]
    [InlineData("r300")]
    [InlineData("a141-axial")]
    [InlineData("a142-axial")]
    [InlineData("two-fields")]
    [InlineData("negative-field")]
    [InlineData("near-shift")]
    [InlineData("r240-shift100")]
    [InlineData("r225-clap")]
    [InlineData("fixed-diameter")]
    [InlineData("mixed-field")]
    [InlineData("narrow-field")]
    public void FloatingConicAndExplicitProjectedEdgesMatchNativeVignetting(string name)
    {
        using var document = JsonDocument.Parse(EvidenceFiles.ReadAllText(Path.Combine(Repository,
            "validation/rechecks/2026-11-07/object-cone-edge/clipping-native.json")));
        var reference = document.RootElement.GetProperty("cases").EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == name);
        var mode = reference.GetProperty("mode").GetString();
        var radius = Format(Number(reference, "radius"));
        var angle = Format(Number(reference, "halfAngle"));
        var offset = Format(Number(reference, "decenterX"));
        var fields = reference.GetProperty("fieldY").EnumerateArray().Select(field => Format(field.GetDouble())).ToArray();
        var text = Prescription.Replace("OBNA 160 1", $"OBNA {angle} 1")
            .Replace("FTYP 0 0 1 1", $"FTYP 0 0 {fields.Length} 1")
            .Replace("YFLN 0", $"YFLN {string.Join(" ", fields)}");
        if (reference.TryGetProperty("fieldX", out var fieldX))
            text = text.Replace("XFLN 0", $"XFLN {string.Join(" ", fieldX.EnumerateArray().Select(field => Format(field.GetDouble())))}");
        if (mode != "circle") text = text.Replace("DIAM 225.92814463071437 0", $"DIAM {radius} 1");
        if (mode == "circle") text = text.Replace("  VCON", $"  CLAP 0 {radius} 0\n  VCON");
        if (mode == "float") text = text.Replace("  VCON", $"  FLAP 0 {radius} 0\n  OBDC {offset} 0\n  VCON");
        var imported = OpticalFormatCatalog.Import(text, ".zmx");
        var snapshot = Optic.FromSnapshot(imported.ToSnapshot());
        var exported = OpticalFormatCatalog.Import(OpticalFormatCatalog.Export(snapshot, ".zmx"), ".zmx");
        foreach (var optic in new[] { imported, snapshot, exported })
        {
            var tracer = optic.SequentialRayTracer;
            var frame = optic.SurfaceGroup.Items[1].CoordinateSystem;
            var rays = reference.GetProperty("inputs").EnumerateArray().Select(p => tracer.RayGenerator.GenerateGeneric(
                Number(p, "hx"), Number(p, "hy"), Number(p, "px"), Number(p, "py"), .55).Rays.Single()).ToArray();
            using var trace = tracer.Trace(new RealRayBundle(rays), TraceRequest.Selected([1, 2]));
            var clipped = 0;
            var positionError = 0d;
            var intensityError = 0d;
            for (var i = 0; i < rays.Length; i++)
            {
                var expected = reference.GetProperty("mirror")[i];
                var vignetted = expected.GetProperty("vignette").GetInt32() != 0;
                var mirror = trace.GetSurfaceSamples(1)[i]!.Value;
                Assert.Equal(vignetted, mirror.Vignetted);
                positionError = Math.Max(positionError, (frame.ToLocalPoint(mirror.Position) - Position(expected)).Length);
                intensityError = Math.Max(intensityError, Math.Abs(mirror.Intensity - (vignetted ? 0 : Number(expected, "intensity"))));
                if (vignetted)
                {
                    clipped++;
                    Assert.Null(trace.GetSurfaceSamples(2)[i]);
                    var diagnostic = tracer.DiagnoseFirstFailure(rays[i]);
                    Assert.Equal(SurfaceTraceFailure.ApertureClipping, diagnostic.Failure);
                    Assert.Equal(1, diagnostic.SurfaceIndex);
                }
                else
                    Assert.False(trace.GetSurfaceSamples(2)[i]!.Value.Vignetted);
            }
            Assert.InRange(positionError, 0, 2e-8);
            Assert.InRange(intensityError, 0, 2e-12);
            output.WriteLine(FormattableString.Invariant(
                $"{name}: {rays.Length} rays, {clipped} vignetted, position mm {positionError:G17}, intensity {intensityError:G17}"));
        }
    }

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static double Number(JsonElement value, string key) => value.GetProperty(key).GetDouble();
    private static Vector3D Position(JsonElement value) => new(Number(value, "x"), Number(value, "y"), Number(value, "z"));
    private static Vector3D Direction(JsonElement value) => new(Number(value, "l"), Number(value, "m"), Number(value, "n"));
}
