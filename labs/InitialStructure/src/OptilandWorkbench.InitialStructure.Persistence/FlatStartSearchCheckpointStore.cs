using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.InitialStructure.Contracts;

namespace OptilandWorkbench.InitialStructure.Persistence;

/// <summary>Separate, checksummed family-search format; never interpreted as a legacy search checkpoint.</summary>
public sealed class FlatStartSearchCheckpointStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async ValueTask SaveAsync(FlatStartSearchCheckpoint checkpoint, string path, CancellationToken cancellationToken = default)
    {
        Validate(checkpoint);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Envelope(checkpoint, Fingerprint(checkpoint)), Options);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await BoundedFile.WriteAllBytesAtomicAsync(path, bytes, InitialStructureLimits.MaximumManifestBytes,
            "flat-family checkpoint", cancellationToken);
    }

    public async Task<FlatStartSearchCheckpoint> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var bytes = await BoundedFile.ReadAllBytesAsync(path, InitialStructureLimits.MaximumManifestBytes, "flat-family checkpoint", cancellationToken);
        Envelope envelope;
        try { envelope = JsonSerializer.Deserialize<Envelope>(bytes, Options) ?? throw new InvalidDataException("Empty flat-family checkpoint."); }
        catch (JsonException exception) { throw new InvalidDataException("Invalid flat-family checkpoint JSON.", exception); }
        if (envelope.Checkpoint is null || envelope.Sha256 != Fingerprint(envelope.Checkpoint))
            throw new InvalidDataException("Flat-family checkpoint checksum mismatch.");
        Validate(envelope.Checkpoint);
        return envelope.Checkpoint;
    }

    private static void Validate(FlatStartSearchCheckpoint checkpoint)
    {
        FlatStartCheckpointValidation.Validate(checkpoint);
        RunDirectoryStore.EnsureSafeIdentifier(checkpoint.RunId, nameof(checkpoint));
        if (checkpoint.Origin is { } origin)
        {
            RunDirectoryStore.EnsureSafeIdentifier(origin.RunId, nameof(checkpoint));
            RunDirectoryStore.ValidateCandidateSet([origin.Source.Candidate!], nameof(checkpoint));
        }
        RunDirectoryStore.ValidateCandidateSet(checkpoint.Trials.Where(trial => trial.Candidate is not null)
            .Select(trial => trial.Candidate!).ToArray(), nameof(checkpoint));
    }

    private static string Fingerprint(FlatStartSearchCheckpoint checkpoint) => Convert.ToHexStringLower(
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(checkpoint, Options)));

    private sealed record Envelope(FlatStartSearchCheckpoint Checkpoint, string Sha256);
}
