using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.CoatingDesign.Engine;

public sealed record CoatingRecoveryDraft(int Version, string SessionId, string Name, DateTimeOffset UpdatedUtc, string EditorJson, string Sha256);

/// <summary>One leased, atomic draft per editor window. Disposing retains a crash draft; CompleteAsync removes only this session.</summary>
public sealed class CoatingRecoverySession : IDisposable
{
    public const long MaximumBytes = 32 * 1024 * 1024;
    private readonly FileStream _lease;
    private readonly SemaphoreSlim _gate = new(1);
    private bool _finished, _disposed;
    private long _revision = -1;
    public string Path { get; }
    public string SessionId { get; }
    private CoatingRecoverySession(string path, string id, FileStream lease) { Path = path; SessionId = id; _lease = lease; }
    public static CoatingRecoverySession Create(string directory)
    {
        Directory.CreateDirectory(directory);
        var id = Guid.NewGuid().ToString("N"); var path = System.IO.Path.Combine(directory, id + ".draft.json");
        return new(path, id, new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
    }
    public static IReadOnlyList<CoatingRecoverySession> AcquireAvailable(string directory)
    {
        if (!Directory.Exists(directory)) return [];
        var acquired = new List<CoatingRecoverySession>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(directory, "*.draft.json").Take(256))
            {
                var id = System.IO.Path.GetFileName(path)[..^11];
                if (!Guid.TryParseExact(id, "N", out _)) continue;
                try { acquired.Add(new(path, id, new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))); }
                catch (IOException) { /* A live editor owns this draft. Never touch it. */ }
            }
            return acquired;
        }
        catch { foreach (var session in acquired) session.Dispose(); throw; }
    }
    public CoatingRecoveryDraft Read()
    {
        var d = JsonSerializer.Deserialize<CoatingRecoveryDraft>(BoundedFile.ReadAllBytes(Path, MaximumBytes, "镀膜恢复草稿"), Experiment.JsonOptions)
            ?? throw new InvalidDataException("恢复草稿为空。");
        if (d.Version != 1 || d.SessionId != SessionId || d.EditorJson is null || d.Name is null || d.Sha256 != Hash(d.EditorJson))
            throw new InvalidDataException("恢复草稿的版本、身份或校验和无效；文件已保留。");
        return d;
    }
    public async Task WriteAsync(string name, string editorJson, long revision)
    {
        ArgumentNullException.ThrowIfNull(editorJson);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_finished || _disposed || revision < _revision) return;
            var draft = new CoatingRecoveryDraft(1, SessionId, name, DateTimeOffset.UtcNow, editorJson, Hash(editorJson));
            var bytes = JsonSerializer.SerializeToUtf8Bytes(draft, Experiment.JsonOptions);
            if (bytes.LongLength > MaximumBytes) throw new InvalidDataException("恢复草稿超过32 MiB，原草稿已保留。");
            await Task.Run(() => BoundedFile.WriteAtomic(Path, MaximumBytes, "镀膜恢复草稿", s => s.Write(bytes))).ConfigureAwait(false);
            _revision = revision;
        }
        finally { _gate.Release(); }
    }
    public async Task ClearAsync(long revision)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { if (!_finished && !_disposed && revision >= _revision) { File.Delete(Path); _revision = revision; } }
        finally { _gate.Release(); }
    }
    public async Task CompleteAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { if (_disposed) return; File.Delete(Path); _finished = true; }
        finally { _gate.Release(); }
        Dispose();
    }
    public void Dispose()
    {
        _gate.Wait();
        try { if (_disposed) return; _disposed = true; _lease.Dispose(); }
        finally { _gate.Release(); }
        // Keep the empty lock file: deleting it after releasing a lease can race another process's lease.
    }
    private static string Hash(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
}
