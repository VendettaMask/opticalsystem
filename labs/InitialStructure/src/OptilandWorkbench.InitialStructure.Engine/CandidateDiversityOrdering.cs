using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.Engine;

internal static class CandidateDiversityOrdering
{
    private const int MaximumDiversityPool = 64;
    private const int MaximumDiversityRepresentatives = 8;

    public static IReadOnlyList<CandidateSnapshot> Order(
        InitialStructureSpecification specification,
        IEnumerable<CandidateSnapshot> candidates)
    {
        return candidates
            .GroupBy(candidate => (
                candidate.Lineage.ElementCount,
                candidate.Lineage.StopVariant))
            .OrderBy(group => group.Key.ElementCount)
            .ThenBy(group => group.Key.StopVariant)
            .SelectMany(group => OrderFamily(specification, group))
            .ToArray();
    }

    private static IReadOnlyList<CandidateSnapshot> OrderFamily(
        InitialStructureSpecification specification,
        IEnumerable<CandidateSnapshot> family)
    {
        var ranked = family
            .Select(candidate => new RankedCandidate(
                candidate,
                CandidateObjective.Score(
                    specification,
                    candidate.Evaluation,
                    candidate.Violations),
                OpticalVector(candidate, specification)))
            .OrderBy(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Candidate.CandidateId, StringComparer.Ordinal)
            .ToArray();
        if (ranked.Length <= 1)
        {
            return ranked.Select(candidate => candidate.Candidate).ToArray();
        }

        var pool = ranked.Take(MaximumDiversityPool).ToList();
        var selected = new List<RankedCandidate> { pool[0] };
        pool.RemoveAt(0);
        var representativeCount = Math.Min(MaximumDiversityRepresentatives, ranked.Length);
        while (selected.Count < representativeCount && pool.Count > 0)
        {
            var next = pool
                .Select(candidate => new
                {
                    Candidate = candidate,
                    Distance = selected.Min(chosen => Distance(candidate.OpticalVector, chosen.OpticalVector))
                })
                .OrderByDescending(item => item.Distance)
                .ThenBy(item => item.Candidate.Score)
                .ThenBy(item => item.Candidate.Candidate.CandidateId, StringComparer.Ordinal)
                .First()
                .Candidate;
            selected.Add(next);
            pool.Remove(next);
        }

        var selectedIds = selected
            .Select(candidate => candidate.Candidate.CandidateId)
            .ToHashSet(StringComparer.Ordinal);
        return selected
            .Concat(ranked.Where(candidate => !selectedIds.Contains(candidate.Candidate.CandidateId)))
            .Select(candidate => candidate.Candidate)
            .ToArray();
    }

    private static double[] OpticalVector(
        CandidateSnapshot candidate,
        InitialStructureSpecification specification)
    {
        var surfaces = candidate.Optic.Surfaces;
        var vector = new double[Math.Max(0, (surfaces.Count - 2) * 3)];
        var offset = 0;
        for (var index = 1; index + 1 < surfaces.Count; index++)
        {
            var surface = surfaces[index];
            var curvature = double.IsFinite(surface.Radius) && Math.Abs(surface.Radius) > 1e-12
                ? specification.EffectiveFocalLengthMillimeters / surface.Radius
                : 0;
            vector[offset++] = Math.Clamp(curvature, -10, 10);
            vector[offset++] = double.IsFinite(surface.Thickness)
                ? Math.Clamp(surface.Thickness / specification.MaximumTrackLengthMillimeters, 0, 1)
                : 0;
            vector[offset++] = double.IsFinite(surface.SemiDiameter)
                ? Math.Clamp(
                    surface.SemiDiameter
                    / Math.Max(1e-9, specification.EffectiveFocalLengthMillimeters / (2 * specification.FNumber)),
                    0,
                    10)
                : 0;
        }

        return vector;
    }

    private static double Distance(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        var count = Math.Min(left.Count, right.Count);
        if (count == 0)
        {
            return 0;
        }

        var sum = 0.0;
        for (var index = 0; index < count; index++)
        {
            var difference = left[index] - right[index];
            sum += difference * difference;
        }

        return Math.Sqrt(sum / count);
    }

    private sealed record RankedCandidate(
        CandidateSnapshot Candidate,
        double Score,
        double[] OpticalVector);
}
