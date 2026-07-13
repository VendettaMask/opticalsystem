using System.Text.Json;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.Tests;

public sealed class CookeTripletGoldenTests
{
    private const double ScalarTolerance = 1e-11;
    private const double TraceTolerance = 1e-10;

    [Theory]
    [InlineData("cooke")]
    [InlineData("tessar")]
    public void OfficialSampleMatchesOptilandParaxialResults(string sampleName)
    {
        using var reference = LoadReference(sampleName);
        var expected = reference.RootElement.GetProperty("prescription");
        var optic = CreateSample(sampleName);

        AssertClose(expected.GetProperty("effective_focal_length").GetDouble(), optic.Paraxial.EstimateEffectiveFocalLength(), ScalarTolerance);
        AssertClose(expected.GetProperty("f_number").GetDouble(), optic.Paraxial.EstimateFNumber(), ScalarTolerance);
        AssertClose(expected.GetProperty("entrance_pupil_diameter").GetDouble(), optic.Paraxial.EstimateEntrancePupilDiameter(), ScalarTolerance);
        AssertClose(expected.GetProperty("entrance_pupil_location").GetDouble(), optic.Paraxial.EstimateEntrancePupilLocation(), ScalarTolerance);
    }

    [Theory]
    [InlineData("cooke")]
    [InlineData("tessar")]
    public void OfficialSampleMatchesOptilandSurfaceBySurface(string sampleName)
    {
        using var reference = LoadReference(sampleName);
        var optic = CreateSample(sampleName);

        foreach (var expectedTrace in reference.RootElement.GetProperty("traces").EnumerateArray())
        {
            var trace = optic.TraceGeneric(
                expectedTrace.GetProperty("field_x").GetDouble(),
                expectedTrace.GetProperty("field_y").GetDouble(),
                expectedTrace.GetProperty("pupil_x").GetDouble(),
                expectedTrace.GetProperty("pupil_y").GetDouble(),
                expectedTrace.GetProperty("wavelength_micrometers").GetDouble());
            var history = Assert.Single(trace.RayHistories);

            foreach (var expectedSurface in expectedTrace.GetProperty("surfaces").EnumerateArray().Skip(1))
            {
                var surfaceNumber = expectedSurface.GetProperty("surface").GetInt32();
                var actual = Assert.Single(history, sample => sample.SurfaceNumber == surfaceNumber);
                AssertSample(expectedTrace.GetProperty("name").GetString()!, expectedSurface, actual);
            }
        }
    }

    [Theory]
    [InlineData("cooke")]
    [InlineData("tessar")]
    public void OfficialSampleMatchesOptilandLineBundles(string sampleName)
    {
        using var reference = LoadReference(sampleName);
        var optic = CreateSample(sampleName);

        foreach (var expectedBundle in reference.RootElement.GetProperty("line_y_bundles").EnumerateArray())
        {
            var trace = optic.Trace(
                0,
                expectedBundle.GetProperty("field_y").GetDouble(),
                expectedBundle.GetProperty("wavelength_micrometers").GetDouble(),
                expectedBundle.GetProperty("ray_count").GetInt32(),
                "line_y");
            var finalSamples = trace.RayHistories.Select(history => history[^1]).ToArray();
            var totalIntensity = finalSamples.Sum(sample => sample.Intensity);
            var centroidX = finalSamples.Sum(sample => sample.Position.X * sample.Intensity) / totalIntensity;
            var centroidY = finalSamples.Sum(sample => sample.Position.Y * sample.Intensity) / totalIntensity;
            var rmsSpotRadius = Math.Sqrt(finalSamples.Sum(sample =>
                ((Math.Pow(sample.Position.X - centroidX, 2) + Math.Pow(sample.Position.Y - centroidY, 2)) * sample.Intensity)) / totalIntensity);

            AssertClose(expectedBundle.GetProperty("centroid_x").GetDouble(), centroidX, TraceTolerance);
            AssertClose(expectedBundle.GetProperty("centroid_y").GetDouble(), centroidY, TraceTolerance);
            AssertClose(expectedBundle.GetProperty("rms_spot_radius").GetDouble(), rmsSpotRadius, TraceTolerance);
        }
    }

