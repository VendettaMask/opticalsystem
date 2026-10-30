using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed class CoatingBandSensitivityTests
{
    [Theory]
    [InlineData(450, 0, ThinFilmPolarization.S)]
    [InlineData(940, 23, ThinFilmPolarization.P)]
    [InlineData(1300, 37, ThinFilmPolarization.Unpolarized)]
    public void MeasuredWidthAndLocalPeakDerivativesMatchIndependentThicknessDifferences(double wave, double angle, ThinFilmPolarization pol)
    {
        var inc = new AirMaterial(); var sub = new ConstantIndexMaterial("substrate", 1.52);
        var low = new ConstantIndexMaterial("low", 1.45, .0003); var high = new ConstantIndexMaterial("high", 2.2, .002);
        CoherentFilm Film(IMaterial m, double factor = 1) => new(m, factor * CoherentThinFilmSolver.QuarterWaveThickness(m, inc, wave, angle));
        var mirror = Enumerable.Range(0, 7).Select(i => Film(i % 2 == 0 ? high : low)).ToArray();
        var films = mirror.Concat(new[] { Film(low, 2) }).Concat(mirror.Reverse()).ToArray();
        CoherentThinFilmSolver Solver(CoherentFilm[] layers) => new(inc, sub, layers);
        var grid = ThinFilmSpectrum.Grid(wave * .9, wave * 1.1, 256);
        var response = grid.Select(w => Solver(films).EvaluateThicknessSensitivity(w, angle, pol)).ToArray();
        var actual = ThinFilmSpectrum.MeasureThicknessSensitivity(grid, response);
        Assert.Equal(ThinFilmSpectrum.Measure(ThinFilmSpectrum.Sample(Solver(films), grid, angle, pol)), actual.Metrics);
        Assert.NotNull(actual.FwhmNanometers); Assert.NotNull(actual.PeakWavelengthNanometers);
        var h = wave * 1e-7;
        foreach (var layer in new[] { 0, films.Length / 2, films.Length - 1 })
        {
            var minus = films.ToArray(); var plus = films.ToArray();
            minus[layer] = minus[layer] with { ThicknessNanometers = minus[layer].ThicknessNanometers - h };
            plus[layer] = plus[layer] with { ThicknessNanometers = plus[layer].ThicknessNanometers + h };
            var a = ThinFilmSpectrum.Sample(Solver(minus), grid, angle, pol); var b = ThinFilmSpectrum.Sample(Solver(plus), grid, angle, pol);
            Assert.Equal(actual.Metrics.PeakWavelengthNanometers, ThinFilmSpectrum.Measure(a).PeakWavelengthNanometers);
            Assert.Equal(actual.Metrics.PeakWavelengthNanometers, ThinFilmSpectrum.Measure(b).PeakWavelengthNanometers);
            Assert.InRange(Math.Abs((ThinFilmSpectrum.Measure(b).FwhmNanometers!.Value - ThinFilmSpectrum.Measure(a).FwhmNanometers!.Value) / (2 * h)
                - actual.FwhmNanometers![layer]), 0, 2e-6);
            Assert.InRange(Math.Abs((ThinFilmSpectrum.EstimatePeakWavelength(b)!.Value - ThinFilmSpectrum.EstimatePeakWavelength(a)!.Value) / (2 * h)
                - actual.PeakWavelengthNanometers![layer]), 0, 2e-6);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-720)]
    [InlineData(-10000)]
    public void RelativeShapeDerivativesSurviveAbsoluteTransmissionUnderflow(double offset)
    {
        var waves = new[] { 490d, 500, 510, 520, 530 };
        var logs = new[] { -2d, -.8, -.1, -.9, -2.2 }; var rates = new[] { .03, -.02, .01, .04, -.01 };
        ThinFilmSample[] Samples(double x) => waves.Select((w, i) => new ThinFilmSample(w,
            new ThinFilmPower(.2, Math.Exp(logs[i] + offset + x * rates[i]), .8 - Math.Exp(logs[i] + offset + x * rates[i]), logs[i] + offset + x * rates[i]))).ToArray();
        var samples = Samples(0);
        var derivatives = samples.Select((s, i) => new ThinFilmThicknessSensitivity(s.Power,
            [new(0, s.Power.Transmittance * rates[i], -s.Power.Transmittance * rates[i], rates[i])], true)).ToArray();
        var actual = ThinFilmSpectrum.MeasureThicknessSensitivity(waves, derivatives); const double h = 1e-3;
        var a = Samples(-h); var b = Samples(h);
        Assert.NotNull(actual.FwhmNanometers); Assert.NotNull(actual.PeakWavelengthNanometers);
        Assert.InRange(Math.Abs((ThinFilmSpectrum.Measure(b).FwhmNanometers!.Value - ThinFilmSpectrum.Measure(a).FwhmNanometers!.Value) / (2 * h) - actual.FwhmNanometers![0]), 0, 1e-7);
        Assert.InRange(Math.Abs((ThinFilmSpectrum.EstimatePeakWavelength(b)!.Value - ThinFilmSpectrum.EstimatePeakWavelength(a)!.Value) / (2 * h) - actual.PeakWavelengthNanometers![0]), 0, 1e-7);
    }

    [Fact]
    public void MissingEdgesEvanescentExitAndFlatPeaksDoNotInventMetricDerivatives()
    {
        var waves = new[] { 500d, 510, 520 };
        var flat = waves.Select(_ => new ThinFilmThicknessSensitivity(new(.2, .8, 0, Math.Log(.8)), [new(0, 0, 0, 0)], true)).ToArray();
        var band = ThinFilmSpectrum.MeasureThicknessSensitivity(waves, flat);
        Assert.Null(band.FwhmNanometers); Assert.Null(band.PeakWavelengthNanometers); Assert.Null(band.PeakWavelengthEstimateNanometers);
        var tir = flat.Select(s => s with { Power = new(1, 0, 0, double.NegativeInfinity), HasLogTransmissionDerivative = false }).ToArray();
        band = ThinFilmSpectrum.MeasureThicknessSensitivity(waves, tir);
        Assert.Null(band.FwhmNanometers); Assert.Null(band.LeftHalfMaximumNanometers); Assert.Null(band.RightHalfMaximumNanometers);
        Assert.Null(band.PeakWavelengthNanometers); Assert.Null(band.PeakWavelengthEstimateNanometers);
        Assert.Throws<ArgumentException>(() => ThinFilmSpectrum.MeasureThicknessSensitivity([500, 500, 520], flat));
        Assert.Throws<ArgumentException>(() => ThinFilmSpectrum.MeasureThicknessSensitivity([500, 520], flat));
    }
}
