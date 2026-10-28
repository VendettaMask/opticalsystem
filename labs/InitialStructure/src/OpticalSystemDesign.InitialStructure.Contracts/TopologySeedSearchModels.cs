using System.Collections.Immutable;

namespace OpticalSystemDesign.InitialStructure.Contracts;

public sealed record TopologyDiversityOptions
{
    public int Version { get; init; } = 1;
    public int RankWindow { get; init; } = 32;
}

/// <summary>Separate finite preparation budget, never borrowed from an autonomous run or final validation reserve.</summary>
public sealed record ParaxialSeedSearchOptions
{
    public int Version { get; init; } = 1;
    public int MaximumStarts { get; init; } = 12;
    public int RetainedSeeds { get; init; } = 4;
    public int MaximumFunctionEvaluations { get; init; } = 300;
    public int MaximumEvaluationsPerStart { get; init; } = 40;
    public TimeSpan TimeLimit { get; init; } = TimeSpan.FromSeconds(15);
    public long MaximumTracedRays { get; init; } = 10_000;
    public double FocusATolerance { get; init; } = .001;
}

public enum ParaxialSeedTermination { Completed, BudgetExhausted, Cancelled, TimeLimit }
public sealed record ParaxialSeedAttempt(int Index, string SourceFingerprint, string Outcome,
    int FunctionEvaluations, double? Score, DesignTopology? Endpoint);

/// <summary>First-order feasibility only. Every returned seed still needs full independent physical acceptance.</summary>
public sealed record ParaxialSeedSearchResult
{
    public string Algorithm { get; init; } = "initial-structure-paraxial-seeds-v1";
    public required string ProtocolFingerprint { get; init; }
    public required AutonomousSearchPlan SourcePlan { get; init; }
    public required ParaxialSeedSearchOptions Options { get; init; }
    public required ParaxialSeedTermination Termination { get; init; }
    public required AutonomousSearchWork Work { get; init; }
    public int MatrixRequests { get; init; }
    public ImmutableArray<DesignTopology> Seeds { get; init; } = [];
    public ImmutableArray<ParaxialSeedAttempt> Attempts { get; init; } = [];
}
