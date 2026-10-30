using System.Collections.Immutable;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.InitialStructure.Contracts;
using static OpticalSystemDesign.InitialStructure.Engine.TopologyValidation;

namespace OpticalSystemDesign.InitialStructure.Engine;

public sealed record TopologyParameterSlot(TopologyVariableRequest Request, OpticalDesignParameter CoreParameter,
    double InitialCoordinate, double LowerCoordinate, double UpperCoordinate);

/// <summary>A binding belongs to one compiled template, material set, coordinate frame and explicit bound set.</summary>
public sealed class BoundTopologyParameters
{
    internal BoundTopologyParameters(string sourceFingerprint, ImmutableArray<TopologyParameterSlot> slots)
    {
        SourceFingerprint = sourceFingerprint;
        Slots = slots;
        Fingerprint = ContentFingerprint.Compute(new { SourceFingerprint, Requests = slots.Select(slot => slot.Request).ToArray() });
    }
    public string SourceFingerprint { get; }
    public string Fingerprint { get; }
    public ImmutableArray<TopologyParameterSlot> Slots { get; }
    public ImmutableArray<OpticalDesignParameter> CoreParameters => Slots.Select(slot => slot.CoreParameter).ToImmutableArray();
    public ImmutableArray<double> InitialCoordinates => Slots.Select(slot => slot.InitialCoordinate).ToImmutableArray();
}

/// <summary>Stable surface IDs become Core parameters; no optical formula is implemented here.</summary>
public static class TopologyParameterBinder
{
    public static BoundTopologyParameters Bind(CompiledTopology source, IEnumerable<TopologyVariableRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(requests);
        var definitions = requests.Take(513).ToArray();
        Require(definitions.Length is >= 1 and <= 512 && definitions.All(item => item is not null),
            TopologyFailureCode.InvalidInput, "A bounded nonempty variable set is required.");
        var keys = new HashSet<(string Surface, OpticalDesignParameterKind Kind, int Power)>();
        var parameters = new List<OpticalDesignParameter>();
        foreach (var request in definitions)
        {
            Require(Enum.IsDefined(request.Kind) && keys.Add((request.SurfaceId, request.Kind, request.Power)),
                TopologyFailureCode.UnsupportedVariable, "Duplicate or undefined topology parameter.");
            Require(source.SurfaceIndices.TryGetValue(request.SurfaceId, out var index), TopologyFailureCode.UnknownBoundary, "The requested surface identity is not present in this topology.");
            Require(double.IsFinite(request.Scale) && request.Scale > 0 && double.IsFinite(request.LowerBound)
                && double.IsFinite(request.UpperBound) && request.LowerBound <= request.UpperBound,
                TopologyFailureCode.InvalidVariableBounds, "Physical bounds and positive scales must be finite.");
            var image = request.SurfaceId == source.Topology.Image.Id;
            var owners = source.Topology.Elements.Where(item => item.FrontSurfaceId == request.SurfaceId || item.BackSurfaceId == request.SurfaceId).ToArray();
            var element = owners.FirstOrDefault();
            Require(image || element is not null, TopologyFailureCode.ProtectedElement, "The reference and independent stop are not optimization variables.");
            Require(image ? !source.Topology.Image.Protected : owners.All(owner => !owner.Protected), TopologyFailureCode.ProtectedElement, "Every owner of a shared face, or the requested image plane, must permit this variable.");
            Require(!image || request.Kind == OpticalDesignParameterKind.AxialPosition && request.Power == 0,
                TopologyFailureCode.UnsupportedVariable, "Only an explicitly unprotected image position is supported.");
            if (!image)
            {
                var face = element!.FrontSurfaceId == request.SurfaceId ? element.Front : element.Back;
                Require(request.Kind is OpticalDesignParameterKind.AxialPosition or OpticalDesignParameterKind.Curvature
                    or OpticalDesignParameterKind.Conic or OpticalDesignParameterKind.EvenAsphereCoefficient,
                    TopologyFailureCode.UnsupportedVariable, "V1 bindings use independent axial/shape coordinates with fixed catalog materials.");
                Require(request.Kind == OpticalDesignParameterKind.AxialPosition || face.Profile.Kind != TopologyProfileKind.Plane,
                    TopologyFailureCode.UnsupportedVariable, "Changing plane type requires a new topology/profile branch.");
                Require(request.Kind != OpticalDesignParameterKind.EvenAsphereCoefficient
                    || face.Profile.Kind == TopologyProfileKind.EvenAsphere && request.Power is >= 4 and <= 18
                    && request.Power % 2 == 0 && request.Power <= face.Profile.MaximumEvenPower,
                    TopologyFailureCode.UnsupportedVariable, "The requested explicit even power is not present in the declared profile order.");
            }
            Require(request.Kind == OpticalDesignParameterKind.EvenAsphereCoefficient || request.Power == 0,
                TopologyFailureCode.UnsupportedVariable, "Only an asphere coefficient carries a polynomial power.");
            parameters.Add(new(index, request.Kind, request.Scale,
                CoefficientIndex: request.Kind == OpticalDesignParameterKind.EvenAsphereCoefficient ? request.Power / 2 - 1 : 0)
            { OriginOffset = request.Kind == OpticalDesignParameterKind.AxialPosition ? source.GlobalToCoreZOffset : 0 });
        }
        var initial = OpticalDesignParameters.Read(source.CreateOptic(), parameters);
        var slots = definitions.Select((request, index) => new TopologyParameterSlot(request, parameters[index], initial[index],
            request.LowerBound / request.Scale, request.UpperBound / request.Scale)).ToImmutableArray();
        foreach (var slot in slots)
            Require(double.IsFinite(slot.InitialCoordinate) && double.IsFinite(slot.LowerCoordinate) && double.IsFinite(slot.UpperCoordinate)
                && slot.InitialCoordinate >= slot.LowerCoordinate && slot.InitialCoordinate <= slot.UpperCoordinate,
                TopologyFailureCode.InvalidVariableBounds, "The initial parameter must lie in representable bounds.");
        return new(source.BindingFingerprint, slots);
    }

