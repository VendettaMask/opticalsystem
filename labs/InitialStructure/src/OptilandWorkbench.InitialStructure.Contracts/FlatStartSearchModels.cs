using OptilandWorkbench.Core.Serialization;
using System.Text.Json.Serialization;

namespace OptilandWorkbench.InitialStructure.Contracts;

public sealed record FlatStartFamily
{
    public IReadOnlyList<string> GlassNames { get; init; } = [];
    public IReadOnlyList<double> CenterThicknesses { get; init; } = [];
    public IReadOnlyList<double> AirGaps { get; init; } = [];
    public int StopSurfaceIndex { get; init; } = 1;
    public int SeedIndex { get; init; }
    public int ElementCount => GlassNames.Count;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BinaryFormStart? BinaryStart { get; init; }
}

/// <summary>Initial curvature directions, not a constraint on the optimized element powers.</summary>
public sealed record BinaryFormStart(string Signs, double RadiusMillimeters);

public enum DesignSearchMode { Quick, Full }

/// <summary>Native search controls inspired by DSEARCH; not a SYNOPSYS macro or PSD implementation.</summary>
public sealed record DesignSearchSettings
{
    public DesignSearchMode Mode { get; init; } = DesignSearchMode.Quick;
    public double? StartingRadiusMillimeters { get; init; }
    public double? StartingCenterThicknessMillimeters { get; init; }
    public double? StartingAirGapMillimeters { get; init; }
    public string SignFilter { get; init; } = "";
    public int? StopSurfaceIndex { get; init; }
    public bool ExploreStopPositions { get; init; } = true;
}

public sealed record FlatStartSearchOptions
{
    public IReadOnlyList<string> AllowedGlassNames { get; init; } = ["N-BK7", "N-F2", "N-SF6"];
    public int MaximumEvaluationsPerTrial { get; init; } = 1_200;
    public int MaximumDisplayedCandidates { get; init; } = 8;
    public double MinimumStructuralDistance { get; init; } = .01;
    // Absent in historical checkpoints; v8 resolves null to the native defaults.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DesignSearchSettings? DesignSearch { get; init; }
}

public enum FamilyTrialState { Reserved, Completed, Failed, Interrupted }
public enum FlatStartSearchState { Running, Completed, Cancelled, BudgetExhausted, TimeLimit, NoUsableGlass }

public static class FlatStartAlgorithm
{
    // Version 5 was an abandoned weighting experiment, never a frozen release.
    public const string Version = "18";

    public static int SchedulingPolicy(string version, InitialStructureSpecification specification) =>
        version is "13" or "14" or "15" or "16" or "17" or "18" && specification.FlatStart?.SamplingPolicy == FlatStartSamplingPolicy.UniformAreaGaussianV1 ? 2 : 1;
}

public sealed record FlatStartScheduleState(int PolicyVersion, int NextDecisionIndex, long NextRandomSeed, string LastDecision);

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
    public AlgorithmIdentity Algorithm { get; init; } = new("strict-flat-family-search", FlatStartAlgorithm.Version, "Managed CPU", true);
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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FlatStartRefinementOrigin? Origin { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FlatStartScheduleState? Schedule { get; init; }
}

public sealed record FlatStartRefinementOrigin(string RunId, InitialStructureSpecification Specification,
    string MaterialFingerprint, int ChargedEvaluations, FamilyTrial Source, FlatStartBootstrapResult RootProof)
{
    public const string ParentReference = "source-parent";
    public FamilyTrial AsParent() => Source with { TrialId = ParentReference, ParentTrialId = null, BootstrapProof = RootProof };
}

public sealed record FlatStartPreflight(IReadOnlyList<string> UsableGlassNames, IReadOnlyList<SearchDiagnostic> Diagnostics);

public sealed record FlatStartSearchResult(FlatStartSearchCheckpoint Checkpoint,
    IReadOnlyList<CandidateSnapshot> Candidates);
