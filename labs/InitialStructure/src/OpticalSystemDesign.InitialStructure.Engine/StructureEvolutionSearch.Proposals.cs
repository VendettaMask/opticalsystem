using System.Collections.Immutable;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

public static partial class StructureEvolutionSearch
{
    private static TopologyEditResult Propose(DesignTopology parent, TopologyOperationKind operation, int variant,
        string id, AutonomousSearchPlan plan, StructureEvolutionOptions policy)
    {
        var expansion = plan.Options.Expansion;
        var permission = operation switch
        {
            TopologyOperationKind.Delete => expansion?.EnableDeletion ?? true,
            TopologyOperationKind.Insert => expansion?.EnableInsertion ?? true,
            TopologyOperationKind.Split => expansion?.EnableSplit == true,
            TopologyOperationKind.Merge => expansion?.EnableMerge == true,
            TopologyOperationKind.Material => expansion?.EnableMaterial == true,
            TopologyOperationKind.Profile => expansion?.EnableProfile == true,
            _ => false
        };
        if (plan.Options.FixedTopologyOnly == true || !permission) return Reject("Operation disabled by original plan permissions.");
        var available = parent.Elements.Where(e => !e.Protected && !e.Transition).ToArray();
        if (available.Length == 0) return Reject("No editable physical element.");
        // Alternate from the rear/front so deletion examines the clipping-prone ends first.
        var index = variant % available.Length;
        index = index % 2 == 0 ? available.Length - 1 - index / 2 : index / 2;
        var element = available[index];
        switch (operation)
        {
            case TopologyOperationKind.Delete:
                return TopologyOperators.Delete(parent, element.Id, plan.Constraints);
            case TopologyOperationKind.Insert:
                var intervals = TopologyValidation.AirIntervals(parent, plan.Constraints)
                    .OrderByDescending(a => a.RightZMillimeters - a.LeftZMillimeters).ToArray();
                if (intervals.Length == 0 || plan.Options.InsertionMaterials.IsEmpty) return Reject("No permitted insertion interval/material.");
                var interval = intervals[variant % intervals.Length];
                var radius = parent.Elements.Min(e => Math.Min(e.Front.ClearSemiDiameterMillimeters, e.Back.ClearSemiDiameterMillimeters));
                var face = new TopologyFace { ClearSemiDiameterMillimeters = radius, MechanicalSemiDiameterMillimeters = radius, Profile = new() };
                var inserted = new LensElement
                {
                    Id = id + "-insert",
                    FrontSurfaceId = id + ":front",
                    BackSurfaceId = id + ":back",
                    ThicknessMillimeters = Math.Max(policy.InsertedThicknessMillimeters, plan.Constraints.MinimumCenterThicknessMillimeters),
                    Material = plan.Options.InsertionMaterials[variant % plan.Options.InsertionMaterials.Length],
                    Front = face,
                    Back = face
                };
                return TopologyOperators.Insert(parent, interval.LeftSurfaceId, interval.RightSurfaceId, inserted, .5, plan.Constraints);
            case TopologyOperationKind.Split:
                return TopologyOperators.Split(parent, element.Id, id + "-a", id + "-b",
                    Math.Max(plan.Constraints.MinimumAirGapMillimeters, expansion!.SplitAirGapMillimeters), expansion.SplitFirstFraction, plan.Constraints);
            case TopologyOperationKind.Merge:
                if (parent.Elements.Length < 2) return Reject("Two adjacent elements required.");
                var left = parent.Elements[variant % (parent.Elements.Length - 1)];
                var right = parent.Elements[variant % (parent.Elements.Length - 1) + 1];
                var material = expansion!.MaterialAlternatives.IsEmpty ? left.Material : expansion.MaterialAlternatives[variant % expansion.MaterialAlternatives.Length];
                return TopologyOperators.Merge(parent, left.Id, right.Id, id + "-merged", material, plan.Constraints);
            case TopologyOperationKind.Material:
                var materials = expansion!.MaterialAlternatives.Where(m => !string.Equals(m, element.Material, StringComparison.OrdinalIgnoreCase)).ToArray();
                return materials.Length == 0 ? Reject("No different permitted material.")
                    : TopologyOperators.ChangeMaterial(parent, element.Id, materials[variant % materials.Length], plan.Constraints);
            case TopologyOperationKind.Profile:
                var faces = available.SelectMany(e => new[] { (e.FrontSurfaceId, e.Front.Profile), (e.BackSurfaceId, e.Back.Profile) })
                    .Where(f => f.Profile.Kind != TopologyProfileKind.EvenAsphere || f.Profile.MaximumEvenPower < expansion!.MaximumEvenPower).ToArray();
                if (faces.Length == 0) return Reject("No permitted surface order upgrade remains.");
                var chosen = faces[variant % faces.Length];
                return TopologyOperators.UpgradeProfile(parent, chosen.Item1,
                    chosen.Profile.Kind == TopologyProfileKind.EvenAsphere ? chosen.Profile.MaximumEvenPower + 2 : 4, plan.Constraints);
            default: return Reject("Unsupported edit.");
        }
        static TopologyEditResult Reject(string message) => new(null, null, new(TopologyFailureCode.UnsupportedFeature, message));
    }

    internal static ImmutableArray<TopologyVariableRequest> EvolutionVariables(DesignTopology topology, AutonomousSearchOptions options)
    {
        var variables = ParaxialSeedSearch.Variables(topology, options, true).Select(v => v.Kind == OpticalDesignParameterKind.AxialPosition
            ? v with { LowerBound = Math.Max(v.LowerBound, topology.Reference.ZMillimeters), UpperBound = Math.Min(v.UpperBound, topology.Image.ZMillimeters) } : v).ToImmutableArray().ToBuilder();
        if (options.Expansion is not { EnableProfile: true } expansion) return variables.ToImmutable();
        foreach (var element in topology.Elements.Where(e => !e.Protected))
            foreach (var (id, face) in new[] { (element.FrontSurfaceId, element.Front), (element.BackSurfaceId, element.Back) })
            {
                if (face.Profile.Kind != TopologyProfileKind.EvenAsphere) continue;
                var radius = face.MechanicalSemiDiameterMillimeters ?? face.ClearSemiDiameterMillimeters;
                for (var power = 4; power <= Math.Min(face.Profile.MaximumEvenPower, expansion.MaximumEvenPower); power += 2)
                {
                    var existing = face.Profile.Terms.SingleOrDefault(t => t.Power == power)?.Coefficient ?? 0;
                    var bound = Math.Max(1e-30, Math.Max(Math.Abs(existing) * 2, expansion.CoefficientCoordinateSagMillimeters / Math.Pow(radius, power)));
                    variables.Add(new(id, OpticalDesignParameterKind.EvenAsphereCoefficient, -bound, bound, bound, power));
                }
            }
        return variables.ToImmutable();
    }
}
