using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Tests;

public sealed class CdgmOfficialCatalogTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };

    [Fact]
    public async Task All352MaterialsKeepEveryRecordFromTheOfficialSeptember2026Download()
    {
        var source = await ZemaxAgfCatalogReader.ImportFileAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures/CDGM-ZEMAX202609.AGF"));
        Assert.Equal("Updated Sep. 2026", source.Comment);
        Assert.Equal(352, source.Glasses.Count);
        var registry = new MaterialRegistry();
        var names = registry.Names.Where(name => name.StartsWith("CDGM:", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.Equal(352, names.Length);
        foreach (var original in source.Glasses)
        {
            var actual = Assert.IsType<CatalogGlassMaterial>(registry.Resolve($"CDGM:{original.Name}"));
            Assert.Equal("CDGM", actual.Manufacturer);
            Assert.Equal(JsonSerializer.Serialize(original, JsonOptions), JsonSerializer.Serialize(actual.ZemaxData, JsonOptions));
            Assert.Equal(original.DispersionCoefficients, actual.Coefficients);
            var direct = new CatalogGlassMaterial(original.Name, "CDGM", $"zemax formula {original.DispersionFormulaNumber}",
                original.MinimumWavelengthMicrometers * 1000, original.MaximumWavelengthMicrometers * 1000,
                original.DispersionCoefficients);
            var wavelength = (original.MinimumWavelengthMicrometers + original.MaximumWavelengthMicrometers) * 500;
            Assert.Equal(direct.RefractiveIndex(wavelength), actual.RefractiveIndex(wavelength));
            Assert.True(double.IsFinite(actual.RefractiveIndex(wavelength)));
        }
    }

    [Theory]
    [InlineData("CDGM")]
    [InlineData("CDGM-H")]
    [InlineData("CDGM-ZEMAX202309")]
    [InlineData("CDGM20113-P")]
    public void OldCatalogKeysResolveTheSingleCurrentDefinition(string catalog)
    {
        var registry = new MaterialRegistry();
        var glass = Assert.IsType<CatalogGlassMaterial>(registry.Resolve($"{catalog}:H-K9L"));
        var current = Assert.IsType<CatalogGlassMaterial>(registry.Resolve("CDGM:H-K9L"));
        Assert.Equal("CDGM", glass.Manufacturer);
        Assert.Equal(current.Coefficients, glass.Coefficients);
        Assert.Equal(current.RefractiveIndex(587.5618), glass.RefractiveIndex(587.5618));
        registry.SetPreferredGlassCatalogs([catalog]);
        Assert.Equal(current.Coefficients, Assert.IsType<CatalogGlassMaterial>(registry.Resolve("H-K9L")).Coefficients);
        registry.SetPreferredGlassCatalogs(["SCHOTT.AGF", catalog + ".AGF"]);
        Assert.Equal("SCHOTT", Assert.IsType<CatalogGlassMaterial>(registry.Resolve("F2")).Manufacturer);
        registry.SetPreferredGlassCatalogs([catalog + ".AGF", "SCHOTT.AGF"]);
        Assert.Equal("CDGM", Assert.IsType<CatalogGlassMaterial>(registry.Resolve("F2")).Manufacturer);
        Assert.DoesNotContain("CDGM-H", registry.GlassManufacturers);
        Assert.DoesNotContain("CDGM-ZEMAX202309", registry.GlassManufacturers);
        Assert.DoesNotContain("CDGM20113-P", registry.GlassManufacturers);
    }

    [Fact]
    public void TypesAbsentFromTheOfficialReleaseDoNotReappearFromTheAuxiliaryCatalog()
    {
        var registry = new MaterialRegistry();
        foreach (var name in new[] { "CDGM:D-LAF82L", "CDGM-H:H-K9L-TEST", "CDGM20113-P:H-ZLAF56A" })
            Assert.False(registry.TryResolve(name, ["CDGM"], out _));
    }

    [Fact]
    public void ExplicitlySavedMaterialValuesSurviveTheCurrentCatalogUpdate()
    {
        // A deliberately different saved definition exercises snapshot identity;
        // it is a test fixture, not manufacturer optical data.
        var saved = new CatalogGlassMaterial("H-K9L", "CDGM-H", "zemax formula 1", 400, 1000,
            [2.25, 0, 0, 0, 0, 0]);
        var registry = new MaterialRegistry();
        var snapshot = ComponentSnapshotFactory.FromMaterial(saved);
        var restored = Assert.IsType<CatalogGlassMaterial>(ComponentSnapshotFactory.ToMaterial(snapshot, "H-K9L", registry));
        Assert.Equal("CDGM-H", restored.Manufacturer);
        Assert.Equal(saved.Coefficients, restored.Coefficients);
        foreach (var wavelength in new[] { 450.0, 587.5618, 900 })
            Assert.Equal(saved.RefractiveIndex(wavelength), restored.RefractiveIndex(wavelength));
        Assert.NotEqual(saved.RefractiveIndex(587.5618), registry.Resolve("CDGM:H-K9L").RefractiveIndex(587.5618));
    }
}
