using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.Persistence;

public sealed class SearchCheckpointStore
{
    private const int MaximumCheckpointFiles = 1_024;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async ValueTask SaveAsync(
        SearchCheckpoint checkpoint,
        string rootDirectory,
        CancellationToken cancellationToken = default)
    {
        Validate(checkpoint);
        var path = GetPath(rootDirectory, checkpoint.RunId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(checkpoint, Options);
        await BoundedFile.WriteAllBytesAtomicAsync(
            path,
            bytes,
            InitialStructureLimits.MaximumManifestBytes,
            "initial-structure checkpoint",
            cancellationToken);
    }

    public async Task<SearchCheckpoint> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var bytes = await BoundedFile.ReadAllBytesAsync(
            Path.GetFullPath(path),
            InitialStructureLimits.MaximumManifestBytes,
            "initial-structure checkpoint",
            cancellationToken);
        SearchCheckpoint checkpoint;
        try
        {
            checkpoint = JsonSerializer.Deserialize<SearchCheckpoint>(bytes, Options)
                ?? throw new InvalidDataException("The initial-structure checkpoint is empty or invalid.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The initial-structure checkpoint JSON is invalid.", exception);
        }

        Validate(checkpoint);
        return checkpoint;
    }

    public async Task<SearchCheckpoint?> LoadLatestAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(root))
        {
            return null;
        }

        var paths = Directory.EnumerateFiles(root, "*.checkpoint.json", SearchOption.TopDirectoryOnly)
            .Take(MaximumCheckpointFiles + 1)
            .ToArray();
        if (paths.Length > MaximumCheckpointFiles)
        {
            throw new InvalidDataException(
                $"The checkpoint directory contains more than {MaximumCheckpointFiles} files.");
        }

        SearchCheckpoint? latest = null;
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = await LoadAsync(path, cancellationToken);
            if (latest is null || candidate.UpdatedUtc > latest.UpdatedUtc)
            {
                latest = candidate;
            }
        }

        return latest;
    }

    public void Delete(string rootDirectory, string runId)
    {
        var path = GetPath(rootDirectory, runId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public static string GetPath(string rootDirectory, string runId)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("A checkpoint root directory is required.", nameof(rootDirectory));
        }

        RunDirectoryStore.EnsureSafeIdentifier(runId, nameof(runId));
        return Path.Combine(Path.GetFullPath(rootDirectory), runId + ".checkpoint.json");
    }

    private static void Validate(SearchCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        RunDirectoryStore.EnsureSafeIdentifier(checkpoint.RunId, nameof(checkpoint));
        if (checkpoint.SchemaVersion != SearchCheckpoint.CurrentSchemaVersion
            || checkpoint.Specification is null
            || checkpoint.Specification.SchemaVersion != InitialStructureSpecification.CurrentSchemaVersion
            || checkpoint.Algorithm is null
            || string.IsNullOrWhiteSpace(checkpoint.Algorithm.Name)
            || string.IsNullOrWhiteSpace(checkpoint.Algorithm.Version)
            || string.IsNullOrWhiteSpace(checkpoint.SpecificationFingerprint)
            || string.IsNullOrWhiteSpace(checkpoint.Stage)
            || checkpoint.UpdatedUtc < checkpoint.CreatedUtc
            || checkpoint.CompletedInitialSeedIndices is null
            || checkpoint.SeedCandidates is null
            || checkpoint.Diagnostics is null)
        {
            throw new InvalidDataException("The initial-structure checkpoint metadata is invalid.");
        }

        var completed = checkpoint.CompletedInitialSeedIndices.ToHashSet();
        if (completed.Count != checkpoint.CompletedInitialSeedIndices.Count
            || completed.Any(index => index < 0))
        {
            throw new InvalidDataException("The checkpoint contains invalid seed indices.");
        }

        RunDirectoryStore.ValidateCandidateSet(checkpoint.SeedCandidates, nameof(checkpoint));
        if (checkpoint.SeedCandidates.Any(candidate =>
            candidate.Lineage.Generation != 1
            || !completed.Contains(candidate.Lineage.SeedIndex)))
        {
            throw new InvalidDataException("The checkpoint candidate set does not match completed seeds.");
        }

        if (checkpoint.Diagnostics.Count > InitialStructureLimits.MaximumDiagnosticCount
            || checkpoint.Diagnostics.Any(diagnostic =>
                diagnostic is null
                || string.IsNullOrWhiteSpace(diagnostic.Code)
                || diagnostic.Code.Length > InitialStructureLimits.MaximumNameLength
                || string.IsNullOrWhiteSpace(diagnostic.Message)
                || diagnostic.Message.Length > InitialStructureLimits.MaximumMessageLength))
        {
            throw new InvalidDataException("The checkpoint diagnostic table is invalid or too large.");
        }
    }
}
