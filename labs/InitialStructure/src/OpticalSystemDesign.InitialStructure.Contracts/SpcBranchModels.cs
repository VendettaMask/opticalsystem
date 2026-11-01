using System.Collections.Immutable;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Optimization;

namespace OpticalSystemDesign.InitialStructure.Contracts;

/// <summary>Explicit research branch policy. Original apertures, targets, permission flags and manufacturing gates remain authoritative.</summary>
public sealed record SpcBranchOptions
{
    public int Version { get; init; } = 1;
    public int MaximumParents { get; init; } = 1;
    public int MaximumGradientEvaluations { get; init; } = 600;
    public int ParentIterations { get; init; } = 80;
    public int BranchIterations { get; init; } = 40;
    public int PhysicalUpdatesPerStage { get; init; } = 12;
    public int MaximumMaterialAlternatives { get; init; } = 2;
    public double GradientTolerance { get; init; } = 1e-7;
    public double CurvatureDifferenceStep { get; init; } = .001;
    public double BranchCoordinateStep { get; init; } = .05;
    public ImmutableArray<double> ThicknessMultipliers { get; init; } = [1, 2, 4];
}

public sealed record SpcStationarityEvidence(string Scope, double Objective, ImmutableArray<double> Coordinates,
    ImmutableArray<double> Gradient, bool InteriorStationary, StationarySpectrum Coarse, StationarySpectrum Fine,
    double CurvatureUncertainty, int StableNegativeDirections, bool Certified);
public sealed record SpcPhysicalStage(string Name, string Material, double ThicknessMillimeters, string Outcome,
    DesignTopology? Topology, TopologyAssessment? Assessment, AutonomousSearchWork Work);
public sealed record SpcOptimizedBranch(string Id, string ParentId, int Sign, string Outcome,
    ImmutableArray<double> InitialCoordinates, ImmutableArray<double> OptimizedCoordinates, double? InitialObjective,
    double? OptimizedObjective, ImmutableArray<SpcPhysicalStage> Stages, DesignTopology? Endpoint, TopologyAssessment? Assessment)
{
    public string? NullOptimizationStopReason { get; init; }
    public ImmutableArray<double> NullGradient { get; init; } = [];
    public bool NullInteriorStationary { get; init; }
    public SpcStationarityEvidence? NullMinimum { get; init; }
}
public sealed record SpcParentEvidence(string Id, string Outcome, DesignTopology Original, DesignTopology? StationaryParent,
    ImmutableArray<TopologyVariableRequest> Variables, SpcStationarityEvidence? ParentStationarity,
    SpcStationarityEvidence? Saddle, string? InterfaceElementId)
{
    public TopologyAssessment? OriginalAssessment { get; init; }
    public TopologyAssessment? StationaryAssessment { get; init; }
}
public sealed record SpcBranchSearchResult
{
    public string Algorithm { get; init; } = "initial-structure-spc-branches-v1";
    public required string ProtocolFingerprint { get; init; }
    public required string StopReason { get; init; }
    public required AutonomousSearchWork Work { get; init; }
    public ImmutableArray<SpcParentEvidence> Parents { get; init; } = [];
    public ImmutableArray<SpcOptimizedBranch> Branches { get; init; } = [];
    public ImmutableArray<string> QualifiedBranchIds { get; init; } = [];
}
