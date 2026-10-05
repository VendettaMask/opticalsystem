using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;
using OptilandWorkbench.App.Panels;

namespace OptilandWorkbench.Tests;

public sealed class MaterialAnalysisTests
{
    [Fact]
    public void GlassLookupUsesQualifiedNamesAndCurrentProjectCatalogOrder()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var schott = Assert.IsType<GlassMaterialDto>(application.Materials.FindGlass("schott:f2"));
        Assert.Equal("SCHOTT", schott.Manufacturer);
        Assert.Equal("F2", schott.Name);
        Assert.InRange(schott.RefractiveIndexD, 1.62, 1.63);
        Assert.InRange(schott.AbbeNumber, 36, 37);

        foreach (var manufacturer in new[] { "CDGM", "SCHOTT" })
        {
            application.Prescription.UpdateGlassCatalogs(new[] { manufacturer });
            var unqualified = Assert.IsType<GlassMaterialDto>(application.Materials.FindGlass("F2"));
            Assert.Equal(manufacturer, unqualified.Manufacturer);
            Assert.Equal(schott.RefractiveIndexD, application.Materials.FindGlass("SCHOTT:F2")!.RefractiveIndexD);
            Assert.Equal(schott.AbbeNumber, application.Materials.FindGlass("SCHOTT:F2")!.AbbeNumber);
        }
    }

    [Fact]
    public void GlassLookupDoesNotSubstituteAnotherCatalogWhenResolutionFails()
    {
        using var application = WorkbenchApplication.Create("cooke");
        foreach (var name in new[] { "", "Air", "MISSING-CATALOG:F2", "SCHOTT:NO-SUCH-GLASS" })
            Assert.Null(application.Materials.FindGlass(name));
    }

    [Theory]
    [InlineData(MaterialAnalysisKind.GlassMap)]
    [InlineData(MaterialAnalysisKind.AthermalGlassMap)]
    public void CatalogMapsExposeFiniteGlassPoints(MaterialAnalysisKind kind)
    {
        using var application = WorkbenchApplication.Create("cooke");

        var view = application.Materials.Analyze(new MaterialAnalysisRequestDto(kind));
        var points = view.Series.SelectMany(series => series.Points).ToArray();

        Assert.NotEmpty(points);
        Assert.All(points, point =>
        {
            Assert.True(double.IsFinite(point.X));
            Assert.True(double.IsFinite(point.Y));
        });
        Assert.Contains(view.Rows, row => row.Metric == "有效绘图点");
    }

    [Fact]
    public void InternalTransmissionNormalizesCatalogDataToRequestedThickness()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var glass = application.Materials.GetGlasses()
            .First(item => item.InternalTransmissionCount > 1);

        var thin = application.Materials.Analyze(new MaterialAnalysisRequestDto(
            MaterialAnalysisKind.InternalTransmission,
            glass.Manufacturer,
            $"{glass.Manufacturer}:{glass.Name}",
            ThicknessMillimeters: 1));
        var thick = application.Materials.Analyze(new MaterialAnalysisRequestDto(
            MaterialAnalysisKind.InternalTransmission,
            glass.Manufacturer,
            $"{glass.Manufacturer}:{glass.Name}",
            ThicknessMillimeters: 10));

        var thinPoints = Assert.Single(thin.Series).Points;
        var thickPoints = Assert.Single(thick.Series).Points;
        Assert.Equal(thinPoints.Count, thickPoints.Count);
        Assert.NotEmpty(thinPoints);
        Assert.All(thinPoints.Zip(thickPoints), pair => Assert.True(pair.Second.Y <= pair.First.Y + 1e-12));
    }

    [Fact]
    public void DispersionCurveUsesSelectedGlassAndValidCatalogRange()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var glass = application.Materials.GetGlasses()
            .First(item => item.Manufacturer.Equals("SCHOTT", StringComparison.OrdinalIgnoreCase)
                && item.Name.Equals("N-BK7", StringComparison.OrdinalIgnoreCase));

        var view = application.Materials.Analyze(new MaterialAnalysisRequestDto(
            MaterialAnalysisKind.DispersionDiagram,
            glass.Manufacturer,
            $"{glass.Manufacturer}:{glass.Name}",
            SampleCount: 81));

        var series = Assert.Single(view.Series);
        Assert.Equal(81, series.Points.Count);
        Assert.All(series.Points, point =>
        {
            Assert.InRange(point.X, glass.MinimumWavelengthMicrometers, glass.MaximumWavelengthMicrometers);
            Assert.True(point.Y > 1);
        });
        Assert.All(series.Points, point => Assert.InRange(point.X, 0.4, 0.8));
        Assert.True(series.Points.First().Y > series.Points.Last().Y);
    }

    [Fact]
    public void DispersionVsWavelengthPlotsIndexDerivative()
    {
        using var application = WorkbenchApplication.Create("cooke");
        var glass = application.Materials.GetGlasses()
            .First(item => item.Manufacturer.Equals("SCHOTT", StringComparison.OrdinalIgnoreCase)
                && item.Name.Equals("N-BK7", StringComparison.OrdinalIgnoreCase));

        var view = application.Materials.Analyze(new MaterialAnalysisRequestDto(
            MaterialAnalysisKind.DispersionVsWavelength,
            glass.Manufacturer,
            $"{glass.Manufacturer}:{glass.Name}",
            SampleCount: 81));

        var series = Assert.Single(view.Series);
        Assert.Equal("色散 dn/dλ (μm⁻¹)", series.YAxisLabel);
        Assert.Equal(81, series.Points.Count);
        Assert.All(series.Points, point => Assert.True(point.Y < 0));
    }

    [Fact]
    public void GlassMapUsesConventionalReversedAbbeAxisAndLabels()
    {
        using var application = WorkbenchApplication.Create("cooke");

        var view = application.Materials.Analyze(new MaterialAnalysisRequestDto(
            MaterialAnalysisKind.GlassMap));

        Assert.True(view.PlotOptions.ReverseX);
        Assert.True(view.PlotOptions.ShowPointLabels);
        Assert.Equal(20, view.PlotOptions.XMinimum);
        Assert.Equal(70, view.PlotOptions.XMaximum);
        Assert.Contains(view.Series.SelectMany(series => series.Points), point =>
            !string.IsNullOrWhiteSpace(point.Label));
    }

    [Fact]
    public void MaterialAnalysisPanelTitlesCoverAllSupportedCommands()
    {
        Assert.Equal("色散图", MaterialAnalysisPanel.Title(MaterialAnalysisKind.DispersionDiagram));
        Assert.Equal("玻璃图", MaterialAnalysisPanel.Title(MaterialAnalysisKind.GlassMap));
        Assert.Equal("无热化玻璃图", MaterialAnalysisPanel.Title(MaterialAnalysisKind.AthermalGlassMap));
        Assert.Equal("内部透过率 vs. 波长", MaterialAnalysisPanel.Title(MaterialAnalysisKind.InternalTransmission));
        Assert.Equal("色散 vs. 波长", MaterialAnalysisPanel.Title(MaterialAnalysisKind.DispersionVsWavelength));
    }
}
