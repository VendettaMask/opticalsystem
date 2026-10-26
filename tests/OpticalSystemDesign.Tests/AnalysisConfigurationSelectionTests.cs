using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Tests;

public sealed class AnalysisConfigurationSelectionTests
{
    public static IEnumerable<object[]> OverlayAnalyses() => WorkbenchAnalysisCatalog.Descriptors
        .Where(descriptor => AnalysisConfigurationSelection.AllDisplay(descriptor.CanonicalKey) == AnalysisConfigurationDisplay.Overlay)
        .Select(descriptor => new object[] { descriptor.CanonicalKey });

    [Theory]
    [MemberData(nameof(OverlayAnalyses))]
    public async Task EveryAdvertisedOverlayCanComputeThreeConfigurations(string key)
    {
        using var app = WorkbenchApplication.Create(key == "Relative Illumination" ? "cooke" : null);
        if (key == "Relative Illumination")
        {
            // The imported Zoom's image-cosine envelope is unavailable even
            // for one configuration; retain that failure in a separate guard.
            app.MultiConfiguration.Add(); app.MultiConfiguration.Add();
        }
        else await app.Documents.OpenAsync(ConfigurationMatrixAnalysisTests.ZoomPath());
        var result = await Run(app, key, "All");
        Assert.Equal(OpticalSystemDesign.Application.Contracts.AnalysisOutcome.Success, result.View.Outcome);
        var series = result.View.PlotPanes.Count == 0 ? result.View.Series : result.View.PlotPanes.SelectMany(pane => pane.Series).ToArray();
        Assert.Equal(new int?[] { 1, 2, 3 }, series.Where(item => item.Points.Count > 0).Select(item => item.ConfigurationNumber).Distinct());
    }

    [Fact]
    public async Task AllPreservesAnUnavailableIndividualAnalysisWithoutInventingAnIlluminationCurve()
    {
        using var app = WorkbenchApplication.Create();
        await app.Documents.OpenAsync(ConfigurationMatrixAnalysisTests.ZoomPath());
        var single = await Assert.ThrowsAsync<AnalysisDataUnavailableException>(() => Run(app, "Relative Illumination", "Current"));
        var all = await Assert.ThrowsAsync<InvalidOperationException>(() => Run(app, "Relative Illumination", "All"));
        Assert.Contains(single.Message, all.Message);
        Assert.Contains("结构 1", all.Message);
    }

    [Theory]
    [InlineData("Spot Diagram")]
    [InlineData("Prescription Report")]
    [InlineData("First Order")]
    public async Task ConfigurationCsvColumnUsesTypedIdentityForPlotsTablesAndSummaryRows(string key)
    {
        using var app = WorkbenchApplication.Create();
        await app.Documents.OpenAsync(ConfigurationMatrixAnalysisTests.ZoomPath());
        var result = await Run(app, key, "2");
        var view = result.View with
        {
            Name = "custom caption",
            Series = result.View.Series.Select(series => series with { Name = "custom series" }).ToArray(),
            PlotPanes = result.View.PlotPanes.Select(pane => pane with
            {
                Title = "custom pane",
                Series = pane.Series.Select(series => series with { Name = "custom series" }).ToArray()
            }).ToArray()
        };
        using var reader = new StringReader(OpticalSystemDesign.Application.Formatting.AnalysisCsvFormatter.Format(view));
        using var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(reader) { HasFieldsEnclosedInQuotes = true };
        parser.SetDelimiters(",");
        Assert.Equal("configuration", parser.ReadFields()![^1]);
        var count = 0;
        while (!parser.EndOfData) { Assert.Equal("2", parser.ReadFields()![^1]); count++; }
        Assert.True(count > 0);
    }

    [Fact]
    public async Task EverySequentialAnalysisHidesTheSelectorForOneConfigurationAndPublishesStableChoicesForMultiple()
    {
        using var app = WorkbenchApplication.Create();
        var names = WorkbenchAnalysisCatalog.DescriptorsForMode(OpticalWorkbenchMode.Sequential);
        foreach (var name in names)
            Assert.DoesNotContain(app.Analyses.GetParameters(name.CanonicalKey), parameter => parameter.Key == "Configuration");
        await app.Documents.OpenAsync(ConfigurationMatrixAnalysisTests.ZoomPath());
        foreach (var name in names)
        {
            var parameter = Assert.Single(app.Analyses.GetParameters(name.CanonicalKey), parameter => parameter.Key == "Configuration");
            Assert.Equal(new[] { "Current", "1", "2", "3" }, parameter.Choices!.Where(choice => choice != "All"));
            Assert.Equal("2 / 3", parameter.ChoiceLabels!["2"]);
            Assert.Equal("当前的", parameter.ChoiceLabels["Current"]);
            Assert.Equal(AnalysisConfigurationSelection.AllDisplay(name.CanonicalKey) != AnalysisConfigurationDisplay.Single,
                parameter.Choices!.Contains("All"));
        }
        app.Documents.NewBlank();
        Assert.DoesNotContain(app.Analyses.MergeSettings("Spot Diagram", new Dictionary<string, string> { ["Configuration"] = "3" }), item => item.Key == "Configuration");
    }

