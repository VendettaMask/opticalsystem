namespace OptilandWorkbench.InitialStructure.Engine.Optimization;

/// <summary>Variables and residuals must already use the caller's chosen dimensionless scales.</summary>
internal sealed record LeastSquaresOptions
{
    public int MaximumEvaluations { get; init; } = 1000;
    public double InitialRadius { get; init; } = 1;
    public double MaximumRadius { get; init; } = 100;
    public double GradientTolerance { get; init; } = 1e-10;
    public double StepTolerance { get; init; } = 1e-12;
    public double RelativeDifferenceStep { get; init; } = 6.055454452393343e-6;
}

/// <summary>Invalid evaluations provide no derivative or acceptance information, but still incur cost.</summary>
internal sealed record LeastSquaresEvaluation(IReadOnlyList<double> Residuals, bool IsValid = true, long WorkUnits = 0);

internal enum LeastSquaresTermination
{
    Stationary,
    StepTooSmall,
    EvaluationLimit,
    StopRequested,
    InvalidInitialEvaluation,
    DerivativeUnavailable,
    NonFiniteModel
}

/// <summary>One trial against a model. Rejected trials keep the same model number and base point.</summary>
internal sealed record LeastSquaresTrial(
    int ModelNumber,
    int EvaluationCount,
    long WorkUnits,
    IReadOnlyList<double> BasePoint,
    IReadOnlyList<double> TrialPoint,
    double Radius,
    double NextRadius,
    double PredictedReduction,
    double? ActualReduction,
    double? ReductionRatio,
    bool Accepted);

/// <summary>
/// Returns only the last accepted point, never a derivative probe. Stationarity is a local numerical
/// condition, not proof that application constraints or targets have been met. WorkUnits counts all
/// completed evaluations; only MaximumEvaluations is a hard solver budget.
/// </summary>
internal sealed record LeastSquaresResult(
    IReadOnlyList<double> Variables,
    LeastSquaresEvaluation? Evaluation,
    LeastSquaresTermination Termination,
    int EvaluationCount,
    int DifferenceEvaluationCount,
    int JacobianBuildCount,
    int FactorizationCount,
    long WorkUnits,
    IReadOnlyList<LeastSquaresTrial> Trials);
