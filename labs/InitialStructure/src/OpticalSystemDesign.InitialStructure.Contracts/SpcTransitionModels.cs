namespace OpticalSystemDesign.InitialStructure.Contracts;

/// <summary>Experimental zero-glass/zero-air null insertion, never an ordinary physical prescription.
/// Identical spherical null faces initially use the adjacent lens material. No local minimum or saddle is certified.</summary>
public sealed record SpcTransitionState
{
    public const string Identity = "InitialStructureSpcTransitionV1";
    public string Algorithm { get; init; } = Identity;
    public int Version { get; init; } = 1;
    public required DesignTopology Baseline { get; init; }
    public required TopologyConstraints Constraints { get; init; }
    public required string BaselineBindingFingerprint { get; init; }
    public required string ParentElementId { get; init; }
    public required string NewElementId { get; init; }
    public double NullCurvatureOffsetPerMillimeter { get; init; }
}
