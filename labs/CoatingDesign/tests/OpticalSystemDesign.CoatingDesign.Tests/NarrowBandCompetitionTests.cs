using System.Text.Json;
using OpticalSystemDesign.CoatingDesign.Engine;
using OpticalSystemDesign.Core.Coatings;
using OpticalSystemDesign.Core.Materials;

namespace OpticalSystemDesign.CoatingDesign.Tests;

public sealed class NarrowBandCompetitionTests
{
    [Fact]
    public void HighReflectorDiagnosisUsesTheWorstVerifiedPointAndAccountsForFilmAbsorption()
    {
        var input = Examples.Create(DesignKind.HighReflector);
        var materials = input.Materials.ToDictionary(m => m.Id, m => m.Resolve(new MaterialRegistry()));
        input = input with
        {
            Layers = Enumerable.Range(0, 32).Select(i =>
        {
            var id = i % 2 == 0 ? input.HighId : input.LowId;
            return new FilmLayer(id, CoherentThinFilmSolver.QuarterWaveThickness(materials[id], materials[input.IncidentId], 550, 0));
        }).ToArray()
        };
        var result = new DesignService().Evaluate(input).Result!;
        var point = result.MaterialSuitability!.SampledWorstReflectance!;
        Assert.Equal(result.Metrics.MinimumReflectance, point.Power.Reflectance);
        Assert.Equal(result.Spectrum.MinBy(s => s.Power.Reflectance), point);
        Assert.True(point.Power.Absorptance > .03);
        Assert.True(point.Power.Transmittance < .001);
        Assert.InRange(Math.Abs(point.Power.Reflectance + point.Power.Transmittance + point.Power.Absorptance - 1), 0, 1e-12);
    }

    [Fact]
    public void AbsorbingMaterialDiagnosisIsInformationalAndNeverChangesInputOrAcceptance()
    {
        var service = new DesignService(); var input = Examples.Create(DesignKind.NarrowBand);
        input = input with { Layers = service.BuildNarrowBandPrototype(input, new([5, 5], [1])) };
        var fingerprint = input.Fingerprint(); var report = service.AssessMaterials(input);
        Assert.False(report.ReferenceLosslessPrototypeApplicable);
        Assert.InRange(report.Films.Single(f => f.MaterialId == input.HighId).CenterExtinction, .0109, .011);
        Assert.InRange(report.Films.Single(f => f.MaterialId == input.LowId).CenterExtinction, .0019, .002);
        var result = service.Evaluate(input).Result!;
        Assert.Equal(report.CenterPower, result.MaterialSuitability!.CenterPower);
        Assert.Equal(fingerprint, input.Fingerprint());
        Assert.Equal(result.SamplingConverged && result.Checks.All(c => c.Passed), result.Passed);
        Assert.DoesNotContain(result.Checks, c => c.Name.Contains("材料", StringComparison.Ordinal));
        Assert.True(report.CenterPower!.Absorptance > .1);
    }

