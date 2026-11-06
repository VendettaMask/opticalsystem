namespace OpticalSystemDesign.Application.Contracts;

public sealed record ToleranceSampleDto(int SchemaVersion, string NominalSnapshotJson,
    TolerancingRequestDto Request, TolerancingTrialRowDto Recorded, string? JointSnapshotJson = null);

public sealed record ToleranceSampleReplayDto(ToleranceSampleDto Sample, TolerancingTrialRowDto Replayed, bool MatchesRecorded);
