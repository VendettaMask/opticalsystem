using System.Collections.Immutable;
using OpticalSystemDesign.InitialStructure.Contracts;
using static OpticalSystemDesign.InitialStructure.Engine.TopologyValidation;

namespace OpticalSystemDesign.InitialStructure.Engine;

public static partial class TopologyOperators
{
    public static TopologyEditResult Split(DesignTopology parent, string elementId, string firstId, string secondId,
        double airGapMillimeters, double firstFraction = .5, TopologyConstraints? constraints = null)
    {
        constraints ??= new();
        return CompileEdit(Edit(parent, constraints, TopologyOperationKind.Split, () =>
        {
            var index = ElementIndex(parent, elementId);
            var original = parent.Elements[index];
            Require(!original.Protected && !original.Transition, TopologyFailureCode.ProtectedElement, "Cannot split a protected or transition element.");
            Require(parent.Elements.Length < constraints.MaximumElementCount, TopologyFailureCode.ElementLimit, "Split exceeds the element limit.");
            Require(double.IsFinite(airGapMillimeters) && airGapMillimeters >= constraints.MinimumAirGapMillimeters
                && double.IsFinite(firstFraction) && firstFraction is > 0 and < 1, TopologyFailureCode.NoSpace, "Split requires a real minimum air gap and a fraction in (0,1).");
            var glass = original.ThicknessMillimeters - airGapMillimeters;
            var firstThickness = glass * firstFraction;
            var secondFront = original.FrontZMillimeters + firstThickness + airGapMillimeters;
            while (secondFront - (original.FrontZMillimeters + firstThickness) < airGapMillimeters)
                secondFront = Math.BitIncrement(secondFront);
            Require(firstThickness >= constraints.MinimumCenterThicknessMillimeters && original.BackZMillimeters - secondFront >= constraints.MinimumCenterThicknessMillimeters,
                TopologyFailureCode.NoSpace, "Two real minimum glass thicknesses and air do not fit in the original envelope.");
            var radius = Math.Min(original.Front.ClearSemiDiameterMillimeters, original.Back.ClearSemiDiameterMillimeters);
            var bodyRadius = Math.Max(original.Front.MechanicalSemiDiameterMillimeters ?? original.Front.ClearSemiDiameterMillimeters,
                original.Back.MechanicalSemiDiameterMillimeters ?? original.Back.ClearSemiDiameterMillimeters);
            var inside = new TopologyFace { ClearSemiDiameterMillimeters = radius, MechanicalSemiDiameterMillimeters = bodyRadius, Profile = new() };
            var first = original with { Id = firstId, BackSurfaceId = firstId + ":inner", ThicknessMillimeters = firstThickness, Back = inside };
            var second = original with
            {
                Id = secondId,
                FrontSurfaceId = secondId + ":inner",
                FrontZMillimeters = secondFront,
                ThicknessMillimeters = original.BackZMillimeters - secondFront,
                Front = inside
            };
            return (parent with { Elements = parent.Elements.RemoveAt(index).InsertRange(index, ImmutableArray.Create(first, second)) }, ImmutableArray.Create(firstId, secondId), ImmutableArray.Create(elementId));
        }), constraints);
    }

    public static TopologyEditResult Merge(DesignTopology parent, string leftId, string rightId, string mergedId, string material,
        TopologyConstraints? constraints = null)
    {
        constraints ??= new();
        return CompileEdit(Edit(parent, constraints, TopologyOperationKind.Merge, () =>
        {
            var index = ElementIndex(parent, leftId);
            Require(index + 1 < parent.Elements.Length && parent.Elements[index + 1].Id == rightId,
                TopologyFailureCode.UnknownElement, "Merge requires adjacent elements in forward order.");
            var left = parent.Elements[index]; var right = parent.Elements[index + 1];
            Require(!left.Protected && !right.Protected && !left.Transition && !right.Transition,
                TopologyFailureCode.ProtectedElement, "Cannot merge a protected or transition element.");
            Require(parent.Stop.ZMillimeters < left.FrontZMillimeters || parent.Stop.ZMillimeters > right.BackZMillimeters,
                TopologyFailureCode.StopInsideElement, "Merge cannot enclose an independent stop.");
            Require(parent.Elements.Length > constraints.MinimumElementCount, TopologyFailureCode.ElementLimit, "Merge violates the minimum count.");
            var merged = left with
            {
                Id = mergedId,
                BackSurfaceId = right.BackSurfaceId,
                Back = right.Back,
                ThicknessMillimeters = right.BackZMillimeters - left.FrontZMillimeters,
                Material = material
            };
            return (parent with { Elements = parent.Elements.RemoveRange(index, 2).Insert(index, merged) }, ImmutableArray.Create(mergedId), ImmutableArray.Create(leftId, rightId));
        }), constraints);
    }

