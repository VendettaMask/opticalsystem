using OptilandWorkbench.Core;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;
using OptilandWorkbench.Core.Interactions;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.NonSequential;
using OptilandWorkbench.Core.Serialization;
using ContractKind = OptilandWorkbench.Application.Contracts.NonSequentialObjectKind;
using DetectorRectangleParameters = OptilandWorkbench.Core.NonSequential.DetectorRectangleParameters;
using NonSequentialObjectKind = OptilandWorkbench.Core.NonSequential.NonSequentialObjectKind;
using NonSequentialSurfaceBehavior = OptilandWorkbench.Core.NonSequential.NonSequentialSurfaceBehavior;
using PlaneRectangleParameters = OptilandWorkbench.Core.NonSequential.PlaneRectangleParameters;

namespace OptilandWorkbench.Tests;

public sealed class NonSequentialRayTracerTests
{
    [Fact]
    public void TraceSelectsNearestObjectInsteadOfInsertionOrder()
    {
        var scene = new NonSequentialScene();
        scene.Add(new NonSequentialObject(20, "Far detector", Plane(20, 10, 5), isDetector: true));
        scene.Add(new NonSequentialObject(10, "Near window", Plane(10, 5, 5)));

        var path = new NonSequentialRayTracer(scene)
            .Trace(Ray(new Vector3D(0, 0, 0), new Vector3D(0, 0, 1)))
            .Paths.Single();

        Assert.Equal(new[] { 10, 20 }, path.Interactions.Select(item => item.ObjectId));
        Assert.Equal(NonSequentialTerminationReason.DetectorHit, path.TerminationReason);
        Assert.Equal(10, path.CumulativePathLength, 8);
    }

    [Fact]
    public void ReflectedRayCanReturnToAnEarlierObject()
    {
        var scene = new NonSequentialScene();
        scene.Add(new NonSequentialObject(1, "Return detector", Plane(1, 0, 5), isDetector: true));
        scene.Add(new NonSequentialObject(2, "Mirror", Plane(2, 5, 5, reflective: true)));

        var path = new NonSequentialRayTracer(scene)
            .Trace(Ray(new Vector3D(0, 0, 1), new Vector3D(0, 0, 1)))
            .Paths.Single();

        Assert.Equal(new[] { 2, 1 }, path.Interactions.Select(item => item.ObjectId));
        Assert.Equal(RayInteractionKind.Reflected, path.Interactions[0].Sample.InteractionKind);
        Assert.True(path.Interactions[0].Sample.Direction.Z < 0);
        Assert.Equal(NonSequentialTerminationReason.DetectorHit, path.TerminationReason);
    }

    [Fact]
    public void MissedFiniteApertureDoesNotHideFartherObject()
    {
        var scene = new NonSequentialScene();
        scene.Add(new NonSequentialObject(1, "Small stop", Plane(1, 5, 1)));
        scene.Add(new NonSequentialObject(2, "Large detector", Plane(2, 10, 5), isDetector: true));

        var path = new NonSequentialRayTracer(scene)
            .Trace(Ray(new Vector3D(2, 0, 0), new Vector3D(0, 0, 1)))
            .Paths.Single();

        var interaction = Assert.Single(path.Interactions);
        Assert.Equal(2, interaction.ObjectId);
        Assert.Equal(NonSequentialTerminationReason.DetectorHit, path.TerminationReason);
    }

    [Fact]
    public void MaximumInteractionLimitStopsMirrorLoop()
    {
        var scene = new NonSequentialScene();
        scene.Add(new NonSequentialObject(1, "Left mirror", Plane(1, 0, 5, reflective: true)));
        scene.Add(new NonSequentialObject(2, "Right mirror", Plane(2, 10, 5, reflective: true)));

        var path = new NonSequentialRayTracer(scene)
            .Trace(
                Ray(new Vector3D(0, 0, 5), new Vector3D(0, 0, 1)),
                new NonSequentialTraceOptions(MaximumInteractions: 3))
            .Paths.Single();

        Assert.Equal(new[] { 2, 1, 2 }, path.Interactions.Select(item => item.ObjectId));
        Assert.Equal(NonSequentialTerminationReason.MaximumInteractions, path.TerminationReason);
    }

