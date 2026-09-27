using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.Engine;

/// <summary>Screen all planned forms first, then share refinement across retained branch lineages.</summary>
internal static class DesignFormScheduler
{
    public static SearchDecision Decide(FlatStartSearchCheckpoint checkpoint)
    {
        var spec = checkpoint.Specification;
        var trials = checkpoint.Trials;
        var branchById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var trial in trials)
            branchById[trial.TrialId] = trial.ParentTrialId is { } parent ? branchById[parent] : trial.TrialId;
        var branches = trials.Where(trial => trial.Candidate is not null)
            .GroupBy(trial => branchById[trial.TrialId])
            .Select(group => new
            {
                Root = group.Key,
                Best = group.OrderBy(trial => FlatStartCandidateArchive.Rank(trial.Candidate!.Status))
                    .ThenBy(trial => FlatStartCandidateArchive.Score(spec, trial.Candidate!)).ThenBy(trial => trial.Index).First(),
                History = trials.Where(trial => branchById[trial.TrialId] == group.Key && trial.ParentTrialId is not null).ToArray()
            })
            .OrderBy(branch => FlatStartCandidateArchive.Rank(branch.Best.Candidate!.Status))
            .ThenBy(branch => FlatStartCandidateArchive.Score(spec, branch.Best.Candidate!))
            .ThenBy(branch => branch.Best.Index)
            .DistinctBy(branch => FlatStartCandidateArchive.FamilyKey(branch.Best.Candidate!))
            .Take(checkpoint.Options.MaximumDisplayedCandidates).ToArray();
        if (branches.Length == 0) return new(null, -1, "No evaluated form; retry a recorded binary starting direction.");
        if (FlatStartAlgorithm.SchedulingPolicy(checkpoint.Algorithm.Version, spec) == 2)
        {
            var enabled = NeighborhoodBudget.Operations(spec, checkpoint.Options, checkpoint.UsableGlassNames.Count)
                .Where(operation => operation != 2 || branches.Any(branch => branch.Best.Family.ElementCount > 1));
            // Cycle across operator types globally before repeating one type on every branch.
            // Reservations/failures count as attempts; an improving child cannot erase history.
            var next = enabled.OrderBy(operation => trials.Count(trial => trial.ParentTrialId is not null
                && FlatStartScheduler.Operation(trial.Operation) == operation)).First();
            var branch = branches.Where(item => next != 2 || item.Best.Family.ElementCount > 1)
                .OrderBy(item => item.History.Count(trial => FlatStartScheduler.Operation(trial.Operation) == next)).First();
            return new(branch.Best, next, $"Mixed neighborhood round: operator {next}, branch {branch.Root}; preserve budget for unvisited operators and reoptimize at full targets.");
        }
        var chosen = branches.OrderBy(branch => branch.History.Length).First();
        var operations = new List<int> { 0, 4 };
        if (checkpoint.UsableGlassNames.Count > 1) operations.AddRange([1, 2]);
        if ((checkpoint.Options.DesignSearch ?? new()).ExploreStopPositions) operations.Add(3);
        // History belongs to the root branch, not its newest improving child.
        // Each operator must receive a turn before another refinement of that branch.
        var operation = operations.OrderBy(op => chosen.History.Count(trial => FlatStartScheduler.Operation(trial.Operation) == op)).First();
        return new(chosen.Best, operation, $"Form {chosen.Best.Family.BinaryStart?.Signs ?? "historical"}, branch {chosen.Root}; {chosen.History.Length} completed/reserved neighborhood turns. Promote and explore retained forms in rounds.");
    }
}
