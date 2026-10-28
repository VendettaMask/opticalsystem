using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

public static partial class AutonomousSearchService
{
    /// <summary>Read-only outcome of an explicit bounded reduction. Does not dispatch or revalidate optics.</summary>
    public static AutonomousReductionSummary ReductionSummary(AutonomousSearchCheckpoint checkpoint)
    {
        Validate(checkpoint);
        if (checkpoint.Plan.Options.QualifiedReductionOnly != true)
            throw new ArgumentException("A reduction conclusion requires an explicit qualified-only reduction plan.");
        var best = checkpoint.AcceptedArchive.OrderBy(candidate => candidate.Topology.Elements.Length)
            .ThenBy(candidate => candidate.Score).ThenBy(candidate => candidate.Id, StringComparer.Ordinal).FirstOrDefault();
        var reason = checkpoint.Status switch
        {
            AutonomousSearchStatus.Ready or AutonomousSearchStatus.Running => AutonomousReductionStopReason.InProgress,
            AutonomousSearchStatus.Cancelled => AutonomousReductionStopReason.Cancelled,
            AutonomousSearchStatus.BudgetExhausted => AutonomousReductionStopReason.BudgetExhausted,
            _ when best is null => AutonomousReductionStopReason.NoQualifiedSource,
            _ when best.Topology.Elements.Length == checkpoint.Plan.Constraints.MinimumElementCount => AutonomousReductionStopReason.MinimumAllowedCountReached,
            _ when checkpoint.TrialCursor >= checkpoint.Plan.Options.MaximumTrials => AutonomousReductionStopReason.TrialLimitReached,
            _ => AutonomousReductionStopReason.ConfiguredBranchesExhausted
        };
        return new(best?.Topology.Elements.Length, best?.Id, reason, checkpoint.Pending is not null,
            checkpoint.Candidates.Count(candidate => candidate.ParentId is not null && !candidate.Assessment.MeetsTargets));
    }
}
