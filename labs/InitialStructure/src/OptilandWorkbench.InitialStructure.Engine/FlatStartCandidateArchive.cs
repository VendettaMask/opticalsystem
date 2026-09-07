using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.Engine;

internal static class FlatStartCandidateArchive
{
    public static IReadOnlyList<CandidateSnapshot> Select(InitialStructureSpecification specification,
        FlatStartSearchOptions options, IEnumerable<FamilyTrial> trials)
    {
        var ranked = trials.Where(trial => trial.Candidate is not null).Select(trial => trial.Candidate!)
            .OrderBy(candidate => Rank(candidate.Status)).ThenBy(candidate => Score(specification, candidate))
            .ThenBy(candidate => candidate.CandidateId, StringComparer.Ordinal)
            .DistinctBy(candidate => candidate.OpticFingerprint).ToArray();
        var selected = new List<CandidateSnapshot>();
        // Choose genuinely different glass/stop/element families before additional members of a family.
        foreach (var category in ranked.GroupBy(candidate => Rank(candidate.Status)).OrderBy(group => group.Key))
        {
            var seenFamilies = selected.Select(FamilyKey).ToHashSet(StringComparer.Ordinal);
            foreach (var candidate in category)
                if (seenFamilies.Add(FamilyKey(candidate)) && selected.Count < options.MaximumDisplayedCandidates) selected.Add(candidate);
            foreach (var candidate in category)
                if (selected.Count < options.MaximumDisplayedCandidates && !selected.Contains(candidate)
                    && selected.Where(other => FamilyKey(other) == FamilyKey(candidate))
                        .All(other => Distance(specification, candidate, other) >= options.MinimumStructuralDistance)) selected.Add(candidate);
        }
        return selected;
    }

    public static int Rank(CandidateStatus status) => status switch
    { CandidateStatus.LabAccepted => 0, CandidateStatus.TraceValid => 1, _ => 2 };

    public static double Score(InitialStructureSpecification specification, CandidateSnapshot candidate)
    {
        var evaluation = candidate.Evaluation;
        return (evaluation.RmsSpotRadiusMillimeters ?? 1e6) / specification.MaximumRmsSpotRadiusMillimeters
            + (evaluation.MaximumSpotRadiusMillimeters ?? 1e6) / specification.MaximumSpotRadiusMillimeters
            + 100 * (1 - evaluation.ValidRayFraction)
            + (evaluation.EffectiveFocalLengthMillimeters is { } focal ? Math.Abs(focal / specification.EffectiveFocalLengthMillimeters - 1) : 1e6);
    }

    internal static string FamilyKey(CandidateSnapshot candidate) => string.Join("|",
        candidate.Optic.Surfaces.Count,
        candidate.Optic.Surfaces.FindIndex(surface => surface.IsStop),
        string.Join(",", candidate.Optic.Surfaces.Skip(1).Where((_, index) => index % 2 == 0)
            .Take(candidate.Lineage.ElementCount).Select(surface => surface.Material.ToUpperInvariant())));

    private static double Distance(InitialStructureSpecification specification, CandidateSnapshot a, CandidateSnapshot b)
    {
        var sum = 0.0;
        for (var index = 1; index < a.Optic.Surfaces.Count - 1; index++)
        {
            var left = a.Optic.Surfaces[index];
            var right = b.Optic.Surfaces[index];
            var curvature = (left.Radius == 0 ? 0 : specification.EffectiveFocalLengthMillimeters / left.Radius)
                - (right.Radius == 0 ? 0 : specification.EffectiveFocalLengthMillimeters / right.Radius);
            var thickness = (left.Thickness - right.Thickness) / specification.MaximumTrackLengthMillimeters;
            sum += curvature * curvature + thickness * thickness;
        }
        return Math.Sqrt(sum / (2 * (a.Optic.Surfaces.Count - 2)));
    }
}