    [Fact]
    public void TransmittedPathTracksMediumAcrossBothSidesOfLens()
    {
        var glass = new ConstantIndexMaterial("Glass", 1.5);
        var front = Plane(1, 5, 5);
        front.MaterialAfter = glass;
        var back = Plane(2, 10, 5);
        back.MaterialBefore = glass;
        var scene = new NonSequentialScene();
        scene.Add(new NonSequentialObject(1, "Front", front));
        scene.Add(new NonSequentialObject(2, "Back detector", back, isDetector: true));

        var path = new NonSequentialRayTracer(scene)
            .Trace(Ray(new Vector3D(0, 0, 0), new Vector3D(0.1, 0, 1)))
            .Paths.Single();

        Assert.Equal("Air", path.Interactions[0].IncidentMaterial);
        Assert.Equal("Glass", path.Interactions[0].OutgoingMaterial);
        Assert.Equal("Glass", path.Interactions[1].IncidentMaterial);
        Assert.Equal("Air", path.Interactions[1].OutgoingMaterial);
        Assert.All(path.Interactions, interaction =>
            Assert.Equal(RayInteractionKind.Transmitted, interaction.Sample.InteractionKind));
    }

    [Fact]
    public void IndependentDocumentDoesNotProjectSequentialSurfacesOnModeSwitch()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var surfaceCount = application.Prescription.GetSurfaces().Count;

