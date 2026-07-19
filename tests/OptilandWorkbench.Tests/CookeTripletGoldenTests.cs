using System.Text.Json;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Phase;
using OptilandWorkbench.Core.Propagation;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Raytrace;
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

    [Fact]
    public void PythonJsonAdapterMatchesApodizationReference()
    {
        using var reference = LoadApodizationReference();
        Assert.Equal("0.5.8", reference.RootElement.GetProperty("optiland_version").GetString());

        foreach (var apodizationCase in reference.RootElement.GetProperty("apodizations").EnumerateArray())
        {
            var expectedDictionary = apodizationCase.GetProperty("dictionary");
            var json = PythonJsonWithSurfaceGeometry(
                PythonPlaneGeometry(),
                apodizationJson: expectedDictionary.GetRawText());
            var optic = PythonOptilandJsonStore.Deserialize(json);
            var apodization = Assert.IsAssignableFrom<IApodizationModel>(optic.Apodization);

            foreach (var sample in apodizationCase.GetProperty("samples").EnumerateArray())
            {
                AssertClose(
                    sample.GetProperty("intensity").GetDouble(),
                    apodization.Intensity(
                        sample.GetProperty("x").GetDouble(),
                        sample.GetProperty("y").GetDouble()),
                    ScalarTolerance);
            }

            var strictExport = PythonOptilandJsonStore.Serialize(optic)
                .Replace("-Infinity", "-1e308", StringComparison.Ordinal)
                .Replace("Infinity", "1e308", StringComparison.Ordinal);
            using var exported = JsonDocument.Parse(strictExport);
            AssertJsonObjectEquivalent(
                expectedDictionary,
                exported.RootElement.GetProperty("apodization"));
        }
    }

    [Fact]
    public void NativeSnapshotRoundTripsApodizationReference()
    {
        using var reference = LoadApodizationReference();
        foreach (var apodizationCase in reference.RootElement.GetProperty("apodizations").EnumerateArray())
        {
            var optic = PythonOptilandJsonStore.Deserialize(PythonJsonWithSurfaceGeometry(
                PythonPlaneGeometry(),
                apodizationJson: apodizationCase.GetProperty("dictionary").GetRawText()));
            var expected = Assert.IsAssignableFrom<IApodizationModel>(optic.Apodization);

            var restored = Optic.FromSnapshot(optic.ToSnapshot());

            AssertApodizationEquivalent(expected, restored.Apodization);
        }
    }

    [Fact]
    public void RayGeneratorAppliesOpticApodizationToEveryEntryPoint()
    {
        var optic = Optic.CreateTessarLens();
        optic.Apodization = new GaussianApodization(0.6);
        var generator = optic.SequentialRayTracer.RayGenerator;
        var wavelength = optic.Wavelengths.First(item => item.IsPrimary);

        var generic = generator.GenerateGeneric(0, 0, 0.5, 0.25, wavelength.Micrometers);
        AssertClose(
            optic.Apodization.Intensity(0.5, 0.25),
            Assert.Single(generic.Rays).Intensity,
            ScalarTolerance);

        var weightedSamples = new[]
        {
            new PupilSample(0, 0, 0.4),
            new PupilSample(-0.4, 0.4, 0.7)
        };
        var normalized = generator.GenerateNormalizedPupilSamples(0, 0, wavelength.Micrometers, weightedSamples);
        for (var index = 0; index < weightedSamples.Length; index++)
        {
            var sample = weightedSamples[index];
            AssertClose(
                sample.Weight * optic.Apodization.Intensity(sample.X, sample.Y),
                normalized.Rays[index].Intensity,
                ScalarTolerance);
        }

        generator.Settings.SamplesPerField = 5;
        generator.Settings.Sampling = PupilSampling.LineX;
        var expectedSamples = ApertureSampler.Generate(5, PupilSampling.LineX);
        var generated = generator.GenerateFor(
            new[] { optic.Fields[0] },
            new[] { wavelength },
            applyFieldWeight: false,
            applyWavelengthWeight: false);
        Assert.Equal(expectedSamples.Count, generated.Rays.Count);
        for (var index = 0; index < expectedSamples.Count; index++)
        {
            var sample = expectedSamples[index];
            AssertClose(
                sample.Weight * optic.Apodization.Intensity(sample.X, sample.Y),
                generated.Rays[index].Intensity,
                ScalarTolerance);
        }

        optic.Apodization = new HannApodization(2);
        var zeroIntensityTrace = optic.TraceGeneric(0, 0, 0, 0, wavelength.Micrometers);
        var history = Assert.Single(zeroIntensityTrace.RayHistories);
        Assert.Equal(optic.SurfaceGroup.Items.Count, history.Count);
        Assert.All(history, sample => Assert.Equal(0, sample.Intensity, precision: 12));
    }

    [Fact]
    public void PythonPhaseProfilesMatchReference()
    {
        using var reference = LoadPhaseReference();
        Assert.Equal("0.5.8", reference.RootElement.GetProperty("optiland_version").GetString());
        foreach (var profileCase in reference.RootElement.GetProperty("profiles").EnumerateArray())
        {
            var expectedDictionary = profileCase.GetProperty("dictionary");
            var optic = PythonOptilandJsonStore.Deserialize(PythonJsonWithSurfaceGeometry(
                PythonPlaneGeometry(),
                interactionJson: PythonPhaseInteraction(expectedDictionary.GetRawText(), false)));
            var interaction = Assert.IsType<PhaseInteractionModel>(optic.SurfaceGroup.Items[1].InteractionModel);

            AssertPhaseSamples(profileCase, interaction.Profile);

            var strictExport = PythonOptilandJsonStore.Serialize(optic)
                .Replace("-Infinity", "-1e308", StringComparison.Ordinal)
                .Replace("Infinity", "1e308", StringComparison.Ordinal);
            using var exported = JsonDocument.Parse(strictExport);
            var actualDictionary = exported.RootElement
                .GetProperty("surface_group")
                .GetProperty("surfaces")[1]
                .GetProperty("interaction_model")
                .GetProperty("phase_profile");
            AssertJsonObjectEquivalent(expectedDictionary, actualDictionary);
        }

        var unsupportedProfile = Assert.Throws<NotSupportedException>(() =>
            PythonOptilandJsonStore.Deserialize(PythonJsonWithSurfaceGeometry(
                PythonPlaneGeometry(),
                interactionJson: PythonPhaseInteraction("""{ "phase_type": "unsupported" }""", false))));
        Assert.Contains("phase profile", unsupportedProfile.Message, StringComparison.OrdinalIgnoreCase);

        var nonPlane = Assert.Throws<NotSupportedException>(() =>
            PythonOptilandJsonStore.Deserialize(PythonJsonWithSurfaceGeometry(
                PythonStandardGeometry(),
                interactionJson: PythonPhaseInteraction("""{ "phase_type": "constant" }""", false))));
        Assert.Contains("Plane", nonPlane.Message, StringComparison.OrdinalIgnoreCase);

        var workbenchOnly = Optic.CreateTessarLens();
        workbenchOnly.SurfaceGroup.Items[1].Geometry = new PlaneGeometry();
        workbenchOnly.SurfaceGroup.Items[1].InteractionModel = new PhaseInteractionModel(
            new PolynomialPhaseProfile(new Dictionary<(int X, int Y), double> { [(1, 0)] = 1 }));
        var unsupportedExport = Assert.Throws<NotSupportedException>(() =>
            PythonOptilandJsonStore.Serialize(workbenchOnly));
        Assert.Contains("phase profile", unsupportedExport.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PythonPhaseInteractionsMatchReferenceRayForRay()
    {
        using var reference = LoadPhaseReference();
        foreach (var interactionCase in reference.RootElement.GetProperty("interactions").EnumerateArray())
        {
            var dictionary = interactionCase.GetProperty("dictionary");
            var optic = PythonOptilandJsonStore.Deserialize(PythonJsonWithSurfaceGeometry(
                PythonPlaneGeometry(),
                interactionJson: dictionary.GetRawText()));
            var interaction = Assert.IsType<PhaseInteractionModel>(optic.SurfaceGroup.Items[1].InteractionModel);

            foreach (var sample in interactionCase.GetProperty("samples").EnumerateArray())
            {
                var ray = new RealRay(
                    new Vector3D(sample.GetProperty("x").GetDouble(), sample.GetProperty("y").GetDouble(), 0),
                    new Vector3D(
                        sample.GetProperty("direction_x").GetDouble(),
                        sample.GetProperty("direction_y").GetDouble(),
                        sample.GetProperty("direction_z").GetDouble()),
                    sample.GetProperty("wavelength_micrometers").GetDouble() * 1000,
                    sample.GetProperty("input_intensity").GetDouble());
                var actual = interaction.Interact(ray, new SurfaceInteractionContext(
                    new Vector3D(0, 0, 1),
                    1,
                    1.5,
                    ray.WavelengthNanometers,
                    interaction.IsReflective));

                AssertPythonFloat(sample.GetProperty("output_direction_x"), actual.Direction.X);
                AssertPythonFloat(sample.GetProperty("output_direction_y"), actual.Direction.Y);
                AssertPythonFloat(sample.GetProperty("output_direction_z"), actual.Direction.Z);
                AssertClose(sample.GetProperty("output_intensity").GetDouble(), actual.Intensity, TraceTolerance);
                AssertClose(sample.GetProperty("opd").GetDouble(), actual.OpticalPathDifference, TraceTolerance);
            }
        }
    }

    [Fact]
    public void NativeSnapshotRoundTripsPhaseProfiles()
    {
        using var reference = LoadPhaseReference();
        foreach (var interactionCase in reference.RootElement.GetProperty("interactions").EnumerateArray())
        {
            var optic = PythonOptilandJsonStore.Deserialize(PythonJsonWithSurfaceGeometry(
                PythonPlaneGeometry(),
                interactionJson: interactionCase.GetProperty("dictionary").GetRawText()));
            var expected = Assert.IsType<PhaseInteractionModel>(optic.SurfaceGroup.Items[1].InteractionModel);

            var restored = Optic.FromSnapshot(optic.ToSnapshot());
            var actual = Assert.IsType<PhaseInteractionModel>(restored.SurfaceGroup.Items[1].InteractionModel);

            Assert.Equal(expected.IsReflective, actual.IsReflective);
            var profileCase = Assert.Single(
                reference.RootElement.GetProperty("profiles").EnumerateArray(),
                item => item.GetProperty("dictionary").GetProperty("phase_type").GetString() == actual.Profile.Kind);
            AssertPhaseSamples(profileCase, actual.Profile);
        }
    }

    [Fact]
    public void PhaseInteractionUsesSurfaceLocalCoordinates()
    {
        var surface = new OpticalSurface
        {
            Geometry = new PlaneGeometry(),
            MaterialAfter = new AirMaterial(),
            InteractionModel = new PhaseInteractionModel(new LinearGratingPhaseProfile(2 * Math.PI)),
            CoordinateSystem = new CoordinateSystem(
                Vector3D.Zero,
                RotationZDegrees: 90)
        };
        var ray = new RealRay(new Vector3D(0, 0, -1), new Vector3D(0, 0, 1), 1000);

        var result = surface.TraceRay(ray, new AirMaterial(), new AirMaterial(), 0, 0);

        AssertClose(0, result.Ray.Direction.X, TraceTolerance);
        Assert.True(result.Ray.Direction.Y > 0.15);
        Assert.True(result.Ray.Direction.Z > 0.9);
    }

    [Fact]
    public void PythonThinLensInteractionsMatchReferenceRayForRay()
    {
        using var reference = LoadThinLensReference();
        Assert.Equal("0.5.8", reference.RootElement.GetProperty("optiland_version").GetString());
        foreach (var thinLensCase in reference.RootElement.GetProperty("cases").EnumerateArray())
        {
            var interaction = new ThinLensInteractionModel(
                thinLensCase.GetProperty("focal_length_millimeters").GetDouble(),
                thinLensCase.GetProperty("is_reflective").GetBoolean());
            var imported = PythonOptilandJsonStore.Deserialize(PythonJsonWithSurfaceGeometry(
                PythonPlaneGeometry(),
                interactionJson: thinLensCase.GetProperty("dictionary").GetRawText(),
                materialPostJson: $$"""
                {
                  "type": "IdealMaterial",
                  "index": {{thinLensCase.GetProperty("material_index_after").GetRawText()}},
                  "absorp": 0.0
                }
                """));
            var importedInteraction = Assert.IsType<ThinLensInteractionModel>(
                imported.SurfaceGroup.Items[1].InteractionModel);
            Assert.Equal(interaction.IsReflective, importedInteraction.IsReflective);
            AssertClose(interaction.FocalLength, importedInteraction.FocalLength, ScalarTolerance);
            var nativeRestored = Optic.FromSnapshot(imported.ToSnapshot());
            var nativeInteraction = Assert.IsType<ThinLensInteractionModel>(
                nativeRestored.SurfaceGroup.Items[1].InteractionModel);
            Assert.Equal(interaction.IsReflective, nativeInteraction.IsReflective);
            AssertClose(interaction.FocalLength, nativeInteraction.FocalLength, ScalarTolerance);
            var strictExport = PythonOptilandJsonStore.Serialize(imported)
                .Replace("-Infinity", "-1e308", StringComparison.Ordinal)
                .Replace("Infinity", "1e308", StringComparison.Ordinal);
            using var exported = JsonDocument.Parse(strictExport);
            AssertJsonObjectEquivalent(
                thinLensCase.GetProperty("dictionary"),
                exported.RootElement.GetProperty("surface_group").GetProperty("surfaces")[1]
                    .GetProperty("interaction_model"));
            var propagationDistance = thinLensCase
                .GetProperty("propagation_distance_millimeters")
                .GetDouble();

            foreach (var sample in thinLensCase.GetProperty("real_samples").EnumerateArray())
            {
                var ray = ThinLensRay(sample);
                var indexBefore = sample.GetProperty("refractive_index_before").GetDouble();
                var indexAfter = sample.GetProperty("refractive_index_after").GetDouble();
                var actual = interaction.Interact(ray, new SurfaceInteractionContext(
                    new Vector3D(0, 0, 1),
                    indexBefore,
                    indexAfter,
                    ray.WavelengthNanometers,
                    interaction.IsReflective,
                    new PlaneGeometry()));

                AssertClose(sample.GetProperty("thin_direction_x").GetDouble(), actual.Direction.X, TraceTolerance);
                AssertClose(sample.GetProperty("thin_direction_y").GetDouble(), actual.Direction.Y, TraceTolerance);
                AssertClose(sample.GetProperty("thin_direction_z").GetDouble(), actual.Direction.Z, TraceTolerance);
                AssertClose(sample.GetProperty("thin_opd").GetDouble(), actual.OpticalPathDifference, TraceTolerance);
                Assert.Equal(sample.GetProperty("thin_is_normalized").GetBoolean(), actual.IsNormalized);
                AssertClose(sample.GetProperty("input_intensity").GetDouble(), actual.Intensity, TraceTolerance);

                var propagated = new HomogeneousPropagationModel().Propagate(actual, propagationDistance) with
                {
                    OpticalPathDifference = actual.OpticalPathDifference + (propagationDistance * indexAfter)
                };
                AssertClose(sample.GetProperty("propagated_x").GetDouble(), propagated.Origin.X, TraceTolerance);
                AssertClose(sample.GetProperty("propagated_y").GetDouble(), propagated.Origin.Y, TraceTolerance);
                AssertClose(sample.GetProperty("propagated_z").GetDouble(), propagated.Origin.Z, TraceTolerance);
                AssertClose(sample.GetProperty("propagated_direction_x").GetDouble(), propagated.Direction.X, TraceTolerance);
                AssertClose(sample.GetProperty("propagated_direction_y").GetDouble(), propagated.Direction.Y, TraceTolerance);
                AssertClose(sample.GetProperty("propagated_direction_z").GetDouble(), propagated.Direction.Z, TraceTolerance);
                AssertClose(sample.GetProperty("propagated_opd").GetDouble(), propagated.OpticalPathDifference, TraceTolerance);
                Assert.True(propagated.IsNormalized);
            }

            foreach (var sample in thinLensCase.GetProperty("paraxial_samples").EnumerateArray())
            {
                var wavelengthNanometers = sample.GetProperty("wavelength_micrometers").GetDouble() * 1000;
                var actual = interaction.Interact(
                    new ParaxialRay(
                        sample.GetProperty("height").GetDouble(),
                        sample.GetProperty("slope").GetDouble(),
                        0,
                        wavelengthNanometers),
                    new SurfaceInteractionContext(
                        new Vector3D(0, 0, 1),
                        sample.GetProperty("refractive_index_before").GetDouble(),
                        sample.GetProperty("refractive_index_after").GetDouble(),
                        wavelengthNanometers,
                        interaction.IsReflective,
                        new PlaneGeometry()));
                AssertClose(sample.GetProperty("output_slope").GetDouble(), actual.Angle, TraceTolerance);
            }
        }
    }

    [Fact]
    public void ThinLensSlopeStatePropagatesAcrossSurfacesLikePython()
    {
        using var reference = LoadThinLensReference();
        foreach (var thinLensCase in reference.RootElement.GetProperty("cases").EnumerateArray())
        {
            var sample = thinLensCase.GetProperty("real_samples")[1];
            var indexBefore = sample.GetProperty("refractive_index_before").GetDouble();
            var indexAfter = sample.GetProperty("refractive_index_after").GetDouble();
            var distance = thinLensCase.GetProperty("propagation_distance_millimeters").GetDouble();
            var beforeMaterial = new ConstantIndexMaterial("Before", indexBefore);
            var afterMaterial = new ConstantIndexMaterial("After", indexAfter);
            var thinSurface = new OpticalSurface
            {
                Geometry = new PlaneGeometry(),
                MaterialAfter = afterMaterial,
                InteractionModel = new ThinLensInteractionModel(
                    thinLensCase.GetProperty("focal_length_millimeters").GetDouble(),
                    thinLensCase.GetProperty("is_reflective").GetBoolean()),
                CoordinateSystem = new CoordinateSystem(Vector3D.Zero)
            };
            var nextSurface = new OpticalSurface
            {
                Geometry = new PlaneGeometry(),
                MaterialAfter = afterMaterial,
                InteractionModel = new RefractiveReflectiveInteractionModel(),
                CoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, distance))
            };

            var thinResult = thinSurface.TraceRay(ThinLensRay(sample), beforeMaterial, afterMaterial, 0, 0);
            Assert.False(thinResult.Ray.IsNormalized);
            var nextResult = nextSurface.TraceRay(
                thinResult.Ray,
                afterMaterial,
                afterMaterial,
                thinResult.CumulativePathLength,
                thinResult.CumulativeOpticalPathLength);

            AssertClose(sample.GetProperty("propagated_x").GetDouble(), nextResult.Ray.Origin.X, TraceTolerance);
            AssertClose(sample.GetProperty("propagated_y").GetDouble(), nextResult.Ray.Origin.Y, TraceTolerance);
            AssertClose(sample.GetProperty("propagated_z").GetDouble(), nextResult.Ray.Origin.Z, TraceTolerance);
            AssertClose(sample.GetProperty("propagated_direction_x").GetDouble(), nextResult.Ray.Direction.X, TraceTolerance);
            AssertClose(sample.GetProperty("propagated_direction_y").GetDouble(), nextResult.Ray.Direction.Y, TraceTolerance);
            AssertClose(sample.GetProperty("propagated_direction_z").GetDouble(), nextResult.Ray.Direction.Z, TraceTolerance);
            AssertClose(sample.GetProperty("propagated_opd").GetDouble(), nextResult.Ray.OpticalPathDifference, TraceTolerance);
            Assert.True(nextResult.Ray.IsNormalized);
        }
    }

    [Fact]
    public void PythonDiffractiveInteractionsMatchReferenceRayForRay()
    {
        using var reference = LoadDiffractiveReference();
        Assert.Equal("0.5.8", reference.RootElement.GetProperty("optiland_version").GetString());
        foreach (var diffractionCase in reference.RootElement.GetProperty("cases").EnumerateArray())
        {
            var geometry = CreateGratingGeometry(diffractionCase);
            var interaction = new DiffractiveInteractionModel(
                diffractionCase.GetProperty("is_reflective").GetBoolean());
            var indexBefore = diffractionCase.GetProperty("refractive_index_before").GetDouble();
            var indexAfter = diffractionCase.GetProperty("refractive_index_after").GetDouble();

            foreach (var sample in diffractionCase.GetProperty("real_samples").EnumerateArray())
            {
                var origin = new Vector3D(
                    sample.GetProperty("x").GetDouble(),
                    sample.GetProperty("y").GetDouble(),
                    sample.GetProperty("z").GetDouble());
                var gratingVector = geometry.GratingVector(origin);
                AssertClose(sample.GetProperty("grating_vector_x").GetDouble(), gratingVector.X, TraceTolerance);
                AssertClose(sample.GetProperty("grating_vector_y").GetDouble(), gratingVector.Y, TraceTolerance);
                AssertClose(sample.GetProperty("grating_vector_z").GetDouble(), gratingVector.Z, TraceTolerance);

                var ray = new RealRay(
                    origin,
                    new Vector3D(
                        sample.GetProperty("direction_x").GetDouble(),
                        sample.GetProperty("direction_y").GetDouble(),
                        sample.GetProperty("direction_z").GetDouble()),
                    sample.GetProperty("wavelength_micrometers").GetDouble() * 1000);
                var actual = interaction.Interact(ray, new SurfaceInteractionContext(
                    geometry.SurfaceNormal(origin),
                    indexBefore,
                    indexAfter,
                    ray.WavelengthNanometers,
                    interaction.IsReflective,
                    geometry));

                AssertPythonFloat(sample.GetProperty("output_direction_x"), actual.Direction.X);
                AssertPythonFloat(sample.GetProperty("output_direction_y"), actual.Direction.Y);
                AssertPythonFloat(sample.GetProperty("output_direction_z"), actual.Direction.Z);
            }

            foreach (var sample in diffractionCase.GetProperty("paraxial_samples").EnumerateArray())
            {
                var wavelengthNanometers = sample.GetProperty("wavelength_micrometers").GetDouble() * 1000;
                var ray = new ParaxialRay(
                    sample.GetProperty("height").GetDouble(),
                    sample.GetProperty("slope").GetDouble(),
                    0,
                    wavelengthNanometers);
                var actual = interaction.Interact(ray, new SurfaceInteractionContext(
                    new Vector3D(0, 0, 1),
                    indexBefore,
                    indexAfter,
                    wavelengthNanometers,
                    interaction.IsReflective,
                    geometry));
                AssertClose(sample.GetProperty("output_slope").GetDouble(), actual.Angle, TraceTolerance);
            }
        }
    }

    [Fact]
    public void PythonJsonRoundTripsDiffractiveGratingSubset()
    {
        using var reference = LoadDiffractiveReference();
        foreach (var diffractionCase in reference.RootElement.GetProperty("cases").EnumerateArray())
        {
            var optic = PythonOptilandJsonStore.Deserialize(PythonJsonWithSurfaceGeometry(
                PythonGratingGeometry(diffractionCase),
                interactionJson: diffractionCase.GetProperty("interaction_dictionary").GetRawText(),
                materialPostJson: $$"""
                {
                  "type": "IdealMaterial",
                  "index": {{diffractionCase.GetProperty("refractive_index_after").GetRawText()}},
                  "absorp": 0.0
                }
                """));
            var surface = optic.SurfaceGroup.Items[1];
            var geometry = Assert.IsAssignableFrom<IGratingGeometry>(surface.Geometry);
            Assert.Equal(diffractionCase.GetProperty("order").GetInt32(), geometry.GratingOrder);
            AssertPythonDouble(
                diffractionCase.GetProperty("period_micrometers"),
                geometry.GratingPeriodMicrometers,
                ScalarTolerance);
            AssertClose(
                diffractionCase.GetProperty("groove_orientation_angle_radians").GetDouble(),
                geometry.GrooveOrientationAngleRadians,
                ScalarTolerance);
            Assert.Equal(
                diffractionCase.GetProperty("is_reflective").GetBoolean(),
                Assert.IsType<DiffractiveInteractionModel>(surface.InteractionModel).IsReflective);

            var strictExport = PythonOptilandJsonStore.Serialize(optic)
                .Replace("-Infinity", "-1e308", StringComparison.Ordinal)
                .Replace("Infinity", "1e308", StringComparison.Ordinal);
            using var exported = JsonDocument.Parse(strictExport);
            var exportedSurface = exported.RootElement.GetProperty("surface_group").GetProperty("surfaces")[1];
            var exportedGeometry = exportedSurface.GetProperty("geometry");
            Assert.Equal(geometry.GratingOrder, exportedGeometry.GetProperty("order").GetInt32());
            if (double.IsPositiveInfinity(geometry.GratingPeriodMicrometers))
            {
                Assert.Equal(1e308, exportedGeometry.GetProperty("period").GetDouble());
            }
            else
            {
                AssertClose(geometry.GratingPeriodMicrometers, exportedGeometry.GetProperty("period").GetDouble(), ScalarTolerance);
            }
            AssertClose(geometry.GrooveOrientationAngleRadians, exportedGeometry.GetProperty("angle").GetDouble(), ScalarTolerance);
            AssertJsonObjectEquivalent(
                diffractionCase.GetProperty("interaction_dictionary"),
                exportedSurface.GetProperty("interaction_model"));
        }

        var planeCase = reference.RootElement.GetProperty("cases")[0];
        var missingPlaneData = Assert.Throws<NotSupportedException>(() =>
            PythonOptilandJsonStore.Deserialize(PythonJsonWithSurfaceGeometry(
                planeCase.GetProperty("geometry_dictionary").GetRawText(),
                interactionJson: planeCase.GetProperty("interaction_dictionary").GetRawText())));
        Assert.Contains("order/period/angle", missingPlaneData.Message, StringComparison.OrdinalIgnoreCase);

        var invalidPeriodGeometry = PythonGratingGeometry(planeCase)
            .Replace("\"period\": 1.2", "\"period\": 0", StringComparison.Ordinal);
        var invalidPeriod = Assert.Throws<InvalidDataException>(() =>
            PythonOptilandJsonStore.Deserialize(PythonJsonWithSurfaceGeometry(
                invalidPeriodGeometry,
                interactionJson: PythonDiffractiveInteraction(false))));
        Assert.Contains("period", invalidPeriod.Message, StringComparison.OrdinalIgnoreCase);

        var invalidOrderGeometry = PythonGratingGeometry(planeCase)
            .Replace("\"order\": 1", "\"order\": 1.5", StringComparison.Ordinal);
        var invalidOrder = Assert.Throws<InvalidDataException>(() =>
            PythonOptilandJsonStore.Deserialize(PythonJsonWithSurfaceGeometry(
                invalidOrderGeometry,
                interactionJson: PythonDiffractiveInteraction(false))));
        Assert.Contains("integer", invalidOrder.Message, StringComparison.OrdinalIgnoreCase);

        var nonGrating = Assert.Throws<NotSupportedException>(() =>
            PythonOptilandJsonStore.Deserialize(PythonJsonWithSurfaceGeometry(
                PythonPlaneGeometry(),
                interactionJson: PythonDiffractiveInteraction(false))));
        Assert.Contains("grating geometry", nonGrating.Message, StringComparison.OrdinalIgnoreCase);

        var nonDiffractive = Assert.Throws<NotSupportedException>(() =>
            PythonOptilandJsonStore.Deserialize(PythonJsonWithSurfaceGeometry(
                PythonGratingGeometry(planeCase),
                interactionJson: PythonRefractiveReflectiveInteraction())));
        Assert.Contains("DiffractiveInteractionModel", nonDiffractive.Message, StringComparison.OrdinalIgnoreCase);

        var objectGrating = Optic.CreateTessarLens();
        objectGrating.SurfaceGroup.Items[0].Geometry = new PlaneGratingGeometry(1, 1, 0);
        objectGrating.SurfaceGroup.Items[0].InteractionModel = new DiffractiveInteractionModel();
        var objectExport = Assert.Throws<NotSupportedException>(() =>
            PythonOptilandJsonStore.Serialize(objectGrating));
        Assert.Contains("object surfaces", objectExport.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NativeSnapshotRoundTripsDiffractiveGratingComponents()
    {
        using var reference = LoadDiffractiveReference();
        foreach (var diffractionCase in reference.RootElement.GetProperty("cases").EnumerateArray())
        {
            var optic = Optic.CreateTessarLens();
            var surface = optic.SurfaceGroup.Items[1];
            surface.Geometry = CreateGratingGeometry(diffractionCase);
            surface.InteractionModel = new DiffractiveInteractionModel(
                diffractionCase.GetProperty("is_reflective").GetBoolean());

            var restored = Optic.FromSnapshot(optic.ToSnapshot()).SurfaceGroup.Items[1];
            var geometry = Assert.IsAssignableFrom<IGratingGeometry>(restored.Geometry);
            Assert.Equal(surface.Geometry.GetType(), restored.Geometry.GetType());
            Assert.Equal(diffractionCase.GetProperty("order").GetInt32(), geometry.GratingOrder);
            AssertPythonDouble(
                diffractionCase.GetProperty("period_micrometers"),
                geometry.GratingPeriodMicrometers,
                ScalarTolerance);
            AssertClose(
                diffractionCase.GetProperty("groove_orientation_angle_radians").GetDouble(),
                geometry.GrooveOrientationAngleRadians,
                ScalarTolerance);
            Assert.Equal(
                diffractionCase.GetProperty("is_reflective").GetBoolean(),
                Assert.IsType<DiffractiveInteractionModel>(restored.InteractionModel).IsReflective);
        }

        var legacy = ComponentSnapshotFactory.ToInteraction(
            new ComponentSnapshot(
                "diffractive",
                new Dictionary<string, double> { ["grooveFrequency"] = 1200, ["order"] = 2 },
                new Dictionary<string, string>()),
            false);
        var legacyDiffractive = Assert.IsType<DiffractiveInteractionModel>(legacy);
        Assert.Equal(1200, legacyDiffractive.GrooveFrequencyLinesPerMillimeter);
        Assert.Equal(2, legacyDiffractive.Order);
        var legacyRay = new RealRay(Vector3D.Zero, new Vector3D(0, 0, 1), 500);
        var legacyOutput = legacyDiffractive.Interact(legacyRay, new SurfaceInteractionContext(
            new Vector3D(0, 0, 1),
            1,
            1,
            500,
            false));
        var legacyDelta = 2 * 500e-6 * 1200;
        var legacyLength = Math.Sqrt(1 + (legacyDelta * legacyDelta));
        AssertClose(legacyDelta / legacyLength, legacyOutput.Direction.X, TraceTolerance);
        AssertClose(0, legacyOutput.Direction.Y, TraceTolerance);
        AssertClose(1 / legacyLength, legacyOutput.Direction.Z, TraceTolerance);
    }

    [Fact]
    public void DiffractiveInteractionUsesSurfaceLocalCoordinates()
    {
        var surface = new OpticalSurface
        {
            Geometry = new PlaneGratingGeometry(1, 2, -Math.PI / 2),
            MaterialAfter = new AirMaterial(),
            InteractionModel = new DiffractiveInteractionModel(),
            CoordinateSystem = new CoordinateSystem(Vector3D.Zero, RotationZDegrees: 90)
        };
        var ray = new RealRay(new Vector3D(0, 0, -1), new Vector3D(0, 0, 1), 1000);

        var result = surface.TraceRay(ray, new AirMaterial(), new AirMaterial(), 0, 0);

        AssertClose(0, result.Ray.Direction.X, TraceTolerance);
        Assert.True(result.Ray.Direction.Y > 0.4);
        Assert.True(result.Ray.Direction.Z > 0.8);
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
    [InlineData(ApertureKind.EntrancePupilDiameter, "EPD")]
    [InlineData(ApertureKind.FNumber, "imageFNO")]
    [InlineData(ApertureKind.NumericalAperture, "objectNA")]
    [InlineData(ApertureKind.FloatByStopSize, "float_by_stop_size")]
    public void PythonJsonRoundTripsSupportedSystemApertureTypes(
        ApertureKind apertureKind,
        string expectedType)
    {
        var optic = Optic.CreateCookeTriplet();
        optic.Aperture.Kind = apertureKind;
        optic.Aperture.Value = 5.6;
        var stop = optic.SurfaceGroup.Items.Single(surface => surface.IsStop);
        stop.SemiDiameter = 3.25;

        var json = PythonOptilandJsonStore.Serialize(optic);
        var strictJson = json
            .Replace("-Infinity", "-1e308", StringComparison.Ordinal)
            .Replace("Infinity", "1e308", StringComparison.Ordinal);
        using var exported = JsonDocument.Parse(strictJson);
        var aperture = exported.RootElement.GetProperty("aperture");
        Assert.Equal(expectedType, aperture.GetProperty("type").GetString());
        Assert.Equal(
            apertureKind == ApertureKind.FloatByStopSize ? 3.25 : 5.6,
            aperture.GetProperty("value").GetDouble(),
            precision: 12);

        var restored = PythonOptilandJsonStore.Deserialize(json);
        Assert.Equal(apertureKind, restored.Aperture.Kind);
        if (apertureKind == ApertureKind.FloatByStopSize)
        {
            var restoredStop = restored.SurfaceGroup.Items.Single(surface => surface.IsStop);
            Assert.Equal(3.25, restored.Aperture.Value, precision: 12);
            Assert.Equal(6.5, restored.Paraxial.EstimateEntrancePupilDiameter(), precision: 12);
            restoredStop.SemiDiameter = 4;
            Assert.Equal(8, restored.Paraxial.EstimateEntrancePupilDiameter(), precision: 12);
        }
    }

    [Fact]
    public void OptimizationVariableFlagsSurviveSnapshotRoundTrip()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.SurfaceGroup.Items[1].RadiusVariable = true;
        optic.SurfaceGroup.Items[2].ThicknessVariable = true;

        var restored = Optic.FromSnapshot(optic.ToSnapshot());

        Assert.True(restored.SurfaceGroup.Items[1].RadiusVariable);
        Assert.False(restored.SurfaceGroup.Items[1].ThicknessVariable);
        Assert.False(restored.SurfaceGroup.Items[2].RadiusVariable);
        Assert.True(restored.SurfaceGroup.Items[2].ThicknessVariable);
    }

    [Fact]
    public void MeritFunctionSurvivesSnapshotRoundTrip()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.MeritFunctionOperands.Add(new MeritOperandDefinition
        {
            Type = "RADI",
            Surface = 2,
            Field = 1,
            Wavelength = 2,
            Target = 12.5,
            Weight = 3,
            Comment = "保持第二面半径",
            PupilRings = 5,
            PupilArms = 10,
            PupilObscuration = 0.25,
            PupilSampling = "uniform"
        });

        var restored = Optic.FromSnapshot(optic.ToSnapshot());
        var operand = Assert.Single(restored.MeritFunctionOperands);

        Assert.Equal("RADI", operand.Type);
        Assert.Equal(2, operand.Surface);
        Assert.Equal(12.5, operand.Target);
        Assert.Equal(3, operand.Weight);
        Assert.Equal("保持第二面半径", operand.Comment);
        Assert.Equal(5, operand.PupilRings);
        Assert.Equal(10, operand.PupilArms);
        Assert.Equal(0.25, operand.PupilObscuration, precision: 12);
        Assert.Equal("uniform", operand.PupilSampling);
    }

    [Theory]
    [InlineData("ObjectHeightField", FieldDefinitionKind.ObjectHeight)]
    [InlineData("ParaxialImageHeightField", FieldDefinitionKind.ParaxialImageHeight)]
    public void PythonJsonImportPreservesFieldDefinitions(
        string fieldType,
        FieldDefinitionKind expected)
    {
        var json = PythonJsonWithSurfaceGeometry(
            PythonPlaneGeometry(),
            fieldType: fieldType);

        var optic = PythonOptilandJsonStore.Deserialize(json);

        Assert.Equal(expected, optic.FieldDefinition);
    }

    [Theory]
    [InlineData("apodization", "apodization")]
    [InlineData("pickups", "pickups")]
    [InlineData("solves", "solves")]
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
                  "type": "UnsupportedApodization"
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

    [Theory]
    [InlineData("aperture")]
    [InlineData("fieldGroup")]
    [InlineData("objectSpace")]
    public void PythonJsonImportPreservesTelecentricContracts(string contract)
    {
        var json = PythonJsonWithSurfaceGeometry(
            PythonPlaneGeometry(),
            systemApertureType: "objectNA",
            apertureObjectSpaceTelecentric: contract == "aperture" ? "true" : "false",
            fieldsTelecentric: contract == "fieldGroup" ? "true" : "false",
            fieldsObjectSpaceTelecentric: contract == "objectSpace" ? "true" : "false",
            fieldType: "ObjectHeightField");

        var optic = PythonOptilandJsonStore.Deserialize(json);

        Assert.Equal(contract == "aperture", optic.Aperture.ObjectSpaceTelecentric);
        Assert.Equal(contract == "fieldGroup", optic.FieldGroupTelecentric);
        Assert.Equal(contract == "objectSpace", optic.ObjectSpaceTelecentric);
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
    public void PythonJsonImportSupportsReflectiveThinLensInteraction()
    {
        var json = PythonJsonWithSurfaceGeometry(PythonPlaneGeometry(), PythonThinLensInteraction("75.0", "true"));

        var optic = PythonOptilandJsonStore.Deserialize(json);
        var interaction = Assert.IsType<ThinLensInteractionModel>(optic.SurfaceGroup.Items[1].InteractionModel);
        Assert.True(interaction.IsReflective);
        Assert.Equal(75, interaction.FocalLength, precision: 12);

        var exported = PythonOptilandJsonStore.Serialize(optic);
        Assert.Contains("\"is_reflective\": true", exported, StringComparison.Ordinal);

        var infinite = PythonOptilandJsonStore.Deserialize(PythonJsonWithSurfaceGeometry(
            PythonPlaneGeometry(),
            PythonThinLensInteraction("Infinity", "false")));
        Assert.True(double.IsPositiveInfinity(
            Assert.IsType<ThinLensInteractionModel>(infinite.SurfaceGroup.Items[1].InteractionModel)
                .FocalLength));
        Assert.Contains(
            "\"focal_length\": Infinity",
            PythonOptilandJsonStore.Serialize(infinite),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task NativeJsonPreservesInfiniteGratingAndThinLensValues()
    {
        var optic = Optic.CreateTessarLens();
        optic.SurfaceGroup.Items[1].Geometry = new PlaneGratingGeometry(0, double.PositiveInfinity, 0);
        optic.SurfaceGroup.Items[1].InteractionModel = new DiffractiveInteractionModel();
        optic.SurfaceGroup.Items[2].InteractionModel = new ThinLensInteractionModel(
            double.NegativeInfinity,
            true);
        var path = Path.Combine(Path.GetTempPath(), $"infinite-components-{Guid.NewGuid():N}.optiland.json");
        try
        {
            await OpticJsonStore.SaveAsync(optic, path);
            var restored = await OpticJsonStore.LoadAsync(path);
            Assert.True(double.IsPositiveInfinity(
                Assert.IsType<PlaneGratingGeometry>(restored.SurfaceGroup.Items[1].Geometry)
                    .GratingPeriodMicrometers));
            var thinLens = Assert.IsType<ThinLensInteractionModel>(
                restored.SurfaceGroup.Items[2].InteractionModel);
            Assert.True(double.IsNegativeInfinity(thinLens.FocalLength));
            Assert.True(thinLens.IsReflective);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
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

    private static JsonDocument LoadApodizationReference()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "optiland-0.5.8-apodization-reference.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static JsonDocument LoadPhaseReference()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "optiland-0.5.8-phase-reference.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static JsonDocument LoadDiffractiveReference()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "optiland-0.5.8-diffractive-reference.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static JsonDocument LoadThinLensReference()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "optiland-0.5.8-thin-lens-reference.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static RealRay ThinLensRay(JsonElement sample) => new(
        new Vector3D(
            sample.GetProperty("x").GetDouble(),
            sample.GetProperty("y").GetDouble(),
            0),
        new Vector3D(
            sample.GetProperty("direction_x").GetDouble(),
            sample.GetProperty("direction_y").GetDouble(),
            sample.GetProperty("direction_z").GetDouble()),
        sample.GetProperty("wavelength_micrometers").GetDouble() * 1000,
        sample.GetProperty("input_intensity").GetDouble());

    private static IGratingGeometry CreateGratingGeometry(JsonElement diffractionCase)
    {
        var order = diffractionCase.GetProperty("order").GetInt32();
        var period = ReadPythonDouble(diffractionCase.GetProperty("period_micrometers"));
        var angle = diffractionCase.GetProperty("groove_orientation_angle_radians").GetDouble();
        return diffractionCase.GetProperty("name").GetString()!.StartsWith("plane", StringComparison.Ordinal)
            ? new PlaneGratingGeometry(order, period, angle)
            : new StandardGratingGeometry(
                diffractionCase.GetProperty("radius").GetDouble(),
                diffractionCase.GetProperty("conic").GetDouble(),
                order,
                period,
                angle);
    }

    private static void AssertPhaseSamples(JsonElement profileCase, IPhaseProfile profile)
    {
        foreach (var sample in profileCase.GetProperty("samples").EnumerateArray())
        {
            var x = sample.GetProperty("x").GetDouble();
            var y = sample.GetProperty("y").GetDouble();
            var gradient = profile.Gradient(x, y, 550);
            AssertClose(sample.GetProperty("phase").GetDouble(), profile.Phase(x, y, 550), TraceTolerance);
            AssertClose(sample.GetProperty("gradient_x").GetDouble(), gradient.Dx, TraceTolerance);
            AssertClose(sample.GetProperty("gradient_y").GetDouble(), gradient.Dy, TraceTolerance);
            AssertClose(
                sample.GetProperty("paraxial_gradient").GetDouble(),
                profile.ParaxialGradient(y, 550),
                TraceTolerance);
        }
    }

    private static void AssertApodizationEquivalent(
        IApodizationModel expected,
        IApodizationModel? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.GetType(), actual.GetType());
        switch (expected)
        {
            case UniformApodization:
                break;
            case GaussianApodization gaussian:
                AssertClose(gaussian.Sigma, Assert.IsType<GaussianApodization>(actual).Sigma, ScalarTolerance);
                break;
            case CosineSquaredApodization cosine:
                AssertClose(cosine.Radius, Assert.IsType<CosineSquaredApodization>(actual).Radius, ScalarTolerance);
                break;
            case HannApodization hann:
                AssertClose(hann.Diameter, Assert.IsType<HannApodization>(actual).Diameter, ScalarTolerance);
                break;
            case PolynomialApodization polynomial:
                var actualPolynomial = Assert.IsType<PolynomialApodization>(actual);
                AssertClose(polynomial.Radius, actualPolynomial.Radius, ScalarTolerance);
                AssertClose(polynomial.Power, actualPolynomial.Power, ScalarTolerance);
                break;
            case SuperGaussianApodization superGaussian:
                var actualSuperGaussian = Assert.IsType<SuperGaussianApodization>(actual);
                AssertClose(superGaussian.Width, actualSuperGaussian.Width, ScalarTolerance);
                AssertClose(superGaussian.Exponent, actualSuperGaussian.Exponent, ScalarTolerance);
                break;
            case TukeyApodization tukey:
                var actualTukey = Assert.IsType<TukeyApodization>(actual);
                AssertClose(tukey.Radius, actualTukey.Radius, ScalarTolerance);
                AssertClose(tukey.Alpha, actualTukey.Alpha, ScalarTolerance);
                break;
            default:
                throw new NotSupportedException($"No test assertion for apodization '{expected.Kind}'.");
        }
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

    private static string PythonStandardGeometry()
    {
        return """
        {
          "type": "StandardGeometry",
          "cs": {
            "x": 0.0,
            "y": 0.0,
            "z": 0.0,
            "rx": 0.0,
            "ry": 0.0,
            "rz": 0.0,
            "reference_cs": null
          },
          "radius": 10.0,
          "conic": 0.0
        }
        """;
    }

    private static string PythonGratingGeometry(JsonElement diffractionCase)
    {
        var isPlane = diffractionCase.GetProperty("name").GetString()!
            .StartsWith("plane", StringComparison.Ordinal);
        var radius = isPlane ? "Infinity" : diffractionCase.GetProperty("radius").GetRawText();
        var type = isPlane ? "PlaneGrating" : "StandardGratingGeometry";
        return $$"""
        {
          "type": "{{type}}",
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
          "conic": {{diffractionCase.GetProperty("conic").GetRawText()}},
          "order": {{diffractionCase.GetProperty("order").GetRawText()}},
          "period": {{PythonNumberText(diffractionCase.GetProperty("period_micrometers"))}},
          "angle": {{diffractionCase.GetProperty("groove_orientation_angle_radians").GetRawText()}}
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

    private static string PythonPhaseInteraction(string profileJson, bool isReflective)
    {
        return $$"""
        {
          "type": "PhaseInteractionModel",
          "is_reflective": {{isReflective.ToString().ToLowerInvariant()}},
          "coating": null,
          "bsdf": null,
          "phase_profile": {{profileJson}}
        }
        """;
    }

    private static string PythonDiffractiveInteraction(bool isReflective)
    {
        return $$"""
        {
          "type": "DiffractiveInteractionModel",
          "is_reflective": {{isReflective.ToString().ToLowerInvariant()}},
          "coating": null,
          "bsdf": null
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

    private static void AssertPythonFloat(JsonElement expected, double actual)
    {
        if (expected.ValueKind == JsonValueKind.Number)
        {
            AssertClose(expected.GetDouble(), actual, TraceTolerance);
            return;
        }

        Assert.Equal("NaN", expected.GetString());
        Assert.True(double.IsNaN(actual));
    }

    private static void AssertPythonDouble(JsonElement expected, double actual, double tolerance)
    {
        var expectedValue = ReadPythonDouble(expected);
        if (double.IsInfinity(expectedValue) || double.IsNaN(expectedValue))
        {
            Assert.Equal(expectedValue, actual);
            return;
        }

        AssertClose(expectedValue, actual, tolerance);
    }

    private static double ReadPythonDouble(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : value.GetString() switch
            {
                "Infinity" => double.PositiveInfinity,
                "-Infinity" => double.NegativeInfinity,
                "NaN" => double.NaN,
                var text => double.Parse(text!)
            };
    }

    private static string PythonNumberText(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number ? value.GetRawText() : value.GetString()!;
}
