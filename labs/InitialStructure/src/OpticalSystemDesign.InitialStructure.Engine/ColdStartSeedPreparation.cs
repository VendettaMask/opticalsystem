using System.Collections.Immutable;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>Explicit preparation dispatch. Unqualified or over-budget structures cannot enter the automatic search.</summary>
internal static class ColdStartSeedPreparation
{
    internal static ParaxialSeedSearchResult Run(AutonomousSearchPlan plan, ParaxialSeedSearchOptions options,
        CancellationToken token, Action<int, int>? progress)
    {
        _ = AutonomousSearchService.Create("cold-preparation-check", plan);
        if (AutonomousSearchService.IsHistoricalReadOnlyPlan(plan))
            throw new NotSupportedException("Historical optimizer records cannot execute a new cold construction protocol.");
        var events = 0;
        var cold = ColdStartDesignSearch.Run(plan, options, options.ColdStart!, token,
            _ => progress?.Invoke(++events, 0));
        var withinBudget = cold.Work.FunctionEvaluations <= options.MaximumFunctionEvaluations
            && cold.Work.GradientEvaluations <= options.ColdStart!.MaximumGradientEvaluations
            && cold.Work.TracedRays <= options.MaximumTracedRays && cold.Work.Elapsed <= options.TimeLimit;
        var eligible = cold.Qualified && withinBudget && cold.StopReason != "Cancelled" && !token.IsCancellationRequested;
        progress?.Invoke(events, cold.Work.FunctionEvaluations);
        return new()
        {
            Algorithm = "initial-structure-cold-design-v" + cold.Policy.Version, ProtocolFingerprint = cold.ProtocolFingerprint,
            SourcePlan = plan, Options = options, ColdStart = cold, Work = cold.Work, MatrixRequests = cold.Work.MatrixEvaluations,
            Termination = token.IsCancellationRequested || cold.StopReason == "Cancelled" ? ParaxialSeedTermination.Cancelled
                : cold.StopReason == "TimeLimit" || cold.Work.Elapsed > options.TimeLimit ? ParaxialSeedTermination.TimeLimit
                : cold.StopReason is "FunctionBudget" or "RayBudget" ? ParaxialSeedTermination.BudgetExhausted : ParaxialSeedTermination.Completed,
            Seeds = eligible ? [cold.Endpoint!] : [],
            Attempts = cold.Construction.Select(c => new ParaxialSeedAttempt(c.Index,
                ContentFingerprint.Compute(cold.ColdPlan), c.Outcome, c.Work.FunctionEvaluations, null, c.Topology)).ToImmutableArray()
        };
    }
}
