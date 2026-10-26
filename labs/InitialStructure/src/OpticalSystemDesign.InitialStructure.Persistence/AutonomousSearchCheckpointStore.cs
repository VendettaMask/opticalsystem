using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;

namespace OpticalSystemDesign.InitialStructure.Persistence;

/// <summary>Checksummed, bounded atomic files with revision compare-and-swap under an exclusive writer lock.</summary>
public sealed class AutonomousSearchCheckpointStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async ValueTask SaveAsync(AutonomousSearchCheckpoint checkpoint, string path, long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        AutonomousSearchService.Validate(checkpoint);
        RunDirectoryStore.EnsureSafeIdentifier(checkpoint.RunId, nameof(checkpoint));
        if (checkpoint.Revision != expectedRevision + 1) throw new InvalidDataException("Checkpoint revision must advance exactly once.");
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var checksum = ContentFingerprint.Compute(checkpoint);
        if (File.Exists(path))
        {
            var current = await LoadAsync(path, cancellationToken);
            if (current.RunId != checkpoint.RunId || current.PlanFingerprint != checkpoint.PlanFingerprint)
                throw new InvalidDataException("Cannot replace another run or original specification.");
            if (current.Revision == checkpoint.Revision && ContentFingerprint.Compute(current) == checksum) return;
            if (current.Revision != expectedRevision) throw new InvalidDataException("Stale autonomous checkpoint writer.");
            if (checkpoint.Consumed.Elapsed < current.Consumed.Elapsed || checkpoint.Events.Length < current.Events.Length
                || ContentFingerprint.Compute(checkpoint.Events.Take(current.Events.Length).ToArray()) != ContentFingerprint.Compute(current.Events.ToArray()))
                throw new InvalidDataException("Consumed work and prior audit events cannot be rolled back or rewritten.");
            foreach (var candidate in current.Candidates)
            {
                var next = checkpoint.Candidates.SingleOrDefault(item => item.Id == candidate.Id);
                if (next is null || next.OperationCursor < candidate.OperationCursor
                    || ContentFingerprint.Compute(next with { OperationCursor = candidate.OperationCursor }) != ContentFingerprint.Compute(candidate))
                    throw new InvalidDataException("Prior candidate prescriptions, assessments and operation cursors cannot be rolled back.");
            }
            foreach (var accepted in current.AcceptedArchive)
                if (!checkpoint.AcceptedArchive.Any(candidate => ContentFingerprint.Compute(candidate) == ContentFingerprint.Compute(accepted)))
                    throw new InvalidDataException("An accepted archive entry cannot be removed or replaced.");
        }
        else if (expectedRevision != 0) throw new InvalidDataException("Missing previous autonomous checkpoint revision.");
        await BoundedFile.WriteAtomicAsync(path, InitialStructureLimits.MaximumManifestBytes, "autonomous topology checkpoint",
            (stream, token) => JsonSerializer.SerializeAsync(stream, new Envelope(checkpoint, checksum), Options, token), cancellationToken);
    }

    public async Task<AutonomousSearchCheckpoint> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var bytes = await BoundedFile.ReadAllBytesAsync(path, InitialStructureLimits.MaximumManifestBytes,
            "autonomous topology checkpoint", cancellationToken);
        Envelope envelope;
        try { envelope = JsonSerializer.Deserialize<Envelope>(bytes, Options) ?? throw new InvalidDataException("Empty autonomous checkpoint."); }
        catch (JsonException error) { throw new InvalidDataException("Invalid autonomous checkpoint JSON.", error); }
        if (envelope.Checkpoint is null || envelope.Sha256 != ContentFingerprint.Compute(envelope.Checkpoint))
            throw new InvalidDataException("Autonomous checkpoint checksum mismatch.");
        AutonomousSearchService.Validate(envelope.Checkpoint);
        RunDirectoryStore.EnsureSafeIdentifier(envelope.Checkpoint.RunId, nameof(path));
        return envelope.Checkpoint;
    }

    private sealed record Envelope(AutonomousSearchCheckpoint Checkpoint, string Sha256);
}
