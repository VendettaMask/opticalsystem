using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.InitialStructure.Contracts;

public enum AutoLensManagedPolicy { HistoricalV1, UpstreamE58 }

/// <summary>Pure managed C# differentiation without a foreign runtime dependency.</summary>
public sealed record AutoLensManagedSettings
{
    // Missing values must continue to identify historical runs, never reinterpret them.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public AutoLensManagedPolicy Policy { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public AutoLensTrainingSampling? TrainingSampling { get; init; }
    // Omitted historical settings retain CPU execution and their specification identity.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public DesignComputeMode ComputeMode { get; init; }
    public int CurriculumIterations { get; init; } = 2000;
    public int RefinementIterations { get; init; } = 3000;
    public bool OptimizeMaterials { get; init; } = true;
    // Zero: spherical, 2..5: coefficients through r^4..r^10. r^2 stays exactly zero.
    public int EvenAsphereOrder { get; init; } = 4;
    public double LearningRateFactor { get; init; } = 1;
    public int FieldRings { get; init; } = 5;
    public int PupilRings { get; init; } = 3;
    public int PupilArms { get; init; } = 12;
    public int ResampleInterval { get; init; } = 20;
    public int TargetPolishEvaluations { get; init; } = 400;
    /// <summary>Zero-based element gaps cemented to the following element; other gaps keep the ordinary air-gap bound.</summary>
    public IReadOnlyList<int> CementedGaps { get; init; } = [];
}

public enum AutoLensManagedPhase { Curriculum, CatalogProjection, CatalogRefinement, Completed, DomainFailure, Finalization, PhysicalValidation }

public sealed record AutoLensAdamState(int Iteration, IReadOnlyList<double> Coordinates,
    IReadOnlyList<double> FirstMoment, IReadOnlyList<double> SecondMoment, int MomentIteration = 0);

public sealed record AutoLensManagedStep(AutoLensManagedPhase Phase, int Iteration, double PupilFraction,
    double BeforeLoss, double AfterLoss, double StepFraction, int ChargedEvaluation)
{
    /// <summary>Upstream records the objective before its unconditional update, without a second loss probe.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsPreUpdateObservation { get; init; }
}

/// <summary>Evaluated design state, never a physical acceptance certificate.</summary>
public sealed record AutoLensManagedState
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public AutoLensReferenceSamplingState? ReferenceSampling { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public OpticSnapshot? TrainingStartOptic { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public double ReferenceFocalLength { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public double ReferenceHalfFieldRadians { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public double ReferenceEffectiveHalfFieldRadians { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool ReferenceCentroid { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<double>? ReferenceLearningRates { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Core.Raytrace.SequentialGpuExecution? PropagationExecution { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public DesignComputeExecution? LastComputeExecution { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public long GpuBatches { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public long GpuRays { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public double GpuMilliseconds { get; init; }
    public int SchemaVersion { get; init; } = 1;
    public string SpecificationFingerprint { get; init; } = "";
    public string AllowedGlassFingerprint { get; init; } = "";
    public string RootFingerprint { get; init; } = "";
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentCandidateId { get; init; }
    public AutoLensManagedPhase Phase { get; init; }
    public int PlannedCurriculumIterations { get; init; }
    public int PlannedRefinementIterations { get; init; }
    public OpticSnapshot Optic { get; init; } = null!;
    public OpticSnapshot? FullTargetIncumbent { get; init; }
    public IReadOnlyList<OpticalDesignParameter> Parameters { get; init; } = [];
    public AutoLensAdamState Adam { get; init; } = new(0, [], [], []);
    public IReadOnlyList<DesignGlassMatch> GlassMatches { get; init; } = [];
    public IReadOnlyList<AutoLensManagedStep> TrainingSteps { get; init; } = [];
    public int EvaluationCount { get; init; }
    public long TracedRayCount { get; init; }
    public long ElapsedComputeTicks { get; init; }
    public int TargetPolishEvaluationCount { get; init; }
    public double? LastLoss { get; init; }
}

/// <summary>The immutable sampling optic regenerates exactly the cached rays on resume.</summary>
public sealed record AutoLensReferenceSamplingState(OpticSnapshot Optic, int Iteration, ulong Seed, double PupilFraction);

/// <summary>Default values are the pinned upstream protocol; explicit overrides identify a different sampling experiment.</summary>
public sealed record AutoLensTrainingSampling
{
    public int CurriculumRings { get; init; } = 8;
    public int CurriculumArms { get; init; } = 1;
    public int CurriculumRays { get; init; } = 512;
    public int RefinementRings { get; init; } = 8;
    public int RefinementArms { get; init; } = 2;
    public int RefinementRays { get; init; } = 1024;
    public int RegularizationRings { get; init; } = 16;
    public int RegularizationRays { get; init; } = 1024;
    public int ShapeRings { get; init; } = 16;
    public int ShapeRays { get; init; } = 1024;
    public int FieldScanRings { get; init; } = 64;
    public int FieldScanRays { get; init; } = 256;
}
