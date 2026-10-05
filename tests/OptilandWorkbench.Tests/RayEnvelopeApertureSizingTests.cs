using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Services;

namespace OptilandWorkbench.Tests;

public sealed class RayEnvelopeApertureSizingTests
{
    private static readonly IReadOnlyList<IReadOnlyList<int>> Groups = [new[] { 1, 2 }];
    private static readonly PupilSample[] Pupils = [new(0, 0, 1), new(0, 1, 1), new(0, -1, 1), new(1, 0, 1), new(-1, 0, 1)];

    [Fact]
    public void MechanicalEnvelopeIncludesExistingChipZonesWithoutEnlargingTraceApertures()
    {
        var optic = Plate(); optic.SurfaceGroup.Items[1].ChipZone = 1;
        optic.SurfaceGroup.Items[2].ChipZone = 2;
        var result = Size(optic, 0); Assert.True(result.Applied);
        foreach (var surface in optic.SurfaceGroup.Items.Skip(1).Take(2))
        {
            Assert.Equal(2.2, surface.SemiDiameter, 10);
            Assert.Equal(4.2, surface.MechanicalSemiDiameter, 10);
            Assert.Equal(2.2, Assert.IsType<CircularAperture>(surface.PhysicalAperture).Radius, 10);
        }
        Assert.Equal(1, optic.SurfaceGroup.Items[1].ChipZone); Assert.Equal(2, optic.SurfaceGroup.Items[2].ChipZone);
    }

    [Fact]
    public void EnvelopeGrowsAndShrinksBothFacesFromTheCurrentBeam()
    {
        var optic = Plate();
        var narrow = Size(optic, 0);
        Assert.True(narrow.Applied);
        Assert.Equal(5, narrow.AttemptedRays);
        Assert.Equal(2.2, optic.SurfaceGroup.Items[1].SemiDiameter, 10);
        var wide = Size(optic, 1);
        Assert.True(wide.Applied);
        Assert.True(optic.SurfaceGroup.Items[1].SemiDiameter > 2.2);
        Assert.Equal(optic.SurfaceGroup.Items[1].SemiDiameter, optic.SurfaceGroup.Items[2].SemiDiameter);
        var contracted = Size(optic, 0);
        Assert.True(contracted.Applied);
        foreach (var surface in optic.SurfaceGroup.Items.Skip(1).Take(2))
        {
            Assert.Equal(2.2, surface.SemiDiameter, 10);
            Assert.Equal(surface.SemiDiameter, surface.MechanicalSemiDiameter);
            Assert.Equal(surface.SemiDiameter, Assert.IsType<CircularAperture>(surface.PhysicalAperture).Radius);
            Assert.True(surface.SemiDiameterFixed);
        }
    }

    [Fact]
    public void UnselectedPhysicalObstructionPreventsSizingAndLeavesSourceUnchanged()
    {
        var optic = Plate();
        var aperture = new CircularAperture(.2);
        optic.SurfaceGroup.Items[^1].PhysicalAperture = aperture;
        var before = Snapshot(optic);
        var result = Size(optic, 0);
        Assert.False(result.Applied);
        Assert.True(result.CompletedRays < result.AttemptedRays);
        Assert.Equal(before, Snapshot(optic));
        Assert.Same(aperture, optic.SurfaceGroup.Items[^1].PhysicalAperture);
    }

    [Fact]
    public void MissingSphericalIntersectionsCannotProduceAnAppliedEnvelope()
    {
        var optic = Plate();
        optic.SurfaceGroup.Items[1].Radius = .3;
        var before = Snapshot(optic);
        var result = Size(optic, 0);
        Assert.False(result.Applied);
        Assert.True(result.CompletedRays < result.AttemptedRays);
        Assert.Equal(before, Snapshot(optic));
    }

    [Fact]
    public void MarginChangesOnlyDimensionsAndIncludesAllRequestedFieldsAndWavelengths()
    {
        var optic = Plate();
        optic.Wavelengths.Add(new Wavelength { Nanometers = 486.1, Weight = 1, IsPrimary = false });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 656.3, Weight = 0, IsPrimary = false });
        var result = AutomaticSemiDiameterSolver.UpdateFromRayEnvelope(optic, Groups, [(0, 0), (0, 1)], Pupils, 1.25);
        Assert.True(result.Applied);
        Assert.Equal(20, result.AttemptedRays);
        Assert.Equal(20, result.CompletedRays);
        Assert.All(result.Surfaces, surface => Assert.Equal(result.Surfaces.Max(s => s.SampledRadiusMillimeters) * 1.25, surface.SemiDiameterMillimeters, 12));
        Assert.Equal(4, optic.Aperture.Value);
        Assert.Equal(10, optic.Fields[^1].YAngleDegrees);
        Assert.Equal(5, optic.SurfaceGroup.Items[1].Thickness);
        foreach (var field in new[] { 0.0, 1.0 })
        {
            var spot = OptilandWorkbench.Core.Analysis.SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, field, Pupils);
            Assert.Equal(0, spot.VignettedRayCount);
        }
    }

    [Fact]
    public void CancellationAndNoncircularConstraintsDoNotMutateTheSource()
    {
        var optic = Plate();
        var before = Snapshot(optic);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => AutomaticSemiDiameterSolver.UpdateFromRayEnvelope(
            optic, Groups, [(0, 0)], Pupils, cancellationToken: cancellation.Token));
        Assert.Equal(before, Snapshot(optic));
        optic.SurfaceGroup.Items[1].PhysicalAperture = new RectangularAperture(2, 3);
        before = Snapshot(optic);
        Assert.Throws<ArgumentException>(() => Size(optic, 0));
        Assert.Equal(before, Snapshot(optic));
    }

    private static RayEnvelopeSizingResult Size(Optic optic, double field) =>
        AutomaticSemiDiameterSolver.UpdateFromRayEnvelope(optic, Groups, [(0, field)], Pupils, 1.1);

    private static string Snapshot(Optic optic) => System.Text.Json.JsonSerializer.Serialize(optic.ToSnapshot(),
        new System.Text.Json.JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });

    private static Optic Plate()
    {
        var optic = new Optic("Envelope reference plate");
        optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter;
        optic.Aperture.Value = 4;
        optic.Fields.Add(new FieldPoint { YAngleDegrees = 0 });
        optic.Fields.Add(new FieldPoint { YAngleDegrees = 10 });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 587.6, Weight = 1, IsPrimary = true });
        optic.SurfaceGroup.ImportLegacySurfaces([
            new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { Thickness = 5, Material = "N-BK7", IsStop = true, SemiDiameter = .5, SemiDiameterFixed = true, PhysicalAperture = new CircularAperture(.5) },
            new OpticalSurface { Thickness = 10, Material = "Air", SemiDiameter = .5, SemiDiameterFixed = true, PhysicalAperture = new CircularAperture(.5) },
            new OpticalSurface { Thickness = 0 }
        ]);
        return optic;
    }
}
