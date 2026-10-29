using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

public static partial class AutonomousSearchService
{
    public static QualifiedStructureComplexity Complexity(DesignTopology topology)
    {
        var faces = topology.Elements.SelectMany(e => new[] { e.Front.Profile, e.Back.Profile }).ToArray();
        return new(topology.Elements.Length, faces.Count(f => f.Kind == TopologyProfileKind.EvenAsphere || f.Conic != 0),
            faces.Sum(f => f.Terms.Count(t => t.Coefficient != 0)));
    }

    public static int CompareQualified(AutonomousSearchCandidate left, AutonomousSearchCandidate right)
    {
        if (!left.Assessment.MeetsTargets || !right.Assessment.MeetsTargets)
            throw new ArgumentException("Complexity ranking requires two independently qualified candidates.");
        var a = Complexity(left.Topology); var b = Complexity(right.Topology);
        var result = a.Elements.CompareTo(b.Elements);
        if (result == 0) result = a.AsphericSurfaces.CompareTo(b.AsphericSurfaces);
        if (result == 0) result = a.PolynomialTerms.CompareTo(b.PolynomialTerms);
        if (result == 0) result = left.Score.CompareTo(right.Score);
        return result;
    }

    public static BoundedCandidateSearchSummary BoundedSummary(AutonomousSearchCheckpoint state)
    {
        Validate(state);
        if (state.Plan.Options.BoundedSearch is null) throw new ArgumentException("No bounded candidate policy is recorded.");
        var best = BestQualified(state.AcceptedArchive);
        var completed = CompletedDecisions(state);
        var failures = 0;
        var incumbent = BestQualified(state.AcceptedArchive.Where(c => c.ParentId is null));
        var evaluated = 0;
        foreach (var trial in completed)
        {
            var candidate = state.AcceptedArchive.FirstOrDefault(c => c.Id == trial.Key);
            var effective = trial.Any(e => e.Work.FunctionEvaluations > 0
                && e.Phase is AutonomousSearchPhase.Recovery or AutonomousSearchPhase.Local or AutonomousSearchPhase.Full);
            if (effective) evaluated++;
            if (candidate is not null && (incumbent is null || CompareQualified(candidate, incumbent) < 0
                && (Complexity(candidate.Topology) != Complexity(incumbent.Topology)
                    || candidate.Score < incumbent.Score * (1 - state.Plan.Options.BoundedSearch.MinimumRelativeQualityGain))))
            { incumbent = candidate; failures = 0; }
            else if (effective) failures++;
        }
        return new(best?.Id, best is null ? null : Complexity(best.Topology), state.StopReason,
            state.Events.Count(e => e.Outcome is "ScreenPassed" or "ScreenRejected"),
            state.Events.Count(e => e.Outcome == "ScreenRejected"), evaluated, failures);
    }

    internal static AutonomousSearchCandidate? BestQualified(IEnumerable<AutonomousSearchCandidate> candidates)
        => candidates.Where(c => c.Assessment.MeetsTargets).OrderBy(c => c, Comparer<AutonomousSearchCandidate>.Create(CompareQualified))
            .ThenBy(c => c.Id, StringComparer.Ordinal).FirstOrDefault();

    private static IGrouping<string, AutonomousSearchEvent>[] CompletedDecisions(AutonomousSearchCheckpoint state)
        => state.Events.Where(e => e.ParentId is not null && e.TrialId != state.Pending?.TrialId)
            .GroupBy(e => e.TrialId).OrderBy(g => g.Min(e => e.Sequence)).ToArray();

    private static BoundedSearchStopReason? BoundedStop(AutonomousSearchCheckpoint state)
    {
        var policy = state.Plan.Options.BoundedSearch;
        if (policy is null || state.Pending is not null || state.SeedCursor < state.Plan.Seeds.Length) return null;
        if (state.TrialCursor >= state.Plan.Options.MaximumTrials) return BoundedSearchStopReason.TrialLimit;
        // Complete-trial minimum includes one screen and one independent validation.
        if (state.Plan.Options.MaximumTracedRays - state.Consumed.TracedRays < state.Plan.Options.FinalValidationRayReserve + policy.MinimumCompensationRays
            || state.Plan.Options.MaximumFunctionEvaluations - state.Consumed.FunctionEvaluations < policy.MinimumCompensationFunctions + 2
            || state.Plan.Options.MaximumGradientEvaluations - state.Consumed.GradientEvaluations < policy.MinimumCompensationGradients)
            return BoundedSearchStopReason.InsufficientCompleteTrialBudget;
        if (BoundedSummary(state).ConsecutiveUnsuccessfulTrials >= policy.MaximumUnsuccessfulTrials) return BoundedSearchStopReason.NoImprovement;
        return null;
    }

    private static bool LayerAvailable(AutonomousSearchCheckpoint state, AutonomousSearchCandidate parent)
        => state.Plan.Options.BoundedSearch is not { } policy || CompletedDecisions(state).Count(g =>
            state.Candidates.Single(c => c.Id == g.First().ParentId).Topology.Elements.Length == parent.Topology.Elements.Length) < policy.MaximumAttemptsPerElementCount;

    private static void ValidateBoundedOptions(BoundedCandidateSearchOptions policy)
    {
        if (policy.Version != 1 || policy.MaximumAttemptsPerElementCount is < 1 or > 128
            || !double.IsFinite(policy.MinimumRelativeQualityGain) || policy.MinimumRelativeQualityGain is < 0 or > .1
            || policy.MaximumUnsuccessfulTrials is < 1 or > 128 || policy.MinimumCompensationRays is < 1 or > 100_000_000
            || policy.MinimumCompensationFunctions is < 1 or > 1000 || policy.MinimumCompensationGradients is < 1 or > 1000)
            throw new ArgumentException("Invalid finite candidate screening/stopping policy.");
    }
}
