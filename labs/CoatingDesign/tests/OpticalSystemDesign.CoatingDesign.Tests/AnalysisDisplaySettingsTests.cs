using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.App.Laboratories.CoatingDesign;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed class AnalysisDisplaySettingsTests
{
    [Fact]
    public void IndependentAnalysisMatchesFactoryStepAndAnglesWithoutChangingDesign()
    {
        var d = Examples.Create(DesignKind.HighReflector);
        d = d with { Layers = [new(d.HighId, 65), new(d.LowId, 95)] };
        var fingerprint = d.Fingerprint(); var service = new DesignService();
        var factory = service.EvaluateAnalysisDisplay(d, AnalysisSpectrumOptions.Default);
        Assert.Single(factory); Assert.Equal(0, factory[0].AngleDegrees); Assert.Equal(801, factory[0].Spectrum.Average.Length);
        Assert.Equal(400, factory[0].Spectrum.Average[0].WavelengthNanometers); Assert.Equal(800, factory[0].Spectrum.Average[^1].WavelengthNanometers);
        Assert.Equal(400.5, factory[0].Spectrum.Average[1].WavelengthNanometers);
        var custom = service.EvaluateAnalysisDisplay(d, new(450, 650, .7, [0, 45]));
        Assert.Equal(2, custom.Length); Assert.Equal(287, custom[0].Spectrum.Average.Length);
        Assert.Equal(650, custom[1].Spectrum.Average[^1].WavelengthNanometers);
        Assert.NotEqual(custom[0].Spectrum.S[0].Power, custom[1].Spectrum.S[0].Power);
        Assert.Equal(fingerprint, d.Fingerprint()); Assert.Null(d.Result);
        Assert.Throws<ArgumentException>(() => service.EvaluateAnalysisDisplay(d, new(400, 800, 0, [0])));
        Assert.Throws<ArgumentException>(() => service.EvaluateAnalysisDisplay(d, new(400, 800, .001, [0])));
        Assert.Throws<ArgumentException>(() => service.EvaluateAnalysisDisplay(d, new(400, 800, .5, [0, 0])));
    }
    [Fact]
    public void SavedDefaultsRoundTripAndFactoryRestoreDoesNotUseDesignTargets()
    {
        var path = Path.Combine(Path.GetTempPath(), "coating-defaults-" + Guid.NewGuid() + ".json");
        try
        {
            Assert.Equal(.5, CoatingPlotDefaults.Load(path).Sampling.StepNm);
            var custom = new CoatingPlotDefaults(new(450, 700, 1, [0, 30]), CoatingWavelengthUnit.Micrometers, CoatingValueUnit.Fraction, true, 0, 1);
            custom.Save(path); var read = CoatingPlotDefaults.Load(path);
            Assert.Equal(custom.Sampling.AnglesDegrees, read.Sampling.AnglesDegrees); Assert.Equal(custom.WavelengthUnit, read.WavelengthUnit);
            Assert.Equal(custom.AutomaticY, read.AutomaticY);
            CoatingPlotDefaults.Factory.Save(path); read = CoatingPlotDefaults.Load(path);
            Assert.Equal(400, read.Sampling.MinimumNm); Assert.Equal(800, read.Sampling.MaximumNm); Assert.False(read.AutomaticY);
            Assert.Equal(100, read.MaximumY); Assert.Equal(new double[] { 0 }, read.Sampling.AnglesDegrees);
            File.WriteAllText(path, "{}"); Assert.Throws<ArgumentException>(() => CoatingPlotDefaults.Load(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
