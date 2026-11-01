using System.Collections.Immutable;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

public static partial class AutonomousSearchService
{
    private static readonly string CementedKernel = ContentFingerprint.Compute(new
    {
        Identity = "autonomous-cemented-safe-updates-v1",
        Core = typeof(SequentialTraceMeasurement).Assembly.ManifestModule.ModuleVersionId,
        Engine = typeof(AutonomousSearchService).Assembly.ManifestModule.ModuleVersionId,
        Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        OS = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        Architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
        VectorWidth = System.Numerics.Vector<double>.Count,
        Avx2 = System.Runtime.Intrinsics.X86.Avx2.IsSupported, Fma = System.Runtime.Intrinsics.X86.Fma.IsSupported,
        AdvSimd = System.Runtime.Intrinsics.Arm.AdvSimd.IsSupported
    });

    public static bool CanExecute(AutonomousSearchCheckpoint state)
        => state.Plan.Options.Expansion?.Cemented is null || state.CementedKernelFingerprint == CementedKernel;

    /// <summary>Geometry/permission guard for the growth branch only. Other originally enabled branches remain independent.</summary>
    public static string? CementedGrowthRejection(AutonomousSearchPlan plan, DesignTopology parent)
    {
        if (plan.Options.Expansion?.Cemented is not { } policy) return "CementedPolicyNotEnabled";
        ValidateCementedPolicy(policy); TopologyValidation.Validate(parent, plan.Constraints);
        if (TaskIdentity(parent) != TaskIdentity(plan.Seeds[0])) throw new ArgumentException("Cemented growth changed the original task metadata.");
        if (StructureSearchDiagnostics.ExternalStopAperturePrecheck(parent).ProvenInfeasible) return "OriginalExternalStopApertureConflict";
        if (plan.Options.FixedTopologyOnly == true || plan.Options.QualifiedReductionOnly == true || plan.Options.Expansion.EnableInsertion != true)
            return "OriginalInsertionPermissionConflict";
        if (parent.Elements.Length >= plan.Constraints.MaximumElementCount) return "OriginalElementLimit";
        return null;
    }

    private static IEnumerable<(string Id, TopologyFace Face)> VariableFaces(DesignTopology topology)
        => topology.Elements.SelectMany(e => new[] { (Id: e.FrontSurfaceId, Face: e.Front), (Id: e.BackSurfaceId, Face: e.Back) })
            .DistinctBy(f => f.Id).Where(f => topology.Elements.Where(e => e.FrontSurfaceId == f.Id || e.BackSurfaceId == f.Id).All(e => !e.Protected));

    private static void ValidateCementedPolicy(CementedGrowthOptions policy)
    {
        if (policy.Version != 1 || policy.MaximumGroupElements is < 2 or > 4 || !double.IsFinite(policy.InitialThicknessMillimeters)
            || policy.InitialThicknessMillimeters is < .001 or > 100 || policy.ThicknessMultipliers.IsDefaultOrEmpty
            || policy.ThicknessMultipliers.Length > 4 || policy.ThicknessMultipliers.Distinct().Count() != policy.ThicknessMultipliers.Length
            || policy.ThicknessMultipliers.Any(t => !double.IsFinite(t) || t is < 1 or > 16))
            throw new ArgumentException("Invalid bounded cemented growth policy.");
    }

    private static (string Operation, TopologyEditResult Edit) CementedEdit(AutonomousSearchCandidate parent, int branch,
        string id, AutonomousSearchPlan plan, Func<int, (string Operation, TopologyEditResult Edit)> airSeparated)
    {
        var expansion = plan.Options.Expansion!; var policy = expansion.Cemented!;
        var topology = parent.Topology; var links = topology.CementedInterfaces?.Interfaces ?? [];
        var sites = topology.Elements.Where(e => !e.Protected && !links.Any(i => i.LeftElementId == e.Id)
            && GroupSize(e.Id) < policy.MaximumGroupElements).ToArray();
        var variants = CementedGrowthRejection(plan, topology) is null
            ? sites.Length * plan.Options.InsertionMaterials.Length * policy.ThicknessMultipliers.Length : 0;
        if (branch < variants)
        {
            var site = sites[branch % sites.Length];
            var variant = branch / sites.Length;
            var material = plan.Options.InsertionMaterials[variant % plan.Options.InsertionMaterials.Length];
            var thickness = Math.Max(policy.InitialThicknessMillimeters, Math.Max(plan.Constraints.MinimumCenterThicknessMillimeters,
                plan.Targets.MinimumEdgeThicknessMillimeters)) * policy.ThicknessMultipliers[variant / plan.Options.InsertionMaterials.Length];
            var occupied = topology.Elements.SelectMany(e => new[] { e.Id, e.FrontSurfaceId, e.BackSurfaceId })
                .Concat(new[] { topology.Reference.Id, topology.Stop.Id, topology.Image.Id }).ToHashSet(StringComparer.Ordinal);
            var prefix = id + "-cemented"; var fresh = prefix;
            for (var suffix = 1; occupied.Contains(fresh) || occupied.Contains(fresh + ":back"); suffix++) fresh = prefix + "-" + suffix;
            return ("cemented-insert", TopologyOperators.CementedInsert(topology, site.Id, fresh, material, thickness, plan.Constraints));
        }
        branch -= variants;
        if (topology.CementedInterfaces is null) return airSeparated(branch);
        // Legacy deletion, split, merge and profile edits are not shared-interface operations.
        // Only the explicitly permitted catalog substitutions follow bounded attachment trials.
        var changes = expansion.EnableMaterial ? topology.Elements.Where(e => !e.Protected)
            .SelectMany(e => expansion.MaterialAlternatives.Where(m => !string.Equals(m, e.Material, StringComparison.OrdinalIgnoreCase))
                .Select(m => (e.Id, Material: m))).ToArray() : [];
        if (branch < changes.Length)
            return ("cemented-material", TopologyOperators.CementedMaterial(topology, changes[branch].Id, changes[branch].Material, plan.Constraints));
        return ("cemented", new(null, null, new(TopologyFailureCode.UnsupportedFeature, "No remaining permitted cemented attachment or material variant; shared members are never sent to legacy edits.")));

        int GroupSize(string tail)
        {
            var size = 1;
            while (links.FirstOrDefault(i => i.RightElementId == tail) is { } previous) { size++; tail = previous.LeftElementId; }
            return size;
        }
    }
}
