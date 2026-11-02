using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Apertures;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Tests;

public sealed class IdenticalFieldIlluminationTests
{
    [Fact]
    public async Task ImportedInterferometerKeepsNormalizedSupportSeparateFromRawIntegral()
    {
        var imported = await new ZemaxZmxImporter().ImportConfigurationSetFileAsync(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "optical-testing", "Twyman Green Interferometer.zmx"));
        foreach (var optic in new[] { imported.Configurations[0], Optic.FromSnapshot(imported.Configurations[0].ToSnapshot()) })
        {
            optic.RayAimingEnabled = false; // The original menu comparison request.
            foreach (var density in new[] { 20, 40 })
            {
                Assert.Throws<AnalysisDataUnavailableException>(() => IlluminationMetrics.Evaluate(optic, (0, 0), .55,
                    density, sampling: IlluminationSamplingKind.FiniteImageCosine, includeScalarTransmission: false));
                var result = new RelativeIlluminationAnalysis(optic, rayDensity: density,
                    sampling: IlluminationSamplingKind.FiniteImageCosine, includeScalarTransmission: false).GenerateData();
                Assert.Equal(false, result.Values["RawIntegralAvailable"]);
                Assert.All(result.PlotSeries.Single().Points, p => { Assert.Equal(0, p.X); Assert.Equal(1, p.Y); });
                Assert.All((int[])result.Values["ValidRayCounts"], count => Assert.True(count > 0));
            }
        }
    }

    [Fact]
    public void IdenticalPhysicalFieldsCancelWithoutInventingAnAbsoluteFoldedIntegral()
    {
        var optic = Folded();
        Assert.Throws<AnalysisDataUnavailableException>(() => IlluminationMetrics.Evaluate(optic, (0, 0), .55,
            sampling: IlluminationSamplingKind.UniformImageCosine));
        var result = new RelativeIlluminationAnalysis(optic, sampling: IlluminationSamplingKind.UniformImageCosine).GenerateData();
        Assert.Equal("IdenticalPhysicalFieldDomain", result.Values["Normalization"]);
        Assert.Equal(false, result.Values["RawIntegralAvailable"]);
        Assert.False(result.Values.ContainsKey("RawProjectedCosineArea"));
        Assert.False(result.Values.ContainsKey("EffectiveFNumbers"));
        Assert.All(result.PlotSeries.Single().Points, p => { Assert.Equal(0, p.X); Assert.Equal(1, p.Y); });
        Assert.Equal(1, IlluminationMetrics.RelativeToAxis(optic, (0, 0), .55, sampling: IlluminationSamplingKind.UniformImageCosine));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingTransmittedSupportNeverProducesAUnitCurve(bool opaque)
    {
        var optic = Folded();
        if (opaque) optic.SurfaceGroup.Items[1].CoatingModel = new SimpleCoatingModel(0);
        else optic.SurfaceGroup.Items[1].PhysicalAperture = new OffsetRadialAperture(.1, offsetX: 3);
        var result = new RelativeIlluminationAnalysis(optic, sampling: IlluminationSamplingKind.UniformImageCosine).GenerateData();
        Assert.Equal(AnalysisOutcome.Unavailable, result.Outcome);
        Assert.Empty(result.PlotSeries);
        Assert.Throws<AnalysisDataUnavailableException>(() => IlluminationMetrics.RelativeToAxis(optic, (0, 0), .55,
            sampling: IlluminationSamplingKind.UniformImageCosine));
    }

    [Fact]
    public void NonzeroFieldDomainStillRequiresAValidIntegral()
    {
        var optic = Folded(); optic.Fields[0].Y = .1;
        Assert.Throws<AnalysisDataUnavailableException>(() => new RelativeIlluminationAnalysis(optic,
            sampling: IlluminationSamplingKind.UniformImageCosine).GenerateData());
    }

    private static Optic Folded()
    {
        var optic = new Optic(); optic.Aperture.Value = 2;
        optic.Fields.Add(new FieldPoint()); optic.Wavelengths.Add(new Wavelength { Nanometers = 550 });
        optic.SurfaceGroup.Replace([new OpticalSurface { Thickness = 10 }, new OpticalSurface { IsStop = true, Thickness = 10, SemiDiameter = 1 }, new OpticalSurface { Thickness = 0 }]);
        optic.SurfaceGroup.Items[1].InteractionModel = new FoldedMap(); return optic;
    }
    private sealed class FoldedMap : IInteractionModel
    {
        public string Kind => "test-folded-pupil-map";
        public RealRayInteractionResult Interact(RealRay ray, SurfaceInteractionContext context)
        {
            var r2 = ray.Origin.X * ray.Origin.X + ray.Origin.Y * ray.Origin.Y;
            var factor = (1 - 2 * r2) / 10;
            return new(ray with { Direction = new Vector3D(ray.Origin.X * factor, ray.Origin.Y * factor, 1) }, RayInteractionKind.Transmitted);
        }
        public ParaxialRay Interact(ParaxialRay ray, SurfaceInteractionContext context) => ray;
        public IInteractionModel Clone() => new FoldedMap();
    }
}
