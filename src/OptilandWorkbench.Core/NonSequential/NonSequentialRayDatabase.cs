using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OptilandWorkbench.Core.Raytrace;

namespace OptilandWorkbench.Core.NonSequential;

public sealed record NonSequentialRayDatabaseObject(
    Guid Id,
    int ObjectNumber,
    string Name,
    NonSequentialObjectKind Kind);

public sealed record NonSequentialRayDatabaseHeader(
    int FormatVersion,
    string SceneHash,
    long SourceRevision,
    DateTimeOffset CreatedUtc,
    string LengthUnit,
    IReadOnlyList<NonSequentialWavelength> Wavelengths,
    IReadOnlyList<NonSequentialRayDatabaseObject> Objects,
    NonSequentialTraceSettings TraceSettings,
    int RandomSeed,
    string? PathFilterExpression = null,
    NonSequentialSplittingMode? SplittingMode = null)
{
    public static NonSequentialRayDatabaseHeader Create(
        NonSequentialDocument document,
        long sourceRevision = 0,
        string? pathFilterExpression = null) => new(
            1,
            NonSequentialSceneHasher.Compute(document),
            sourceRevision,
            DateTimeOffset.UtcNow,
            "mm",
            document.Wavelengths.ToArray(),
            document.Objects.Select((item, index) => new NonSequentialRayDatabaseObject(
                item.Id,
                index + 1,
                item.Name,
                item.Kind)).ToArray(),
            document.TraceSettings,
            document.TraceSettings.RandomSeed,
            pathFilterExpression,
            document.TraceSettings.SplitFresnelRays
                ? NonSequentialSplittingMode.FullFresnel
                : NonSequentialSplittingMode.None);
}

public sealed class NonSequentialRayDatabaseWriter : INonSequentialTraceSink, IDisposable
{
    public const int CurrentVersion = 1;
    private const int FileHeaderLength = 52;
    private const int ChunkHeaderLength = 48;
    private const int BranchesPerChunk = 512;
    private static readonly byte[] Magic = "STARRDB\x1a"u8.ToArray();
    private static readonly byte[] ChunkMagic = "CHNK"u8.ToArray();
    private static readonly byte[] IndexMagic = "INDX"u8.ToArray();
    private static readonly byte[] TrailerMagic = "RDBE"u8.ToArray();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly List<NonSequentialRayBranch> _pending = new(BranchesPerChunk);
    private readonly List<ChunkIndex> _index = new();
    private bool _completed;
    private bool _disposed;

    public NonSequentialRayDatabaseWriter(
        Stream stream,
        NonSequentialRayDatabaseHeader header,
        bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(header);
        if (!stream.CanWrite || !stream.CanSeek)
        {
            throw new ArgumentException("光线数据库输出流必须可写且可定位。", nameof(stream));
        }
        if (header.FormatVersion != CurrentVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(header), "光线数据库文件头版本无效。");
        }

