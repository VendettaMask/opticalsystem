using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class FixedStopDelanoTests
{
    [Theory]
    [InlineData(4.0)]
    [InlineData(12.0)]
    public void ActualCoreThickMatricesMeetTheSpecifiedImageFocalLengthAndInternalStop(double stopZ)
    {
        var optic = new Optic("Fixed-stop thick Delano"); const double wavelength = 587.6;
        var index = optic.Materials.Resolve("N-BK7").RefractiveIndex(wavelength);
        ThickDelanoElement[] segments = [new(1, 2, index), new(8, 2, index), new(15, 3, index)];
        using var rays = SequentialTraceMeasurement.Begin();
        var result = FixedStopDelano.Construct(segments, 2.5, stopZ, 2, 30, 20, [.5, .8, 1, 1.2, .7]);
        var nodes = new List<(double Z, OpticalSurface Surface)> { (0, new() { Material = "Air" }),
            (stopZ, new() { Material = "Air", IsStop = true, SemiDiameter = 2 }), (30, new() { Material = "Air" }) };
        for (var i = 0; i < segments.Length; i++)
        {
            nodes.Add((segments[i].FrontZMillimeters, new() { Material = "N-BK7", Radius = Radius(result.FrontCurvaturesPerMillimeter[i]), SemiDiameter = 3 }));
            nodes.Add((segments[i].FrontZMillimeters + segments[i].ThicknessMillimeters,
                new() { Material = "Air", Radius = Radius(result.BackCurvaturesPerMillimeter[i]), SemiDiameter = 3 }));
        }
        nodes = nodes.OrderBy(n => n.Z).ToList();
        for (var i = 0; i + 1 < nodes.Count; i++) nodes[i].Surface.Thickness = nodes[i + 1].Z - nodes[i].Z;
        optic.SurfaceGroup.ImportLegacySurfaces(new[] { new OpticalSurface { Material = "Air", Thickness = double.PositiveInfinity } }
            .Concat(nodes.Select(n => n.Surface)), optic.Materials);
        optic.Aperture.Kind = ApertureKind.FloatByStopSize; optic.Aperture.Value = 4;
        optic.Solves.KeepImageAtBackFocus = false;
        optic.Wavelengths.Add(new() { Nanometers = wavelength, IsPrimary = true, Weight = 1 });
        optic.Fields.Add(new() { Weight = 1 });
        Assert.Equal(20, optic.Paraxial.EstimateEffectiveFocalLength(), 10);
        Assert.Equal(4, optic.Paraxial.EstimateFNumber(), 10);
        Assert.Equal(0, optic.Paraxial.GetTransferMatrix(1, nodes.Count).A, 11);
        var stopIndex = nodes.FindIndex(n => n.Surface.IsStop) + 1;
        Assert.Equal(2, 2.5 * optic.Paraxial.GetTransferMatrix(1, stopIndex).A, 11);
        Assert.Equal(0, rays.RayCount);
        static double Radius(double curvature) => curvature == 0 ? double.PositiveInfinity : 1 / curvature;
    }

    [Fact]
    public void SingularOrInconsistentPhysicalSegmentsAreRejectedExplicitly()
    {
        ThickDelanoElement[] segments = [new(1, 2, 1.5), new(8, 2, 1.5)];
        Assert.Throws<ArgumentException>(() => FixedStopDelano.Construct(segments, 2.5, 2, 2, 30, 20, [1, 1, 1]));
        Assert.Throws<ArithmeticException>(() => FixedStopDelano.Construct(segments, 2.5, 0, 2, 30, 20, [1, 1, 1]));
        Assert.Throws<ArgumentException>(() => FixedStopDelano.Construct(segments, 2.5, 5, 2, 30, 20, [1, double.NaN, 1]));
        Assert.Throws<ArgumentException>(() => FixedStopDelano.Construct(segments, 2.5, 5, 2, 30, 20, [1, 1]));
    }
}