    [Theory]
    [InlineData("Spot Diagram")]
    [InlineData("Ray Fan")]
    [InlineData("First Order")]
    [InlineData("Prescription Report")]
    [InlineData("Cardinal Points Data")]
    [InlineData("Wavefront Map")]
    public async Task CurrentFollowsTheActiveConfigurationWhilePinnedSelectionDoesNotMutateTheDocument(string key)
    {
        using var app = WorkbenchApplication.Create();
        await app.Documents.OpenAsync(ConfigurationMatrixAnalysisTests.ZoomPath());
        app.MultiConfiguration.Activate(1);
        var expected = await Run(app, key, "Current");
        var track = expected.SourceDocument!.TotalTrack;
        app.MultiConfiguration.Activate(0);
        var revision = app.Events.Revision;
        var pinned = await Run(app, key, "2");
        Assert.Equal(revision, app.Events.Revision);
        Assert.Equal(0, app.MultiConfiguration.GetNavigationState().ActiveIndex);
        Assert.Equal(new[] { 2 }, pinned.View.ConfigurationScope!.ConfigurationNumbers);
        Assert.Equal("2", pinned.View.ConfigurationScope.Selection);
        Assert.Equal(track, pinned.SourceDocument!.TotalTrack, 12);
        Assert.Equal(expected.View.Series.Select(series => series.Points), pinned.View.Series.Select(series => series.Points));
        Assert.Equal(expected.View.PlotPanes.SelectMany(pane => pane.Series).Select(series => series.Points),
            pinned.View.PlotPanes.SelectMany(pane => pane.Series).Select(series => series.Points));
        Assert.Equal(expected.View.Rows.Skip(1), pinned.View.Rows.Skip(1));
        var current = await Run(app, key, "Current");
        Assert.Equal(new[] { 1 }, current.View.ConfigurationScope!.ConfigurationNumbers);
        Assert.NotEqual(pinned.RequestFingerprint, current.RequestFingerprint);
        if (key == "Cardinal Points Data")
        {
            Assert.NotNull(pinned.ReferenceScene?.TwoDimensional);
            Assert.Equal(pinned.SourceDocument.TotalTrack, pinned.ReferenceScene!.Summary.TotalTrack);
            Assert.Equal(expected.ReferenceScene!.TwoDimensional!.Surfaces.Select(surface => surface.Points),
                pinned.ReferenceScene.TwoDimensional!.Surfaces.Select(surface => surface.Points));
        }
    }

    [Fact]
    public async Task AllSpotConfigurationsOverlayUnmodifiedCorePointsWithIndependentLegendsAndExports()
    {
        using var app = WorkbenchApplication.Create();
        await app.Documents.OpenAsync(ConfigurationMatrixAnalysisTests.ZoomPath());
        var result = await Run(app, "Spot Diagram", "All");
        Assert.Equal(AnalysisConfigurationDisplay.Overlay, result.View.ConfigurationScope!.Display);
        Assert.Equal(new[] { 1, 2, 3 }, result.View.ConfigurationScope.ConfigurationNumbers);
        Assert.Equal(3, result.View.PlotPanes.Count);
        var imported = new ZemaxZmxImporter().ImportConfigurationSet(File.ReadAllText(ConfigurationMatrixAnalysisTests.ZoomPath()));
        for (var configuration = 0; configuration < 3; configuration++)
        {
            var expected = new SpotDiagramAnalysis(imported.Configurations[configuration], new SpotDiagramSettings(RayDensity: 2, Reference: "主光线")).GenerateData();
            for (var field = 0; field < 3; field++)
            {
                var actual = result.View.PlotPanes[field].Series.Skip(configuration * 3).Take(3).ToArray();
                Assert.Equal(expected.PlotPanes![field].Series.Select(series => series.Points.Select(point => (point.X, point.Y))),
                    actual.Select(series => series.Points.Select(point => (point.X, point.Y))));
                Assert.All(actual, series => Assert.StartsWith("wavelength:", series.LegendKey));
                Assert.All(actual, series => Assert.Equal(configuration + 1, series.ConfigurationNumber));
            }
        }
        Assert.Equal(9, result.View.PlotPanes[0].Series.Select(series => (series.ConfigurationNumber, series.LegendKey)).Distinct().Count());
        foreach (var pane in result.View.PlotPanes)
        {
            Assert.True(pane.PlotOptions.XMinimum <= pane.Series.SelectMany(series => series.Points).Min(point => point.X));
            Assert.True(pane.PlotOptions.XMaximum >= pane.Series.SelectMany(series => series.Points).Max(point => point.X));
        }
        Assert.Contains("结构 3", result.View.ReportText);
        Assert.Contains("结构 3", OpticalSystemDesign.Application.Formatting.AnalysisCsvFormatter.Format(result.View));
        Assert.EndsWith(",configuration", OpticalSystemDesign.Application.Formatting.AnalysisCsvFormatter.Format(result.View).Split('\n')[0].TrimEnd('\r'));
    }

