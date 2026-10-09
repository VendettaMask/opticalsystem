using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Rays;

namespace OptilandWorkbench.Tests;

public sealed class RayLaunchCoordinateRepairTests
{
    [Fact]
    public void InfiniteAngleLaunchAndTraceFollowRigidTranslations()
        => CheckTranslations(FieldDefinitionKind.Angle, finite: false);

    [Fact]
    public void FiniteAngleLaunchAndTraceFollowRigidTranslations()
        => CheckTranslations(FieldDefinitionKind.Angle, finite: true);

    [Fact]
    public void FiniteObjectHeightLaunchAndTraceFollowRigidTranslations()
        => CheckTranslations(FieldDefinitionKind.ObjectHeight, finite: true);

    [Fact]
    public void ParaxialImageHeightLaunchAndTraceFollowRigidTranslations()
    {
        CheckTranslations(FieldDefinitionKind.ParaxialImageHeight, finite: false);
        CheckTranslations(FieldDefinitionKind.ParaxialImageHeight, finite: true);
    }

    [Fact]
    public void RealImageHeightSolveAndTraceFollowRigidTranslations()
    {
        CheckTranslations(FieldDefinitionKind.RealImageHeight, finite: false);
        CheckTranslations(FieldDefinitionKind.RealImageHeight, finite: true);
    }

    [Fact]
    public void PreparedAndBatchLaunchesUseTheSameMovingDatum()
    {
        foreach (var finite in new[] { false, true })
        foreach (var aiming in new[] { false, true })
        {
            var source = Fixture(finite ? FieldDefinitionKind.ObjectHeight : FieldDefinitionKind.Angle, finite);
            var shift = new Vector3D(10, -10, 7);
            var translated = Translate(source, shift);
            var wave = source.Wavelengths[0].Micrometers;
            var pupils = SpotAnalysisEngine.CreatePupilSamples(4, "uniform");
            var a = source.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(.6, .8, wave, pupils, aiming);
            var b = translated.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(.6, .8, wave, pupils, aiming);
            Assert.Equal(a.Rays.Count, b.Rays.Count);
            foreach (var pair in a.Rays.Zip(b.Rays)) AssertTranslatedRay(pair.First, pair.Second, shift);
            var originalSampler = source.SequentialRayTracer.RayGenerator.CreatePupilRaySampler(.6, .8, wave, aiming);
            var translatedSampler = translated.SequentialRayTracer.RayGenerator.CreatePupilRaySampler(.6, .8, wave, aiming);
            foreach (var pupil in new[] { (0d, 0d), (.25, .5), (-.5, 0d) })
                AssertTranslatedRay(originalSampler(pupil.Item1, pupil.Item2),
                    translatedSampler(pupil.Item1, pupil.Item2), shift);
        }
    }

    [Fact]
    public void JointTranslationPreservesWavefrontAndBothDiffractionPaths()
    {
        var source = Optic.CreateCookeTriplet();
        source.RayAimingEnabled = true;
        var wavelength = source.Wavelengths[0];
        foreach (var field in new[] { (0d, 0d), (0d, 1d) })
        {
            var baseline = WavefrontEngine.GenerateChiefRayUniform(source, field, wavelength, 16, aimAtStop: true);
            var huygens = DiffractionEngine.ComputeHuygensPsf(source, field, wavelength, 16, 16, .0005, aimAtStop: true);
            var fft = DiffractionEngine.ComputeFftPsf(source, field, wavelength, 16, 32, aimAtStop: true);
            foreach (var shift in new[] { new Vector3D(10, 10, 0), new Vector3D(20, 0, 3) })
            {
                var optic = Translate(source, shift);
                var wave = optic.Wavelengths[0];
                var actual = WavefrontEngine.GenerateChiefRayUniform(optic, field, wave, 16, aimAtStop: true);
                Assert.Equal(baseline.Samples.Count, actual.Samples.Count);
                Assert.InRange(Math.Abs(baseline.Radius - actual.Radius), 0, 1e-9);
                foreach (var pair in baseline.Samples.Zip(actual.Samples))
                {
                    Assert.Equal(pair.First.Intensity > 0, pair.Second.Intensity > 0);
                    if (pair.First.Intensity <= 0) continue;
                    Assert.InRange(Math.Abs(pair.First.OpdWaves - pair.Second.OpdWaves), 0, 1e-8);
                }
                Assert.InRange(MaxDifference(huygens.Values, DiffractionEngine.ComputeHuygensPsf(
                    optic, field, wave, 16, 16, .0005, aimAtStop: true).Values), 0, 1e-6);
                Assert.InRange(MaxDifference(fft.Values, DiffractionEngine.ComputeFftPsf(
                    optic, field, wave, 16, 32, aimAtStop: true).Values), 0, 1e-6);
            }
        }
    }

