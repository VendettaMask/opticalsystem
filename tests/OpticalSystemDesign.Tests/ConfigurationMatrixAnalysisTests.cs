using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.FileIO;
using CoreSeriesKind = OpticalSystemDesign.Core.Analysis.AnalysisSeriesKind;

namespace OpticalSystemDesign.Tests;

public sealed class ConfigurationMatrixAnalysisTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 2)]
    public void EveryCellMatchesAnIndependentSpotRunForThatConfiguration(int field, int wavelength)
    {
        var imported = ImportZoom();
        Assert.Equal(3, imported.Configurations.Count);
        var settings = new SpotDiagramSettings(RayDensity: 2, Reference: "主光线", FieldNumber: field, WavelengthNumber: wavelength);
        var matrix = new SpotDiagramVariantAnalysis(imported.ActiveOptic, SpotDiagramVariant.ConfigurationMatrix, settings, imported.Configurations).GenerateData();
        Assert.Equal(3, matrix.PlotPaneColumns);
        Assert.Equal(3, matrix.Values["ConfigurationCount"]);
        var expected = imported.Configurations.Select(optic => new SpotDiagramAnalysis(optic, settings).GenerateData()).ToArray();
        var fieldCount = field == 0 ? 3 : 1;
        Assert.Equal(fieldCount * 3, matrix.PlotPanes!.Count);
        Assert.Equal(wavelength == 0 ? 3 : 1, matrix.Values["WavelengthCount"]);
        for (var row = 0; row < fieldCount; row++)
            for (var column = 0; column < 3; column++)
            {
                var pane = matrix.PlotPanes[row * 3 + column];
                Assert.Equal(new AnalysisMatrixCell(column + 1, field == 0 ? row + 1 : field, expected[column].PlotPanes![row].Title), pane.MatrixCell);
                Assert.Equal(expected[column].PlotPanes![row].Series.Select(series => series.Points), pane.Series.Select(series => series.Points));
                Assert.Equal(expected[column].PlotPanes![row].Metrics, pane.Metrics);
                Assert.All(pane.Series, series => Assert.Equal(CoreSeriesKind.Scatter, series.Kind));
            }
        Assert.Single(matrix.PlotPanes.Select(pane => (pane.PlotOptions.XMinimum, pane.PlotOptions.XMaximum)).Distinct());
        Assert.False(matrix.PlotPanes[0].Series[0].Points.SequenceEqual(matrix.PlotPanes[1].Series[0].Points));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ApplicationWorkerKeepsAllConfigurationsWithoutChangingTheActiveOne(int activeIndex)
    {
        using var app = WorkbenchApplication.Create();
        await app.Documents.OpenAsync(ZoomPath());
        app.MultiConfiguration.Activate(activeIndex);
        var revision = app.Events.Revision;
        var surfaces = app.Prescription.GetSurfaces();
        var result = await Run(app);
        Assert.Equal(revision, result.SourceRevision);
        Assert.Equal(revision, app.Events.Revision);
        Assert.Equal(activeIndex, app.MultiConfiguration.GetNavigationState().ActiveIndex);
        Assert.Equal(surfaces.Select(surface => surface.Thickness), app.Prescription.GetSurfaces().Select(surface => surface.Thickness));
        Assert.Equal(3, result.View.PlotPaneColumns);
        Assert.Equal(9, result.View.PlotPanes.Count);
        var imported = ImportZoom();
        for (var column = 0; column < 3; column++)
        {
            var expected = new SpotDiagramAnalysis(imported.Configurations[column], new SpotDiagramSettings(RayDensity: 2, Reference: "主光线")).GenerateData();
            for (var row = 0; row < 3; row++)
            {
                var actual = result.View.PlotPanes[row * 3 + column];
                Assert.Equal(column + 1, actual.MatrixCell!.ConfigurationNumber);
                Assert.Equal(row + 1, actual.MatrixCell.FieldNumber);
                Assert.Equal(3, actual.Series.Count);
                foreach (var (expectedSeries, actualSeries) in expected.PlotPanes![row].Series.Zip(actual.Series))
                {
                    Assert.Equal(expectedSeries.Points.Count, actualSeries.Points.Count);
                    foreach (var (left, right) in expectedSeries.Points.Zip(actualSeries.Points))
                    {
                        Assert.Equal(left.X, right.X, 12);
                        Assert.Equal(left.Y, right.Y, 12);
                    }
                }
            }
        }
    }

    [Fact]
    public async Task EditingAnInactiveConfigurationChangesOnlyItsMatrixColumn()
    {
        using var app = WorkbenchApplication.Create();
        await app.Documents.OpenAsync(ZoomPath());
        var before = await Run(app);
        app.MultiConfiguration.SetThickness(1, 8, 5.25, app.Events.Revision);
        var after = await Run(app);
        Assert.True(after.SourceRevision > before.SourceRevision);
        Assert.Equal(0, app.MultiConfiguration.GetNavigationState().ActiveIndex);
        Assert.Equal(9, after.View.PlotPanes.Count);
        for (var column = 0; column < 3; column++)
        {
            var original = before.View.PlotPanes.Where((_, index) => index % 3 == column).SelectMany(pane => pane.Series).SelectMany(series => series.Points).ToArray();
            var changed = after.View.PlotPanes.Where((_, index) => index % 3 == column).SelectMany(pane => pane.Series).SelectMany(series => series.Points).ToArray();
            Assert.Equal(column != 1, original.SequenceEqual(changed));
        }
        var csv = OpticalSystemDesign.Application.Formatting.AnalysisCsvFormatter.Format(after.View);
        Assert.Contains("结构 2", csv);
        Assert.Contains("结构 3", csv);
    }

    [Fact]
    public void UnequalFieldCountsRetainEveryConfigurationColumnAndMarkMissingCells()
    {
        var imported = ImportZoom();
        imported.Configurations[1].Fields.RemoveAt(2);
        var matrix = new SpotDiagramVariantAnalysis(imported.ActiveOptic, SpotDiagramVariant.ConfigurationMatrix,
            new SpotDiagramSettings(RayDensity: 1), imported.Configurations).GenerateData();
        Assert.Equal(9, matrix.PlotPanes!.Count);
        var missing = matrix.PlotPanes[7];
        Assert.Equal(2, missing.MatrixCell!.ConfigurationNumber);
        Assert.Equal(3, missing.MatrixCell.FieldNumber);
        Assert.Empty(missing.Series);
        Assert.Contains("未定义", missing.Footer);
        Assert.NotEmpty(matrix.PlotPanes[8].Series);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ASelectionMissingFromAnotherConfigurationIsRejectedInsteadOfClamped(bool fieldSelection)
    {
        var imported = ImportZoom();
        if (fieldSelection) imported.Configurations[1].Fields.RemoveAt(2);
        else imported.Configurations[1].Wavelengths.RemoveAt(2);
        var settings = new SpotDiagramSettings(RayDensity: 1, FieldNumber: fieldSelection ? 3 : 0, WavelengthNumber: fieldSelection ? 0 : 3);
        var analysis = new SpotDiagramVariantAnalysis(imported.ActiveOptic, SpotDiagramVariant.ConfigurationMatrix, settings, imported.Configurations);
        var error = Assert.Throws<ArgumentException>(() => analysis.GenerateData());
        Assert.Contains("结构 2", error.Message);
        Assert.Contains(fieldSelection ? "视场 3" : "波长 3", error.Message);
    }

    internal static Task<AnalysisResultDto> Run(WorkbenchApplication app) => app.Analyses.RunAsync(new AnalysisRequestDto(
        Guid.NewGuid(), 1, "Configuration Matrix Spot Diagram", app.Analyses.MergeSettings("Configuration Matrix Spot Diagram",
            new Dictionary<string, string> { ["RayDensity"] = "2" })));

    private static ZemaxZmxImportResult ImportZoom() => new ZemaxZmxImporter().ImportConfigurationSet(File.ReadAllText(ZoomPath()));

    internal static string ZoomPath()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "OpticalSystemDesign.slnx"))) root = root.Parent;
        return Path.Combine(root!.FullName, "src/OpticalSystemDesign.App/Assets/SequentialExamples/Zoom systems/Zoom lens.zmx");
    }
}