    public static TopologyEditResult ChangeMaterial(DesignTopology parent, string elementId, string material, TopologyConstraints? constraints = null)
    {
        constraints ??= new();
        return CompileEdit(Edit(parent, constraints, TopologyOperationKind.Material, () =>
        {
            var index = ElementIndex(parent, elementId);
            var element = parent.Elements[index];
            Require(!element.Protected && !element.Transition, TopologyFailureCode.ProtectedElement, "Cannot change protected/transition glass.");
            Require(!string.Equals(element.Material, material, StringComparison.OrdinalIgnoreCase), TopologyFailureCode.InvalidInput, "Material branch must change the catalog material.");
            return (parent with { Elements = parent.Elements.SetItem(index, element with { Material = material }) }, ImmutableArray<string>.Empty, ImmutableArray<string>.Empty);
        }), constraints);
    }

    public static TopologyEditResult UpgradeProfile(DesignTopology parent, string surfaceId, int maximumEvenPower, TopologyConstraints? constraints = null)
    {
        constraints ??= new();
        return CompileEdit(Edit(parent, constraints, TopologyOperationKind.Profile, () =>
        {
            var index = -1;
            for (var i = 0; i < parent.Elements.Length; i++)
                if (parent.Elements[i].FrontSurfaceId == surfaceId || parent.Elements[i].BackSurfaceId == surfaceId) { index = i; break; }
            Require(index >= 0, TopologyFailureCode.UnknownBoundary, "Unknown profile surface identity.");
            var element = parent.Elements[index];
            Require(!element.Protected && !element.Transition, TopologyFailureCode.ProtectedElement, "Cannot change a protected/transition profile.");
            var front = element.FrontSurfaceId == surfaceId;
            var face = front ? element.Front : element.Back;
            Require(maximumEvenPower is >= 4 and <= 18 && maximumEvenPower % 2 == 0
                && (face.Profile.Kind != TopologyProfileKind.EvenAsphere || maximumEvenPower > face.Profile.MaximumEvenPower),
                TopologyFailureCode.UnsupportedProfile, "A profile branch must explicitly introduce or increase a supported even order.");
            var profile = face.Profile with
            {
                Kind = TopologyProfileKind.EvenAsphere,
                MaximumEvenPower = maximumEvenPower,
                Terms = Enumerable.Range(2, maximumEvenPower / 2 - 1).Select(power => new TopologyAsphereTerm(power * 2,
                    face.Profile.Terms.SingleOrDefault(term => term.Power == power * 2)?.Coefficient ?? 0)).ToImmutableArray()
            };
            var changed = face with { Profile = profile };
            return (parent with { Elements = parent.Elements.SetItem(index, front ? element with { Front = changed } : element with { Back = changed }) },
                ImmutableArray<string>.Empty, ImmutableArray<string>.Empty);
        }), constraints);
    }

    public static GradualDeletionState CreateGradualDeletion(DesignTopology parent, string elementId, ImmutableArray<double> scales,
        TopologyConstraints? constraints = null)
    {
        constraints ??= new();
        Validate(parent, constraints);
        Require(parent.CementedInterfaces is null, TopologyFailureCode.UnsupportedFeature, "Gradual deletion of a cemented member requires an explicit group policy.");
        var original = parent.Elements[ElementIndex(parent, elementId)];
        Require(!parent.HasTransitionElements && constraints.TransitionAllowance is null && !original.Protected
            && parent.Elements.Length > constraints.MinimumElementCount, TopologyFailureCode.TransitionNotAllowed, "Gradual deletion requires an ordinary, removable original element.");
        var result = new GradualDeletionState { Baseline = parent, BaselineFingerprint = ContentFingerprint.Compute(parent), OriginalElement = original, Scales = scales };
        ValidateGradualDeletion(result);
        return result;
    }

    public static void ValidateGradualDeletion(GradualDeletionState state)
    {
        Require(state.BaselineFingerprint == ContentFingerprint.Compute(state.Baseline) && !state.Baseline.HasTransitionElements
            && !state.OriginalElement.Protected && state.Baseline.Elements.Any(element => element.Id == state.OriginalElement.Id
                && ContentFingerprint.Compute(element) == ContentFingerprint.Compute(state.OriginalElement)),
            TopologyFailureCode.IdentityMismatch, "The immutable unscaled deletion origin does not match its baseline.");
        Require(state.Scales.Length is >= 1 and <= 8 && state.StageIndex >= 0 && state.StageIndex <= state.Scales.Length
            && state.Scales.All(scale => double.IsFinite(scale) && scale is > 0 and < 1)
            && state.Scales.Zip(state.Scales.Skip(1), (a, b) => a > b).All(value => value),
            TopologyFailureCode.InvalidInput, "Gradual deletion requires a bounded strictly descending original-relative schedule.");
    }

