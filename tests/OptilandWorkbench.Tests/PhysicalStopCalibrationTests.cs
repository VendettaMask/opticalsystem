using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Core.Backend;

namespace OptilandWorkbench.Tests;

public sealed class PhysicalStopCalibrationTests
{
    [Theory]
    [InlineData(ApertureKind.EntrancePupilDiameter)]
    [InlineData(ApertureKind.FNumber)]
    [InlineData(ApertureKind.FloatByStopSize)]
    public void CalibratedStopRecoversTheDeclaredPupilWhenFloated(ApertureKind kind)
    {
        var optic = Optic.CreateCookeTriplet();
        optic.Aperture.Kind = kind;
        optic.Aperture.Value = kind == ApertureKind.FNumber ? 5 : 10;
        var expected = optic.Paraxial.EstimateEntrancePupilDiameter();
        var stop = optic.SurfaceGroup.Items.Single(surface => surface.IsStop);
        stop.MechanicalSemiDiameter = 40;
        var originalValue = optic.Aperture.Value;
        var result = PhysicalStopCalibration.Apply(optic);
        Assert.Equal(kind, optic.Aperture.Kind);
        Assert.Equal(originalValue, optic.Aperture.Value);
        Assert.Equal(expected, result.EntrancePupilDiameterMillimeters);
        Assert.Equal(stop.SemiDiameter, Assert.IsType<CircularAperture>(stop.PhysicalAperture).Radius);
        Assert.Equal(40, stop.MechanicalSemiDiameter);
        var floated = Optic.FromSnapshot(optic.ToSnapshot());
        floated.Aperture.Kind = ApertureKind.FloatByStopSize;
        Assert.Equal(expected, floated.Paraxial.EstimateEntrancePupilDiameter(), 10);
        Assert.Equal(optic.Paraxial.EstimateFNumber(), floated.Paraxial.EstimateFNumber(), 10);
    }

    [Fact]
    public void FlatSystemNeedsNoUndefinedFocalLengthFallback()
    {
        var optic = Plate();
        var result = PhysicalStopCalibration.Apply(optic);
        Assert.Equal(5, result.ClearSemiDiameterMillimeters, 12);
        Assert.Equal(10, result.EntrancePupilDiameterMillimeters);
        Assert.Equal(0, optic.Paraxial.EstimateOpticalPower());
    }

    [Fact]
    public void EnvelopeResizesTheLensBodyWithoutChangingItsPhysicalStop()
    {
        var optic = Plate();
        optic.RayAimingEnabled = true;
        PhysicalStopCalibration.Apply(optic);
        var stop = optic.SurfaceGroup.Items[2];
        var aperture = stop.PhysicalAperture;
        PupilSample[] samples = [new(0, 0, 1), new(1, 0, 1), new(-1, 0, 1), new(0, 1, 1), new(0, -1, 1)];
        var wide = AutomaticSemiDiameterSolver.UpdateFromRayEnvelope(optic, [new[] { 1, 2 }],
            [(0, 0), (0, 1)], samples, 1.25, preserveStopAperture: true);
        Assert.True(wide.Applied);
        Assert.Equal(wide.AttemptedRays, wide.CompletedRays);
        Assert.Same(aperture, stop.PhysicalAperture);
        Assert.Equal(5, stop.SemiDiameter, 12);
        Assert.Equal(optic.SurfaceGroup.Items[1].SemiDiameter, stop.MechanicalSemiDiameter);
        Assert.True(stop.MechanicalSemiDiameter > 6.25);
        var previousMechanical = stop.MechanicalSemiDiameter;
        var narrow = AutomaticSemiDiameterSolver.UpdateFromRayEnvelope(optic, [new[] { 1, 2 }],
            [(0, 0)], samples, 1.1, preserveStopAperture: true);
        Assert.True(narrow.Applied);
        Assert.True(stop.MechanicalSemiDiameter < previousMechanical);
        Assert.Equal(5.5, stop.MechanicalSemiDiameter, 11);
        Assert.Equal(5, stop.SemiDiameter, 12);
        Assert.Same(aperture, stop.PhysicalAperture);
    }

    [Fact]
    public void UnaimedOffAxisPupilCannotSilentlyExpandThePhysicalStop()
    {
        var optic = Plate();
        PhysicalStopCalibration.Apply(optic);
        var before = Snapshot(optic);
        var result = AutomaticSemiDiameterSolver.UpdateFromRayEnvelope(optic, [new[] { 1, 2 }],
            [(0, 1)], [new(0, 1, 1), new(0, -1, 1)], 1.25, preserveStopAperture: true);
        Assert.False(result.Applied);
        Assert.True(result.CompletedRays < result.AttemptedRays);
        Assert.Equal(before, Snapshot(optic));
    }

    [Fact]
    public void PreservedStopCannotBeEnlargedToConcealClipping()
    {
        var optic = Plate();
        PhysicalStopCalibration.Apply(optic);
        optic.SurfaceGroup.Items[2].SemiDiameter = 2;
        var before = Snapshot(optic);
        var result = AutomaticSemiDiameterSolver.UpdateFromRayEnvelope(optic, [new[] { 1, 2 }],
            [(0, 0)], [new(0, .9, 1)], 1.25, preserveStopAperture: true);
        Assert.False(result.Applied);
        Assert.Equal(0, result.CompletedRays);
        Assert.Equal(before, Snapshot(optic));
    }

