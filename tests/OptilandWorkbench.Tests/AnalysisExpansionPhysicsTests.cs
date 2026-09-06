using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.FileIO;

namespace OptilandWorkbench.Tests;

public sealed class AnalysisExpansionPhysicsTests
{
    private static Optic Lens() => OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "zemax-ms-l7-high-na.ZMX")), ".zmx");

    [Fact]
    public void TransverseJonesTransportConservesUnitInputWithoutAttenuation()
    {
        var optic = Lens();
        var result = JonesPupilEngine.Generate(optic, (0, -0.7), optic.Wavelengths[0], 9,
            useFresnelCoatings: false, aimAtStop: true);
        var valid = result.Samples.Where(s => s.IsValid).ToArray();
        Assert.True(valid.Length > 20);
        foreach (var s in valid)
        {
            Assert.InRange(s.Jxx.Magnitude * s.Jxx.Magnitude + s.Jyx.Magnitude * s.Jyx.Magnitude, 1 - 1e-10, 1 + 1e-10);
            Assert.InRange(s.Jxy.Magnitude * s.Jxy.Magnitude + s.Jyy.Magnitude * s.Jyy.Magnitude, 1 - 1e-10, 1 + 1e-10);
            // Projection onto image X/Y may omit a longitudinal component, but cannot add energy.
            Assert.InRange(s.ImageExForY.Magnitude * s.ImageExForY.Magnitude + s.ImageEyForY.Magnitude * s.ImageEyForY.Magnitude, 0, 1 + 1e-10);
        }
    }

    [Fact]
    public void JonesBulkAbsorptionUsesAmplitudeBeerLambertWithoutDoubleCountingFresnel()
    {
        var optic = Lens();
        var wavelength = optic.Wavelengths[0];
        var transparent = JonesPupilEngine.Generate(optic, (0, 0), wavelength, 3, useFresnelCoatings: false, aimAtStop: true);
        var absorbing = JonesPupilEngine.Generate(optic, (0, 0), wavelength, 3, useFresnelCoatings: false, aimAtStop: true, includeBulkAbsorption: true);
        var ray = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, 0, 0, wavelength.Micrometers, true);
        var history = optic.SequentialRayTracer.Trace(ray).RayHistories.Single();
        var opticalDepth = history.Select((sample, i) => optic.SurfaceGroup.Items[i].MaterialBefore.ExtinctionCoefficient(wavelength.Nanometers) * sample.SegmentLength).Sum();
        Assert.True(opticalDepth > 0);
        var expectedAmplitude = Math.Exp(-2 * Math.PI * opticalDepth * 1000 / wavelength.Micrometers);
        var reference = transparent.Samples.Single(s => s.Px == 0 && s.Py == 0);
        var actual = absorbing.Samples.Single(s => s.Px == 0 && s.Py == 0);
        Assert.Equal(expectedAmplitude, actual.Jyy.Magnitude / reference.Jyy.Magnitude, 12);
    }

    [Fact]
    public void ContrastPhaseRetainsTheOriginalWavefrontAcrossCorrelationDirectionsAndFrequencies()
    {
        var optic = Lens();
        var low = new ContrastLossMapAnalysis(optic, sampling: 9, frequency: 20, showOpd: true).GenerateData();
        var high = new ContrastLossMapAnalysis(optic, sampling: 9, frequency: 80, showOpd: true).GenerateData();
        var lowOriginal = (AnalysisSeries[])low.Values["UnshiftedPupilPhaseSeries"];
        var highOriginal = (AnalysisSeries[])high.Values["UnshiftedPupilPhaseSeries"];
        var reference = lowOriginal[0].Points;
        var comparisons = new[] { lowOriginal[1], highOriginal[0], highOriginal[1] };
        foreach (var series in comparisons)
        {
            var shared = reference.Zip(series.Points)
                .Where(p => double.IsFinite(p.First.Value ?? double.NaN) && double.IsFinite(p.Second.Value ?? double.NaN)).ToArray();
            Assert.True(shared.Length > 20);
            foreach (var p in shared)
            {
                Assert.Equal(Math.Sin(2 * Math.PI * p.First.Value!.Value), Math.Sin(2 * Math.PI * p.Second.Value!.Value), 10);
                Assert.Equal(Math.Cos(2 * Math.PI * p.First.Value!.Value), Math.Cos(2 * Math.PI * p.Second.Value!.Value), 10);
            }
        }
        Assert.Equal(0, reference.Single(p => p.X == 0 && p.Y == 0).Value!.Value, 10);
        Assert.NotEqual(low.PlotPanes![2].Series.Single().Points.Single(p => p.X == 0 && p.Y == 0).Value,
            high.PlotPanes![2].Series.Single().Points.Single(p => p.X == 0 && p.Y == 0).Value);
    }

    [Fact]
    public void GaussianAngularConvergenceIsExplicitAndDoesNotReplaceGeneralDefaults()
    {
        var optic = Lens();
        var ordinary = new RmsWavefrontVsFieldAnalysis(optic, numRings: 6);
        Assert.Equal(6, ordinary.GaussianAzimuthalSamples);
        AnalysisData Calculate(int arms) => new RmsWavefrontVsFieldAnalysis(optic, numRings: 6,
            wavelengthNumber: 1)
        { GaussianAzimuthalSamples = arms }.GenerateData();
        var twelve = Calculate(12).PlotSeries.Single().Points;
        var twentyFour = Calculate(24).PlotSeries.Single().Points;
        Assert.Equal(twelve.Count, twentyFour.Count);
        foreach (var pair in twelve.Zip(twentyFour)) Assert.InRange(Math.Abs(pair.First.Y - pair.Second.Y), 0, 2e-7);
    }
}
