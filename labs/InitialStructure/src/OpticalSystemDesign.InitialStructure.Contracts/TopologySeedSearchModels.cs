using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace OpticalSystemDesign.InitialStructure.Contracts;

public sealed record TopologyDiversityOptions
{
    public int Version { get; init; } = 1;
    public int RankWindow { get; init; } = 32;
}

/// <summary>Separate finite preparation budget, never borrowed from an autonomous run or final validation reserve.</summary>
public sealed record ParaxialSeedSearchOptions
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SeedImageScreeningOptions? ImageScreening { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? FixedStopRecoveryVersion { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StructureGenerationOptions? Generation { get; init; }
    public int Version { get; init; } = 1;
    public int MaximumStarts { get; init; } = 12;
    public int RetainedSeeds { get; init; } = 4;
    public int MaximumFunctionEvaluations { get; init; } = 300;
    public int MaximumEvaluationsPerStart { get; init; } = 40;
    public TimeSpan TimeLimit { get; init; } = TimeSpan.FromSeconds(15);
    public long MaximumTracedRays { get; init; } = 10_000;
    public double FocusATolerance { get; init; } = .001;
}

/// <summary>Bounded real-ray ranking only; never changes independent final acceptance.</summary>
public sealed record SeedImageScreeningOptions
{
    /// <summary>Null retains the original 32-arm screen; explicit sparse checks are ranking only.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? BoundaryCheckArms { get; init; }
    public int Version { get; init; } = 1;
    public int MaximumCandidates { get; init; } = 8;
    public int FieldsPerDirection { get; init; } = 2;
    public int PupilRings { get; init; } = 2;
    public int PupilArms { get; init; } = 8;
    public long MaximumTracedRaysPerCandidate { get; init; } = 2_500;
    public double RelativeQualityWindow { get; init; } = .25;
}

public sealed record SeedImageScreeningResult(string TopologyFingerprint, string Outcome,
    int CompletedFields, int ExpectedFields, double? WorstRmsMillimeters, double? WorstMaximumMillimeters,
    double? MinimumValidFraction, bool BoundaryChecksPassed, double? QualityScore, long TracedRays);

public enum StructureGenerationMethod { RandomPhysical, Delano }

/// <summary>One actual singlet. Cemented groups require a shared-interface model and are not encoded as singlets.</summary>
public sealed record StructureElementDescriptor
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StructureApertureDescriptor? FrontAperture { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StructureApertureDescriptor? BackAperture { get; init; }
    public string Kind { get; init; } = "S";
    public TopologyProfileKind Profile { get; init; } = TopologyProfileKind.Standard;
    public ImmutableArray<string> Materials { get; init; } = ["N-BK7"];
}

public sealed record StructureApertureDescriptor(double ClearSemiDiameterMillimeters, double? MechanicalSemiDiameterMillimeters = null);

public sealed record StructureGenerationOptions
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaterializationVersion { get; init; }
    public int Version { get; init; } = 1;
    public StructureGenerationMethod Method { get; init; } = StructureGenerationMethod.RandomPhysical;
    public ImmutableArray<StructureElementDescriptor> Elements { get; init; } = [];
    public int StopAfterElement { get; init; }
    public double ClearSemiDiameterMillimeters { get; init; } = 5;
    public double MinimumThicknessMillimeters { get; init; } = 1;
    public double MaximumThicknessMillimeters { get; init; } = 4;
    public double MinimumBackFocusMillimeters { get; init; } = 5;
    public int AnnealingProposals { get; init; } = 24;
}

public sealed record PopulationExplorationOptions
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PopulationGeometryOptions? Geometry { get; init; }
    public int Version { get; init; } = 1;
    public double DifferentialWeight { get; init; } = .5;
    public double CrossoverProbability { get; init; } = .7;
    public double EliteFraction { get; init; } = .25;
    public double RebirthProbability { get; init; } = .1;
}

public sealed record PopulationGeometryOptions
{
    public int Version { get; init; } = 1;
    public int MaximumAttempts { get; init; } = 8;
    public double RebirthBoundFraction { get; init; } = .05;
}

/// <summary>Finite search of qualified complexity/quality improvements; no desired element count.</summary>
public sealed record BoundedCandidateSearchOptions
{
    public int Version { get; init; } = 1;
    public int MaximumAttemptsPerElementCount { get; init; } = 3;
    public int MaximumUnsuccessfulTrials { get; init; } = 2;
    public double MinimumRelativeQualityGain { get; init; } = .001;
    public long MinimumCompensationRays { get; init; } = 100_000;
    public int MinimumCompensationFunctions { get; init; } = 2;
    public int MinimumCompensationGradients { get; init; } = 1;
}

public enum BoundedSearchStopReason { TrialLimit, LayerLimit, NoImprovement, NoEligibleBranch, InsufficientCompleteTrialBudget, TimeLimit, Cancelled }
public sealed record QualifiedStructureComplexity(int Elements, int AsphericSurfaces, int PolynomialTerms);
public sealed record BoundedCandidateSearchSummary(string? BestQualifiedId, QualifiedStructureComplexity? Complexity,
    BoundedSearchStopReason? StopReason, int ScreenedTrials, int RejectedScreens, int EvaluatedTrials, int ConsecutiveUnsuccessfulTrials);

public sealed record PopulationCoordinateProposal(string BindingFingerprint,
    ImmutableArray<TopologyVariableRequest> Variables, ImmutableArray<double> Coordinates);

public enum ParaxialSeedTermination { Completed, BudgetExhausted, Cancelled, TimeLimit }
public sealed record ParaxialSeedAttempt(int Index, string SourceFingerprint, string Outcome,
    int FunctionEvaluations, double? Score, DesignTopology? Endpoint);

/// <summary>First-order feasibility only. Every returned seed still needs full independent physical acceptance.</summary>
public sealed record ParaxialSeedSearchResult
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImmutableArray<SeedImageScreeningResult>? ImageScreens { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DiagramEvaluations { get; init; }
    public string Algorithm { get; init; } = "initial-structure-paraxial-seeds-v1";
    public required string ProtocolFingerprint { get; init; }
    public required AutonomousSearchPlan SourcePlan { get; init; }
    public required ParaxialSeedSearchOptions Options { get; init; }
    public required ParaxialSeedTermination Termination { get; init; }
    public required AutonomousSearchWork Work { get; init; }
    public int MatrixRequests { get; init; }
    public ImmutableArray<DesignTopology> Seeds { get; init; } = [];
    public ImmutableArray<ParaxialSeedAttempt> Attempts { get; init; } = [];
}
