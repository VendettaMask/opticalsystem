using OptilandWorkbench.Application.Legacy;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
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
    public void ParaxialSystemMatrixIncludesReflectiveThinLensPower()
    {
        var optic = Optic.CreateBlank();
        var poweredSurface = optic.SurfaceGroup.Items[1];
        poweredSurface.InteractionModel = new ThinLensInteractionModel(50, isReflective: true);

        var effectiveFocalLength = optic.Paraxial.EstimateEffectiveFocalLength();

        Assert.Equal(50, effectiveFocalLength, 8);
    }
}
