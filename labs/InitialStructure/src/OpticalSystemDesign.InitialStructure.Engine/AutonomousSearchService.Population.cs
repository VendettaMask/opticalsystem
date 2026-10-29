using System.Collections.Immutable;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

public static partial class AutonomousSearchService
{
    /// <summary>Current-to-pbest/binomial proposals alternate with the normal Local/Full stages.
    /// This is bounded DE exploration, not a claim of identical upstream SHADE execution.</summary>
    private static (AutonomousSearchCheckpoint State, string? Failure)? ProposePopulation(AutonomousSearchCheckpoint state)
    {
        var options = state.Plan.Options;
        var policy = options.PopulationExploration!;
        var pool = state.Pool.Select(id => state.Candidates.Single(candidate => candidate.Id == id))
            .Where(candidate => LayerAvailable(state, candidate))
            .OrderBy(candidate => candidate.Score).ThenBy(candidate => candidate.Id, StringComparer.Ordinal).ToArray();
        if (pool.Length == 0) return null;
        var random = DeterministicRandom.Restore(state.RandomState);
        var parent = pool[state.TrialCursor % pool.Length];
        var pending = AnchorAxialCompensation(new()
        {
            TrialId = "trial-" + state.TrialCursor,
            ParentId = parent.Id,
            Operation = "population-difference",
            Template = parent.Topology,
            Phase = AutonomousSearchPhase.Local
        }, options);
        string? failure = null;
        try
        {
            var source = TopologyCompiler.Compile(parent.Topology, state.Plan.Constraints);
            var binding = TopologyParameterBinder.Bind(source, ExpansionVariables(pending, options));
            if (binding.Slots.IsEmpty) throw new ArgumentException("No permitted population variables.");
            var coordinates = binding.InitialCoordinates.ToArray();
            var compatible = pool.Where(candidate => Descriptor(candidate.Topology) == Descriptor(parent.Topology)).ToArray();
            var rebirth = compatible.Length < 4 || random.NextUnitDouble() < policy.RebirthProbability;
            if (rebirth)
            {
                pending = pending with { Operation = "population-rebirth" };
                for (var i = 0; i < coordinates.Length; i++)
                {
                    var slot = binding.Slots[i];
                    coordinates[i] = policy.Geometry is { } geometry
                        ? Math.Clamp(coordinates[i] + geometry.RebirthBoundFraction * (slot.UpperCoordinate - slot.LowerCoordinate)
                            * (2 * random.NextUnitDouble() - 1), slot.LowerCoordinate, slot.UpperCoordinate)
                        : slot.LowerCoordinate + random.NextUnitDouble() * (slot.UpperCoordinate - slot.LowerCoordinate);
                }
            }
            else
            {
                // Distinct donors exclude the target; one forced crossover avoids a clone.
                var donors = compatible.Where(candidate => candidate.Id != parent.Id).ToList();
                var first = donors[random.NextInt32(donors.Count)]; donors.Remove(first);
                var second = donors[random.NextInt32(donors.Count)];
                var elite = compatible[random.NextInt32(Math.Max(1, (int)Math.Ceiling(compatible.Length * policy.EliteFraction)))];
                var forced = random.NextInt32(coordinates.Length);
                for (var i = 0; i < coordinates.Length; i++)
                {
                    if (i != forced && random.NextUnitDouble() >= policy.CrossoverProbability) continue;
                    var slot = binding.Slots[i];
                    var candidate = coordinates[i] + policy.DifferentialWeight * (Read(elite.Topology, slot.Request) / slot.Request.Scale - coordinates[i])
                        + policy.DifferentialWeight * (Read(first.Topology, slot.Request) - Read(second.Topology, slot.Request)) / slot.Request.Scale;
                    coordinates[i] = Math.Clamp(candidate, slot.LowerCoordinate, slot.UpperCoordinate);
                }
            }
            pending = policy.Geometry is not null ? pending with
            {
                Phase = AutonomousSearchPhase.Screening,
                AfterScreening = AutonomousSearchPhase.Local,
                PopulationProposal = new(binding.Fingerprint, binding.Slots.Select(slot => slot.Request).ToImmutableArray(), coordinates.ToImmutableArray())
            }
                : pending with { Template = TopologyParameterBinder.Apply(source, binding, coordinates).Topology };
        }
        catch (Exception error) when (TopologyEvaluationPlan.DomainError(error)) { failure = error.Message; }
        return (state with { TrialCursor = state.TrialCursor + 1, RandomState = random.State, Pending = pending }, failure);
    }

    private static string Descriptor(DesignTopology topology) => ContentFingerprint.Compute(topology.Elements.Select(element => new
    {
        element.Id,
        element.FrontSurfaceId,
        element.BackSurfaceId,
        element.Protected,
        Front = element.Front.Profile.Kind,
        Back = element.Back.Profile.Kind,
        FrontOrder = element.Front.Profile.MaximumEvenPower,
        BackOrder = element.Back.Profile.MaximumEvenPower
    }).ToArray());

    private static double Read(DesignTopology topology, TopologyVariableRequest request)
    {
        var element = topology.Elements.Single(element => element.FrontSurfaceId == request.SurfaceId || element.BackSurfaceId == request.SurfaceId);
        var front = request.SurfaceId == element.FrontSurfaceId;
        var face = front ? element.Front : element.Back;
        return request.Kind switch
        {
            OpticalDesignParameterKind.Curvature => face.Profile.CurvaturePerMillimeter,
            OpticalDesignParameterKind.AxialPosition => front ? element.FrontZMillimeters : element.BackZMillimeters,
            OpticalDesignParameterKind.Conic => face.Profile.Conic,
            OpticalDesignParameterKind.EvenAsphereCoefficient => face.Profile.Terms.SingleOrDefault(term => term.Power == request.Power)?.Coefficient ?? 0,
            _ => throw new NotSupportedException("Unsupported population coordinate.")
        };
    }

    private static void ValidatePopulation(PopulationExplorationOptions options, AutonomousSearchPlan plan)
    {
        if (options.Geometry is { } geometry && (geometry.Version != 1 || geometry.MaximumAttempts is < 1 or > 16
            || !double.IsFinite(geometry.RebirthBoundFraction) || geometry.RebirthBoundFraction is <= 0 or > .25))
            throw new ArgumentException("Invalid bounded population geometry policy.");
        if (options.Version != 1 || !double.IsFinite(options.DifferentialWeight) || options.DifferentialWeight is <= 0 or > 1
            || !double.IsFinite(options.CrossoverProbability) || options.CrossoverProbability is <= 0 or > 1
            || !double.IsFinite(options.EliteFraction) || options.EliteFraction is <= 0 or > 1
            || !double.IsFinite(options.RebirthProbability) || options.RebirthProbability is < 0 or > 1
            || plan.Options.QualifiedReductionOnly == true || plan.Seeds.Any(seed => seed.HasTransitionElements))
            throw new ArgumentException("Invalid population protocol or incompatible qualified-only reduction/transition plan.");
    }
}
