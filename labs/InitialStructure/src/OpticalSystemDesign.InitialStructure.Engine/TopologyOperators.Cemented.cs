using System.Collections.Immutable;
using OpticalSystemDesign.InitialStructure.Contracts;
using static OpticalSystemDesign.InitialStructure.Engine.TopologyValidation;

namespace OpticalSystemDesign.InitialStructure.Engine;

public static partial class TopologyOperators
{
    /// <summary>Attach a positive-thickness catalog lens to one actual free back face.
    /// No air interface is fabricated; no retained geometry, aperture or material is replaced.</summary>
    public static TopologyEditResult CementedInsert(DesignTopology parent, string parentElementId, string newId,
        string material, double thickness, TopologyConstraints constraints)
    {
        try
        {
            Validate(parent, constraints);
            var index = Array.FindIndex(parent.Elements.ToArray(), e => e.Id == parentElementId);
            Require(index >= 0, TopologyFailureCode.UnknownElement, "Unknown cemented attachment member.");
            var left = parent.Elements[index];
            Require(!left.Protected, TopologyFailureCode.ProtectedElement, "Cannot attach to a protected member.");
            Require(!(parent.CementedInterfaces?.Interfaces.Any(i => i.LeftElementId == left.Id) ?? false),
                TopologyFailureCode.UnsupportedFeature, "This back face already belongs to a cemented interface.");
            Require(parent.Elements.Length < constraints.MaximumElementCount, TopologyFailureCode.ElementLimit, "Original element limit prevents cemented growth.");
            Require(double.IsFinite(thickness) && thickness >= constraints.MinimumCenterThicknessMillimeters && thickness > 0,
                TopologyFailureCode.NoSpace, "Cemented growth requires ordinary positive center thickness.");
            var lens = new LensElement
            {
                Id = newId, FrontSurfaceId = left.BackSurfaceId, BackSurfaceId = newId + ":back",
                FrontZMillimeters = left.BackZMillimeters, ThicknessMillimeters = thickness, Material = material,
                Front = left.Back, Back = left.Back
            };
            var child = parent with
            {
                Elements = parent.Elements.Insert(index + 1, lens),
                CementedInterfaces = new() { Interfaces = (parent.CementedInterfaces?.Interfaces ?? []).Add(new(left.Id, newId, left.BackSurfaceId)) }
            };
            Validate(child, constraints); // Includes the actual remaining gap/stop/image clearance and catalog permission.
            _ = TopologyCompiler.Compile(child, constraints);
            return new(child, new(TopologyOperationKind.CementedInsert, ContentFingerprint.Compute(parent), ContentFingerprint.Compute(child), [newId], []), null);
        }
        catch (TopologyValidationException error) { return new(null, null, new(error.Code, error.Message)); }
    }

    /// <summary>Catalog substitution retains the exact shared faces of every member.</summary>
    public static TopologyEditResult CementedMaterial(DesignTopology parent, string elementId, string material, TopologyConstraints constraints)
    {
        try
        {
            Validate(parent, constraints);
            var index = Array.FindIndex(parent.Elements.ToArray(), e => e.Id == elementId);
            Require(index >= 0, TopologyFailureCode.UnknownElement, "Unknown cemented material member.");
            Require(!parent.Elements[index].Protected, TopologyFailureCode.ProtectedElement, "Cannot replace protected glass.");
            var child = parent with { Elements = parent.Elements.SetItem(index, parent.Elements[index] with { Material = material }) };
            _ = TopologyCompiler.Compile(child, constraints);
            return new(child, new(TopologyOperationKind.Material, ContentFingerprint.Compute(parent), ContentFingerprint.Compute(child), [], []), null);
        }
        catch (TopologyValidationException error) { return new(null, null, new(error.Code, error.Message)); }
    }
}
