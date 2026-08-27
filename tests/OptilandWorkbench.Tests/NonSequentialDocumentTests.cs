using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.NonSequential;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;
using ContractDetectorRectangleParameters = OptilandWorkbench.Application.Contracts.DetectorRectangleParameters;
using ContractKind = OptilandWorkbench.Application.Contracts.NonSequentialObjectKind;
using ContractSphereParameters = OptilandWorkbench.Application.Contracts.SphereParameters;
using BoxParameters = OptilandWorkbench.Core.NonSequential.BoxParameters;
using CylinderParameters = OptilandWorkbench.Core.NonSequential.CylinderParameters;
using DetectorRectangleParameters = OptilandWorkbench.Core.NonSequential.DetectorRectangleParameters;
using NonSequentialObjectKind = OptilandWorkbench.Core.NonSequential.NonSequentialObjectKind;
using NonSequentialObjectParameters = OptilandWorkbench.Core.NonSequential.NonSequentialObjectParameters;
using NonSequentialSurfaceBehavior = OptilandWorkbench.Core.NonSequential.NonSequentialSurfaceBehavior;
using NonSequentialTraceSettings = OptilandWorkbench.Core.NonSequential.NonSequentialTraceSettings;
using PlaneRectangleParameters = OptilandWorkbench.Core.NonSequential.PlaneRectangleParameters;
using SourceRectangleParameters = OptilandWorkbench.Core.NonSequential.SourceRectangleParameters;
using SphereParameters = OptilandWorkbench.Core.NonSequential.SphereParameters;
using StandardLensParameters = OptilandWorkbench.Core.NonSequential.StandardLensParameters;

namespace OptilandWorkbench.Tests;

