using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;
using OptilandWorkbench.InitialStructure.Persistence;
using Xunit.Abstractions;

namespace OptilandWorkbench.InitialStructure.Tests;

public sealed class FlatStartSearchTests(ITestOutputHelper output)
{
    private static InitialStructureSpecification Specification(int budget = 500, int roots = 3, int parallel = 1)
    {
        var frozen = FlatStartDesignTests.LoadSpecification("03-50mm-monochrome.json");
        return frozen with { Budget = frozen.Budget with { MaximumEvaluations = budget, InitialSeedCount = roots, MaximumParallelism = parallel } };
    }
    private static FlatStartSearchOptions Options(int perTrial = 120) => new() { MaximumEvaluationsPerTrial = perTrial };

    [Fact]
    public void RootPlanCoversCountsMaterialsStopsAndStrictlyZeroCurvature()
    {
        var spec = Specification(2_000, 9) with { MinimumElementCount = 1, MaximumElementCount = 3 };
        var materialSet = FlatStartSearchPlanning.Materials(spec, Options());
        var roots = FlatStartSearchPlanning.Roots(spec, materialSet.Names);
        Assert.Equal(new[] { 1, 2, 3, 1, 2, 3, 1, 2, 3 }, roots.Select(root => root.ElementCount));
        Assert.Contains(roots, root => root.GlassNames.Contains("N-F2"));
        Assert.Contains(roots, root => root.StopSurfaceIndex > 1);
        foreach (var family in roots)
        {
            var root = new FlatRootFactory().Create(spec, family.ElementCount, family: family);
            Assert.All(root.Surfaces, surface => Assert.Equal(0, surface.Radius));
            Assert.Equal(family.StopSurfaceIndex, root.Surfaces.FindIndex(surface => surface.IsStop));
            for (var i = 0; i < family.ElementCount; i++)
            {
                Assert.Equal(family.GlassNames[i], root.Surfaces[2 * i + 1].Material);
                Assert.Equal(family.CenterThicknesses[i], root.Surfaces[2 * i + 1].Thickness);
            }
        }
        Assert.Equal(ContentFingerprint.Compute(roots), ContentFingerprint.Compute(FlatStartSearchPlanning.Roots(spec, materialSet.Names)));
        Assert.NotEqual(ContentFingerprint.Compute(roots), ContentFingerprint.Compute(FlatStartSearchPlanning.Roots(
            spec with { Budget = spec.Budget with { RandomSeed = 102 } }, materialSet.Names)));
    }

    [Fact]
    public async Task MissingGlassIsReportedAndRemainingActualCatalogGlassStillRuns()
    {
        var spec = Specification(60, 1) with { InitialGlass = "ABSENT-GLASS-P3" };
        var result = await new FlatStartSearchService().RunAsync(spec, Options() with { AllowedGlassNames = ["ABSENT-GLASS-P3", "N-BK7"] });
        Assert.Contains(result.Checkpoint.Diagnostics, item => item.Code == "search.glass-excluded");
        Assert.Equal(new[] { "N-BK7" }, result.Checkpoint.UsableGlassNames);
        Assert.Contains(result.Checkpoint.Trials, trial => trial.State == FamilyTrialState.Completed);
        Assert.All(result.Checkpoint.Trials, trial => Assert.All(trial.Family.GlassNames, name => Assert.Equal("N-BK7", name)));
        Assert.True(result.Checkpoint.TracedRealRayCount > 0);
    }

