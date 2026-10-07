using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Tests;

public sealed class VignettedWavefrontPhaseTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 30)]
    [InlineData(false, 90)]
    [InlineData(true, 0)]
    [InlineData(true, 30)]
    [InlineData(true, 90)]
    public void TiltedPlaneWaveRemainsFlatInCompressedShiftedRotatedAfocalPupil(bool aiming, double angle)
    {
        var optic = new Optic("Tilted plane wave") { ImageSpaceAfocal = true };
        optic.Aperture.Value = 10;
        optic.Fields.Add(new FieldPoint { X = .7, Y = -1.1,
            VignetteFactorX = .25, VignetteFactorY = .4,
            VignetteDecenterX = .1, VignetteDecenterY = -.15, VignetteAngleDegrees = angle });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        optic.SurfaceGroup.ImportLegacySurfaces([
            new() { Thickness = double.PositiveInfinity, SemiDiameter = 50 },
            new() { Thickness = 10, SemiDiameter = 50, IsStop = true },
            new() { Thickness = 0, SemiDiameter = 50 }
        ]);
        var samples = new[] { (0.0, 0.0), (-.7, 0.0), (.7, 0.0), (0.0, -.7), (0.0, .7) };
        var result = WavefrontEngine.GenerateChiefRaySamples(optic,
            SpotAnalysisEngine.DefinedFields(optic).Single(), optic.Wavelengths[0], samples, aimAtStop: aiming);
        Assert.Equal(0, result.VignettedRayCount);
        Assert.All(result.Samples, sample => Assert.InRange(Math.Abs(sample.OpdWaves), 0, 1e-7));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 30)]
    [InlineData(true, 0)]
    [InlineData(true, 30)]
    public void RemappedPupilTracesHaveTheSameRelativePhaseAsPhysicalSamples(bool aiming, double angle)
    {
        var clear = Optic.CreateCookeTriplet();
        var vignetted = Optic.FromSnapshot(clear.ToSnapshot());
        var field = SpotAnalysisEngine.DefinedFields(clear).Last();
        var factors = new PupilVignetting(.08, -.06, .3, .4, angle);
        var row = vignetted.Fields[^1];
        row.VignetteDecenterX = factors.DecenterX; row.VignetteDecenterY = factors.DecenterY;
        row.VignetteFactorX = factors.CompressionX; row.VignetteFactorY = factors.CompressionY;
        row.VignetteAngleDegrees = factors.AngleDegrees;
        var samples = new[] { (X: 0.0, Y: 0.0), (X: -.5, Y: 0.0), (X: .5, Y: 0.0), (X: 0.0, Y: -.5), (X: 0.0, Y: .5) };
        var physical = samples.Select(p => factors.Transform(p.X, p.Y)).ToArray();
        var sphere = WavefrontEngine.CreateChiefRayReferenceSphere(clear, field, clear.Wavelengths[0], aiming);
        var a = WavefrontEngine.GenerateChiefRaySamples(vignetted, field, vignetted.Wavelengths[0], samples,
            aimAtStop: aiming, referenceSphere: sphere);
        var b = WavefrontEngine.GenerateChiefRaySamples(clear, field, clear.Wavelengths[0], physical,
            aimAtStop: aiming, referenceSphere: sphere);
        Assert.Equal(0, a.VignettedRayCount); Assert.Equal(0, b.VignettedRayCount);
        foreach (var (actual, expected) in a.Samples.Zip(b.Samples))
            Assert.InRange(Math.Abs((actual.OpdWaves - a.Samples[0].OpdWaves)
                - (expected.OpdWaves - b.Samples[0].OpdWaves)), 0, 1e-7);
    }

    [Theory]
    [InlineData(ReferenceSphereStrategy.CentroidSphere, 60)]
    [InlineData(ReferenceSphereStrategy.CentroidSphere, 180)]
    [InlineData(ReferenceSphereStrategy.BestFitSphere, 60)]
    [InlineData(ReferenceSphereStrategy.BestFitSphere, 180)]
    public void RotatedCompressedPupilPreservesReferenceSphereForTheSamePhysicalRays(ReferenceSphereStrategy strategy, double angle)
    {
        var full = Optic.CreateCookeTriplet();
        full.Aperture.Kind = ApertureKind.EntrancePupilDiameter;
        full.Aperture.Value = 5;
        var small = Optic.FromSnapshot(full.ToSnapshot());
        foreach (var row in full.Fields)
        { row.VignetteFactorX = .5; row.VignetteFactorY = .5; row.VignetteAngleDegrees = angle; }
        foreach (var row in small.Fields)
        { row.VignetteFactorX = .5; row.VignetteFactorY = .5; }
        var field = SpotAnalysisEngine.DefinedFields(full).Last();
        var pupil = SpotAnalysisEngine.CreatePupilSamples(3, "hexapolar");
        var fullRays = full.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(field.Hx, field.Hy, full.Wavelengths[0].Micrometers, pupil).Rays;
        var smallRays = small.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(field.Hx, field.Hy, small.Wavelengths[0].Micrometers, pupil).Rays;
        // A 60-degree hexapolar rotation permutes the same physical rays;
        // keep the entrance-pupil diameter and launch plane identical.
        foreach (var ray in fullRays)
            Assert.Contains(smallRays, other => (other.Origin - ray.Origin).Length < 1e-9 && (other.Direction - ray.Direction).Length < 1e-9);
        var a = ReferenceSphereWavefrontEngine.Generate(full, field, full.Wavelengths[0], 3, strategy);
        var b = ReferenceSphereWavefrontEngine.Generate(small, field, small.Wavelengths[0], 3, strategy);
        Assert.Equal(0, a.VignettedRayCount); Assert.Equal(0, b.VignettedRayCount);
        Assert.InRange(Math.Abs(a.Rms - b.Rms), 0, 1e-7);
    }
}
