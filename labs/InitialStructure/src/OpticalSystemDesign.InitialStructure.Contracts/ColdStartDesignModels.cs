using System.Collections.Immutable;

namespace OpticalSystemDesign.InitialStructure.Contracts;

/// <summary>Versioned cold construction policy; it cannot expand original physical permissions.</summary>
public sealed record ColdStartDesignOptions
{
    /// <summary>1 preserves the original schedule; 2 uses joint signed gates; 3 adds inward training margins;
    /// 4 warms the permitted fourth-order profile sparsely before dense sixth-order compensation;
    /// 6 additionally respects the existing 400-update stage cap when early convergence leaves a large remainder.
    /// Version 5 was an unadopted development prototype and is not accepted.
    /// The default stays 1 for historical JSON; new explicit desktop selections choose their tested version.</summary>
    public int Version { get; init; } = 1;
    public bool ExploreBackFocus { get; init; } = true;
    public ImmutableArray<double> BackFocusSlackFractions { get; init; } = [.75, .875, .9375];
    public int AttemptsPerFraction { get; init; } = 3;
    public int MaximumGradientEvaluations { get; init; } = 600;
}

public sealed record ColdStartConstruction(int Index, double SamplingBackFocusMinimumMillimeters,
    string Outcome, AutonomousSearchWork Work, DesignTopology? Topology, TopologyAssessment? Assessment);

public sealed record ColdStartDesignPhase(string Name, string Outcome, AutonomousSearchWork Work,
    ImmutableArray<TopologyVariableRequest> AnchoredVariables, DesignTopology Topology, TopologyAssessment? Assessment,
    ImmutableArray<string> Diagnostics);

public sealed record ColdStartDesignResult
{
    public required string ProtocolFingerprint { get; init; }
    public required AutonomousSearchPlan ColdPlan { get; init; }
    public required ParaxialSeedSearchOptions Preparation { get; init; }
    public required ColdStartDesignOptions Policy { get; init; }
    public required AutonomousSearchWork Work { get; init; }
    public required string StopReason { get; init; }
    public ImmutableArray<ColdStartConstruction> Construction { get; init; } = [];
    public ImmutableArray<ColdStartDesignPhase> Phases { get; init; } = [];
    public DesignTopology? Endpoint { get; init; }
    public TopologyAssessment? Assessment { get; init; }
    public bool Qualified => Assessment is { Completed: true, MeetsTargets: true };
}
