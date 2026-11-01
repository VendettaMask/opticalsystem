using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpticalSystemDesign.InitialStructure.Contracts;

public enum SeedPreparationStatus { Ready, Running, Suspended, Completed, BudgetExhausted, TimeLimit, Interrupted }

/// <summary>Independent preparation identity; neither a published lens nor a main-search checkpoint.</summary>
public sealed record SeedPreparationCheckpoint
{
    public int Version { get; init; } = 1;
    public required string RunId { get; init; }
    public long Revision { get; init; }
    public required string PlanFingerprint { get; init; }
    public required AutonomousSearchPlan Plan { get; init; }
    public required ParaxialSeedSearchOptions Options { get; init; }
    public required string ProtocolFingerprint { get; init; }
    public string Mode { get; init; } = "Cold";
    public string KernelFingerprint { get; init; } = "";
    public bool ContinueIntoMain { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SpcPreparationState? Spc { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? EngineState { get; init; }
    public SeedPreparationStatus Status { get; init; }
    public string Stage { get; init; } = "Construction";
    public int Cursor { get; init; }
    public ulong RandomState { get; init; }
    public ImmutableArray<ColdStartConstruction> Candidates { get; init; } = [];
    public ImmutableArray<ColdStartDesignPhase> Phases { get; init; } = [];
    public DesignTopology? Endpoint { get; init; }
    public TopologyAssessment? Assessment { get; init; }
    public double RankingLoss { get; init; } = double.PositiveInfinity;
    public double RankingQuality { get; init; } = double.PositiveInfinity;
    public ImmutableArray<TopologyVariableRequest> Variables { get; init; } = [];
    public AutonomousSearchWork Consumed { get; init; } = new();
    public PreparationPhaseCheckpoint? Pending { get; init; }
    public ParaxialSeedSearchResult? Result { get; init; }
    public string? StopReason { get; init; }
    public int LostReservations { get; init; }
    public TimeSpan PersistenceElapsed { get; init; }
}

/// <summary>Original anchored phase source/options; all counters are cumulative within this phase.</summary>
public sealed record PreparationPhaseCheckpoint
{
    public required string Name { get; init; }
    public required DesignTopology Source { get; init; }
    public required ImmutableArray<TopologyVariableRequest> Variables { get; init; }
    public FixedTopologyOptions? Options { get; init; }
    public FixedTopologyContinuation? Continuation { get; init; }
    public FixedTopologyResult? Optimized { get; init; }
    public AutonomousSearchWork Reservation { get; init; } = new();
    public AutonomousSearchWork PhaseBase { get; init; } = new();
    public bool InFlight { get; init; }
    public TimeSpan Elapsed { get; init; }
}
