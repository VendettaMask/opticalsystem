using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.Engine;

/// <summary>Evaluation accounting only; no optical approximations or target changes.</summary>
internal static class NeighborhoodBudget
{
    public static int[] Operations(InitialStructureSpecification spec, FlatStartSearchOptions options, int glasses) =>
        new[] { 0, 1, 3, 4, 2 }.Where(operation => operation switch
        {
            1 => glasses > 1,
            2 => glasses > 1 && spec.MaximumElementCount > 1,
            3 => (options.DesignSearch ?? new()).ExploreStopPositions,
            _ => true
        }).ToArray();

    public static int Nominal(InitialStructureSpecification spec, FlatStartSearchOptions options, int operation, int elements) =>
        operation == 0 ? options.MaximumEvaluationsPerTrial
        : Math.Min(options.MaximumEvaluationsPerTrial, 6 * (2 * Dimension(spec, elements) + 1));

    // Entry + solver entry + a two-sided Jacobian + one trial + independent final validation.
    public static int Minimum(InitialStructureSpecification spec, FlatStartSearchOptions options, int elements) =>
        Math.Min(options.MaximumEvaluationsPerTrial, 2 * Dimension(spec, elements) + 4);

    public static int RootReserve(InitialStructureSpecification spec, FlatStartSearchOptions options) =>
        (int)Math.Min(2L * spec.Budget.MaximumEvaluations / 5,
            Operations(spec, options, options.AllowedGlassNames.Distinct(StringComparer.OrdinalIgnoreCase).Count())
                .Sum(operation => (long)Nominal(spec, options, operation, spec.MaximumElementCount)));

    public static int Allocate(FlatStartSearchCheckpoint checkpoint, int operation, int elements, int remaining)
    {
        var spec = checkpoint.Specification;
        var options = checkpoint.Options;
        var visits = checkpoint.Trials.Where(trial => trial.ParentTrialId is not null)
            .Select(trial => FlatStartScheduler.Operation(trial.Operation)).ToHashSet();
        var unvisited = Operations(spec, options, checkpoint.UsableGlassNames.Count)
            .Where(other => other != operation && !visits.Contains(other)).ToArray();
        var nominal = Nominal(spec, options, operation, elements);
        if (unvisited.Length == 0) return Math.Min(remaining, nominal);
        var minimum = Minimum(spec, options, elements);
        var reserve = unvisited.Length * Minimum(spec, options, spec.MaximumElementCount);
        if (remaining < minimum + reserve)
            return Math.Min(nominal, Math.Max(1, remaining / (unvisited.Length + 1)));
        var total = nominal + unvisited.Sum(other => (long)Nominal(spec, options, other, spec.MaximumElementCount));
        var share = (int)((long)remaining * nominal / total);
        return Math.Min(Math.Min(nominal, remaining - reserve), Math.Max(minimum, share));
    }

    private static int Dimension(InitialStructureSpecification spec, int elements) =>
        4 * elements - (spec.FlatStart!.FixedBackFocusMillimeters.HasValue ? 1 : 0);
}