    public static CompiledTopology Apply(CompiledTopology template, BoundTopologyParameters binding, IReadOnlyList<double> coordinates)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(coordinates);
        Require(template.BindingFingerprint == binding.SourceFingerprint, TopologyFailureCode.IdentityMismatch, "Variables belong to another topology, material set, bounds contract or coordinate frame.");
        Require(coordinates.Count == binding.Slots.Length && coordinates.Select((value, index) =>
            double.IsFinite(value) && value >= binding.Slots[index].LowerCoordinate && value <= binding.Slots[index].UpperCoordinate).All(valid => valid),
            TopologyFailureCode.InvalidVariableBounds, "Optimizer coordinates exceed the declared bounds.");
        var axial = binding.Slots.Select((slot, index) => (slot, index))
            .Where(item => item.slot.Request.Kind == OpticalDesignParameterKind.AxialPosition).ToArray();
        if (axial.Length == 0)
        {
            var updated = OpticalDesignParameters.Apply(template.Snapshot, binding.CoreParameters, coordinates);
            return TopologyCompiler.Update(template, updated.ToSnapshot());
        }
        // Axial requests are already absolute coordinates in the declared mechanical frame.
        // Do not recover them (or fixed planes) by subtracting then accumulating Core thicknesses:
        // that round trip can move a protected stop by one ULP and reject an otherwise legal step.
        // Core still applies all shape parameters and performs all subsequent physical calculations.
        var shape = binding.Slots.Select((slot, index) => (slot, index))
            .Where(item => item.slot.Request.Kind != OpticalDesignParameterKind.AxialPosition).ToArray();
        var shaped = shape.Length == 0 ? template : TopologyCompiler.Update(template,
            OpticalDesignParameters.Apply(template.Snapshot, shape.Select(item => item.slot.CoreParameter).ToArray(),
                shape.Select(item => coordinates[item.index]).ToArray()).ToSnapshot());
        var positions = axial.ToDictionary(item => item.slot.Request.SurfaceId, item => coordinates[item.index] * item.slot.Request.Scale);
        var topology = shaped.Topology with
        {
            Elements = shaped.Topology.Elements.Select(element =>
            {
                var front = positions.GetValueOrDefault(element.FrontSurfaceId, element.FrontZMillimeters);
                var back = positions.GetValueOrDefault(element.BackSurfaceId, element.BackZMillimeters);
                return positions.ContainsKey(element.FrontSurfaceId) || positions.ContainsKey(element.BackSurfaceId)
                    ? element with { FrontZMillimeters = front, ThicknessMillimeters = back - front } : element;
            }).ToImmutableArray(),
            Image = positions.TryGetValue(shaped.Topology.Image.Id, out var imageZ)
                ? shaped.Topology.Image with { ZMillimeters = imageZ } : shaped.Topology.Image
        };
        return TopologyCompiler.Compile(topology, template.Constraints);
    }
}