    [Theory]
    [InlineData("ABSENT-GLASS-P3", 587.6)]
    [InlineData("Air", 587.6)]
    [InlineData("N-BK7", 100)]
    public async Task NoUsableCatalogGlassNeverCreatesAnApproximation(string name, double wavelength)
    {
        var spec = Specification() with { Wavelengths = [new() { Nanometers = wavelength, IsPrimary = true }] };
        var result = await new FlatStartSearchService().RunAsync(spec, Options() with { AllowedGlassNames = [name] });
        Assert.Equal(FlatStartSearchState.NoUsableGlass, result.Checkpoint.State);
        Assert.Empty(result.Checkpoint.Trials);
        Assert.Empty(result.Candidates);
        Assert.Equal(0, result.Checkpoint.ChargedEvaluations);
        FlatStartCheckpointValidation.Validate(result.Checkpoint);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(25)]
    [InlineData(160)]
    public async Task QuotasIncludeEveryRootRefinementProbeAndFinalValidation(int budget)
    {
        var result = await new FlatStartSearchService().RunAsync(Specification(budget, 3, 3), Options());
        FlatStartCheckpointValidation.Validate(result.Checkpoint);
        Assert.Equal(budget, result.Checkpoint.ChargedEvaluations);
        Assert.All(result.Checkpoint.Trials, trial =>
        {
            Assert.Equal(FamilyTrialState.Completed, trial.State);
            Assert.Equal(trial.CompletedEvaluations, trial.ChargedEvaluations);
            Assert.InRange(trial.ChargedEvaluations, 1, trial.AllocatedEvaluations);
            if (trial.AllocatedEvaluations == 1 && trial.ParentTrialId is null) Assert.Null(trial.Candidate);
        });
    }

    [Fact]
    public async Task RefinementProvenanceAndFullFieldResultsUseTheFormalEngine()
    {
        var spec = Specification(1_400, 1);
        var result = await new FlatStartSearchService().RunAsync(spec, Options());
        FlatStartCheckpointValidation.Validate(result.Checkpoint);
        foreach (var operation in new[] { "refine", "glass-swap", "glass-pair-swap", "stop-surface-change", "parameter-perturbation" })
            Assert.Contains(result.Checkpoint.Trials, trial => trial.Operation == operation && trial.State == FamilyTrialState.Completed);
        foreach (var trial in result.Checkpoint.Trials.Where(trial => trial.Candidate is not null))
        {
            var candidate = trial.Candidate!;
            Assert.All(candidate.FlatRootOptic.Surfaces, surface => Assert.Equal(0, surface.Radius));
            if (trial.ParentTrialId is { } id)
            {
                var parent = result.Checkpoint.Trials.Single(item => item.TrialId == id).Candidate!;
                Assert.Equal(parent.CandidateId, candidate.Lineage.ParentCandidateId);
                Assert.Equal(parent.Lineage.RootFingerprint, candidate.Lineage.RootFingerprint);
                Assert.Null(trial.BootstrapProof);
                Assert.NotNull(trial.RefinementStartOptic);
            }
            var optic = Optic.FromSnapshot(candidate.Optic);
            foreach (var field in trial.FinalValidation!.Fields)
            {
                var actual = SpotMetricEvaluator.EvaluatePupilSamples(optic, 0, field.NormalizedFieldY,
                    FlatStartDesignProblem.Pupils(true).ToArray(), includeSurfaceTransmission: false);
                Assert.Equal(actual.Metrics?.RmsSpotRadius, field.RmsRadiusMillimeters);
                Assert.Equal(actual.Metrics?.MaximumSpotRadius, field.MaximumRadiusMillimeters);
                Assert.Equal(actual.VignettedRayCount, field.AttemptedRays - field.ValidRays);
            }
            Assert.Equal(trial.FinalValidation.MeetsTargets, candidate.Status == CandidateStatus.LabAccepted);
        }
        Assert.Contains(result.Candidates, candidate => candidate.Status == CandidateStatus.LabAccepted);
        output.WriteLine($"{result.Checkpoint.Trials.Count} trials; {result.Checkpoint.ChargedEvaluations} evaluations; {result.Checkpoint.TracedRealRayCount} formal rays");
    }

