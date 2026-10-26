using System.Collections.Immutable;

namespace OpticalSystemDesign.InitialStructure.Contracts;

/// <summary>Explicit new-plan policy. Null preserves the original M4 dispatch and checkpoint identity.</summary>
public sealed record TopologyExpansionOptions
{
    public bool EnableInsertion { get; init; } = true;
    public bool EnableDeletion { get; init; } = true;
    public bool EnableGradualDeletion { get; init; } = true;
    public bool EnableSplit { get; init; } = true;
    public bool EnableMerge { get; init; } = true;
    public bool EnableMaterial { get; init; } = true;
    public bool EnableProfile { get; init; } = true;
    public ImmutableArray<string> MaterialAlternatives { get; init; } = ["N-BK7", "N-F2"];
    public ImmutableArray<double> DeletionScales { get; init; } = [.7, .4, .2, .1, .02];
    public int? DirectDeletionRecoveryMaximumFunctionEvaluations { get; init; }
    public double SplitAirGapMillimeters { get; init; } = .05;
    public double SplitFirstFraction { get; init; } = .55;
    public int MaximumEvenPower { get; init; } = 8;
    public double MaximumAbsoluteConic { get; init; } = 2;
    public double CoefficientCoordinateSagMillimeters { get; init; } = .02;
}

/// <summary>Never overwrite the unscaled origin with a compensated or previously scaled element.</summary>
public sealed record GradualDeletionState
{
    public required DesignTopology Baseline { get; init; }
    public required string BaselineFingerprint { get; init; }
    public required LensElement OriginalElement { get; init; }
    public required ImmutableArray<double> Scales { get; init; }
    public int StageIndex { get; init; }
}