    [Fact]
    public void SparseEnvelopeNeverMakesTheLensBodySmallerThanItsStop()
    {
        var optic = Plate();
        PhysicalStopCalibration.Apply(optic);
        var result = AutomaticSemiDiameterSolver.UpdateFromRayEnvelope(optic, [new[] { 1, 2 }],
            [(0, 0)], [new(0, 0, 1)], preserveStopAperture: true);
        Assert.True(result.Applied);
        Assert.All(result.Surfaces, size => Assert.Equal(5, size.MechanicalSemiDiameterMillimeters));
        Assert.Equal(5, optic.SurfaceGroup.Items[1].SemiDiameter);
    }

    [Fact]
    public void InvalidCalibrationDoesNotPartiallyMutateTheOptic()
    {
        var optic = Plate();
        optic.Aperture.Value = .01;
        var before = Snapshot(optic);
        Assert.Throws<InvalidOperationException>(() => PhysicalStopCalibration.Apply(optic));
        Assert.Equal(before, Snapshot(optic));
        optic.Aperture.Value = 10;
        optic.SurfaceGroup.Items[2].PhysicalAperture = new RectangularAperture(2, 3);
        before = Snapshot(optic);
        Assert.Throws<NotSupportedException>(() => PhysicalStopCalibration.Apply(optic));
        Assert.Equal(before, Snapshot(optic));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AimingReachesAStopBehindAnInvertingPupilRelay(bool finite)
    {
        var optic = new Optic("Inverting pupil relay");
        optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter;
        optic.Aperture.Value = 2;
        optic.Fields.Add(new FieldPoint());
        optic.Fields.Add(new FieldPoint { Y = 1 });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 587.6, Weight = 1, IsPrimary = true });
        optic.SurfaceGroup.ImportLegacySurfaces([
            new OpticalSurface { Thickness = finite ? 500 : double.PositiveInfinity },
            new OpticalSurface { Radius = 20, Thickness = 2, Material = "N-BK7", SemiDiameter = 10 },
            new OpticalSurface { Radius = -20, Thickness = 30, Material = "Air", SemiDiameter = 10 },
            new OpticalSurface { IsStop = true, Thickness = 10, SemiDiameter = 10 },
            new OpticalSurface()
        ]);
        PhysicalStopCalibration.Apply(optic);
        var stop = optic.SurfaceGroup.Items[3];
        foreach (var pupil in ApertureSampler.Generate(24, PupilSampling.Ring))
        {
            var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 1,
                pupil.X, pupil.Y, .5876, aimAtStop: true);
            using var trace = optic.SequentialRayTracer.Trace(bundle, TraceRequest.Selected([3, 4]));
            Assert.True(trace.TryGetSample(0, 4, out var image));
            Assert.False(image.Vignetted);
            Assert.True(image.Intensity > 0);
            Assert.True(trace.TryGetSample(0, 3, out var sample));
            var point = stop.CoordinateSystem.ToLocalPoint(sample.Position);
            Assert.InRange(Math.Abs(double.Hypot(point.X, point.Y) - stop.SemiDiameter), 0, 1e-10);
        }
    }

    [Fact]
    public void CircularBoundaryAllowsRoundoffButRejectsResolvedClipping()
    {
        var aperture = new CircularAperture(5);
        Assert.True(aperture.Contains(new Vector3D(5 + 1e-12, 0, 0)));
        Assert.False(aperture.Contains(new Vector3D(5 + 1e-7, 0, 0)));
        Assert.False(aperture.Contains(new Vector3D(double.NaN, 0, 0)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScalarAndBatchedTracesUseTheSameCircularBoundary(bool batched)
    {
        var optic = Plate();
        PhysicalStopCalibration.Apply(optic);
        var bundle = new OptilandWorkbench.Core.Rays.RealRayBundle(new[] { 1e-12, 1e-7 }.Select(error =>
            new OptilandWorkbench.Core.Rays.RealRay(new Vector3D(5 + error, 0, -1), new Vector3D(0, 0, 1), 587.6)).ToArray());
        using var trace = optic.SequentialRayTracer.Trace(bundle, TraceRequest.Selected([2, 3]) with { UseBatchedBackend = batched });
        Assert.True(trace.TryGetSample(0, 3, out var accepted));
        Assert.False(accepted.Vignetted);
        Assert.True(accepted.Intensity > 0);
        Assert.True(trace.TryGetSample(1, 2, out var clipped));
        Assert.True(clipped.Vignetted);
    }

    private static string Snapshot(Optic optic) => System.Text.Json.JsonSerializer.Serialize(optic.ToSnapshot(),
        new System.Text.Json.JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });

    private static Optic Plate()
    {
        var optic = new Optic("Independent physical stop");
        optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter;
        optic.Aperture.Value = 10;
        optic.Fields.Add(new FieldPoint());
        optic.Fields.Add(new FieldPoint { Y = 10 });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 587.6, Weight = 1, IsPrimary = true });
        optic.SurfaceGroup.ImportLegacySurfaces([
            new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { Thickness = 5, Material = "N-BK7", SemiDiameter = 1 },
            new OpticalSurface { Thickness = 10, Material = "Air", IsStop = true, SemiDiameter = 10 },
            new OpticalSurface()
        ]);
        return optic;
    }
}
