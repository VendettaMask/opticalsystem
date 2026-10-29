using System.Collections.Immutable;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.Differentiation;

namespace OpticalSystemDesign.InitialStructure.Contracts;

public sealed record AutonomousSearchOptions
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BoundedCandidateSearchOptions? BoundedSearch { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PopulationExplorationOptions? PopulationExploration { get; init; }
    /// <summary>Explicit preparation configuration. Only the separate seed-preparation command executes it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ParaxialSeedSearchOptions? SeedPreparation { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TopologyDiversityOptions? PoolDiversity { get; init; }
    /// <summary>Explicit finite reduction: only qualified parents dispatch deletion; failed children remain evidence.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? QualifiedReductionOnly { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CheckStageFeasibility { get; init; }
    /// <summary>Defer optional post-local dense checks when their full quota would consume the full-compensation allowance.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ProtectCompensationRayBudget { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ProtectPostRecoveryRayBudget { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? UseAxialVariablesInRecovery { get; init; }
    /// <summary>Release conic coordinates during compensation without enabling structural/profile branches.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CompensateConic { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? FixedTopologyOnly { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SkipParaxialRecovery { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TopologyExpansionOptions? Expansion { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TopologyAxialCompensationOptions? AxialCompensation { get; init; }
    public int MaximumTrials { get; init; } = 32;
    public int CandidatesPerElementCount { get; init; } = 4;
    public int BranchesPerParent { get; init; } = 6;
    public int MaximumFunctionEvaluations { get; init; } = 10_000;
    public int MaximumGradientEvaluations { get; init; } = 2_000;
    public long MaximumTracedRays { get; init; } = 10_000_000;
    public long MaximumStageTracedRays { get; init; } = 2_000_000;
    public long FinalValidationRayReserve { get; init; } = 200_000;
    public TimeSpan TimeLimit { get; init; } = TimeSpan.FromMinutes(30);
    public long Seed { get; init; } = 42;
    public double CurvatureBoundPerMillimeter { get; init; } = .08;
    public ImmutableArray<string> InsertionMaterials { get; init; } = ["N-BK7"];
    public ParaxialRecoveryOptions Recovery { get; init; } = new();
    public FixedTopologyOptions Local { get; init; } = new() { ComputeMode = DesignComputeMode.Cpu, PerformFinalAssessment = false };
    public FixedTopologyOptions Full { get; init; } = new()
    {
        ComputeMode = DesignComputeMode.Cpu,
        PerformFinalAssessment = false,
        MaximumUpdates = 160,
        MaximumFunctionEvaluations = 161,
        MaximumGradientEvaluations = 161
    };
}

/// <summary>Per-trial physical bounds, anchored before recovery and retained across stages and resume.</summary>
public sealed record TopologyAxialCompensationOptions
{
    public double MaximumDisplacementMillimeters { get; init; } = .25;
    public double CoordinateScaleMillimeters { get; init; } = .1;
    /// <summary>Empty selects all unprotected retained elements. Reference, stop and image remain fixed.</summary>
    public ImmutableArray<string> ElementIds { get; init; } = [];
}

public sealed record TopologyAxialAnchor(string SurfaceId, double ZMillimeters);

public sealed record AutonomousSearchPlan
{
    public required ImmutableArray<DesignTopology> Seeds { get; init; }
    public TopologyTargets Targets { get; init; } = new();
    public TopologyConstraints Constraints { get; init; } = new();
    public AutonomousSearchOptions Options { get; init; } = new();
}

/// <summary>CPU submissions include aiming, failed rays and diagnostics. Matrix/FD counts cover explicit recovery only.</summary>
public sealed record AutonomousSearchWork
{
    public int FunctionEvaluations { get; init; }
    public int GradientEvaluations { get; init; }
    public int ValidationEvaluations { get; init; }
    public int MatrixEvaluations { get; init; }
    public int DifferenceEvaluations { get; init; }
    public int JacobianBuilds { get; init; }
    public long TracedRays { get; init; }
    public long GeometryProbes { get; init; }
    public TimeSpan Elapsed { get; init; }
}

public enum AutonomousSearchPhase { Recovery, Local, Full, Validation, TransitionValidation, Feasibility, Screening }
public enum AutonomousSearchStatus { Ready, Running, Cancelled, BudgetExhausted, Completed }

public sealed record AutonomousSearchCandidate(string Id, string? ParentId, DesignTopology Topology,
    TopologyAssessment Assessment, double Score, int OperationCursor = 0);

public sealed record AutonomousSearchPending
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PopulationCoordinateProposal? PopulationProposal { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AutonomousSearchPhase? AfterScreening { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<TopologyAxialAnchor>? AxialAnchors { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AutonomousSearchPhase? AfterFeasibility { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GradualDeletionState? GradualDeletion { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TopologyChange? Change { get; init; }
    public required string TrialId { get; init; }
    public string? ParentId { get; init; }
    public required string Operation { get; init; }
    public required DesignTopology Template { get; init; }
    public required AutonomousSearchPhase Phase { get; init; }
    public FixedTopologyOptions? StageOptions { get; init; }
    public FixedTopologyContinuation? Continuation { get; init; }
    public ImmutableArray<TopologyVariableRequest> Variables { get; init; } = [];
    public AutonomousSearchWork PhaseConsumed { get; init; } = new();
}

public sealed record AutonomousSearchReservation(string Id, AutonomousSearchPhase Phase, AutonomousSearchWork Maximum);
public sealed record AutonomousSearchEvent(int Sequence, string TrialId, string? ParentId, string Operation,
    AutonomousSearchPhase? Phase, string Outcome, string? Detail, AutonomousSearchWork Work,
    DesignTopology? Endpoint = null, ParaxialRecoveryResult? Recovery = null, FixedTopologyResult? Optimization = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TopologyAssessment? Assessment { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TopologyChange? Change { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GradualDeletionState? GradualDeletion { get; init; }
}

/// <summary>Independent durable schema. Loading never executes this state.</summary>
public sealed record AutonomousSearchCheckpoint
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BoundedSearchStopReason? StopReason { get; init; }
    public int SchemaVersion { get; init; } = 1;
    public string Algorithm { get; init; } = AutonomousTopologyIdentity.Name;
    public required string RunId { get; init; }
    public required AutonomousSearchPlan Plan { get; init; }
    public required string PlanFingerprint { get; init; }
    public long Revision { get; init; }
    public AutonomousSearchStatus Status { get; init; } = AutonomousSearchStatus.Ready;
    public int SeedCursor { get; init; }
    public int TrialCursor { get; init; }
    public ulong RandomState { get; init; }
    public AutonomousSearchWork Consumed { get; init; } = new();
    public ImmutableArray<AutonomousSearchCandidate> Candidates { get; init; } = [];
    public ImmutableArray<string> Pool { get; init; } = [];
    public ImmutableArray<AutonomousSearchCandidate> AcceptedArchive { get; init; } = [];
    public ImmutableArray<AutonomousSearchEvent> Events { get; init; } = [];
    public AutonomousSearchPending? Pending { get; init; }
    public AutonomousSearchReservation? Reservation { get; init; }
}
