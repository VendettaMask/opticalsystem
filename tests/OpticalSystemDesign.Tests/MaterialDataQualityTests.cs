using OpticalSystemDesign.App.Panels;
using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.Tests;

public sealed class MaterialDataQualityTests
{
    [Theory]
    [InlineData(900, 2000, false)]
    [InlineData(550, 610, true)]
    public void MissingReferencesNeverExposeOutOfRangeFdcExtrapolation(double minimum, double maximum, bool validD)
    {
        var material = new CatalogGlassMaterial("narrow", "fixture", "zemax formula 1", minimum, maximum, [2.25, .01, .02, 0, 0, 0]);
        var dto = MaterialCatalogService.ToGlassMaterialDto(material);
        Assert.Equal(validD, double.IsFinite(dto.RefractiveIndexD));
        Assert.True(double.IsNaN(dto.AbbeNumber));
        Assert.Equal(GlassConstantsCalculationStatus.WavelengthOutOfRange, dto.CalculatedConstantsStatus);
        Assert.Contains("超出", MaterialLibraryPanel.DataQualityText(dto));
    }

    [Theory]
    [InlineData("zemax formula 1")]
    [InlineData("unsupported")]
    public void InvalidModelsDoNotBreakCatalogBrowsing(string formula)
    {
        var material = new CatalogGlassMaterial("invalid", "fixture", formula, 400, 700, [2.25],
            zemaxData: new() { ReferenceIndexD = double.PositiveInfinity, ReferenceAbbeNumber = double.PositiveInfinity });
        var dto = MaterialCatalogService.ToGlassMaterialDto(material);
        Assert.True(double.IsNaN(dto.RefractiveIndexD)); Assert.True(double.IsNaN(dto.AbbeNumber));
        Assert.Null(dto.CatalogReferenceIndexD); Assert.Null(dto.CatalogReferenceAbbeNumber);
        Assert.NotEqual(GlassConstantsCalculationStatus.Available, dto.CalculatedConstantsStatus);
    }

    [Fact]
    public void TabulatedCoverageAndUnroundedDDefineFallbackConstants()
    {
        var table = new CatalogGlassMaterial("table", "fixture", "tabulated n", 400, 700,
            refractiveIndexWavelengthsNanometers: [550, 600], refractiveIndices: [1.6, 1.5]);
        var narrow = MaterialCatalogService.ToGlassMaterialDto(table);
        Assert.True(double.IsFinite(narrow.RefractiveIndexD)); Assert.True(double.IsNaN(narrow.AbbeNumber));
        using var app = WorkbenchApplication.Create();
        var basis = app.Materials.FindGlass("APEL:APL5014CL")!;
        var analytic = new CatalogGlassMaterial("round", "fixture", basis.Formula, 400, 700, basis.DispersionCoefficients,
            zemaxData: new() { ReferenceIndexD = 1.54 });
        var dto = MaterialCatalogService.ToGlassMaterialDto(analytic);
        var computed = CatalogGlassConstants.Calculate(analytic);
        Assert.Equal(1.54, dto.RefractiveIndexD);
        Assert.Equal(computed.AbbeNumber, dto.AbbeNumber);
    }

    [Fact]
    public void ThermalReviewFlagsSourceValuesWithoutChangingThemOrTheProject()
    {
        using var app = WorkbenchApplication.Create();
        var before = app.Documents.GetRecoveryState(); var revision = app.Events.Revision;
        foreach (var name in new[] { "OHARA-A:TPH55", "HIKARI:7054", "ANGSTROMLINK:AL-6265-(OKP-850)" })
        {
            var glass = app.Materials.GetGlasses().Single(g => $"{g.Manufacturer}:{g.Name}" == name);
            Assert.True(glass.RequiresThermalDataReview, name);
            Assert.True(Math.Abs(glass.ReferenceThermalIndexDerivative!.Value) >= .01);
            Assert.Contains("未缩放或修正", MaterialLibraryPanel.DataQualityText(glass));
            Assert.Contains("热数据需核对", MaterialLibraryPanel.BuildGlassReport(glass));
            Assert.Equal(glass.ThermalCoefficients, app.Materials.GetGlasses().Single(g => $"{g.Manufacturer}:{g.Name}" == name).ThermalCoefficients);
        }
        Assert.False(app.Materials.FindGlass("SCHOTT:N-BK7")!.RequiresThermalDataReview);
        Assert.Equal(before, app.Documents.GetRecoveryState()); Assert.Equal(revision, app.Events.Revision);
    }

    [Theory]
    [InlineData("HIKARI", "7054")]
    [InlineData("OHARA-A", "TPH55")]
    public void BrowserRecalculationUsesTheDisplayedCatalogWhileProjectResolutionStaysUnchanged(string manufacturer, string name)
    {
        using var app = WorkbenchApplication.Create();
        var before = app.Documents.GetRecoveryState(); var revision = app.Events.Revision;
        var qualified = manufacturer + ":" + name;
        var project = app.Materials.FindGlass(qualified)!;
        var glass = app.Materials.GetGlasses().Single(g => g.Manufacturer == manufacturer && g.Name == name);
        var source = new CatalogGlassMaterial(name, manufacturer, glass.Formula,
            glass.MinimumWavelengthMicrometers * 1000, glass.MaximumWavelengthMicrometers * 1000, glass.DispersionCoefficients);
        var expected = CatalogGlassConstants.Calculate(source);
        var result = app.Materials.CalculateGlassConstants(qualified, useCatalogDefinition: true)!;
        Assert.Equal(expected.IndexD, result.RefractiveIndexD);
        Assert.Equal(expected.AbbeNumber, result.AbbeNumber);
        Assert.Equal(project.DispersionCoefficients, app.Materials.FindGlass(qualified)!.DispersionCoefficients);
        Assert.Equal(project.ThermalCoefficients, app.Materials.FindGlass(qualified)!.ThermalCoefficients);
        Assert.Equal(before, app.Documents.GetRecoveryState()); Assert.Equal(revision, app.Events.Revision);
    }
}
