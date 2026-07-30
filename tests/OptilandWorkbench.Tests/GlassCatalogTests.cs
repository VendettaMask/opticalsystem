using System.Text.Json;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Materials;

namespace OptilandWorkbench.Tests;

public sealed class GlassCatalogTests
{
    [Fact]
    public void EmbeddedCatalogExposesManufacturerGlassLibrary()
    {
        var registry = new MaterialRegistry();

        Assert.Equal(1740, registry.CatalogGlassCount);
        Assert.Contains("SCHOTT", registry.GlassManufacturers);
        Assert.Contains("OHARA", registry.GlassManufacturers);
        Assert.Contains("HOYA", registry.GlassManufacturers);
        Assert.Contains("HIKARI", registry.GlassManufacturers);
        Assert.Contains("CDGM", registry.GlassManufacturers);
        Assert.Contains("SUMITA", registry.GlassManufacturers);
        Assert.Contains("N-BK7", registry.Names);
        Assert.Contains("SCHOTT:F2", registry.Names);
        Assert.Contains("CDGM:F2", registry.Names);
    }

    [Fact]
    public void BundledZemaxDatabaseResolvesHzlaf96WithoutImport()
    {
        var registry = new MaterialRegistry();
        var material = Assert.IsType<CatalogGlassMaterial>(
            registry.Resolve("CDGM-ZEMAX202309:H-ZLAF96"));

        Assert.Equal("CDGM-ZEMAX202309", material.Manufacturer);
        Assert.Equal("H-ZLAF96", material.CatalogName);
        Assert.Equal(2, material.ZemaxData!.DispersionFormulaNumber);
        Assert.Equal(2.0509, material.ZemaxData.ReferenceIndexD, precision: 7);
        Assert.Equal(2.0509, material.RefractiveIndex(587.5618), precision: 5);
    }

    [Fact]
    public void CatalogIndicesAndExtinctionMatchPythonOptiland058()
    {
        using var expected = JsonDocument.Parse(File.ReadAllText(
            FixturePath("optiland-0.5.8-glass-reference.json")));
        var registry = new MaterialRegistry();
        var root = expected.RootElement;

        Assert.Equal("0.5.8", root.GetProperty("optiland_version").GetString());
        foreach (var entry in root.GetProperty("entries").EnumerateArray())
        {
            var manufacturer = entry.GetProperty("manufacturer").GetString()!;
            var name = entry.GetProperty("name").GetString()!;
            var material = Assert.IsType<CatalogGlassMaterial>(registry.Resolve($"{manufacturer}:{name}"));

            Assert.Equal(manufacturer, material.Manufacturer);
            Assert.Equal(name, material.CatalogName);
            foreach (var sample in entry.GetProperty("samples").EnumerateArray())
            {
                var wavelengthNanometers = sample.GetProperty("wavelength_um").GetDouble() * 1000.0;
                Assert.Equal(
                    sample.GetProperty("n").GetDouble(),
                    material.RefractiveIndex(wavelengthNanometers),
                    precision: 12);
                Assert.Equal(
                    sample.GetProperty("k").GetDouble(),
                    material.ExtinctionCoefficient(wavelengthNanometers),
                    precision: 13);
            }
        }
    }

    [Fact]
    public void ZemaxGcatDisambiguatesSameNamedManufacturerGlass()
    {
        var cdgm = ImportSingleGlass("CDGM", "F2");
        var schott = ImportSingleGlass("SCHOTT", "F2");
        var registry = new MaterialRegistry();
        var directCdgm = Assert.IsType<CatalogGlassMaterial>(registry.Resolve("F2", new[] { "CDGM" }));
        var directSchott = Assert.IsType<CatalogGlassMaterial>(registry.Resolve("F2", new[] { "SCHOTT" }));

        Assert.Equal("CDGM", cdgm.Manufacturer);
        Assert.Equal("SCHOTT", schott.Manufacturer);
        Assert.Equal("CDGM", directCdgm.Manufacturer);
        Assert.Equal("SCHOTT", directSchott.Manufacturer);
        Assert.NotEqual(
            cdgm.RefractiveIndex(587.5618),
            schott.RefractiveIndex(587.5618));
    }

    [Fact]
    public void SystemGlassCatalogPriorityControlsResolutionAndSurvivesSnapshot()
    {
        var optic = Optic.CreateDemo();
        optic.Materials.SetPreferredGlassCatalogs(new[] { "CDGM", "SCHOTT" });

        var cdgm = Assert.IsType<CatalogGlassMaterial>(optic.Materials.Resolve("F2"));
        var restored = Optic.FromSnapshot(optic.ToSnapshot());

        Assert.Equal("CDGM", cdgm.Manufacturer);
        Assert.Equal(new[] { "CDGM", "SCHOTT" }, restored.GlassCatalogs);
        Assert.Equal(
            "CDGM",
            Assert.IsType<CatalogGlassMaterial>(restored.Materials.Resolve("F2")).Manufacturer);

        restored.Materials.SetPreferredGlassCatalogs(new[] { "SCHOTT", "CDGM" });
        Assert.Equal(
            "SCHOTT",
            Assert.IsType<CatalogGlassMaterial>(restored.Materials.Resolve("F2")).Manufacturer);
    }

