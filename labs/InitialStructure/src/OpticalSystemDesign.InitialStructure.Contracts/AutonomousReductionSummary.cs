namespace OpticalSystemDesign.InitialStructure.Contracts;

/// <summary>Finite execution outcomes, never a proof of global minimum element count or physical infeasibility.</summary>
public enum AutonomousReductionStopReason
{
    InProgress, Cancelled, BudgetExhausted, NoQualifiedSource, MinimumAllowedCountReached,
    TrialLimitReached, ConfiguredBranchesExhausted
}

public sealed record AutonomousReductionSummary(int? MinimumVerifiedElementCount, string? QualifiedCandidateId,
    AutonomousReductionStopReason Reason, bool HasPendingWork, int RetainedUnqualifiedChildren);
