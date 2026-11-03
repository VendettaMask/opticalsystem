using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Interactions;

namespace OpticalSystemDesign.Tests;

public sealed class GeometricPupilAndFrequencyTests
{
    [Theory]
    [InlineData(486, -6.780363036593026, -5.114612230995146)]
    [InlineData(587, -5.434767949731042, -3.773835738122495)]
    [InlineData(656, -4.8218167996025905, -3.277745222044682)]
    public async Task DisplacedStopRetainsNativePhaseAcrossWavelengths(double nm, double minus, double plus)
    {
        var optic = (await Import()).Configurations[1];
        var wave = optic.Wavelengths.Single(w => w.Nanometers == nm);
        var phase = WavefrontEngine.GenerateChiefRaySamples(optic, (-1, 0), wave, [(-1d, 0d), (1d, 0d)], true);
        Assert.InRange(Math.Abs(phase.Samples[0].OpdWaves - minus), 0, 1e-6);
        Assert.InRange(Math.Abs(phase.Samples[1].OpdWaves - plus), 0, 1e-6);
        // The formal scalar paraxial report retains its independent convention.
        Assert.Equal(-1100.1415031357033, optic.Paraxial.EstimateExitPupilLocation(.587), 8);
    }

    [Fact]
    public async Task TranslatingTheDisplacedPupilPreservesItsPhase()
    {
        var optic = (await Import()).Configurations[1]; var shifted = optic.Clone();
        foreach (var surface in shifted.SurfaceGroup.Items)
            surface.CoordinateSystem = surface.CoordinateSystem with { Origin = surface.CoordinateSystem.Origin + new Vector3D(10, 6, -4) };
        foreach (var field in new[] { (0d, 0d), (-1d, 0d) })
        {
            var a = WavefrontEngine.GenerateChiefRaySamples(optic, field, optic.Wavelengths[1], [(-.7, 0d), (0d, .7), (.3, -.4)], true);
            var b = WavefrontEngine.GenerateChiefRaySamples(shifted, field, shifted.Wavelengths[1], [(-.7, 0d), (0d, .7), (.3, -.4)], true);
            for (var i = 0; i < a.Samples.Count; i++) Assert.InRange(Math.Abs(a.Samples[i].OpdWaves - b.Samples[i].OpdWaves), 0, 1e-6);
        }
    }

    [Fact]
    public async Task ObstructedAuxiliaryPupilIsOptInAndDoesNotChangePhysicalTransmission()
    {
        var optic = (await new ZemaxZmxImporter().ImportConfigurationSetFileAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "biocular", "Unobscured Gregorian.zmx"))).Configurations[0];
        // Deliberately mask the axis: the real imported mirror's decentered clear
        // aperture transmits it. This synthetic mask tests reference-only opt-in.
        optic.SurfaceGroup.Items[3].PhysicalAperture = new CircularAperture(55);
        var wave = optic.Wavelengths.First(w => w.IsPrimary);
        var launch = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, 0, 0, wave.Micrometers, optic.RayAimingEnabled);
        Assert.Null(optic.SequentialRayTracer.TraceFinalSamples(launch).Single());
        Assert.Throws<AnalysisDataUnavailableException>(() => ParabasalPupilEngine.Compute(optic, wave));
        var pupil = ParabasalPupilEngine.Compute(optic, wave, allowGeometricReference: true);
        Assert.True(double.IsFinite(pupil.ImageAxialDistanceMillimeters));
        Assert.True(pupil.DiameterMillimeters > 0);
        Assert.Null(optic.SequentialRayTracer.TraceFinalSamples(launch).Single());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(20, 0)]
    [InlineData(20, 10)]
    [InlineData(-20, -15)]
    public void FourierScaleIsConjugateToDetectorCoordinates(double angle, double detectorTilt)
    {
        var optic = Optic.CreateBlank(); optic.Aperture.Value = 2;
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = double.PositiveInfinity },
            new OpticalSurface { IsStop = true, Thickness = 20, SemiDiameter = 2 },
            new OpticalSurface { Thickness = 0, SemiDiameter = 20 }
        ]);
        optic.SurfaceGroup.Items[1].InteractionModel = new ParaxialLensInteractionModel(.05, .05);
        var image = optic.SurfaceGroup.Items[^1];
        image.CoordinateSystem = image.CoordinateSystem with { RotationYDegrees = detectorTilt };
        optic.Fields.Clear(); optic.Fields.Add(new FieldPoint { X = angle });
        optic.Wavelengths.Clear(); optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        var field = (angle == 0 ? 0d : Math.Sign(angle), 0d);
        var a = angle * Math.PI / 180; var t = detectorTilt * Math.PI / 180;
        // Independent thin-plane-lens slope law and detector tangent projection.
        Vector3D Direction(double x, double y)
        { var d = new Vector3D(Math.Tan(a) - .05 * x, -.05 * y, 1); return d / d.Length; }
        var chief = Direction(0, 0);
        double L(Vector3D d) => Math.Cos(t) * d.X - Math.Sin(t) * d.Z;
        var expectedX = 1 / (Math.Abs(L(Direction(1, 0)) - L(chief)) + Math.Abs(L(Direction(-1, 0)) - L(chief)));
        var expectedY = 1 / (Math.Abs(Direction(0, 1).Y - chief.Y) + Math.Abs(Direction(0, -1).Y - chief.Y));
        var actual = DiffractionEngine.ImagePlaneFourierFNumbers(optic, field, optic.Wavelengths[0], false);
        Assert.InRange(Math.Abs(actual.Sagittal - expectedX), 0, 1e-10);
        Assert.InRange(Math.Abs(actual.Tangential - expectedY), 0, 1e-10);
        if (detectorTilt == 0)
        {
            var psf = DiffractionEngine.ComputeFftPsf(optic, field, optic.Wavelengths[0], 16, 32,
                cellCenteredPupil: true, zemaxFftSampling: true, imageDelta: -1);
            var angular = DiffractionEngine.WorkingFNumbers(optic, field, optic.Wavelengths[0], zemaxDirectionalAverage: true);
            Assert.Equal(angular.Sagittal, psf.SagittalWorkingFNumber, 10);
            Assert.Equal(expectedX, psf.SagittalFourierFNumber!.Value, 10);
        }
    }

    private static Task<ZemaxZmxImportResult> Import() => new ZemaxZmxImporter().ImportConfigurationSetFileAsync(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "biocular", "Bi-ocular lens.ZMX"));
}
