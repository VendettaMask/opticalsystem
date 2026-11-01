using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;

namespace OpticalSystemDesign.InitialStructure.Persistence;

/// <summary>Bounded atomic preparation files. Streaming checksums avoid materializing and
/// re-parsing dense Jacobians on every update. Cached metadata is trusted only after
/// verifying the complete on-disk digest under the exclusive writer lease.</summary>
public sealed class SeedPreparationCheckpointStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
    private sealed record SavedSummary(string FileDigest, string StateDigest, string RunId, string Plan,
        string Protocol, long Revision, AutonomousSearchWork Work, string Stage, int Cursor, int Lost,
        string[] Candidates, string[] Phases, string Kernel, string[] SpcParents, string[] SpcBranches, string[] SpcPool);
    private readonly ConcurrentDictionary<string, SavedSummary> _saved = new(StringComparer.Ordinal);

    public async ValueTask SaveAsync(SeedPreparationCheckpoint state, string path, long expectedRevision, CancellationToken token = default)
    {
        SeedPreparationSearch.Validate(state);
        RunDirectoryStore.EnsureSafeIdentifier(state.RunId, nameof(state));
        if (state.Revision != expectedRevision + 1) throw new InvalidDataException("Preparation revision must advance exactly once.");
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (File.Exists(path))
        {
            var fileDigest = await FileDigest(path, token);
            if (!_saved.TryGetValue(path, out var previous) || previous.FileDigest != fileDigest)
            {
                _ = await LoadAsync(path, token);
                previous = _saved[path];
            }
            if (previous.RunId != state.RunId || previous.Plan != state.PlanFingerprint || previous.Protocol != state.ProtocolFingerprint || previous.Kernel != state.KernelFingerprint)
                throw new InvalidDataException("Cannot replace another preparation or alter its original task.");
            if (previous.Revision == state.Revision && previous.StateDigest == await StateDigest(state, token)) return;
            if (previous.Revision != expectedRevision) throw new InvalidDataException("Stale preparation checkpoint writer.");
            var a = previous.Work; var b = state.Consumed;
            if (b.FunctionEvaluations < a.FunctionEvaluations || b.GradientEvaluations < a.GradientEvaluations || b.ValidationEvaluations < a.ValidationEvaluations
                || b.MatrixEvaluations < a.MatrixEvaluations || b.DifferenceEvaluations < a.DifferenceEvaluations || b.JacobianBuilds < a.JacobianBuilds
                || b.GeometryProbes < a.GeometryProbes || b.TracedRays < a.TracedRays || b.Elapsed < a.Elapsed
                || state.Candidates.Length < previous.Candidates.Length || state.Phases.Length < previous.Phases.Length
                || !previous.Candidates.SequenceEqual(state.Candidates.Take(previous.Candidates.Length).Select(ContentFingerprint.Compute))
                || !previous.Phases.SequenceEqual(state.Phases.Take(previous.Phases.Length).Select(ContentFingerprint.Compute))
                || !previous.SpcParents.SequenceEqual((state.Spc?.Parents ?? []).Take(previous.SpcParents.Length).Select(ContentFingerprint.Compute))
                || !previous.SpcBranches.SequenceEqual((state.Spc?.Branches ?? []).Take(previous.SpcBranches.Length).Select(ContentFingerprint.Compute))
                || !previous.SpcPool.SequenceEqual((state.Spc?.Pool ?? []).Take(previous.SpcPool.Length).Select(ContentFingerprint.Compute))
                || state.LostReservations < previous.Lost || state.Stage == previous.Stage && state.Cursor < previous.Cursor || previous.Stage == "Finished")
                throw new InvalidDataException("Preparation history and consumed budgets cannot be rolled back or rewritten.");
        }
        else if (expectedRevision != 0) throw new InvalidDataException("Missing previous preparation checkpoint revision.");

        string? stateDigest = null, savedFileDigest = null;
        await BoundedFile.WriteAtomicAsync(path, InitialStructureLimits.MaximumManifestBytes, "seed preparation checkpoint", async (stream, ct) =>
        {
            using var fileHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var whole = new HashingWriter(stream, fileHash);
            await whole.WriteAsync("{\"format\":2,\"checkpoint\":"u8.ToArray(), ct);
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                using var body = new HashingWriter(whole, hash);
                await JsonSerializer.SerializeAsync(body, state, Json, ct);
                stateDigest = Convert.ToHexStringLower(hash.GetHashAndReset());
            }
            await whole.WriteAsync(Encoding.UTF8.GetBytes(",\"sha256\":\"" + stateDigest + "\"}"), ct);
            savedFileDigest = Convert.ToHexStringLower(fileHash.GetHashAndReset());
        }, token);
        _saved[path] = Summary(state, savedFileDigest!, stateDigest!);
    }

    public async Task<SeedPreparationCheckpoint> LoadAsync(string path, CancellationToken token = default)
    {
        path = Path.GetFullPath(path);
        var bytes = await BoundedFile.ReadAllBytesAsync(path, InitialStructureLimits.MaximumManifestBytes, "seed preparation checkpoint", token);
        Envelope envelope;
        try { envelope = JsonSerializer.Deserialize<Envelope>(bytes, Json) ?? throw new InvalidDataException("Empty preparation checkpoint."); }
        catch (JsonException error) { throw new InvalidDataException("Invalid preparation checkpoint JSON.", error); }
        if (envelope.Checkpoint is null || envelope.Format is not (1 or 2)) throw new InvalidDataException("Unsupported preparation envelope.");
        var checksum = envelope.Format == 1 ? ContentFingerprint.Compute(envelope.Checkpoint) : await StateDigest(envelope.Checkpoint, token);
        if (envelope.Sha256 != checksum) throw new InvalidDataException("Preparation checkpoint checksum mismatch.");
        SeedPreparationSearch.Validate(envelope.Checkpoint);
        RunDirectoryStore.EnsureSafeIdentifier(envelope.Checkpoint.RunId, nameof(path));
        _saved[path] = Summary(envelope.Checkpoint, Convert.ToHexStringLower(SHA256.HashData(bytes)),
            envelope.Format == 2 ? checksum : await StateDigest(envelope.Checkpoint, token));
        return envelope.Checkpoint;
    }

    private static SavedSummary Summary(SeedPreparationCheckpoint state, string fileDigest, string stateDigest) =>
        new(fileDigest, stateDigest, state.RunId, state.PlanFingerprint, state.ProtocolFingerprint, state.Revision, state.Consumed,
            state.Stage, state.Cursor, state.LostReservations, state.Candidates.Select(ContentFingerprint.Compute).ToArray(), state.Phases.Select(ContentFingerprint.Compute).ToArray(),
            state.KernelFingerprint, (state.Spc?.Parents ?? []).Select(ContentFingerprint.Compute).ToArray(),
            (state.Spc?.Branches ?? []).Select(ContentFingerprint.Compute).ToArray(), (state.Spc?.Pool ?? []).Select(ContentFingerprint.Compute).ToArray());

    private static async Task<string> FileDigest(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > InitialStructureLimits.MaximumManifestBytes) throw new InvalidDataException("Preparation checkpoint exceeds the bounded file limit.");
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token));
    }
    private static async Task<string> StateDigest(SeedPreparationCheckpoint state, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var stream = new HashingWriter(Stream.Null, hash);
        await JsonSerializer.SerializeAsync(stream, state, Json, token);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    private sealed record Envelope(SeedPreparationCheckpoint Checkpoint, string Sha256, int Format = 1);

    private sealed class HashingWriter(Stream target, IncrementalHash hash) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => target.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => target.FlushAsync(cancellationToken);
        public override void Write(byte[] buffer, int offset, int count) { hash.AppendData(buffer, offset, count); target.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { hash.AppendData(buffer); target.Write(buffer); }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        { hash.AppendData(buffer.Span); await target.WriteAsync(buffer, cancellationToken); }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        // The atomic file and hash scopes are owned by the caller, not by this adapter.
    }
}
