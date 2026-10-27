using System.Collections.Immutable;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Raytrace;

namespace OpticalSystemDesign.InitialStructure.Contracts;

public sealed record TopologyTargets
{
    public double EffectiveFocalLengthMillimeters { get; init; } = 50;
    public double FNumber { get; init; } = 12.5;
    public double FocalLengthRelativeTolerance { get; init; } = .02;
    public double FNumberRelativeTolerance { get; init; } = .02;
    public double MaximumRmsRadiusMillimeters { get; init; } = .05;
    public double MaximumSpotRadiusMillimeters { get; init; } = .15;
    public double MaximumDistortionFraction { get; init; } = .03;
    public double MinimumValidRayFraction { get; init; } = .98;
    public double MinimumEdgeThicknessMillimeters { get; init; } = .1;
    public double MinimumEdgeAirGapMillimeters { get; init; } = .01;
    public double MaximumOpticalTrackMillimeters { get; init; } = 10_000;
    public double MaximumAbsoluteSurfaceSlope { get; init; } = 10;
}

/// <summary>Immutable within an objective. Final acceptance always uses the separate 21/10/32 protocol.</summary>
public sealed record TopologyTrainingSampling
{
    /// <summary>Add independent center/boundary clearance residuals; never mix them into Gaussian spot statistics.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IncludeBoundaryClearance { get; init; }
    /// <summary>Include independent checks about the integration centroid in the maximum-radius constraint.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IncludeBoundaryMaximum { get; init; }
    public int FieldsPerDirection { get; init; } = 3;
    public int PupilRings { get; init; } = 3;
    public int PupilArms { get; init; } = 12;
    public int ProfileRadialSamples { get; init; } = 64;
}

public sealed record FixedTopologyOptions
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? TransmissionFirst { get; init; }
    /// <summary>Choose up to this many independent boundary/constraint sensitivity columns per proposal.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ActiveParameterLimit { get; init; }
    /// <summary>Explicit strategy identity; unversioned development records remain readable but cannot execute this policy.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TransmissionPolicyVersion { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? RequireDescent { get; init; }
    public int MaximumUpdates { get; init; } = 80;
    public int MaximumFunctionEvaluations { get; init; } = 82;
    public int MaximumGradientEvaluations { get; init; } = 81;
    public TimeSpan TimeLimit { get; init; } = TimeSpan.FromMinutes(5);
    public double LearningRate { get; init; } = .01;
    public DesignComputeMode ComputeMode { get; init; } = DesignComputeMode.Automatic;
    public TopologyTrainingSampling Sampling { get; init; } = new();
    public bool PerformFinalAssessment { get; init; } = true;
}

public sealed record TopologyFieldCoordinate(double X, double Y);
public sealed record TopologyMetricViolation(string Code, double? Actual, double Limit);
public sealed record TopologyFieldAssessment(TopologyFieldCoordinate Field, int AttemptedRays, int ValidRays,
    int CheckAttemptedRays, int CheckValidRays, double? RmsRadiusMillimeters, double? MaximumRadiusMillimeters,
    double? DistortionFraction, ImmutableArray<double> WavelengthValidFractions);

public sealed record TopologyAssessment
{
    public required string OpticFingerprint { get; init; }
    public required string ProtocolFingerprint { get; init; }
    public required TopologyTargets Targets { get; init; }
    public TopologyConstraints StructuralConstraints { get; init; } = new();
    public double? EffectiveFocalLengthMillimeters { get; init; }
    public double? FNumber { get; init; }
    public ImmutableArray<TopologyFieldAssessment> Fields { get; init; } = [];
    public ImmutableArray<TopologyMetricViolation> Violations { get; init; } = [];
    public bool Completed { get; init; }
    public bool MeetsTargets { get; init; }
    public long TracedRayCount { get; init; }
    public long GeometryProbes { get; init; }
    public string? Failure { get; init; }
}

public sealed record TopologyObjectiveEvaluation
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<TopologyConstraintTangent>? ConstraintTangents { get; init; }
    public required string LayoutFingerprint { get; init; }
    public required string OpticFingerprint { get; init; }
    public ImmutableArray<double> Residuals { get; init; } = [];
    public ImmutableArray<double> Gradient { get; init; } = [];
    public ImmutableArray<ImmutableArray<double>> Jacobian { get; init; } = [];
    public double? Objective { get; init; }
    public bool Available { get; init; }
    public string? Failure { get; init; }
    public long TracedRayCount { get; init; }
    public long GeometryProbes { get; init; }
    public DesignComputeExecution? Execution { get; init; }
    public SequentialGpuExecution? PropagationExecution { get; init; }
}
public sealed record TopologyConstraintTangent(int Row, double Value, ImmutableArray<double> Derivatives);

public enum FixedTopologyTermination { UpdateLimit, Converged, BudgetExhausted, TimeLimit, Cancelled, DomainFailure, Unsupported, Stalled }
public sealed record FixedTopologyWork(int FunctionEvaluations, int GradientEvaluations, int ValidationEvaluations,
    long TracedRays, long GeometryProbes, long? GpuDerivativeRays, long? GpuBatches, TimeSpan Elapsed, int CompletedGradientEvaluations);
public sealed record FixedTopologyStep(int Attempt, bool Accepted, double? Objective, string? Failure)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<int>? ActiveParameters { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TopologyTransmissionStep? Transmission { get; init; }
}
public sealed record TopologyTransmissionStep(double BeforeClearance, double AfterClearance,
    double BeforeOtherGates, double AfterOtherGates, int ModelRank);

/// <summary>Same-template continuation only. Structural/variable/protocol changes require a fresh phase.</summary>
public sealed record FixedTopologyContinuation
{
    public required string PhaseFingerprint { get; init; }
    public required AutoLensAdamState Adam { get; init; }
    public required double LearningRate { get; init; }
    public required FixedTopologyWork Work { get; init; }
    public int AcceptedUpdates { get; init; }
    public ImmutableArray<FixedTopologyStep> Steps { get; init; } = [];
    public TopologyObjectiveEvaluation? InitialEvaluation { get; init; }
    public TopologyObjectiveEvaluation? EndpointEvaluation { get; init; }
}
public sealed record FixedTopologyResult
{
    public string Algorithm { get; init; } = AutonomousTopologyIdentity.Name;
    public required string ObjectiveFingerprint { get; init; }
    public required DesignTopology Topology { get; init; }
    public TopologyConstraints Constraints { get; init; } = new();
    public TopologyTargets Targets { get; init; } = new();
    public ImmutableArray<TopologyVariableRequest> Variables { get; init; } = [];
    public required FixedTopologyTermination Termination { get; init; }
    public required FixedTopologyWork Work { get; init; }
    public int AcceptedUpdates { get; init; }
    public ImmutableArray<FixedTopologyStep> Steps { get; init; } = [];
    public TopologyObjectiveEvaluation? InitialEvaluation { get; init; }
    public TopologyObjectiveEvaluation? EndpointEvaluation { get; init; }
    public TopologyAssessment? FinalAssessment { get; init; }
    public ImmutableArray<string> Diagnostics { get; init; } = [];
    public FixedTopologyContinuation? Continuation { get; init; }
}
