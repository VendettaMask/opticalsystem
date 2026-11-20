namespace OpticalSystemDesign.Application.Contracts;

public sealed record ProjectArchiveFileDto(string RelativePath, string Kind, string SourcePath,
    long Bytes, bool Required = true, bool IsResult = false)
{
    public bool Exists => File.Exists(SourcePath);
}
public sealed record ProjectArchiveEntryDto(string RelativePath, string Kind, long Bytes, string Sha256, bool Required);
public sealed record ProjectArchiveInfoDto(string Name, string ApplicationVersion, DateTimeOffset CreatedUtc,
    long SourceRevision, OpticalWorkbenchMode Mode, IReadOnlyList<ProjectArchiveEntryDto> Entries);
public sealed record ProjectArchiveProgressDto(string Phase, string File, long CompletedBytes, long TotalBytes);
public sealed record ProjectArchiveRestoreDto(string Directory, string ProjectPath, ProjectArchiveInfoDto Info,
    IReadOnlyList<ProjectArchiveFileDto> Files);

public interface IProjectArchiveService
{
    IReadOnlyList<ProjectArchiveFileDto> LinkedFiles { get; }
    ProjectArchiveFileDto LinkFile(string path, string kind = "attachment", bool required = false, bool isResult = false);
    Task<ProjectArchiveCapture> CaptureAsync(IReadOnlyList<ProjectArchiveFileDto>? extraFiles = null,
        CancellationToken cancellationToken = default);
    Task<ProjectArchiveInfoDto> CreateAsync(string path, ProjectArchiveCapture capture, IReadOnlySet<string> includedPaths,
        IProgress<ProjectArchiveProgressDto>? progress = null, CancellationToken cancellationToken = default);
    Task<ProjectArchiveInfoDto> InspectAsync(string path, CancellationToken cancellationToken = default);
    Task<ProjectArchiveRestoreDto> RestoreAsync(string path, string directory, IProgress<ProjectArchiveProgressDto>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class ProjectArchiveCapture : IDisposable
{
    internal ProjectArchiveCapture(string directory, string name, long revision, OpticalWorkbenchMode mode, IReadOnlyList<ProjectArchiveFileDto> files)
    { Directory = directory; Name = name; SourceRevision = revision; Mode = mode; Files = files; }
    internal string Directory { get; }
    internal Dictionary<string, FileStream> Pinned { get; } = new(StringComparer.Ordinal);
    internal SemaphoreSlim WriteGate { get; } = new(1, 1);
    public string Name { get; }
    public long SourceRevision { get; }
    public OpticalWorkbenchMode Mode { get; }
    public IReadOnlyList<ProjectArchiveFileDto> Files { get; private set; }
    public void RelocateFile(string relativePath, string path)
    {
        var source = Files.Single(file => file.RelativePath == relativePath);
        if (source.Kind == "project") throw new InvalidOperationException("The captured native project cannot be replaced.");
        path = Path.GetFullPath(path);
        var updated = source with { SourcePath = path, Bytes = new FileInfo(path).Length };
        Files = Files.Select(file => file.RelativePath == relativePath ? updated : file).ToArray();
    }
    public void Dispose()
    {
        foreach (var stream in Pinned.Values) stream.Dispose();
        var root = Path.GetFullPath(Path.GetTempPath());
        if (!Directory.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(Directory).StartsWith("starpack-capture-", StringComparison.Ordinal))
            throw new IOException("Invalid archive capture folder.");
        if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, recursive: true);
    }
}
