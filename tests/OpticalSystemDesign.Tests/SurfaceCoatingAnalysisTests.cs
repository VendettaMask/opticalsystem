using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

public sealed class SurfaceCoatingAnalysisTests
{
    [Fact]
    public void FresnelPowerAndBrewsterAngleUseTheSelectedInterface()
    {
        var plate = PolarizationAnalysisIntegrationTests.Plate();
        var angle = Math.Atan(1.5) * 180 / Math.PI;
        var data = new SurfaceCoatingAnalysis(plate, SurfaceCoatingAnalysis.Reflection,
            new(MinimumAngle: 0, MaximumAngle: angle, Samples: 2)).GenerateData();
        Assert.Equal(AnalysisOutcome.Success, data.Outcome);
        Assert.Equal(.04, data.PlotSeries[0].Points[0].Y, 10);
        Assert.Equal(0, data.PlotSeries[1].Points[^1].Y, 10);
        Assert.Equal(data.PlotSeries[0].Points[^1].Y / 2, data.PlotSeries[2].Points[^1].Y, 10);
        Assert.Equal(AnalysisAxisUnit.Degree, data.PlotSeries[0].XUnit);
    }

    [Fact]
    public async Task ChangingTheDesignFileUsesItsSavedMaterialsAndPolarizedLabResponse()
    {
        var runtime = new Application.Runtime.WorkbenchRuntime(PolarizationAnalysisIntegrationTests.Plate());
        var settings = new Dictionary<string, string>
        {
            ["SurfaceIndex"] = "1",
            ["MinimumWavelengthNm"] = "540",
            ["MaximumWavelengthNm"] = "560",
            ["IncidenceAngle"] = "0",
            ["Samples"] = "3"
        };
        var curves = new List<double>();
        foreach (var name in new[] { "METAL", "FP" })
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Examples", "Sequential", "Coatings", name + ".coating.json");
            settings["CoatingFile"] = path;
            var design = await CoatingDesign.Engine.ExperimentStore.OpenAsync(path);
            var lab = new CoatingDesign.Engine.DesignService().EvaluateAnalysisDisplay(design, new(540, 560, 10, [0]));
            var data = runtime.BuildAnalysisData(SurfaceCoatingAnalysis.Spectrum, settings);
            Assert.Equal(AnalysisOutcome.Success, data.Outcome);
            var power = data.PlotSeries[0].Points[1].Y;
            Assert.Equal(lab[0].Spectrum.S[1].Power.Transmittance, power, 12);
            curves.Add(power);
        }
        Assert.NotEqual(curves[0], curves[1]);
    }

    [Fact]
    public void UnresolvedCoatingSurvivesNativeRoundTripUntilReplaced()
    {
        var input = SequentialExampleCatalog.Examples.Single(e => e.Name == "Etalon");
        var imported = ZemaxZmxReader.Import(File.ReadAllText(input.FilePath));
        var restored = new Optic();
        restored.ApplySnapshot(imported.ToSnapshot());
        var surface = restored.SurfaceGroup.Items[1];
        Assert.Equal("ETALON", surface.Coating);
        Assert.Equal("ETALON", surface.Clone().ReferencedCoating);
        Assert.Contains("ETALON", new SurfaceCoatingAnalysis(restored, SurfaceCoatingAnalysis.Spectrum).GenerateData().OutcomeReason);
        surface.CoatingModel = new SimpleCoatingModel(.65, .3);
        Assert.Null(surface.ReferencedCoating);
        var data = new SurfaceCoatingAnalysis(restored, SurfaceCoatingAnalysis.Spectrum).GenerateData();
        Assert.Equal(.65, data.PlotSeries[0].Points[0].Y);
        Assert.Equal(AnalysisOutcome.Unavailable, new SurfaceCoatingAnalysis(restored, SurfaceCoatingAnalysis.Phase).GenerateData().Outcome);
    }
}
