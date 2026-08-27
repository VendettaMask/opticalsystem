using System.Buffers.Binary;
using System.Text;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Coordinates;
using OptilandWorkbench.Core.NonSequential;
using OptilandWorkbench.Core.Rays;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;
using CoreKind = OptilandWorkbench.Core.NonSequential.NonSequentialObjectKind;
using CoreMeshParameters = OptilandWorkbench.Core.NonSequential.MeshObjectParameters;
using CoreBehavior = OptilandWorkbench.Core.NonSequential.NonSequentialSurfaceBehavior;

namespace OptilandWorkbench.Tests;

public sealed class NonSequentialStrayLightTests
{
    [Fact]
    public void AsciiStlImportCreatesClosedPositiveOrientedCube()
    {
        var asset = NonSequentialStlImporter.Import(
            Encoding.UTF8.GetBytes(CubeStl),
            "机械挡板 中文.stl");

        Assert.Equal(8, asset.VertexCount);
        Assert.Equal(12, asset.TriangleCount);
        Assert.True(asset.IsClosed);
        Assert.True(asset.IsManifold);
        Assert.True(asset.IsConnected);
        Assert.True(asset.IsOrientable);
        Assert.False(asset.HasSelfIntersections);
        Assert.Equal(1, asset.SignedVolumeCubicMillimeters, 10);
        Assert.Equal(64, asset.Sha256.Length);
        Assert.Equal(asset.VertexCount, asset.GetGeometry().Vertices.Count);
    }

    [Fact]
    public void BinaryStlIsDetectedAndUnitScaleIsApplied()
    {
        var bytes = BinaryTriangle();
        var asset = NonSequentialStlImporter.Import(bytes, "triangle.stl", OptilandWorkbench.Core.NonSequential.NonSequentialMeshUnit.Inch);

        Assert.Equal("Binary STL", asset.SourceFormat);
        Assert.Equal(25.4, asset.UnitScaleToMillimeters, 12);
        Assert.Equal(25.4, asset.BoundsMaximum.X, 5);
        Assert.False(asset.IsClosed);
    }

    [Fact]
    public void TruncatedStlAndCoplanarSelfIntersectionAreDetected()
    {
        var truncated = BinaryTriangle()[..^1];
        Assert.Throws<InvalidDataException>(() => NonSequentialStlImporter.Import(truncated, "truncated.stl"));

        const string overlapping = """
            solid overlap
              facet normal 0 0 1
                outer loop
                  vertex 0 0 0
                  vertex 2 0 0
                  vertex 0 2 0
                endloop
              endfacet
              facet normal 0 0 1
                outer loop
                  vertex 0.5 0.5 0
                  vertex 2.5 0.5 0
                  vertex 0.5 2.5 0
                endloop
              endfacet
            endsolid overlap
            """;
        var asset = NonSequentialStlImporter.Import(Encoding.UTF8.GetBytes(overlapping), "overlap.stl");
        Assert.True(asset.HasSelfIntersections);
    }

    [Fact]
    public void OpenMeshCannotBeUsedAsRefractiveSolid()
    {
        var optic = Optic.CreateBlank();
        var document = StarOptProjectStore.CreateDefaultNonSequentialDocument(optic);
        var asset = NonSequentialStlImporter.Import(BinaryTriangle(), "open.stl");
        document.AddMeshAsset(asset);

        Assert.Throws<InvalidDataException>(() => document.Insert(0,
            NonSequentialObjectDefinition.Create(CoreKind.SourceRay) with
            {
                Kind = CoreKind.Mesh,
                Parameters = new CoreMeshParameters(asset.Id, CoreBehavior.Refractive, "N-BK7")
            }));
        Assert.Empty(document.Objects);
    }

