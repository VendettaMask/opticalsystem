using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.InitialStructure.Contracts;

public sealed record FlatStartFamily
{
    public IReadOnlyList<string> GlassNames { get; init; } = [];
    public IReadOnlyList<double> CenterThicknesses { get; init; } = [];
    public IReadOnlyList<double> AirGaps { get; init; } = [];
    public int StopSurfaceIndex { get; init; } = 1;
    public int SeedIndex { get; init; }
    public int ElementCount => GlassNames.Count;
}

public sealed record FlatStartSearchOptions
{
    public IReadOnlyList<string> AllowedGlassNames { get; init; } = ["N-BK7", "N-F2", "N-SF6"];
    public int MaximumEvaluationsPerTrial { get; init; } = 1_200;
    public int MaximumDisplayedCandidates { get; init; } = 8;
    public double MinimumStructuralDistance { get; init; } = .01;
}

public enum FamilyTrialState { Reserved, Completed, Failed, Interrupted }
public enum FlatStartSearchState { Running, Completed, Cancelled, BudgetExhausted, TimeLimit, NoUsableGlass }

public sealed record FamilyTrial
{
    public int Index { get; init; }
    public string TrialId { get; init; } = string.Empty;
    public FlatStartFamily Family { get; init; } = new();
    public string Operation { get; init; } = "flat-root";
    public string? ParentTrialId { get; init; }
    public FamilyTrialState State { get; init; }
    public int AllocatedEvaluations { get; init; }
    public int ChargedEvaluations { get; init; }
    public int? CompletedEvaluations { get; init; }
    public long TracedRealRayCount { get; init; }
    public FlatStartDesignState? DesignState { get; init; }
    public FlatStartBootstrapResult? BootstrapProof { get; init; }
    public OpticSnapshot? FlatRoot { get; init; }
    public OpticSnapshot? RefinementStartOptic { get; init; }
    public FlatStartStep? FirstCurvatureUpdate { get; init; }
    public DesignStep? FirstRefinementStep { get; init; }
    public IReadOnlyList<DesignStep> Stages { get; init; } = [];
    public CandidateSnapshot? Candidate { get; init; }
    public DesignEvaluation? FinalValidation { get; init; }
    public IReadOnlyList<SearchDiagnostic> Diagnostics { get; init; } = [];
}

/// <summary>Reserved work is charged before dispatch; interrupted work never restores its quota.</summary>
public sealed record FlatStartSearchCheckpoint
{
    public int SchemaVersion { get; init; } = 1;
    public AlgorithmIdentity Algorithm { get; init; } = new("strict-flat-family-search", "1", "Managed CPU", true);
    public string RunId { get; init; } = string.Empty;
    public InitialStructureSpecification Specification { get; init; } = new();
    public FlatStartSearchOptions Options { get; init; } = new();
    public string SpecificationFingerprint { get; init; } = string.Empty;
    public string OptionsFingerprint { get; init; } = string.Empty;
    public string MaterialFingerprint { get; init; } = string.Empty;
    public IReadOnlyList<string> UsableGlassNames { get; init; } = [];
    public IReadOnlyList<FlatStartFamily> RootPlan { get; init; } = [];
    public int RootEvaluationQuota { get; init; }
    public int NextRootIndex { get; init; }
    public int NextRefinementIndex { get; init; }
    public long ElapsedComputeTicks { get; init; }
    public long ReservedComputeTicks { get; init; }
    public FlatStartSearchState State { get; init; }
    public IReadOnlyList<FamilyTrial> Trials { get; init; } = [];
    public IReadOnlyList<SearchDiagnostic> Diagnostics { get; init; } = [];
    public int ChargedEvaluations => Trials.Sum(trial => trial.ChargedEvaluations);
    public long TracedRealRayCount => Trials.Sum(trial => trial.TracedRealRayCount);
}

public sealed record FlatStartSearchResult(FlatStartSearchCheckpoint Checkpoint,
    IReadOnlyList<CandidateSnapshot> Candidates);
