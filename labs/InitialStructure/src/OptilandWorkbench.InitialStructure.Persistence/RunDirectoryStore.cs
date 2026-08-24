using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.Persistence;

public sealed class RunDirectoryStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task<string> SaveAsync(
        SearchRunManifest manifest,
        string rootDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("A run root directory is required.", nameof(rootDirectory));
        }

        EnsureSafeIdentifier(manifest.RunId, nameof(manifest));
        var runDirectory = Path.Combine(Path.GetFullPath(rootDirectory), manifest.RunId);
        var candidateDirectory = Path.Combine(runDirectory, "candidates");
        Directory.CreateDirectory(candidateDirectory);

        foreach (var candidate in manifest.Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureSafeIdentifier(candidate.CandidateId, nameof(manifest));
            var candidatePath = Path.Combine(candidateDirectory, candidate.CandidateId + ".json");
            await WriteAtomicAsync(candidatePath, candidate, cancellationToken);
        }

        var manifestPath = Path.Combine(runDirectory, "manifest.json");
        await WriteAtomicAsync(manifestPath, manifest, cancellationToken);
        return manifestPath;
    }

    public async Task<SearchRunManifest> LoadAsync(
        string manifestPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            throw new ArgumentException("A manifest path is required.", nameof(manifestPath));
        }

        await using var stream = File.OpenRead(Path.GetFullPath(manifestPath));
        var manifest = await JsonSerializer.DeserializeAsync<SearchRunManifest>(
            stream,
            Options,
            cancellationToken);
        if (manifest is null)
        {
            throw new InvalidDataException("The run manifest is empty or invalid.");
        }

        if (manifest.SchemaVersion != SearchRunManifest.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Run schema {manifest.SchemaVersion} is not supported.");
        }

        EnsureSafeIdentifier(manifest.RunId, nameof(manifestPath));
        return manifest;
    }

    private static async Task WriteAtomicAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void EnsureSafeIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            throw new ArgumentException(
                "Run and candidate identifiers may contain only ASCII letters, digits, and hyphens.",
                parameterName);
        }
    }
}
