using System.Security.Cryptography;
using System.Text.Json;
using OptilandWorkbench.CoatingDesign.Engine;
using OptilandWorkbench.Core.Coatings;
using OptilandWorkbench.Core.Materials;

namespace OptilandWorkbench.CoatingDesign.Tests;

public sealed class DailyDesignTests
{
    [Fact]
    public void MaterialImportPreservesUnitsExtinctionAndExactTableOnRoundTrip()
    {
        var material = MaterialTable.Parse("测量材料", "测量批次 A", "wavelength_um,n,k\n0.4,2.1,0.015\n0.7,2.0,0.02\n");
        Assert.Equal(400, material.MinimumNm, 10); Assert.Equal(700, material.MaximumNm, 10);
        var resolved = material.Resolve(new MaterialRegistry());
        Assert.Equal(2.05, resolved.RefractiveIndex(550), 13);
        Assert.Equal(0.0175, resolved.ExtinctionCoefficient(550), 13);
        var again = MaterialTable.Parse(material.Name, material.Source, MaterialTable.Export(material), material.Id);
        Assert.Equal(JsonSerializer.Serialize(material), JsonSerializer.Serialize(again));
        foreach (var data in new[] { "wavelength,n,k\n400,2,0\n700,2,0", "wavelength_nm,n,k\n700,2,0\n400,2,0", "wavelength_nm,n,k\n400,2,-0.1\n700,2,0", "wavelength_nm,n,k\n400,NaN,0\n700,2,0" })
            Assert.Throws<ArgumentException>(() => MaterialTable.Parse("bad", "test", data));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void MultipleCavitiesGenerateRealStacksWithinLayerLimitAndDenseGridAgrees(int cavities)
    {
        var d = Examples.Create(DesignKind.NarrowBand);
        d = d with { Target = d.Target with { Structure = new(cavities), MaximumLayers = 4 * cavities + 3 } };
        d = new DesignService().Generate(d);
        Assert.Equal(4 * cavities + 3, d.Layers.Length);
        Assert.Equal(cavities, d.Layers.Count(l => l.ThicknessNm > d.Layers.First(x => x.MaterialId == d.LowId).ThicknessNm * 1.5));
        var materials = d.Materials.ToDictionary(m => m.Id, m => m.Resolve(new MaterialRegistry()));
        var solver = new CoherentThinFilmSolver(materials[d.IncidentId], materials[d.SubstrateId], d.Layers.Select(l => new CoherentFilm(materials[l.MaterialId], l.ThicknessNm)));
        var points = ThinFilmSpectrum.Sample(solver, ThinFilmSpectrum.Grid(d.Target.MinimumNm, d.Target.MaximumNm, 16000), 0, d.Target.Polarization);
        foreach (var point in d.Result!.Spectrum.Where((_, i) => i % 100 == 0))
        {
            var nearest = points.MinBy(x => Math.Abs(x.WavelengthNanometers - point.WavelengthNanometers))!;
            Assert.InRange(Math.Abs(nearest.Power.Transmittance - point.Power.Transmittance), 0, 0.002);
        }
        Assert.Equal(d.Result.Checks.All(c => c.Passed) && d.Result.SamplingConverged, d.Result.Passed);
    }

    [Fact]
    public void SearchIsDeterministicRecordsEveryStartAndPreservesFixedLayers()
    {
        var service = new DesignService();
        var d = service.Generate(Examples.Create(DesignKind.Antireflection));
        d = d with
        {
            Layers = d.Layers.Select((l, i) => l with { Variable = i != 0 }).ToArray(),
            Result = null,
            Settings = d.Settings with { MaximumIterations = 3, Search = new(3, 8, true) }
        };
        var a = service.Search(d); var b = service.Search(d);
        Assert.Equal(a.Layers, b.Layers);
        Assert.Equal(3, a.Candidates!.Length);
        Assert.All(a.Candidates, c => Assert.Equal(d.Layers[0], c.Layers[0]));
        Assert.All(a.Candidates.SelectMany(c => c.Layers), l => Assert.InRange(l.ThicknessNm, d.Settings.MinimumThicknessNm, d.Settings.MaximumThicknessNm));
        Assert.Equal(a.Candidates.Sum(c => c.Run!.Evaluations), a.Result!.Run.Evaluations);
        Assert.Equal(d.Settings.RandomSeed, a.Result.Run.RandomSeed);
        Assert.DoesNotContain("结构枚举", a.Result.Run.Algorithm);
        Assert.Equal(a.Result.Spectrum, service.Evaluate(a).Result!.Spectrum);
        using var cancel = new CancellationTokenSource();
        var progress = new Immediate(p => { if (p.Stage.StartsWith("结构搜索", StringComparison.Ordinal)) cancel.Cancel(); });
        Assert.Throws<OperationCanceledException>(() => service.Search(d, progress, cancel.Token));
    }

    [Theory]
    [InlineData(ThicknessCorrelation.Common)]
    [InlineData(ThicknessCorrelation.SameMaterial)]
    [InlineData(ThicknessCorrelation.Independent)]
    public async Task ToleranceCorrelationsReproduceActualTrialsAndSurviveExport(ThicknessCorrelation correlation)
    {
        var service = new DesignService();
        var d = Examples.Create(DesignKind.HighReflector);
        d = service.Generate(d with { Target = d.Target with { MaximumLayers = 8 } });
        d = d with { Result = null, Settings = d.Settings with { ToleranceTrials = 3, Tolerancing = new(ToleranceDistribution.Normal, correlation, 0.05) } };
        d = service.Evaluate(d);
        var tolerance = service.Tolerance(d);
        Assert.Equal(JsonSerializer.Serialize(tolerance), JsonSerializer.Serialize(service.Tolerance(d)));
        foreach (var trial in tolerance.Samples!)
        {
            Assert.Null(trial.Error);
            var recomputed = service.Evaluate(d with
            {
                Layers = d.Layers.Select((l, i) => l with { ThicknessNm = trial.ThicknessNm[i] }).ToArray(),
                Target = d.Target with { AngleDegrees = Math.Abs(trial.AngleDegrees) },
                Result = null
            });
            Assert.Equal(trial.Merit, recomputed.Result!.Merit);
            Assert.Equal(trial.Checks, recomputed.Result.Checks);
            var ratios = trial.ThicknessNm.Select((v, i) => v / d.Layers[i].ThicknessNm).ToArray();
            if (correlation == ThicknessCorrelation.Common) Assert.All(ratios, r => Assert.Equal(ratios[0], r, 13));
            if (correlation == ThicknessCorrelation.SameMaterial)
                for (var i = 2; i < ratios.Length; i++) Assert.Equal(ratios[i % 2], ratios[i], 13);
            if (correlation == ThicknessCorrelation.Independent) Assert.True(ratios.Distinct().Count() > 1);
        }
        var directory = Path.Combine(Path.GetTempPath(), "coating-daily-" + Guid.NewGuid());
        try
        {
            d = d with { Tolerance = tolerance };
            await ExperimentStore.ExportAsync(d, directory);
            var reopened = await ExperimentStore.OpenAsync(Path.Combine(directory, "experiment.coating.json"));
            Assert.Equal(d.Fingerprint(), reopened.Fingerprint());
            Assert.Equal(JsonSerializer.Serialize(tolerance), JsonSerializer.Serialize(reopened.Tolerance));
            Assert.Equal(tolerance.Trials + 1, File.ReadAllLines(Path.Combine(directory, "tolerance.csv")).Length);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task LegacyVersionOneExampleRemainsReadableWithOriginalFingerprint()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var file = Path.Combine(root.FullName, "labs/CoatingDesign/examples/Antireflection/experiment.coating.json");
        var saved = JsonSerializer.Deserialize<Experiment>(await File.ReadAllTextAsync(file), Experiment.JsonOptions)!;
        var opened = await ExperimentStore.OpenAsync(file);
        Assert.Equal(saved.Result!.Fingerprint, opened.Fingerprint());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyWindowsFingerprintsPreserveResultsAndToleranceOnOpenAndSave(bool saveThroughStore)
    {
        var service = new DesignService();
        var input = Examples.Create(DesignKind.Antireflection);
        var d = service.Generate(input with { Settings = input.Settings with { ToleranceTrials = 1 } });
        d = d with { Tolerance = service.Tolerance(d) };
        var canonical = d.Fingerprint();
        var windows = WindowsFingerprint(d);
        Assert.NotEqual(canonical, windows);
        var legacy = d with { Result = d.Result! with { Fingerprint = windows }, Tolerance = d.Tolerance! with { Fingerprint = windows } };
        var path = Path.Combine(Path.GetTempPath(), $"coating-windows-{Guid.NewGuid():N}.coating.json");
        try
        {
            if (saveThroughStore) await ExperimentStore.SaveAsync(legacy, path);
            else await File.WriteAllTextAsync(path, JsonSerializer.Serialize(legacy, Experiment.JsonOptions));
            var reopened = await ExperimentStore.OpenAsync(path);
            Assert.Equal(canonical, reopened.Fingerprint());
            Assert.Equal(canonical, reopened.Result!.Fingerprint);
            Assert.Equal(d.Result!.Spectrum, reopened.Result.Spectrum);
            Assert.Equal(JsonSerializer.Serialize(d.Tolerance), JsonSerializer.Serialize(reopened.Tolerance));
            if (saveThroughStore)
            {
                var saved = JsonSerializer.Deserialize<Experiment>(await File.ReadAllTextAsync(path), Experiment.JsonOptions)!;
                Assert.Equal(canonical, saved.Result!.Fingerprint);
                Assert.Equal(canonical, saved.Tolerance!.Fingerprint);
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task LegacyWindowsFingerprintStillRejectsChangedInputs()
    {
        var d = new DesignService().Generate(Examples.Create(DesignKind.Antireflection));
        var legacy = d with { Result = d.Result! with { Fingerprint = WindowsFingerprint(d) }, Target = d.Target with { Reflectance = .5 } };
        var path = Path.Combine(Path.GetTempPath(), $"coating-stale-windows-{Guid.NewGuid():N}.coating.json");
        try
        {
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(legacy, Experiment.JsonOptions));
            await Assert.ThrowsAsync<InvalidDataException>(() => ExperimentStore.OpenAsync(path));
            await Assert.ThrowsAsync<InvalidDataException>(() => ExperimentStore.SaveAsync(legacy, path));
        }
        finally { File.Delete(path); }
    }

    private static string WindowsFingerprint(Experiment d) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
        new { d.Target, d.Settings, d.IncidentId, d.SubstrateId, d.LowId, d.HighId, d.Materials, d.Layers },
        new JsonSerializerOptions(Experiment.JsonOptions) { NewLine = "\r\n" })));

    private sealed class Immediate(Action<DesignProgress> callback) : IProgress<DesignProgress>
    { public void Report(DesignProgress value) => callback(value); }
}