    [Theory]
    [InlineData("cooke")]
    [InlineData("tessar")]
    public void OfficialSampleRetainsParityAfterSnapshotRoundTrip(string sampleName)
    {
        using var reference = LoadReference(sampleName);
        var traceCase = reference.RootElement.GetProperty("traces")[0];
        var original = CreateSample(sampleName);
        var restored = Optic.FromSnapshot(original.ToSnapshot());
        var originalTrace = TraceCase(original, traceCase);
        var restoredTrace = TraceCase(restored, traceCase);

        AssertClose(original.Paraxial.EstimateEffectiveFocalLength(), restored.Paraxial.EstimateEffectiveFocalLength(), ScalarTolerance);
        Assert.Equal(originalTrace.Count, restoredTrace.Count);
        for (var index = 0; index < originalTrace.Count; index++)
        {
            AssertClose(originalTrace[index].Position.X, restoredTrace[index].Position.X, TraceTolerance);
            AssertClose(originalTrace[index].Position.Y, restoredTrace[index].Position.Y, TraceTolerance);
            AssertClose(originalTrace[index].CumulativeOpticalPathLength, restoredTrace[index].CumulativeOpticalPathLength, TraceTolerance);
            AssertClose(originalTrace[index].Intensity, restoredTrace[index].Intensity, TraceTolerance);
        }
    }

    [Theory]
    [InlineData("cooke")]
    [InlineData("tessar")]
    public async Task OfficialPythonNativeJsonImportsWithNumericalParity(string sampleName)
    {
        using var reference = LoadReference(sampleName);
        var expectedPrescription = reference.RootElement.GetProperty("prescription");
        var nativePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", $"optiland-0.5.8-{sampleName}-native.json");
        var optic = await OpticJsonStore.LoadAsync(nativePath);

        AssertClose(expectedPrescription.GetProperty("effective_focal_length").GetDouble(), optic.Paraxial.EstimateEffectiveFocalLength(), ScalarTolerance);
        AssertClose(expectedPrescription.GetProperty("f_number").GetDouble(), optic.Paraxial.EstimateFNumber(), ScalarTolerance);
        AssertClose(expectedPrescription.GetProperty("entrance_pupil_diameter").GetDouble(), optic.Paraxial.EstimateEntrancePupilDiameter(), ScalarTolerance);
        AssertClose(expectedPrescription.GetProperty("entrance_pupil_location").GetDouble(), optic.Paraxial.EstimateEntrancePupilLocation(), ScalarTolerance);

        foreach (var expectedTrace in reference.RootElement.GetProperty("traces").EnumerateArray())
        {
            var history = TraceCase(optic, expectedTrace);
            foreach (var expectedSurface in expectedTrace.GetProperty("surfaces").EnumerateArray().Skip(1))
            {
                var surfaceNumber = expectedSurface.GetProperty("surface").GetInt32();
                var actual = Assert.Single(history, sample => sample.SurfaceNumber == surfaceNumber);
                AssertSample(expectedTrace.GetProperty("name").GetString()!, expectedSurface, actual);
            }
        }
    }

