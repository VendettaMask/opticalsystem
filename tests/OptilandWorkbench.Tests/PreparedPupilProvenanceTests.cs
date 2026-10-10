using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Apertures;
using OptilandWorkbench.Core.Apodization;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Materials;
using OptilandWorkbench.Core.Propagation;
using OptilandWorkbench.Core.Rays;

namespace OptilandWorkbench.Tests;

public sealed class PreparedPupilProvenanceTests
{
    [Theory]
    [InlineData("curvature")]
    [InlineData("thickness")]
    [InlineData("material")]
    [InlineData("physical_aperture")]
    [InlineData("system_aperture")]
    [InlineData("environment")]
    [InlineData("vignetting")]
    [InlineData("apodization")]
    [InlineData("polarization")]
    public void ChangedOpticRejectsPreviouslyPreparedWavefront(string change)
    {
        var optic = Fixture();
        var pupil = Prepare(optic);
        switch (change)
        {
            case "curvature": optic.SurfaceGroup.Items[1].Radius *= 1.001; break;
            case "thickness": optic.SurfaceGroup.Items[1].Thickness += .001; break;
            case "material": optic.SurfaceGroup.Items[1].MaterialAfter = new ConstantIndexMaterial("Changed", 1.51); break;
            case "physical_aperture": optic.SurfaceGroup.Items[1].PhysicalAperture = new CircularAperture(100); break;
            case "system_aperture": optic.Aperture.Value *= .999; break;
            case "environment": optic.Environment.TemperatureCelsius += .1; break;
            case "vignetting": optic.Fields[0].VignetteFactorX = .001; break;
            case "apodization": optic.Apodization = new GaussianApodization(1); break;
            case "polarization": optic.Polarization = optic.Polarization with { Unpolarized = false }; break;
        }
        Assert.Throws<InvalidOperationException>(() => Compute(optic, pupil));
    }

