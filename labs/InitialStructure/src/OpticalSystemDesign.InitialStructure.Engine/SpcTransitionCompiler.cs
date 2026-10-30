using System.Collections.Immutable;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.InitialStructure.Contracts;
using static OpticalSystemDesign.InitialStructure.Engine.TopologyValidation;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>This type cannot be passed to the ordinary optimizer or qualified-design archive.</summary>
public sealed class CompiledSpcTransition
{
    private readonly OpticSnapshot _snapshot;
    internal CompiledSpcTransition(SpcTransitionState state, Optic optic, int originalBack, int reference, int image)
    {
        State = state;
        _snapshot = optic.ToSnapshot();
        Fingerprint = ContentFingerprint.Compute(new { state, Snapshot = _snapshot });
        OriginalBackIndex = originalBack;
        ReferenceIndex = reference;
        ImageIndex = image;
    }
    public SpcTransitionState State { get; }
    public string Algorithm => SpcTransitionState.Identity;
    public string Fingerprint { get; }
    public int OriginalBackIndex { get; }
    public int NullFrontIndex => OriginalBackIndex + 1;
    public int NullBackIndex => OriginalBackIndex + 2;
    public int ReferenceIndex { get; }
    public int ImageIndex { get; }
    public Optic CreateOptic() => Optic.FromSnapshot(_snapshot);
}

/// <summary>Translation only. Core supplies media, matrices, differentiation and all physical assessment after materialization.</summary>
public static class SpcTransitionCompiler
{
    public static SpcTransitionState Create(CompiledTopology source, string parentElementId, string newElementId)
    {
        ArgumentNullException.ThrowIfNull(source);
        var state = new SpcTransitionState
        {
            Baseline = source.Topology,
            Constraints = source.Constraints,
            BaselineBindingFingerprint = source.BindingFingerprint,
            ParentElementId = parentElementId,
            NewElementId = newElementId
        };
        _ = Compile(state);
        return state;
    }

    public static CompiledSpcTransition Compile(SpcTransitionState state)
    {
        var (source, parent, _) = ValidateState(state);
        var mapping = source.Elements.Single(element => element.ElementId == parent.Id);
        var snapshot = source.Snapshot;
        var original = snapshot.Surfaces[mapping.BackIndex];
        var components = original.Components!;
        var frontId = state.NewElementId + ":front";
        var backId = state.NewElementId + ":back";
        // Keep all three coincident interfaces: old glass/air, air/null glass, null glass/air.
        // Zero air is experimental and must not be collapsed when the null curvatures are perturbed.
        snapshot.Surfaces[mapping.BackIndex] = original with { Thickness = 0 };
        snapshot.Surfaces.Insert(mapping.BackIndex + 1, original with
        {
            Label = frontId,
            Thickness = 0,
            Material = parent.Material,
            Components = components with
            {
                MaterialBefore = components.MaterialAfter,
                MaterialBeforeComponent = components.MaterialAfterComponent,
                MaterialAfter = components.MaterialBefore,
                MaterialAfterComponent = components.MaterialBeforeComponent
            }
        });
        snapshot.Surfaces.Insert(mapping.BackIndex + 2, original with { Label = backId });
        snapshot = snapshot with { Surfaces = snapshot.Surfaces.Select((surface, index) => surface with { Number = index }).ToList() };
        var optic = Optic.FromSnapshot(snapshot);
        if (state.NullCurvatureOffsetPerMillimeter != 0)
        {
            var curvature = parent.Back.Profile.CurvaturePerMillimeter + state.NullCurvatureOffsetPerMillimeter;
            optic = OpticalDesignParameters.Apply(optic.ToSnapshot(),
                [new(mapping.BackIndex + 1, OpticalDesignParameterKind.Curvature), new(mapping.BackIndex + 2, OpticalDesignParameterKind.Curvature)],
                [curvature, curvature]);
        }
        return new(state, optic, mapping.BackIndex, source.SurfaceIndices[source.Topology.Reference.Id],
            source.SurfaceIndices[source.Topology.Image.Id] + 2);
    }

