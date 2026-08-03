using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Application.Legacy;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Raytrace;

namespace OptilandWorkbench.Tests;

public sealed class ArchitectureConvergenceTests
{
    [Fact]
    public void UnknownAnalysisDoesNotFallBackToAnUnrelatedMetric()
    {
        var exception = Assert.Throws<UnknownAnalysisException>(() =>
            Optic.CreateCookeTriplet().Analyses.Create("not-registered"));

        Assert.Equal("not-registered", exception.AnalysisName);
    }

    [Fact]
    public void WorkbenchAnalysisDescriptorsAreTheCanonicalProductMetadataSource()
    {
        var descriptors = WorkbenchAnalysisCatalog.Descriptors;
        Assert.Equal(
            descriptors.Count,
            descriptors.Select(descriptor => descriptor.CanonicalKey).Distinct(StringComparer.Ordinal).Count());

        var optic = Optic.CreateCookeTriplet();
        Assert.All(optic.Analyses.Names, canonicalKey =>
            Assert.True(
                WorkbenchAnalysisCatalog.TryGetDescriptor(canonicalKey, out _),
                $"Core analysis '{canonicalKey}' is missing a Workbench descriptor."));

        foreach (var descriptor in descriptors)
        {
            Assert.Equal(
                descriptor.CanonicalKey,
                WorkbenchAnalysisCatalog.CanonicalKey(descriptor.CanonicalKey));
            Assert.Equal(
                descriptor.CanonicalKey,
                WorkbenchAnalysisCatalog.CanonicalKey(descriptor.DisplayName));
            Assert.Equal(
                descriptor.DisplayName,
                WorkbenchAnalysisCatalog.DisplayName(descriptor.CanonicalKey));
            Assert.Equal(
                descriptor.PresentationKind,
                WorkbenchAnalysisCatalog.PresentationKind(descriptor.CanonicalKey));
            Assert.All(descriptor.Aliases, alias =>
                Assert.Equal(descriptor.CanonicalKey, WorkbenchAnalysisCatalog.CanonicalKey(alias)));
        }
    }

    [Fact]
    public void RibbonAnalysisCommandsResolveThroughWorkbenchDescriptors()
    {
        var commandIds = WorkbenchAnalysisCatalog.RibbonCommands
            .Select(command => command.Id)
            .ToHashSet(StringComparer.Ordinal);
        Assert.All(
            WorkbenchAnalysisCatalog.RibbonMenus.SelectMany(menu => menu.CommandIds),
            commandId => Assert.True(commandId == "-" || commandIds.Contains(commandId)));

        foreach (var command in WorkbenchAnalysisCatalog.RibbonCommands
                     .Where(command => command.Kind == AnalysisRibbonCommandKind.Analysis))
        {
            Assert.True(
                WorkbenchAnalysisCatalog.TryGetDescriptor(command.Name, out var descriptor),
                $"Ribbon command '{command.Id}' with name '{command.Name}' has no descriptor.");
            Assert.Equal(command, descriptor.RibbonCommand);
        }
    }

    [Fact]
    public void SpotMetricDoesNotReportZeroWhenOpticalInputsAreMissing()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.Wavelengths.Clear();

