using OpticalSystemDesign.Application.Contracts;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Application.Services;

internal sealed class ProjectArchiveService : WorkbenchServiceBase, IProjectArchiveService
{
    private readonly IOpticalDocumentService _documents;
    private readonly IWorkbenchModeService _modes;
    private readonly INonSequentialAnalysisService _analysis;
    private readonly List<ProjectArchiveFileDto> _linked = [];
    internal ProjectArchiveService(WorkspaceCoordinator workspace, IOpticalDocumentService documents,
        IWorkbenchModeService modes, INonSequentialAnalysisService analysis) : base(workspace)
    {
        _documents = documents; _modes = modes; _analysis = analysis;
        workspace.Changed += (_, args) => { if (args.FileSwitched) lock (Gate) _linked.Clear(); };
    }
    public IReadOnlyList<ProjectArchiveFileDto> LinkedFiles
    {
        get
        {
            lock (Gate)
            {
                var path = _documents.CurrentPath;
                if (_linked.Count == 0 && path is not null && Path.GetFileName(path) == ProjectArchiveStore.ProjectPath
                    && File.Exists(Path.Combine(Path.GetDirectoryName(path)!, ProjectArchiveStore.RestoredManifest)))
                {
                    var root = Path.GetDirectoryName(path)!;
                    var info = ProjectArchiveStore.ReadRestoredManifest(root);
                    _linked.AddRange(info.Entries.Where(entry => entry.Kind != "project").Select(entry => new ProjectArchiveFileDto(
                        entry.RelativePath, entry.Kind, Path.Combine(root, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar)),
                        entry.Bytes, entry.Required, ResultKind(entry.Kind))));
                }
                return _linked.ToArray();
            }
        }
    }

    public ProjectArchiveFileDto LinkFile(string path, string kind = "attachment", bool required = false, bool isResult = false)
    {
        path = Path.GetFullPath(path);
        if (kind.Length is < 1 or > 64 || kind.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) throw new ArgumentException("Invalid archive resource kind.", nameof(kind));
        lock (Gate)
        {
            var existing = _linked.FirstOrDefault(file => file.SourcePath == path);
            if (existing is not null) return existing;
            var file = new ProjectArchiveFileDto($"attachments/{Guid.NewGuid():N}/{PortableName(Path.GetFileName(path))}", kind, path,
                File.Exists(path) ? new FileInfo(path).Length : 0, required, isResult);
            _linked.Add(file); return file;
        }
    }

    public async Task<ProjectArchiveCapture> CaptureAsync(IReadOnlyList<ProjectArchiveFileDto>? extraFiles = null,
        CancellationToken cancellationToken = default)
    {
        LoadedOpticalDocument document;
        string name; long revision; OpticalWorkbenchMode mode; ProjectArchiveFileDto[] linked;
        _ = LinkedFiles;
        lock (Gate)
        {
            document = Runtime.CaptureDocument(); name = document.ActiveOptic.Name; revision = Workspace.Revision;
            mode = _modes.CurrentMode; linked = _linked.ToArray();
        }
        var directory = Path.Combine(Path.GetTempPath(), "starpack-capture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var project = Path.Combine(directory, ProjectArchiveStore.ProjectPath);
            await WorkbenchRuntime.SaveDocumentAsync(document, project, cancellationToken).ConfigureAwait(false);
            var files = new List<ProjectArchiveFileDto> { new(ProjectArchiveStore.ProjectPath, "project", project, new FileInfo(project).Length) };
            files.AddRange(linked);
            if (extraFiles is not null)
            {
                var overridden = extraFiles.Select(file => file.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                files.RemoveAll(file => file.Kind != "project" && overridden.Contains(file.RelativePath));
                files.AddRange(extraFiles);
            }
            var database = _analysis.GetCurrentSession();
            if (database is not null)
            {
                files.RemoveAll(file => file.RelativePath == "results/rays.starrdb");
                files.Add(new("results/rays.starrdb", "ray-database", database.RayDatabasePath,
                    File.Exists(database.RayDatabasePath) ? new FileInfo(database.RayDatabasePath).Length : 0, false, true));
            }
            var capture = new ProjectArchiveCapture(directory, string.IsNullOrWhiteSpace(name) ? "optical-system" : name, revision, mode, files);
            try
            {
                foreach (var file in files.Where(file => file.Kind == "ray-database" && File.Exists(file.SourcePath)).DistinctBy(file => file.SourcePath))
                    capture.Pinned.Add(file.SourcePath, new FileStream(file.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete));
                return capture;
            }
            catch { capture.Dispose(); throw; }
        }
        catch { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); throw; }
    }

    public async Task<ProjectArchiveInfoDto> CreateAsync(string path, ProjectArchiveCapture capture, IReadOnlySet<string> includedPaths,
        IProgress<ProjectArchiveProgressDto>? progress = null, CancellationToken cancellationToken = default)
    {
        var output = Path.GetFullPath(path);
        if (_documents.CurrentPath is { } current && string.Equals(output, Path.GetFullPath(current),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ArgumentException("Choose an archive path different from the current project.", nameof(path));
        var files = capture.Files.Where(file => file.Required || includedPaths.Contains(file.RelativePath)).ToArray();
        var sources = files.Select(file => new ProjectArchiveSource(file.RelativePath, file.Kind, file.SourcePath, file.Required,
            capture.Pinned.GetValueOrDefault(file.SourcePath))).ToArray();
        await capture.WriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return Map(await ProjectArchiveStore.CreateAsync(path, capture.Name, typeof(WorkbenchApplication).Assembly.GetName().Version!.ToString(),
                capture.SourceRevision, capture.Mode.ToString(), sources, Adapt(progress), cancellationToken).ConfigureAwait(false));
        }
        finally { capture.WriteGate.Release(); }
    }

    public async Task<ProjectArchiveInfoDto> InspectAsync(string path, CancellationToken cancellationToken = default) =>
        Map(await ProjectArchiveStore.InspectAsync(path, cancellationToken).ConfigureAwait(false));

    public async Task<ProjectArchiveRestoreDto> RestoreAsync(string path, string directory, IProgress<ProjectArchiveProgressDto>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var restored = await ProjectArchiveStore.RestoreAsync(path, directory, Adapt(progress), cancellationToken).ConfigureAwait(false);
        var files = restored.Manifest.Entries.Where(entry => entry.Kind != "project").Select(entry => new ProjectArchiveFileDto(
            entry.RelativePath, entry.Kind, Path.Combine(restored.Directory, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar)),
            entry.Bytes, entry.Required, ResultKind(entry.Kind))).ToArray();
        // Inspect database semantics before replacing the current document.
        foreach (var database in files.Where(file => file.Kind == "ray-database"))
            _ = _analysis.InspectRayDatabase(database.SourcePath, cancellationToken: cancellationToken);
        await _documents.OpenAsync(restored.ProjectPath, cancellationToken).ConfigureAwait(false);
        _modes.SwitchTo(Enum.Parse<OpticalWorkbenchMode>(restored.Manifest.Mode));
        lock (Gate) { _linked.Clear(); _linked.AddRange(files); }
        var selected = files.FirstOrDefault(file => file.RelativePath == "results/rays.starrdb" && file.Kind == "ray-database")
            ?? files.FirstOrDefault(file => file.Kind == "ray-database");
        if (selected is not null) _analysis.SelectRayDatabase(selected.SourcePath);
        return new(restored.Directory, restored.ProjectPath, Map(restored.Manifest), files);
    }

    private static string PortableName(string name)
    {
        var safe = new string(name.Take(160).Select(c => c < 32 || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray()).TrimEnd('.', ' ');
        return "file-" + (safe.Length == 0 ? "attachment" : safe);
    }
    private static bool ResultKind(string kind) => kind is "ray-database" or "result" or "report" || kind.EndsWith("-result", StringComparison.Ordinal);
    private static ProjectArchiveInfoDto Map(ProjectArchiveManifest value) => new(value.Name, value.ApplicationVersion, value.CreatedUtc,
        value.SourceRevision, Enum.Parse<OpticalWorkbenchMode>(value.Mode), value.Entries.Select(entry =>
            new ProjectArchiveEntryDto(entry.RelativePath, entry.Kind, entry.Bytes, entry.Sha256, entry.Required)).ToArray());
    private static IProgress<ProjectArchiveProgress>? Adapt(IProgress<ProjectArchiveProgressDto>? progress) => progress is null ? null : new Observer(progress);
    private sealed class Observer(IProgress<ProjectArchiveProgressDto> target) : IProgress<ProjectArchiveProgress>
    { public void Report(ProjectArchiveProgress value) => target.Report(new(value.Phase, value.File, value.CompletedBytes, value.TotalBytes)); }
}
