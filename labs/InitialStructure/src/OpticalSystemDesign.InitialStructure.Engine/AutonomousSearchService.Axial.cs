using System.Collections.Immutable;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.InitialStructure.Contracts;

namespace OpticalSystemDesign.InitialStructure.Engine;

public static partial class AutonomousSearchService
{
    private static IEnumerable<TopologyAxialAnchor> AxialFaces(AutonomousSearchPending pending, TopologyAxialCompensationOptions policy)
        => pending.Template.Elements.Where(element => !element.Protected && element.Id != pending.GradualDeletion?.OriginalElement.Id
                && (policy.ElementIds.IsEmpty || policy.ElementIds.Contains(element.Id, StringComparer.Ordinal)))
            .SelectMany(element => new[] { new TopologyAxialAnchor(element.FrontSurfaceId, element.FrontZMillimeters), new(element.BackSurfaceId, element.BackZMillimeters) });

    private static AutonomousSearchPending AnchorAxialCompensation(AutonomousSearchPending pending, AutonomousSearchOptions policy)
        => policy.AxialCompensation is { } axial && pending.AxialAnchors is null
            ? pending with { AxialAnchors = AxialFaces(pending, axial).ToImmutableArray() } : pending;

    private static ImmutableArray<TopologyVariableRequest> AxialVariables(AutonomousSearchPending pending, AutonomousSearchOptions options)
    {
        if (options.AxialCompensation is not { } policy || pending.AxialAnchors is not { } anchors) return [];
        var surfaces = AxialFaces(pending, policy).Select(face => face.SurfaceId).ToHashSet(StringComparer.Ordinal);
        return anchors.Where(anchor => surfaces.Contains(anchor.SurfaceId)).Select(anchor => new TopologyVariableRequest(anchor.SurfaceId,
            OpticalDesignParameterKind.AxialPosition,
            Math.Max(pending.Template.Reference.ZMillimeters, anchor.ZMillimeters - policy.MaximumDisplacementMillimeters),
            Math.Min(pending.Template.Image.ZMillimeters, anchor.ZMillimeters + policy.MaximumDisplacementMillimeters), policy.CoordinateScaleMillimeters)).ToImmutableArray();
    }

    private static void ValidateAxialPolicy(TopologyAxialCompensationOptions policy, AutonomousSearchPlan plan)
    {
        if (!double.IsFinite(policy.MaximumDisplacementMillimeters) || policy.MaximumDisplacementMillimeters is <= 0 or > 10
            || !double.IsFinite(policy.CoordinateScaleMillimeters) || policy.CoordinateScaleMillimeters is <= 0 or > 10
            || policy.ElementIds.IsDefault || policy.ElementIds.Length > AutonomousTopologyIdentity.MaximumElements
            || policy.ElementIds.Any(id => !Identifier(id)) || policy.ElementIds.Distinct(StringComparer.Ordinal).Count() != policy.ElementIds.Length
            || policy.ElementIds.Any(id => !plan.Seeds.Any(seed => seed.Elements.Any(element => element.Id == id && !element.Protected))))
            throw new ArgumentException("Invalid bounded axial compensation policy or protected/unknown element selection.");
    }

    private static void ValidateAxialCheckpoint(AutonomousSearchCheckpoint state)
    {
        if (state.Pending is not { } pending) return;
        var axialRequests = pending.Variables.Where(request => request.Kind == OpticalDesignParameterKind.AxialPosition).ToArray();
        if (state.Plan.Options.AxialCompensation is not { } policy)
        {
            if (pending.AxialAnchors is not null || axialRequests.Length != 0) throw new InvalidDataException("Unexpected autonomous axial continuation.");
            return;
        }
        if (pending.AxialAnchors is not { } anchors)
        {
            if (pending.StageOptions is not null || axialRequests.Length != 0) throw new InvalidDataException("Missing per-trial axial anchors.");
            return;
        }
        var faces = AxialFaces(pending, policy).ToDictionary(face => face.SurfaceId, StringComparer.Ordinal);
        if (anchors.IsDefault || anchors.Any(anchor => anchor is null || string.IsNullOrWhiteSpace(anchor.SurfaceId))
            || anchors.Select(anchor => anchor.SurfaceId).Distinct(StringComparer.Ordinal).Count() != anchors.Length
            || anchors.Length != faces.Count || anchors.Any(anchor => !double.IsFinite(anchor.ZMillimeters) || !faces.ContainsKey(anchor.SurfaceId)
                || anchor.ZMillimeters < pending.Template.Reference.ZMillimeters || anchor.ZMillimeters > pending.Template.Image.ZMillimeters
                || Math.Abs(faces[anchor.SurfaceId].ZMillimeters - anchor.ZMillimeters) > policy.MaximumDisplacementMillimeters + 1e-10))
            throw new InvalidDataException("Per-trial axial bounds, eligibility or origin changed.");
        var expected = AxialVariables(pending, state.Plan.Options);
        var allVariables = pending.StageOptions is not null && (pending.Phase == AutonomousSearchPhase.Full
            || pending.Phase == AutonomousSearchPhase.Local && pending.StageOptions.ActiveParameterLimit is not null);
        if (pending.Phase == AutonomousSearchPhase.Recovery ? state.Plan.Options.UseAxialVariablesInRecovery != true && axialRequests.Length != 0
                || axialRequests.Any(request => !expected.Contains(request))
            : axialRequests.Any(request => !expected.Contains(request)) || allVariables && !axialRequests.SequenceEqual(expected))
            throw new InvalidDataException("Axial variables do not match the persisted physical window.");
    }
}
