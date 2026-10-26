using System.Collections.Immutable;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.InitialStructure.Contracts;
using static OpticalSystemDesign.InitialStructure.Engine.TopologyValidation;

namespace OpticalSystemDesign.InitialStructure.Engine;

/// <summary>Vertex-level edits only; optical restoration/acceptance are separate stages.</summary>
public static partial class TopologyOperators
{
    public static TopologyEditResult Insert(DesignTopology parent, string leftSurfaceId, string rightSurfaceId,
        LensElement newElement, double positionFraction, TopologyConstraints? constraints = null)
    {
        constraints ??= new();
        return Edit(parent, constraints, TopologyOperationKind.Insert, () =>
        {
            ArgumentNullException.ThrowIfNull(newElement);
            Require(double.IsFinite(positionFraction) && positionFraction is >= 0 and <= 1,
                TopologyFailureCode.InvalidInput, "Insertion fraction must be in [0,1].");
            Require(parent.Elements.Length < constraints.MaximumElementCount, TopologyFailureCode.ElementLimit, "Insertion exceeds the allowed element count.");
            var interval = AirIntervals(parent, constraints).SingleOrDefault(item => item.LeftSurfaceId == leftSurfaceId && item.RightSurfaceId == rightSurfaceId);
            Require(interval is not null, TopologyFailureCode.UnknownBoundary, "The requested insertion air interval does not exist.");
            var rightClearance = rightSurfaceId == parent.Image.Id
                ? Math.Max(constraints.MinimumAirGapMillimeters, constraints.MinimumBackFocusMillimeters)
                : constraints.MinimumAirGapMillimeters;
            Require(double.IsFinite(newElement.ThicknessMillimeters) && newElement.ThicknessMillimeters > 0,
                TopologyFailureCode.NoSpace, "The air interval cannot contain this real thickness and both minimum gaps.");
            var lower = interval.LeftZMillimeters + constraints.MinimumAirGapMillimeters;
            for (var adjustment = 0; adjustment < 4 && lower - interval.LeftZMillimeters < constraints.MinimumAirGapMillimeters; adjustment++)
                lower = Math.BitIncrement(lower);
            var upper = interval.RightZMillimeters - rightClearance - newElement.ThicknessMillimeters;
            for (var adjustment = 0; adjustment < 4 && interval.RightZMillimeters - (upper + newElement.ThicknessMillimeters) < rightClearance; adjustment++)
                upper = Math.BitDecrement(upper);
            Require(double.IsFinite(lower) && double.IsFinite(upper) && lower <= upper
                && lower - interval.LeftZMillimeters >= constraints.MinimumAirGapMillimeters
                && interval.RightZMillimeters - (upper + newElement.ThicknessMillimeters) >= rightClearance,
                TopologyFailureCode.NoSpace, "The air interval cannot represent this thickness and both minimum gaps.");
            var positioned = newElement with { FrontZMillimeters = Math.Clamp(lower + positionFraction * (upper - lower), lower, upper) };
            var index = 0;
            while (index < parent.Elements.Length && parent.Elements[index].FrontZMillimeters < positioned.FrontZMillimeters) index++;
            return (parent with { Elements = parent.Elements.Insert(index, positioned) }, ImmutableArray.Create(positioned.Id), ImmutableArray<string>.Empty);
        });
    }

    public static TopologyEditResult Delete(DesignTopology parent, string elementId, TopologyConstraints? constraints = null)
    {
        constraints ??= new();
        return Edit(parent, constraints, TopologyOperationKind.Delete, () =>
        {
            Require(parent.Elements.Length > constraints.MinimumElementCount, TopologyFailureCode.ElementLimit, "Deletion violates the minimum element count.");
            var index = -1;
            for (var i = 0; i < parent.Elements.Length; i++) if (parent.Elements[i].Id == elementId) { index = i; break; }
            Require(index >= 0, TopologyFailureCode.UnknownElement, "The requested lens element does not exist.");
            Require(!parent.Elements[index].Protected, TopologyFailureCode.ProtectedElement, "The requested lens element is protected.");
            return (parent with { Elements = parent.Elements.RemoveAt(index) }, ImmutableArray<string>.Empty, ImmutableArray.Create(elementId));
        });
    }

    private static TopologyEditResult Edit(DesignTopology parent, TopologyConstraints constraints, TopologyOperationKind operation,
        Func<(DesignTopology Child, ImmutableArray<string> Created, ImmutableArray<string> Removed)> edit)
    {
        try
        {
            Validate(parent, constraints);
            var (child, created, removed) = edit();
            Validate(child, constraints);
            return new(child, new(operation, ContentFingerprint.Compute(parent), ContentFingerprint.Compute(child), created, removed), null);
        }
        catch (TopologyValidationException error) { return new(null, null, new(error.Code, error.Message)); }
    }
}
