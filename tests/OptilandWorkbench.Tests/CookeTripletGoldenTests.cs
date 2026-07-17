using System.Text.Json;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
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
            new BiconicGeometry(1.3, 1.4, -0.1, -0.2),
            new ToroidalGeometry(80, 30),
            new PolynomialGeometry(new Dictionary<(int X, int Y), double>
            {
                [(0, 2)] = 1e-3,
                [(1, 1)] = 2e-4,
                [(3, 0)] = -3e-6
            }),
            new ChebyshevGeometry(new Dictionary<(int XOrder, int YOrder), double>
            {
                [(0, 2)] = 1e-3,
                [(1, 1)] = 2e-4,
                [(3, 0)] = -3e-6
            }, 5, 7),
            new ZernikeGeometry(new Dictionary<(int RadialOrder, int AzimuthalFrequency), double>
            {
                [(0, 0)] = 1e-3,
                [(1, -1)] = 2e-4,
                [(2, 0)] = -3e-6,
                [(3, 1)] = 4e-7
            }, 6)
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

    [Theory]
    [InlineData("0.25", "[]", "conic_yz")]
    [InlineData("0.0", "[1e-6]", "coeffs_poly_y")]
    public void PythonJsonImportRejectsUnsupportedToroidalTermsExplicitly(
        string conicYz,
        string coeffsPolyY,
        string expectedTerm)
    {
        var json = PythonJsonWithToroidalGeometry(conicYz, coeffsPolyY);

        var error = Assert.Throws<NotSupportedException>(() => PythonOptilandJsonStore.Deserialize(json));

        Assert.Contains("ToroidalGeometry", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expectedTerm, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PythonJsonImportRejectsUnsupportedPolynomialBaseExplicitly()
    {
        var json = PythonJsonWithPolynomialGeometry("42.0");

        var error = Assert.Throws<NotSupportedException>(() => PythonOptilandJsonStore.Deserialize(json));

        Assert.Contains("PolynomialGeometry", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("finite base radius", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PythonJsonImportRejectsUnsupportedChebyshevBaseExplicitly()
    {
        var json = PythonJsonWithChebyshevGeometry("42.0");

        var error = Assert.Throws<NotSupportedException>(() => PythonOptilandJsonStore.Deserialize(json));

        Assert.Contains("ChebyshevPolynomialGeometry", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("finite base radius", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PythonJsonImportRejectsUnsupportedZernikeBaseExplicitly()
    {
        var json = PythonJsonWithZernikeGeometry("42.0", "fringe");

        var error = Assert.Throws<NotSupportedException>(() => PythonOptilandJsonStore.Deserialize(json));

        Assert.Contains("ZernikePolynomialGeometry", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("finite base radius", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PythonJsonImportRejectsUnsupportedZernikeTypeExplicitly()
    {
        var json = PythonJsonWithZernikeGeometry("Infinity", "standard");

        var error = Assert.Throws<NotSupportedException>(() => PythonOptilandJsonStore.Deserialize(json));

        Assert.Contains("ZernikePolynomialGeometry", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("standard", error.Message, StringComparison.OrdinalIgnoreCase);
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
    public void PythonJsonAdapterRoundTripsThinLensInteractionDictionary()
    {
        var optic = Optic.CreateTessarLens();
        optic.SurfaceGroup.Items[1].InteractionModel = new ThinLensInteractionModel(75);

        var json = PythonOptilandJsonStore.Serialize(optic);
        var restored = PythonOptilandJsonStore.Deserialize(json);
        var restoredInteraction = Assert.IsType<ThinLensInteractionModel>(restored.SurfaceGroup.Items[1].InteractionModel);

        Assert.Contains("\"type\": \"ThinLensInteractionModel\"", json, StringComparison.Ordinal);
        Assert.Contains("\"focal_length\": 75", json, StringComparison.Ordinal);
        Assert.Equal(75, restoredInteraction.FocalLength, precision: 12);
    }

    [Fact]
    public void PythonJsonAdapterRoundTripsSupportedPhysicalApertures()
    {
        IPhysicalAperture[] apertures =
        {
            new CircularAperture(2.5),
            new AnnularAperture(3, 1),
            new OffsetRadialAperture(2.5, 0.5, 1.25, -0.75),
            new RectangularAperture(3, 2, 1, -1),
            new EllipticalAperture(4, 2, 0.5, -0.25),
            CreateExtendedPhysicalAperture("polygon"),
            CreateExtendedPhysicalAperture("file"),
            CreateExtendedPhysicalAperture("union"),
            CreateExtendedPhysicalAperture("intersection"),
            CreateExtendedPhysicalAperture("difference")
        };

        foreach (var aperture in apertures)
        {
            var optic = Optic.CreateTessarLens();
            optic.SurfaceGroup.Items[1].PhysicalAperture = aperture;

            var json = PythonOptilandJsonStore.Serialize(optic);
            var restored = PythonOptilandJsonStore.Deserialize(json);

            AssertPhysicalApertureEquivalent(aperture, restored.SurfaceGroup.Items[1].PhysicalAperture);
        }
    }

    [Fact]
    public void PythonJsonAdapterMatchesExtendedPhysicalApertureReference()
    {
        using var reference = LoadApertureReference();
        Assert.Equal("0.5.8", reference.RootElement.GetProperty("optiland_version").GetString());

        foreach (var apertureCase in reference.RootElement.GetProperty("apertures").EnumerateArray())
        {
            var expectedDictionary = apertureCase.GetProperty("dictionary");
            var json = PythonJsonWithSurfaceGeometry(
                PythonPlaneGeometry(),
                apertureJson: expectedDictionary.GetRawText());
            var optic = PythonOptilandJsonStore.Deserialize(json);
            var aperture = Assert.IsAssignableFrom<IPhysicalAperture>(optic.SurfaceGroup.Items[1].PhysicalAperture);

            foreach (var sample in apertureCase.GetProperty("samples").EnumerateArray())
            {
                var point = new Vector3D(
                    sample.GetProperty("x").GetDouble(),
                    sample.GetProperty("y").GetDouble(),
                    0);
                Assert.Equal(
                    sample.GetProperty("inside").GetBoolean(),
                    aperture.Contains(point));
            }

            var strictExport = PythonOptilandJsonStore.Serialize(optic)
                .Replace("-Infinity", "-1e308", StringComparison.Ordinal)
                .Replace("Infinity", "1e308", StringComparison.Ordinal);
            using var exported = JsonDocument.Parse(strictExport);
            var actualDictionary = exported.RootElement
                .GetProperty("surface_group")
                .GetProperty("surfaces")[1]
                .GetProperty("aperture");
            AssertJsonObjectEquivalent(expectedDictionary, actualDictionary);
        }
    }

    [Theory]
    [InlineData("annular")]
    [InlineData("offset_radial")]
    [InlineData("asymmetric_rectangular")]
    [InlineData("elliptical")]
    [InlineData("polygon")]
    [InlineData("file")]
    [InlineData("union")]
    [InlineData("intersection")]
    [InlineData("difference")]
    public void NativeSnapshotRoundTripsExtendedPhysicalApertures(string kind)
    {
        IPhysicalAperture expected = kind switch
        {
            "annular" => new AnnularAperture(3, 1),
            "offset_radial" => new OffsetRadialAperture(2.5, 0.5, 1.25, -0.75),
            "asymmetric_rectangular" => new RectangularAperture(3, 2, 1, -1),
            "elliptical" => new EllipticalAperture(4, 2, 0.5, -0.25),
            _ => CreateExtendedPhysicalAperture(kind)
        };
        var optic = Optic.CreateTessarLens();
        optic.SurfaceGroup.Items[1].PhysicalAperture = expected;

        var restored = Optic.FromSnapshot(optic.ToSnapshot());

        AssertPhysicalApertureEquivalent(expected, restored.SurfaceGroup.Items[1].PhysicalAperture);
    }

    [Fact]
    public void PythonJsonImportRejectsUnsupportedSystemApertureExplicitly()
    {
        var json = PythonJsonWithSurfaceGeometry(
            PythonPlaneGeometry(),
            systemApertureType: "chief_ray_height");

        var error = Assert.Throws<NotSupportedException>(() => PythonOptilandJsonStore.Deserialize(json));

        Assert.Contains("aperture", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("chief_ray_height", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("ObjectHeightField")]
    [InlineData("ParaxialImageHeightField")]
    public void PythonJsonImportRejectsUnsupportedFieldDefinitionsExplicitly(string fieldType)
    {
        var json = PythonJsonWithSurfaceGeometry(
            PythonPlaneGeometry(),
            fieldType: fieldType);

        var error = Assert.Throws<NotSupportedException>(() => PythonOptilandJsonStore.Deserialize(json));

        Assert.Contains("field type", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(fieldType, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("apodization", "apodization")]
    [InlineData("pickups", "pickups")]
    [InlineData("solves", "solves")]
    [InlineData("apertureTelecentric", "telecentric")]
    [InlineData("fieldTelecentric", "telecentric")]
    [InlineData("fieldObjectSpaceTelecentric", "telecentric")]
    [InlineData("polarization", "polarization")]
    public void PythonJsonImportRejectsUnsupportedRootContractsExplicitly(
        string contract,
        string expectedTerm)
    {
        var json = contract switch
        {
            "apodization" => PythonJsonWithSurfaceGeometry(
                PythonPlaneGeometry(),
                apodizationJson: """
                {
                  "type": "GaussianApodization"
                }
                """),
            "pickups" => PythonJsonWithSurfaceGeometry(
                PythonPlaneGeometry(),
                pickupsJson: """
                [
                  {
                    "source_surface_idx": 1,
                    "target_surface_idx": 2,
                    "attribute": "radius",
                    "scale": 1.0,
                    "offset": 0.0
                  }
                ]
                """),
            "solves" => PythonJsonWithSurfaceGeometry(
                PythonPlaneGeometry(),
                solvesJson: """
                {
                  "solves": [
                    {
                      "type": "QuickFocusSolve"
                    }
                  ]
                }
                """),
            "apertureTelecentric" => PythonJsonWithSurfaceGeometry(
                PythonPlaneGeometry(),
                systemApertureType: "objectNA",
                apertureObjectSpaceTelecentric: "true"),
            "fieldTelecentric" => PythonJsonWithSurfaceGeometry(
                PythonPlaneGeometry(),
                fieldsTelecentric: "true"),
            "fieldObjectSpaceTelecentric" => PythonJsonWithSurfaceGeometry(
                PythonPlaneGeometry(),
                fieldsObjectSpaceTelecentric: "true"),
            "polarization" => PythonJsonWithSurfaceGeometry(
                PythonPlaneGeometry(),
                wavelengthPolarizationJson: "\"linear_x\""),
            _ => throw new ArgumentOutOfRangeException(nameof(contract))
        };

        var error = Assert.Throws<NotSupportedException>(() => PythonOptilandJsonStore.Deserialize(json));

        Assert.Contains(expectedTerm, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PythonJsonImportRejectsUnsupportedMaterialPropagationExplicitly()
    {
        var json = PythonJsonWithSurfaceGeometry(
            PythonPlaneGeometry(),
            materialPostJson: """
            {
              "type": "IdealMaterial",
              "propagation_model": {
                "class": "GRINPropagation"
              },
              "index": 1.5,
              "absorp": 0.0
            }
            """);

        var error = Assert.Throws<NotSupportedException>(() => PythonOptilandJsonStore.Deserialize(json));

        Assert.Contains("propagation", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GRINPropagation", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PythonJsonImportRejectsReflectiveThinLensInteractionExplicitly()
    {
        var json = PythonJsonWithSurfaceGeometry(PythonPlaneGeometry(), PythonThinLensInteraction("75.0", "true"));

        var error = Assert.Throws<NotSupportedException>(() => PythonOptilandJsonStore.Deserialize(json));

        Assert.Contains("ThinLensInteractionModel", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reflective", error.Message, StringComparison.OrdinalIgnoreCase);
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
            case ToroidalGeometry toroidal:
                var actualToroidal = Assert.IsType<ToroidalGeometry>(actual);
                AssertClose(toroidal.TangentialRadius, actualToroidal.TangentialRadius, ScalarTolerance);
                AssertClose(toroidal.SagittalRadius, actualToroidal.SagittalRadius, ScalarTolerance);
                break;
            case PolynomialGeometry polynomial:
                var actualPolynomial = Assert.IsType<PolynomialGeometry>(actual);
                AssertPairCoefficientsEqual(polynomial.Coefficients, actualPolynomial.Coefficients);
                break;
            case ChebyshevGeometry chebyshev:
                var actualChebyshev = Assert.IsType<ChebyshevGeometry>(actual);
                AssertClose(chebyshev.NormalizationX, actualChebyshev.NormalizationX, ScalarTolerance);
                AssertClose(chebyshev.NormalizationY, actualChebyshev.NormalizationY, ScalarTolerance);
                AssertPairCoefficientsEqual(chebyshev.Coefficients, actualChebyshev.Coefficients);
                break;
            case ZernikeGeometry zernike:
                var actualZernike = Assert.IsType<ZernikeGeometry>(actual);
                AssertClose(zernike.PupilRadius, actualZernike.PupilRadius, ScalarTolerance);
                AssertPairCoefficientsEqual(zernike.Coefficients, actualZernike.Coefficients);
                break;
            default:
                throw new NotSupportedException($"No test assertion for geometry '{expected.Kind}'.");
        }
    }

    private static void AssertPairCoefficientsEqual(
        IReadOnlyDictionary<(int X, int Y), double> expected,
        IReadOnlyDictionary<(int X, int Y), double> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        foreach (var coefficient in expected)
        {
            Assert.True(actual.TryGetValue(coefficient.Key, out var actualCoefficient));
            AssertClose(coefficient.Value, actualCoefficient, ScalarTolerance);
        }
    }

    private static void AssertPhysicalApertureEquivalent(IPhysicalAperture expected, IPhysicalAperture? actual)
    {
        switch (expected)
        {
            case CircularAperture circular:
                var actualCircular = Assert.IsType<CircularAperture>(actual);
                AssertClose(circular.Radius, actualCircular.Radius, ScalarTolerance);
                break;
            case AnnularAperture annular:
                var actualAnnular = Assert.IsType<AnnularAperture>(actual);
                AssertClose(annular.OuterRadius, actualAnnular.OuterRadius, ScalarTolerance);
                AssertClose(annular.InnerRadius, actualAnnular.InnerRadius, ScalarTolerance);
                break;
            case OffsetRadialAperture offset:
                var actualOffset = Assert.IsType<OffsetRadialAperture>(actual);
                AssertClose(offset.OuterRadius, actualOffset.OuterRadius, ScalarTolerance);
                AssertClose(offset.InnerRadius, actualOffset.InnerRadius, ScalarTolerance);
                AssertClose(offset.OffsetX, actualOffset.OffsetX, ScalarTolerance);
                AssertClose(offset.OffsetY, actualOffset.OffsetY, ScalarTolerance);
                break;
            case RectangularAperture rectangular:
                var actualRectangular = Assert.IsType<RectangularAperture>(actual);
                AssertClose(rectangular.HalfWidth, actualRectangular.HalfWidth, ScalarTolerance);
                AssertClose(rectangular.HalfHeight, actualRectangular.HalfHeight, ScalarTolerance);
                AssertClose(rectangular.CenterX, actualRectangular.CenterX, ScalarTolerance);
                AssertClose(rectangular.CenterY, actualRectangular.CenterY, ScalarTolerance);
                break;
            case EllipticalAperture elliptical:
                var actualElliptical = Assert.IsType<EllipticalAperture>(actual);
                AssertClose(elliptical.SemiAxisX, actualElliptical.SemiAxisX, ScalarTolerance);
                AssertClose(elliptical.SemiAxisY, actualElliptical.SemiAxisY, ScalarTolerance);
                AssertClose(elliptical.OffsetX, actualElliptical.OffsetX, ScalarTolerance);
                AssertClose(elliptical.OffsetY, actualElliptical.OffsetY, ScalarTolerance);
                break;
            case FileAperture file:
                var actualFile = Assert.IsType<FileAperture>(actual);
                Assert.Equal(file.FilePath, actualFile.FilePath);
                Assert.Equal(file.Delimiter, actualFile.Delimiter);
                Assert.Equal(file.SkipHeader, actualFile.SkipHeader);
                AssertVerticesEqual(file.Vertices, actualFile.Vertices);
                break;
            case PolygonAperture polygon:
                var actualPolygon = Assert.IsType<PolygonAperture>(actual);
                AssertVerticesEqual(polygon.Vertices, actualPolygon.Vertices);
                break;
            case BooleanAperture boolean:
                var actualBoolean = Assert.IsAssignableFrom<BooleanAperture>(actual);
                Assert.Equal(boolean.GetType(), actualBoolean.GetType());
                AssertPhysicalApertureEquivalent(boolean.Left, actualBoolean.Left);
                AssertPhysicalApertureEquivalent(boolean.Right, actualBoolean.Right);
                break;
            default:
                throw new NotSupportedException($"No test assertion for aperture '{expected.Kind}'.");
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

    private static JsonDocument LoadApertureReference()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "optiland-0.5.8-aperture-reference.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static void AssertJsonObjectEquivalent(JsonElement expected, JsonElement actual)
    {
        Assert.Equal(expected.ValueKind, actual.ValueKind);
        if (expected.ValueKind != JsonValueKind.Object)
        {
            AssertJsonValueEquivalent(expected, actual);
            return;
        }

        var expectedProperties = expected.EnumerateObject().ToArray();
        var actualProperties = actual.EnumerateObject().ToArray();
        Assert.Equal(expectedProperties.Length, actualProperties.Length);
        foreach (var property in expectedProperties)
        {
            Assert.True(actual.TryGetProperty(property.Name, out var actualValue));
            AssertJsonValueEquivalent(property.Value, actualValue);
        }
    }

    private static void AssertJsonValueEquivalent(JsonElement expected, JsonElement actual)
    {
        Assert.Equal(expected.ValueKind, actual.ValueKind);
        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                AssertJsonObjectEquivalent(expected, actual);
                break;
            case JsonValueKind.Array:
                Assert.Equal(expected.GetArrayLength(), actual.GetArrayLength());
                for (var index = 0; index < expected.GetArrayLength(); index++)
                {
                    AssertJsonValueEquivalent(expected[index], actual[index]);
                }
                break;
            case JsonValueKind.Number:
                AssertClose(expected.GetDouble(), actual.GetDouble(), ScalarTolerance);
                break;
            case JsonValueKind.String:
                Assert.Equal(expected.GetString(), actual.GetString());
                break;
            case JsonValueKind.True or JsonValueKind.False:
                Assert.Equal(expected.GetBoolean(), actual.GetBoolean());
                break;
            case JsonValueKind.Null:
                break;
            default:
                throw new NotSupportedException($"No JSON assertion for {expected.ValueKind}.");
        }
    }

    private static IPhysicalAperture CreateExtendedPhysicalAperture(string kind)
    {
        var vertices = new (double X, double Y)[]
        {
            (-3, -1),
            (2, -2),
            (3, 1),
            (0, 3)
        };
        return kind switch
        {
            "polygon" => new PolygonAperture(vertices),
            "file" => new FileAperture(
                vertices,
                "tools/python-reference/aperture_vertices.txt",
                " ",
                0),
            "union" => new UnionAperture(
                new CircularAperture(2),
                new OffsetRadialAperture(1.5, offsetX: 2)),
            "intersection" => new IntersectionAperture(
                new RectangularAperture(2.5, 1),
                new EllipticalAperture(3, 2)),
            "difference" => new DifferenceAperture(
                new CircularAperture(3),
                new RectangularAperture(0.5, 4)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static void AssertVerticesEqual(
        IReadOnlyList<(double X, double Y)> expected,
        IReadOnlyList<(double X, double Y)> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            AssertClose(expected[index].X, actual[index].X, ScalarTolerance);
            AssertClose(expected[index].Y, actual[index].Y, ScalarTolerance);
        }
    }

    private static string PythonJsonWithToroidalGeometry(string conicYz, string coeffsPolyY)
    {
        return PythonJsonWithSurfaceGeometry($$"""
        {
          "type": "ToroidalGeometry",
          "cs": {
            "x": 0.0,
            "y": 0.0,
            "z": 0.0,
            "rx": 0.0,
            "ry": 0.0,
            "rz": 0.0,
            "reference_cs": null
          },
          "radius": 30.0,
          "conic": 0.0,
          "radius_x": 80.0,
          "radius_y": 30.0,
          "conic_yz": {{conicYz}},
          "coeffs_poly_y": {{coeffsPolyY}}
        }
        """);
    }

    private static string PythonJsonWithPolynomialGeometry(string radius)
    {
        return PythonJsonWithSurfaceGeometry($$"""
        {
          "type": "PolynomialGeometry",
          "cs": {
            "x": 0.0,
            "y": 0.0,
            "z": 0.0,
            "rx": 0.0,
            "ry": 0.0,
            "rz": 0.0,
            "reference_cs": null
          },
          "radius": {{radius}},
          "conic": 0.0,
          "tol": 1e-10,
          "max_iter": 100,
          "coefficients": [
            [0.0, 0.0, 0.001],
            [0.0, 0.0002],
            [],
            [-0.000003]
          ]
        }
        """);
    }

    private static string PythonJsonWithChebyshevGeometry(string radius)
    {
        return PythonJsonWithSurfaceGeometry($$"""
        {
          "type": "ChebyshevPolynomialGeometry",
          "cs": {
            "x": 0.0,
            "y": 0.0,
            "z": 0.0,
            "rx": 0.0,
            "ry": 0.0,
            "rz": 0.0,
            "reference_cs": null
          },
          "radius": {{radius}},
          "conic": 0.0,
          "tol": 1e-10,
          "max_iter": 100,
          "coefficients": [
            [0.0, 0.0, 0.001],
            [0.0, 0.0002],
            [],
            [-0.000003]
          ],
          "norm_x": 5.0,
          "norm_y": 7.0
        }
        """);
    }

    private static string PythonJsonWithZernikeGeometry(string radius, string zernikeType)
    {
        return PythonJsonWithSurfaceGeometry($$"""
        {
          "type": "ZernikePolynomialGeometry",
          "cs": {
            "x": 0.0,
            "y": 0.0,
            "z": 0.0,
            "rx": 0.0,
            "ry": 0.0,
            "rz": 0.0,
            "reference_cs": null
          },
          "radius": {{radius}},
          "conic": 0.0,
          "tol": 1e-10,
          "max_iter": 100,
          "coefficients": [
            0.001,
            0.0,
            0.0002,
            -0.000003,
            0.0,
            0.0,
            0.0000004
          ],
          "zernike_type": "{{zernikeType}}",
          "norm_radius": 6.0
        }
        """);
    }

    private static string PythonPlaneGeometry()
    {
        return """
        {
          "type": "Plane",
          "cs": {
            "x": 0.0,
            "y": 0.0,
            "z": 0.0,
            "rx": 0.0,
            "ry": 0.0,
            "rz": 0.0,
            "reference_cs": null
          },
          "radius": Infinity
        }
        """;
    }

    private static string PythonThinLensInteraction(string focalLength, string isReflective)
    {
        return $$"""
        {
          "type": "ThinLensInteractionModel",
          "is_reflective": {{isReflective}},
          "coating": null,
          "bsdf": null,
          "focal_length": {{focalLength}}
        }
        """;
    }

    private static string PythonRefractiveReflectiveInteraction()
    {
        return """
        {
          "type": "RefractiveReflectiveModel",
          "is_reflective": false,
          "coating": null,
          "bsdf": null
        }
        """;
    }

    private static string PythonJsonWithSurfaceGeometry(
        string geometryJson,
        string? interactionJson = null,
        string? apertureJson = null,
        string systemApertureType = "EPD",
        string apertureObjectSpaceTelecentric = "false",
        string fieldsTelecentric = "false",
        string fieldsObjectSpaceTelecentric = "false",
        string fieldType = "AngleField",
        string wavelengthPolarizationJson = "\"ignore\"",
        string apodizationJson = "null",
        string pickupsJson = "[]",
        string? materialPostJson = null,
        string solvesJson = """
        {
          "solves": []
        }
        """)
    {
        interactionJson ??= PythonRefractiveReflectiveInteraction();
        apertureJson ??= "null";
        materialPostJson ??= """
        {
          "type": "IdealMaterial",
          "index": 1.0,
          "absorp": 0.0
        }
        """;
        return $$"""
        {
          "version": 1.0,
          "aperture": {
            "type": "{{systemApertureType}}",
            "value": 1.0,
            "object_space_telecentric": {{apertureObjectSpaceTelecentric}}
          },
          "fields": {
            "fields": [
              {
                "x": 0.0,
                "y": 0.0,
                "vx": 0.0,
                "vy": 0.0
              }
            ],
            "telecentric": {{fieldsTelecentric}},
            "field_definition": {
              "field_type": "{{fieldType}}"
            },
            "object_space_telecentric": {{fieldsObjectSpaceTelecentric}}
          },
          "wavelengths": {
            "wavelengths": [
              {
                "value": 0.5875618,
                "is_primary": true,
                "unit": "um",
                "weight": 1.0
              }
            ],
            "polarization": {{wavelengthPolarizationJson}}
          },
          "apodization": {{apodizationJson}},
          "pickups": {{pickupsJson}},
          "solves": {{solvesJson}},
          "surface_group": {
            "surfaces": [
              {
                "type": "ObjectSurface",
                "geometry": {
                  "type": "Plane",
                  "cs": {
                    "x": 0.0,
                    "y": 0.0,
                    "z": 0.0,
                    "rx": 0.0,
                    "ry": 0.0,
                    "rz": 0.0,
                    "reference_cs": null
                  },
                  "radius": 0.0
                },
                "material_post": {
                  "type": "IdealMaterial",
                  "index": 1.0,
                  "absorp": 0.0
                },
                "comment": ""
              },
              {
                "type": "Surface",
                "thickness": 1.0,
                "geometry": {{geometryJson}},
                "material_post": {{materialPostJson}},
                "is_stop": false,
                "aperture": {{apertureJson}},
                "interaction_model": {{interactionJson}},
                "comment": ""
              }
            ]
          }
        }
        """;
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