    [Fact]
    public void ChangedOpticRejectsOldJonesEvenWithFreshWavefront()
    {
        var optic = Fixture();
        var jones = JonesPupilEngine.Generate(optic, (0, 0), optic.Wavelengths[0], 16,
            cellCentered: true, aimAtStop: true);
        optic.SurfaceGroup.Items[1].Radius *= 1.001;
        Assert.Throws<InvalidOperationException>(() => Compute(optic, Prepare(optic), jones));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingWavefrontSourceCannotBeReconstructedFromMatchingLabels(bool afocal)
    {
        var optic = Fixture(); optic.ImageSpaceAfocal = afocal;
        var traced = Prepare(optic);
        var unknown = new WavefrontResult(traced.Samples, traced.Radius, traced.ReferenceOpticalPath,
            traced.VignettedRayCount, traced.ChiefImageDirectionZ, traced.ImageRefractiveIndex,
            traced.ImageSpaceAfocal, traced.AfocalPupilDiameterMillimeters)
        {
            UseRayAiming = traced.UseRayAiming, SourceField = traced.SourceField,
            SourceWavelengthNanometers = traced.SourceWavelengthNanometers,
            SourceReferenceWavelengthNanometers = traced.SourceReferenceWavelengthNanometers,
            PupilGrid = traced.PupilGrid, ReferenceSphere = traced.ReferenceSphere
        };
        Assert.Throws<InvalidOperationException>(() => Compute(optic, unknown));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IdenticalSnapshotCanReusePreparedSamples(bool polarized)
    {
        var optic = Fixture();
        var pupil = Prepare(optic);
        var jones = polarized ? JonesPupilEngine.Generate(optic, (0, 0), optic.Wavelengths[0], 16,
            cellCentered: true, aimAtStop: true) : null;
        var clone = Optic.FromSnapshot(optic.ToSnapshot());
        Assert.Equal(Compute(optic, pupil, jones).Values.Cast<double>(),
            Compute(clone, pupil, jones).Values.Cast<double>());
    }

    [Fact]
    public void CallerPhaseRemainsAuthoritativeAndUnmodified()
    {
        var optic = Fixture();
        var pupil = Prepare(optic);
        var changed = pupil with { Samples = pupil.Samples.Select(sample =>
            sample with { OpdWaves = sample.OpdWaves + 2 * sample.NormalizedPupilX }).ToArray() };
        var original = changed.Samples.ToArray();
        var result = Compute(optic, changed);
        Assert.Equal(original, changed.Samples);
        Assert.False(result.Values.Cast<double>().SequenceEqual(Compute(optic, pupil).Values.Cast<double>()));
    }

    [Fact]
    public void PresentationNamesDoNotInvalidateTheOpticalSource()
    {
        var optic = Fixture(); var pupil = Prepare(optic);
        var original = Compute(optic, pupil);
        optic.Name = "改名"; optic.Fields[0].Label = "视场";
        optic.Wavelengths[0].Label = "波长"; optic.SurfaceGroup.Items[1].Label = "表面";
        Assert.Equal(original.Values.Cast<double>(), Compute(optic, pupil).Values.Cast<double>());
    }

    [Fact]
    public void CustomModelWithLossySnapshotCannotReusePreparedInput()
    {
        var optic = Fixture(); optic.Apodization = new CustomUniform();
        var pupil = Prepare(optic);
        Assert.Throws<InvalidOperationException>(() => Compute(optic, pupil));
        Assert.True(DiffractionEngine.ComputeFftPsf(optic, (0, 0), optic.Wavelengths[0], 16, 32,
            cellCenteredPupil: true, aimAtStop: true).PeakStrehlRatio > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MaterialPropagationNotCapturedBySnapshotCannotReusePreparedInput(bool builtIn)
    {
        var optic = Fixture();
        optic.SurfaceGroup.Items[1].MaterialAfter = new ConstantIndexMaterial("Custom propagation", 1.5,
            propagationModel: builtIn ? new EntranceDirectionApproximationPropagationModel(0) : new CustomPropagation());
        var pupil = Prepare(optic);
        Assert.Throws<InvalidOperationException>(() => Compute(optic, pupil));
        Assert.True(DiffractionEngine.ComputeFftPsf(optic, (0, 0), optic.Wavelengths[0], 16, 32,
            cellCenteredPupil: true, aimAtStop: true).PeakStrehlRatio > 0);
    }

    [Fact]
    public void AfocalSourceAlsoRejectsChangedDetectorFrame()
    {
        var optic = Fixture(); optic.ImageSpaceAfocal = true;
        var pupil = Prepare(optic);
        var image = optic.SurfaceGroup.Items[^1];
        image.CoordinateSystem = image.CoordinateSystem with
        { Origin = image.CoordinateSystem.Origin + new Vector3D(.001, 0, 0) };
        Assert.Throws<InvalidOperationException>(() => Compute(optic, pupil));
    }

    [Fact]
    public void JonesBulkAbsorptionCannotBeAppliedAgainToAnAbsorbedWavefront()
    {
        var optic = Fixture();
        var jones = JonesPupilEngine.Generate(optic, (0, 0), optic.Wavelengths[0], 16,
            cellCentered: true, aimAtStop: true, includeBulkAbsorption: true);
        Assert.Throws<InvalidOperationException>(() => Compute(optic, Prepare(optic), jones));
    }

    private static Optic Fixture()
    {
        var optic = Optic.CreateCookeTriplet(); optic.RayAimingEnabled = true; return optic;
    }
    private static WavefrontResult Prepare(Optic optic) => WavefrontEngine.GenerateChiefRayUniform(
        optic, (0, 0), optic.Wavelengths[0], 16, cellCentered: true, aimAtStop: true);
    private static PsfResult Compute(Optic optic, WavefrontResult pupil, JonesPupilResult? jones = null)
        => DiffractionEngine.ComputeFftPsf(optic, (0, 0), optic.Wavelengths[0], 16, 32,
            usePolarization: jones is not null, cellCenteredPupil: true, aimAtStop: true,
            preparedWavefront: pupil, preparedPolarization: jones);
    private sealed class CustomUniform : IApodizationModel
    {
        public string Kind => "uniform";
        public double Intensity(double normalizedPupilX, double normalizedPupilY) => 1;
        public IApodizationModel Clone() => new CustomUniform();
    }
    private sealed class CustomPropagation : IPropagationModel
    {
        public string Kind => "homogeneous";
        public RealRay Propagate(RealRay ray, double distance) => new HomogeneousPropagationModel().Propagate(ray, distance);
        public IPropagationModel Clone() => new CustomPropagation();
    }
}