    [Fact]
    public void ZemaxGcatRoundTripPreservesTheConfiguredCatalogOrder()
    {
        var imported = OpticalFormatCatalog.Import(ZemaxSource("CDGM", "F2"), ".zmx");

        Assert.Equal(new[] { "CDGM" }, imported.GlassCatalogs);

        var exported = OpticalFormatCatalog.Export(imported, ".zmx");
        var restored = OpticalFormatCatalog.Import(exported, ".zmx");

        Assert.Contains("GCAT CDGM", exported, StringComparison.Ordinal);
        Assert.Equal(new[] { "CDGM" }, restored.GlassCatalogs);
    }

    [Fact]
    public void UnqualifiedLegacyGlassUsesCatalogPriorityAndUnknownNamesDoNotFallback()
    {
        var registry = new MaterialRegistry();

        var legacyF2 = Assert.IsType<CatalogGlassMaterial>(registry.Resolve("F2"));
        Assert.Equal("SCHOTT", legacyF2.Manufacturer);
        Assert.Equal("F2", legacyF2.CatalogName);
        Assert.Throws<KeyNotFoundException>(() => registry.Resolve("NOT-A-REAL-GLASS"));
        var exception = Assert.Throws<KeyNotFoundException>(() =>
            OpticalFormatCatalog.Import(ZemaxSource("SCHOTT", "NOT-A-REAL-GLASS"), ".zmx"));
        Assert.Contains("did not provide valid nd/Vd", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ZemaxExportPreservesCatalogIdentityForRoundTrip()
    {
        var optic = Optic.CreateDemo();

        var text = OpticalFormatCatalog.Export(optic, ".zmx");
        var restored = OpticalFormatCatalog.Import(text, ".zmx");

        Assert.Contains("GCAT SCHOTT", text, StringComparison.Ordinal);
        Assert.DoesNotContain("GLAS SCHOTT:", text, StringComparison.Ordinal);
        var catalogMaterials = restored.SurfaceGroup.Items
            .Select(surface => surface.MaterialAfter)
            .OfType<CatalogGlassMaterial>()
            .ToArray();
        Assert.NotEmpty(catalogMaterials);
        Assert.All(catalogMaterials, material => Assert.Equal("SCHOTT", material.Manufacturer));
    }

    [Fact]
    public async Task ZemaxAgfRoundTripsAllSupportedRecordsIntoOptilandCatalog()
    {
        var document = ZemaxAgfCatalogReader.Import(ZemaxAgfSource, "CODEXAGF.AGF");
        var glass = Assert.Single(document.Glasses);

        Assert.Equal("CODEXAGF", document.CatalogName);
        Assert.Equal("Test catalog", document.Comment);
        Assert.Equal("H-ZLAF96", glass.Name);
        Assert.Equal(1, glass.DispersionFormulaNumber);
        Assert.Equal("900000", glass.MilNumber);
        Assert.Equal(1.9, glass.ReferenceIndexD, precision: 12);
        Assert.Equal(30, glass.ReferenceAbbeNumber, precision: 12);
        Assert.True(glass.ExcludeSubstitution);
        Assert.Equal(2, glass.Status);
        Assert.Equal(3, glass.MeltFrequency);
        Assert.Equal("Imported test glass", glass.Comment);
        Assert.Equal(8.3, glass.ThermalExpansionLow);
        Assert.Equal(0.2, glass.ThermalExpansionHigh);
        Assert.Equal(3.56, glass.Density);
        Assert.Equal(0.0001, glass.RelativePartialDispersionDeviation);
        Assert.True(glass.IgnoreThermalExpansion);
        Assert.Equal(10, glass.DispersionCoefficients.Count);
        Assert.Equal(7, glass.ThermalCoefficients.Count);
        Assert.Equal(5, glass.MechanicalData.Count);
        Assert.Equal(6, glass.OtherData.Count);
        Assert.Equal(0.365, glass.MinimumWavelengthMicrometers, precision: 12);
        Assert.Equal(2.5, glass.MaximumWavelengthMicrometers, precision: 12);
        Assert.Single(glass.InternalTransmissions);
        Assert.Single(glass.StressData);
        Assert.Contains("ZZ future-record", glass.UnrecognizedRecords);

        var path = Path.Combine(Path.GetTempPath(), $"optiland-catalog-{Guid.NewGuid():N}.ogcat");
        try
        {
            await OptilandGlassCatalogStore.SaveAsync(document, path);
            var restored = await OptilandGlassCatalogStore.LoadAsync(path);
            var restoredGlass = Assert.Single(restored.Glasses);

            Assert.Equal(document.CatalogName, restored.CatalogName);
            Assert.Equal(glass.Name, restoredGlass.Name);
            Assert.Equal(glass.DispersionCoefficients, restoredGlass.DispersionCoefficients);
            Assert.Equal(glass.ThermalCoefficients, restoredGlass.ThermalCoefficients);
            Assert.Equal(glass.InternalTransmissions, restoredGlass.InternalTransmissions);
            Assert.Equal(glass.StressData, restoredGlass.StressData);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ImportedZemaxGlassUsesAgfFormulaAndResolvesDuringZmxImport()
    {
        var document = ZemaxAgfCatalogReader.Import(ZemaxAgfSource, "CODEXAGF.AGF");
        ExternalGlassCatalogDatabase.Register(document);
        var registry = new MaterialRegistry();
        var material = Assert.IsType<CatalogGlassMaterial>(
            registry.Resolve("H-ZLAF96", new[] { "CODEXAGF" }));
        const double wavelengthMicrometers = 0.5875618;
        var wavelengthSquared = wavelengthMicrometers * wavelengthMicrometers;
        var coefficients = document.Glasses[0].DispersionCoefficients;
        var expectedIndex = Math.Sqrt(
            coefficients[0] +
            coefficients[1] * wavelengthSquared +
            coefficients[2] / wavelengthSquared +
            coefficients[3] / Math.Pow(wavelengthMicrometers, 4) +
            coefficients[4] / Math.Pow(wavelengthMicrometers, 6) +
            coefficients[5] / Math.Pow(wavelengthMicrometers, 8));

        Assert.Equal("CODEXAGF", material.Manufacturer);
        Assert.Equal("zemax formula 1", material.Formula);
        Assert.Equal(expectedIndex, material.RefractiveIndex(wavelengthMicrometers * 1000), precision: 12);

        var optic = OpticalFormatCatalog.Import(ZemaxSource("CODEXAGF", "H-ZLAF96"), ".zmx");
        var imported = Assert.IsType<CatalogGlassMaterial>(optic.SurfaceGroup.Items[0].MaterialAfter);
        Assert.Equal("CODEXAGF", imported.Manufacturer);
        Assert.Equal("H-ZLAF96", imported.CatalogName);
    }

    [Fact]
    public async Task ZemaxAgfPreservesLegacyMissingValuesAndUsesLastDuplicate()
    {
        var document = ZemaxAgfCatalogReader.Import("""
            NM DUPLICATE 1 1 1.600000 40.000000 0 0 0
            CD 2.5 0.01 0.001 0.00001 0.0000001 0.000000001
            MD _ _ _ _ _
            LD 0.4 1.0
            IT 1.0
            BD 0.587 3.5
            NM DUPLICATE 1 2 1.700000 30.000000 0 0 0
            CD 2.7 0.02 0.002 0.00002 0.0000002 0.000000002
            LD 0.4 1.0
            """, "REALCOMPAT.AGF");
        var path = Path.Combine(Path.GetTempPath(), $"optiland-legacy-{Guid.NewGuid():N}.ogcat");

        try
        {
            await OptilandGlassCatalogStore.SaveAsync(document, path);
            var restored = await OptilandGlassCatalogStore.LoadAsync(path);
            var first = restored.Glasses[0];

            Assert.Equal(2, restored.Glasses.Count);
            Assert.All(first.MechanicalData, value => Assert.True(double.IsNaN(value)));
            Assert.True(double.IsNaN(Assert.Single(first.InternalTransmissions).Transmission));
            var stress = Assert.Single(first.StressData);
            Assert.True(double.IsNaN(stress.NegativeK11));
            Assert.True(double.IsNaN(stress.NegativeK12));

            ExternalGlassCatalogDatabase.Register(restored);
            var material = Assert.IsType<CatalogGlassMaterial>(
                new MaterialRegistry().Resolve("REALCOMPAT:DUPLICATE"));
            Assert.Equal(1.7, material.ZemaxData!.ReferenceIndexD, precision: 12);
            Assert.Equal(2.7, material.Coefficients[0], precision: 12);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static CatalogGlassMaterial ImportSingleGlass(string catalog, string name)
    {
        var optic = OpticalFormatCatalog.Import(ZemaxSource(catalog, name), ".zmx");
        return Assert.IsType<CatalogGlassMaterial>(optic.SurfaceGroup.Items[0].MaterialAfter);
    }

    private static string ZemaxSource(string catalog, string name) => $"""
        MODE SEQ
        GCAT {catalog}
        ENPD 10
        SURF 0
          CURV 0
          DISZ 0
          GLAS {name}
        SURF 1
          CURV 0
          DISZ 0
          GLAS AIR
        """;

    private const string ZemaxAgfSource = """
        ! AGF parser test
        CC Test catalog
        NM H-ZLAF96 1 900000 1.900000 30.000000 1 2 3
        GC Imported test glass
        ED 8.3 0.2 3.56 0.0001 1
        CD 3.61 -0.02 0.03 0.001 -0.0001 0.00001 0 0 0 0
        TD 1e-6 2e-8 3e-10 4e-7 5e-9 0.2 20
        MD 100 0.25 600 500 1.2
        OD 4 1 2 3 4 5
        LD 0.365 2.5
        IT 0.5 0.98 10
        BD 0.587 1 2 3
        ZZ future-record
        """;

    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
}
