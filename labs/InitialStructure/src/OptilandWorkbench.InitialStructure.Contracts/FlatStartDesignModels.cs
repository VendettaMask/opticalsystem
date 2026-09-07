using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.InitialStructure.Contracts;

public sealed record DesignStage(double PupilFraction, double FieldFraction, bool AllWavelengths);

public sealed record DesignWavelengthEvaluation(double Nanometers, int AttemptedRays, int ValidRays);

public sealed record DesignFieldEvaluation(double NormalizedFieldY, double HalfFieldAngleDegrees,
    int AttemptedRays, int ValidRays, double? RmsRadiusMillimeters, double? MaximumRadiusMillimeters,
    IReadOnlyList<DesignWavelengthEvaluation> Wavelengths);

public sealed record DesignEvaluation
{
    public double? EffectiveFocalLengthMillimeters { get; init; }
    public double? FNumber { get; init; }
    public double Merit { get; init; }
    public IReadOnlyList<double> Residuals { get; init; } = [];
    public IReadOnlyList<DesignFieldEvaluation> Fields { get; init; } = [];
    public IReadOnlyList<ConstraintViolation> Violations { get; init; } = [];
    /// <summary>Geometry and the specified per-field/per-wave physical throughput gate are satisfied.</summary>
    public bool IsFeasible { get; init; }
    /// <summary>Search derivatives exist, possibly from unclipped Core diagnostics; never an acceptance gate.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool HasContinuousSearchResiduals { get; init; }
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
    public AlgorithmIdentity Algorithm { get; init; } = new("strict-flat-design", "4", "Managed CPU", true);
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
