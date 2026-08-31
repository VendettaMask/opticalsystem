using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.InitialStructure.Contracts;

public static class InitialStructureLimits
{
    public const int MaximumNameLength = 256;
    public const int MaximumIdentifierLength = 128;
    public const int MaximumMessageLength = 16_384;
    public const int MaximumWavelengthCount = 64;
    public const int MaximumGlassCatalogCount = 128;
    public const int MaximumInitialSeedCount = 10_000;
    public const int MaximumEvaluations = 100_000;
    public const int MaximumCandidateCount = MaximumEvaluations;
    public const int MaximumParallelism = 256;
    public const int MaximumViolationsPerCandidate = 1_024;
    public const int MaximumDiagnosticCount = 100_000;
    public const double MaximumFieldAngleDegrees = 89;
    public const long MaximumSettingsBytes = 4L * 1024 * 1024;
    public const long MaximumManifestBytes = 64L * 1024 * 1024;
    public static readonly TimeSpan MaximumTimeLimit = TimeSpan.FromHours(24);
}

public enum ObjectConjugateMode
{
    Infinite
}

public enum CandidateStatus
{
    Exploratory,
    TraceValid,
    Refinable,
    LabAccepted,
    Rejected
}

public enum SearchRunState
{
    Created,
    Running,
    Completed,
    Cancelled,
    Failed
}

public enum ConstraintSeverity
{
    Information,
    Warning,
    Hard
}

public sealed record WavelengthSpecification
{
    public string Label { get; init; } = "d";

    public double Nanometers { get; init; } = 587.6;

    public double Weight { get; init; } = 1;

    public bool IsPrimary { get; init; }
}

public sealed record SearchBudget
{
    public int InitialSeedCount { get; init; } = 24;

    public int MaximumEvaluations { get; init; } = 2_000;

    public int MaximumParallelism { get; init; } = Math.Max(1, Environment.ProcessorCount / 2);

    public TimeSpan TimeLimit { get; init; } = TimeSpan.FromMinutes(5);

    public long RandomSeed { get; init; } = 1;
}

public sealed record InitialStructureSpecification
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string Name { get; init; } = "Initial structure experiment";

    public ObjectConjugateMode Conjugate { get; init; } = ObjectConjugateMode.Infinite;

    public double EffectiveFocalLengthMillimeters { get; init; } = 50;

    public double FNumber { get; init; } = 4;

    public double MaximumFieldAngleDegrees { get; init; } = 10;

    public IReadOnlyList<WavelengthSpecification> Wavelengths { get; init; } =
    [
        new() { Label = "F", Nanometers = 486.1, Weight = 0.5 },
        new() { Label = "d", Nanometers = 587.6, Weight = 1, IsPrimary = true },
        new() { Label = "C", Nanometers = 656.3, Weight = 0.5 }
    ];

    public int MinimumElementCount { get; init; } = 3;

    public int MaximumElementCount { get; init; } = 3;

    public double MaximumTrackLengthMillimeters { get; init; } = 100;

    public double MinimumCenterThicknessMillimeters { get; init; } = 2;

    public double MinimumAirGapMillimeters { get; init; } = 1;

    public double MinimumBackFocusMillimeters { get; init; } = 5;

    public double SemiDiameterMarginFactor { get; init; } = 1.25;

    public double MaximumRmsSpotRadiusMillimeters { get; init; } = 0.25;

    public double MaximumSpotRadiusMillimeters { get; init; } = 1.0;

    public string InitialGlass { get; init; } = "N-BK7";

    public IReadOnlyList<string> GlassCatalogs { get; init; } = [];

    public SearchBudget Budget { get; init; } = new();
}

public sealed record AlgorithmIdentity(
    string Name,
    string Version,
    string NumericBackend,
    bool Deterministic);

public sealed record ConstraintViolation(
    string Code,
    ConstraintSeverity Severity,
    string Message,
    double? Actual = null,
    double? Limit = null);

public sealed record EvaluationVector
{
    public double? EffectiveFocalLengthMillimeters { get; init; }

    public double? FNumber { get; init; }

    public double ValidRayFraction { get; init; }

    public double? RmsSpotRadiusMillimeters { get; init; }

    public double? MaximumSpotRadiusMillimeters { get; init; }

    public int EvaluatedRayCount { get; init; }

    public int ValidRayCount { get; init; }
}

public sealed record CandidateLineage
{
    public string RootFingerprint { get; init; } = string.Empty;

    public string? ParentCandidateId { get; init; }

    public string Operation { get; init; } = "flat-root";

    public int Generation { get; init; }

    public int ElementCount { get; init; }

    public int StopVariant { get; init; }

    public int SeedIndex { get; init; }
}

public sealed record CandidateSnapshot
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string CandidateId { get; init; } = string.Empty;

    public string OpticFingerprint { get; init; } = string.Empty;

    public CandidateStatus Status { get; init; } = CandidateStatus.Exploratory;

    public OpticSnapshot FlatRootOptic { get; init; } = null!;

    public OpticSnapshot Optic { get; init; } = null!;

    public CandidateLineage Lineage { get; init; } = new();

    public EvaluationVector Evaluation { get; init; } = new();

    public IReadOnlyList<ConstraintViolation> Violations { get; init; } = [];
}

public sealed record SearchDiagnostic(
    string Code,
    string Message,
    string? CandidateId = null);

public sealed record SearchRunManifest
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string RunId { get; init; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; init; }

    public DateTimeOffset? CompletedUtc { get; init; }

    public SearchRunState State { get; init; } = SearchRunState.Created;

    public InitialStructureSpecification Specification { get; init; } = new();

    public string SpecificationFingerprint { get; init; } = string.Empty;

    public AlgorithmIdentity Algorithm { get; init; } =
        new("unassigned", "0", "Managed CPU", true);

    public IReadOnlyList<CandidateSnapshot> Candidates { get; init; } = [];

    public IReadOnlyList<SearchDiagnostic> Diagnostics { get; init; } = [];
}

public sealed record SearchCheckpoint
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string RunId { get; init; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; init; }

    public DateTimeOffset UpdatedUtc { get; init; }

    public string Stage { get; init; } = "initial-seeds";

    public InitialStructureSpecification Specification { get; init; } = new();

    public string SpecificationFingerprint { get; init; } = string.Empty;

    public AlgorithmIdentity Algorithm { get; init; } =
        new("unassigned", "0", "Managed CPU", true);

    public IReadOnlyList<int> CompletedInitialSeedIndices { get; init; } = [];

    public IReadOnlyList<CandidateSnapshot> SeedCandidates { get; init; } = [];

    public IReadOnlyList<SearchDiagnostic> Diagnostics { get; init; } = [];
}
