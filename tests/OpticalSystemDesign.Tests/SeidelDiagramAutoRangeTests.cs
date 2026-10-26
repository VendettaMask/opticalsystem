using System.Globalization;
using System.Reflection;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

public sealed class SeidelDiagramAutoRangeTests
{
    [Theory]
    [InlineData(0.1)]
    [InlineData(1)]
    [InlineData(3)]
    public void AutomaticRangeFitsEverySurfaceAndTotalWithoutChangingCoefficients(double fieldScale)
    {
        var optic = LoadLens();
        foreach (var field in optic.Fields)
        {
            field.X *= fieldScale;
            field.Y *= fieldScale;
        }

        var automatic = new SeidelDiagramAnalysis(optic, 2, maximumAberration: 0).GenerateData();
        var manual = new SeidelDiagramAnalysis(optic, 2, maximumAberration: 0.001).GenerateData();
        var maximum = Assert.IsType<double>(automatic.Values["MaximumAberration"]);
        Assert.True(double.IsFinite(maximum) && maximum > 0);
        Assert.Equal(-maximum, automatic.PlotOptions!.YMinimum);
        Assert.Equal(maximum, automatic.PlotOptions.YMaximum);
        Assert.Equal(0.01, automatic.Values["GridInterval"]);
        Assert.Equal("总和", automatic.Table!.Rows[^1][0]);
        Assert.Equal(manual.PlotOptions! with { YMinimum = -maximum, YMaximum = maximum }, automatic.PlotOptions);
        for (var index = 0; index < automatic.SeriesList!.Count; index++)
        {
            Assert.Equal(manual.SeriesList![index].Points, automatic.SeriesList[index].Points);
            Assert.All(automatic.SeriesList[index].Points, point => Assert.True(Math.Abs(point.Y) < maximum));
        }
        Assert.Equal(manual.Table!.Rows.SelectMany(row => row), automatic.Table.Rows.SelectMany(row => row));
        Assert.All(automatic.Table.Rows.SelectMany(row => row.Skip(1)), value =>
            Assert.True(Math.Abs(double.Parse(value, CultureInfo.InvariantCulture)) < maximum));
        Assert.Equal(0.001, manual.Values["MaximumAberration"]);
        Assert.Equal(-0.001, manual.PlotOptions.YMinimum);
        Assert.Equal(0.001, manual.PlotOptions.YMaximum);
    }

    [Fact]
    public void DesktopDefaultsToAutomaticWhilePositiveSavedRangesRemainManual()
    {
        var runtime = new WorkbenchRuntime(LoadLens());
        var parameter = Assert.Single(runtime.GetAnalysisParameters("Seidel Diagram"), item => item.Key == "MaximumAberration");
        Assert.Equal("0", parameter.DefaultValue);
        Assert.Equal(0, parameter.Minimum);
        Assert.Contains("0=自动", parameter.DisplayName);
        var automatic = runtime.BuildAnalysisView("Seidel Diagram");
        var mapper = typeof(WorkbenchApplication).Assembly.GetType("OpticalSystemDesign.Application.Services.WorkbenchMapper")!;
        var view = Assert.IsType<AnalysisViewDto>(mapper.GetMethod("ToAnalysisViewDto", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [automatic]));
        var build = typeof(AnalysisPanel).GetMethod("BuildSeidelDiagramPlot", BindingFlags.Static | BindingFlags.NonPublic)!;
        var control = Assert.IsType<SeidelDiagramControl>(build.Invoke(null, [view]));
        Assert.Equal(automatic.PlotOptions.YMaximum!.Value, control.MaximumAberration, 6);
        Assert.Equal(0.01, control.GridInterval);
        Assert.Same(view.Table, control.Table);

        var saved = new Dictionary<string, string> { ["MaximumAberration"] = "0.1" };
        Assert.Equal("0.1", runtime.MergeAnalysisSettings("Seidel Diagram", saved)["MaximumAberration"]);
        var manual = runtime.BuildAnalysisView("Seidel Diagram", saved);
        Assert.Equal(-0.1, manual.PlotOptions.YMinimum);
        Assert.Equal(0.1, manual.PlotOptions.YMaximum);
    }

    [Fact]
    public void AutomaticRangePreservesUnavailableResult()
    {
        var optic = LoadLens();
        optic.Wavelengths.Clear();
        var data = new SeidelDiagramAnalysis(optic, maximumAberration: 0).GenerateData();
        Assert.Equal(OpticalSystemDesign.Core.Analysis.AnalysisOutcome.Unavailable, data.Outcome);
        Assert.Null(data.Table);
        Assert.False(string.IsNullOrWhiteSpace(data.OutcomeReason));
    }

    private static Optic LoadLens() => OpticalFormatCatalog.Import(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "zemax-123456.ZMX")), ".zmx");
}
