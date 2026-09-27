using OptilandWorkbench.Core.Serialization;

namespace OptilandWorkbench.InitialStructure.Contracts;

public enum FlatStartSamplingPolicy { LegacyEqualRings, UniformAreaGaussianV1 }

public sealed record FlatStartSettings
{
    // Zero/omitted retains the exact historical ring metric and serialized fingerprint.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public FlatStartSamplingPolicy SamplingPolicy { get; init; }

    // Absent in historical files: preserve their fixed-aperture meaning and fingerprints.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool AutomaticLensDiameters { get; init; }

    public double? FixedBackFocusMillimeters { get; init; }
    public double MinimumEdgeThicknessMillimeters { get; init; } = 0.5;
    public double EffectiveFocalLengthRelativeTolerance { get; init; } = 0.02;
    public double FNumberRelativeTolerance { get; init; } = 0.05;
    public double MinimumValidRayFraction { get; init; } = 0.98;
}

public enum FlatStartBootstrapState
{
    Focused,
    BudgetExhausted,
    Stalled,
    TimeLimit,
    Initialized
}

public sealed record FlatStartEvaluation
{
    [System.Text.Json.Serialization.JsonIgnore]
    public OpticSnapshot? EvaluatedOptic { get; init; }

    public double OpticalPowerPerMillimeter { get; init; }
    public double Merit { get; init; }
    public double? RmsInterceptMillimeters { get; init; }
    public double ValidRayFraction { get; init; }
    public IReadOnlyList<double> Residuals { get; init; } = [];
    public IReadOnlyList<ConstraintViolation> Violations { get; init; } = [];
}

public sealed record FlatStartStep(
    int EvaluationNumber,
    string Operation,
    OpticSnapshot Optic,
    FlatStartEvaluation Evaluation);

/// <summary>Primary-wavelength, axial startup proof, not full-field design acceptance.</summary>
public sealed record FlatStartBootstrapResult
{
    public AlgorithmIdentity Algorithm { get; init; } = new("strict-flat-bootstrap", "2", "Managed CPU", true);
    public InitialStructureSpecification Specification { get; init; } = new();
    public string SpecificationFingerprint { get; init; } = string.Empty;
    public FlatStartBootstrapState State { get; init; }
    public double EntrancePupilFraction { get; init; }
    public int EvaluationCount { get; init; }
    public long TracedRayCount { get; init; }
    public IReadOnlyList<FlatStartStep> Steps { get; init; } = [];
    public IReadOnlyList<SearchDiagnostic> Diagnostics { get; init; } = [];
}