    public static LensElement PrescribedDeletionElement(GradualDeletionState state)
    {
        ValidateGradualDeletion(state);
        Require(state.StageIndex < state.Scales.Length, TopologyFailureCode.InvalidInput, "The final stage has no remaining target element.");
        var original = state.OriginalElement;
        var scale = state.Scales[state.StageIndex];
        var thickness = original.ThicknessMillimeters * scale;
        return original with
        {
            Transition = true,
            ThicknessMillimeters = thickness,
            FrontZMillimeters = original.FrontZMillimeters + (original.ThicknessMillimeters - thickness) / 2,
            Front = Scale(original.Front),
            Back = Scale(original.Back)
        };

        TopologyFace Scale(TopologyFace face) => face with
        {
            Profile = face.Profile with
            {
                CurvaturePerMillimeter = face.Profile.CurvaturePerMillimeter * scale,
                Terms = face.Profile.Terms.Select(term => term with { Coefficient = term.Coefficient * scale }).ToImmutableArray()
            }
        };
    }

    public static TopologyConstraints DeletionConstraints(GradualDeletionState state, TopologyConstraints original, TopologyTargets targets)
    {
        ValidateGradualDeletion(state);
        if (state.StageIndex == state.Scales.Length) return original;
        var scale = state.Scales[state.StageIndex];
        return original with
        {
            AllowTransitionElements = true,
            TransitionAllowance = new(state.OriginalElement.Id,
            original.MinimumCenterThicknessMillimeters * scale, targets.MinimumEdgeThicknessMillimeters * scale)
        };
    }

    public static TopologyEditResult ApplyDeletionStage(GradualDeletionState state, DesignTopology compensated,
        TopologyConstraints originalConstraints, TopologyTargets targets)
    {
        try
        {
            ValidateGradualDeletion(state);
            Require(ContentFingerprint.Compute(compensated with { Elements = state.Baseline.Elements }) == state.BaselineFingerprint
                && compensated.Elements.Select(element => element.Id).SequenceEqual(state.Baseline.Elements.Select(element => element.Id)),
                TopologyFailureCode.IdentityMismatch, "Gradual deletion cannot change task metadata or the retained element identities.");
            var index = ElementIndex(compensated, state.OriginalElement.Id);
            var expected = state.StageIndex == 0 ? state.OriginalElement : PrescribedDeletionElement(state with { StageIndex = state.StageIndex - 1 });
            Require(ContentFingerprint.Compute(compensated.Elements[index]) == ContentFingerprint.Compute(expected),
                TopologyFailureCode.IdentityMismatch, "The target was changed outside its original-relative prescribed scale.");
            foreach (var element in compensated.Elements.Where(element => element.Id != state.OriginalElement.Id))
                Require(!element.Transition && (!element.Protected || ContentFingerprint.Compute(element)
                    == ContentFingerprint.Compute(state.Baseline.Elements.Single(item => item.Id == element.Id))),
                    TopologyFailureCode.ProtectedElement, "Compensation cannot mark another transition or alter protected glass.");
            if (state.StageIndex == state.Scales.Length)
                return CompileEdit(Delete(compensated, state.OriginalElement.Id,
                    DeletionConstraints(state with { StageIndex = state.StageIndex - 1 }, originalConstraints, targets)), originalConstraints);
            var child = compensated with { Elements = compensated.Elements.SetItem(index, PrescribedDeletionElement(state)) };
            Validate(child, DeletionConstraints(state, originalConstraints, targets));
            return CompileEdit(new(child, new(TopologyOperationKind.ScaleForDeletion, ContentFingerprint.Compute(compensated), ContentFingerprint.Compute(child), [], []), null),
                DeletionConstraints(state, originalConstraints, targets));
        }
        catch (TopologyValidationException error) { return new(null, null, new(error.Code, error.Message)); }
    }

    private static int ElementIndex(DesignTopology topology, string id)
    {
        for (var index = 0; index < topology.Elements.Length; index++) if (topology.Elements[index].Id == id) return index;
        throw new TopologyValidationException(TopologyFailureCode.UnknownElement, "Unknown element identity.");
    }

    private static TopologyEditResult CompileEdit(TopologyEditResult result, TopologyConstraints constraints)
    {
        if (!result.Succeeded) return result;
        try { _ = TopologyCompiler.Compile(result.Topology!, constraints); return result; }
        catch (TopologyValidationException error) { return new(null, null, new(error.Code, error.Message)); }
    }
}