    [Fact]
    public void PublicPrototypeBuilderAgreesWithEveryFrozenPinnedReferenceLayerIncludingParityAndSpacerOrder()
    {
        using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/tfstudio-competition-8b942b3.json")));
        var input = Lossless(); var service = new DesignService();
        foreach (var test in reference.RootElement.GetProperty("cases").EnumerateArray())
        {
            var mirrors = test.GetProperty("mirrors").EnumerateArray().Select(x => x.GetInt32()).ToArray();
            var orders = test.GetProperty("spacers").EnumerateArray().Select(x => x.GetInt32()).ToArray();
            var actual = service.BuildNarrowBandPrototype(input with { Target = input.Target with { Structure = new(orders.Length) } }, new(mirrors, orders));
            var expected = test.GetProperty("layers").EnumerateArray().ToArray();
            Assert.Equal(expected.Length, actual.Length);
            for (var i = 0; i < actual.Length; i++)
            {
                Assert.Equal(expected[i].GetProperty("tag").GetString() == "H" ? input.HighId : input.LowId, actual[i].MaterialId);
                Assert.Equal(expected[i].GetProperty("thicknessNm").GetDouble(), actual[i].ThicknessNm, 10);
            }
        }
        Assert.Throws<ArgumentException>(() => service.BuildNarrowBandPrototype(input, new([5, 5], [400])));
        Assert.Throws<ArgumentException>(() => service.BuildNarrowBandPrototype(input, new([41, 41], [1])));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ActualMediumMatchingRestoresLosslessTransmissionWithoutChangingTheFilterAndHonorsFixedLayers(int referenceCase)
    {
        var service = new DesignService(); var input = Lossless();
        using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures/tfstudio-competition-8b942b3.json")));
        var test = reference.RootElement.GetProperty("matching")[referenceCase];
        var mirrors = test.GetProperty("mirrors").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        var orders = test.GetProperty("spacers").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        input = input with { Target = input.Target with { Structure = new(orders.Length) } };
        input = input with { Layers = service.BuildNarrowBandPrototype(input, new(mirrors, orders)) };
        var before = input.Fingerprint(); var options = service.MatchIncidentMedium(input);
        Assert.NotEmpty(options);
        var best = options.MinBy(o => o.After.Reflectance)!;
        Assert.True(best.Before.Reflectance > .01);
        Assert.InRange(best.After.Reflectance, 0, 1e-5);
        Assert.True(best.After.Transmittance > .9999);
        var expected = test.GetProperty("after");
        Assert.InRange(Math.Abs(best.After.Reflectance - expected.GetProperty("R").GetDouble()), 0, 1e-5);
        Assert.InRange(Math.Abs(best.After.Transmittance - expected.GetProperty("T").GetDouble()), 0, 1e-5);
        Assert.All(options, option =>
        {
            Assert.InRange(option.Layers.Length, input.Layers.Length, input.Target.MaximumLayers);
            Assert.All(option.Layers, l => Assert.InRange(l.ThicknessNm, input.Settings.MinimumThicknessNm, input.Settings.MaximumThicknessNm));
        });
        Assert.Equal(before, input.Fingerprint());
        var fixedInput = input with { Layers = input.Layers.Select((l, i) => l with { Variable = i != 0 }).ToArray() };
        Assert.Empty(service.MatchIncidentMedium(fixedInput));
        Assert.Empty(service.MatchIncidentMedium(input with { Target = input.Target with { MaximumLayers = input.Layers.Length } }));
    }

    [Fact]
    public async Task CompetitionPreservesBoundsWorkProvenanceAndOriginalDenseAcceptanceOnSaveAndReopen()
    {
        var service = new DesignService(); var input = Lossless();
        input = input with
        {
            Target = input.Target with { MaximumLayers = 19, Structure = new(2) },
            Settings = input.Settings with { MaximumIterations = 2, Search = new(2) }
        };
        var fingerprint = input.Fingerprint(); var generated = service.Generate(input);
        var run = generated.Result!.Run; var record = run.NarrowBandCompetition!;
        Assert.Equal(DesignService.NarrowBandCompetitionProtocol, run.DesignProtocol);
        Assert.InRange(record.InitialStructures, 1, 512);
        Assert.InRange(record.DiscreteEvaluations, record.InitialStructures, record.DiscreteEvaluationLimit);
        Assert.InRange(run.Screening!.Length, 1, record.ShortCandidateLimit);
        Assert.Equal(run.Screening.Sum(s => s.Run.Evaluations) + record.MatchingTrials.Sum(m => m.Run.Evaluations), run.Evaluations);
        Assert.All(generated.Candidates!, c => Assert.False(c.Passed));
        Assert.All(run.Screening, s => Assert.All(s.Layers, l => Assert.InRange(l.ThicknessNm, 2, 2000)));
        Assert.Contains(run.Screening, s => s.OriginPrototype is not null);
        Assert.Equal(fingerprint, input.Fingerprint());
        var path = Path.Combine(Path.GetTempPath(), "coating-competition-" + Guid.NewGuid() + ".coating.json");
        try
        {
            await ExperimentStore.SaveAsync(generated, path);
            var reopened = await ExperimentStore.OpenAsync(path);
            Assert.Equal(generated.Fingerprint(), reopened.Fingerprint());
            Assert.Equal(generated.Result.Spectrum, reopened.Result!.Spectrum);
            Assert.Equal(JsonSerializer.Serialize(run, Experiment.JsonOptions), JsonSerializer.Serialize(reopened.Result.Run, Experiment.JsonOptions));
            Assert.Equal(reopened.Result.SamplingConverged && reopened.Result.Checks.All(c => c.Passed), reopened.Result.Passed);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void FixedLayerSearchCannotRegenerateOrAddMatchingLayersAndCancellationPublishesNoResult()
    {
        var service = new DesignService(); var input = Lossless();
        input = input with
        {
            Layers = service.BuildNarrowBandPrototype(input, new([5, 5], [1])).Select((l, i) => l with { Variable = i != 0 }).ToArray(),
            Settings = input.Settings with { MaximumIterations = 1, Search = new(3) }
        };
        var searched = service.Search(input);
        Assert.Equal(input.Layers.Length, searched.Layers.Length);
        Assert.Equal(input.Layers[0], searched.Layers[0]);
        Assert.Empty(searched.Result!.Run.NarrowBandCompetition!.MatchingTrials);
        Assert.Equal(4, searched.Candidates!.Length); // Three complete starts plus the original independently verified fallback.
        Assert.All(searched.Candidates, c => Assert.Equal(input.Layers[0], c.Layers[0]));
        using var cancellation = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => service.Generate(Lossless(), new Immediate(p =>
        { if (p.Stage.StartsWith("镜段/腔阶竞争", StringComparison.Ordinal)) cancellation.Cancel(); }), cancellation.Token));
        Assert.Null(input.Result);
    }

    [Fact]
    public void StructuralSearchRecordsEveryCompleteFinalistAndRetainsIndependentAcceptance()
    {
        var input = Lossless();
        input = input with
        {
            Target = input.Target with { MaximumLayers = 19, Structure = new(2) },
            Settings = input.Settings with { MaximumIterations = 2, Search = new(2) }
        };
        var result = new DesignService().Search(input); var run = result.Result!.Run;
        Assert.Equal(2, result.Candidates!.Length);
        Assert.All(result.Candidates, c => Assert.NotNull(c.Run));
        Assert.Equal(run.Screening!.Sum(s => s.Run.Evaluations) + run.NarrowBandCompetition!.MatchingTrials.Sum(m => m.Run.Evaluations)
            + result.Candidates.Sum(c => c.Run!.Evaluations), run.Evaluations);
        Assert.Equal(result.Result.Passed, result.Result.SamplingConverged && result.Result.Checks.All(c => c.Passed));
        Assert.Contains(result.Candidates, c => c.Layers.SequenceEqual(result.Layers) && c.Passed == result.Result.Passed);
        Assert.All(result.Candidates, c => Assert.InRange(c.Layers.Length, 1, input.Target.MaximumLayers));
    }

    internal static Experiment Lossless()
    {
        var input = Examples.Create(DesignKind.NarrowBand);
        var low = MaterialLibrary.Capture(new ConstantIndexMaterial("L", 1.45), "independent reference fixture", 300, 1000);
        var high = MaterialLibrary.Capture(new ConstantIndexMaterial("H", 2.1), "independent reference fixture", 300, 1000);
        var substrate = MaterialLibrary.Capture(new ConstantIndexMaterial("S", 1.52), "independent reference fixture", 300, 1000);
        return input with
        {
            LowId = low.Id,
            HighId = high.Id,
            SubstrateId = substrate.Id,
            Materials = input.Materials.Concat([low, high, substrate]).ToArray(),
            Settings = input.Settings with { MaximumIterations = 32, DesignProtocolVersion = 3 }
        };
    }

    private sealed class Immediate(Action<DesignProgress> callback) : IProgress<DesignProgress>
    { public void Report(DesignProgress value) => callback(value); }
}