    [Fact]
    public void PhysicalObstructionStillBlocksTheTranslatedAimedRay()
    {
        var source = Fixture(FieldDefinitionKind.Angle, finite: false);
        source.SurfaceGroup.Items[1].PhysicalAperture = new CircularAperture(.001);
        foreach (var optic in new[] { source, Translate(source, new Vector3D(10, 10, 0)) })
        {
            var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, .5, 0,
                optic.Wavelengths[0].Micrometers, aimAtStop: true);
            var final = optic.SequentialRayTracer.TraceFinalSamples(bundle).Single();
            Assert.True(final is null || final.Vignetted || final.Intensity <= 0);
        }
    }

    private static void CheckTranslations(FieldDefinitionKind kind, bool finite)
    {
        foreach (var aiming in new[] { false, true })
        {
            var source = Fixture(kind, finite);
            foreach (var shift in new[] { new Vector3D(10, 10, 0), new Vector3D(-10, 10, 7), new Vector3D(20, 0, -5) })
            {
                var translated = Translate(source, shift);
                foreach (var field in new[] { (0d, 0d), (.6, .8) })
                foreach (var pupil in new[] { (0d, 0d), (.25, .5), (-.5, 0d) })
                foreach (var wave in source.Wavelengths)
                {
                    var a = source.SequentialRayTracer.RayGenerator.GenerateGeneric(field.Item1, field.Item2,
                        pupil.Item1, pupil.Item2, wave.Micrometers, aiming);
                    var b = translated.SequentialRayTracer.RayGenerator.GenerateGeneric(field.Item1, field.Item2,
                        pupil.Item1, pupil.Item2, wave.Micrometers, aiming);
                    AssertTranslatedRay(a.Rays.Single(), b.Rays.Single(), shift);
                    var original = source.SequentialRayTracer.TraceFinalSamples(a).Single();
                    var actual = translated.SequentialRayTracer.TraceFinalSamples(b).Single();
                    Assert.NotNull(original);
                    Assert.NotNull(actual);
                    Assert.True(original.Intensity > 0 && !original.Vignetted);
                    Assert.True(actual.Intensity > 0 && !actual.Vignetted);
                    Assert.InRange((actual.Position - original.Position - shift).Length, 0, 1e-8);
                    Assert.InRange((actual.Direction - original.Direction).Length, 0, 1e-9);
                    Assert.InRange(Math.Abs(actual.CumulativeOpticalPathLength - original.CumulativeOpticalPathLength), 0, 1e-8);
                    Assert.InRange(Math.Abs(actual.Intensity - original.Intensity), 0, 1e-10);
                }
            }
        }
    }

    private static void AssertTranslatedRay(RealRay original, RealRay actual, Vector3D shift)
    {
        Assert.InRange((actual.Origin - original.Origin - shift).Length, 0, 1e-8);
        Assert.InRange((actual.Direction - original.Direction).Length, 0, 1e-9);
        Assert.InRange(Math.Abs(actual.Intensity - original.Intensity), 0, 1e-12);
    }

    private static Optic Translate(Optic source, Vector3D shift)
    {
        var optic = Optic.FromSnapshot(source.ToSnapshot());
        optic.InvalidateRayTraceCache();
        foreach (var surface in optic.SurfaceGroup.Items)
            surface.CoordinateSystem = surface.CoordinateSystem with { Origin = surface.CoordinateSystem.Origin + shift };
        return optic;
    }

    private static Optic Fixture(FieldDefinitionKind kind, bool finite)
    {
        var optic = finite ? new Optic("Finite coordinate control") : Optic.CreateCookeTriplet();
        if (finite)
        {
            optic.Aperture.Kind = ApertureKind.EntrancePupilDiameter;
            optic.Aperture.Value = 4;
            optic.Wavelengths.Clear();
            optic.Wavelengths.Add(new Wavelength { Nanometers = 480, IsPrimary = false });
            optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
            optic.Wavelengths.Add(new Wavelength { Nanometers = 650, IsPrimary = false });
            optic.SurfaceGroup.ImportLegacySurfaces([
                new OpticalSurface { Label = "Object", Thickness = 80, Material = "Air", SemiDiameter = 10 },
                new OpticalSurface { Label = "Front", Radius = 35, Thickness = 5, Material = "N-BK7", SemiDiameter = 10 },
                new OpticalSurface { Label = "Back", Radius = -35, Thickness = 60, Material = "Air", SemiDiameter = 10, IsStop = true },
                new OpticalSurface { Label = "Image", Material = "Air", SemiDiameter = 15 }
            ]);
        }
        optic.FieldDefinition = kind;
        optic.Fields.Clear();
        optic.Fields.Add(new FieldPoint { X = 0, Y = 0 });
        var scale = kind == FieldDefinitionKind.Angle ? (finite ? .5 : 5) : 1;
        optic.Fields.Add(new FieldPoint { X = .6 * scale, Y = .8 * scale });
        return optic;
    }

    private static double MaxDifference(double[,] a, double[,] b)
        => a.Cast<double>().Zip(b.Cast<double>()).Max(p => Math.Abs(p.First - p.Second));
}