    [Fact]
    public async Task CompletedBatchCheckpointResumeMatchesUninterruptedSearch()
    {
        var spec = Specification();
        var options = Options();
        var service = new FlatStartSearchService();
        var uninterrupted = await service.RunAsync(spec, options);
        using var stop = new CancellationTokenSource();
        var path = TemporaryPath();
        var store = new FlatStartSearchCheckpointStore();
        try
        {
            var cancelled = await service.RunAsync(spec, options, stop.Token, checkpointSink: async (saved, token) =>
            {
                await store.SaveAsync(saved, path, token);
                if (saved.Trials.Any(trial => trial.State == FamilyTrialState.Completed)) stop.Cancel();
            });
            Assert.Equal(FlatStartSearchState.Cancelled, cancelled.Checkpoint.State);
            var checkpoint = await store.LoadAsync(path);
            var resumed = await service.RunAsync(spec, options, checkpoint: checkpoint);
            AssertResultsEqual(uninterrupted, resumed);
            Assert.Equal(checkpoint.RunId, resumed.Checkpoint.RunId);
            Assert.True(resumed.Checkpoint.ElapsedComputeTicks >= checkpoint.ElapsedComputeTicks);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ParallelRootCompletionOrderDoesNotChangePhysicalResultsOrCharges()
    {
        var serial = await new FlatStartSearchService().RunAsync(Specification(), Options());
        var parallel = await new FlatStartSearchService().RunAsync(Specification(parallel: 3), Options());
        AssertResultsEqual(serial, parallel);
    }

    [Fact]
    public async Task ReservedUnknownWorkIsChargedOnResumeAndCannotBeSpentAgain()
    {
        var spec = Specification(100, 2);
        FlatStartSearchCheckpoint? reserved = null;
        await Assert.ThrowsAsync<IOException>(() => new FlatStartSearchService().RunAsync(spec, Options(), checkpointSink: (saved, _) =>
        {
            reserved = saved;
            throw new IOException("Simulated process loss after persisting reservation.");
        }));
        Assert.NotNull(reserved);
        var charged = reserved.ChargedEvaluations;
        var reservedTime = reserved.ReservedComputeTicks;
        var result = await new FlatStartSearchService().RunAsync(spec, Options(), checkpoint: reserved);
        var interrupted = result.Checkpoint.Trials[0];
        Assert.Equal(FamilyTrialState.Interrupted, interrupted.State);
        Assert.Null(interrupted.CompletedEvaluations);
        Assert.Equal(charged, interrupted.ChargedEvaluations);
        Assert.Equal(100, result.Checkpoint.ChargedEvaluations);
        Assert.True(result.Checkpoint.ElapsedComputeTicks >= reserved.ElapsedComputeTicks + reservedTime);
        FlatStartCheckpointValidation.Validate(result.Checkpoint);
    }

    [Fact]
    public async Task CheckpointRejectsChangedSpecificationCatalogCountersLineageAndChecksum()
    {
        var spec = Specification(60, 1);
        var result = await new FlatStartSearchService().RunAsync(spec, Options());
        var saved = result.Checkpoint;
        var service = new FlatStartSearchService();
        await Assert.ThrowsAsync<InvalidDataException>(() => service.RunAsync(spec with { FNumber = 9 }, Options(), checkpoint: saved));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.RunAsync(spec, Options(), checkpoint: saved with { MaterialFingerprint = new string('0', 64) }));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.RunAsync(spec, Options(), checkpoint: saved with { NextRootIndex = 0 }));
        var altered = saved.Trials.ToArray();
        altered[0] = altered[0] with { ParentTrialId = altered[0].TrialId };
        await Assert.ThrowsAsync<InvalidDataException>(() => service.RunAsync(spec, Options(), checkpoint: saved with { Trials = altered }));
        var path = TemporaryPath();
        var store = new FlatStartSearchCheckpointStore();
        try
        {
            await store.SaveAsync(saved, path);
            var text = await File.ReadAllTextAsync(path);
            await File.WriteAllTextAsync(path, text.Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal));
            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task PreCancelledRunPreservesEmptyResumableStateWithoutOpticalWork()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var result = await new FlatStartSearchService().RunAsync(Specification(), Options(), source.Token);
        Assert.Empty(result.Checkpoint.Trials);
        Assert.Equal(FlatStartSearchState.Cancelled, result.Checkpoint.State);
        FlatStartCheckpointValidation.Validate(result.Checkpoint);
    }

    [Fact]
    public async Task ArchiveRemovesDuplicateOpticsAndPreservesDistinctFamilies()
    {
        var spec = Specification(400, 3);
        var result = await new FlatStartSearchService().RunAsync(spec, Options());
        var trials = result.Checkpoint.Trials.Where(trial => trial.Candidate is not null).ToArray();
        Assert.True(trials.Length > 2);
        var duplicated = trials.Concat(trials.Select(trial => trial with { Candidate = trial.Candidate! with { CandidateId = "duplicate-" + trial.Candidate!.CandidateId } }));
        var selected = FlatStartCandidateArchive.Select(spec, Options() with { MinimumStructuralDistance = 1e9 }, duplicated);
        Assert.Equal(selected.Count, selected.Select(candidate => candidate.OpticFingerprint).Distinct().Count());
        Assert.True(selected.Select(FlatStartCandidateArchive.FamilyKey).Distinct().Count() >= 2);
        Assert.Equal(selected.Count, selected.Select(FlatStartCandidateArchive.FamilyKey).Distinct().Count());
    }