        _stream = stream;
        _leaveOpen = leaveOpen;
        WriteHeader(header);
    }

    public long BranchCount { get; private set; }
    public long SegmentCount { get; private set; }

    public void OnBranch(NonSequentialRayBranch branch)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed) throw new InvalidOperationException("光线数据库已完成，不能继续写入。");
        ArgumentNullException.ThrowIfNull(branch);
        _pending.Add(branch);
        BranchCount++;
        SegmentCount += branch.Segments.Count;
        if (_pending.Count >= BranchesPerChunk) FlushChunk();
    }

    public void Complete()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed) return;
        FlushChunk();
        var indexOffset = _stream.Position;
        _stream.Write(IndexMagic);
        Span<byte> count = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(count, _index.Count);
        _stream.Write(count);
        var indexValue = new byte[24];
        foreach (var entry in _index)
        {
            var value = indexValue.AsSpan();
            BinaryPrimitives.WriteInt64LittleEndian(value[..8], entry.Offset);
            BinaryPrimitives.WriteInt32LittleEndian(value.Slice(8, 4), entry.BranchCount);
            BinaryPrimitives.WriteInt32LittleEndian(value.Slice(12, 4), entry.UncompressedLength);
            BinaryPrimitives.WriteInt32LittleEndian(value.Slice(16, 4), entry.CompressedLength);
            BinaryPrimitives.WriteInt32LittleEndian(value.Slice(20, 4), 0);
            _stream.Write(value);
        }

        Span<byte> trailer = stackalloc byte[20];
        TrailerMagic.CopyTo(trailer);
        BinaryPrimitives.WriteInt64LittleEndian(trailer.Slice(4, 8), indexOffset);
        BinaryPrimitives.WriteInt64LittleEndian(trailer.Slice(12, 8), BranchCount);
        _stream.Write(trailer);
        _stream.Flush();
        _completed = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_leaveOpen) _stream.Dispose();
    }

    private void WriteHeader(NonSequentialRayDatabaseHeader header)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(header, JsonOptions);
        var compressed = Compress(json);
        var fixedHeader = new byte[FileHeaderLength];
        Magic.CopyTo(fixedHeader, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(fixedHeader.AsSpan(8, 2), CurrentVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(fixedHeader.AsSpan(10, 2), 1);
        BinaryPrimitives.WriteInt32LittleEndian(fixedHeader.AsSpan(12, 4), json.Length);
        BinaryPrimitives.WriteInt32LittleEndian(fixedHeader.AsSpan(16, 4), compressed.Length);
        SHA256.HashData(json).CopyTo(fixedHeader, 20);
        _stream.Write(fixedHeader);
        _stream.Write(compressed);
    }

    private void FlushChunk()
    {
        if (_pending.Count == 0) return;
        var json = JsonSerializer.SerializeToUtf8Bytes(_pending, JsonOptions);
        var compressed = Compress(json);
        var offset = _stream.Position;
        var header = new byte[ChunkHeaderLength];
        ChunkMagic.CopyTo(header, 0);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), _pending.Count);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8, 4), json.Length);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12, 4), compressed.Length);
        SHA256.HashData(json).CopyTo(header, 16);
        _stream.Write(header);
        _stream.Write(compressed);
        _index.Add(new ChunkIndex(offset, _pending.Count, json.Length, compressed.Length));
        _pending.Clear();
    }

    private static byte[] Compress(ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            brotli.Write(data);
        }
        return output.ToArray();
    }

    private sealed record ChunkIndex(long Offset, int BranchCount, int UncompressedLength, int CompressedLength);
}

public sealed class NonSequentialRayDatabaseReader : IDisposable
{
    private const int FileHeaderLength = 52;
    private const int ChunkHeaderLength = 48;
    private static readonly byte[] Magic = "STARRDB\x1a"u8.ToArray();
    private static readonly byte[] ChunkMagic = "CHNK"u8.ToArray();
    private static readonly byte[] IndexMagic = "INDX"u8.ToArray();
    private static readonly byte[] TrailerMagic = "RDBE"u8.ToArray();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly IReadOnlyList<ChunkIndex> _index;

