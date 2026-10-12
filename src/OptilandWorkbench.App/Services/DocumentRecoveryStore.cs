using System.Security.Cryptography;
using System.Text.Json;
using OptilandWorkbench.Application.Contracts;
using OptilandWorkbench.Application.Services;

namespace OptilandWorkbench.App.Services;

internal sealed record DocumentRecoveryEntry(int FormatVersion, string Id, string Name,
    string? OriginalPath, long Revision, long DocumentGeneration, DateTimeOffset CapturedAt,
    string Sha256);

internal sealed record DocumentRecoveryCandidate(string SessionDirectory, DocumentRecoveryEntry Entry)
{
    public override string ToString() => $"{Entry.Name} — {Entry.CapturedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}\n"
        + (Entry.OriginalPath ?? "未命名工程");
}

// Only metadata published after a complete native save is offered for recovery.
// A live process holds owner.lock; other instances never retire its checkpoints.
internal sealed class DocumentRecoveryStore : IAsyncDisposable
{
    private readonly IOpticalDocumentService _documents;
    private readonly string _root;
    private readonly string _session;
    private readonly FileStream _owner;
    private readonly SemaphoreSlim _gate = new(1);
    private bool _stopped;
    private long _lastRevision = -1;
    internal const int MaximumListedEntries = 100;

