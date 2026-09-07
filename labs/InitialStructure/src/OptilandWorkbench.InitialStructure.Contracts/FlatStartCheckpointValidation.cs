using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.InitialStructure.Contracts;

/// <summary>Structural validation shared by persistence and search; performs no optical evaluation.</summary>
public static class FlatStartCheckpointValidation
{
    public static void Validate(FlatStartSearchCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (checkpoint.SchemaVersion != 1 || checkpoint.Algorithm != new AlgorithmIdentity("strict-flat-family-search", "1", "Managed CPU", true)
            || checkpoint.Specification?.FlatStart is null || checkpoint.Specification.Budget is null || checkpoint.Options is null
            || checkpoint.RootPlan is not { Count: <= 128 } || checkpoint.Trials is not { Count: <= 256 }
            || checkpoint.UsableGlassNames is not { Count: <= 64 } || checkpoint.Diagnostics is null
            || checkpoint.NextRootIndex < 0 || checkpoint.NextRootIndex > checkpoint.RootPlan.Count
            || checkpoint.NextRefinementIndex < 0 || checkpoint.NextRefinementIndex > 256
            || checkpoint.NextRootIndex + checkpoint.NextRefinementIndex != checkpoint.Trials.Count
            || checkpoint.RootEvaluationQuota < 0 || checkpoint.RootEvaluationQuota > checkpoint.Options.MaximumEvaluationsPerTrial
            || checkpoint.ElapsedComputeTicks < 0 || checkpoint.ReservedComputeTicks < 0
            || checkpoint.ElapsedComputeTicks > 2 * InitialStructureLimits.MaximumTimeLimit.Ticks
            || checkpoint.ReservedComputeTicks > InitialStructureLimits.MaximumTimeLimit.Ticks
            || !Enum.IsDefined(checkpoint.State)
            || string.IsNullOrWhiteSpace(checkpoint.RunId) || checkpoint.RunId.Length > 128
            || !Hash(checkpoint.SpecificationFingerprint) || !Hash(checkpoint.OptionsFingerprint) || !Hash(checkpoint.MaterialFingerprint))
            throw new InvalidDataException("Invalid flat-family checkpoint header or counters.");
        long charged = 0;
        var parents = new Dictionary<string, FamilyTrial>(StringComparer.Ordinal);
        for (var index = 0; index < checkpoint.Trials.Count; index++)
        {
            var trial = checkpoint.Trials[index];
            if (trial is null || trial.Index != index || trial.TrialId != $"trial-{index:D4}" || !Enum.IsDefined(trial.State)
                || trial.AllocatedEvaluations < 1 || trial.AllocatedEvaluations > checkpoint.Options.MaximumEvaluationsPerTrial
                || trial.ChargedEvaluations < 0 || trial.ChargedEvaluations > trial.AllocatedEvaluations
                || trial.TracedRealRayCount < 0 || trial.Diagnostics is null || trial.Stages is null
                || trial.Family?.GlassNames is not { Count: > 0 and <= 8 }
                || trial.Family.GlassNames.Any(name => !checkpoint.UsableGlassNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                || trial.Family.CenterThicknesses?.Count != trial.Family.ElementCount
                || trial.Family.AirGaps?.Count != trial.Family.ElementCount - 1
                || trial.Family.StopSurfaceIndex < 1 || trial.Family.StopSurfaceIndex > 2 * trial.Family.ElementCount)
                throw new InvalidDataException("Invalid flat-family trial or material allocation.");
            charged += trial.ChargedEvaluations;
            if (trial.State == FamilyTrialState.Completed)
            {
                if (trial.CompletedEvaluations is not > 0 || trial.CompletedEvaluations != trial.ChargedEvaluations
                    || trial.FlatRoot is null || trial.DesignState is null)
                    throw new InvalidDataException("Completed work needs exact evaluation accounting and flat-root proof.");
                OpticSnapshotValidator.Validate(trial.FlatRoot);
                if (trial.FlatRoot.Surfaces.Any(surface => surface.Radius != 0))
                    throw new InvalidDataException("A recorded flat root contains nonzero curvature.");
            }
            else if (trial.CompletedEvaluations is not null || trial.ChargedEvaluations != trial.AllocatedEvaluations
                || trial.Candidate is not null || trial.TracedRealRayCount != 0)
                throw new InvalidDataException("Unknown work must retain its reserved quota without fabricated results.");
            FamilyTrial? parent = null;
            if (trial.ParentTrialId is { } parentId && (!parents.TryGetValue(parentId, out parent) || parent.Candidate is null))
                throw new InvalidDataException("A refinement must reference an earlier completed candidate.");
            if (trial.State == FamilyTrialState.Completed && parent is null
                && (trial.BootstrapProof?.Steps is not { Count: > 0 } || trial.BootstrapProof.Steps[0].Optic.Surfaces.Any(surface => surface.Radius != 0)))
                throw new InvalidDataException("A root trial needs a recorded strict-flat bootstrap.");
            if (trial.Candidate is { } candidate && (candidate.Lineage is null
                || candidate.Lineage.ParentCandidateId != parent?.Candidate?.CandidateId
                || trial.FinalValidation is null || candidate.Status == CandidateStatus.LabAccepted && !trial.FinalValidation.MeetsTargets))
                throw new InvalidDataException("A candidate has inconsistent lineage or no full-target validation.");
            parents.Add(trial.TrialId, trial);
        }
        if (charged > checkpoint.Specification.Budget.MaximumEvaluations
            || checkpoint.Trials.Any(trial => trial.State == FamilyTrialState.Reserved) != (checkpoint.ReservedComputeTicks > 0))
            throw new InvalidDataException("Invalid reserved evaluation/time accounting.");
    }

    private static bool Hash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
