using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core.FileIO;

namespace OpticalSystemDesign.Core.Serialization;

public static class ProjectArchiveStore
{
    public const string Extension = ".starpack";
    public const string Format = "S.T.A.R.Labs.ProjectArchive";
    public const string ProjectPath = "main.staropt";
    public const string RestoredManifest = ".starpack-manifest.json";
    public const int Version = 1;
    private const int ManifestLimit = 8 * 1024 * 1024;
    private const int CentralDirectoryLimit = 32 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new()
    {
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
        WriteIndented = true
    };

    public static async Task<ProjectArchiveManifest> CreateAsync(string path, string name, string applicationVersion,
        long revision, string mode, IReadOnlyList<ProjectArchiveSource> sources,
        IProgress<ProjectArchiveProgress>? progress = null, CancellationToken cancellationToken = default,
        ProjectArchiveLimits? limits = null)
    {
        limits ??= new(); limits.Validate();
        if (sources.Count is < 1 || sources.Count > limits.MaximumFiles) throw new InvalidDataException("Archive file count is invalid.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
            if (!ValidPath(source.RelativePath) || !names.Add(source.RelativePath.Normalize(NormalizationForm.FormC))) throw new InvalidDataException("Archive paths must be unique portable relative paths.");
        if (!sources.Any(source => source.RelativePath == ProjectPath && source.Required && source.Kind == "project"))
            throw new InvalidDataException("Archive must contain the native project.");
        path = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(path)!; Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, ".starpack-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var entries = new List<ProjectArchiveEntry>();
            long total = 0;
            var collectionBytes = sources.Sum(source => source.Snapshot?.Length ?? new FileInfo(source.SourcePath).Length);
            foreach (var source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var input = source.Snapshot ?? new FileStream(source.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                    128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var owned = source.Snapshot is null ? input : null;
                input.Position = 0;
                if (input.Length > limits.MaximumFileBytes || input.Length > limits.MaximumExpandedBytes - total)
                    throw new InvalidDataException("Archive input exceeds the selected size budget.");
                var temporary = Path.Combine(staging, Guid.NewGuid().ToString("N"));
                (long bytes, string hash) copied;
                await using (var output = File.Create(temporary))
                    copied = await CopyAsync(input, output, input.Length, null, progress, "collect", source.RelativePath,
                        total, collectionBytes, cancellationToken).ConfigureAwait(false);
                total = checked(total + copied.bytes);
                var content = Path.Combine(staging, copied.hash);
                if (File.Exists(content)) File.Delete(temporary); else File.Move(temporary, content);
                entries.Add(new(source.RelativePath, source.Kind, copied.bytes, copied.hash, source.Required));
            }
            var manifest = new ProjectArchiveManifest(Format, Version, name, applicationVersion, DateTimeOffset.UtcNow,
                revision, mode, entries);
            Validate(manifest, limits);
            var native = entries.Single(entry => entry.RelativePath == ProjectPath);
            _ = await StarOptProjectStore.LoadAsync(Path.Combine(staging, native.Sha256), cancellationToken).ConfigureAwait(false);
            var metadata = JsonSerializer.SerializeToUtf8Bytes(manifest, Json);
            if (metadata.Length > ManifestLimit) throw new InvalidDataException("Archive manifest is too large.");
            await BoundedFile.WriteStreamingAtomicAsync(path, checked(limits.MaximumExpandedBytes + CentralDirectoryLimit + ManifestLimit),
                "Project archive", async (stream, token) =>
                {
                    using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
                    await using (var header = zip.CreateEntry("manifest.json", CompressionLevel.Optimal).Open())
                        await header.WriteAsync(metadata, token).ConfigureAwait(false);
                    long copiedBytes = 0;
                    var payloads = entries.DistinctBy(entry => entry.Sha256).ToArray();
                    var packedBytes = payloads.Sum(entry => entry.Bytes);
                    foreach (var entry in payloads)
                    {
                        await using var input = File.OpenRead(Path.Combine(staging, entry.Sha256));
                        await using var output = zip.CreateEntry(Blob(entry.Sha256), Compress(entry.RelativePath)).Open();
                        await CopyAsync(input, output, entry.Bytes, entry.Sha256, progress, "pack", entry.RelativePath,
                            copiedBytes, packedBytes, token).ConfigureAwait(false);
                        copiedBytes += entry.Bytes;
                    }
                }, cancellationToken).ConfigureAwait(false);
            return manifest;
        }
        finally { DeleteOwnedDirectory(staging, parent, ".starpack-build-"); }
    }

    public static Task<ProjectArchiveManifest> InspectAsync(string path, CancellationToken cancellationToken = default,
        ProjectArchiveLimits? limits = null) => Task.Run(() =>
        {
            limits ??= new(); limits.Validate(); cancellationToken.ThrowIfCancellationRequested();
            using var input = Open(path, limits);
            return Inspect(input, limits);
        }, cancellationToken);

    public static ProjectArchiveManifest Inspect(Stream input, ProjectArchiveLimits? limits = null)
    {
        limits ??= new(); limits.Validate();
        if (!input.CanSeek || input.Length > limits.MaximumExpandedBytes + CentralDirectoryLimit + ManifestLimit)
            throw new InvalidDataException("Archive exceeds the selected size budget or is not seekable.");
        CheckDirectory(input, limits);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        return ReadManifest(zip, limits);
    }

    public static async Task<ProjectArchiveRestored> RestoreAsync(string path, string directory,
        IProgress<ProjectArchiveProgress>? progress = null, CancellationToken cancellationToken = default,
        ProjectArchiveLimits? limits = null)
    {
        limits ??= new(); limits.Validate(); cancellationToken.ThrowIfCancellationRequested();
        directory = Path.GetFullPath(directory);
        if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("Choose a new archive restoration folder.");
        var parent = Path.GetDirectoryName(directory) ?? throw new ArgumentException("Restoration requires a project folder.", nameof(directory));
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, ".starpack-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            await using var input = Open(path, limits);
            CheckDirectory(input, limits);
            using var zip = new ZipArchive(input, ZipArchiveMode.Read);
            var manifest = ReadManifest(zip, limits);
            var total = manifest.Entries.Sum(entry => entry.Bytes); long completed = 0;
            foreach (var entry in manifest.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = Path.GetFullPath(Path.Combine(staging, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidDataException("Invalid archive destination.");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var source = zip.GetEntry(Blob(entry.Sha256))!.Open();
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous);
                await CopyAsync(source, output, entry.Bytes, entry.Sha256, progress, "restore", entry.RelativePath,
                    completed, total, cancellationToken).ConfigureAwait(false);
                completed += entry.Bytes;
            }
            // Validate the native document before publishing any restored files.
            _ = await StarOptProjectStore.LoadAsync(Path.Combine(staging, ProjectPath), cancellationToken).ConfigureAwait(false);
            await BoundedFile.WriteAllTextAtomicAsync(Path.Combine(staging, RestoredManifest), JsonSerializer.Serialize(manifest, Json),
                ManifestLimit, "Restored archive manifest", cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(staging, directory);
            return new(directory, Path.Combine(directory, ProjectPath), manifest);
        }
        finally { DeleteOwnedDirectory(staging, parent, ".starpack-restore-"); }
    }

    public static ProjectArchiveManifest ReadRestoredManifest(string directory)
    {
        var value = JsonSerializer.Deserialize<ProjectArchiveManifest>(BoundedFile.ReadAllText(Path.Combine(directory, RestoredManifest),
            ManifestLimit, "Restored archive manifest"), Json) ?? throw new InvalidDataException("Empty archive manifest.");
        Validate(value, new()); return value;
    }

    private static FileStream Open(string path, ProjectArchiveLimits limits)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > limits.MaximumExpandedBytes + CentralDirectoryLimit + ManifestLimit)
        { stream.Dispose(); throw new InvalidDataException("Archive exceeds the selected size budget."); }
        return stream;
    }