    public NonSequentialRayDatabaseReader(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("光线数据库输入流必须可读且可定位。", nameof(stream));
        }
        _stream = stream;
        _leaveOpen = leaveOpen;
        (Header, _index, BranchCount) = ReadStructure();
    }

    public NonSequentialRayDatabaseHeader Header { get; }
    public long BranchCount { get; }

    public bool IsStale(NonSequentialDocument document) =>
        !Header.SceneHash.Equals(NonSequentialSceneHasher.Compute(document), StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<NonSequentialRayBranch> ReadAllBranches(NonSequentialPathFilter? filter = null)
    {
        return ReadBranches(filter).ToArray();
    }

    public IEnumerable<NonSequentialRayBranch> ReadBranches(
        NonSequentialPathFilter? filter = null,
        int maximumCount = int.MaxValue)
    {
        if (maximumCount <= 0) throw new ArgumentOutOfRangeException(nameof(maximumCount));
        filter ??= NonSequentialPathFilter.MatchAll;
        var document = HeaderDocument();
        var count = 0;
        foreach (var entry in _index)
        {
            foreach (var branch in ReadChunk(entry))
            {
                if (!filter.IsMatch(document, branch)) continue;
                yield return branch;
                count++;
                if (count >= maximumCount) yield break;
            }
        }
    }

    public IEnumerable<IReadOnlyList<NonSequentialRayBranch>> ReadChunks()
    {
        foreach (var entry in _index) yield return ReadChunk(entry);
    }

    public void Dispose()
    {
        if (!_leaveOpen) _stream.Dispose();
    }

    private (NonSequentialRayDatabaseHeader Header, IReadOnlyList<ChunkIndex> Index, long BranchCount) ReadStructure()
    {
        if (_stream.Length < FileHeaderLength + 20)
        {
            throw new InvalidDataException("光线数据库文件过短。");
        }
        Span<byte> fixedHeader = stackalloc byte[FileHeaderLength];
        _stream.Position = 0;
        ReadExactly(fixedHeader);
        if (!fixedHeader[..Magic.Length].SequenceEqual(Magic))
        {
            throw new InvalidDataException("所选文件不是 STARRDB 光线数据库。");
        }
        var version = BinaryPrimitives.ReadUInt16LittleEndian(fixedHeader.Slice(8, 2));
        var uncompressedLength = BinaryPrimitives.ReadInt32LittleEndian(fixedHeader.Slice(12, 4));
        var compressedLength = BinaryPrimitives.ReadInt32LittleEndian(fixedHeader.Slice(16, 4));
        if (version != NonSequentialRayDatabaseWriter.CurrentVersion
            || uncompressedLength <= 0 || uncompressedLength > 64 * 1024 * 1024
            || compressedLength <= 0 || compressedLength > _stream.Length - FileHeaderLength)
        {
            throw new InvalidDataException("光线数据库版本或文件头长度无效。");
        }
        var compressed = new byte[compressedLength];
        ReadExactly(compressed);
        var json = Decompress(compressed, uncompressedLength);
        if (!CryptographicOperations.FixedTimeEquals(fixedHeader.Slice(20, SHA256.HashSizeInBytes), SHA256.HashData(json)))
        {
            throw new InvalidDataException("光线数据库文件头校验失败。");
        }
        var header = JsonSerializer.Deserialize<NonSequentialRayDatabaseHeader>(json, JsonOptions)
            ?? throw new InvalidDataException("光线数据库文件头为空。");

        _stream.Position = _stream.Length - 20;
        Span<byte> trailer = stackalloc byte[20];
        ReadExactly(trailer);
        if (!trailer[..4].SequenceEqual(TrailerMagic))
        {
            throw new InvalidDataException("光线数据库未完整完成或文件尾已损坏。");
        }
        var indexOffset = BinaryPrimitives.ReadInt64LittleEndian(trailer.Slice(4, 8));
        var branchCount = BinaryPrimitives.ReadInt64LittleEndian(trailer.Slice(12, 8));
        if (indexOffset < FileHeaderLength + compressedLength || indexOffset > _stream.Length - 28 || branchCount < 0)
        {
            throw new InvalidDataException("光线数据库索引偏移无效。");
        }
        _stream.Position = indexOffset;
        Span<byte> indexHeader = stackalloc byte[8];
        ReadExactly(indexHeader);
        if (!indexHeader[..4].SequenceEqual(IndexMagic)) throw new InvalidDataException("光线数据库索引标记无效。");
        var count = BinaryPrimitives.ReadInt32LittleEndian(indexHeader.Slice(4, 4));
        if (count < 0 || count > 10_000_000 || indexOffset + 8L + count * 24L + 20 != _stream.Length)
        {
            throw new InvalidDataException("光线数据库索引长度无效。");
        }
        var entries = new ChunkIndex[count];
        long indexedBranches = 0;
        var indexValue = new byte[24];
        for (var index = 0; index < entries.Length; index++)
        {
            var value = indexValue.AsSpan();
            ReadExactly(value);
            entries[index] = new ChunkIndex(
                BinaryPrimitives.ReadInt64LittleEndian(value[..8]),
                BinaryPrimitives.ReadInt32LittleEndian(value.Slice(8, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(value.Slice(12, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(value.Slice(16, 4)));
            if (!entries[index].IsValid(indexOffset)) throw new InvalidDataException("光线数据库包含无效分块索引。");
            indexedBranches += entries[index].BranchCount;
        }
        if (indexedBranches != branchCount) throw new InvalidDataException("光线数据库分支总数与索引不一致。");
        return (header, entries, branchCount);
    }

    private IReadOnlyList<NonSequentialRayBranch> ReadChunk(ChunkIndex entry)
    {
        _stream.Position = entry.Offset;
        Span<byte> header = stackalloc byte[ChunkHeaderLength];
        ReadExactly(header);
        if (!header[..4].SequenceEqual(ChunkMagic)
            || BinaryPrimitives.ReadInt32LittleEndian(header.Slice(4, 4)) != entry.BranchCount
            || BinaryPrimitives.ReadInt32LittleEndian(header.Slice(8, 4)) != entry.UncompressedLength
            || BinaryPrimitives.ReadInt32LittleEndian(header.Slice(12, 4)) != entry.CompressedLength)
        {
            throw new InvalidDataException("光线数据库分块文件头与索引不一致。");
        }
        var compressed = new byte[entry.CompressedLength];
        ReadExactly(compressed);
        var json = Decompress(compressed, entry.UncompressedLength);
        if (!CryptographicOperations.FixedTimeEquals(header.Slice(16, SHA256.HashSizeInBytes), SHA256.HashData(json)))
        {
            throw new InvalidDataException("光线数据库分块校验失败。");
        }
        var branches = JsonSerializer.Deserialize<List<NonSequentialRayBranch>>(json, JsonOptions)
            ?? throw new InvalidDataException("光线数据库分块为空。");
        if (branches.Count != entry.BranchCount) throw new InvalidDataException("光线数据库分块数量不一致。");
        return branches;
    }

    private NonSequentialDocument HeaderDocument()
    {
        var primary = Header.Wavelengths.Any(item => item.IsPrimary)
            ? Header.Wavelengths
            : Header.Wavelengths.Select((item, index) => item with { IsPrimary = index == 0 }).ToArray();
        var objects = Header.Objects.Select(item =>
            NonSequentialObjectDefinition.Create(NonSequentialObjectKind.SourceRay, item.Name, item.Id)).ToArray();
        return new NonSequentialDocument("STARRDB", primary, objects);
    }

    private void ReadExactly(Span<byte> buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = _stream.Read(buffer[offset..]);
            if (read == 0) throw new EndOfStreamException("光线数据库意外结束。");
            offset += read;
        }
    }

    private static byte[] Decompress(byte[] compressed, int expectedLength)
    {
        try
        {
            using var source = new MemoryStream(compressed, writable: false);
            using var brotli = new BrotliStream(source, CompressionMode.Decompress);
            using var output = new MemoryStream(expectedLength);
            var buffer = new byte[64 * 1024];
            while (true)
            {
                var read = brotli.Read(buffer);
                if (read == 0) break;
                if (output.Length + read > expectedLength) throw new InvalidDataException("光线数据库分块解压超过声明长度。");
                output.Write(buffer, 0, read);
            }
            if (output.Length != expectedLength) throw new InvalidDataException("光线数据库分块已截断。");
            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            throw new InvalidDataException("光线数据库压缩数据无效。", exception);
        }
    }

    private sealed record ChunkIndex(long Offset, int BranchCount, int UncompressedLength, int CompressedLength)
    {
        public bool IsValid(long indexOffset) => Offset >= FileHeaderLength
            && BranchCount > 0 && BranchCount <= 512
            && UncompressedLength > 0 && UncompressedLength <= 256 * 1024 * 1024
            && CompressedLength > 0 && Offset + ChunkHeaderLength + CompressedLength <= indexOffset;
    }
}

public static class NonSequentialSceneHasher
{
    public static string Compute(NonSequentialDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string value) => hash.AppendData(Encoding.UTF8.GetBytes(value));
        Add(document.Name);
        Add(document.AmbientMaterial);
        Add(JsonSerializer.Serialize(document.TraceSettings));
        foreach (var wavelength in document.Wavelengths) Add(JsonSerializer.Serialize(wavelength));
        foreach (var item in document.Objects)
        {
            Add(item.Id.ToString("N"));
            Add(item.Name);
            Add(item.Kind.ToString());
            Add(item.Enabled.ToString());
            Add(JsonSerializer.Serialize(item.LocalCoordinateSystem));
            Add(item.ReferenceObjectId?.ToString("N") ?? string.Empty);
            Add(item.ContainingObjectId?.ToString("N") ?? string.Empty);
            Add(JsonSerializer.Serialize(item.Parameters, item.Parameters.GetType()));
        }
        foreach (var asset in document.MeshAssets.OrderBy(item => item.Id))
        {
            Add(asset.Id.ToString("N"));
            Add(asset.Sha256);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
