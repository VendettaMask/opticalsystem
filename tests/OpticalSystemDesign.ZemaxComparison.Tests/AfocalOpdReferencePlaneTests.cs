using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.ZemaxComparison.Workbench;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

public sealed class AfocalOpdReferencePlaneTests
{
    [Theory]
    [InlineData(1, ReferenceOpdMode.ExitPupil, false)]
    [InlineData(2, ReferenceOpdMode.ExitPupil, false)]
    [InlineData(3, ReferenceOpdMode.ExitPupil, false)]
    [InlineData(1, ReferenceOpdMode.Absolute, false)]
    [InlineData(3, ReferenceOpdMode.Absolute, false)]
    [InlineData(1, ReferenceOpdMode.Absolute2, false)]
    [InlineData(3, ReferenceOpdMode.Absolute2, false)]
    [InlineData(1, ReferenceOpdMode.ExitPupil, true)]
    [InlineData(3, ReferenceOpdMode.ExitPupil, true)]
    public async Task ReferenceChiefSelectionKeepsTheSystemPupilDatum(
        int wavelengthNumber, ReferenceOpdMode mode, bool defaultReference)
    {
        var optic = (await WorkbenchExecutor.Import(Path.Combine(AppContext.BaseDirectory,
            "Fixtures/afocal-repair/sources/Afocal/Afocal Riflescope.ZMX"))).ActiveOptic;
        optic.ReferenceOpd = mode;
        var before = optic.ToSnapshot();
        var wave = optic.Wavelengths[wavelengthNumber - 1];
        var primary = optic.Wavelengths.Single(w => w.IsPrimary);
        var referenceWave = defaultReference ? primary : wave;
        var generator = optic.SequentialRayTracer.RayGenerator;
        var chiefBundle = generator.GenerateGeneric(0, 1, 0, 0, wave.Micrometers, true);
        var chief = optic.SequentialRayTracer.TraceFinalSamples(chiefBundle).Single()!;
        var reference = optic.SequentialRayTracer.TraceFinalSamples(generator.GenerateGeneric(
            0, 1, 0, 0, referenceWave.Micrometers, true)).Single()!;
        var frame = optic.SurfaceGroup.Items[^1].CoordinateSystem;
        var normal = mode == ReferenceOpdMode.Absolute2 ? frame.ToGlobalDirection(new(0, 0, 1)) : reference.Direction;
        var distance = optic.Paraxial.EstimateExitPupilLocation(primary.Micrometers);
        var anchor = mode == ReferenceOpdMode.ExitPupil ? reference.Position + normal
            * ((distance - frame.ToLocalPoint(reference.Position).Z) / frame.ToLocalDirection(normal).Z)
            : mode == ReferenceOpdMode.Absolute2 ? frame.Origin : reference.Position;
        double PathToPlane(OpticalSystemDesign.Core.Rays.RayTraceSample ray)
            => ray.CumulativeOpticalPathLength - Dot(ray.Position - anchor, normal) / Dot(ray.Direction, normal);
        var pupils = new[] { (0d, 0d), (0d, 1d), (0d, -.8), (.6, 0d), (-.6, 0d) };
        var result = WavefrontEngine.GenerateChiefRaySamples(optic, (0, 1), wave, pupils, aimAtStop: true,
            referenceWavelength: defaultReference ? null : wave);
        Assert.Equal(referenceWave.Nanometers, result.SourceReferenceWavelengthNanometers);
        Assert.InRange((result.AfocalReferencePlaneOrigin!.Value - anchor).Length, 0, 1e-10);
        for (var i = 0; i < pupils.Length; i++)
        {
            var bundle = generator.GenerateGeneric(0, 1, pupils[i].Item1, pupils[i].Item2, wave.Micrometers, true);
            var ray = optic.SequentialRayTracer.TraceFinalSamples(bundle).Single()!;
            var incidentPhase = Dot(bundle.Rays[0].Origin - chiefBundle.Rays[0].Origin, chiefBundle.Rays[0].Direction);
            var expected = (PathToPlane(chief) - PathToPlane(ray) - incidentPhase) / (wave.Micrometers * .001);
            Assert.True(result.Samples[i].Intensity > 0);
            Assert.InRange(Math.Abs(result.Samples[i].OpdWaves - expected), 0, 1e-7);
        }
        if (!defaultReference && wavelengthNumber != 2 && mode == ReferenceOpdMode.ExitPupil)
            Assert.True(Math.Abs(optic.Paraxial.EstimateExitPupilLocation(wave.Micrometers) - distance) > .1);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(before, JsonFiles.Options),
            System.Text.Json.JsonSerializer.Serialize(optic.ToSnapshot(), JsonFiles.Options));
    }

    private static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}
