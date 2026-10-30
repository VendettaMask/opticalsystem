using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed class CoatingSpectrumEdgeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-690)]
    [InlineData(-745)]
    [InlineData(-1000)]
    public void HalfMaximumWidthAndPeakAreInvariantToTransmissionScaleIncludingUnderflow(double logPeak)
    {
        // An independent piecewise-linear triangular peak has half-height crossings at 525 and 575 nm.
        var samples = new[] { Point(500, double.NegativeInfinity), Point(550, logPeak), Point(600, double.NegativeInfinity) };
        var m = ThinFilmSpectrum.Measure(samples);
        Assert.Equal(550, m.PeakWavelengthNanometers);
        Assert.Equal(525, m.LeftHalfMaximumNanometers!.Value, 10);
        Assert.Equal(575, m.RightHalfMaximumNanometers!.Value, 10);
        Assert.Equal(50, m.FwhmNanometers!.Value, 10);
        Assert.Equal(Math.Exp(logPeak), m.PeakTransmittance);
    }

    [Theory]
    [InlineData(ThinFilmPolarization.S)]
    [InlineData(ThinFilmPolarization.P)]
    [InlineData(ThinFilmPolarization.Unpolarized)]
    public void TotalInternalReflectionHasNoHalfMaximumEdgesOrNanMetrics(ThinFilmPolarization pol)
    {
        var solver = new CoherentThinFilmSolver(new ConstantIndexMaterial("incident", 1.7), new AirMaterial(),
            [new(new ConstantIndexMaterial("film", 1.6), 100)]);
        var samples = ThinFilmSpectrum.Sample(solver, [450, 550, 650], 65, pol);
        Assert.All(samples, s => Assert.Equal(0, s.Power.Transmittance));
        var m = ThinFilmSpectrum.Measure(samples);
        Assert.Null(m.LeftHalfMaximumNanometers); Assert.Null(m.RightHalfMaximumNanometers); Assert.Null(m.FwhmNanometers);
        Assert.Equal(0, m.PeakTransmittance);
    }

    [Theory]
    [InlineData(ThinFilmPolarization.S, false)]
    [InlineData(ThinFilmPolarization.P, false)]
    [InlineData(ThinFilmPolarization.Unpolarized, false)]
    [InlineData(ThinFilmPolarization.S, true)]
    [InlineData(ThinFilmPolarization.P, true)]
    [InlineData(ThinFilmPolarization.Unpolarized, true)]
    public async Task DarkNarrowBandSpectraFinishEvaluationAndCanReopenWithoutInventingAPassband(ThinFilmPolarization pol, bool tir)
    {
        var d = GeneralCoatingTests.Fixture(tir ? 940 : 550, 1.4, tir ? 1.6 : 2.3, tir ? 0 : 4,
            tir ? 65 : 15, pol);
        if (tir)
        {
            var incident = MaterialLibrary.Capture(new ConstantIndexMaterial("transparent incident", 1.7), "synthetic TIR control", 300, 2000);
            var substrate = MaterialLibrary.Capture(new AirMaterial(), "synthetic TIR control", 300, 2000) with { Id = "edge-air-substrate" };
            d = d with { IncidentId = incident.Id, SubstrateId = substrate.Id, Materials = d.Materials.Concat([incident, substrate]).ToArray() };
        }
        d = d with
        {
            Layers = [new(d.HighId, tir ? 100 : 100000)],
            Settings = d.Settings with { PreviewIntervals = 40, OptimizationIntervals = 20, MaximumThicknessNm = 100000 }
        };
        var evaluated = new DesignService().Evaluate(d);
        var r = evaluated.Result!;
        Assert.Equal(ThinFilmSpectrum.MetricsVersion, r.SpectrumMetricsVersion);
        Assert.False(r.Passed); Assert.Equal(0, r.Metrics.PeakTransmittance); Assert.Null(r.Metrics.FwhmNanometers);
        Assert.True(r.Metrics.LeftHalfMaximumNanometers is null or > 0);
        Assert.True(r.Metrics.RightHalfMaximumNanometers is null or > 0);
        Assert.All(r.Spectrum, s => { Assert.Equal(0, s.Power.Transmittance); Assert.False(double.IsNaN(s.Power.LogTransmittance)); });
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".coating.json");
        try
        {
            await ExperimentStore.SaveAsync(evaluated, path);
            var reopened = await ExperimentStore.OpenAsync(path);
            Assert.Equal(evaluated.Fingerprint(), reopened.Fingerprint()); Assert.Equal(r.Metrics, reopened.Result!.Metrics);
            Assert.Equal(r.SpectrumMetricsVersion, reopened.Result.SpectrumMetricsVersion);
        }
        finally { File.Delete(path); }
    }

    private static ThinFilmSample Point(double wave, double logT) => new(wave, new(0, Math.Exp(logT), 1 - Math.Exp(logT), logT));
}
