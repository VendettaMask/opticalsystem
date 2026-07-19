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
    public void UnknownAndAmbiguousGlassNamesNeverBecomeConstantIndexFallbacks()
    {
        var registry = new MaterialRegistry();

        Assert.Throws<InvalidDataException>(() => registry.Resolve("F2"));
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

    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
}
