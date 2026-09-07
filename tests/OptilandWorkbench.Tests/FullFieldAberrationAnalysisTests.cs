using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Application.Runtime;

namespace OptilandWorkbench.Tests;

public sealed class FullFieldAberrationAnalysisTests
{
    [Fact]
    public void OnAxisOnlyFieldUsesOneFiniteCenterSample()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.Fields.Clear();
        optic.Fields.Add(new FieldPoint { Label = "On axis" });

        var data = new FullFieldAberrationAnalysis(
            optic,
            xFieldSamples: 5,
            yFieldSamples: 5,
            pupilSampling: 8).GenerateData();

        var point = Assert.Single(data.PlotSeries[0].Points);
        Assert.Equal(0, point.X);
        Assert.Equal(0, point.Y);
        Assert.True(double.IsFinite(point.Value!.Value));
        Assert.Equal(1, data.Values["ValidFieldSamples"]);
        Assert.Equal(0, data.Values["SkippedOutsideFieldNormalization"]);
    }

    [Fact]
    public void ApplicationDefaultCompletesForOnAxisOnlyField()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.Fields.Clear();
        optic.Fields.Add(new FieldPoint { Label = "On axis" });
        var runtime = new WorkbenchRuntime(optic);

        var parameters = runtime.GetAnalysisParameters("Full Field Aberration");
        Assert.Equal(0, parameters.Single(parameter => parameter.Key == "XFieldWidth").Minimum);
        Assert.Equal(0, parameters.Single(parameter => parameter.Key == "YFieldWidth").Minimum);

        var view = runtime.BuildAnalysisView(
            "Full Field Aberration",
            new Dictionary<string, string>
            {
                ["XFieldSamples"] = "5",
                ["YFieldSamples"] = "5",
                ["PupilSampling"] = "8 x 8",
                ["MaximumTerm"] = "9"
            });

        Assert.Single(Assert.Single(view.SeriesList).Points);
        Assert.Contains(view.Rows, row => row.Metric == "归一化边界外采样数" && row.Value == "0");
    }

    [Fact]
    public void SamplesOutsideRadialFieldNormalizationAreOmitted()
    {
        var optic = Optic.CreateCookeTriplet();
        var maximumField = FieldCoordinates.MaximumRadius(optic.Fields);

        var data = new FullFieldAberrationAnalysis(
            optic,
            fieldShape: "矩形",
            xFieldWidth: 1,
            yFieldWidth: 1,
            fieldNumber: optic.Fields.Count,
            xFieldSamples: 3,
            yFieldSamples: 3,
            pupilSampling: 8).GenerateData();

        Assert.All(data.PlotSeries[0].Points, point =>
            Assert.True((point.X * point.X) + (point.Y * point.Y)
                <= (maximumField * maximumField) + 1e-12));
        Assert.Equal(5, data.Values["SkippedOutsideFieldNormalization"]);
    }

    [Fact]
    public void GeneratesEllipseOfValueScaledFieldIcons()
    {
        var optic = Optic.CreateCookeTriplet();
        optic.FieldDefinition = FieldDefinitionKind.RealImageHeight;

        var data = new FullFieldAberrationAnalysis(
            optic,
            xFieldWidth: 4.5,
            yFieldWidth: 4.5,
            maximumTerm: 9,
            xFieldSamples: 5,
            yFieldSamples: 5,
            pupilSampling: 8).GenerateData();

        var series = Assert.Single(data.PlotSeries);
        Assert.Equal(AnalysisSeriesKind.Scatter, series.Kind);
        Assert.Equal(13, series.Points.Count);
        Assert.All(series.Points, point =>
        {
            Assert.True(point.Value.HasValue);
            Assert.True(double.IsFinite(point.Value!.Value));
            Assert.True(
                (point.X * point.X / (4.5 * 4.5))
                + (point.Y * point.Y / (4.5 * 4.5))
                <= 1 + 1e-12);
        });
        Assert.Equal("X视场，单位：毫米", series.XAxisLabel);
        Assert.Equal("Y视场，单位：毫米", series.YAxisLabel);
        Assert.Equal("离焦", Assert.IsType<string>(data.Values["Aberration"]));
    }

    [Fact]
    public void RectangleAndSignedDisplayRetainTheCompleteGrid()
    {
        var data = new FullFieldAberrationAnalysis(
            Optic.CreateCookeTriplet(),
            fieldShape: "矩形",
            xFieldWidth: 1,
            yFieldWidth: 1,
            maximumTerm: 9,
            xFieldSamples: 3,
            yFieldSamples: 3,
            pupilSampling: 8,
            displayMode: "带符号").GenerateData();

        Assert.Equal(9, data.PlotSeries[0].Points.Count);
        Assert.Equal("矩形", Assert.IsType<string>(data.Values["FieldShape"]));
        Assert.Equal("带符号", Assert.IsType<string>(data.Values["DisplayMode"]));
    }
}