    internal DocumentRecoveryStore(IOpticalDocumentService documents, string root)
    {
        _documents = documents;
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
        CheckPath(_root);
        _session = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_session);
        _owner = new FileStream(Path.Combine(_session, "owner.lock"), FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.None);
    }

    internal static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OptilandWorkbench", "Recovery");

    internal async Task<bool> CheckpointAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_stopped) return false;
            var before = _documents.GetRecoveryState();
            RetireOtherGenerations(before);
            if (!before.IsDirty)
            {
                RetireSavedCopies(before);
                return false;
            }
            if (before.Revision == _lastRevision) return false;
            var id = Guid.NewGuid().ToString("N");
            var project = Path.Combine(_session, id + ".staropt");
            var metadataPath = Path.Combine(_session, id + ".json");
            try
            {
                var source = await _documents.WriteRecoveryCopyAsync(project, cancellationToken).ConfigureAwait(false);
                if (source is null) return false;
                var current = _documents.GetRecoveryState();
                if (source.DocumentGeneration != current.DocumentGeneration
                    || !current.IsDirty && current.Revision >= source.Revision)
                {
                    File.Delete(project);
                    return false;
                }
                var entry = new DocumentRecoveryEntry(1, id, source.Name, source.OriginalPath,
                    source.Revision, source.DocumentGeneration, DateTimeOffset.UtcNow, Hash(project));
                await BoundedApplicationFile.WriteAllTextAtomicAsync(metadataPath,
                    JsonSerializer.Serialize(entry), 64 * 1024, "Document recovery metadata", cancellationToken).ConfigureAwait(false);
                _lastRevision = source.Revision;
                foreach (var old in OwnEntries().OrderByDescending(e => e.CapturedAt).Skip(2)) DeleteOwn(old);
                return true;
            }
            catch
            {
                // An unpublished file is not a recovery checkpoint. Keep the
                // previous published copies and avoid repeated orphan growth.
                if (!File.Exists(metadataPath) && File.Exists(project)) File.Delete(project);
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    internal IReadOnlyList<DocumentRecoveryCandidate> ListCandidates()
    {
        var candidates = new List<DocumentRecoveryCandidate>();
        foreach (var session in Directory.EnumerateDirectories(_root).Order(StringComparer.Ordinal).Take(1000))
        {
            if (!Guid.TryParseExact(Path.GetFileName(session), "N", out _) || session == _session) continue;
            try
            {
                CheckPath(session);
                var lockPath = Path.Combine(session, "owner.lock");
                using var probe = File.Exists(lockPath)
                    ? new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.None) : null;
                foreach (var metadata in Directory.EnumerateFiles(session, "*.json").Take(100))
                {
                    try
                    {
                        var entry = ReadEntry(metadata);
                        var project = Path.Combine(session, entry.Id + ".staropt");
                        if (Hash(project) == entry.Sha256) candidates.Add(new(session, entry));
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException
                        or JsonException or InvalidDataException)
                    { }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException) { }
        }
        return candidates.OrderByDescending(c => c.Entry.CapturedAt).Take(MaximumListedEntries).ToArray();
    }

    internal async Task RestoreAsync(DocumentRecoveryCandidate candidate, CancellationToken cancellationToken = default)
    {
        var project = ValidateCandidate(candidate);
        await _documents.RestoreRecoveryCopyAsync(project, cancellationToken).ConfigureAwait(false);
        // Publish a replacement first, so a second crash during recovery loses nothing.
        if (!await CheckpointAsync(cancellationToken).ConfigureAwait(false))
            throw new IOException("已恢复工程，但新的恢复副本未写入；原恢复副本保留。");
        DeleteCandidate(candidate);
    }

    internal void DeleteCandidate(DocumentRecoveryCandidate candidate)
    {
        _ = ValidateCandidate(candidate);
        File.Delete(Path.Combine(candidate.SessionDirectory, candidate.Entry.Id + ".json"));
        File.Delete(Path.Combine(candidate.SessionDirectory, candidate.Entry.Id + ".staropt"));
    }

    private string ValidateCandidate(DocumentRecoveryCandidate candidate)
    {
        var session = Path.GetFullPath(candidate.SessionDirectory);
        if (!Guid.TryParseExact(candidate.Entry.Id, "N", out _))
            throw new InvalidDataException("恢复副本身份无效。");
        if (!string.Equals(Path.GetDirectoryName(session), _root, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(Path.GetFileName(session), "N", out _))
            throw new InvalidDataException("恢复副本不属于本恢复目录。");
        CheckPath(session);
        var lockPath = Path.Combine(session, "owner.lock");
        using var probe = File.Exists(lockPath)
            ? new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.None) : null;
        var entry = ReadEntry(Path.Combine(session, candidate.Entry.Id + ".json"));
        if (entry != candidate.Entry) throw new InvalidDataException("恢复副本已变化，请重新打开列表。");
        var project = Path.Combine(session, entry.Id + ".staropt");
        if (Hash(project) != entry.Sha256) throw new InvalidDataException("恢复副本校验失败；原工程保持不变。");
        return project;
    }

    private static DocumentRecoveryEntry ReadEntry(string path)
    {
        CheckPath(path);
        var entry = JsonSerializer.Deserialize<DocumentRecoveryEntry>(
            BoundedApplicationFile.ReadAllText(path, 64 * 1024, "Document recovery metadata"))
            ?? throw new InvalidDataException("恢复记录为空。");
        if (entry.FormatVersion != 1 || !Guid.TryParseExact(entry.Id, "N", out _)
            || Path.GetFileNameWithoutExtension(path) != entry.Id || entry.Revision < 0
            || entry.Sha256 is not { Length: 64 } || !entry.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("恢复记录无效。");
        return entry;
    }

    private IEnumerable<DocumentRecoveryEntry> OwnEntries() => Directory.EnumerateFiles(_session, "*.json")
        .Select(ReadEntry).ToArray();

    private void RetireOtherGenerations(DocumentRecoveryState current)
    {
        foreach (var entry in OwnEntries().Where(e => e.DocumentGeneration != current.DocumentGeneration)) DeleteOwn(entry);
    }

    private void RetireSavedCopies(DocumentRecoveryState current)
    {
        foreach (var entry in OwnEntries().Where(e => e.Revision <= current.Revision)) DeleteOwn(entry);
    }

    private void DeleteOwn(DocumentRecoveryEntry entry)
    {
        File.Delete(Path.Combine(_session, entry.Id + ".json"));
        File.Delete(Path.Combine(_session, entry.Id + ".staropt"));
    }

    private static string Hash(string path)
    {
        CheckPath(path);
        using var stream = BoundedApplicationFile.OpenRead(path, 1024L * 1024 * 1024, "Recovery project");
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void CheckPath(string path)
    {
        for (var item = Path.GetFullPath(path); !string.IsNullOrEmpty(item); item = Path.GetDirectoryName(item))
            if (Path.Exists(item) && (File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("恢复目录不能包含符号链接或目录联接。");
    }

    internal async Task StopAsync(bool cleanExit)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_stopped) return;
            if (cleanExit) foreach (var entry in OwnEntries()) DeleteOwn(entry);
            _stopped = true;
            _owner.Dispose();
            if (cleanExit)
            {
                File.Delete(Path.Combine(_session, "owner.lock"));
                if (!Directory.EnumerateFileSystemEntries(_session).Any()) Directory.Delete(_session);
            }
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync() => await StopAsync(false).ConfigureAwait(false);
}
