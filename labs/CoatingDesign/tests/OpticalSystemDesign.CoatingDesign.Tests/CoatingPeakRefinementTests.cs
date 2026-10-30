using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed class CoatingPeakRefinementTests
{
    [Fact]
    public void RefinedPhysicalPeakIsStationaryAndIndependentOfTheStartingGridAndOverallScale()
    {
        ThinFilmPeakRefinement Refine(double wave, int intervals)
        {
            var inc = new AirMaterial(); var low = new ConstantIndexMaterial("L", 1.45, .0003); var high = new ConstantIndexMaterial("H", 2.2, .002);
            CoherentFilm Film(IMaterial material, double factor = 1) => new(material,
                factor * CoherentThinFilmSolver.QuarterWaveThickness(material, inc, wave, 23));
            var mirror = Enumerable.Range(0, 7).Select(i => Film(i % 2 == 0 ? high : low)).ToArray();
            var solver = new CoherentThinFilmSolver(inc, new ConstantIndexMaterial("S", 1.52), mirror.Concat([Film(low, 2)]).Concat(mirror.Reverse()));
            var samples = ThinFilmSpectrum.Sample(solver, ThinFilmSpectrum.Grid(wave * .9, wave * 1.1, intervals), 23, ThinFilmPolarization.P);
            var refined = ThinFilmSpectrum.RefineTransmissionPeak(solver, samples, 23, ThinFilmPolarization.P);
            Assert.Equal(solver.Evaluate(refined.Peak.WavelengthNanometers, 23, ThinFilmPolarization.P), refined.Peak.Power);
            Assert.True(refined.Peak.Power.LogTransmittance >= samples.Max(s => s.Power.LogTransmittance)); Assert.Equal(34, refined.Evaluations);
            foreach (var side in new[] { -1, 1 })
                Assert.True(solver.Evaluate(refined.Peak.WavelengthNanometers + side * wave * 1e-5, 23, ThinFilmPolarization.P).Transmittance <= refined.Peak.Power.Transmittance + 1e-10);
            return refined;
        }
        var a = Refine(450, 128); var b = Refine(1350, 256);
        Assert.InRange(Math.Abs(a.Peak.Power.Transmittance - b.Peak.Power.Transmittance), 0, 2e-13);
        Assert.InRange(Math.Abs(a.Peak.WavelengthNanometers / 450 - b.Peak.WavelengthNanometers / 1350), 0, 1e-7);
    }

    [Fact]
    public void FlatOrEvanescentSpectraKeepTheirActualPowerWithoutInventingAnInteriorPeak()
    {
        foreach (var (solver, angle) in new[]
        {
            (new CoherentThinFilmSolver(new AirMaterial(), new AirMaterial(), []), 0d),
            (new CoherentThinFilmSolver(new ConstantIndexMaterial("incident", 1.7), new AirMaterial(), [new(new ConstantIndexMaterial("film", 1.6), 100)]), 65d)
        })
        {
            var samples = ThinFilmSpectrum.Sample(solver, [500, 550, 600], angle, ThinFilmPolarization.S);
            var refined = ThinFilmSpectrum.RefineTransmissionPeak(solver, samples, angle, ThinFilmPolarization.S);
            Assert.Equal(samples[0], refined.Peak); Assert.Equal(0, refined.Evaluations);
        }
    }

    [Fact]
    public void RefinementHonorsCancellationAndItsBoundedResourceContract()
    {
        var solver = new CoherentThinFilmSolver(new AirMaterial(), new ConstantIndexMaterial("S", 1.52),
            [new(new ConstantIndexMaterial("L", Math.Sqrt(1.52)), 550 / (4 * Math.Sqrt(1.52)))]);
        var samples = ThinFilmSpectrum.Sample(solver, [450, 550, 650], 0, ThinFilmPolarization.S);
        Assert.Throws<ArgumentException>(() => ThinFilmSpectrum.RefineTransmissionPeak(solver, samples, 0, ThinFilmPolarization.S, 0));
        Assert.Throws<ArgumentException>(() => ThinFilmSpectrum.RefineTransmissionPeak(solver, samples, 0, ThinFilmPolarization.S, 129));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); using var scope = ComputationCancellation.Push(cancelled.Token);
        Assert.Throws<OperationCanceledException>(() => ThinFilmSpectrum.RefineTransmissionPeak(solver, samples, 0, ThinFilmPolarization.S));
    }
}
