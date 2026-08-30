using System.Buffers.Binary;
using System.Text;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Panels;
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
    public void DetectorDisplayPeakNormalizationDoesNotMutatePhysicalValues()
    {
        double[] physicalValues = [0, 2, 4, 1];

        var display = NonSequentialDetectorDisplay.Transform(
            physicalValues,
            width: 2,
            height: 2,
            valueUnit: "W/mm2",
            DetectorDisplayNormalization.Peak,
            smoothingRadius: 0,
            logarithmic: false,
            manualMinimum: null,
            manualMaximum: null);

        Assert.Equal([0, 2, 4, 1], physicalValues);
        Assert.Equal([0, 0.5, 1, 0.25], display.Values);
        Assert.Equal("peak-normalized", display.ValueUnit);
    }

    [Fact]
    public void DetectorDisplayBoxSmoothingUsesOnlyAvailableEdgePixels()
    {
        double[] impulse =
        [
            0, 0, 0,
            0, 9, 0,
            0, 0, 0
        ];

        var display = NonSequentialDetectorDisplay.Transform(
            impulse,
            width: 3,
            height: 3,
            valueUnit: "W",
            DetectorDisplayNormalization.Absolute,
            smoothingRadius: 1,
            logarithmic: false,
            manualMinimum: null,
            manualMaximum: null);

        Assert.Equal(1, display.Values[4], precision: 12);
        Assert.Equal(2.25, display.Values[0], precision: 12);
        Assert.Equal(1.5, display.Values[1], precision: 12);
    }

    [Fact]
    public void DetectorDisplayProfilesUsePixelCenterPhysicalCoordinates()
    {
        var view = DetectorView(
            pixelsX: 3,
            pixelsY: 2,
            values: [1, 2, 3, 4, 5, 6]);

        var xProfile = NonSequentialDetectorDisplay.Profile(
            view,
            view.Values,
            DetectorProfileAxis.X,
            index: 1);
        var yProfile = NonSequentialDetectorDisplay.Profile(
            view,
            view.Values,
            DetectorProfileAxis.Y,
            index: 1);

        Assert.Equal([-2, 0, 2], xProfile.Select(point => point.X));
        Assert.Equal([4, 5, 6], xProfile.Select(point => point.Y));
        Assert.Equal([-1, 1], yProfile.Select(point => point.X));
        Assert.Equal([2, 5], yProfile.Select(point => point.Y));
    }

    [Fact]
    public void DetectorDisplayRejectsIncompleteOrReversedManualRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NonSequentialDetectorDisplay.Transform(
            [1.0], 1, 1, "W", DetectorDisplayNormalization.Absolute, 0, false, 0, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => NonSequentialDetectorDisplay.Transform(
            [1.0], 1, 1, "W", DetectorDisplayNormalization.Absolute, 0, false, 1, 1));
    }

    [Theory]
    [InlineData(NonSequentialDetectorDataType.PixelPower, AnalysisAxisQuantity.Power, AnalysisAxisUnit.Watt)]
    [InlineData(NonSequentialDetectorDataType.IncoherentIrradiance, AnalysisAxisQuantity.Irradiance, AnalysisAxisUnit.WattsPerSquareMillimeter)]
    [InlineData(NonSequentialDetectorDataType.HitCount, AnalysisAxisQuantity.Count, AnalysisAxisUnit.Dimensionless)]
    [InlineData(NonSequentialDetectorDataType.RadiantIntensity, AnalysisAxisQuantity.Intensity, AnalysisAxisUnit.WattsPerSteradian)]
    public void DetectorDisplayPublishesTypedPhysicalValueAxes(
        NonSequentialDetectorDataType dataType,
        AnalysisAxisQuantity expectedQuantity,
        AnalysisAxisUnit expectedUnit)
    {
        Assert.Equal(
            (expectedQuantity, expectedUnit),
            NonSequentialDetectorDisplay.ValueAxis(dataType, transformed: false));
        Assert.Equal(
            (AnalysisAxisQuantity.Unspecified, AnalysisAxisUnit.Dimensionless),
            NonSequentialDetectorDisplay.ValueAxis(dataType, transformed: true));
    }

    [Fact]
    public void StarMeshCodecRejectsInvalidGeometryFlagsAndOverflowingCounts()
    {
        var vertices = new[]
        {
            new Vector3D(0, 0, 0),
            new Vector3D(1, 0, 0),
            new Vector3D(0, 1, 0)
        };
        Assert.Throws<ArgumentException>(() => NonSequentialMeshCodec.Encode(
            vertices,
            new[] { new NonSequentialMeshTriangle(0, 0, 2, 1) }));

        var encoded = NonSequentialMeshCodec.Encode(
            vertices,
            new[] { new NonSequentialMeshTriangle(0, 1, 2, 1) });
        encoded[10] = 1;
        Assert.Throws<InvalidDataException>(() => NonSequentialMeshCodec.Decode(encoded));

        var oversizedHeader = new byte[20];
        "STARMESH"u8.CopyTo(oversizedHeader);
        BinaryPrimitives.WriteUInt16LittleEndian(oversizedHeader.AsSpan(8, 2), 1);
        BinaryPrimitives.WriteInt32LittleEndian(oversizedHeader.AsSpan(12, 4), int.MaxValue);
        BinaryPrimitives.WriteInt32LittleEndian(oversizedHeader.AsSpan(16, 4), 1);
        Assert.Throws<InvalidDataException>(() => NonSequentialMeshCodec.Decode(oversizedHeader));
    }

    [Fact]
    public async Task DatabaseInspectionDoesNotChangeSelectionAndSamePathReplacementRefreshesMetadata()
    {
        var firstPath = Path.Combine(Path.GetTempPath(), $"inspect-a-{Guid.NewGuid():N}.starrdb");
        var secondPath = Path.Combine(Path.GetTempPath(), $"inspect-b-{Guid.NewGuid():N}.starrdb");
        try
        {
            using var application = WorkbenchApplication.Create("blank");
            application.NonSequential.AddObject(OptilandWorkbench.Application.Contracts.NonSequentialObjectKind.SourcePoint);
            var first = await application.NonSequentialAnalysis.TraceAsync(new NonSequentialTraceRunRequestDto(
                RayDatabasePath: firstPath,
                RayCountOverride: 1));
            var second = await application.NonSequentialAnalysis.TraceAsync(new NonSequentialTraceRunRequestDto(
                RayDatabasePath: secondPath,
                RayCountOverride: 3));

            application.NonSequentialAnalysis.SelectRayDatabase(firstPath);
            _ = application.NonSequentialAnalysis.InspectRayDatabase(secondPath);
            Assert.Equal(Path.GetFullPath(firstPath), application.NonSequentialAnalysis.GetCurrentSession()!.RayDatabasePath);

            File.Copy(secondPath, firstPath, overwrite: true);
            application.NonSequentialAnalysis.SelectRayDatabase(firstPath);
            Assert.Equal(second.TotalBranchCount, application.NonSequentialAnalysis.GetCurrentSession()!.BranchCount);
            Assert.NotEqual(first.TotalBranchCount, application.NonSequentialAnalysis.GetCurrentSession()!.BranchCount);
        }
        finally
        {
            if (File.Exists(firstPath)) File.Delete(firstPath);
            if (File.Exists(secondPath)) File.Delete(secondPath);
        }
    }

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
    public void StlDetectsIntersectionAwayFromASharedVertex()
    {
        const string sharedVertexIntersection = """
            solid shared
              facet normal 0 0 1
                outer loop
                  vertex 0 0 0
                  vertex 2 0 0
                  vertex 0 2 0
                endloop
              endfacet
              facet normal 1 -1 0
                outer loop
                  vertex 0 0 0
                  vertex 0.5 0.5 -1
                  vertex 0.5 0.5 1
                endloop
              endfacet
            endsolid shared
            """;

        var asset = NonSequentialStlImporter.Import(
            Encoding.UTF8.GetBytes(sharedVertexIntersection),
            "shared-vertex-intersection.stl");

        Assert.True(asset.HasSelfIntersections);
    }

    [Fact]
    public void StlPathImportRejectsOversizedInputBeforeReadingIt()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oversized-{Guid.NewGuid():N}.stl");
        try
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            {
                stream.SetLength(NonSequentialStlImporter.MaximumInputBytes + 1);
            }

            var exception = Assert.Throws<InvalidDataException>(() =>
                NonSequentialStlImporter.Import(path));

            Assert.Contains("256 MiB", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
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
    public void PathFilterRejectsExcessiveLengthDepthAndNodeCount()
    {
        Assert.Throws<NonSequentialPathFilterException>(() =>
            NonSequentialPathFilter.Parse(new string(' ', NonSequentialPathFilter.MaximumExpressionLength) + "Q1"));
        Assert.Throws<NonSequentialPathFilterException>(() =>
            NonSequentialPathFilter.Parse(new string('!', NonSequentialPathFilter.MaximumNestingDepth + 1) + "Q1"));
        Assert.Throws<NonSequentialPathFilterException>(() =>
            NonSequentialPathFilter.Parse(string.Join('|', Enumerable.Repeat("Q1", 140))));
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
    public void RayDatabaseRejectsCorruptedChunkChecksum()
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
        var chunkHeader = 52 + headerCompressedLength;
        bytes[chunkHeader + 16] ^= 0x40;
        using var corrupted = new MemoryStream(bytes);
        using var reader = new NonSequentialRayDatabaseReader(corrupted);
        Assert.Throws<InvalidDataException>(() => reader.ReadAllBranches());
    }

    [Fact]
    public void RayDatabaseWriterRejectsBranchesAboveTheReadableSegmentLimit()
    {
        var document = StarOptProjectStore.CreateDefaultNonSequentialDocument(Optic.CreateBlank());
        using var stream = new MemoryStream();
        using var writer = new NonSequentialRayDatabaseWriter(
            stream,
            NonSequentialRayDatabaseHeader.Create(document),
            leaveOpen: true);
        var branch = new NonSequentialRayBranch(
            1,
            null,
            0,
            null,
            new NonSequentialRaySegment[NonSequentialDocument.MaximumSegmentsPerRay + 1],
            NonSequentialTerminationReason.MaximumSegments,
            1);

        Assert.Throws<InvalidDataException>(() => writer.OnBranch(branch));
    }

    [Fact]
    public void RayDatabaseRejectsOverlappingChunkIndexEntries()
    {
        var document = StarOptProjectStore.CreateDefaultNonSequentialDocument(Optic.CreateBlank());
        using var stream = new MemoryStream();
        using (var writer = new NonSequentialRayDatabaseWriter(
            stream,
            NonSequentialRayDatabaseHeader.Create(document),
            leaveOpen: true))
        {
            for (var index = 0; index < 513; index++)
            {
                writer.OnBranch(new NonSequentialRayBranch(
                    index + 1,
                    null,
                    0,
                    null,
                    Array.Empty<NonSequentialRaySegment>(),
                    NonSequentialTerminationReason.Escaped,
                    1));
            }
            writer.Complete();
        }

        var bytes = stream.ToArray();
        var indexOffset = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(bytes.Length - 16, 8));
        var firstChunkOffset = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(checked((int)indexOffset + 8), 8));
        BinaryPrimitives.WriteInt64LittleEndian(
            bytes.AsSpan(checked((int)indexOffset + 8 + 24), 8),
            firstChunkOffset);

        using var corrupted = new MemoryStream(bytes);
        Assert.Throws<InvalidDataException>(() => new NonSequentialRayDatabaseReader(corrupted));
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
        var service = Assert.IsType<NonSequentialAnalysisService>(application.NonSequentialAnalysis);
        var detectorCache = typeof(NonSequentialAnalysisService).GetField(
            "_detectorFrameCache",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        Assert.NotNull(detectorCache.GetValue(service));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            application.NonSequentialAnalysis.TraceAsync(new NonSequentialTraceRunRequestDto(
                Command: NonSequentialTraceCommand.TraceOnly,
                MaximumSegmentsPerRay: 25)));

        var managedPath = accumulated.RayDatabasePath;
        await application.NonSequentialAnalysis.ClearDetectorsAsync();
        Assert.Null(application.NonSequentialAnalysis.GetCurrentSession());
        Assert.False(File.Exists(managedPath));
        Assert.Null(detectorCache.GetValue(service));
    }

    [Fact]
    public async Task FilteredTraceSessionCountsStoredBranchesAndFilteredPagingUsesStableIndices()
    {
        using var application = WorkbenchApplication.Create("blank");
        application.NonSequential.AddObject(OptilandWorkbench.Application.Contracts.NonSequentialObjectKind.SourceRay);
        var splitterId = application.NonSequential.AddObject(
            OptilandWorkbench.Application.Contracts.NonSequentialObjectKind.PlaneRectangle);
        var detectorId = application.NonSequential.AddObject(
            OptilandWorkbench.Application.Contracts.NonSequentialObjectKind.DetectorRectangle);
        var splitter = application.NonSequential.GetDocument().Objects.Single(item => item.Id == splitterId);
        var detector = application.NonSequential.GetDocument().Objects.Single(item => item.Id == detectorId);
        application.NonSequential.UpdateObject(new NonSequentialObjectUpdateDto(
            splitter.Id, true, true, splitter.Kind, splitter.Name, null, null,
            0, 0, 5, 0, 0, 0,
            new OptilandWorkbench.Application.Contracts.PlaneRectangleParameters(
                20, 20,
                OptilandWorkbench.Application.Contracts.NonSequentialSurfaceBehavior.Refractive,
                "Air", "N-BK7")));
        application.NonSequential.UpdateObject(new NonSequentialObjectUpdateDto(
            detector.Id, true, true, detector.Kind, detector.Name, null, null,
            0, 0, 10, 0, 0, 0, detector.Parameters));

        var result = await application.NonSequentialAnalysis.TraceAsync(
            new NonSequentialTraceRunRequestDto(PathFilterExpression: "D3"));
        var session = Assert.IsType<NonSequentialTraceSessionDto>(
            application.NonSequentialAnalysis.GetCurrentSession());
        var database = application.NonSequentialAnalysis.OpenRayDatabase(session.RayDatabasePath, "D3");
        var page = application.NonSequentialAnalysis.GetRayDatabasePage(
            session.RayDatabasePath, 0, 100, "D3");

        Assert.True(result.TotalBranchCount > result.MatchedBranchCount);
        Assert.Equal(result.MatchedBranchCount, session.BranchCount);
        Assert.Equal(session.BranchCount, database.BranchCount);
        Assert.Equal(session.BranchCount, page.Branches.Count);
        Assert.All(page.Branches, branch => Assert.Equal(nameof(NonSequentialTerminationReason.DetectorHit), branch.TerminationReason));
    }

    [Fact]
    public async Task AnalysisTraceCommandsAreSerializedAndClearWinsAfterRunningTrace()
    {
        using var application = WorkbenchApplication.Create("blank");
        var sourceId = application.NonSequential.AddObject(
            OptilandWorkbench.Application.Contracts.NonSequentialObjectKind.SourcePoint);
        var source = application.NonSequential.GetDocument().Objects.Single(item => item.Id == sourceId);
        application.NonSequential.UpdateObject(new NonSequentialObjectUpdateDto(
            source.Id, true, true, source.Kind, source.Name, null, null,
            0, 0, 0, 0, 0, 0,
            new OptilandWorkbench.Application.Contracts.SourcePointParameters(
                1, 1, 1, 20, 25_000)));

        var trace = application.NonSequentialAnalysis.TraceAsync(new NonSequentialTraceRunRequestDto());
        var clear = application.NonSequentialAnalysis.TraceAsync(new NonSequentialTraceRunRequestDto(
            Command: NonSequentialTraceCommand.ClearOnly));
        await Task.WhenAll(trace, clear);
        var clearResult = await clear;

        Assert.Null(application.NonSequentialAnalysis.GetCurrentSession());
        Assert.Equal(NonSequentialTraceSessionState.Empty, clearResult.SessionState);
    }

    [Fact]
    public async Task DetectorViewRejectsDimensionallyInvalidSpaceAndQuantityPairs()
    {
        using var application = WorkbenchApplication.Create("blank");
        application.NonSequential.AddObject(
            OptilandWorkbench.Application.Contracts.NonSequentialObjectKind.SourceRay);
        var detectorId = application.NonSequential.AddObject(
            OptilandWorkbench.Application.Contracts.NonSequentialObjectKind.DetectorRectangle);
        var detector = application.NonSequential.GetDocument().Objects.Single(item => item.Id == detectorId);
        application.NonSequential.UpdateObject(new NonSequentialObjectUpdateDto(
            detector.Id, true, true, detector.Kind, detector.Name, null, null,
            0, 0, 10, 0, 0, 0, detector.Parameters));
        await application.NonSequentialAnalysis.TraceAsync(new NonSequentialTraceRunRequestDto());

        Assert.Throws<ArgumentException>(() => application.NonSequentialAnalysis.GetDetectorView(
            new NonSequentialDetectorViewRequestDto(
                detectorId,
                NonSequentialDetectorSpace.Position,
                NonSequentialDetectorDataType.RadiantIntensity)));
        Assert.Throws<ArgumentException>(() => application.NonSequentialAnalysis.GetDetectorView(
            new NonSequentialDetectorViewRequestDto(
                detectorId,
                NonSequentialDetectorSpace.Angle,
                NonSequentialDetectorDataType.IncoherentIrradiance)));
    }

    [Fact]
    public async Task DetectorViewerSourceSelectionFiltersDatabasePower()
    {
        using var application = WorkbenchApplication.Create("blank");
        var firstSourceId = application.NonSequential.AddObject(
            OptilandWorkbench.Application.Contracts.NonSequentialObjectKind.SourceRay);
        var secondSourceId = application.NonSequential.AddObject(
            OptilandWorkbench.Application.Contracts.NonSequentialObjectKind.SourceRay);
        var detectorId = application.NonSequential.AddObject(
            OptilandWorkbench.Application.Contracts.NonSequentialObjectKind.DetectorRectangle);
        foreach (var (sourceId, power) in new[] { (firstSourceId, 1.0), (secondSourceId, 3.0) })
        {
            var source = application.NonSequential.GetDocument().Objects.Single(item => item.Id == sourceId);
            application.NonSequential.UpdateObject(new NonSequentialObjectUpdateDto(
                source.Id, true, true, source.Kind, source.Name, null, null,
                0, 0, 0, 0, 0, 0,
                new OptilandWorkbench.Application.Contracts.SourceRayParameters(
                    power, 1,
                    new NonSequentialVector3(0, 0, 0),
                    new NonSequentialVector3(0, 0, 1))));
        }
        var detector = application.NonSequential.GetDocument().Objects.Single(item => item.Id == detectorId);
        application.NonSequential.UpdateObject(new NonSequentialObjectUpdateDto(
            detector.Id, true, true, detector.Kind, detector.Name, null, null,
            0, 0, 10, 0, 0, 0, detector.Parameters));
        await application.NonSequentialAnalysis.TraceAsync(new NonSequentialTraceRunRequestDto());
        application.Modes.SwitchTo(OpticalWorkbenchMode.NonSequential);

        var parameters = application.Analyses.GetParameters("Non-Sequential Detector Viewer")
            .ToDictionary(item => item.Key, item => item.DefaultValue);
        parameters["SourceNumber"] = "2";
        var result = await application.Analyses.RunAsync(new AnalysisRequestDto(
            Guid.NewGuid(), 1, "Non-Sequential Detector Viewer", parameters));

        Assert.Contains(result.View.Rows, row => row.Metric == "TotalPowerWatts" && row.Value == "3");
        Assert.Contains(result.View.Rows, row => row.Metric == "SelectedSource");
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
            await application.NonSequentialAnalysis.ClearDetectorsAsync();
            Assert.True(File.Exists(externalPath));
        }
        finally
        {
            if (File.Exists(externalPath)) File.Delete(externalPath);
        }
    }

    [Fact]
    public async Task CaseDistinctExternalDatabaseNeverInheritsTemporaryOwnership()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"case-path-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var ownedPath = Path.Combine(directory, "Owned.starrdb");
        var externalPath = Path.Combine(directory, "owned.starrdb");
        try
        {
            using var application = WorkbenchApplication.Create("blank");
            application.NonSequential.AddObject(
                OptilandWorkbench.Application.Contracts.NonSequentialObjectKind.SourceRay);
            var trace = await application.NonSequentialAnalysis.TraceAsync(new NonSequentialTraceRunRequestDto());
            var sourcePath = Assert.IsType<string>(trace.RayDatabasePath);
            File.Copy(sourcePath, ownedPath);
            try
            {
                File.Copy(sourcePath, externalPath);
            }
            catch (IOException)
            {
                return;
            }

            using var session = new NonSequentialAnalysisSession(new StubWorkspaceEvents());
            session.Publish(
                application.NonSequentialAnalysis.GetCurrentSession()! with { RayDatabasePath = ownedPath },
                ownsDatabase: true);
            session.Set(externalPath, null);
            session.Clear();

            Assert.True(File.Exists(externalPath));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class StubWorkspaceEvents : IWorkspaceEventStream
    {
        public event EventHandler<WorkspaceChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public event EventHandler? StatusChanged
        {
            add { }
            remove { }
        }

        public long Revision => 0;
    }

    private static NonSequentialDetectorViewDto DetectorView(
        int pixelsX,
        int pixelsY,
        IReadOnlyList<double> values) => new(
            Guid.NewGuid(),
            "Detector",
            pixelsX,
            pixelsY,
            -3,
            3,
            -2,
            2,
            "mm",
            "mm",
            "W",
            values,
            Array.Empty<double>(),
            Array.Empty<double>(),
            new NonSequentialDetectorStatisticsDto(0, 0, 0, 0, 0, 0, 0, 0),
            false,
            "test");

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
