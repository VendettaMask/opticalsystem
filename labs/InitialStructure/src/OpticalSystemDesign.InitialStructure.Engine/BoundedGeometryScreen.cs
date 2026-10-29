using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

internal sealed record GeometryScreenResult(DesignTopology? Topology, string Outcome, string? Detail, AutonomousSearchWork Work);

/// <summary>Formal shared-Core manufacturing checks, with no optical rays or approximate geometry.</summary>
internal static class BoundedGeometryScreen
{
    internal static GeometryScreenResult Run(CompiledTopology anchor, BoundTopologyParameters? binding,
        IReadOnlyList<double>? proposal, TopologyTargets targets, int maximumAttempts, CancellationToken token)
    {
        var functions = 0; long probes = 0; string? failure = null;
        try
        {
            var initial = binding?.InitialCoordinates.ToArray();
            for (var attempt = 0; attempt < maximumAttempts; attempt++)
            {
                token.ThrowIfCancellationRequested(); functions++;
                try
                {
                    var candidate = binding is null ? anchor : TopologyParameterBinder.Apply(anchor, binding,
                        proposal!.Select((value, index) => initial![index] + Math.ScaleB(1.0, -attempt) * (value - initial[index])).ToArray());
                    if (binding is not null && candidate.BindingFingerprint == anchor.BindingFingerprint)
                        return Finish(null, "ScreenRejected", "The proposal is an unchanged prescription.");
                    var optic = candidate.CreateOptic(); var valid = true;
                    foreach (var row in TopologyEvaluationPlan.Geometry(candidate, targets, 64))
                    {
                        token.ThrowIfCancellationRequested(); probes++;
                        var value = TopologyEvaluationPlan.FormalMeasure(optic, row);
                        if (!double.IsFinite(value) || (row.Upper ? value > row.Limit : value < row.Limit))
                        { failure = row.Id; valid = false; break; }
                    }
                    if (valid) return Finish(candidate.Topology, "ScreenPassed", $"Accepted after {functions} formal geometry request(s).");
                }
                catch (Exception error) when (TopologyEvaluationPlan.DomainError(error)) { failure = error.Message; }
                if (binding is null) break;
            }
            return Finish(null, "ScreenRejected", failure);
        }
        catch (OperationCanceledException) { return Finish(null, "ScreenCancelled", "Proposal abandoned; completed screen work remains charged."); }

        GeometryScreenResult Finish(DesignTopology? topology, string outcome, string? detail) => new(topology, outcome, detail,
            new() { FunctionEvaluations = functions, GeometryProbes = probes });
    }
}