    /// <summary>Returns an ordinary manufacturable-bound topology, not an accepted design. The original full validator remains required.</summary>
    public static CompiledTopology Materialize(SpcTransitionState state, double thicknessMillimeters, double airGapMillimeters,
        string? material = null)
    {
        var (_, parent, parentIndex) = ValidateState(state);
        Require(double.IsFinite(thicknessMillimeters) && thicknessMillimeters >= state.Constraints.MinimumCenterThicknessMillimeters
            && double.IsFinite(airGapMillimeters) && (airGapMillimeters == 0 || airGapMillimeters >= state.Constraints.MinimumAirGapMillimeters),
            TopologyFailureCode.NoSpace, "Materialization requires an ordinary positive glass thickness and either a shared interface or a real minimum air gap.");
        Require(airGapMillimeters != 0 || state.NullCurvatureOffsetPerMillimeter == 0,
            TopologyFailureCode.IdentityMismatch, "Perturbed null faces cannot replace the original back face by a mismatched cemented interface. Use a real air gap.");
        var face = parent.Back with
        {
            Profile = parent.Back.Profile with
            { CurvaturePerMillimeter = parent.Back.Profile.CurvaturePerMillimeter + state.NullCurvatureOffsetPerMillimeter }
        };
        var element = new LensElement
        {
            Id = state.NewElementId,
            FrontSurfaceId = airGapMillimeters == 0 ? parent.BackSurfaceId : state.NewElementId + ":front",
            BackSurfaceId = state.NewElementId + ":back",
            FrontZMillimeters = parent.BackZMillimeters + airGapMillimeters,
            ThicknessMillimeters = thicknessMillimeters,
            Material = material ?? parent.Material,
            Front = face,
            Back = face
        };
        var topology = state.Baseline with
        {
            Elements = state.Baseline.Elements.Insert(parentIndex + 1, element),
            CementedInterfaces = airGapMillimeters == 0 ? new()
            { Interfaces = [new(parent.Id, element.Id, parent.BackSurfaceId)] } : null
        };
        return TopologyCompiler.Compile(topology, state.Constraints);
    }

    private static (CompiledTopology Source, LensElement Parent, int Index) ValidateState(SpcTransitionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(state.Baseline);
        ArgumentNullException.ThrowIfNull(state.Constraints);
        Require(state.Algorithm == SpcTransitionState.Identity && state.Version == 1,
            TopologyFailureCode.InvalidIdentity, "Unsupported SPC transition identity/version.");
        var source = TopologyCompiler.Compile(state.Baseline, state.Constraints);
        Require(source.BindingFingerprint == state.BaselineBindingFingerprint, TopologyFailureCode.IdentityMismatch, "The frozen SPC parent, catalog or structural bounds changed.");
        Require(state.Baseline.CementedInterfaces is null && !state.Baseline.HasTransitionElements
            && !state.Constraints.AllowTransitionElements && state.Constraints.TransitionAllowance is null,
            TopologyFailureCode.TransitionNotAllowed, "SPC V1 starts from ordinary air-separated elements with ordinary structural limits.");
        Require(state.Baseline.Elements.Length < state.Constraints.MaximumElementCount, TopologyFailureCode.ElementLimit, "There is no remaining physical element slot.");
        Require(!string.IsNullOrWhiteSpace(state.NewElementId) && state.NewElementId.Length <= InitialStructureLimits.MaximumIdentifierLength - 6
            && !state.NewElementId.Any(char.IsControl)
            && !state.Baseline.Elements.Any(element => element.Id == state.NewElementId
                || element.Id == state.NewElementId + ":front" || element.Id == state.NewElementId + ":back")
            && !source.SurfaceIndices.ContainsKey(state.NewElementId) && !source.SurfaceIndices.ContainsKey(state.NewElementId + ":front")
            && !source.SurfaceIndices.ContainsKey(state.NewElementId + ":back"), TopologyFailureCode.DuplicateId, "A fresh bounded null-element identity is required.");
        var index = -1;
        for (var i = 0; i < state.Baseline.Elements.Length; i++) if (state.Baseline.Elements[i].Id == state.ParentElementId) { index = i; break; }
        Require(index >= 0, TopologyFailureCode.UnknownElement, "Unknown SPC parent element.");
        var parent = state.Baseline.Elements[index];
        Require(!parent.Protected, TopologyFailureCode.ProtectedElement, "SPC cannot alter a protected parent interface.");
        Require(parent.Back.Profile.Kind == TopologyProfileKind.Standard && parent.Back.Profile.Conic == 0
            && double.IsFinite(state.NullCurvatureOffsetPerMillimeter) && Math.Abs(state.NullCurvatureOffsetPerMillimeter) <= 1
            && double.IsFinite(parent.Back.Profile.CurvaturePerMillimeter + state.NullCurvatureOffsetPerMillimeter),
            TopologyFailureCode.UnsupportedProfile, "SPC V1 null insertion requires a spherical back face and a finite bounded curvature offset.");
        return (source, parent, index);
    }
}
