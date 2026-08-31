using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core.Serialization;
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
        ValidateManifest(manifest, nameof(manifest));
        var fullRoot = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(fullRoot);
        var runDirectory = Path.Combine(fullRoot, manifest.RunId);
        if (Directory.Exists(runDirectory))
        {
            throw new IOException($"Run directory '{manifest.RunId}' already exists and is immutable.");
        }

        var stagingDirectory = Path.Combine(fullRoot, $".{manifest.RunId}.{Guid.NewGuid():N}.tmp");
        try
        {
            var candidateDirectory = Path.Combine(stagingDirectory, "candidates");
            Directory.CreateDirectory(candidateDirectory);
            foreach (var candidate in manifest.Candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureSafeIdentifier(candidate.CandidateId, nameof(manifest));
                var candidatePath = Path.Combine(candidateDirectory, candidate.CandidateId + ".json");
                await WriteAtomicAsync(
                    candidatePath,
                    candidate,
                    InitialStructureLimits.MaximumSettingsBytes,
                    cancellationToken);
            }

            var stagingManifestPath = Path.Combine(stagingDirectory, "manifest.json");
            await WriteAtomicAsync(
                stagingManifestPath,
                manifest,
                InitialStructureLimits.MaximumManifestBytes,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(stagingDirectory, runDirectory);
            return Path.Combine(runDirectory, "manifest.json");
        }
        finally
        {
            DeleteDirectory(stagingDirectory);
        }
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
        if (stream.Length > InitialStructureLimits.MaximumManifestBytes)
        {
            throw new InvalidDataException(
                $"Run manifest exceeds the {InitialStructureLimits.MaximumManifestBytes:N0}-byte limit.");
        }
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
        ValidateManifest(manifest, nameof(manifestPath));
        await ValidateCandidateFilesAsync(
            Path.GetDirectoryName(Path.GetFullPath(manifestPath))
                ?? throw new InvalidDataException("The run manifest has no parent directory."),
            manifest.Candidates,
            cancellationToken);
        return manifest;
    }

    private static async Task WriteAtomicAsync<T>(
        string path,
        T value,
        long maximumBytes,
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
                await using var bounded = new MaximumLengthWriteStream(stream, maximumBytes);
                await JsonSerializer.SerializeAsync(bounded, value, Options, cancellationToken);
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

    internal static void EnsureSafeIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > InitialStructureLimits.MaximumIdentifierLength
            || value.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            throw new ArgumentException(
                $"Run and candidate identifiers may contain at most {InitialStructureLimits.MaximumIdentifierLength} ASCII letters, digits, and hyphens.",
                parameterName);
        }
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static async Task ValidateCandidateFilesAsync(
        string runDirectory,
        IReadOnlyList<CandidateSnapshot> candidates,
        CancellationToken cancellationToken)
    {
        var candidateDirectory = Path.Combine(runDirectory, "candidates");
        if (!Directory.Exists(candidateDirectory))
        {
            throw new InvalidDataException("The run candidate directory is missing.");
        }

        var expectedFiles = candidates
            .Select(candidate => candidate.CandidateId + ".json")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var actualFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(candidateDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!actualFiles.Add(Path.GetFileName(path)) || actualFiles.Count > expectedFiles.Count)
            {
                throw new InvalidDataException("The run candidate directory contains duplicate or unexpected JSON files.");
            }
        }
        if (!actualFiles.SetEquals(expectedFiles))
        {
            throw new InvalidDataException("The run candidate directory does not match the manifest.");
        }

        foreach (var expected in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(candidateDirectory, expected.CandidateId + ".json");
            await using var stream = File.OpenRead(path);
            if (stream.Length is <= 0 or > InitialStructureLimits.MaximumSettingsBytes)
            {
                throw new InvalidDataException(
                    $"Candidate '{expected.CandidateId}' exceeds the supported file size.");
            }

            var actual = await JsonSerializer.DeserializeAsync<CandidateSnapshot>(
                    stream,
                    Options,
                    cancellationToken)
                ?? throw new InvalidDataException($"Candidate '{expected.CandidateId}' is empty or invalid.");
            ValidateCandidate(actual, nameof(candidates));
            var expectedHash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(expected, Options));
            var actualHash = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(actual, Options));
            if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
            {
                throw new InvalidDataException(
                    $"Candidate '{expected.CandidateId}' does not match the run manifest.");
            }
        }
    }

    private static void ValidateManifest(SearchRunManifest manifest, string parameterName)
    {
        if (!Enum.IsDefined(manifest.State))
        {
            throw new InvalidDataException("The run state is invalid.");
        }
        if (manifest.Specification is null
            || manifest.Specification.SchemaVersion != InitialStructureSpecification.CurrentSchemaVersion)
        {
            throw new InvalidDataException("The run specification is missing or uses an unsupported schema.");
        }
        RequireText(manifest.SpecificationFingerprint, "specification fingerprint");
        if (manifest.Algorithm is null)
        {
            throw new InvalidDataException("The run algorithm identity is missing.");
        }
        RequireText(manifest.Algorithm.Name, "algorithm name");
        RequireText(manifest.Algorithm.Version, "algorithm version");
        RequireText(manifest.Algorithm.NumericBackend, "numeric backend");
        if (manifest.CompletedUtc is { } completed && completed < manifest.CreatedUtc)
        {
            throw new InvalidDataException("The run completion time precedes its creation time.");
        }
        if (manifest.Diagnostics is null
            || manifest.Diagnostics.Count > InitialStructureLimits.MaximumDiagnosticCount)
        {
            throw new InvalidDataException("The run diagnostic table is missing or too large.");
        }
        foreach (var diagnostic in manifest.Diagnostics)
        {
            if (diagnostic is null)
            {
                throw new InvalidDataException("A run diagnostic cannot be null.");
            }
            RequireText(diagnostic.Code, "diagnostic code");
            RequireText(
                diagnostic.Message,
                "diagnostic message",
                InitialStructureLimits.MaximumMessageLength);
            if (diagnostic.CandidateId is { } candidateId)
            {
                EnsureSafeIdentifier(candidateId, parameterName);
            }
        }

        ValidateCandidateSet(manifest.Candidates, parameterName);
    }

    internal static void ValidateCandidateSet(
        IReadOnlyList<CandidateSnapshot> candidates,
        string parameterName)
    {
        if (candidates is null)
        {
            throw new InvalidDataException("A run candidate list is required.");
        }

        if (candidates.Count > InitialStructureLimits.MaximumCandidateCount)
        {
            throw new InvalidDataException(
                $"A run cannot contain more than {InitialStructureLimits.MaximumCandidateCount} candidates.");
        }

        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (candidate is null)
            {
                throw new InvalidDataException("A run candidate cannot be null.");
            }

            EnsureSafeIdentifier(candidate.CandidateId, parameterName);
            if (!identifiers.Add(candidate.CandidateId))
            {
                throw new InvalidDataException($"Duplicate candidate id '{candidate.CandidateId}'.");
            }

            ValidateCandidate(candidate, parameterName);
        }
    }

    private static void ValidateCandidate(CandidateSnapshot candidate, string parameterName)
    {
        if (candidate.SchemaVersion != CandidateSnapshot.CurrentSchemaVersion
            || !Enum.IsDefined(candidate.Status))
        {
            throw new InvalidDataException($"Candidate '{candidate.CandidateId}' has invalid schema or status.");
        }
        RequireText(candidate.OpticFingerprint, "optic fingerprint");
        if (candidate.FlatRootOptic is null || candidate.Optic is null)
        {
            throw new InvalidDataException($"Candidate '{candidate.CandidateId}' has no optical snapshots.");
        }
        OpticSnapshotValidator.Validate(candidate.FlatRootOptic);
        OpticSnapshotValidator.Validate(candidate.Optic);
        if (candidate.Lineage is null
            || candidate.Lineage.Generation < 0
            || candidate.Lineage.ElementCount < 1
            || candidate.Lineage.SeedIndex < 0)
        {
            throw new InvalidDataException($"Candidate '{candidate.CandidateId}' has invalid lineage.");
        }
        RequireText(candidate.Lineage.RootFingerprint, "root fingerprint");
        RequireText(candidate.Lineage.Operation, "lineage operation");
        if (candidate.Lineage.ParentCandidateId is { } parentCandidateId)
        {
            EnsureSafeIdentifier(parentCandidateId, parameterName);
        }
        if (candidate.Evaluation is null
            || !Finite(candidate.Evaluation.EffectiveFocalLengthMillimeters)
            || !Finite(candidate.Evaluation.FNumber)
            || !Finite(candidate.Evaluation.RmsSpotRadiusMillimeters)
            || !Finite(candidate.Evaluation.MaximumSpotRadiusMillimeters)
            || !double.IsFinite(candidate.Evaluation.ValidRayFraction)
            || candidate.Evaluation.ValidRayFraction is < 0 or > 1
            || candidate.Evaluation.EvaluatedRayCount < 0
            || candidate.Evaluation.ValidRayCount < 0
            || candidate.Evaluation.ValidRayCount > candidate.Evaluation.EvaluatedRayCount)
        {
            throw new InvalidDataException($"Candidate '{candidate.CandidateId}' has invalid evaluation data.");
        }
        if (candidate.Violations is null
            || candidate.Violations.Count > InitialStructureLimits.MaximumViolationsPerCandidate)
        {
            throw new InvalidDataException($"Candidate '{candidate.CandidateId}' has an invalid violation table.");
        }
        foreach (var violation in candidate.Violations)
        {
            if (violation is null
                || !Enum.IsDefined(violation.Severity)
                || !Finite(violation.Actual)
                || !Finite(violation.Limit))
            {
                throw new InvalidDataException($"Candidate '{candidate.CandidateId}' has an invalid violation.");
            }
            RequireText(violation.Code, "violation code");
            RequireText(
                violation.Message,
                "violation message",
                InitialStructureLimits.MaximumMessageLength);
        }
    }

    private static bool Finite(double? value) => value is null || double.IsFinite(value.Value);

    private static void RequireText(
        string value,
        string description,
        int maximumLength = InitialStructureLimits.MaximumNameLength)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > maximumLength)
        {
            throw new InvalidDataException(
                $"The run {description} is missing or exceeds {maximumLength} characters.");
        }
    }

    private sealed class MaximumLengthWriteStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _maximumBytes;
        private long _written;

        public MaximumLengthWriteStream(Stream inner, long maximumBytes)
        {
            _inner = inner;
            _maximumBytes = maximumBytes;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _written;
        public override long Position
        {
            get => _written;
            set => throw new NotSupportedException();
        }

        public override void Flush() => _inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            _inner.FlushAsync(cancellationToken);

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureCapacity(count);
            _inner.Write(buffer, offset, count);
            _written += count;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureCapacity(buffer.Length);
            _inner.Write(buffer);
            _written += buffer.Length;
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            EnsureCapacity(buffer.Length);
            await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            _written += buffer.Length;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
        }

        private void EnsureCapacity(int count)
        {
            if (count < 0 || _written > _maximumBytes - count)
            {
                throw new InvalidDataException(
                    $"Serialized JSON exceeds the {_maximumBytes:N0}-byte limit.");
            }
        }
    }
}
