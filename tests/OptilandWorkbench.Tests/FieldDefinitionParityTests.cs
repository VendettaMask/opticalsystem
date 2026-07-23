using System.Text.Json;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Visualization;

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
    public void FieldDefinitionsMatchPython058ParaxialNormalizedTrace()
    {
        using var reference = LoadReference();
        var root = reference.RootElement;
        var hy = root.GetProperty("normalized_field").GetProperty("y").GetDouble();
        var py = root.GetProperty("normalized_pupil").GetProperty("y").GetDouble();
        var wavelength = root.GetProperty("wavelength_micrometers").GetDouble();
        var finiteJson = root.GetProperty("finite_system").GetRawText();

        foreach (var expectedCase in root.GetProperty("cases").EnumerateArray())
        {
            var expected = expectedCase.GetProperty("paraxial_trace");
            if (expected.GetProperty("heights").EnumerateArray().Any(item => item.ValueKind != JsonValueKind.Number))
            {
                continue;
            }

            var name = expectedCase.GetProperty("name").GetString()!;
            var optic = name.StartsWith("finite_", StringComparison.Ordinal)
                ? PythonOptilandJsonStore.Deserialize(finiteJson, name)
                : Optic.CreateCookeTriplet();
            Configure(optic, expectedCase);
            var actual = optic.Paraxial.TraceNormalizedPupil(hy, new[] { py }, wavelength);
            var actualHeights = actual.Heights.Select(values => values[0]).ToArray();
            var actualSlopes = actual.Slopes.Select(values => values[0]).ToArray();
            var expectedHeights = expected.GetProperty("heights").EnumerateArray().Select(item => item.GetDouble()).ToArray();
            var expectedSlopes = expected.GetProperty("slopes").EnumerateArray().Select(item => item.GetDouble()).ToArray();

            Assert.Equal(expectedHeights.Length, actualHeights.Length);
            Assert.Equal(expectedSlopes.Length, actualSlopes.Length);
            for (var index = 0; index < expectedHeights.Length; index++)
            {
                AssertClose(expectedHeights[index], actualHeights[index], name);
                AssertClose(expectedSlopes[index], actualSlopes[index], name);
            }
        }
    }

    [Fact]
    public void RealImageHeightChiefRayHitsRequestedTwoDimensionalCoordinate()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.FieldDefinition = FieldDefinitionKind.RealImageHeight;
        optic.Fields.Clear();
        optic.Fields.Add(new FieldPoint { Label = "On axis" });
        optic.Fields.Add(new FieldPoint { Label = "Diagonal", X = 3, Y = 4 });
        var wavelength = optic.Wavelengths.First(item => item.IsPrimary).Micrometers;

        var final = optic.TraceGeneric(0.6, 0.8, 0, 0, wavelength).RayHistories.Single()[^1];
        var local = optic.SurfaceGroup.Items[^1].CoordinateSystem.ToLocalPoint(final.Position);

        AssertClose(3, local.X, "real_image_height_x");
        AssertClose(4, local.Y, "real_image_height_y");
    }

    [Fact]
    public void FiniteConjugateRealImageHeightChiefRayHitsRequestedCoordinate()
    {
        using var reference = LoadReference();
        var optic = PythonOptilandJsonStore.Deserialize(
            reference.RootElement.GetProperty("finite_system").GetRawText());
        optic.FieldDefinition = FieldDefinitionKind.RealImageHeight;
        optic.Fields.Clear();
        optic.Fields.Add(new FieldPoint { Label = "On axis" });
        optic.Fields.Add(new FieldPoint { Label = "Diagonal", X = 1.5, Y = 2 });
        var wavelength = optic.Wavelengths.First(item => item.IsPrimary).Micrometers;

        var final = optic.TraceGeneric(0.6, 0.8, 0, 0, wavelength).RayHistories.Single()[^1];
        var local = optic.SurfaceGroup.Items[^1].CoordinateSystem.ToLocalPoint(final.Position);

        AssertClose(1.5, local.X, "finite_real_image_height_x");
        AssertClose(2, local.Y, "finite_real_image_height_y");
    }

    [Fact]
    public void LongFiniteConjugateRealImageHeightAllowsFullNewtonCorrection()
    {
        var optic = new Optic("Long finite conjugate");
        optic.FieldDefinition = FieldDefinitionKind.RealImageHeight;
        optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter;
        optic.Aperture.Value = 10;
        optic.Fields.Add(new FieldPoint { Label = "On axis" });
        optic.Fields.Add(new FieldPoint { Label = "Full field", Y = 4.5 });
        optic.Wavelengths.Add(new Wavelength
        {
            Label = "d",
            Nanometers = 587.6,
            Weight = 1,
            IsPrimary = true
        });
        optic.SurfaceGroup.Replace(new[]
        {
            new OpticalSurface
            {
                Label = "Object",
                Thickness = 2500,
                Material = "Air",
                SemiDiameter = 1000
            },
            new OpticalSurface
            {
                Label = "Aperture stop",
                Thickness = 50,
                Material = "Air",
                SemiDiameter = 1000,
                IsStop = true
            },
            new OpticalSurface
            {
                Label = "Image",
                Material = "Air",
                SemiDiameter = 1000
            }
        });
        var wavelength = optic.Wavelengths.Single().Micrometers;

        var final = optic.TraceGeneric(0, 1, 0, 0, wavelength).RayHistories.Single()[^1];
        var local = optic.SurfaceGroup.Items[^1].CoordinateSystem.ToLocalPoint(final.Position);

        AssertClose(4.5, local.Y, "long_finite_real_image_height_y");
        Assert.False(final.Vignetted);
    }

    [Fact]
    public void ViewerUsesPythonRadialNormalizationForDiagonalFields()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.Fields.Clear();
        optic.Fields.Add(new FieldPoint { Label = "On axis" });
        optic.Fields.Add(new FieldPoint { Label = "Diagonal", X = 3, Y = 4 });
        var wavelengthIndex = optic.Wavelengths.ToList().FindIndex(item => item.IsPrimary);
        var wavelength = optic.Wavelengths[wavelengthIndex].Micrometers;
        var expected = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0.6, 0.8, 0, 0, wavelength).Rays.Single();

        var scene = new Layout2DBuilder(optic).Build3D(options: new LayoutBuildOptions(
            FieldIndex: 1,
            WavelengthIndex: wavelengthIndex,
            RayCount: 1));
        var actual = scene.Rays.Single().Points[0];

        AssertClose(expected.Origin.X, actual.X, "viewer_field_x");
        AssertClose(expected.Origin.Y, actual.Y, "viewer_field_y");
        AssertClose(expected.Origin.Z, actual.Z, "viewer_field_z");
    }

    [Fact]
    public void ParaxialChiefRayPreservesNegativeFieldDirection()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.Fields.Clear();
        optic.Fields.Add(new FieldPoint { Label = "Negative", Y = -12 });
        var wavelength = optic.Wavelengths.First(item => item.IsPrimary).Micrometers;

        var chief = optic.Paraxial.ChiefRay(wavelength);
        var direct = optic.Paraxial.TraceNormalizedPupil(-1, new[] { 0.0 }, wavelength);

        AssertClose(direct.Heights[^1][0], chief.Heights[^1][0], "negative_chief_height");
        AssertClose(direct.Slopes[^1][0], chief.Slopes[^1][0], "negative_chief_slope");
        Assert.True(chief.Heights[^1][0] < 0);
    }

    [Fact]
    public void ParaxialChiefRayUsesLargestAbsoluteSignedField()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.Fields.Clear();
        optic.Fields.Add(new FieldPoint { Label = "Positive", Y = 8 });
        optic.Fields.Add(new FieldPoint { Label = "Negative full field", Y = -12 });
        var wavelength = optic.Wavelengths.First(item => item.IsPrimary).Micrometers;

        var chief = optic.Paraxial.ChiefRay(wavelength);
        var direct = optic.Paraxial.TraceNormalizedPupil(-1, new[] { 0.0 }, wavelength);

        AssertClose(direct.Heights[^1][0], chief.Heights[^1][0], "signed_full_field_height");
        AssertClose(direct.Slopes[^1][0], chief.Slopes[^1][0], "signed_full_field_slope");
    }

    [Fact]
    public void AnalysesUseMaximumRadialFieldForDiagonalCoordinates()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.Fields.Clear();
        optic.Fields.Add(new FieldPoint { Label = "On axis" });
        optic.Fields.Add(new FieldPoint { Label = "Diagonal", X = 3, Y = 4 });

        var distortion = new DistortionAnalysis(optic, numPoints: 3).GenerateData();
        var fieldCurvature = new FieldCurvatureAnalysis(optic, numPoints: 3).GenerateData();

        Assert.Equal(5, Convert.ToDouble(distortion.Values["MaxFieldDegrees"]), precision: 12);
        Assert.Equal(5, distortion.PlotSeries[0].Points[^1].Y, precision: 12);
        Assert.Equal(5, Convert.ToDouble(fieldCurvature.Values["MaxFieldDegrees"]), precision: 12);
        Assert.Equal(5, fieldCurvature.PlotSeries[0].Points[^1].Y, precision: 12);
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
