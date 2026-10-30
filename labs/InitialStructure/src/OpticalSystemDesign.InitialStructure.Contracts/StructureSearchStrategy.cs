using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace OpticalSystemDesign.InitialStructure.Contracts;

/// <summary>Opt-in experimental scheduling; null preserves competition v1.</summary>
public sealed record StructureSearchStrategy
{
    public int Version { get; init; } = 1;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PowerSignSearchOptions? PowerSigns { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public QuickAberrationOptions? QuickEvaluation { get; init; }
    public int ChallengeCandidatesPerRound { get; init; } = 1;
    public double FinalRayRecoveryFactor { get; init; } = 2;
    public TimeSpan FinalTimePerCandidate { get; init; } = TimeSpan.FromSeconds(8);
}

public sealed record PowerSignSearchOptions
{
    public int Version { get; init; } = 1;
    public int MaximumPatterns { get; init; } = 16;
    public bool RetainNativeProposals { get; init; } = true;
}

public sealed record QuickAberrationOptions
{
    public int Version { get; init; } = 1;
    public int MaximumProposalsPerStage { get; init; } = 2;
    public double CoordinateStep { get; init; } = .01;
}

public sealed record StructurePrecheck(string Code, bool ProvenInfeasible, double Actual, double Limit, string Assumptions);
public sealed record StructurePhaseCost(string CandidateId, int StageIndex, string Phase, TimeSpan Elapsed,
    int Functions, int Gradients, long Rays, long GeometryProbes, int MatrixRequests);
public sealed record StructureQuickEvidence(string CandidateId, int StageIndex, string Outcome,
    string Units, double? InitialScore, double? EndpointScore, int Proposals, int AcceptedUpdates);
public sealed record StructureChallengeEvidence(int StageIndex, string CandidateId, string ReplacedCandidateId,
    double? SparseQuality, string Purpose);
public sealed record StructureCandidateEvidence(string Source, ImmutableArray<int> RequestedPowerSigns,
    ImmutableArray<double> ConstructedPowersPerMillimeter, bool? SignsVerified);
public sealed record StructureSearchEvidence(ImmutableArray<StructurePrecheck> Prechecks,
    ImmutableArray<StructurePhaseCost> Costs, ImmutableArray<StructureQuickEvidence> QuickEvaluations,
    ImmutableArray<StructureChallengeEvidence> Challenges, long EstimatedFinalRaysPerCandidate,
    long ReservedFinalRays, TimeSpan ReservedFinalTime, string BudgetModel);