public sealed class NonSequentialDocumentTests
{
    [Fact]
    public async Task StarOptVersionOneMigratesToEmptyIndependentScene()
    {
        var optic = Optic.CreateDemo();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            FormatVersion = 1,
            Application = "Optical System Design",
            ActiveConfigurationIndex = 0,
            Configurations = new[] { optic.ToSnapshot() }
        });
        var bytes = CreateStarOptContainer(payload);
        var path = Path.Combine(Path.GetTempPath(), $"staropt-v1-{Guid.NewGuid():N}.staropt");
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            var migrated = await StarOptProjectStore.LoadAsync(path);

            Assert.Empty(Assert.IsType<NonSequentialDocument>(migrated.NonSequentialDocument).Objects);
            Assert.Equal(
                optic.Wavelengths.Select(wavelength => wavelength.Nanometers),
                migrated.NonSequentialDocument!.Wavelengths.Select(wavelength => wavelength.Nanometers));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ReferenceTransformUsesStableIdsAndZyxRotationChain()
    {
        var optic = Optic.CreateBlank();
        var document = StarOptProjectStore.CreateDefaultNonSequentialDocument(optic);
        var parent = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.Box) with
        {
            LocalCoordinateSystem = new CoordinateSystem(new Vector3D(10, 0, 0), RotationZDegrees: 90)
        };
        var child = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.SourceRay) with
        {
            ReferenceObjectId = parent.Id,
            LocalCoordinateSystem = new CoordinateSystem(new Vector3D(2, 0, 0))
        };
        document.Insert(0, parent);
        document.Insert(1, child);

        var world = document.ToWorldPoint(child.Id, Vector3D.Zero);

        Assert.Equal(10, world.X, 10);
        Assert.Equal(2, world.Y, 10);
        Assert.Equal(0, world.Z, 10);
    }

    [Fact]
    public void ReferenceCyclesAndReferencedDeletesAreRejectedWithoutMutation()
    {
        using var application = WorkbenchApplication.Create("blank");
        var firstId = application.NonSequential.AddObject(ContractKind.Box);
        var secondId = application.NonSequential.AddObject(ContractKind.Sphere);
        var second = application.NonSequential.GetDocument().Objects.Single(item => item.Id == secondId);
        application.NonSequential.UpdateObject(ToUpdate(second) with { ReferenceObjectId = firstId });
        var first = application.NonSequential.GetDocument().Objects.Single(item => item.Id == firstId);

        Assert.Throws<InvalidDataException>(() =>
            application.NonSequential.UpdateObject(ToUpdate(first) with { ReferenceObjectId = secondId }));
        Assert.Throws<InvalidOperationException>(() => application.NonSequential.DeleteObject(firstId));
        Assert.Equal(2, application.NonSequential.GetDocument().Objects.Count);
        Assert.Null(application.NonSequential.GetDocument().Objects.Single(item => item.Id == firstId).ReferenceObjectId);
    }

    [Fact]
    public void InvalidMaterialEditRollsBackAndSingleUndoRestoresAdd()
    {
        using var application = WorkbenchApplication.Create("blank");
        var id = application.NonSequential.AddObject(ContractKind.Sphere);
        var row = application.NonSequential.GetDocument().Objects.Single(item => item.Id == id);

        Assert.Throws<KeyNotFoundException>(() => application.NonSequential.UpdateObject(
            ToUpdate(row) with { Parameters = ((ContractSphereParameters)row.Parameters) with { Material = "NOT-A-GLASS" } }));
        Assert.Equal("N-BK7", ((ContractSphereParameters)application.NonSequential.GetDocument().Objects.Single().Parameters).Material);

        Assert.True(application.Documents.Undo());
        Assert.Empty(application.NonSequential.GetDocument().Objects);
        Assert.True(application.Documents.Redo());
        Assert.Single(application.NonSequential.GetDocument().Objects);
    }

    [Fact]
    public void SwitchingModesDoesNotDirtyOrConvertScene()
    {
        using var application = WorkbenchApplication.Create("cooke");
        Assert.False(application.Documents.GetSnapshot().IsDirty);

        application.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);
        application.Modes.SwitchTo(OpticalWorkbenchMode.Sequential);

        Assert.Empty(application.NonSequential.GetDocument().Objects);
        Assert.False(application.Documents.GetSnapshot().IsDirty);
    }

    [Fact]
    public void ExplicitSequentialConversionCreatesIndependentLensAndDetectorWithoutSource()
    {
        using var application = WorkbenchApplication.Create("cooke");

        var result = application.NonSequential.ConvertFromSequential();
        var objects = application.NonSequential.GetDocument().Objects;

        Assert.True(result.ObjectCount > 0);
        Assert.Contains(objects, item => item.Kind == ContractKind.StandardLens);
        Assert.Equal(ContractKind.DetectorRectangle, objects[^1].Kind);
        Assert.DoesNotContain(objects, item => item.Parameters is OptilandWorkbench.Application.Contracts.SourceParameters);
        Assert.Contains(result.Warnings, warning => warning.Contains("光源", StringComparison.Ordinal));
    }

    [Fact]
    public void SeededRectangleSourceIsDeterministic()
    {
        var optic = Optic.CreateBlank();
        var document = StarOptProjectStore.CreateDefaultNonSequentialDocument(optic);
        document.Insert(0, NonSequentialObjectDefinition.Create(NonSequentialObjectKind.SourceRectangle) with
        {
            Parameters = new SourceRectangleParameters(4, 3, 10, AnalysisRayCount: 12)
        });
        document.Insert(1, NonSequentialObjectDefinition.Create(NonSequentialObjectKind.DetectorRectangle) with
        {
            LocalCoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, 10)),
            Parameters = new DetectorRectangleParameters(100, 100, 10, 10)
        });
        var tracer = new NonSequentialDocumentTracer();

        var first = tracer.Trace(document, optic.Materials);
        var second = tracer.Trace(document, optic.Materials);

        Assert.Equal(
            first.Branches.SelectMany(branch => branch.Segments).Select(segment => segment.End).ToArray(),
            second.Branches.SelectMany(branch => branch.Segments).Select(segment => segment.End).ToArray());
        Assert.Equal(first.Detectors.Single().PowerByWavelength[1], second.Detectors.Single().PowerByWavelength[1]);
    }

    [Theory]
    [InlineData(NonSequentialObjectKind.Sphere)]
    [InlineData(NonSequentialObjectKind.Cylinder)]
    [InlineData(NonSequentialObjectKind.Box)]
    public void SolidPrimitiveIntersectionsAreIndependentOfObjectOrder(NonSequentialObjectKind kind)
    {
        var optic = Optic.CreateBlank();
        var document = StarOptProjectStore.CreateDefaultNonSequentialDocument(optic);
        var far = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.DetectorRectangle) with
        {
            LocalCoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, 30))
        };
        var solid = NonSequentialObjectDefinition.Create(kind) with
        {
            LocalCoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, 10)),
            Parameters = ReflectiveParameters(kind)
        };
        document.Insert(0, far);
        document.Insert(1, solid);
        var ray = new OptilandWorkbench.Core.Rays.RealRay(new Vector3D(0, 0, -10), new Vector3D(0, 0, 1), 587.5618);

        var result = new NonSequentialDocumentTracer().Trace(
            document,
            optic.Materials,
            new NonSequentialDocumentTraceRequest(DirectRay: ray));

        Assert.Equal(solid.Id, result.Branches.SelectMany(branch => branch.Segments).First().ObjectId);
    }

    [Fact]
    public void FrontOnlyDetectorIgnoresBacksideRay()
    {
        var optic = Optic.CreateBlank();
        var document = StarOptProjectStore.CreateDefaultNonSequentialDocument(optic);
        document.Insert(0, NonSequentialObjectDefinition.Create(NonSequentialObjectKind.DetectorRectangle) with
        {
            LocalCoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, 5)),
            Parameters = new DetectorRectangleParameters(20, 20, 10, 10, FrontOnly: true)
        });
        var ray = new OptilandWorkbench.Core.Rays.RealRay(new Vector3D(0, 0, 10), new Vector3D(0, 0, -1), 587.5618);

        var result = new NonSequentialDocumentTracer().Trace(
            document,
            optic.Materials,
            new NonSequentialDocumentTraceRequest(DirectRay: ray));

        Assert.Equal(0, result.EnergyBalance.DetectorPowerWatts, 12);
        Assert.Equal(1, result.EnergyBalance.EscapedPowerWatts, 12);
    }

    [Fact]
    public void NonAbsorbingDetectorRecordsPowerAndAllowsRayToReachFollowingDetector()
    {
        var optic = Optic.CreateBlank();
        var document = StarOptProjectStore.CreateDefaultNonSequentialDocument(optic);
        document.Insert(0, NonSequentialObjectDefinition.Create(NonSequentialObjectKind.SourceRay));
        document.Insert(1, NonSequentialObjectDefinition.Create(NonSequentialObjectKind.DetectorRectangle, "近场") with
        {
            LocalCoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, 10)),
            Parameters = new DetectorRectangleParameters(20, 20, 20, 20, Absorb: false)
        });
        document.Insert(2, NonSequentialObjectDefinition.Create(NonSequentialObjectKind.DetectorRectangle, "远场") with
        {
            LocalCoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, 20)),
            Parameters = new DetectorRectangleParameters(20, 20, 20, 20)
        });

        var result = new NonSequentialDocumentTracer().Trace(document, optic.Materials);

        Assert.Equal(2, result.Detectors.Count);
        Assert.All(result.Detectors, detector => Assert.Equal(1, detector.TotalPowerWatts, 12));
        Assert.Equal(1, result.EnergyBalance.DetectorPowerWatts, 12);
        Assert.Equal(NonSequentialTerminationReason.DetectorHit, Assert.Single(result.Branches).TerminationReason);
    }

    [Fact]
    public void StandardLensProducesTwoSolidBoundaryHitsAndReachesDetector()
    {
        var optic = Optic.CreateBlank();
        var document = StarOptProjectStore.CreateDefaultNonSequentialDocument(optic);
        document.Insert(0, NonSequentialObjectDefinition.Create(NonSequentialObjectKind.SourceRay) with
        {
            LocalCoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, -10))
        });
        var lens = NonSequentialObjectDefinition.Create(NonSequentialObjectKind.StandardLens) with
        {
            Parameters = new StandardLensParameters(50, -50, 0, 0, 5, 10, "N-BK7")
        };
        document.Insert(1, lens);
        document.Insert(2, NonSequentialObjectDefinition.Create(NonSequentialObjectKind.DetectorRectangle) with
        {
            LocalCoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, 20)),
            Parameters = new DetectorRectangleParameters(30, 30, 10, 10)
        });

        var result = new NonSequentialDocumentTracer().Trace(
            document,
            optic.Materials,
            new NonSequentialDocumentTraceRequest(SplitFresnelRays: false));
        var lensHits = result.Branches.SelectMany(branch => branch.Segments)
            .Where(segment => segment.ObjectId == lens.Id).ToArray();

        Assert.Equal(2, lensHits.Length);
        Assert.Equal(new[] { 1, 2 }, lensHits.Select(segment => segment.FaceNumber));
        Assert.InRange(result.EnergyBalance.DetectorPowerWatts, 0.9, 1);
        Assert.Equal(1, result.EnergyBalance.AccountedPowerWatts, 10);
    }

    [Fact]
    public async Task NonSequentialVisualizationReadsIndependentObjectsAndTraceTree()
    {
        using var application = WorkbenchApplication.Create("blank");
        application.NonSequential.AddObject(ContractKind.SourceRay);
        application.NonSequential.AddObject(ContractKind.Box);
        var detectorId = application.NonSequential.AddObject(ContractKind.DetectorRectangle);
        var detector = application.NonSequential.GetDocument().Objects.Single(item => item.Id == detectorId);
        application.NonSequential.UpdateObject(ToUpdate(detector) with { Z = 20 });
        application.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);

        var scene = await application.Visualization.BuildSceneAsync(SceneDimension.ThreeDimensional);
        var threeDimensional = Assert.IsType<Scene3Dto>(scene.ThreeDimensional);

        Assert.Equal(3, threeDimensional.Surfaces.Count);
        Assert.NotEmpty(threeDimensional.Surfaces.SelectMany(surface => surface.Faces));
        Assert.Equal(SceneSurfaceRenderRole.Source, threeDimensional.Surfaces[0].RenderRole);
        Assert.Equal(SceneSurfaceRenderRole.NonSequentialObject, threeDimensional.Surfaces[1].RenderRole);
        Assert.Equal(SceneSurfaceRenderRole.Detector, threeDimensional.Surfaces[2].RenderRole);
        Assert.False(threeDimensional.Surfaces[2].IsReferencePlane);
        Assert.Empty(threeDimensional.Surfaces[1].Rim);
        Assert.Equal(5, threeDimensional.Surfaces[2].Rim.Count);
        Assert.Equal(threeDimensional.Surfaces[2].Rim[0], threeDimensional.Surfaces[2].Rim[^1]);
        Assert.Empty(threeDimensional.Rays);
        Assert.Empty(threeDimensional.LensElements);

        await application.NonSequentialAnalysis.TraceAsync(new NonSequentialTraceRunRequestDto());
        var traced = await application.Visualization.BuildSceneAsync(SceneDimension.ThreeDimensional);
        Assert.NotEmpty(Assert.IsType<Scene3Dto>(traced.ThreeDimensional).Rays);
    }

    [Fact]
    public async Task LayoutSessionIsCreatedOnceAndReusedUntilSceneChanges()
    {
        using var application = WorkbenchApplication.Create("blank");
        application.NonSequential.AddObject(ContractKind.SourcePoint);
        var detectorId = application.NonSequential.AddObject(ContractKind.DetectorRectangle);
        var detector = application.NonSequential.GetDocument().Objects.Single(item => item.Id == detectorId);
        application.NonSequential.UpdateObject(ToUpdate(detector) with { Z = 20 });
        application.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);

        var first = await application.NonSequentialAnalysis.EnsureLayoutSessionAsync();
        var second = await application.NonSequentialAnalysis.EnsureLayoutSessionAsync();

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, second.TracePassCount);
        Assert.Equal(20, second.BranchCount);
        var scene = await application.Visualization.BuildSceneAsync(SceneDimension.ThreeDimensional);
        Assert.Equal(20, Assert.IsType<Scene3Dto>(scene.ThreeDimensional).Rays.Count);

        var source = application.NonSequential.GetDocument().Objects[0];
        application.NonSequential.UpdateObject(ToUpdate(source) with { X = 1 });
        Assert.True(application.NonSequentialAnalysis.GetCurrentSession()!.IsStale);

        var refreshed = await application.NonSequentialAnalysis.EnsureLayoutSessionAsync();

        Assert.NotEqual(first.Id, refreshed.Id);
        Assert.False(refreshed.IsStale);
    }

    [Fact]
    public void NewTracerTerminatesMirrorCavityAtMaximumSegments()
    {
        var optic = Optic.CreateBlank();
        var baseDocument = StarOptProjectStore.CreateDefaultNonSequentialDocument(optic);
        var document = new NonSequentialDocument(
            baseDocument.Name,
            baseDocument.Wavelengths,
            new[]
            {
                NonSequentialObjectDefinition.Create(NonSequentialObjectKind.SourceRay) with
                {
                    LocalCoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, 5))
                },
                NonSequentialObjectDefinition.Create(NonSequentialObjectKind.PlaneRectangle) with
                {
                    Parameters = new PlaneRectangleParameters(20, 20, NonSequentialSurfaceBehavior.Reflective)
                },
                NonSequentialObjectDefinition.Create(NonSequentialObjectKind.PlaneRectangle) with
                {
                    LocalCoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, 10)),
                    Parameters = new PlaneRectangleParameters(20, 20, NonSequentialSurfaceBehavior.Reflective)
                }
            },
            traceSettings: new NonSequentialTraceSettings(MaximumSegmentsPerRay: 3));

        var result = new NonSequentialDocumentTracer().Trace(document, optic.Materials);

        Assert.Equal(NonSequentialTerminationReason.MaximumSegments, Assert.Single(result.Branches).TerminationReason);
        Assert.Equal(3, result.SegmentCount);
    }

    private static NonSequentialObjectUpdateDto ToUpdate(NonSequentialObjectRowDto row) => new(
        row.Id, row.Enabled, row.Visible, row.Kind, row.Name, row.ReferenceObjectId, row.ContainingObjectId,
        row.X, row.Y, row.Z, row.TiltXDegrees, row.TiltYDegrees, row.TiltZDegrees, row.Parameters);

    private static NonSequentialObjectParameters ReflectiveParameters(NonSequentialObjectKind kind) => kind switch
    {
        NonSequentialObjectKind.Sphere => new SphereParameters(5, Behavior: NonSequentialSurfaceBehavior.Reflective),
        NonSequentialObjectKind.Cylinder => new CylinderParameters(5, 10, Behavior: NonSequentialSurfaceBehavior.Reflective),
        NonSequentialObjectKind.Box => new BoxParameters(10, 10, 10, Behavior: NonSequentialSurfaceBehavior.Reflective),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static byte[] CreateStarOptContainer(byte[] payload)
    {
        byte[] compressed;
        using (var output = new MemoryStream())
        {
            using (var brotli = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                brotli.Write(payload);
            }

            compressed = output.ToArray();
        }

        const int headerLength = 52;
        var bytes = new byte[headerLength + compressed.Length];
        "STAROPT\x1a"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10, 2), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12, 4), payload.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16, 4), compressed.Length);
        SHA256.HashData(payload).CopyTo(bytes, 20);
        compressed.CopyTo(bytes, headerLength);
        return bytes;
    }
}