        application.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);

        Assert.Empty(application.NonSequential.GetDocument().Objects);
        Assert.Equal(surfaceCount, application.Prescription.GetSurfaces().Count);
    }

    [Fact]
    public void CoreAnalysisPublishesTraceTableAndTermination()
    {
        var optic = Optic.CreateCookeTriplet();
        var document = StarOptProjectStore.CreateDefaultNonSequentialDocument(optic);
        document.Insert(0, NonSequentialObjectDefinition.Create(NonSequentialObjectKind.SourceRay));
        document.Insert(1, NonSequentialObjectDefinition.Create(NonSequentialObjectKind.DetectorRectangle) with
        {
            LocalCoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, 10))
        });

        var data = new NonSequentialRayTraceAnalysis(optic, document).GenerateData();

        Assert.Equal("Non-Sequential Ray Trace", data.Name);
        Assert.Equal(1, data.Values["DetectorCount"]);
        Assert.Equal(1.0, Assert.IsType<double>(data.Values["DetectorPowerWatts"]), 12);
        Assert.NotNull(data.Table);
        Assert.NotEmpty(data.Table!.Rows);
        Assert.Contains("Non-Sequential Ray Trace", optic.Analyses.Names);
        Assert.IsType<NonSequentialRayTraceAnalysis>(optic.Analyses.Create("Non-Sequential Ray Trace"));
    }

    [Fact]
    public async Task ApplicationCatalogRunsNonSequentialAnalysis()
    {
        using var application = WorkbenchApplication.Create("cooke");
        Assert.Equal(OpticalWorkbenchMode.Sequential, application.Modes.CurrentMode);
        Assert.DoesNotContain("非序列单光线追迹", application.Analyses.AnalysisNames);
        Assert.Throws<InvalidOperationException>(() =>
            application.Analyses.GetParameters("Non-Sequential Ray Trace"));

        WorkbenchModeChangedEventArgs? modeChange = null;
        application.Modes.ModeChanged += (_, args) => modeChange = args;
        application.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);

        Assert.Equal(OpticalWorkbenchMode.NonSequential, application.Modes.CurrentMode);
        Assert.Equal(OpticalWorkbenchMode.Sequential, modeChange?.PreviousMode);
        Assert.Equal(OpticalWorkbenchMode.NonSequential, modeChange?.CurrentMode);
        Assert.Equal(new[] { "非序列单光线追迹", "非序列探测器查看" }, application.Analyses.AnalysisNames);
        var sourceId = application.NonSequential.AddObject(ContractKind.SourceRay);
        var detectorId = application.NonSequential.AddObject(ContractKind.DetectorRectangle);
        var detector = application.NonSequential.GetDocument().Objects.Single(item => item.Id == detectorId);
        application.NonSequential.UpdateObject(new NonSequentialObjectUpdateDto(
            detector.Id, true, true, detector.Kind, detector.Name, null, null,
            0, 0, 10, 0, 0, 0, detector.Parameters));
        var objects = application.NonSequential.GetDocument().Objects;
        Assert.Equal(2, objects.Count);
        Assert.Equal("探测器", objects[^1].Role);
        Assert.NotEqual(Guid.Empty, sourceId);
        var parameters = application.Analyses.GetParameters("非序列单光线追迹");

        var result = await application.Analyses.RunAsync(new AnalysisRequestDto(
            Guid.NewGuid(),
            1,
            "Non-Sequential Ray Trace",
            parameters.ToDictionary(parameter => parameter.Key, parameter => parameter.DefaultValue)));

        Assert.Equal("Non-Sequential Ray Trace", result.CanonicalAnalysisKey);
        Assert.Equal("非序列单光线追迹", result.View.Name);
        Assert.NotNull(result.View.Table);
        Assert.Contains(parameters, parameter => parameter.Key == "SourceNumber");
        Assert.Contains(parameters, parameter => parameter.Key == "SplitFresnelRays");

        var detectorParameters = application.Analyses.GetParameters("Non-Sequential Detector Viewer");
        var detectorResult = await application.Analyses.RunAsync(new AnalysisRequestDto(
            Guid.NewGuid(),
            1,
            "Non-Sequential Detector Viewer",
            detectorParameters.ToDictionary(parameter => parameter.Key, parameter => parameter.DefaultValue)));
        Assert.Equal("Non-Sequential Detector Viewer", detectorResult.CanonicalAnalysisKey);
        Assert.Contains(detectorResult.View.Series, series => series.Kind ==
            OptilandWorkbench.Application.Contracts.AnalysisSeriesKind.Heatmap);
    }

    [Fact]
    public void ObjectSourceTraceAccumulatesDetectorPixelsAndConservesPower()
    {
        var optic = Optic.CreateBlank();
        var document = StarOptProjectStore.CreateDefaultNonSequentialDocument(optic);
        document.Insert(0, NonSequentialObjectDefinition.Create(NonSequentialObjectKind.SourceRay));
        var detector = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.DetectorRectangle) with
        {
            LocalCoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, 10)),
            Parameters = new DetectorRectangleParameters(20, 20, 10, 10)
        };
        document.Insert(1, detector);

        var result = new NonSequentialDocumentTracer().Trace(document, optic.Materials);

        var frame = Assert.Single(result.Detectors);
        Assert.Single(result.Branches);
        Assert.Equal(1.0, frame.TotalPowerWatts, 12);
        Assert.Equal(1.0, result.EnergyBalance.AccountedPowerWatts, 12);
        Assert.Equal(1.0, frame.PowerByWavelength[1].Sum(), 12);
    }

    [Fact]
    public void FresnelInterfaceCreatesParentedBranchesWithEnergyBalance()
    {
        var optic = Optic.CreateBlank();
        var document = StarOptProjectStore.CreateDefaultNonSequentialDocument(optic);
        document.Insert(0, NonSequentialObjectDefinition.Create(NonSequentialObjectKind.SourceRay));
        document.Insert(1, NonSequentialObjectDefinition.Create(NonSequentialObjectKind.PlaneRectangle) with
        {
            LocalCoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, 5)),
            Parameters = new PlaneRectangleParameters(20, 20, NonSequentialSurfaceBehavior.Refractive, "Air", "N-BK7")
        });
        document.Insert(2, NonSequentialObjectDefinition.Create(NonSequentialObjectKind.DetectorRectangle) with
        {
            LocalCoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, 10))
        });

        var result = new NonSequentialDocumentTracer().Trace(document, optic.Materials);

        Assert.Contains(result.Branches, branch => branch.TerminationReason == NonSequentialTerminationReason.Split);
        Assert.Contains(result.Branches, branch => branch.ParentId is not null);
        Assert.InRange(Math.Abs(result.EnergyBalance.SourcePowerWatts - result.EnergyBalance.AccountedPowerWatts), 0, 1e-12);
        Assert.InRange(result.EnergyBalance.DetectorPowerWatts, 0.8, 1.0);
        Assert.True(result.EnergyBalance.EscapedPowerWatts > 0);
    }

    [Fact]
    public async Task StarOptRoundTripPreservesIndependentUnicodeScene()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nsc-{Guid.NewGuid():N}.staropt");
        try
        {
            using (var application = WorkbenchApplication.Create("cooke"))
            {
                var id = application.NonSequential.AddObject(ContractKind.SourceGaussian);
                var row = application.NonSequential.GetDocument().Objects.Single(item => item.Id == id);
                application.NonSequential.UpdateObject(new NonSequentialObjectUpdateDto(
                    row.Id, true, true, row.Kind, "中文高斯光源", null, null,
                    1, 2, 3, 4, 5, 6, row.Parameters));
                await application.Documents.SaveAsync(path);
            }

            using var restored = WorkbenchApplication.Create("blank");
            await restored.Documents.OpenAsync(path);
            var restoredRow = Assert.Single(restored.NonSequential.GetDocument().Objects);
            Assert.Equal("中文高斯光源", restoredRow.Name);
            Assert.Equal(3, restoredRow.Z, 12);
            Assert.False(restored.Documents.GetSnapshot().IsDirty);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static OpticalSurface Plane(
        int number,
        double z,
        double semiDiameter,
        bool reflective = false) => new()
        {
            Number = number,
            Label = $"Surface {number}",
            Geometry = new PlaneGeometry(),
            CoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, z)),
            SemiDiameter = semiDiameter,
            MaterialBefore = new AirMaterial(),
            MaterialAfter = new AirMaterial(),
            InteractionModel = new RefractiveReflectiveInteractionModel(reflective)
        };

    private static RealRay Ray(Vector3D origin, Vector3D direction) => new(
        origin,
        direction,
        587.6);
}
