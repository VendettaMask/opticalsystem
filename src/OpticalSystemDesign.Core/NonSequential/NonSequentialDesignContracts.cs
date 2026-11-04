using OpticalSystemDesign.Core.Optimization;

namespace OpticalSystemDesign.Core.NonSequential;

public enum NonSequentialVariableDomain { Position, Rotation, ObjectParameter, SourcePhase }
public sealed record NonSequentialDesignVariable(Guid ObjectId, NonSequentialVariableDomain Domain, string Parameter,
    double LowerBound, double UpperBound, double Step);
public enum NonSequentialMetric
{
    TotalPower, PixelPower, MeanIrradiance, PeakIrradiance, RelativeUniformityRms,
    CentroidX, CentroidY, RmsX, RmsY, RmsRadius, CoherentPower, StokesQ, StokesU, StokesV
}
public sealed record NonSequentialDesignTarget(Guid DetectorId, NonSequentialMetric Metric, double Target, double Tolerance,
    double Weight = 1, int WavelengthNumber = 0, int PixelIndex = 0, string? PathFilter = null, Guid? SourceObjectId = null);
public sealed record NonSequentialDesignSettings(string Optimizer = "Damped Least Squares", int MaximumIterations = 50,
    int MaximumTraceEvaluations = 200, long MaximumLaunchedRays = 1_000_000, int? RayCountOverride = null,
    int TrainingSeed = 1, int ValidationSeed = 104730, double MaximumTruncatedFraction = .001);
public sealed record NonSequentialMetricValue(NonSequentialDesignTarget Definition, string Unit, double Value, bool Passed);
public sealed record NonSequentialDesignCost(int TraceEvaluations, long LaunchedSourceRays, long Branches, long Segments, int CacheHits,
    long ReservedSourceRays = 0);
public sealed record NonSequentialDesignProgress(string Phase, int TraceEvaluations, long LaunchedSourceRays, double? Merit = null);
public sealed record NonSequentialDesignResult(string Algorithm, string SourceSceneSha256, NonSequentialDocument Candidate,
    IReadOnlyList<NonSequentialDesignVariable> Variables, IReadOnlyList<double> InitialValues, IReadOnlyList<double> BestValues,
    IReadOnlyList<NonSequentialMetricValue> InitialMetrics, IReadOnlyList<NonSequentialMetricValue> TrainingMetrics,
    IReadOnlyList<NonSequentialMetricValue> ValidationMetrics, bool Passed, OptimizerResult Optimizer,
    NonSequentialDesignCost Cost, NonSequentialDesignSettings Settings);
public enum NonSequentialToleranceDistribution { Uniform, Gaussian }
public sealed record NonSequentialToleranceVariable(NonSequentialDesignVariable Variable, double Scale,
    NonSequentialToleranceDistribution Distribution = NonSequentialToleranceDistribution.Gaussian);
public sealed record NonSequentialToleranceSettings(int Samples = 100, int ManufacturingSeed = 1,
    NonSequentialDesignSettings? Trace = null, int CompensationTraceEvaluations = 8);
public sealed record NonSequentialDesignExperiment(int FormatVersion, string SourceSceneSha256,
    IReadOnlyList<NonSequentialDesignVariable> Variables, IReadOnlyList<NonSequentialDesignTarget> Targets,
    NonSequentialDesignSettings Settings, IReadOnlyList<NonSequentialToleranceVariable>? Tolerances = null,
    NonSequentialToleranceSettings? ToleranceSettings = null,
    IReadOnlyList<NonSequentialDesignVariable>? Compensators = null);
public sealed record NonSequentialToleranceSample(int Number, IReadOnlyList<double> Values,
    IReadOnlyList<NonSequentialMetricValue>? Metrics, bool Passed, string? Failure,
    IReadOnlyList<double>? CompensatedValues = null);
public sealed record NonSequentialSensitivity(int VariableIndex, double MinusValue, double PlusValue,
    IReadOnlyList<double>? Derivatives, string? Failure);
public sealed record NonSequentialToleranceResult(string Algorithm, string SourceSceneSha256,
    IReadOnlyList<NonSequentialMetricValue> NominalMetrics, IReadOnlyList<NonSequentialSensitivity> Sensitivities,
    IReadOnlyList<NonSequentialToleranceSample> Samples, double? Yield, int Seed, NonSequentialDesignCost Cost);
