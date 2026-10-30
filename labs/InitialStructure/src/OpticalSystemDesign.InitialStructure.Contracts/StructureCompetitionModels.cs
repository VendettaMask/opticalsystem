using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace OpticalSystemDesign.InitialStructure.Contracts;

/// <summary>Initial marginal-path proposals, not a claim of prescribed element powers.</summary>
public enum StructureRayPathFamily { Balanced, FrontBending, RearBending, AlternatingBending }

public sealed record StructureCompetitionStage(string Name, double FieldFraction, double PupilFraction);

/// <summary>Explicit preparation protocol. Intermediate specifications never qualify a design.</summary>
public sealed record StructureCompetitionOptions
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StructureSearchStrategy? SearchStrategy { get; init; }
    public int Version { get; init; } = 1;
    public ImmutableArray<StructureRayPathFamily> Families { get; init; } =
        [StructureRayPathFamily.Balanced, StructureRayPathFamily.FrontBending,
         StructureRayPathFamily.RearBending, StructureRayPathFamily.AlternatingBending];
    public ImmutableArray<StructureCompetitionStage> Stages { get; init; } =
        [new("Explore", .5, .7), new("Expand", .75, .85), new("Original", 1, 1)];
    public int UpdatesPerStage { get; init; } = 4;
    public int MaximumGradientEvaluations { get; init; } = 200;
    public int MaximumFinalists { get; init; } = 2;
    public double SurvivalFraction { get; init; } = .67;
    public long MaximumStageRaysPerCandidate { get; init; } = 15_000;
    public long ScreeningRaysPerCandidate { get; init; } = 2_500;
    public long FinalValidationRayReserve { get; init; } = 100_000;
    public TimeSpan MaximumStageTimePerCandidate { get; init; } = TimeSpan.FromSeconds(8);
    public double FinalValidationTimeReserveFraction { get; init; } = .2;
}

public sealed record StructureCompetitionScreen(bool Completed, bool GeometryPassed,
    int CompletedFields, int ExpectedFields, double? MinimumValidFraction, bool BoundaryChecksPassed,
    double? WorstRmsMillimeters, double? WorstMaximumMillimeters, double? QualityScore,
    string Outcome, long TracedRays, long GeometryProbes);

public sealed record StructureCompetitionRound(int StageIndex, StructureCompetitionStage Stage,
    string CandidateId, StructureCompetitionScreen Screen, bool Selected,
    StructureCompetitionOptimization? Optimization, AutonomousSearchWork Work, StructureCompetitionAllocation Allocation);

public sealed record StructureCompetitionAllocation(int MaximumFunctionEvaluations, int MaximumGradientEvaluations,
    long MaximumTracedRays, long ScreeningRayReserve, TimeSpan MaximumOptimizationTime)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TimeSpan? MaximumStageTime { get; init; }
}

/// <summary>Reviewable phase evidence, without replay state or unbounded derivative arrays.</summary>
public sealed record StructureCompetitionOptimization(string Algorithm, string ObjectiveFingerprint,
    FixedTopologyTermination Termination, int AcceptedUpdates, FixedTopologyWork Work,
    double? InitialObjective, double? EndpointObjective, bool EndpointAvailable,
    DesignTopology Topology, TopologyTargets Targets, ImmutableArray<TopologyVariableRequest> Variables,
    ImmutableArray<FixedTopologyStep> Steps, ImmutableArray<string> Diagnostics);

public sealed record StructureCompetitionCandidate(string Id, StructureRayPathFamily Family,
    int StopAfterElement, string Outcome, DesignTopology? Topology, int CompletedStages,
    TopologyAssessment? OriginalAssessment, AutonomousSearchWork Work)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StructureCandidateEvidence? SearchEvidence { get; init; }
}

public sealed record StructureCompetitionResult
{
    public string Algorithm { get; init; } = "initial-structure-competition-v1";
    public required string ProtocolFingerprint { get; init; }
    public ImmutableArray<StructureCompetitionCandidate> Candidates { get; init; } = [];
    public ImmutableArray<StructureCompetitionRound> Rounds { get; init; } = [];
    public ImmutableArray<string> SelectedIds { get; init; } = [];
    /// <summary>Only completed original-specification TopologyValidator results enter this archive.</summary>
    public ImmutableArray<string> QualifiedIds { get; init; } = [];
    public required string StopReason { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StructureSearchEvidence? SearchEvidence { get; init; }
}
