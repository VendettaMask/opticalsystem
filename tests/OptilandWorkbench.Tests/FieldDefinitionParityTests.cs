using System.Text.Json;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.Tests;

public sealed class FieldDefinitionParityTests
{
    private const double Tolerance = 2e-9;

    [Fact]
    public void FieldDefinitionsMatchPython058InitialAndFinalRealRays()
    {
        using var reference = LoadReference();
        var root = reference.RootElement;
        var hx = root.GetProperty("normalized_field").GetProperty("x").GetDouble();
        var hy = root.GetProperty("normalized_field").GetProperty("y").GetDouble();
        var px = root.GetProperty("normalized_pupil").GetProperty("x").GetDouble();
        var py = root.GetProperty("normalized_pupil").GetProperty("y").GetDouble();
        var wavelength = root.GetProperty("wavelength_micrometers").GetDouble();
        var finiteJson = root.GetProperty("finite_system").GetRawText();

        foreach (var expectedCase in root.GetProperty("cases").EnumerateArray())
        {
            var name = expectedCase.GetProperty("name").GetString()!;
            var optic = name.StartsWith("finite_", StringComparison.Ordinal)
                ? PythonOptilandJsonStore.Deserialize(finiteJson, name)
                : Optic.CreateCookeTriplet();
            Configure(optic, expectedCase);

            var distributionRay = optic.SequentialRayTracer.RayGenerator
                .GenerateNormalizedPupilSamples(
                    hx,
                    hy,
                    wavelength,
                    new[] { new PupilSample(px, py, 1) })
                .Rays.Single();
            AssertRay(expectedCase.GetProperty("initial_distribution_ray"), distributionRay, name);

            var genericRay = optic.SequentialRayTracer.RayGenerator
                .GenerateGeneric(hx, hy, px, py, wavelength)
                .Rays.Single();
            AssertRay(expectedCase.GetProperty("initial_generic_ray"), genericRay, name);

            var final = optic.TraceGeneric(hx, hy, px, py, wavelength)
                .RayHistories.Single()[^1];
            AssertSample(expectedCase.GetProperty("final_generic_ray"), final, name);
        }
    }

    [Fact]
    public void ParaxialImageHeightUnitChiefRaysMatchPython058()
    {
        using var reference = LoadReference();
        var root = reference.RootElement;
        var finiteJson = root.GetProperty("finite_system").GetRawText();

        foreach (var expectedCase in root.GetProperty("cases").EnumerateArray()
                     .Where(item => item.TryGetProperty("unit_chief_ray", out _)))
        {
            var name = expectedCase.GetProperty("name").GetString()!;
            var optic = name.StartsWith("finite_", StringComparison.Ordinal)
                ? PythonOptilandJsonStore.Deserialize(finiteJson, name)
                : Optic.CreateCookeTriplet();
            Configure(optic, expectedCase);
            var stopIndex = optic.SurfaceGroup.Items.ToList().FindIndex(surface => surface.IsStop);
            var positions = optic.SurfaceGroup.Items.Select(surface => surface.CoordinateSystem.Origin.Z).ToArray();
            var wavelength = optic.Wavelengths.First(item => item.IsPrimary).Micrometers;
            var image = optic.Paraxial.TraceGeneric(
                new[] { 0.0 },
                new[] { 1.0 },
                positions[stopIndex],
                wavelength,
                stopIndex);
            var reverse = optic.Paraxial.TraceGenericReverse(
                new[] { 0.0 },
                new[] { 1.0 },
                positions[^1] - positions[stopIndex],
                wavelength,
                optic.SurfaceGroup.Items.Count - stopIndex);
            var expected = expectedCase.GetProperty("unit_chief_ray");

            AssertClose(expected.GetProperty("image_height").GetDouble(), image.Heights[^1][0], name);
            AssertClose(expected.GetProperty("object_height").GetDouble(), reverse.Heights[^1][0], name);
            AssertClose(expected.GetProperty("object_slope").GetDouble(), reverse.Slopes[^1][0], name);
        }
    }