    [Theory]
    [InlineData("cooke")]
    [InlineData("tessar")]
    public void PythonJsonExportRoundTripsSupportedOfficialSamples(string sampleName)
    {
        using var reference = LoadReference(sampleName);
        var original = CreateSample(sampleName);
        var json = PythonOptilandJsonStore.Serialize(original);
        var restored = PythonOptilandJsonStore.Deserialize(json, $"Restored {sampleName}");
        var traceCases = reference.RootElement.GetProperty("traces");
        var traceCase = traceCases[traceCases.GetArrayLength() - 1];
        var originalTrace = TraceCase(original, traceCase);
        var restoredTrace = TraceCase(restored, traceCase);

        Assert.True(PythonOptilandJsonStore.LooksLike(json));
        Assert.Contains("-Infinity", json, StringComparison.Ordinal);
        Assert.Equal(original.Fields.Count, restored.Fields.Count);
        Assert.Equal(original.Wavelengths.Count, restored.Wavelengths.Count);
        Assert.Equal(original.SurfaceGroup.Items.Count, restored.SurfaceGroup.Items.Count);
        AssertClose(original.Paraxial.EstimateEffectiveFocalLength(), restored.Paraxial.EstimateEffectiveFocalLength(), ScalarTolerance);
        Assert.Equal(originalTrace.Count, restoredTrace.Count);
        for (var index = 0; index < originalTrace.Count; index++)
        {
            AssertClose(originalTrace[index].Position.X, restoredTrace[index].Position.X, TraceTolerance);
            AssertClose(originalTrace[index].Position.Y, restoredTrace[index].Position.Y, TraceTolerance);
            AssertClose(originalTrace[index].CumulativeOpticalPathLength, restoredTrace[index].CumulativeOpticalPathLength, TraceTolerance);
            AssertClose(originalTrace[index].Intensity, restoredTrace[index].Intensity, TraceTolerance);
        }
    }

    [Fact]
    public void PythonJsonExportRoundTripsSupportedNonStandardGeometries()
    {
        IGeometry[] geometries =
        {
            new EvenAsphereGeometry(44, -0.7, new[] { 1e-5, -2e-8 }),
            new OddAsphereGeometry(42, -0.2, new[] { 2e-4, -3e-6 }),
            new BiconicGeometry(1.3, 1.4, -0.1, -0.2)
        };

        foreach (var geometry in geometries)
        {
            var optic = Optic.CreateTessarLens();
            optic.SurfaceGroup.Items[1].Geometry = geometry;

            var json = PythonOptilandJsonStore.Serialize(optic);
            var restored = PythonOptilandJsonStore.Deserialize(json);

            AssertGeometryEquivalent(geometry, restored.SurfaceGroup.Items[1].Geometry);
        }
    }

