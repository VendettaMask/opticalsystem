using System.Text.Json;
using OptilandWorkbench.InitialStructure.Contracts;
using OptilandWorkbench.InitialStructure.Engine;
using OptilandWorkbench.InitialStructure.Persistence;

namespace OptilandWorkbench.InitialStructure.Tests;

public sealed class FlatStartRefinementTests
{
    internal static InitialStructureSpecification Spec(int budget = 120) => FlatStartDesignTests.LoadSpecification("03-50mm-monochrome.json") is { } frozen
        ? frozen with { Budget = frozen.Budget with { InitialSeedCount = 1, MaximumEvaluations = budget, MaximumParallelism = 1 } }
        : throw new InvalidOperationException();
    internal static FlatStartSearchOptions Options => new() { MaximumEvaluationsPerTrial = 120 };

    [Fact]
    public async Task SelectedRefinementPreservesTargetsRootSourceAndUsesOnlyNewBudget()
    {
        var source = (await new FlatStartSearchService().RunAsync(Spec(), Options)).Checkpoint;
        var originalHash = ContentFingerprint.Compute(source);
        var selected = FlatStartSearchService.SelectCandidates(source)[0];
        var child = FlatStartSearchService.CreateRefinementCheckpoint(source, selected.CandidateId, 160, TimeSpan.FromMinutes(2));
        Assert.NotEqual(source.RunId, child.RunId);
        Assert.Equal(0, child.ChargedEvaluations);
        Assert.Empty(child.RootPlan);
        Assert.Equal(originalHash, ContentFingerprint.Compute(source));
        Assert.Equal(ContentFingerprint.Compute(source.Specification), ContentFingerprint.Compute(child.Specification with { Budget = source.Specification.Budget }));
        Assert.Equal(source.ChargedEvaluations, child.Origin!.ChargedEvaluations);
        Assert.Equal(selected.OpticFingerprint, child.Origin.Source.Candidate!.OpticFingerprint);
        var result = await new FlatStartSearchService().RunAsync(child.Specification, child.Options, checkpoint: child);
        FlatStartCheckpointValidation.Validate(result.Checkpoint);
        Assert.Equal(160, result.Checkpoint.ChargedEvaluations);
        Assert.All(result.Checkpoint.Trials, trial =>
        {
            Assert.Equal("refine-selected", trial.Operation);
            Assert.Equal(FamilyTrialState.Completed, trial.State);
            Assert.All(trial.FlatRoot!.Surfaces, surface => Assert.Equal(0, surface.Radius));
            Assert.Equal(selected.Lineage.RootFingerprint, trial.Candidate!.Lineage.RootFingerprint);
            Assert.NotEqual(selected.CandidateId, trial.Candidate.CandidateId);
        });
        Assert.Contains(result.Candidates, candidate => FlatStartCandidateArchive.Score(child.Specification, candidate) <= FlatStartCandidateArchive.Score(child.Specification, selected));
        Assert.Equal(originalHash, ContentFingerprint.Compute(source));
    }

    [Fact]
    public async Task RefinementCancellationPersistenceAndResumePreserveQuotaAndCandidateIdentity()
    {
        var source = (await new FlatStartSearchService().RunAsync(Spec(), Options)).Checkpoint;
        var child = FlatStartSearchService.CreateRefinementCheckpoint(source, FlatStartSearchService.SelectCandidates(source)[0].CandidateId, 160, TimeSpan.FromMinutes(2));
        using var cancel = new CancellationTokenSource();
        var stopped = await new FlatStartSearchService().RunAsync(child.Specification, child.Options, cancel.Token, child,
            (saved, _) => { if (saved.Trials.Any(trial => trial.State == FamilyTrialState.Completed)) cancel.Cancel(); return ValueTask.CompletedTask; });
        Assert.Equal(FlatStartSearchState.Cancelled, stopped.Checkpoint.State);
        Assert.InRange(stopped.Checkpoint.ChargedEvaluations, 1, 159);
        var dir = Path.Combine(Path.GetTempPath(), "flat-p4-" + Guid.NewGuid().ToString("N"));
        try
        {
            var library = new FlatStartRunLibrary(dir);
            await library.SaveAsync(source);
            var before = await File.ReadAllBytesAsync(library.PathFor(source.RunId));
            await library.SaveAsync(stopped.Checkpoint);
            var loaded = await library.LoadLastAsync();
            Assert.Equal(child.RunId, loaded!.RunId);
            var resumed = await new FlatStartSearchService().RunAsync(child.Specification, child.Options, checkpoint: loaded);
            Assert.Equal(160, resumed.Checkpoint.ChargedEvaluations);
            Assert.Equal(stopped.Checkpoint.Trials[0].Candidate!.CandidateId, resumed.Checkpoint.Trials[0].Candidate!.CandidateId);
            Assert.Equal(before, await File.ReadAllBytesAsync(library.PathFor(source.RunId)));
            var next = FlatStartSearchService.CreateRefinementCheckpoint(resumed.Checkpoint, resumed.Candidates[0].CandidateId, 20, TimeSpan.FromMinutes(1));
            FlatStartCheckpointValidation.Validate(next);
            Assert.Equal(0, next.ChargedEvaluations);
            Assert.Equal(child.RunId, next.Origin!.RunId);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task ChangedTargetsAndNonflatOriginCannotBeResumed()
    {
        var source = (await new FlatStartSearchService().RunAsync(Spec(), Options)).Checkpoint;
        var child = FlatStartSearchService.CreateRefinementCheckpoint(source, FlatStartSearchService.SelectCandidates(source)[0].CandidateId, 20, TimeSpan.FromMinutes(1));
        var changed = child.Specification with { FNumber = child.Specification.FNumber + 1 };
        await Assert.ThrowsAsync<InvalidDataException>(() => new FlatStartSearchService().RunAsync(changed, child.Options,
            checkpoint: child with { Specification = changed, SpecificationFingerprint = ContentFingerprint.Compute(changed) }));
        var root = child.Origin!.RootProof.Steps[0].Optic;
        var surfaces = root.Surfaces.ToList(); surfaces[1] = surfaces[1] with { Radius = 100 };
        var steps = child.Origin.RootProof.Steps.ToArray(); steps[0] = steps[0] with { Optic = root with { Surfaces = surfaces } };
        Assert.Throws<InvalidDataException>(() => FlatStartCheckpointValidation.Validate(child with
        { Origin = child.Origin with { RootProof = child.Origin.RootProof with { Steps = steps } } }));
        Assert.Throws<ArgumentException>(() => FlatStartSearchService.CreateRefinementCheckpoint(source, "not-a-candidate", 20, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task LegacyP3CheckpointOmitsOriginAndLibraryRejectsUnsafePointer()
    {
        var result = await new FlatStartSearchService().RunAsync(Spec(1), Options);
        var dir = Path.Combine(Path.GetTempPath(), "flat-p4-" + Guid.NewGuid().ToString("N"));
        try
        {
            var library = new FlatStartRunLibrary(dir);
            await library.SaveAsync(result.Checkpoint);
            var json = await File.ReadAllTextAsync(library.PathFor(result.Checkpoint.RunId));
            Assert.DoesNotContain("\"origin\"", json);
            Assert.Equal(result.Checkpoint.RunId, (await library.LoadLastAsync())!.RunId);
            await File.WriteAllTextAsync(Path.Combine(dir, "last-run.json"), JsonSerializer.Serialize("../outside"));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => library.LoadLastAsync());
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