        Assert.Throws<AnalysisDataUnavailableException>(() =>
            SpotMetricEvaluator.Evaluate(optic));
    }

    [Fact]
    public void RemovedRayAimerModesAreNotExposedBySequentialTracer()
    {
        var tracerType = typeof(SequentialRayTracer);

        Assert.Null(tracerType.GetProperty("RayAimer"));
        Assert.Null(tracerType.GetMethod("SetAiming"));
    }

    [Fact]
    public void FieldGroupTelecentricFlagParticipatesInRayGeneration()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.FieldGroupTelecentric = true;
        optic.ObjectSpaceTelecentric = false;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, 0, 0, 0.5876));

        Assert.Contains("telecentric", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FullFieldAberrationUsesSelectedFieldAsMapCenterAndClampsFringeTerms()
    {
        var optic = Optic.CreateCookeTriplet();
        var selected = optic.Fields[1];
        var data = new FullFieldAberrationAnalysis(
            optic,
            xFieldWidth: 0.05,
            yFieldWidth: 0.05,
            maximumTerm: 256,
            fieldNumber: 2,
            xFieldSamples: 3,
            yFieldSamples: 3,
            pupilSampling: 8).GenerateData();
        Assert.Equal(ZernikeFitEngine.MaximumFringeTerm, data.Values["MaximumTerm"]);
        Assert.Equal(selected.X, Assert.IsType<double>(data.Values["FieldCenterX"]), 12);
        Assert.Equal(selected.Y, Assert.IsType<double>(data.Values["FieldCenterY"]), 12);
    }

    [Fact]
    public void FullFieldAberrationDoesNotTurnTotalSamplingFailureIntoZeroMetrics()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.SurfaceGroup.Items[1].PhysicalAperture = new ClosedAperture();
        var analysis = new FullFieldAberrationAnalysis(
            optic,
            xFieldWidth: 0.05,
            yFieldWidth: 0.05,
            xFieldSamples: 3,
            yFieldSamples: 3,
            pupilSampling: 8);

        Assert.Throws<AnalysisDataUnavailableException>(analysis.GenerateData);
    }

    [Fact]
    public void PsfRejectsUnknownSurfaceInsteadOfIgnoringIt()
    {
        var analysis = new PsfAnalysis(
            Optic.CreateCookeTriplet(),
            numRays: 8,
            gridSize: 8,
            surfaceNumber: 999);

        Assert.Throws<ArgumentOutOfRangeException>(analysis.GenerateData);
    }

    [Fact]
    public void FullFieldUiDoesNotOfferAChoiceWithOnlyOneImplementation()
    {
        var connector = new OptilandConnector(Optic.CreateCookeTriplet());

        Assert.DoesNotContain(
            connector.GetAnalysisParameters("Full Field Aberration"),
            parameter => parameter.Key == "Decomposition");
        Assert.Equal(
            ZernikeFitEngine.MaximumFringeTerm,
            connector.GetAnalysisParameters("Full Field Aberration")
                .Single(parameter => parameter.Key == "MaximumTerm").Maximum);
    }

    [Fact]
    public void RmsFieldMapDoesNotOfferAnOverlayThatItCannotRender()
    {
        var connector = new OptilandConnector(Optic.CreateCookeTriplet());

        Assert.DoesNotContain(
            connector.GetAnalysisParameters("RMS Field Map"),
            parameter => parameter.Key == "ShowDiffractionLimit");
    }

    [Fact]
    public void WavefrontFieldMapUsesWavefrontMetadataNames()
    {
        var data = new RmsFieldMapAnalysis(
            Optic.CreateCookeTriplet(),
            xFieldSamples: 3,
            yFieldSamples: 3,
            xFieldWidth: 0.01,
            yFieldWidth: 0.01,
            numRings: 2,
            data: "wavefront").GenerateData();

        Assert.Contains("MinimumRmsWavefrontError", data.Values.Keys);
        Assert.Contains("MaximumRmsWavefrontError", data.Values.Keys);
        Assert.DoesNotContain("MinimumRmsSpotRadius", data.Values.Keys);
        Assert.DoesNotContain("MaximumRmsSpotRadius", data.Values.Keys);
    }

    [Fact]
    public void LegacyRadiusAndConicImmediatelyUpdateCanonicalGeometry()
    {
        var surface = new OpticalSurface
        {
            Geometry = new EvenAsphereGeometry(40, -1, new[] { 1e-5 })
        };

        surface.Radius = 55;
        surface.Conic = -0.5;

        var geometry = Assert.IsType<EvenAsphereGeometry>(surface.Geometry);
        Assert.Equal(55, geometry.Base.Radius, 12);
        Assert.Equal(-0.5, geometry.Base.Conic, 12);
        Assert.Equal(1e-5, Assert.Single(geometry.Coefficients), 12);
    }

    [Fact]
    public void CanonicalGeometryImmediatelyUpdatesLegacyProjection()
    {
        var surface = new OpticalSurface
        {
            Radius = 10,
            Conic = 0
        };

        surface.Geometry = new StandardGeometry(75, -1.25);

        Assert.Equal(75, surface.Radius, 12);
        Assert.Equal(-1.25, surface.Conic, 12);
    }

    [Fact]
    public void CanonicalInteractionImmediatelyUpdatesLegacyReflectionProjection()
    {
        var surface = new OpticalSurface();

        surface.InteractionModel = new RefractiveReflectiveInteractionModel(true);
        Assert.True(surface.IsReflective);

        surface.IsReflective = false;
        Assert.False(Assert.IsType<RefractiveReflectiveInteractionModel>(surface.InteractionModel).IsReflective);
    }

    [Fact]
    public void LegacyReflectionProjectionPreservesSpecializedInteractionParameters()
    {
        var thinLens = new OpticalSurface
        {
            InteractionModel = new ThinLensInteractionModel(42, isReflective: false)
        };
        thinLens.IsReflective = true;

        var reflectedThinLens = Assert.IsType<ThinLensInteractionModel>(thinLens.InteractionModel);
        Assert.Equal(42, reflectedThinLens.FocalLength, 12);
        Assert.True(reflectedThinLens.IsReflective);

        var diffractive = new OpticalSurface
        {
            InteractionModel = new DiffractiveInteractionModel(600, order: -1)
        };
        diffractive.IsReflective = true;

        var reflectedDiffractive = Assert.IsType<DiffractiveInteractionModel>(diffractive.InteractionModel);
        Assert.Equal(600, reflectedDiffractive.GrooveFrequencyLinesPerMillimeter);
        Assert.Equal(-1, reflectedDiffractive.Order);
        Assert.True(reflectedDiffractive.IsReflective);
    }

    [Fact]
    public void LegacyReflectionValueIsAProjectionOfTheCanonicalInteraction()
    {
        var model = new RefractiveReflectiveInteractionModel();
        var surface = new OpticalSurface { InteractionModel = model };

        model.IsReflective = true;

        Assert.True(surface.IsReflective);
    }

    [Fact]
    public void CanonicalReplaceAndRenumberPreserveSurfaceComponentsAndDecenter()
    {
        var optic = Optic.CreateBlank();
        var interaction = new ThinLensInteractionModel(42, isReflective: true);
        var aperture = new RectangularAperture(3, 2);
        var coating = new SimpleCoatingModel(0.8, 0.15);
        var surface = new OpticalSurface
        {
            Thickness = 7,
            InteractionModel = interaction,
            PhysicalAperture = aperture,
            CoatingModel = coating,
            CoordinateSystem = new CoordinateSystem(
                new Vector3D(1.25, -2.5, 99),
                RotationXDegrees: 3,
                RotationYDegrees: 4,
                RotationZDegrees: 5)
        };

        optic.SurfaceGroup.Replace(new[] { surface });
        optic.SurfaceGroup.Renumber();

        Assert.Same(interaction, surface.InteractionModel);
        Assert.Same(aperture, surface.PhysicalAperture);
        Assert.Same(coating, surface.CoatingModel);
        Assert.Equal(1.25, surface.CoordinateSystem.Origin.X, 12);
        Assert.Equal(-2.5, surface.CoordinateSystem.Origin.Y, 12);
        Assert.Equal(0, surface.CoordinateSystem.Origin.Z, 12);
        Assert.Equal(3, surface.CoordinateSystem.RotationXDegrees, 12);
        Assert.Equal(4, surface.CoordinateSystem.RotationYDegrees, 12);
        Assert.Equal(5, surface.CoordinateSystem.RotationZDegrees, 12);
    }

    [Fact]
    public void LegacyImportExplicitlyBuildsCanonicalComposition()
    {
        var optic = new Optic("Legacy import");

        optic.SurfaceGroup.ImportLegacySurfaces(new[]
        {
            new OpticalSurface
            {
                Radius = 25,
                Material = "N-BK7",
                Coating = "MgF2"
            }
        });

        var surface = Assert.Single(optic.SurfaceGroup.Items);
        Assert.IsType<StandardGeometry>(surface.Geometry);
        Assert.Equal("N-BK7", surface.MaterialAfter.Name);
        Assert.IsType<ThinFilmStackCoating>(surface.CoatingModel);
        Assert.False(Assert.IsType<RefractiveReflectiveInteractionModel>(surface.InteractionModel).IsReflective);
    }

    [Fact]
    public void CanonicalMaterialAndCoatingComponentsUpdateCompatibilityProjections()
    {
        var surface = new OpticalSurface();

        surface.Coating = "MgF2";

        Assert.Equal("MgF2", Assert.Single(
            Assert.IsType<ThinFilmStackCoating>(surface.CoatingModel).Layers).MaterialName);

        surface.MaterialAfter = new ConstantIndexMaterial("CustomGlass", 1.7);
        surface.CoatingModel = new SimpleCoatingModel(0.8, 0.1);

        Assert.Equal("CustomGlass", surface.Material);
        Assert.Equal("Simple", surface.Coating);

        surface.InteractionModel = new RefractiveReflectiveInteractionModel(true);

        Assert.True(surface.IsReflective);
        Assert.Equal("MIRROR", surface.Material);
    }

    [Fact]
    public void ParaxialSystemMatrixIncludesReflectiveThinLensPower()
    {
        var optic = Optic.CreateBlank();
        var poweredSurface = optic.SurfaceGroup.Items[1];
        poweredSurface.InteractionModel = new ThinLensInteractionModel(50, isReflective: true);

        var effectiveFocalLength = optic.Paraxial.EstimateEffectiveFocalLength();

        Assert.Equal(50, effectiveFocalLength, 8);
    }

    [Fact]
    public void EntrancePupilUsesTheSameMatrixRulesForReflectiveThinLenses()
    {
        var optic = new Optic("Reflective pupil");
        optic.SurfaceGroup.Replace(new[]
        {
            new OpticalSurface { Label = "Object", Thickness = 0 },
            new OpticalSurface
            {
                Label = "Powered mirror",
                Thickness = 10,
                InteractionModel = new ThinLensInteractionModel(50, isReflective: true)
            },
            new OpticalSurface { Label = "Stop", IsStop = true }
        });

        Assert.Equal(-12.5, optic.Paraxial.EstimateEntrancePupilLocation(), 10);
    }

    private sealed class ClosedAperture : IPhysicalAperture
    {
        public string Kind => "test-closed";

        public bool Contains(Vector3D localPoint) => false;

        public IPhysicalAperture Clone() => new ClosedAperture();
    }
}
