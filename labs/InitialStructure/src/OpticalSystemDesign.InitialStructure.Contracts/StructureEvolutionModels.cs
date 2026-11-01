using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace OpticalSystemDesign.InitialStructure.Contracts;

public enum StructureEvolutionMode { DirectCompensation, RecoveryAndCompensation, RecoveryAndCompetition }

/// <summary>Explicit bounded M2 policy. Existing expansion flags and structural/material permissions remain authoritative.</summary>
public sealed record StructureEvolutionOptions
{
    /// <summary>Apply a fresh gate-aware compensation policy to edited structures and real-ray competition phases.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? GateCompensationVersion { get; init; }
    /// <summary>Explicit release of permitted fourth-order terms during failed-transmission restoration.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TransmissionProfileVersion { get; init; }
    /// <summary>Experimental fixed-stop first-order envelope preconditioning; original physical validation remains authoritative.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TransmissionEnvelopeVersion { get; init; }
    public int Version { get; init; } = 1;
    public StructureEvolutionMode Mode { get; init; } = StructureEvolutionMode.RecoveryAndCompetition;
    public int MaximumParents { get; init; } = 1;
    public int MaximumBranches { get; init; } = 8;
    public ImmutableArray<TopologyOperationKind> Operations { get; init; } =
        [TopologyOperationKind.Delete, TopologyOperationKind.Insert, TopologyOperationKind.Split,
         TopologyOperationKind.Merge, TopologyOperationKind.Material, TopologyOperationKind.Profile];
    public bool OptimizeParents { get; init; } = true;
    public int RecoveryFunctionsPerBranch { get; init; } = 24;
    public int CompensationUpdatesPerBranch { get; init; } = 3;
    public long CompensationRaysPerBranch { get; init; } = 20000;
    public TimeSpan MaximumPhaseTime { get; init; } = TimeSpan.FromSeconds(4);
    public double InsertedThicknessMillimeters { get; init; } = .5;
    public PowerAllocationSeedOptions? PowerAllocation { get; init; }
}

public sealed record PowerAllocationSeedOptions
{
    public int Version { get; init; } = 1;
    public int MaximumAttempts { get; init; } = 1;
    public int ClosureFunctionsPerAttempt { get; init; } = 40;
    public double MaximumAbsoluteNormalizedPower { get; init; } = 1;
    public double PerturbationFraction { get; init; } = .25;
}

public sealed record PowerAllocationSeedEvidence(string ParentId, string Outcome,
    ImmutableArray<string> AllocatedElementIds, ImmutableArray<double> RequestedPowersPerMillimeter,
    ImmutableArray<double> EndpointPowersPerMillimeter, bool AllocatedPowersPreserved,
    string? ClosureProtocolFingerprint, AutonomousSearchWork Work);

public sealed record StructureEvolutionParent(string Id, DesignTopology Topology, TopologyAssessment Assessment, AutonomousSearchWork Work);
public sealed record StructureEvolutionTrial
{
    public required string Id { get; init; }
    public required string ParentId { get; init; }
    public required string Operation { get; init; }
    public required string Outcome { get; init; }
    public string? Detail { get; init; }
    public TopologyChange? Change { get; init; }
    public DesignTopology? EditedTopology { get; init; }
    public DesignTopology? Endpoint { get; init; }
    public string? EditBindingFingerprint { get; init; }
    public string? CompensationBindingFingerprint { get; init; }
    public ImmutableArray<TopologyVariableRequest> AnchoredVariables { get; init; } = [];
    public ParaxialRecoveryResult? Recovery { get; init; }
    public StructureCompetitionOptimization? Compensation { get; init; }
    public StructureCompetitionScreen? Screen { get; init; }
    public TopologyAssessment? OriginalAssessment { get; init; }
    public PowerAllocationSeedEvidence? PowerAllocation { get; init; }
    public AutonomousSearchWork Work { get; init; } = new();
}

public sealed record QualifiedEvolutionDesign(string Id, string? ParentId, DesignTopology Topology,
    TopologyAssessment Assessment, QualifiedStructureComplexity Complexity, double WorstRmsMillimeters, double WorstMaximumMillimeters);

public sealed record StructureEvolutionResult(string Algorithm, string ProtocolFingerprint, string StopReason,
    ImmutableArray<StructureEvolutionParent> Parents, ImmutableArray<StructureEvolutionTrial> Trials,
    ImmutableArray<QualifiedEvolutionDesign> QualifiedArchive, ImmutableArray<string> ParetoIds,
    ImmutableArray<StructurePhaseCost> Costs)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<StructurePrecheck>? Prechecks { get; init; }
    public TimeSpan? FirstNewQualifiedElapsed { get; init; }
}
