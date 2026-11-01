using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class WavefrontTraceBatchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentTraceBatchesRetainLaunchRaysOrderingAcceptanceAndPhase(bool polarized)
    {
        var optic = Optic.CreateBlank(); var wave = optic.Wavelengths.First(w => w.IsPrimary);
        var glass = new ConstantIndexMaterial("Lossless plate", 1.5);
        optic.Aperture.Value = 1;
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = 2 },
            new OpticalSurface { Thickness = 3, MaterialAfter = glass, SemiDiameter = .3, IsStop = true },
            new OpticalSurface { Thickness = 2, MaterialBefore = glass, SemiDiameter = 2 },
            new OpticalSurface { SemiDiameter = 2 }
        ]);
        var coordinates = new[] { (-.8, -.3), (0d, 0d), (.4, .2), (.7, .7), (-.5, .1), (.2, -.6), (0d, .8) };
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0, 0,
            wave.Micrometers, coordinates.Select(p => new PupilSample(p.Item1, p.Item2, 1)), false);
        var expected = WavefrontEngine.TraceFinalSamples(optic, bundle, polarized);
        var actual = WavefrontEngine.TraceFinalSamples(optic, bundle, polarized, 3);
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i] is null, actual[i] is null);
            if (expected[i] is not { } a || actual[i] is not { } b) continue;
            Assert.Equal(a.Vignetted, b.Vignetted);
            Assert.InRange((a.Position - b.Position).Length, 0, 1e-11);
            Assert.InRange((a.Direction - b.Direction).Length, 0, 1e-13);
            Assert.InRange(Math.Abs(a.Intensity - b.Intensity), 0, 1e-13);
            Assert.InRange(Math.Abs(a.CumulativeOpticalPathLength - b.CumulativeOpticalPathLength), 0, 1e-11);
            if (b.Vignetted) continue;
            var direction = bundle.Rays[i].Direction;
            var glassZ = Math.Sqrt(1 - (direction.X * direction.X + direction.Y * direction.Y) / (1.5 * 1.5));
            Assert.InRange((b.Direction - direction).Length, 0, 1e-13);
            Assert.InRange(Math.Abs(b.CumulativeOpticalPathLength - (4 / direction.Z + 4.5 / glassZ)), 0, 1e-11);
            Assert.InRange(Math.Abs(b.PhaseInclusiveOpticalPathLength!.Value - b.CumulativeOpticalPathLength), 0, 1e-11);
        }
        var slice = bundle.Slice(2, 3);
        for (var i = 0; i < slice.Rays.Count; i++) Assert.Same(bundle.Rays[i + 2], slice.Rays[i]);
        Assert.Equal(1_000_000, SequentialTraceLimits.MaximumRayCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => WavefrontEngine.TraceFinalSamples(optic, bundle,
            polarized, SequentialTraceLimits.MaximumRayCount + 1));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        using var scope = ComputationCancellation.Push(cancelled.Token);
        Assert.Throws<OperationCanceledException>(() => WavefrontEngine.TraceFinalSamples(optic, bundle, polarized, 3));
    }
}