    [Fact]
    public void PythonJsonExportRejectsUnsupportedGeometryExplicitly()
    {
        var optic = Optic.CreateTessarLens();
        optic.SurfaceGroup.Items[1].Geometry = new ForbesQGeometry(42, -0.6, 8, new[] { 1e-4, -2e-5 });

        var error = Assert.Throws<NotSupportedException>(() => PythonOptilandJsonStore.Serialize(optic));

        Assert.Contains("forbes_q", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PythonJsonAdapterRoundTripsSimpleCoatingDictionary()
    {
        var optic = Optic.CreateTessarLens();
        optic.SurfaceGroup.Items[1].CoatingModel = new SimpleCoatingModel(0.82, 0.07);

        var json = PythonOptilandJsonStore.Serialize(optic);
        var restored = PythonOptilandJsonStore.Deserialize(json);
        var restoredCoating = Assert.IsType<SimpleCoatingModel>(restored.SurfaceGroup.Items[1].CoatingModel);

        Assert.Contains("\"type\": \"SimpleCoating\"", json, StringComparison.Ordinal);
        Assert.Contains("\"transmittance\": 0.82", json, StringComparison.Ordinal);
        Assert.Contains("\"reflectance\": 0.07", json, StringComparison.Ordinal);
        Assert.Equal(0.82, restoredCoating.Transmittance, precision: 12);
        Assert.Equal(0.07, restoredCoating.Reflectance, precision: 12);
    }

    [Fact]
    public void PythonJsonExportRejectsUnsupportedCoatingExplicitly()
    {
        var optic = Optic.CreateTessarLens();
        optic.SurfaceGroup.Items[1].CoatingModel = new ThinFilmStackCoating(new[]
        {
            new ThinFilmLayer("MgF2", 120)
        });

        var error = Assert.Throws<NotSupportedException>(() => PythonOptilandJsonStore.Serialize(optic));

        Assert.Contains("thin_film_stack", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertSample(string traceName, JsonElement expected, RayTraceSample actual)
    {
        var surfaceNumber = expected.GetProperty("surface").GetInt32();
        AssertClose(expected.GetProperty("x").GetDouble(), actual.Position.X, TraceTolerance, traceName, surfaceNumber, "x");
        AssertClose(expected.GetProperty("y").GetDouble(), actual.Position.Y, TraceTolerance, traceName, surfaceNumber, "y");
        AssertClose(expected.GetProperty("z").GetDouble(), actual.Position.Z, TraceTolerance, traceName, surfaceNumber, "z");
        AssertClose(expected.GetProperty("l").GetDouble(), actual.Direction.X, TraceTolerance, traceName, surfaceNumber, "l");
        AssertClose(expected.GetProperty("m").GetDouble(), actual.Direction.Y, TraceTolerance, traceName, surfaceNumber, "m");
        AssertClose(expected.GetProperty("n").GetDouble(), actual.Direction.Z, TraceTolerance, traceName, surfaceNumber, "n");
        AssertClose(expected.GetProperty("opd").GetDouble(), actual.CumulativeOpticalPathLength, TraceTolerance, traceName, surfaceNumber, "opd");
        AssertClose(expected.GetProperty("intensity").GetDouble(), actual.Intensity, TraceTolerance, traceName, surfaceNumber, "intensity");
    }

    private static IReadOnlyList<RayTraceSample> TraceCase(Optic optic, JsonElement traceCase)
    {
        return optic.TraceGeneric(
            traceCase.GetProperty("field_x").GetDouble(),
            traceCase.GetProperty("field_y").GetDouble(),
            traceCase.GetProperty("pupil_x").GetDouble(),
            traceCase.GetProperty("pupil_y").GetDouble(),
            traceCase.GetProperty("wavelength_micrometers").GetDouble()).RayHistories.Single();
    }

    private static void AssertGeometryEquivalent(IGeometry expected, IGeometry actual)
    {
        switch (expected)
        {
            case EvenAsphereGeometry even:
                var actualEven = Assert.IsType<EvenAsphereGeometry>(actual);
                AssertClose(even.Base.Radius, actualEven.Base.Radius, ScalarTolerance);
                AssertClose(even.Base.Conic, actualEven.Base.Conic, ScalarTolerance);
                Assert.Equal(even.Coefficients, actualEven.Coefficients);
                break;
            case OddAsphereGeometry odd:
                var actualOdd = Assert.IsType<OddAsphereGeometry>(actual);
                AssertClose(odd.Base.Radius, actualOdd.Base.Radius, ScalarTolerance);
                AssertClose(odd.Base.Conic, actualOdd.Base.Conic, ScalarTolerance);
                Assert.Equal(odd.Coefficients, actualOdd.Coefficients);
                break;
            case BiconicGeometry biconic:
                var actualBiconic = Assert.IsType<BiconicGeometry>(actual);
                AssertClose(biconic.RadiusX, actualBiconic.RadiusX, ScalarTolerance);
                AssertClose(biconic.RadiusY, actualBiconic.RadiusY, ScalarTolerance);
                AssertClose(biconic.ConicX, actualBiconic.ConicX, ScalarTolerance);
                AssertClose(biconic.ConicY, actualBiconic.ConicY, ScalarTolerance);
                break;
            default:
                throw new NotSupportedException($"No test assertion for geometry '{expected.Kind}'.");
        }
    }

    private static Optic CreateSample(string sampleName)
    {
        return sampleName switch
        {
            "cooke" => Optic.CreateCookeTriplet(),
            "tessar" => Optic.CreateTessarLens(),
            _ => throw new ArgumentOutOfRangeException(nameof(sampleName))
        };
    }

    private static JsonDocument LoadReference(string sampleName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", $"optiland-0.5.8-{sampleName}.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static void AssertClose(
        double expected,
        double actual,
        double tolerance,
        string? traceName = null,
        int? surfaceNumber = null,
        string? quantity = null)
    {
        var difference = Math.Abs(expected - actual);
        Assert.True(
            difference <= tolerance,
            $"{traceName ?? "paraxial"} surface {surfaceNumber?.ToString() ?? "-"} {quantity ?? "value"}: "
            + $"expected {expected:R}, actual {actual:R}, difference {difference:E3}, tolerance {tolerance:E3}");
    }
}
