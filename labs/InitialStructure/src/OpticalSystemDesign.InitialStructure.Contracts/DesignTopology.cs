using System.Collections.Immutable;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Differentiation;

namespace OpticalSystemDesign.InitialStructure.Contracts;

public static class AutonomousTopologyIdentity
{
    public const string Name = "AutonomousTopologyV1";
    public const int SchemaVersion = 1;
    public const int MaximumElements = 16;
    public const int MaximumEvenPower = 18;
    public const double MinimumPhysicalSemiDiameter = .001;
}

public enum TopologyProfileKind { Plane, Standard, EvenAsphere }

public sealed record TopologyAsphereTerm(int Power, double Coefficient);

/// <summary>Polynomial powers are explicit; the supported r² coefficient is identically zero.</summary>
public sealed record TopologyProfile
{
    public TopologyProfileKind Kind { get; init; } = TopologyProfileKind.Standard;
    public double CurvaturePerMillimeter { get; init; }
    public double Conic { get; init; }
    public int MaximumEvenPower { get; init; }
    public ImmutableArray<TopologyAsphereTerm> Terms { get; init; } = [];
}

public sealed record TopologyFace
{
    public TopologyProfile Profile { get; init; } = new();
    public double ClearSemiDiameterMillimeters { get; init; } = 5;
    public double? MechanicalSemiDiameterMillimeters { get; init; }
}

public sealed record LensElement
{
    public string Id { get; init; } = "";
    public string FrontSurfaceId { get; init; } = "";
    public string BackSurfaceId { get; init; } = "";
    public double FrontZMillimeters { get; init; }
    public double ThicknessMillimeters { get; init; } = 1;
    public string Material { get; init; } = "N-BK7";
    public TopologyFace Front { get; init; } = new();
    public TopologyFace Back { get; init; } = new();
    public bool Protected { get; init; }
    public bool Transition { get; init; }

    [JsonIgnore] public double BackZMillimeters => FrontZMillimeters + ThicknessMillimeters;
    [JsonIgnore] public double ClearSemiDiameterMillimeters => Math.Max(Front.ClearSemiDiameterMillimeters, Back.ClearSemiDiameterMillimeters);
}

public sealed record TopologyReferencePlane(string Id, double ZMillimeters);
public sealed record TopologyStop(string Id, double ZMillimeters, double ClearSemiDiameterMillimeters, double? MechanicalSemiDiameterMillimeters = null);
public sealed record TopologyImagePlane(string Id, double ZMillimeters, bool Protected = true);
public sealed record TopologyField(string Label, double XAngleDegrees, double YAngleDegrees, double Weight = 1);

/// <summary>An explicit adjacent glass/glass boundary. Both elements name the same physical surface.</summary>
public sealed record TopologyCementedInterface(string LeftElementId, string RightElementId, string SurfaceId);

public sealed record TopologyCementedInterfaces
{
    public int Version { get; init; } = 1;
    public ImmutableArray<TopologyCementedInterface> Interfaces { get; init; } = [];
}

