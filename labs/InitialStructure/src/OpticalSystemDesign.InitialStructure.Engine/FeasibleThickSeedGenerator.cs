using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>Multiple native thick realizations with hard geometry before restoration.
/// Every optical value and clearance comes from the unchanged shared Core.</summary>
internal static class FeasibleThickSeedGenerator
{
    internal static DesignTopology Create(AutonomousSearchPlan plan, StructureGenerationOptions descriptor,
        ref DeterministicRandom random, Action charge, StructureGenerationWork work, CancellationToken token)
    {
        var search = descriptor.FeasibleLayouts!;
        var partitions = search.StopAfterElementCandidates.IsEmpty
            ? [descriptor.StopAfterElement] : search.StopAfterElementCandidates.ToArray();
        DesignTopology? best = null;
        double? bestMargin = null;
        int? bestPartition = null;
        // The declared annealing budget is shared across layouts, not multiplied.
        for (var layout = 0; layout < search.MaximumLayouts; layout++)
        {
            token.ThrowIfCancellationRequested();
            charge(); work.AttemptedLayouts++;
            var partition = partitions[layout % partitions.Length];
            var proposals = descriptor.AnnealingProposals / search.MaximumLayouts
                + (layout < descriptor.AnnealingProposals % search.MaximumLayouts ? 1 : 0);
            try
            {
                var topology = ThickStructureSeedGenerator.Create(plan,
                    descriptor with { StopAfterElement = partition, AnnealingProposals = proposals },
                    ref random, charge, work, token, requireFeasibleGeometry: true);
                work.FeasibleLayouts++;
                var margin = work.MinimumClearanceMarginMillimeters!.Value;
                if (best is null || margin > bestMargin)
                {
                    best = topology with { Name = "Feasible Thick Delano" };
                    bestMargin = margin;
                    bestPartition = partition;
                }
            }
            catch (Exception error) when (TopologyEvaluationPlan.DomainError(error)
                && !Core.Raytrace.SequentialTraceBudgetExceededException.IsBudgetException(error))
            {
                // An impossible fixed-stop partition or a failed sag domain is
                // a rejected layout. Budget and cancellation exceptions propagate.
            }
        }
        work.MinimumClearanceMarginMillimeters = bestMargin;
        work.SelectedStopAfterElement = bestPartition;
        return best ?? throw new ArithmeticException("No thick layout passed the original geometry and first-order constraints within the declared budget.");
    }
}
