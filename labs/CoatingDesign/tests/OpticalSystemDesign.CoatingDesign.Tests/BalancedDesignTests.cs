using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed class BalancedDesignTests
{
    [Fact]
    public void ConstantIndexCavityProposalsMatchTheActualPinnedTfStudioLayerBuilder()
    {
        using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Fixtures/tfstudio-prototypes-8b942b3.json")));
        var input = Examples.Create(DesignKind.NarrowBand);
        var low = MaterialLibrary.Capture(new ConstantIndexMaterial("L", 1.45), "reference fixture", 300, 1000);
        var high = MaterialLibrary.Capture(new ConstantIndexMaterial("H", 2.1), "reference fixture", 300, 1000);
        input = input with
        {
            LowId = low.Id,
            HighId = high.Id,
            Materials = input.Materials.Concat([low, high]).ToArray(),
            Settings = input.Settings with { DesignProtocolVersion = 2 },
            Target = input.Target with { MaximumLayers = 19, Structure = new(2) { PrototypeFamilyVersion = 2 } }
        };
        var generated = new DesignService().Generate(input);
        var proposals = generated.Result!.Run.Screening!.Select(s => s.InitialLayers).ToArray();
        foreach (var test in reference.RootElement.GetProperty("cases").EnumerateArray())
        {
            var expected = test.GetProperty("layers").EnumerateArray().ToArray();
            Assert.Contains(proposals, layers => layers.Length == expected.Length && layers.Zip(expected).All(pair =>
                pair.First.MaterialId == (pair.Second.GetProperty("tag").GetString() == "H" ? high.Id : low.Id)
                && Math.Abs(pair.First.ThicknessNm - pair.Second.GetProperty("thicknessNm").GetDouble()) < 1e-10));
        }
        Assert.All(proposals, p => Assert.InRange(p.Length, 11, 19));
    }

    [Fact]
    public void RefinedTemplatesProduceAPromisingArSeedAndKeepUnverifiedCandidatesUnqualified()
    {
        var input = Examples.Create(DesignKind.Antireflection); var fingerprint = input.Fingerprint();
        var service = new DesignService(); var generated = service.Generate(input);
        Assert.Equal(fingerprint, input.Fingerprint());
        Assert.NotNull(generated.Result);
        var optimized = service.Optimize(generated with { Settings = generated.Settings with { MaximumIterations = 24 } });
        Assert.True(optimized.Result!.Passed);
        Assert.Equal(DesignService.ExtendedGeneratorVersion, generated.Result.Run.AlgorithmVersion);
        Assert.Equal(DesignService.TargetBalancedProtocol, generated.Result.Run.DesignProtocol);
        var screens = generated.Result.Run.Screening!;
        Assert.InRange(screens.Length, 2, 16);
        Assert.True(generated.Result.Run.TemplateEvaluations >= screens.Length);
        Assert.Equal(screens.Sum(s => s.Run.Evaluations), generated.Result.Run.Evaluations);
        Assert.All(screens, s => Assert.InRange(s.Run.Iterations, 0, 2));
        Assert.All(generated.Candidates!, c => Assert.False(c.Passed));
        foreach (var screen in screens.Take(3))
        {
            var before = service.Evaluate(input with { Layers = screen.InitialLayers }).Result!;
            var after = service.Evaluate(input with { Layers = screen.Layers }).Result!;
            Assert.True(after.Merit <= before.Merit + 1e-12);
            Assert.Equal(after.Merit, screen.Merit, 12);
        }
    }

    [Theory]
    [InlineData(DesignKind.Antireflection)]
    [InlineData(DesignKind.NarrowBand)]
    public void ConstantPhysicalResponseDoesNotGainOrLoseWeightWhenObjectiveGridDensityChanges(DesignKind kind)
    {
        var input = Examples.Create(kind);
        var substrate = MaterialLibrary.Capture(new ConstantIndexMaterial("constant substrate", 1.5), "analytic test", 300, 1000);
        input = input with
        {
            SubstrateId = substrate.Id,
            Materials = input.Materials.Append(substrate).ToArray(),
            Settings = input.Settings with { DesignProtocolVersion = 2 }
        };
        var service = new DesignService();
        var sparse = service.Evaluate(input with { Settings = input.Settings with { OptimizationIntervals = 20, PreviewIntervals = 40 } });
        var dense = service.Evaluate(input with { Settings = input.Settings with { OptimizationIntervals = 800, PreviewIntervals = 40 } });
        Assert.InRange(Math.Abs(sparse.Result!.Merit - dense.Result!.Merit) / Math.Max(1, Math.Abs(sparse.Result.Merit)), 0, 1e-12);
        Assert.Equal(sparse.Result.Passed, dense.Result.Passed);
        Assert.False(sparse.Result.Passed);
    }

    [Fact]
    public async Task ModernSearchReusesScreenedTemplatesAccountsForEveryRefinementAndPreservesFixedLayers()
    {
        var service = new DesignService(); var input = service.Generate(Examples.Create(DesignKind.Antireflection));
        input = input with
        {
            Layers = input.Layers.Select((l, i) => l with { Variable = i != 0 }).ToArray(),
            Settings = input.Settings with { MaximumIterations = 2, Search = new(2, 8, true) },
            Result = null
        };
        var result = service.Search(input); var repeated = service.Search(input);
        Assert.Equal(result.Layers, repeated.Layers);
        var run = result.Result!.Run;
        Assert.Equal(DesignService.ScreenedSearchVersion, run.AlgorithmVersion);
        Assert.Single(run.Screening!);
        Assert.Equal(2, result.Candidates!.Length);
        Assert.All(result.Candidates, c => Assert.Equal(input.Layers[0], c.Layers[0]));
        Assert.Equal(result.Candidates.Sum(c => c.Run!.Evaluations) + run.Screening!.Sum(s => s.Run.Evaluations), run.Evaluations);
        Assert.Equal(result.Candidates.Sum(c => c.Run!.Iterations) + run.Screening!.Sum(s => s.Run.Iterations), run.Iterations);
        Assert.DoesNotContain("结构枚举", run.Algorithm);
        using var cancel = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => service.Search(input,
            new Immediate(p => { if (p.Stage.StartsWith("候选短程优化", StringComparison.Ordinal)) cancel.Cancel(); }), cancel.Token));
        var path = Path.Combine(Path.GetTempPath(), $"coating-balanced-{Guid.NewGuid():N}.coating.json");
        try
        {
            await ExperimentStore.SaveAsync(result, path);
            var reopened = await ExperimentStore.OpenAsync(path);
            Assert.Equal(result.Fingerprint(), reopened.Fingerprint());
            Assert.Equal(result.Result.Spectrum, reopened.Result!.Spectrum);
            Assert.Equal(JsonSerializer.Serialize(run, Experiment.JsonOptions), JsonSerializer.Serialize(reopened.Result.Run, Experiment.JsonOptions));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NullProtocolPreservesLegacyGenerationAndUnknownProtocolsAreRejected()
    {
        var input = Examples.Create(DesignKind.Antireflection);
        input = input with { Settings = input.Settings with { DesignProtocolVersion = null } };
        Assert.DoesNotContain("DesignProtocolVersion", JsonSerializer.Serialize(input, Experiment.JsonOptions));
        var generated = new DesignService().Generate(input);
        Assert.Equal(DesignService.GeneratorVersion, generated.Result!.Run.AlgorithmVersion);
        Assert.Null(generated.Result.Run.Screening); Assert.Null(generated.Result.Run.DesignProtocol);
        Assert.Throws<ArgumentException>(() => new DesignService().Generate(input with
        { Settings = input.Settings with { DesignProtocolVersion = 1 } }));
    }

    private sealed class Immediate(Action<DesignProgress> callback) : IProgress<DesignProgress>
    { public void Report(DesignProgress value) => callback(value); }
}