    [Fact]
    public void MeshParticipatesInNearestHitAndAbsorbsRay()
    {
        var optic = Optic.CreateBlank();
        var document = StarOptProjectStore.CreateDefaultNonSequentialDocument(optic);
        var asset = NonSequentialStlImporter.Import(Encoding.UTF8.GetBytes(CubeStl), "cube.stl");
        document.AddMeshAsset(asset);
        var mesh = new NonSequentialObjectDefinition(
            Guid.NewGuid(), "Cube", CoreKind.Mesh, true, true,
            new CoordinateSystem(new Vector3D(0, 0, 5)), null, null,
            new CoreMeshParameters(asset.Id, CoreBehavior.Absorbing));
        document.Insert(0, mesh);
        document.Insert(1, NonSequentialObjectDefinition.Create(CoreKind.DetectorRectangle) with
        {
            LocalCoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, 20))
        });

        var result = new NonSequentialDocumentTracer().Trace(
            document,
            optic.Materials,
            new NonSequentialDocumentTraceRequest(DirectRay: new RealRay(
                new Vector3D(0.5, 0.5, 0), new Vector3D(0, 0, 1), 587.5618)));

        var branch = Assert.Single(result.Branches);
        Assert.Equal(NonSequentialTerminationReason.Absorbed, branch.TerminationReason);
        Assert.Equal(mesh.Id, Assert.Single(branch.Segments).ObjectId);
        Assert.Equal(1, result.EnergyBalance.AbsorbedPowerWatts, 12);
    }

    [Fact]
    public async Task StarOptV3RoundTripEmbedsAndDeduplicatesMeshAsset()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mesh-{Guid.NewGuid():N}.staropt");
        try
        {
            var optic = Optic.CreateBlank();
            var document = StarOptProjectStore.CreateDefaultNonSequentialDocument(optic);
            var asset = NonSequentialStlImporter.Import(Encoding.UTF8.GetBytes(CubeStl), "中文机械件.stl");
            var firstId = document.AddMeshAsset(asset);
            var secondId = document.AddMeshAsset(asset with { Id = Guid.NewGuid() });
            Assert.Equal(firstId, secondId);
            document.Insert(0, new NonSequentialObjectDefinition(
                Guid.NewGuid(), "网格机械件", CoreKind.Mesh, true, true,
                CoordinateSystem.Global, null, null,
                new CoreMeshParameters(firstId, CoreBehavior.Reflective)));

            await StarOptProjectStore.SaveAsync(new StarOptProjectDocument(new[] { optic }, 0, NonSequentialDocument: document), path);
            var restored = await StarOptProjectStore.LoadAsync(path);
            var restoredDocument = Assert.IsType<NonSequentialDocument>(restored.NonSequentialDocument);
            var restoredAsset = Assert.Single(restoredDocument.MeshAssets);

            Assert.True(restoredAsset.HasGeometry);
            Assert.Equal(asset.Sha256, restoredAsset.Sha256);
            Assert.Equal("中文机械件.stl", restoredAsset.OriginalFileName);
            Assert.Equal(12, restoredAsset.GetGeometry().Triangles.Count);
            Assert.Equal(CoreKind.Mesh, Assert.Single(restoredDocument.Objects).Kind);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void PathFilterSupportsPrecedenceSequenceMissAndErrorPosition()
    {
        var optic = Optic.CreateBlank();
        var document = StarOptProjectStore.CreateDefaultNonSequentialDocument(optic);
        var source = NonSequentialObjectDefinition.Create(CoreKind.SourceRay);
        var detector = NonSequentialObjectDefinition.Create(CoreKind.DetectorRectangle) with
        {
            LocalCoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, 10))
        };
        document.Insert(0, source);
        document.Insert(1, detector);
        var branch = Assert.Single(new NonSequentialDocumentTracer().Trace(document, optic.Materials).Branches);

        Assert.True(NonSequentialPathFilter.Parse("Q1 & D2 | A").IsMatch(document, branch));
        Assert.True(NonSequentialPathFilter.Parse("SEQ(Q1,H2,D2) & M1").IsMatch(document, branch));
        Assert.False(NonSequentialPathFilter.Parse("A | E").IsMatch(document, branch));
        var error = Assert.Throws<NonSequentialPathFilterException>(() => NonSequentialPathFilter.Parse("Q1 & Z2"));
        Assert.Equal(5, error.Position);
    }

    [Fact]
    public void RayDatabaseRoundTripValidatesChunksAndReportsStaleScene()
    {
        var optic = Optic.CreateBlank();
        var document = StarOptProjectStore.CreateDefaultNonSequentialDocument(optic);
        document.Insert(0, NonSequentialObjectDefinition.Create(CoreKind.SourceRay));
        document.Insert(1, NonSequentialObjectDefinition.Create(CoreKind.DetectorRectangle) with
        {
            LocalCoordinateSystem = new CoordinateSystem(new Vector3D(0, 0, 10))
        });
        using var stream = new MemoryStream();
        using (var writer = new NonSequentialRayDatabaseWriter(
            stream,
            NonSequentialRayDatabaseHeader.Create(document),
            leaveOpen: true))
        {
            var trace = new NonSequentialDocumentTracer().Trace(
                document,
                optic.Materials,
                new NonSequentialDocumentTraceRequest(OutputMode: OptilandWorkbench.Core.NonSequential.NonSequentialTraceOutputMode.RayDatabase),
                writer);
            writer.Complete();
            Assert.Empty(trace.Branches);
            Assert.Equal(1, trace.TotalBranchCount);
        }

        stream.Position = 0;
        using var reader = new NonSequentialRayDatabaseReader(stream, leaveOpen: true);
        var branch = Assert.Single(reader.ReadAllBranches(NonSequentialPathFilter.Parse("D2")));
        Assert.Equal(NonSequentialTerminationReason.DetectorHit, branch.TerminationReason);
        Assert.False(reader.IsStale(document));
        document.Insert(2, NonSequentialObjectDefinition.Create(CoreKind.Box));
        Assert.True(reader.IsStale(document));
    }

    [Fact]
    public void RayDatabaseRejectsCorruptedCompressedChunk()
    {
        var optic = Optic.CreateBlank();
        var document = StarOptProjectStore.CreateDefaultNonSequentialDocument(optic);
        document.Insert(0, NonSequentialObjectDefinition.Create(CoreKind.SourceRay));
        using var stream = new MemoryStream();
        using (var writer = new NonSequentialRayDatabaseWriter(
            stream,
            NonSequentialRayDatabaseHeader.Create(document),
            leaveOpen: true))
        {
            var branch = Assert.Single(new NonSequentialDocumentTracer().Trace(document, optic.Materials).Branches);
            writer.OnBranch(branch);
            writer.Complete();
        }
        var bytes = stream.ToArray();
        var headerCompressedLength = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16, 4));
        var chunkPayload = 52 + headerCompressedLength + 48;
        bytes[chunkPayload + 1] ^= 0x40;
        using var corrupted = new MemoryStream(bytes);
        using var reader = new NonSequentialRayDatabaseReader(corrupted);
        Assert.Throws<InvalidDataException>(() => reader.ReadAllBranches());
    }

    [Fact]
    public async Task ApplicationDatabaseWriteIsAtomicAndOpenProducesPathSummary()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rays-{Guid.NewGuid():N}.starrdb");
        try
        {
            using var application = WorkbenchApplication.Create("blank");
            application.NonSequential.AddObject(OptilandWorkbench.Application.Contracts.NonSequentialObjectKind.SourceRay);
            var detectorId = application.NonSequential.AddObject(OptilandWorkbench.Application.Contracts.NonSequentialObjectKind.DetectorRectangle);
            var detector = application.NonSequential.GetDocument().Objects.Single(item => item.Id == detectorId);
            application.NonSequential.UpdateObject(new NonSequentialObjectUpdateDto(
                detector.Id, true, true, detector.Kind, detector.Name, null, null,
                0, 0, 10, 0, 0, 0, detector.Parameters));

            var result = await application.NonSequential.TraceAsync(new NonSequentialTraceRunRequestDto(
                OptilandWorkbench.Application.Contracts.NonSequentialTraceOutputMode.RayDatabase,
                RayDatabasePath: path));
            var database = application.NonSequential.OpenRayDatabase(path);

            Assert.True(result.RayDatabaseBytes > 0);
            Assert.Equal(1, database.BranchCount);
            Assert.False(database.IsStale);
            Assert.Contains(database.Paths, item => item.TerminationReason == nameof(NonSequentialTerminationReason.DetectorHit));
            Assert.DoesNotContain(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.*.tmp"), _ => true);

            application.NonSequential.OpenRayDatabase(path, "E");
            application.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);
            var parameters = application.Analyses.GetParameters("Non-Sequential Detector Viewer");
            var detectorView = await application.Analyses.RunAsync(new AnalysisRequestDto(
                Guid.NewGuid(),
                1,
                "Non-Sequential Detector Viewer",
                parameters.ToDictionary(item => item.Key, item => item.DefaultValue)));
            Assert.Contains(detectorView.View.Rows, row => row.Metric == "Source" && row.Value == "Filtered ray database");
            Assert.All(detectorView.View.Series.SelectMany(series => series.Points), point => Assert.Equal(0, point.Value ?? 0, 12));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task CancelledDatabaseTracePreservesExistingTargetAndDeletesTemporaryFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cancel-{Guid.NewGuid():N}.starrdb");
        var original = Encoding.UTF8.GetBytes("existing database remains");
        await File.WriteAllBytesAsync(path, original);
        try
        {
            using var application = WorkbenchApplication.Create("blank");
            application.NonSequential.AddObject(OptilandWorkbench.Application.Contracts.NonSequentialObjectKind.SourceRay);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                application.NonSequential.TraceAsync(new NonSequentialTraceRunRequestDto(
                    OptilandWorkbench.Application.Contracts.NonSequentialTraceOutputMode.RayDatabase,
                    RayDatabasePath: path), cancellation.Token));

            Assert.Equal(original, await File.ReadAllBytesAsync(path));
            Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.*.tmp"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task AnalysisSessionAccumulatesTraceOnlyAndDrivesDetectorAndDatabaseViews()
    {
        using var application = WorkbenchApplication.Create("blank");
        application.NonSequential.AddObject(OptilandWorkbench.Application.Contracts.NonSequentialObjectKind.SourceRay);
        var detectorId = application.NonSequential.AddObject(
            OptilandWorkbench.Application.Contracts.NonSequentialObjectKind.DetectorRectangle);
        var detector = application.NonSequential.GetDocument().Objects.Single(item => item.Id == detectorId);
        application.NonSequential.UpdateObject(new NonSequentialObjectUpdateDto(
            detector.Id, true, true, detector.Kind, detector.Name, null, null,
            0, 0, 10, 0, 0, 0, detector.Parameters));

        var first = await application.NonSequentialAnalysis.TraceAsync(new NonSequentialTraceRunRequestDto());
        var firstSession = application.NonSequentialAnalysis.GetCurrentSession();

        Assert.NotNull(firstSession);
        Assert.Equal(1, firstSession!.TracePassCount);
        Assert.True(firstSession.IsTemporaryDatabase);
        Assert.True(File.Exists(firstSession.RayDatabasePath));
        Assert.Equal(1, first.TotalBranchCount);

        var second = await application.NonSequentialAnalysis.TraceAsync(new NonSequentialTraceRunRequestDto(
            Command: NonSequentialTraceCommand.TraceOnly));
        var accumulated = application.NonSequentialAnalysis.GetCurrentSession();
        var view = application.NonSequentialAnalysis.GetDetectorView(new NonSequentialDetectorViewRequestDto(
            detectorId,
            DataType: NonSequentialDetectorDataType.PixelPower));
        var angularHits = application.NonSequentialAnalysis.GetDetectorView(new NonSequentialDetectorViewRequestDto(
            detectorId,
            Space: NonSequentialDetectorSpace.Angle,
            DataType: NonSequentialDetectorDataType.HitCount));
        var page = application.NonSequentialAnalysis.GetRayDatabasePage(pageSize: 10);

        Assert.Equal(2, accumulated!.TracePassCount);
        Assert.Equal(2, accumulated.BranchCount);
        Assert.Equal(2, view.Statistics.TotalHits);
        Assert.Equal(2.0, view.Statistics.TotalPowerWatts, 12);
        Assert.Equal(2, angularHits.Values.Sum());
        Assert.Equal(2, angularHits.Statistics.TotalHits);
        Assert.Equal(new long[] { 1, 2 }, page.Branches.Select(branch => branch.Id));
        Assert.Equal(1, second.TracePassCount - first.TracePassCount);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            application.NonSequentialAnalysis.TraceAsync(new NonSequentialTraceRunRequestDto(
                Command: NonSequentialTraceCommand.TraceOnly,
                MaximumSegmentsPerRay: 25)));

        var managedPath = accumulated.RayDatabasePath;
        application.NonSequentialAnalysis.ClearDetectors();
        Assert.Null(application.NonSequentialAnalysis.GetCurrentSession());
        Assert.False(File.Exists(managedPath));
    }

    [Fact]
    public async Task OpeningExternalDatabaseReleasesReplacedManagedSession()
    {
        var externalPath = Path.Combine(Path.GetTempPath(), $"external-{Guid.NewGuid():N}.starrdb");
        try
        {
            using var application = WorkbenchApplication.Create("blank");
            application.NonSequential.AddObject(OptilandWorkbench.Application.Contracts.NonSequentialObjectKind.SourceRay);
            var trace = await application.NonSequentialAnalysis.TraceAsync(new NonSequentialTraceRunRequestDto());
            var managedPath = Assert.IsType<string>(trace.RayDatabasePath);
            File.Copy(managedPath, externalPath);

            application.NonSequentialAnalysis.OpenRayDatabase(externalPath);

            Assert.False(File.Exists(managedPath));
            application.NonSequentialAnalysis.ClearDetectors();
            Assert.True(File.Exists(externalPath));
        }
        finally
        {
            if (File.Exists(externalPath)) File.Delete(externalPath);
        }
    }

    private static byte[] BinaryTriangle()
    {
        var bytes = new byte[84 + 50];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(80, 4), 1);
        Write(84 + 12, 0, 0, 0);
        Write(84 + 24, 1, 0, 0);
        Write(84 + 36, 0, 1, 0);
        return bytes;

        void Write(int offset, float x, float y, float z)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset, 4), x);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + 4, 4), y);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset + 8, 4), z);
        }
    }

    private const string CubeStl = """
        solid cube
          facet normal 0 0 -1
            outer loop
              vertex 0 0 0
              vertex 1 1 0
              vertex 1 0 0
            endloop
          endfacet
          facet normal 0 0 -1
            outer loop
              vertex 0 0 0
              vertex 0 1 0
              vertex 1 1 0
            endloop
          endfacet
          facet normal 0 0 1
            outer loop
              vertex 0 0 1
              vertex 1 0 1
              vertex 1 1 1
            endloop
          endfacet
          facet normal 0 0 1
            outer loop
              vertex 0 0 1
              vertex 1 1 1
              vertex 0 1 1
            endloop
          endfacet
          facet normal 0 -1 0
            outer loop
              vertex 0 0 0
              vertex 1 0 0
              vertex 1 0 1
            endloop
          endfacet
          facet normal 0 -1 0
            outer loop
              vertex 0 0 0
              vertex 1 0 1
              vertex 0 0 1
            endloop
          endfacet
          facet normal 1 0 0
            outer loop
              vertex 1 0 0
              vertex 1 1 0
              vertex 1 1 1
            endloop
          endfacet
          facet normal 1 0 0
            outer loop
              vertex 1 0 0
              vertex 1 1 1
              vertex 1 0 1
            endloop
          endfacet
          facet normal 0 1 0
            outer loop
              vertex 1 1 0
              vertex 0 1 0
              vertex 0 1 1
            endloop
          endfacet
          facet normal 0 1 0
            outer loop
              vertex 1 1 0
              vertex 0 1 1
              vertex 1 1 1
            endloop
          endfacet
          facet normal -1 0 0
            outer loop
              vertex 0 1 0
              vertex 0 0 0
              vertex 0 0 1
            endloop
          endfacet
          facet normal -1 0 0
            outer loop
              vertex 0 1 0
              vertex 0 0 1
              vertex 0 1 1
            endloop
          endfacet
        endsolid cube
        """;
}