    private static ProjectArchiveManifest ReadManifest(ZipArchive zip, ProjectArchiveLimits limits)
    {
        var physical = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in zip.Entries)
            if (!physical.Add(item.FullName)) throw new InvalidDataException("Duplicate archive member.");
        var header = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("Project archive manifest is missing.");
        if (header.Length > ManifestLimit) throw new InvalidDataException("Archive manifest is too large.");
        using var input = header.Open();
        var bytes = BoundedFile.ReadToEnd(input, ManifestLimit, "Archive manifest");
        ProjectArchiveManifest manifest;
        try { manifest = JsonSerializer.Deserialize<ProjectArchiveManifest>(bytes, Json) ?? throw new InvalidDataException("Empty archive manifest."); }
        catch (JsonException error) { throw new InvalidDataException("Invalid archive manifest.", error); }
        Validate(manifest, limits);
        var expected = manifest.Entries.Select(entry => Blob(entry.Sha256)).Append("manifest.json").ToHashSet(StringComparer.Ordinal);
        if (!physical.SetEquals(expected)) throw new InvalidDataException("Archive members do not match the manifest.");
        foreach (var entry in manifest.Entries)
            if (zip.GetEntry(Blob(entry.Sha256))!.Length != entry.Bytes) throw new InvalidDataException("Archive member length differs from the manifest.");
        return manifest;
    }

    private static void Validate(ProjectArchiveManifest manifest, ProjectArchiveLimits limits)
    {
        if (manifest.Format != Format || manifest.Version != Version || string.IsNullOrWhiteSpace(manifest.Name)
            || manifest.Name.Length > 1024 || string.IsNullOrWhiteSpace(manifest.ApplicationVersion)
            || manifest.SourceRevision < 0 || manifest.Mode is not ("Sequential" or "NonSequential")
            || manifest.Entries is null || manifest.Entries.Count is < 1 || manifest.Entries.Count > limits.MaximumFiles)
            throw new InvalidDataException("Unsupported or invalid project archive manifest.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var entry in manifest.Entries)
        {
            if (entry is null || !ValidPath(entry.RelativePath) || entry.RelativePath.Equals(RestoredManifest, StringComparison.OrdinalIgnoreCase)
                || !names.Add(entry.RelativePath.Normalize(NormalizationForm.FormC))
                || string.IsNullOrWhiteSpace(entry.Kind) || entry.Kind.Length > 64 || entry.Bytes < 0 || entry.Bytes > limits.MaximumFileBytes
                || entry.Sha256 is null || entry.Sha256.Length != 64 || entry.Sha256.Any(c => !char.IsAsciiHexDigitLower(c))
                || entry.Bytes > limits.MaximumExpandedBytes - total) throw new InvalidDataException("Invalid archive resource.");
            total += entry.Bytes;
        }
        if (!manifest.Entries.Any(entry => entry.RelativePath == ProjectPath && entry.Required && entry.Kind == "project"))
            throw new InvalidDataException("Archive native project is missing.");
    }

    private static bool ValidPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1024 || path.Contains('\\')) return false;
        foreach (var part in path.Split('/'))
        {
            if (part.Length is < 1 or > 200 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
                || part.Any(c => c < 32 || "<>:\"|?*".Contains(c))) return false;
            var stem = part.Split('.')[0];
            if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
                || Enumerable.Range(1, 9).Any(i => stem.Equals("COM" + i, StringComparison.OrdinalIgnoreCase) || stem.Equals("LPT" + i, StringComparison.OrdinalIgnoreCase))) return false;
        }
        return true;
    }

    private static async Task<(long Bytes, string Hash)> CopyAsync(Stream source, Stream output, long expected, string? expectedHash,
        IProgress<ProjectArchiveProgress>? progress, string phase, string file, long completed, long total, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024]; long copied = 0; var last = Environment.TickCount64 - 100;
        while (true)
        {
            var count = await source.ReadAsync(buffer, token).ConfigureAwait(false); if (count == 0) break;
            if (count > expected - copied) throw new InvalidDataException("Archive resource length changed.");
            hash.AppendData(buffer, 0, count);
            await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false); copied += count;
            if (Environment.TickCount64 - last >= 100)
            { progress?.Report(new(phase, file, completed + copied, total)); last = Environment.TickCount64; }
        }
        var digest = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (copied != expected || expectedHash is not null && digest != expectedHash) throw new InvalidDataException("Archive resource checksum or length is invalid.");
        progress?.Report(new(phase, file, completed + copied, total));
        return (copied, digest);
    }

    private static string Blob(string hash) => "data/" + hash;
    private static CompressionLevel Compress(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".json" or ".csv" or ".txt" or ".tsv" ? CompressionLevel.Fastest : CompressionLevel.NoCompression;

    private static void DeleteOwnedDirectory(string directory, string parent, string prefix)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(directory)) != Path.GetFullPath(parent)
            || !Path.GetFileName(directory).StartsWith(prefix, StringComparison.Ordinal)) throw new IOException("Invalid temporary archive directory.");
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    private static void CheckDirectory(Stream stream, ProjectArchiveLimits limits)
    {
        var tail = new byte[(int)Math.Min(stream.Length, 65557)]; stream.Position = stream.Length - tail.Length; stream.ReadExactly(tail);
        var position = -1;
        for (var i = tail.Length - 22; i >= 0; i--)
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i)) == 0x06054b50
                && i + 22 + BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 20)) == tail.Length) { position = i; break; }
        if (position < 0) throw new InvalidDataException("Archive directory is missing.");
        var end = stream.Length - tail.Length + position;
        var record = tail.AsSpan(position);
        if (BinaryPrimitives.ReadUInt32LittleEndian(record[4..]) != 0) throw new InvalidDataException("Split archives are not supported.");
        long count = BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
        long bytes = BinaryPrimitives.ReadUInt32LittleEndian(record[12..]);
        long offset = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);
        if (count == ushort.MaxValue || bytes == uint.MaxValue || offset == uint.MaxValue)
        {
            if (end < 20) throw new InvalidDataException("ZIP64 locator is missing.");
            Span<byte> locator = stackalloc byte[20]; stream.Position = end - 20; stream.ReadExactly(locator);
            if (BinaryPrimitives.ReadUInt32LittleEndian(locator) != 0x07064b50 || BinaryPrimitives.ReadUInt32LittleEndian(locator[4..]) != 0
                || BinaryPrimitives.ReadUInt32LittleEndian(locator[16..]) != 1) throw new InvalidDataException("Invalid ZIP64 locator.");
            var location = BinaryPrimitives.ReadInt64LittleEndian(locator[8..]);
            if (location < 0 || location > end - 76) throw new InvalidDataException("Invalid ZIP64 directory location.");
            Span<byte> large = stackalloc byte[56]; stream.Position = location; stream.ReadExactly(large);
            if (BinaryPrimitives.ReadUInt32LittleEndian(large) != 0x06064b50 || BinaryPrimitives.ReadUInt64LittleEndian(large[16..]) != 0)
                throw new InvalidDataException("Invalid ZIP64 directory.");
            count = BinaryPrimitives.ReadInt64LittleEndian(large[32..]); bytes = BinaryPrimitives.ReadInt64LittleEndian(large[40..]);
            offset = BinaryPrimitives.ReadInt64LittleEndian(large[48..]);
        }
        if (count < 1 || count > limits.MaximumFiles + 1 || bytes < 0 || bytes > CentralDirectoryLimit
            || offset < 0 || offset > end || bytes > end - offset) throw new InvalidDataException("Archive directory exceeds the selected limits.");
        stream.Position = 0;
    }
}
