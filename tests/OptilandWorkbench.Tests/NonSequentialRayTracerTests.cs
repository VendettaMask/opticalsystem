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
using CoreObjectParameters = OptilandWorkbench.Core.NonSequential.NonSequentialObjectParameters;
using CoreSourceApertureShape = OptilandWorkbench.Core.NonSequential.NonSequentialSourceApertureShape;
using CoreSourceRadialSample = OptilandWorkbench.Core.NonSequential.SourceRadialSample;
using CoreSurfaceSourceAngularDistribution = OptilandWorkbench.Core.NonSequential.NonSequentialSurfaceSourceAngularDistribution;
using CoreVolumeSourceAngularDistribution = OptilandWorkbench.Core.NonSequential.NonSequentialVolumeSourceAngularDistribution;
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
        Assert.DoesNotContain("探测器查看器", application.Analyses.AnalysisNames);
        Assert.Throws<InvalidOperationException>(() =>
            application.Analyses.GetParameters("Non-Sequential Ray Trace"));

        WorkbenchModeChangedEventArgs? modeChange = null;
        application.Modes.ModeChanged += (_, args) => modeChange = args;
        application.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);

        Assert.Equal(OpticalWorkbenchMode.NonSequential, application.Modes.CurrentMode);
        Assert.Equal(OpticalWorkbenchMode.Sequential, modeChange?.PreviousMode);
        Assert.Equal(OpticalWorkbenchMode.NonSequential, modeChange?.CurrentMode);
        Assert.Equal(new[] { "非序列单光线追迹", "探测器查看器" }, application.Analyses.AnalysisNames);
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

        await application.NonSequentialAnalysis.TraceAsync(new NonSequentialTraceRunRequestDto());
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
        Assert.Equal(1, frame.HitCountByWavelength![1].Sum());
        Assert.Equal(1.0, frame.AngularPowerByWavelength![1].Sum(), 12);
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
    public void SimpleStochasticSplittingIsDeterministicAndKeepsWholeRayPower()
    {
        var optic = Optic.CreateBlank();
        var document = StarOptProjectStore.CreateDefaultNonSequentialDocument(optic);
        document.Insert(0, NonSequentialObjectDefinition.Create(NonSequentialObjectKind.SourcePoint) with
        {
            Parameters = new OptilandWorkbench.Core.NonSequential.SourcePointParameters(1, 1, 0, 20, 2000)
        });
        document.Insert(1, NonSequentialObjectDefinition.Create(NonSequentialObjectKind.PlaneRectangle) with
        {
            LocalCoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, 5)),
            Parameters = new PlaneRectangleParameters(20, 20, NonSequentialSurfaceBehavior.Refractive, "Air", "N-BK7")
        });
        document.Insert(2, NonSequentialObjectDefinition.Create(NonSequentialObjectKind.DetectorRectangle) with
        {
            LocalCoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, 10))
        });
        var request = new NonSequentialDocumentTraceRequest(
            SplittingMode: OptilandWorkbench.Core.NonSequential.NonSequentialSplittingMode.SimpleStochastic,
            RandomSeed: 17);

        var first = new NonSequentialDocumentTracer().Trace(document, optic.Materials, request);
        var second = new NonSequentialDocumentTracer().Trace(document, optic.Materials, request);

        Assert.Equal(first.EnergyBalance, second.EnergyBalance);
        Assert.Equal(first.Detectors.Single().PowerByWavelength[1], second.Detectors.Single().PowerByWavelength[1]);
        Assert.DoesNotContain(first.Branches, branch => branch.TerminationReason == NonSequentialTerminationReason.Split);
        Assert.InRange(Math.Abs(first.EnergyBalance.SourcePowerWatts - first.EnergyBalance.AccountedPowerWatts), 0, 1e-12);
    }

    [Fact]
    public void ExtendedNativeSourcesRespectSpatialBoundsPowerAndSeed()
    {
        var sources = new (NonSequentialObjectKind Kind, CoreObjectParameters Parameters)[]
        {
            (NonSequentialObjectKind.SourceEllipse,
                new OptilandWorkbench.Core.NonSequential.SourceEllipseParameters(10, 6, 10, 2, 1, 20, 500)),
            (NonSequentialObjectKind.SourceTwoAngle,
                new OptilandWorkbench.Core.NonSequential.SourceTwoAngleParameters(
                    8, 4, CoreSourceApertureShape.Rectangle, 25, 5, 2, 1, 20, 500)),
            (NonSequentialObjectKind.SourceRadial,
                new OptilandWorkbench.Core.NonSequential.SourceRadialParameters(
                    new[] { new CoreSourceRadialSample(0, 1), new CoreSourceRadialSample(20, 0.7), new CoreSourceRadialSample(60, 0) },
                    2, 1, 20, 500)),
            (NonSequentialObjectKind.SourceVolumeRectangle,
                new OptilandWorkbench.Core.NonSequential.SourceVolumeRectangleParameters(8, 6, 4, 10, 2, 1, 20, 500)),
            (NonSequentialObjectKind.SourceVolumeEllipse,
                new OptilandWorkbench.Core.NonSequential.SourceVolumeEllipseParameters(4, 3, 2, 10, 2, 1, 20, 500)),
            (NonSequentialObjectKind.SourceVolumeCylinder,
                new OptilandWorkbench.Core.NonSequential.SourceVolumeCylinderParameters(4, 2, 6, 10, 2, 1, 20, 500))
        };

        foreach (var (kind, parameters) in sources)
        {
            var first = TraceSource(kind, parameters);
            var second = TraceSource(kind, parameters);
            var starts = first.Branches.Select(branch => branch.Segments.Single().Start).ToArray();

            Assert.Equal(500, first.TotalBranchCount);
            Assert.Equal(2, first.EnergyBalance.SourcePowerWatts, 12);
            Assert.Equal(2, first.EnergyBalance.DetectorPowerWatts, 12);
            Assert.Equal(
                first.Branches.Select(branch => branch.Segments.Single().Start),
                second.Branches.Select(branch => branch.Segments.Single().Start));
            Assert.Equal(
                first.Branches.Select(branch => branch.Segments.Single().End),
                second.Branches.Select(branch => branch.Segments.Single().End));

            Assert.All(starts, point => AssertSourcePoint(kind, point));
            if (kind == NonSequentialObjectKind.SourceRadial)
            {
                Assert.All(first.Branches, branch =>
                {
                    var segment = branch.Segments.Single();
                    var direction = (segment.End - segment.Start) / (segment.End - segment.Start).Length;
                    var angle = Math.Acos(Math.Clamp(direction.Z, -1, 1)) * 180 / Math.PI;
                    Assert.InRange(angle, 0, 60.1);
                });
            }
        }
    }

    [Fact]
    public void EscapedSourceOnlyBranchesRetainFinalStateForVisualization()
    {
        var optic = Optic.CreateBlank();
        var document = StarOptProjectStore.CreateDefaultNonSequentialDocument(optic);
        document.Insert(0, NonSequentialObjectDefinition.Create(NonSequentialObjectKind.SourceEllipse));

        var result = new NonSequentialDocumentTracer().Trace(
            document,
            optic.Materials,
            new NonSequentialDocumentTraceRequest(
                NonSequentialTracePurpose.Layout,
                SplittingMode: OptilandWorkbench.Core.NonSequential.NonSequentialSplittingMode.None,
                RandomSeed: 7));

        Assert.Equal(20, result.TotalBranchCount);
        Assert.All(result.Branches, branch =>
        {
            Assert.Equal(NonSequentialTerminationReason.Escaped, branch.TerminationReason);
            Assert.Empty(branch.Segments);
            if (branch.FinalOrigin is not { } origin || branch.FinalDirection is not { } direction)
            {
                throw new Xunit.Sdk.XunitException("Escaped branch did not publish a final ray state.");
            }

            Assert.True(origin.X * origin.X / 25 + origin.Y * origin.Y / 25 <= 1 + 1e-12);
            Assert.Equal(0, origin.Z, 12);
            Assert.Equal(0, direction.X, 12);
            Assert.Equal(0, direction.Y, 12);
            Assert.Equal(1, direction.Z, 12);
        });
    }

    [Fact]
    public void SurfaceEllipseSourceSupportsZemaxStyleVirtualPointAndInnerEllipse()
    {
        var optic = Optic.CreateBlank();
        var document = StarOptProjectStore.CreateDefaultNonSequentialDocument(optic);
        document.Insert(0, NonSequentialObjectDefinition.Create(NonSequentialObjectKind.SourceEllipse) with
        {
            Parameters = new OptilandWorkbench.Core.NonSequential.SourceEllipseParameters(
                10,
                8,
                20,
                LayoutRayCount: 20,
                AnalysisRayCount: 200,
                AngularDistribution: CoreSurfaceSourceAngularDistribution.VirtualPoint,
                SourceDistanceMillimeters: 40,
                SourceX: 0.5,
                SourceY: -0.25,
                MinimumXHalfWidthMillimeters: 1.5,
                MinimumYHalfWidthMillimeters: 0.75)
        });

        var result = new NonSequentialDocumentTracer().Trace(
            document,
            optic.Materials,
            new NonSequentialDocumentTraceRequest(
                SplittingMode: OptilandWorkbench.Core.NonSequential.NonSequentialSplittingMode.None,
                RandomSeed: 11));

        Assert.Equal(200, result.TotalBranchCount);
        Assert.All(result.Branches, branch =>
        {
            if (branch.FinalOrigin is not { } origin || branch.FinalDirection is not { } direction)
            {
                throw new Xunit.Sdk.XunitException("Escaped branch did not publish a final ray state.");
            }

            Assert.True(origin.X * origin.X / 25 + origin.Y * origin.Y / 16 <= 1 + 1e-12);
            Assert.True(origin.X * origin.X / 2.25 + origin.Y * origin.Y / 0.5625 >= 1 - 1e-12);
            var expected = Normalize(origin - new Vector3D(0.5, -0.25, -40));
            Assert.Equal(1, Dot(expected, Normalize(direction)), 12);
        });
    }

    [Fact]
    public void NewVolumeSourcesCanEmitAcrossTheFullSphere()
    {
        var optic = Optic.CreateBlank();
        var document = StarOptProjectStore.CreateDefaultNonSequentialDocument(optic);
        document.Insert(0, NonSequentialObjectDefinition.Create(NonSequentialObjectKind.SourceVolumeRectangle) with
        {
            Parameters = new OptilandWorkbench.Core.NonSequential.SourceVolumeRectangleParameters(
                8,
                6,
                4,
                20,
                LayoutRayCount: 20,
                AnalysisRayCount: 1_000,
                AngularDistribution: CoreVolumeSourceAngularDistribution.UniformSphere)
        });

        var result = new NonSequentialDocumentTracer().Trace(
            document,
            optic.Materials,
            new NonSequentialDocumentTraceRequest(
                SplittingMode: OptilandWorkbench.Core.NonSequential.NonSequentialSplittingMode.None,
                RandomSeed: 23));
        var directions = result.Branches.Select(branch => branch.FinalDirection!.Value).ToArray();

        Assert.Equal(1_000, directions.Length);
        Assert.InRange(directions.Count(direction => direction.Z > 0), 400, 600);
        Assert.InRange(directions.Count(direction => direction.Z < 0), 400, 600);
        Assert.All(directions, direction => Assert.Equal(1, direction.Length, 12));
    }

    [Fact]
    public void PositionalSourceConstructorsKeepLegacyConeDistribution()
    {
        var optic = Optic.CreateBlank();
        var document = StarOptProjectStore.CreateDefaultNonSequentialDocument(optic);
        document.Insert(0, NonSequentialObjectDefinition.Create(NonSequentialObjectKind.SourceEllipse) with
        {
            Parameters = new OptilandWorkbench.Core.NonSequential.SourceEllipseParameters(10, 6, 10, 2, 1, 20, 200)
        });

        var result = new NonSequentialDocumentTracer().Trace(
            document,
            optic.Materials,
            new NonSequentialDocumentTraceRequest(
                SplittingMode: OptilandWorkbench.Core.NonSequential.NonSequentialSplittingMode.None,
                RandomSeed: 31));
        var minimumZ = Math.Cos(10 * Math.PI / 180);

        Assert.Equal(200, result.TotalBranchCount);
        Assert.All(result.Branches, branch =>
        {
            if (branch.FinalDirection is not { } direction)
            {
                throw new Xunit.Sdk.XunitException("Escaped branch did not publish a final direction.");
            }

            Assert.InRange(direction.Z, minimumZ - 1e-12, 1);
        });
        Assert.Contains(result.Branches, branch => branch.FinalDirection!.Value.Z < 0.999);
    }

    [Fact]
    public async Task ExtendedNativeSourcesRoundTripThroughStarOpt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sources-{Guid.NewGuid():N}.staropt");
        var kinds = new[]
        {
            ContractKind.SourceEllipse,
            ContractKind.SourceTwoAngle,
            ContractKind.SourceRadial,
            ContractKind.SourceVolumeRectangle,
            ContractKind.SourceVolumeEllipse,
            ContractKind.SourceVolumeCylinder
        };
        try
        {
            using (var application = WorkbenchApplication.Create("blank"))
            {
                foreach (var kind in kinds)
                {
                    Assert.IsAssignableFrom<OptilandWorkbench.Application.Contracts.SourceParameters>(
                        application.NonSequential.GetDefaultParameters(kind));
                    application.NonSequential.AddObject(kind);
                }
                await application.Documents.SaveAsync(path);
            }

            using var restored = WorkbenchApplication.Create("blank");
            await restored.Documents.OpenAsync(path);

            Assert.Equal(kinds, restored.NonSequential.GetDocument().Objects.Select(item => item.Kind));
            Assert.All(restored.NonSequential.GetDocument().Objects, item => Assert.Equal("光源", item.Role));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RadialSourceRejectsUnorderedOrNegativeDistribution()
    {
        var document = StarOptProjectStore.CreateDefaultNonSequentialDocument(Optic.CreateBlank());
        var source = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.SourceRadial) with
        {
            Parameters = new OptilandWorkbench.Core.NonSequential.SourceRadialParameters(new[]
            {
                new CoreSourceRadialSample(0, 1),
                new CoreSourceRadialSample(20, -0.1)
            })
        };

        Assert.Throws<InvalidDataException>(() => document.Insert(0, source));
        Assert.Empty(document.Objects);
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

    private static Vector3D Normalize(Vector3D value) => value / value.Length;

    private static double Dot(Vector3D left, Vector3D right) =>
        left.X * right.X + left.Y * right.Y + left.Z * right.Z;

    private static NonSequentialDocumentTraceResult TraceSource(
        NonSequentialObjectKind kind,
        CoreObjectParameters parameters)
    {
        var optic = Optic.CreateBlank();
        var document = StarOptProjectStore.CreateDefaultNonSequentialDocument(optic);
        document.Insert(0, NonSequentialObjectDefinition.Create(kind) with { Parameters = parameters });
        document.Insert(1, NonSequentialObjectDefinition.Create(NonSequentialObjectKind.DetectorRectangle) with
        {
            LocalCoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, 100)),
            Parameters = new DetectorRectangleParameters(1_000, 1_000, 10, 10)
        });
        return new NonSequentialDocumentTracer().Trace(
            document,
            optic.Materials,
            new NonSequentialDocumentTraceRequest(
                SplittingMode: OptilandWorkbench.Core.NonSequential.NonSequentialSplittingMode.None,
                RandomSeed: 42));
    }

    private static void AssertSourcePoint(NonSequentialObjectKind kind, Vector3D point)
    {
        const double tolerance = 1e-10;
        switch (kind)
        {
            case NonSequentialObjectKind.SourceEllipse:
                Assert.True(point.X * point.X / 25 + point.Y * point.Y / 9 <= 1 + tolerance);
                Assert.Equal(0, point.Z, 12);
                break;
            case NonSequentialObjectKind.SourceTwoAngle:
                Assert.InRange(point.X, -4, 4);
                Assert.InRange(point.Y, -2, 2);
                Assert.Equal(0, point.Z, 12);
                break;
            case NonSequentialObjectKind.SourceRadial:
                Assert.Equal(Vector3D.Zero, point);
                break;
            case NonSequentialObjectKind.SourceVolumeRectangle:
                Assert.InRange(point.X, -4, 4);
                Assert.InRange(point.Y, -3, 3);
                Assert.InRange(point.Z, -2, 2);
                break;
            case NonSequentialObjectKind.SourceVolumeEllipse:
                Assert.True(point.X * point.X / 16 + point.Y * point.Y / 9 + point.Z * point.Z / 4 <= 1 + tolerance);
                break;
            case NonSequentialObjectKind.SourceVolumeCylinder:
                Assert.True(point.X * point.X / 16 + point.Y * point.Y / 4 <= 1 + tolerance);
                Assert.InRange(point.Z, -3, 3);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }
}
