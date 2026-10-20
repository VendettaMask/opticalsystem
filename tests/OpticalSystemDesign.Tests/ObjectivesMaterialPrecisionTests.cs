using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.Tests;

public sealed class ObjectivesMaterialPrecisionTests
{
    [Theory]
    [InlineData("517642")]
    [InlineData("648338")]
    public void MilProfilesPreserveCapturedSamplesAndIndependentHalfNanometerControls(string code)
    {
        var root = ProductDependencyArchitectureTests.RepositoryRoot();
        var folder = Path.Combine(root, "validation/zemax/2026-r1/objectives-2026-10-20/materials");
        var material = Assert.IsType<TabulatedMaterial>(new MaterialRegistry().Resolve(code));
        using var captured = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "materials.json")));
        var reference = captured.RootElement.GetProperty("materials").EnumerateArray().Single(item => item.GetProperty("name").GetString() == code);
        AssertSamples(reference, material, 0);
        using var heldOut = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "held-out-indices.json")));
        AssertSamples(heldOut.RootElement.EnumerateArray().Single(item => item.GetProperty("name").GetString() == code), material, 1e-10);
        Assert.Throws<ArgumentOutOfRangeException>(() => material.RefractiveIndex(349));
        Assert.Throws<ArgumentOutOfRangeException>(() => material.RefractiveIndex(1051));
        Assert.Contains("LDE.GetIndex", material.Provenance);
    }

    [Fact]
    public void ImportedMilDoubletPersistsMaterialSamplesAndProvenanceThroughSnapshot()
    {
        var root = ProductDependencyArchitectureTests.RepositoryRoot();
        var path = Path.Combine(root, "src/OpticalSystemDesign.App/Assets/SequentialExamples/Objectives/Doublet using MIL number glasses.zmx");
        var imported = OpticalFormatCatalog.Import(File.ReadAllText(path), ".zmx");
        var restored = Optic.FromSnapshot(imported.ToSnapshot());
        foreach (var number in new[] { 1, 2 })
        {
            var before = Assert.IsType<TabulatedMaterial>(imported.SurfaceGroup.Items[number].MaterialAfter);
            var after = Assert.IsType<TabulatedMaterial>(restored.SurfaceGroup.Items[number].MaterialAfter);
            Assert.Equal(before.Name, after.Name);
            Assert.Equal(before.Provenance, after.Provenance);
            Assert.Equal(before.WavelengthsNanometers, after.WavelengthsNanometers);
            Assert.Equal(before.Indices, after.Indices);
            foreach (var wave in imported.Wavelengths)
                Assert.Equal(before.RefractiveIndex(wave.Nanometers), after.RefractiveIndex(wave.Nanometers));
        }
        Assert.False(new MaterialRegistry().TryResolve("600500", null, out _));
    }

    private static void AssertSamples(JsonElement reference, TabulatedMaterial material, double absoluteLimit)
    {
        var wavelengths = reference.GetProperty("wavelengthsNanometers").Deserialize<double[]>()!;
        var indices = reference.GetProperty("indices").Deserialize<double[]>()!;
        Assert.Equal(wavelengths.Length, indices.Length);
        foreach (var (wavelength, expected) in wavelengths.Zip(indices))
            Assert.InRange(Math.Abs(material.RefractiveIndex(wavelength) - expected), 0, absoluteLimit);
    }
}
