using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Scattering;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class CoreArchitectureTests
{
    [Fact]
    public void DemoOpticContainsExpectedArchitecturePieces()
    {
        var optic = Optic.CreateDemo();

        Assert.NotEmpty(optic.Fields);
        Assert.NotEmpty(optic.Wavelengths);
        Assert.NotEmpty(optic.SurfaceGroup.Items);
        Assert.NotNull(optic.RealRayTracer);
        Assert.NotNull(optic.Paraxial);
        Assert.NotNull(optic.Pickups);
        Assert.NotNull(optic.Solves);
    }

    [Fact]
    public void RayTracerReturnsFiniteSegments()
    {
        var optic = Optic.CreateDemo();
        var trace = optic.RealRayTracer.TraceMeridionalRays();

        Assert.NotEmpty(trace.Paths);
        Assert.All(trace.Paths, path =>
        {
            Assert.NotEmpty(path.Segments);
            Assert.All(path.Segments, segment =>
            {
                Assert.True(double.IsFinite(segment.Start.Z));
                Assert.True(double.IsFinite(segment.Start.Y));
                Assert.True(double.IsFinite(segment.End.Z));
                Assert.True(double.IsFinite(segment.End.Y));
            });
        });
    }

    [Fact]
    public async Task JsonStoreRoundTripsOptic()
    {
        var optic = Optic.CreateDemo();
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.optic.json");

        try
        {
            await OpticJsonStore.SaveAsync(optic, path);
            var loaded = await OpticJsonStore.LoadAsync(path);

            Assert.Equal(optic.Name, loaded.Name);
            Assert.Equal(optic.Fields.Count, loaded.Fields.Count);
            Assert.Equal(optic.Wavelengths.Count, loaded.Wavelengths.Count);
            Assert.Equal(optic.SurfaceGroup.Items.Count, loaded.SurfaceGroup.Items.Count);
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
    public async Task StarOptProjectIsAValidatedContainerAndRoundTripsConfigurations()
    {
        var baseOptic = Optic.CreateDemo();
        var alternate = Optic.FromSnapshot(baseOptic.ToSnapshot());
        alternate.Name = "Alternate configuration";
        alternate.SurfaceGroup.Items[2].Thickness = 17.25;
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.staropt");

        try
        {
            await StarOptProjectStore.SaveAsync(
                new StarOptProjectDocument(new[] { baseOptic, alternate }, 1),
                path);

            var bytes = await File.ReadAllBytesAsync(path);
            Assert.True(StarOptProjectStore.HasMagic(bytes));
            Assert.True(await StarOptProjectStore.HasMagicAsync(path));
            Assert.NotEqual((byte)'{', bytes[0]);

            var loaded = await StarOptProjectStore.LoadAsync(path);
            Assert.Equal(2, loaded.Configurations.Count);
            Assert.Equal(1, loaded.ActiveConfigurationIndex);
            Assert.Equal("Alternate configuration", loaded.Configurations[1].Name);
            Assert.Equal(17.25, loaded.Configurations[1].SurfaceGroup.Items[2].Thickness, precision: 12);
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
    public async Task StarOptProjectRejectsTamperedPayload()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.staropt");

        try
        {
            await StarOptProjectStore.SaveAsync(
                new StarOptProjectDocument(new[] { Optic.CreateDemo() }, 0),
                path);
            var bytes = await File.ReadAllBytesAsync(path);
            bytes[^1] ^= 0x5a;
            await File.WriteAllBytesAsync(path, bytes);

            await Assert.ThrowsAsync<InvalidDataException>(() => StarOptProjectStore.LoadAsync(path));
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
    public void UndoRedoRestoresSurfaceState()
    {
        var optic = Optic.CreateDemo();
        var undoRedo = new UndoRedoManager();
        var originalRadius = optic.SurfaceGroup.Items[2].Radius;

        undoRedo.Capture(optic);
        optic.SurfaceGroup.Items[2].Radius = originalRadius + 10;

        Assert.True(undoRedo.TryUndo(optic));
        Assert.Equal(originalRadius, optic.SurfaceGroup.Items[2].Radius);

        Assert.True(undoRedo.TryRedo(optic));
        Assert.Equal(originalRadius + 10, optic.SurfaceGroup.Items[2].Radius);
    }

    [Fact]
    public void UndoFailureLeavesHistoryStacksUnchanged()
    {
        var optic = Optic.CreateDemo();
        var originalWavelength = optic.Wavelengths[0].Nanometers;
        var undoRedo = new UndoRedoManager();

        optic.Wavelengths[0].Nanometers = double.NaN;
        undoRedo.Capture(optic);
        optic.Wavelengths[0].Nanometers = originalWavelength;

        Assert.Throws<InvalidDataException>(() => undoRedo.TryUndo(optic));
        Assert.True(undoRedo.CanUndo);
        Assert.False(undoRedo.CanRedo);
        Assert.Equal(originalWavelength, optic.Wavelengths[0].Nanometers, precision: 12);
    }

    [Fact]
    public void SnapshotAndUndoPreservePickupAndSolveSettings()
    {
        var optic = Optic.CreateDemo();
        optic.Pickups.LinkRadius(1, 2, -0.5, 3);
        optic.Solves.DesiredBackFocus = 42;
        optic.Solves.KeepImageAtBackFocus = false;
        var restored = Optic.FromSnapshot(optic.ToSnapshot());

        Assert.Equal(optic.Pickups.RadiusPickups, restored.Pickups.RadiusPickups);
        Assert.Equal(42, restored.Solves.DesiredBackFocus, precision: 12);
        Assert.False(restored.Solves.KeepImageAtBackFocus);

        var undoRedo = new UndoRedoManager();
        undoRedo.Capture(optic);
        optic.Pickups.Clear();
        optic.Solves.DesiredBackFocus = 12;
        Assert.True(undoRedo.TryUndo(optic));
        Assert.Single(optic.Pickups.RadiusPickups);
        Assert.Equal(42, optic.Solves.DesiredBackFocus, precision: 12);
    }

    [Fact]
    public void SnapshotPreservesEnvironmentSettings()
    {
        var optic = Optic.CreateDemo();
        optic.Environment.MatchRefractiveIndexData = false;
        optic.Environment.TemperatureCelsius = 27.5;
        optic.Environment.PressureAtmospheres = 0.92;

        var restored = Optic.FromSnapshot(optic.ToSnapshot());

        Assert.False(restored.Environment.MatchRefractiveIndexData);
        Assert.Equal(27.5, restored.Environment.TemperatureCelsius, precision: 12);
        Assert.Equal(0.92, restored.Environment.PressureAtmospheres, precision: 12);
    }

    [Fact]
    public void SnapshotPreservesMeasuredBsdfSamples()
    {
        var optic = Optic.CreateDemo();
        var samples = new[]
        {
            (AngleDegrees: 0.0, Value: 0.01),
            (AngleDegrees: 12.5, Value: 0.08),
            (AngleDegrees: 40.0, Value: 0.22)
        };
        optic.SurfaceGroup.Items[2].ScatteringModel = new MeasuredBsdfScatteringModel(samples);

        var restored = Optic.FromSnapshot(optic.ToSnapshot());

        var measured = Assert.IsType<MeasuredBsdfScatteringModel>(
            restored.SurfaceGroup.Items[2].ScatteringModel);
        Assert.Equal(samples, measured.Samples);
    }

    [Fact]
    public void UndoHistoryEvictsOldestSnapshotsAtCapacity()
    {
        var optic = Optic.CreateDemo();
        var surface = optic.SurfaceGroup.Items[2];
        var originalRadius = surface.Radius;
        var undoRedo = new UndoRedoManager(capacity: 2);

        surface.Radius = originalRadius + 1;
        undoRedo.Capture(optic);
        surface.Radius = originalRadius + 2;
        undoRedo.Capture(optic);
        surface.Radius = originalRadius + 3;
        undoRedo.Capture(optic);
        surface.Radius = originalRadius + 4;

        Assert.True(undoRedo.TryUndo(optic));
        Assert.Equal(
            originalRadius + 3,
            optic.SurfaceGroup.Items[2].Radius,
            precision: 12);
        Assert.True(undoRedo.TryUndo(optic));
        Assert.Equal(
            originalRadius + 2,
            optic.SurfaceGroup.Items[2].Radius,
            precision: 12);
        Assert.False(undoRedo.TryUndo(optic));
    }

    [Fact]
    public void SimpleOptimizerDoesNotWorsenSpotMetric()
    {
        var optic = Optic.CreateDemo();
        var surface = optic.SurfaceGroup.Items[2];
        var before = SpotMetricEvaluator.Evaluate(optic).RmsSpotRadius;

        var result = new SimpleOptimizer(optic).OptimizeRadius(surface, iterations: 8);

        Assert.True(result.FinalMetric <= before + 1e-9);
    }
}
