using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.Engine;

internal sealed record SearchDecision(FamilyTrial? Parent, int Operation, string Reason);

/// <summary>Deterministic screening/promotion and stagnation policy, using only dense-validated candidate scores.</summary>
internal static class FlatStartScheduler
{
    public static long Seed(long seed, int decision) => unchecked(seed + 15485863L * (decision + 1));

    public static bool IsStagnant(FlatStartSearchCheckpoint checkpoint)
    {
        const int window = 48;
        if (checkpoint.Origin is not null || checkpoint.NextRootIndex < checkpoint.RootPlan.Count
            || checkpoint.Trials.Count < checkpoint.RootPlan.Count + window) return false;
        var recent = checkpoint.Trials.TakeLast(window).ToArray();
        if (recent.Count(trial => trial.Operation == "flat-root-retry") < 2) return false;
        var earlier = checkpoint.Trials.SkipLast(window).Where(trial => trial.Candidate is not null)
            .Select(trial => trial.Candidate!).ToArray();
        if (earlier.Length == 0) return !recent.Any(trial => trial.Candidate is not null);
        var best = earlier.OrderBy(candidate => FlatStartCandidateArchive.Rank(candidate.Status))
            .ThenBy(candidate => FlatStartCandidateArchive.Score(checkpoint.Specification, candidate)).First();
        return !recent.Any(trial => trial.Candidate is { } candidate
            && (FlatStartCandidateArchive.Rank(candidate.Status) < FlatStartCandidateArchive.Rank(best.Status)
                || candidate.Status == best.Status && FlatStartCandidateArchive.Score(checkpoint.Specification, candidate)
                    < FlatStartCandidateArchive.Score(checkpoint.Specification, best) * (1 - 1e-6)));
    }

    public static SearchDecision Decide(FlatStartSearchCheckpoint checkpoint)
    {
        var spec = checkpoint.Specification;
        var trials = checkpoint.Trials;
        var parents = trials.Where(trial => trial.Candidate?.Evaluation.EffectiveFocalLengthMillimeters > 0)
            .OrderBy(trial => FlatStartCandidateArchive.Rank(trial.Candidate!.Status))
            .ThenBy(trial => FlatStartCandidateArchive.Score(spec, trial.Candidate!)).ThenBy(trial => trial.Index)
            .DistinctBy(trial => FlatStartCandidateArchive.FamilyKey(trial.Candidate!)).ToArray();
        if (parents.Length == 0) return new(null, -1, "No traceable parent; explore a new strict-flat family.");
        // Give each retained family an opportunity before repeatedly spending on a
        // stationary parent. Progress creates a new parent, eligible for promotion.
        var parent = parents.OrderBy(trial => Children(trial).Length == 0 ? 0 : 1)
            .ThenBy(trial => FlatStartCandidateArchive.Rank(trial.Candidate!.Status))
            .ThenBy(trial => FlatStartCandidateArchive.Score(spec, trial.Candidate!) + Math.Log(1 + Children(trial).Length))
            .ThenBy(trial => trial.Index).First();
        var children = Children(parent);
        if (children.Length == 0) return new(parent, 0, "Promote a distinct screened family to dense refinement.");
        var order = parent.Candidate!.Evaluation.ValidRayFraction < spec.FlatStart!.MinimumValidRayFraction
            ? new[] { 3, 4, 1, 2, 0 } : new[] { 4, 1, 3, 2, 0 };
        if (checkpoint.UsableGlassNames.Count == 1) order = order.Where(operation => operation is not (1 or 2)).ToArray();
        foreach (var operation in order)
            if (!children.Any(child => Operation(child.Operation) == operation))
                return new(parent, operation, "Parent stalled; try an unvisited structural neighborhood.");
        var parentScore = FlatStartCandidateArchive.Score(spec, parent.Candidate);
        var improving = children.Where(child => child.Candidate is { } candidate &&
            (FlatStartCandidateArchive.Rank(candidate.Status) < FlatStartCandidateArchive.Rank(parent.Candidate.Status)
             || FlatStartCandidateArchive.Rank(candidate.Status) == FlatStartCandidateArchive.Rank(parent.Candidate.Status)
                && FlatStartCandidateArchive.Score(spec, candidate) < parentScore * (1 - 1e-6))).ToArray();
        if (improving.Length == 0)
            return new(null, -1, "All available neighborhoods stalled; restart from a new strict-flat root.");
        var chosen = order.OrderByDescending(operation =>
        {
            var visits = children.Where(child => Operation(child.Operation) == operation).ToArray();
            var reward = visits.Sum(child => child.Candidate is { } candidate
                ? Math.Max(0, (parentScore - FlatStartCandidateArchive.Score(spec, candidate)) / Math.Max(1, parentScore))
                    / Math.Max(1, child.TracedRealRayCount / (double)Math.Max(1, parent.TracedRealRayCount)) : 0) / Math.Max(1, visits.Length);
            return reward + Math.Sqrt(2 * Math.Log(1 + children.Length) / (1 + visits.Length));
        }).First();
        return new(parent, chosen, "Allocate by improvement per traced-ray cost and unexplored opportunities.");

        FamilyTrial[] Children(FamilyTrial trial) => trials.Where(child => child.ParentTrialId == trial.TrialId).ToArray();
    }

    public static int Allocation(InitialStructureSpecification spec, FlatStartSearchOptions options, string operation, int elements)
    {
        if (operation is "refine" or "refine-selected") return options.MaximumEvaluationsPerTrial;
        var dimension = 4 * elements - (spec.FlatStart!.FixedBackFocusMillimeters.HasValue ? 1 : 0);
        return Math.Min(options.MaximumEvaluationsPerTrial, 4 * (2 * dimension + 1));
    }

    public static int Operation(string name) => name switch
    { "refine" => 0, "glass-swap" => 1, "glass-pair-swap" => 2, "stop-surface-change" => 3, "parameter-perturbation" => 4, _ => -1 };
}
