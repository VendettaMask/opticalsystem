using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Optimization;
using OptilandWorkbench.Core.Multiconfig;
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
    public void ParaxialTraceUsesFirstSurfaceRoleInsteadOfPresentationLabel()
    {
        var optic = Optic.CreateCookeTriplet();
        var wavelength = optic.Wavelengths.First(item => item.IsPrimary).Micrometers;
        var baseline = optic.Paraxial.MarginalRay(wavelength);

        optic.SurfaceGroup.Items[0].Label = "Source plane";
        optic.SurfaceGroup.Items[1].Label = "Object";
        optic.SurfaceGroup.Items[^1].Label = "Sensor plane";
        var relabeled = optic.Paraxial.MarginalRay(wavelength);

        Assert.Equal(
            baseline.Heights.SelectMany(values => values),
            relabeled.Heights.SelectMany(values => values));
        Assert.Equal(
            baseline.Slopes.SelectMany(values => values),
            relabeled.Slopes.SelectMany(values => values));
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
    public async Task StarOptProjectPreservesMultiConfigurationBrokenLinks()
    {
        var multiConfiguration = new MultiConfiguration(Optic.CreateDemo());
        var alternateIndex = multiConfiguration.AddConfiguration();
        multiConfiguration.SetThickness(alternateIndex, 2, 17.25);
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.staropt");

        try
        {
            await StarOptProjectStore.SaveAsync(
                new StarOptProjectDocument(
                    multiConfiguration.Configurations,
                    0,
                    multiConfiguration.BrokenLinks),
                path);

            var loaded = await StarOptProjectStore.LoadAsync(path);
            var restored = new MultiConfiguration(loaded.Configurations, loaded.BrokenLinks);
            restored.SetThickness(0, 2, 6.5);
            restored.PropagateBaseLinks();

            Assert.Equal(17.25, restored.Configurations[alternateIndex].SurfaceGroup.Items[2].Thickness, 12);
            Assert.Contains(
                restored.BrokenLinks,
                link => link.ConfigurationIndex == alternateIndex
                    && link.SurfaceNumber == 2
                    && link.Property == "thickness");
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
    public void RuntimeNumericParametersRejectNonFiniteValues()
    {
        var optic = Optic.CreateDemo();
        var surface = optic.SurfaceGroup.Items[0];

        Assert.Throws<ArgumentOutOfRangeException>(() => optic.Wavelengths[0].Nanometers = double.NaN);
        Assert.Throws<ArgumentOutOfRangeException>(() => optic.Wavelengths[0].Weight = double.PositiveInfinity);
        Assert.Throws<ArgumentOutOfRangeException>(() => optic.Fields[0].X = double.NaN);
        Assert.Throws<ArgumentOutOfRangeException>(() => optic.Fields[0].Weight = double.NegativeInfinity);
        Assert.Throws<ArgumentOutOfRangeException>(() => optic.Aperture.Value = double.PositiveInfinity);
        Assert.Throws<ArgumentOutOfRangeException>(() => surface.Radius = double.NaN);
        Assert.Throws<ArgumentOutOfRangeException>(() => surface.Thickness = double.NaN);
        Assert.Throws<ArgumentOutOfRangeException>(() => surface.Thickness = double.NegativeInfinity);
        Assert.Throws<ArgumentOutOfRangeException>(() => surface.SemiDiameter = double.NaN);
        Assert.Throws<ArgumentOutOfRangeException>(() => surface.Conic = double.PositiveInfinity);

        surface.Thickness = double.PositiveInfinity;
        Assert.True(double.IsPositiveInfinity(surface.Thickness));
    }

    [Fact]
    public void UndoFailureLeavesHistoryStacksUnchanged()
    {
        var optic = Optic.CreateDemo();
        var originalThickness = optic.SurfaceGroup.Items[1].Thickness;
        var undoRedo = new UndoRedoManager();

        optic.SurfaceGroup.Items[1].Thickness = double.PositiveInfinity;
        undoRedo.Capture(optic);
        optic.SurfaceGroup.Items[1].Thickness = originalThickness;

        Assert.Throws<InvalidDataException>(() => undoRedo.TryUndo(optic));
        Assert.True(undoRedo.CanUndo);
        Assert.False(undoRedo.CanRedo);
        Assert.Equal(originalThickness, optic.SurfaceGroup.Items[1].Thickness, precision: 12);
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
    public void BackFocusSolveUsesImageSurfaceRoleInsteadOfEnglishLabel()
    {
        var optic = Optic.CreateDemo();
        optic.SurfaceGroup.Items[^1].Label = "像面";
        optic.Solves.KeepImageAtBackFocus = true;
        optic.Solves.DesiredBackFocus = 75;

        optic.Solves.ApplyAll();

        var poweredTrack = optic.SurfaceGroup.Items.Take(optic.SurfaceGroup.Items.Count - 1)
            .Where((surface, index) => index != 0 || !ObjectConjugate.IsInfinite(surface))
            .Sum(surface => surface.Thickness);
        Assert.Equal(Math.Max(0, 75 - poweredTrack), optic.SurfaceGroup.Items[^1].Thickness, 12);
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
        optic.SurfaceGroup.Items[2].ScatteringModel = new MeanMeasuredScatterLoss(samples);

        var restored = Optic.FromSnapshot(optic.ToSnapshot());

        var measured = Assert.IsType<MeanMeasuredScatterLoss>(
            restored.SurfaceGroup.Items[2].ScatteringModel);
        Assert.Equal(samples, measured.Samples);
    }

    [Fact]
    public void ExperimentalLossApproximationsUseTruthfulKindsAndMigrateLegacySnapshots()
    {
        var coating = new ApproximateTransmissionRippleCoating(
            new[] { new ThinFilmLayer("MgF2", 120) });
        var mainRayLoss = new MainRayScatterLossApproximation(0.1);
        var measuredLoss = new MeanMeasuredScatterLoss(
            new[] { (AngleDegrees: 0.0, Value: 0.05) });

        Assert.Equal("approximate_transmission_ripple", coating.Kind);
        Assert.Contains("Experimental", coating.ExperimentalWarning, StringComparison.Ordinal);
        Assert.Equal("main_ray_scatter_loss_approximation", mainRayLoss.Kind);
        Assert.Contains("不生成", mainRayLoss.ExperimentalWarning, StringComparison.Ordinal);
        Assert.Equal("mean_measured_scatter_loss", measuredLoss.Kind);
        Assert.Contains("BSDF", measuredLoss.ExperimentalWarning, StringComparison.Ordinal);

        var legacyCoating = ComponentSnapshotFactory.ToCoating(new ComponentSnapshot(
            "thin_film_stack",
            new Dictionary<string, double> { ["count"] = 1, ["thickness_0"] = 120 },
            new Dictionary<string, string> { ["material_0"] = "MgF2" }));
        var legacyLambertian = ComponentSnapshotFactory.ToScattering(new ComponentSnapshot(
            "lambertian",
            new Dictionary<string, double> { ["scatterFraction"] = 0.2 },
            new Dictionary<string, string>()));
        var legacyMeasured = ComponentSnapshotFactory.ToScattering(new ComponentSnapshot(
            "measured_bsdf",
            new Dictionary<string, double>
            {
                ["sampleCount"] = 1,
                ["angle0"] = 12,
                ["value0"] = 0.3
            },
            new Dictionary<string, string>()));

        Assert.IsType<ApproximateTransmissionRippleCoating>(legacyCoating);
        Assert.IsType<MainRayScatterLossApproximation>(legacyLambertian);
        Assert.IsType<MeanMeasuredScatterLoss>(legacyMeasured);
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

}