    [Fact]
    public void PythonAndNativeRoundTripsPreserveFieldContracts()
    {
        using var reference = LoadReference();
        var optic = PythonOptilandJsonStore.Deserialize(
            reference.RootElement.GetProperty("finite_system").GetRawText());
        optic.FieldDefinition = FieldDefinitionKind.ParaxialImageHeight;
        optic.ObjectSpaceTelecentric = true;
        optic.FieldGroupTelecentric = true;
        optic.Aperture.ObjectSpaceTelecentric = true;

        var pythonRoundTrip = PythonOptilandJsonStore.Deserialize(PythonOptilandJsonStore.Serialize(optic));
        var nativeRoundTrip = Optic.FromSnapshot(optic.ToSnapshot());

        foreach (var restored in new[] { pythonRoundTrip, nativeRoundTrip })
        {
            Assert.Equal(FieldDefinitionKind.ParaxialImageHeight, restored.FieldDefinition);
            Assert.True(restored.ObjectSpaceTelecentric);
            Assert.True(restored.FieldGroupTelecentric);
            Assert.True(restored.Aperture.ObjectSpaceTelecentric);
            Assert.Equal(100, restored.SurfaceGroup.Items[0].Thickness, precision: 12);
            Assert.Equal(100, restored.SurfaceGroup.Items[1].CoordinateSystem.Origin.Z, precision: 12);
            Assert.Equal(0.2, restored.Fields[1].VignetteFactorX, precision: 12);
            Assert.Equal(0.35, restored.Fields[1].VignetteFactorY, precision: 12);
        }
    }

    [Fact]
    public void ObjectHeightRejectsInfiniteObjectAndTelecentricAngleIsInvalid()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.FieldDefinition = FieldDefinitionKind.ObjectHeight;
        Assert.Throws<InvalidOperationException>(() =>
            optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 1, 0, 0, 0.55));

        optic.FieldDefinition = FieldDefinitionKind.Angle;
        optic.ObjectSpaceTelecentric = true;
        optic.Aperture.Kind = ApertureKind.NumericalAperture;
        optic.Aperture.Value = 0.2;
        Assert.Throws<InvalidOperationException>(() =>
            optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 1, 0, 0, 0.55));
    }

    private static void Configure(Optic optic, JsonElement expectedCase)
    {
        optic.FieldDefinition = expectedCase.GetProperty("field_type").GetString() switch
        {
            "object_height" => FieldDefinitionKind.ObjectHeight,
            "paraxial_image_height" => FieldDefinitionKind.ParaxialImageHeight,
            _ => FieldDefinitionKind.Angle
        };
        optic.ObjectSpaceTelecentric = expectedCase.GetProperty("telecentric").GetBoolean();
        optic.Aperture.Kind = expectedCase.GetProperty("aperture_type").GetString() switch
        {
            "objectNA" => ApertureKind.NumericalAperture,
            "imageFNO" => ApertureKind.FNumber,
            _ => ApertureKind.EntrancePupilDiameter
        };
        optic.Aperture.Value = expectedCase.GetProperty("aperture_value").GetDouble();
    }

    private static void AssertRay(JsonElement expected, RealRay actual, string caseName)
    {
        AssertClose(expected.GetProperty("x").GetDouble(), actual.Origin.X, caseName);
        AssertClose(expected.GetProperty("y").GetDouble(), actual.Origin.Y, caseName);
        AssertClose(expected.GetProperty("z").GetDouble(), actual.Origin.Z, caseName);
        AssertClose(expected.GetProperty("l").GetDouble(), actual.Direction.X, caseName);
        AssertClose(expected.GetProperty("m").GetDouble(), actual.Direction.Y, caseName);
        AssertClose(expected.GetProperty("n").GetDouble(), actual.Direction.Z, caseName);
        AssertClose(expected.GetProperty("intensity").GetDouble(), actual.Intensity, caseName);
    }

    private static void AssertSample(JsonElement expected, RayTraceSample actual, string caseName)
    {
        AssertClose(expected.GetProperty("x").GetDouble(), actual.Position.X, caseName);
        AssertClose(expected.GetProperty("y").GetDouble(), actual.Position.Y, caseName);
        AssertClose(expected.GetProperty("z").GetDouble(), actual.Position.Z, caseName);
        AssertClose(expected.GetProperty("l").GetDouble(), actual.Direction.X, caseName);
        AssertClose(expected.GetProperty("m").GetDouble(), actual.Direction.Y, caseName);
        AssertClose(expected.GetProperty("n").GetDouble(), actual.Direction.Z, caseName);
        AssertClose(expected.GetProperty("intensity").GetDouble(), actual.Intensity, caseName);
    }

    private static void AssertClose(double expected, double actual, string caseName)
    {
        Assert.True(
            Math.Abs(expected - actual) <= Tolerance * Math.Max(1, Math.Abs(expected)),
            $"{caseName}: expected {expected:R}, actual {actual:R}");
    }

    private static JsonDocument LoadReference()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "optiland-0.5.8-field-definition-reference.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }
}