    [Theory]
    [InlineData("01-24mm-monochrome.json")]
    [InlineData("02-35mm-fixed-back.json")]
    [InlineData("03-50mm-monochrome.json")]
    [InlineData("04-75mm-visible.json")]
    [InlineData("05-100mm-fixed-back.json")]
    [InlineData("06-150mm-visible.json")]
    [InlineData("07-24mm-visible.json")]
    [InlineData("08-35mm-visible.json")]
    [InlineData("09-50mm-visible.json")]
    [InlineData("10-85mm-visible.json")]
    [InlineData("11-100mm-visible.json")]
    [InlineData("12-50mm-six-elements.json")]
    public async Task FrozenSpecificationSurveyKeepsTargetsAndReportsActualFamilyOutcomes(string name)
    {
        var spec = FlatStartDesignTests.LoadSpecification(name);
        var result = await new FlatStartSearchService().RunAsync(spec);
        FlatStartCheckpointValidation.Validate(result.Checkpoint);
        Assert.Equal(ContentFingerprint.Compute(spec), result.Checkpoint.SpecificationFingerprint);
        Assert.InRange(result.Checkpoint.ChargedEvaluations, 1, spec.Budget.MaximumEvaluations);
        Assert.All(result.Checkpoint.Trials, trial => Assert.NotEqual(FamilyTrialState.Failed, trial.State));
        foreach (var trial in result.Checkpoint.Trials.Where(trial => trial.Candidate?.Status == CandidateStatus.LabAccepted))
        {
            Assert.True(trial.FinalValidation!.MeetsTargets);
            Assert.All(trial.FinalValidation.Fields, field =>
            {
                Assert.True(field.RmsRadiusMillimeters <= spec.MaximumRmsSpotRadiusMillimeters);
                Assert.True(field.MaximumRadiusMillimeters <= spec.MaximumSpotRadiusMillimeters);
                Assert.True((double)field.ValidRays / field.AttemptedRays >= spec.FlatStart!.MinimumValidRayFraction);
            });
        }
        var accepted = result.Checkpoint.Trials.Count(trial => trial.Candidate?.Status == CandidateStatus.LabAccepted);
        output.WriteLine($"{name}: {accepted} accepted trials / {result.Checkpoint.Trials.Count}; {result.Checkpoint.ChargedEvaluations} evaluations; {result.Checkpoint.TracedRealRayCount} rays");
        var directory = Environment.GetEnvironmentVariable("INITIAL_STRUCTURE_FAMILY_EVIDENCE");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            await new FlatStartSearchCheckpointStore().SaveAsync(result.Checkpoint, Path.Combine(directory, name));
            await File.WriteAllTextAsync(Path.Combine(directory, Path.GetFileNameWithoutExtension(name) + "-candidates.json"),
                JsonSerializer.Serialize(result.Candidates, new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals }));
        }
    }

    private static void AssertResultsEqual(FlatStartSearchResult first, FlatStartSearchResult second)
    {
        Assert.Equal(first.Checkpoint.ChargedEvaluations, second.Checkpoint.ChargedEvaluations);
        Assert.Equal(first.Checkpoint.TracedRealRayCount, second.Checkpoint.TracedRealRayCount);
        Assert.Equal(first.Checkpoint.Trials.Select(trial => (trial.Operation, trial.CompletedEvaluations, trial.Candidate?.OpticFingerprint)),
            second.Checkpoint.Trials.Select(trial => (trial.Operation, trial.CompletedEvaluations, trial.Candidate?.OpticFingerprint)));
        Assert.Equal(first.Candidates.Select(candidate => candidate.OpticFingerprint), second.Candidates.Select(candidate => candidate.OpticFingerprint));
    }

    private static string TemporaryPath() => Path.Combine(Path.GetTempPath(), "family-" + Guid.NewGuid().ToString("N") + ".json");
}
