using System.Collections.Immutable;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.InitialStructure.Contracts;

public sealed record ParaxialRecoveryOptions
{
    public int MaximumAcceptedSteps { get; init; } = 30;
    public int MaximumFunctionEvaluations { get; init; } = 400;
    public TimeSpan TimeLimit { get; init; } = TimeSpan.FromMinutes(1);
    public double AScale { get; init; } = .01;
    public double FocalCScale { get; init; } = .01;
    public double ATolerance { get; init; } = 1e-4;
    public double FocalCTolerance { get; init; } = 1e-4;
    public double MinimumChangeWeight { get; init; } = 1e-6;
    public double RelativeDifferenceStep { get; init; } = 6.055454452393343e-6;
    public double RelativeRankTolerance { get; init; } = 1e-8;
    public int ProfileRadialSamples { get; init; } = 64;
}

public enum ParaxialRecoveryTermination
{
    Recovered, RankDeficient, NoFreeVariables, GeometryBlocked, NoParaxialRecovery,
    BudgetExhausted, Cancelled, TimeLimit, Unsupported
}

public sealed record ParaxialRecoveryStep(int AcceptedStep, ImmutableArray<double> Coordinates,
    double AError, double FocalCError, double CoordinateDisplacementNorm);
public sealed record ParaxialRecoveryTrial(int FunctionEvaluations, bool Accepted, ImmutableArray<double> BaseCoordinates,
    ImmutableArray<double> TrialCoordinates, double Radius, double NextRadius, double PredictedReduction,
    double? ActualReduction, double? ReductionRatio);

/// <summary>Function requests include the parent reference and every child/difference attempt.
/// Matrix/probe counts charge actual attempted Core calls, including rejected points.</summary>
public sealed record ParaxialRecoveryWork(int FunctionEvaluations, int DifferenceEvaluations,
    int MatrixEvaluations, int JacobianBuilds, long GeometryProbes, TimeSpan Elapsed);

public sealed record ParaxialRecoveryResult
{
    public string Algorithm { get; init; } = AutonomousTopologyIdentity.Name;
    public required string ProtocolFingerprint { get; init; }
    public required DesignTopology Topology { get; init; }
    public required ParaxialRecoveryTermination Termination { get; init; }
    public required ParaxialRecoveryWork Work { get; init; }
    public ImmutableArray<TopologyVariableRequest> Variables { get; init; } = [];
    public ImmutableArray<double> ChildInitialCoordinates { get; init; } = [];
    public ImmutableArray<double> EndpointCoordinates { get; init; } = [];
    public ParaxialTransferMatrix? ParentMatrix { get; init; }
    public ParaxialTransferMatrix? InitialMatrix { get; init; }
    public ParaxialTransferMatrix? EndpointMatrix { get; init; }
    public double? InitialAError { get; init; }
    public double? InitialFocalCError { get; init; }
    public double? EndpointAError { get; init; }
    public double? EndpointFocalCError { get; init; }
    public int? InitialJacobianRank { get; init; }
    public ImmutableArray<ParaxialRecoveryStep> Steps { get; init; } = [];
    public ImmutableArray<ParaxialRecoveryTrial> Trials { get; init; } = [];
    public ImmutableArray<string> Diagnostics { get; init; } = [];
}

public sealed record TopologyRestorationOptions
{
    public int MaximumFunctionEvaluations { get; init; } = 1000;
    public int MaximumGradientEvaluations { get; init; } = 400;
    public TimeSpan TimeLimit { get; init; } = TimeSpan.FromMinutes(10);
    public ParaxialRecoveryOptions Recovery { get; init; } = new();
    public FixedTopologyOptions LocalCompensation { get; init; } = new();
    public FixedTopologyOptions FullOptimization { get; init; } = new() { MaximumUpdates = 160, MaximumFunctionEvaluations = 162, MaximumGradientEvaluations = 161 };
}

public enum TopologyRestorationTermination
{
    Completed, EditRejected, RecoveryFailed, CompensationFailed, BudgetExhausted, Cancelled, TimeLimit, Unsupported
}

/// <summary>Aggregates phase requests, not equivalent-cost optical evaluations. MatrixEvaluations
/// covers the explicit recovery interface; internal fixed-optimizer paraxial calls are not separately instrumented.</summary>
public sealed record TopologyRestorationWork(int FunctionEvaluations, int GradientEvaluations,
    int ValidationEvaluations, int MatrixEvaluations, long TracedRays, long GeometryProbes, TimeSpan Elapsed);

public sealed record TopologyRestorationResult
{
    public string Algorithm { get; init; } = AutonomousTopologyIdentity.Name;
    public required string ProtocolFingerprint { get; init; }
    public required DesignTopology Parent { get; init; }
    public DesignTopology? EditedChild { get; init; }
    public required DesignTopology Endpoint { get; init; }
    public TopologyConstraints Constraints { get; init; } = new();
    public TopologyTargets Targets { get; init; } = new();
    public TopologyRestorationOptions Options { get; init; } = new();
    public ImmutableArray<TopologyVariableRequest> RecoveryVariables { get; init; } = [];
    public ImmutableArray<TopologyVariableRequest> LocalVariables { get; init; } = [];
    public ImmutableArray<TopologyVariableRequest> FullVariables { get; init; } = [];
    public TopologyChange? Change { get; init; }
    public required TopologyRestorationTermination Termination { get; init; }
    public required TopologyRestorationWork Work { get; init; }
    public ParaxialRecoveryResult? Recovery { get; init; }
    public FixedTopologyResult? LocalCompensation { get; init; }
    public FixedTopologyResult? FullOptimization { get; init; }
    public TopologyAssessment? FinalAssessment { get; init; }
    public ImmutableArray<string> Diagnostics { get; init; } = [];
}