    [Theory]
    [InlineData("Ray Fan")]
    [InlineData("MTF")]
    [InlineData("RMS vs Field")]
    [InlineData("Field Curvature and Distortion")]
    public async Task CurveOverlaysRetainEachConfigurationResultWithoutAveragingOrResampling(string key)
    {
        using var app = WorkbenchApplication.Create();
        await app.Documents.OpenAsync(ConfigurationMatrixAnalysisTests.ZoomPath());
        var overlay = await Run(app, key, "All");
        for (var index = 0; index < 3; index++)
        {
            var single = await Run(app, key, (index + 1).ToString());
            Assert.Equal(single.View.PlotPanes.Count, overlay.View.PlotPanes.Count);
            if (single.View.PlotPanes.Count == 0)
                Assert.Equal(single.View.Series.Select(series => series.Points),
                    overlay.View.Series.Skip(index * single.View.Series.Count).Take(single.View.Series.Count).Select(series => series.Points));
            else
                foreach (var (left, right) in single.View.PlotPanes.Zip(overlay.View.PlotPanes))
                    Assert.Equal(left.Series.Select(series => series.Points),
                        right.Series.Skip(index * left.Series.Count).Take(left.Series.Count).Select(series => series.Points));
        }
    }

    [Fact]
    public async Task IncompatibleAxesCannotBeOverlaidAndInvalidFieldNumbersCannotBeClamped()
    {
        using var app = WorkbenchApplication.Create();
        await app.Documents.OpenAsync(ConfigurationMatrixAnalysisTests.ZoomPath());
        var invalid = new Dictionary<string, string> { ["Configuration"] = "2", ["FieldNumber"] = "4" };
        await Assert.ThrowsAsync<ArgumentException>(() => app.Analyses.RunAsync(new(Guid.NewGuid(), 1, "MTF", invalid)));
        app.MultiConfiguration.Activate(1);
        app.Prescription.UpdateSystemSettings(app.Prescription.GetSystemSettings() with { ImageSpaceAfocal = true });
        app.MultiConfiguration.Activate(0);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Run(app, "Spot Diagram", "All"));
        Assert.Contains("单位", error.Message);
    }

    [Theory]
    [InlineData("Current", 2)]
    [InlineData("3", 3)]
    public async Task MatrixSingleSelectionPreservesTheOriginalConfigurationNumber(string selection, int number)
    {
        using var app = WorkbenchApplication.Create();
        await app.Documents.OpenAsync(ConfigurationMatrixAnalysisTests.ZoomPath());
        app.MultiConfiguration.Activate(1);
        var result = await Run(app, "Configuration Matrix Spot Diagram", selection);
        Assert.Equal(1, result.View.PlotPaneColumns);
        Assert.Equal(3, result.View.PlotPanes.Count);
        Assert.All(result.View.PlotPanes, pane => Assert.Equal(number, pane.MatrixCell!.ConfigurationNumber));
        Assert.All(result.View.PlotPanes, pane => Assert.StartsWith($"结构 {number} ·", pane.Title));
    }

    [Fact]
    public async Task WorkerRejectsMissingConfigurationsAndCoherentSumsInsteadOfSilentlyUsingCurrent()
    {
        using var app = WorkbenchApplication.Create();
        await app.Documents.OpenAsync(ConfigurationMatrixAnalysisTests.ZoomPath());
        await Assert.ThrowsAsync<ArgumentException>(() => Run(app, "Spot Diagram", "4"));
        await Assert.ThrowsAsync<ArgumentException>(() => Run(app, "Huygens MTF", "All"));
        await Assert.ThrowsAsync<ArgumentException>(() => Run(app, "Huygens PSF", "All"));
        Assert.Equal("Current", app.Analyses.MergeSettings("Spot Diagram", new Dictionary<string, string> { ["Configuration"] = "4" })["Configuration"]);
    }

    private static Task<AnalysisResultDto> Run(WorkbenchApplication app, string key, string selection)
    {
        var settings = app.Analyses.MergeSettings(key, null);
        var parameters = app.Analyses.GetParameters(key).ToDictionary(parameter => parameter.Key);
        foreach (var name in new[] { "RayDensity", "NumberOfRays" })
            if (settings.ContainsKey(name) && key != "Relative Illumination") settings[name] = Math.Clamp(2, parameters[name].Minimum, parameters[name].Maximum)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var name in new[] { "FieldDensity", "NumRings" })
            if (settings.ContainsKey(name)) settings[name] = "2";
        if (settings.ContainsKey("Sampling") && key == "MTF") settings["Sampling"] = "32";
        settings["Configuration"] = selection;
        return app.Analyses.RunAsync(new AnalysisRequestDto(Guid.NewGuid(), 1, key, settings));
    }
}
