using System.Collections.Immutable;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>Pure selection over persisted Core assessments and prescription coordinates; no hidden optical work.</summary>
internal static class TopologyDiversitySelection
{
    internal static ImmutableArray<string> Select(AutonomousSearchCheckpoint state)
    {
        var policy = state.Plan.Options;
        var priority = Comparer<AutonomousSearchCandidate>.Create((a, b) => policy.BoundedSearch is not null
            ? a.Assessment.MeetsTargets && b.Assessment.MeetsTargets ? AutonomousSearchService.CompareQualified(a, b)
                : a.Assessment.MeetsTargets != b.Assessment.MeetsTargets ? b.Assessment.MeetsTargets.CompareTo(a.Assessment.MeetsTargets) : a.Score.CompareTo(b.Score)
            : a.Score.CompareTo(b.Score));
        var eligible = state.Candidates.Where(candidate => policy.QualifiedReductionOnly != true || candidate.Assessment.MeetsTargets);
        if (policy.PoolDiversity is null)
            return eligible.GroupBy(candidate => candidate.Topology.Elements.Length)
                .SelectMany(group => group.OrderBy(candidate => candidate, priority).ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
                    .Take(policy.CandidatesPerElementCount)).Select(candidate => candidate.Id).ToImmutableArray();
        var result = ImmutableArray.CreateBuilder<string>();
        foreach (var group in eligible.GroupBy(candidate => candidate.Topology.Elements.Length))
        {
            var pool = group.OrderByDescending(candidate => candidate.Assessment.MeetsTargets)
                .ThenBy(candidate => candidate, priority).ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
                .DistinctBy(candidate => PrescriptionIdentity(candidate.Topology))
                .Take(policy.PoolDiversity.RankWindow).ToList();
            var selected = new List<AutonomousSearchCandidate>();
            while (pool.Count > 0 && selected.Count < policy.CandidatesPerElementCount)
            {
                var next = selected.Count == 0 ? pool[0] : pool.OrderByDescending(candidate => candidate.Assessment.MeetsTargets)
                    .ThenByDescending(candidate => selected.Min(chosen => Distance(candidate.Topology, chosen.Topology, state.Plan.Targets)))
                    .ThenBy(candidate => candidate.Score).ThenBy(candidate => candidate.Id, StringComparer.Ordinal).First();
                selected.Add(next); pool.Remove(next); result.Add(next.Id);
            }
        }
        return result.ToImmutable();
    }

    internal static string PrescriptionIdentity(DesignTopology topology) => ContentFingerprint.Compute(new
    {
        Elements = topology.Elements.Select(element => new
        {
            element.FrontZMillimeters,
            element.ThicknessMillimeters,
            element.Material,
            element.Front,
            element.Back,
            element.Protected,
            element.Transition
        }).ToArray()
    });

    internal static double Distance(DesignTopology left, DesignTopology right, TopologyTargets targets)
    {
        if (left.Elements.Length != right.Elements.Length) return double.PositiveInfinity;
        double sum = 0;
        for (var index = 0; index < left.Elements.Length; index++)
        {
            var a = left.Elements[index]; var b = right.Elements[index];
            Add((a.FrontZMillimeters - b.FrontZMillimeters) / targets.EffectiveFocalLengthMillimeters);
            Add((a.ThicknessMillimeters - b.ThicknessMillimeters) / targets.EffectiveFocalLengthMillimeters);
            Add((a.Front.Profile.CurvaturePerMillimeter - b.Front.Profile.CurvaturePerMillimeter) * targets.EffectiveFocalLengthMillimeters);
            Add((a.Back.Profile.CurvaturePerMillimeter - b.Back.Profile.CurvaturePerMillimeter) * targets.EffectiveFocalLengthMillimeters);
            Add((a.Front.Profile.Conic - b.Front.Profile.Conic) / 2);
            Add((a.Back.Profile.Conic - b.Back.Profile.Conic) / 2);
            Add(string.Equals(a.Material, b.Material, StringComparison.OrdinalIgnoreCase) ? 0 : 1);
        }
        return sum;
        void Add(double difference) { var bounded = Math.Clamp(difference, -10, 10); sum += bounded * bounded; }
    }
}
