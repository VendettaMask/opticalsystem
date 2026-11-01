using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed class ReferenceExampleTests
{
    private static JsonDocument Fixture() => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "Fixtures", "tfstudio-demo-examples-8b942b3.json")));

    [Theory]
    [InlineData("demo-bbar-4", 4)]
    [InlineData("demo-edge", 20)]
    [InlineData("demo-hr-qw", 15)]
    [InlineData("demo-metal-ag", 2)]
    [InlineData("demo-mgf2-ar", 1)]
    public async Task ExactPrescriptionsAndEverySpectrumSampleMatchReference(string id, int layers)
    {
        using var fixture = Fixture();
        var expected = fixture.RootElement.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("id").GetString() == id);
        var input = ReferenceExamples.Create(id); var fingerprint = input.Fingerprint();
        Assert.Equal(layers, input.Layers.Length); Assert.Equal(expected.GetProperty("name").GetString(), input.Name);
        Assert.Equal(ReferenceExamples.SourceCommit, input.ReferenceExample!.SourceCommit);
        Assert.Equal(1, input.ReferenceExample.SubstrateThicknessMm); Assert.Equal("Air", input.ReferenceExample.ExitMediumId);
        Assert.Equal("front_only", input.ReferenceExample.SurfaceMode); Assert.Equal("side", input.ReferenceExample.EvaluationMode);
        var originalLayers = expected.GetProperty("layers").EnumerateArray().ToArray();
        for (var i = 0; i < layers; i++)
        {
            Assert.Equal(originalLayers[i].GetProperty("material").GetString(), input.Layers[i].MaterialId);
            Assert.Equal(originalLayers[i].GetProperty("thickness").GetDouble(), input.Layers[i].ThicknessNm);
            Assert.Equal(!originalLayers[i].GetProperty("locked").GetBoolean(), input.Layers[i].Variable);
        }
        var analysis = new DesignService().EvaluateAnalysisDisplay(input, new(400, 800, .5, [0, 35, 57]));
        foreach (var angle in expected.GetProperty("angles").EnumerateArray())
        {
            var actual = analysis.Single(a => a.AngleDegrees == angle.GetProperty("angle").GetDouble()).Spectrum;
            var rows = angle.GetProperty("samples").EnumerateArray().ToArray();
            Assert.Equal(801, actual.S.Length); Assert.Equal(801, actual.P.Length);
            for (var i = 0; i < rows.Length; i++)
            {
                var row = rows[i].EnumerateArray().Select(v => v.GetDouble()).ToArray();
                Assert.Equal(row[0], actual.S[i].WavelengthNanometers);
                Compare(actual.S[i].Power, row[1..4]); Compare(actual.P[i].Power, row[4..7]);
                Compare(actual.Average[i].Power, Enumerable.Range(0, 3).Select(j => (row[j + 1] + row[j + 4]) / 2).ToArray());
            }
        }
        Assert.Null(input.Result); Assert.Equal(fingerprint, input.Fingerprint());
        var evaluated = new DesignService().Evaluate(input);
        Assert.Equal(fingerprint, evaluated.Fingerprint()); Assert.Equal(input.Layers, evaluated.Layers);
        Assert.Equal(Examples.Create(input.Target.Kind).Target.Reflectance, evaluated.Target.Reflectance);
        var directory = Path.Combine(Path.GetTempPath(), "coating-reference-example-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "example.coating.json"); await ExperimentStore.SaveAsync(evaluated, path);
            var opened = await ExperimentStore.OpenAsync(path);
            Assert.Equal(fingerprint, opened.Fingerprint()); Assert.Equal(input.ReferenceExample, opened.ReferenceExample);
            Assert.Equal(evaluated.Result!.Spectrum, opened.Result!.Spectrum);
            Assert.Equal(input.Materials.Select(m => (m.DisplayColor, m.HoldTabulatedEndpoints)), opened.Materials.Select(m => (m.DisplayColor, m.HoldTabulatedEndpoints)));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("Air")]
    [InlineData("BK7")]
    [InlineData("SiO2")]
    [InlineData("MgF2")]
    [InlineData("TiO2")]
    [InlineData("Ta2O5")]
    [InlineData("Ag")]
    public void MaterialKnotsMidpointsAbsorptionAndEndpointRulesMatchReference(string id)
    {
        using var fixture = Fixture();
        var snapshot = ReferenceExamples.Presets.Select(p => ReferenceExamples.Create(p.Id)).SelectMany(d => d.Materials).First(m => m.Id == id);
        var material = snapshot.Resolve(new MaterialRegistry());
        var expected = fixture.RootElement.GetProperty("materialChecks").EnumerateArray().Single(m => m.GetProperty("id").GetString() == id);
        foreach (var point in expected.GetProperty("values").EnumerateArray())
        {
            var row = point.EnumerateArray().Select(v => v.GetDouble()).ToArray();
            Assert.InRange(Math.Abs(material.RefractiveIndex(row[0]) - row[1]), 0, 2e-12);
            Assert.InRange(Math.Abs(material.ExtinctionCoefficient(row[0]) - row[2]), 0, 2e-12);
        }
        if (id == "BK7") Assert.True(material.ExtinctionCoefficient(550) > 0);
        if (id == "Ag") Assert.True(material.ExtinctionCoefficient(550) > 1);
    }

    [Fact]
    public void EndpointHoldingIsExplicitAndDoesNotRelaxOriginalMaterials()
    {
        var d = ReferenceExamples.Create("demo-edge");
        Assert.Contains(d.Materials, m => m.Id == "Ta2O5" && m.MinimumNm == 500 && m.RangeNotice(400, 800)!.Contains("非实测"));
        var strict = d with { Materials = d.Materials.Select(m => m with { HoldTabulatedEndpoints = false }).ToArray() };
        Assert.Throws<ArgumentException>(() => new DesignService().EvaluateAnalysisDisplay(strict, AnalysisSpectrumOptions.Default));
        var invalid = d with { Materials = d.Materials.Select(m => m.Id == "Air" ? m with { HoldTabulatedEndpoints = true } : m).ToArray() };
        Assert.Throws<InvalidDataException>(invalid.Validate);
        Assert.Throws<ArgumentException>(() => ReferenceExamples.Create("unknown"));
        var old = Examples.Create(DesignKind.HighReflector);
        Assert.All(old.Materials, m => Assert.False(m.HoldTabulatedEndpoints));
        Assert.Equal(.98, old.Target.Reflectance);
    }

    [Fact]
    public void HybridAnalyticIndexKeepsItsDeclaredRangeWhileItsKTableUsesPchip()
    {
        var bk7 = ReferenceExamples.Create("demo-hr-qw").Materials.Single(m => m.Id == "BK7");
        Assert.False(bk7.HoldTabulatedEndpoints);
        var air = new ConstantIndexMaterial("Air", 1);
        var solver = new CoherentThinFilmSolver(air, bk7.Resolve(new MaterialRegistry()), []);
        Assert.Throws<ArgumentException>(() => solver.Evaluate(2900, 0, ThinFilmPolarization.S));
        Assert.Throws<InvalidDataException>(() => (bk7 with { HoldTabulatedEndpoints = true }).Resolve(new MaterialRegistry()));
    }

    [Fact]
    public void EditingReferenceTablePreservesItsInterpolationBoundaryPolicyAndRawValues()
    {
        var source = ReferenceExamples.Create("demo-edge").Materials.Single(m => m.Id == "Ta2O5");
        var edited = MaterialTable.Parse(source.Name, source.Source, MaterialTable.Export(source), source.Id, source);
        Assert.True(edited.HoldTabulatedEndpoints); Assert.Equal(source.DisplayColor, edited.DisplayColor);
        var before = source.Resolve(new MaterialRegistry()); var after = edited.Resolve(new MaterialRegistry());
        for (var w = 400d; w <= 800; w += .5)
        {
            Assert.Equal(before.RefractiveIndex(w), after.RefractiveIndex(w));
            Assert.Equal(before.ExtinctionCoefficient(w), after.ExtinctionCoefficient(w));
        }
        Assert.Equal(MaterialTable.Export(source), MaterialTable.Export(edited));
    }

    [Fact]
    public void ExplicitCubicModelRetainsKnotsAndPlateausWhileLegacyLinearIsUnchanged()
    {
        var x = new double[] { 400, 450, 620, 800 }; var y = new double[] { 1.2, 2.3, 2.3, 1.1 };
        CatalogGlassMaterial Build(string formula) => new("test", "test", formula, 400, 800,
            refractiveIndexWavelengthsNanometers: x, refractiveIndices: y, extinctionWavelengthsNanometers: x, extinctionCoefficients: [0, .2, .2, 0]);
        var cubic = Build(CatalogGlassMaterial.PchipTableFormula); var linear = Build("tabulated nk");
        Assert.Equal(1.75, linear.RefractiveIndex(425));
        for (var i = 0; i < x.Length; i++) Assert.Equal(y[i], cubic.RefractiveIndex(x[i]));
        for (var w = 400; w <= 800; w++) Assert.InRange(cubic.RefractiveIndex(w), 1.1, 2.3);
        Assert.Equal(2.3, cubic.RefractiveIndex(550));
        Assert.Equal(1.2, cubic.RefractiveIndex(390)); Assert.Equal(1.1, cubic.RefractiveIndex(810));
        var air = new ConstantIndexMaterial("Air", 1);
        Assert.True(double.IsFinite(new CoherentThinFilmSolver(air, air, [new(cubic, 80)]).Evaluate(390, 0, ThinFilmPolarization.S).Reflectance));
        Assert.Throws<ArgumentException>(() => new CoherentThinFilmSolver(air, air, [new(linear, 80)]).Evaluate(390, 0, ThinFilmPolarization.S));
    }

    private static void Compare(ThinFilmPower actual, double[] expected)
    {
        Assert.InRange(Math.Abs(actual.Reflectance - expected[0]), 0, 2e-11);
        Assert.InRange(Math.Abs(actual.Transmittance - expected[1]), 0, 2e-11);
        Assert.InRange(Math.Abs(actual.Absorptance - expected[2]), 0, 2e-11);
    }
}
