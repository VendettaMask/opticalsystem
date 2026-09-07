using System.Text.Json;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.Persistence;

/// <summary>The new desktop's run files and last-run pointer never share the legacy v3 store.</summary>
public sealed class FlatStartRunLibrary(string directory)
{
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public string PathFor(string runId)
    {
        RunDirectoryStore.EnsureSafeIdentifier(runId, nameof(runId));
        return Path.Combine(DirectoryPath, runId + ".family.json");
    }
    public async ValueTask SaveAsync(FlatStartSearchCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        await new FlatStartSearchCheckpointStore().SaveAsync(checkpoint, PathFor(checkpoint.RunId), cancellationToken);
        await BoundedFile.WriteAllBytesAtomicAsync(Path.Combine(DirectoryPath, "last-run.json"),
            JsonSerializer.SerializeToUtf8Bytes(checkpoint.RunId), 1024, "last flat-start run", cancellationToken);
    }
    public async Task<FlatStartSearchCheckpoint?> LoadLastAsync(CancellationToken cancellationToken = default)
    {
        var pointer = Path.Combine(DirectoryPath, "last-run.json");
        if (!File.Exists(pointer)) return null;
        var bytes = await BoundedFile.ReadAllBytesAsync(pointer, 1024, "last flat-start run", cancellationToken);
        string id;
        try { id = JsonSerializer.Deserialize<string>(bytes) ?? throw new InvalidDataException("The last-run pointer is empty."); }
        catch (JsonException exception) { throw new InvalidDataException("The last-run pointer is damaged.", exception); }
        return await new FlatStartSearchCheckpointStore().LoadAsync(PathFor(id), cancellationToken);
    }
}
