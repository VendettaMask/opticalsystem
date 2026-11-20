namespace OpticalSystemDesign.Core.Serialization;

public sealed record ProjectArchiveSource(string RelativePath, string Kind, string SourcePath, bool Required = true, Stream? Snapshot = null);
public sealed record ProjectArchiveEntry(string RelativePath, string Kind, long Bytes, string Sha256, bool Required);
public sealed record ProjectArchiveManifest(string Format, int Version, string Name, string ApplicationVersion,
    DateTimeOffset CreatedUtc, long SourceRevision, string Mode, IReadOnlyList<ProjectArchiveEntry> Entries);
public sealed record ProjectArchiveProgress(string Phase, string File, long CompletedBytes, long TotalBytes);
public sealed record ProjectArchiveRestored(string Directory, string ProjectPath, ProjectArchiveManifest Manifest);

public sealed record ProjectArchiveLimits(long MaximumFileBytes = 64L * 1024 * 1024 * 1024,
    long MaximumExpandedBytes = 256L * 1024 * 1024 * 1024, int MaximumFiles = 10_000)
{
    internal void Validate()
    {
        if (MaximumFileBytes < 1 || MaximumExpandedBytes < MaximumFileBytes || MaximumExpandedBytes > long.MaxValue / 2
            || MaximumFiles is < 1 or > 10_000) throw new ArgumentOutOfRangeException(nameof(ProjectArchiveLimits));
    }
}
