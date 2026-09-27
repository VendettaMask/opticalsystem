using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.InitialStructure.Contracts;

public sealed record DesignStage(double PupilFraction, double FieldFraction, bool AllWavelengths);

public enum FlatStartObjectiveKind { RealRaySumSquaresV1, ConstrainedRealRaySumSquaresV2 }

/// <summary>Comparable only for the same specification, stage and sampling. Physical acceptance is separate.</summary>
public sealed record FlatStartObjectiveValue(FlatStartObjectiveKind Kind, DesignStage Stage,
    bool DenseSampling, bool HasContinuousResiduals, double SumSquares)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public FlatStartSamplingPolicy SamplingPolicy { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool IndependentValidation { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool UsePhysicalStop { get; init; }
}

public sealed record DesignWavelengthEvaluation(double Nanometers, int AttemptedRays, int ValidRays);

public sealed record DesignFieldEvaluation(double NormalizedFieldY, double HalfFieldAngleDegrees,
    int AttemptedRays, int ValidRays, double? RmsRadiusMillimeters, double? MaximumRadiusMillimeters,
    IReadOnlyList<DesignWavelengthEvaluation> Wavelengths)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int CheckAttemptedRays { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int CheckValidRays { get; init; }
}

public sealed record DesignEvaluation
{
    // Exact post-sizing prescription used for this evaluation, retained only within a solve.
    [System.Text.Json.Serialization.JsonIgnore]
    public OpticSnapshot? EvaluatedOptic { get; init; }

    public double? EffectiveFocalLengthMillimeters { get; init; }
    public double? FNumber { get; init; }
    public double Merit { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public FlatStartObjectiveValue? Objective { get; init; }
    public IReadOnlyList<double> Residuals { get; init; } = [];
    public IReadOnlyList<DesignFieldEvaluation> Fields { get; init; } = [];
    public IReadOnlyList<ConstraintViolation> Violations { get; init; } = [];
    /// <summary>Geometry and the specified per-field/per-wave physical throughput gate are satisfied.</summary>
    public bool IsFeasible { get; init; }
    /// <summary>Search derivatives exist, possibly from unclipped Core diagnostics; never an acceptance gate.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool HasContinuousSearchResiduals { get; init; }
    // Solve-local vectors are regenerated from the frozen snapshot/settings. They
    // are never restored as a Jacobian/model from a historical checkpoint.
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<double> GeometryResiduals { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<double> ConstraintResiduals { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<double> TransmissionResiduals { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<double> ImageResiduals { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public bool GeometryFeasible { get; init; }
    public bool MeetsTargets => Violations.Count == 0;
}

public sealed record DesignStep(int EvaluationNumber, string Operation, DesignStage Stage,
    OpticSnapshot Optic, DesignEvaluation Evaluation);

public enum FlatStartDesignState
{
    Accepted,
    TargetsNotMet,
    StartupFailed,
    BudgetExhausted,
    TimeLimit
}

/// <summary>One fixed material/element family; not the multi-family P3 search.</summary>
public sealed record FlatStartDesignResult
{
    public AlgorithmIdentity Algorithm { get; init; } = new("strict-flat-design", FlatStartAlgorithm.Version, "Managed CPU", true);
    public InitialStructureSpecification Specification { get; init; } = new();
    public string SpecificationFingerprint { get; init; } = string.Empty;
    public FlatStartDesignState State { get; init; }
    public FlatStartBootstrapResult Bootstrap { get; init; } = new();
    public int EvaluationCount { get; init; }
    public long TracedRayCount { get; init; }
    public IReadOnlyList<DesignStep> Steps { get; init; } = [];
    public DesignEvaluation? FinalValidation { get; init; }
    public CandidateSnapshot? Candidate { get; init; }
    public IReadOnlyList<SearchDiagnostic> Diagnostics { get; init; } = [];
}