/// <summary>Immutable lens-level prescription. Positions use one fixed mechanical coordinate frame.</summary>
public sealed record DesignTopology
{
    public int SchemaVersion { get; init; } = AutonomousTopologyIdentity.SchemaVersion;
    public string Algorithm { get; init; } = AutonomousTopologyIdentity.Name;
    public string Name { get; init; } = "Autonomous topology";
    public ObjectConjugateMode Conjugate { get; init; } = ObjectConjugateMode.Infinite;
    public TopologyReferencePlane Reference { get; init; } = new("reference", 0);
    public TopologyStop Stop { get; init; } = new("stop", 0, 5);
    public TopologyImagePlane Image { get; init; } = new("image", 60);
    public ImmutableArray<LensElement> Elements { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TopologyCementedInterfaces? CementedInterfaces { get; init; }
    public ImmutableArray<TopologyField> Fields { get; init; } = [new("Axis", 0, 0), new("Edge", 0, 5)];
    public ImmutableArray<WavelengthSpecification> Wavelengths { get; init; } =
    [
        new() { Label = "F", Nanometers = 486.1, Weight = 1 },
        new() { Label = "d", Nanometers = 587.6, Weight = 1, IsPrimary = true },
        new() { Label = "C", Nanometers = 656.3, Weight = 1 }
    ];
    public ImmutableArray<string> GlassCatalogs { get; init; } = [];
    public bool RayAimingEnabled { get; init; } = true;
    public bool MatchRefractiveIndexData { get; init; } = true;
    public double TemperatureCelsius { get; init; } = 20;
    public double PressureAtmospheres { get; init; } = 1;

    [JsonIgnore] public double MechanicalTrackMillimeters => Image.ZMillimeters - Reference.ZMillimeters;
    [JsonIgnore] public double OpticalTrackMillimeters => Elements.IsDefaultOrEmpty ? double.NaN : Image.ZMillimeters - Elements[0].FrontZMillimeters;
    [JsonIgnore] public bool HasTransitionElements => !Elements.IsDefault && Elements.Any(element => element.Transition);
}

/// <summary>Structural bounds, separate from optical/manufacturing acceptance after real tracing.</summary>
public sealed record TopologyConstraints
{
    public int MinimumElementCount { get; init; } = 1;
    public int MaximumElementCount { get; init; } = 8;
    public double MinimumCenterThicknessMillimeters { get; init; } = .1;
    public double MinimumAirGapMillimeters { get; init; } = .05;
    public double MinimumBackFocusMillimeters { get; init; } = .1;
    public double MaximumMechanicalTrackMillimeters { get; init; } = 10_000;
    public bool AllowTransitionElements { get; init; }
    public ImmutableArray<string> AllowedGlassNames { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TopologyTransitionAllowance? TransitionAllowance { get; init; }
}

public sealed record TopologyTransitionAllowance(string ElementId, double MinimumCenterThicknessMillimeters, double MinimumEdgeThicknessMillimeters);

public enum TopologyFailureCode
{
    InvalidIdentity, InvalidInput, UnsupportedConjugate, UnsupportedProfile, UnsupportedFeature,
    UnsupportedMaterial, InvalidProfile, InvalidAperture, DuplicateId, ElementLimit,
    AxialOrder, CenterThickness, AirGap, BackFocus, TrackLength, StopInsideElement,
    ProtectedElement, UnknownElement, UnknownBoundary, NoSpace, TransitionNotAllowed,
    UnsupportedVariable, InvalidVariableBounds, IdentityMismatch
}

public sealed record TopologyFailure(TopologyFailureCode Code, string Message);

public sealed class TopologyValidationException(TopologyFailureCode code, string message) : ArgumentException(message)
{
    public TopologyFailureCode Code { get; } = code;
}

public sealed record TopologyAirInterval(string LeftSurfaceId, string RightSurfaceId, double LeftZMillimeters, double RightZMillimeters)
{
    [JsonIgnore] public double LengthMillimeters => RightZMillimeters - LeftZMillimeters;
}

public enum TopologyOperationKind { Insert, Delete, Split, Merge, Material, Profile, ScaleForDeletion }
public sealed record TopologyChange(TopologyOperationKind Operation, string ParentFingerprint, string ChildFingerprint,
    ImmutableArray<string> CreatedElementIds, ImmutableArray<string> RemovedElementIds);
public sealed record TopologyEditResult(DesignTopology? Topology, TopologyChange? Change, TopologyFailure? Failure)
{
    [JsonIgnore] public bool Succeeded => Topology is not null && Change is not null && Failure is null;
}

/// <summary>Bounds and scale are physical; the numerical optimizer coordinate is physical / Scale.</summary>
public sealed record TopologyVariableRequest(string SurfaceId, OpticalDesignParameterKind Kind,
    double LowerBound, double UpperBound, double Scale = 1, int Power = 0);
