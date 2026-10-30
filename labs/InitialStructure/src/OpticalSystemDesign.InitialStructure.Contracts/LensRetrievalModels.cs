using System.Collections.Immutable;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.InitialStructure.Contracts;

public enum LensSourceKind { ProjectAuthored, UserProvidedLocalUse, LicensedRedistribution }
public sealed record LensSourceProvenance(LensSourceKind Kind, string Title, string Origin,
    string UsageAuthority, string SourceDocumentSha256);

/// <summary>Source geometry and installed material identity are frozen. Indexed descriptors never certify image quality.</summary>
public sealed record LensLibraryEntry
{
    public required string Id { get; init; }
    public required LensSourceProvenance Provenance { get; init; }
    public required DesignTopology Topology { get; init; }
    public required TopologyConstraints Constraints { get; init; }
    public required string SourceOpticFingerprint { get; init; }
    public required CenteredPrescriptionDescriptor Descriptor { get; init; }
    public AutonomousSearchWork IndexingWork { get; init; } = new();
    /// <summary>Null means source acquisition cost unknown; it must not be described as free.</summary>
    public AutonomousSearchWork? SourceAcquisitionWork { get; init; }
}

public sealed record LensLibraryDocument
{
    public string Algorithm { get; init; } = "InitialStructureLensLibraryV1";
    public int Version { get; init; } = 1;
    public string Name { get; init; } = "Local lens library";
    public ImmutableArray<LensLibraryEntry> Entries { get; init; } = [];
}

public sealed record LensRetrievalOptions
{
    public int Version { get; init; } = 1;
    public required LensLibraryDocument Library { get; init; }
    public int MaximumCandidates { get; init; } = 4;
    public double MinimumScale { get; init; } = .5;
    public double MaximumScale { get; init; } = 2;
    public double MaximumSourceSlowerFraction { get; init; } = .25;
    public double FinalRayRecoveryFactor { get; init; } = 12;
}

public sealed record RetrievedFaceAperture(string SurfaceId, double ScaledSourceClearMillimeters,
    double TaskClearMillimeters, double? TaskMechanicalMillimeters);
public sealed record LensRetrievalTransform(double UniformScale, double StopAnchoredTranslationMillimeters,
    double ScaledSourceImageZMillimeters, double FixedTaskImageZMillimeters,
    ImmutableArray<RetrievedFaceAperture> FixedApertures);
public sealed record LensRetrievalTrial
{
    public required string EntryId { get; init; }
    public required LensSourceProvenance Provenance { get; init; }
    public required string EntryFingerprint { get; init; }
    public required string Outcome { get; init; }
    public string? Detail { get; init; }
    public double? RankScore { get; init; }
    public LensRetrievalTransform? Transform { get; init; }
    public DesignTopology? Topology { get; init; }
    public TopologyAssessment? Assessment { get; init; }
    public bool MatchesOriginalInput { get; init; }
    public AutonomousSearchWork Work { get; init; } = new();
}

public sealed record LensRetrievalResult
{
    public string Algorithm { get; init; } = "initial-structure-lens-retrieval-v1";
    public required string LibraryFingerprint { get; init; }
    public required string StopReason { get; init; }
    public ImmutableArray<LensRetrievalTrial> Trials { get; init; } = [];
    public ImmutableArray<string> QualifiedEntryIds { get; init; } = [];
    public ImmutableArray<string> RetainedEntryIds { get; init; } = [];
    public int DescriptorEvaluations { get; init; }
    public int UnknownAcquisitionCosts { get; init; }
    public AutonomousSearchWork KnownSourceAcquisitionWork { get; init; } = new();
    public AutonomousSearchWork LibraryIndexingWork { get; init; } = new();
}
