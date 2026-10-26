using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Serialization;

namespace OpticalSystemDesign.Core.NonSequential;

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
        string? pathFilterExpression = null,
        MaterialRegistry? materials = null) => new(
            1,
            NonSequentialSceneHasher.Compute(document, materials),
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
    public const int MaximumHeaderUncompressedBytes = 64 * 1024 * 1024;
    public const int MaximumChunkUncompressedBytes = 256 * 1024 * 1024;
    public static readonly int MaximumHeaderCompressedBytes = BrotliEncoder.GetMaxCompressedLength(MaximumHeaderUncompressedBytes);
    public static readonly int MaximumChunkCompressedBytes = BrotliEncoder.GetMaxCompressedLength(MaximumChunkUncompressedBytes);
    public const int MaximumIndexEntryCount = 1_000_000;
    private const int FileHeaderLength = 52;
    private const int ChunkHeaderLength = 48;
    private const int BranchesPerChunk = 512;
    private const int MaximumSegmentsPerChunk = NonSequentialDocument.MaximumSegmentsPerRay;
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
    private int _pendingSegmentCount;
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
        if (branch.Segments.Count > NonSequentialDocument.MaximumSegmentsPerRay)
        {
            throw new InvalidDataException(
                $"单个光线分支不能包含超过 {NonSequentialDocument.MaximumSegmentsPerRay:N0} 个分段。");
        }
        if (_pending.Count > 0
            && (_pending.Count >= BranchesPerChunk
                || checked(_pendingSegmentCount + branch.Segments.Count) > MaximumSegmentsPerChunk))
        {
            FlushChunk();
        }
        _pending.Add(branch);
        _pendingSegmentCount = checked(_pendingSegmentCount + branch.Segments.Count);
        BranchCount++;
        SegmentCount += branch.Segments.Count;
        if (_pending.Count >= BranchesPerChunk || _pendingSegmentCount >= MaximumSegmentsPerChunk)
        {
            FlushChunk();
        }
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
        if (json.Length > MaximumHeaderUncompressedBytes)
        {
            throw new InvalidDataException("光线数据库文件头超过读取器支持的大小。");
        }
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
        if (_index.Count >= MaximumIndexEntryCount)
        {
            throw new InvalidDataException("光线数据库分块索引数量超过读取器支持的上限。");
        }
        var json = JsonSerializer.SerializeToUtf8Bytes(_pending, JsonOptions);
        if (json.Length > MaximumChunkUncompressedBytes)
        {
            throw new InvalidDataException("光线数据库分块超过读取器支持的大小。");
        }
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
        _pendingSegmentCount = 0;
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

    private readonly record struct ChunkIndex(long Offset, int BranchCount, int UncompressedLength, int CompressedLength);
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
    private readonly object _streamGate = new();
    private readonly HashSet<Guid> _objectIds;

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
        _objectIds = Header.Objects.Select(item => item.Id).ToHashSet();
    }

    public NonSequentialRayDatabaseHeader Header { get; }
    public long BranchCount { get; }

    public bool IsStale(NonSequentialDocument document, MaterialRegistry? materials = null) =>
        !Header.SceneHash.Equals(NonSequentialSceneHasher.Compute(document, materials), StringComparison.OrdinalIgnoreCase);

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
        var document = CreateHeaderDocument();
        var matches = filter.Bind(document);
        var count = 0;
        foreach (var entry in _index)
        {
            foreach (var branch in ReadChunk(entry))
            {
                if (!matches(branch)) continue;
                yield return branch;
                count++;
                if (count >= maximumCount) yield break;
            }
        }
    }

    public IEnumerable<(long Index, NonSequentialRayBranch Branch)> ReadBranchesWithIndices(
        NonSequentialPathFilter? filter = null)
    {
        filter ??= NonSequentialPathFilter.MatchAll;
        var document = CreateHeaderDocument();
        var matches = filter.Bind(document);
        long index = 0;
        foreach (var entry in _index)
        {
            foreach (var branch in ReadChunk(entry))
            {
                if (matches(branch)) yield return (index, branch);
                index++;
            }
        }
    }

    public IEnumerable<IReadOnlyList<NonSequentialRayBranch>> ReadChunks()
    {
        foreach (var entry in _index) yield return ReadChunk(entry);
    }

    public IReadOnlyList<NonSequentialRayBranch> ReadRange(long offset, int count)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (offset >= BranchCount) return Array.Empty<NonSequentialRayBranch>();

        var result = new List<NonSequentialRayBranch>(Math.Min(count, 512));
        long chunkStart = 0;
        foreach (var entry in _index)
        {
            var chunkEnd = chunkStart + entry.BranchCount;
            if (chunkEnd <= offset)
            {
                chunkStart = chunkEnd;
                continue;
            }

            var branches = ReadChunk(entry);
            var localStart = (int)Math.Max(0, offset - chunkStart);
            for (var index = localStart; index < branches.Count && result.Count < count; index++)
            {
                result.Add(branches[index]);
            }
            if (result.Count >= count) break;
            chunkStart = chunkEnd;
        }
        return result;
    }

    public IReadOnlyList<NonSequentialRayBranch> ReadIndices(IReadOnlyList<long> indices)
    {
        ArgumentNullException.ThrowIfNull(indices);
        if (indices.Count == 0) return Array.Empty<NonSequentialRayBranch>();
        if (indices.Any(index => index < 0 || index >= BranchCount))
        {
            throw new ArgumentOutOfRangeException(nameof(indices));
        }
        for (var index = 1; index < indices.Count; index++)
        {
            if (indices[index] <= indices[index - 1])
            {
                throw new ArgumentException("光线数据库索引必须严格递增。", nameof(indices));
            }
        }

        var result = new List<NonSequentialRayBranch>(indices.Count);
        var target = 0;
        long chunkStart = 0;
        foreach (var entry in _index)
        {
            var chunkEnd = chunkStart + entry.BranchCount;
            if (indices[target] >= chunkEnd)
            {
                chunkStart = chunkEnd;
                continue;
            }

            var branches = ReadChunk(entry);
            while (target < indices.Count && indices[target] < chunkEnd)
            {
                result.Add(branches[checked((int)(indices[target] - chunkStart))]);
                target++;
            }
            if (target >= indices.Count) break;
            chunkStart = chunkEnd;
        }
        return result;
    }

    public void Dispose()
    {
        lock (_streamGate) { if (!_leaveOpen) _stream.Dispose(); }
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
            || uncompressedLength <= 0
            || uncompressedLength > NonSequentialRayDatabaseWriter.MaximumHeaderUncompressedBytes
            || compressedLength <= 0
            || compressedLength > NonSequentialRayDatabaseWriter.MaximumHeaderCompressedBytes
            || compressedLength > _stream.Length - FileHeaderLength)
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
        ValidateHeader(header);
        header = header with
        {
            Wavelengths = Array.AsReadOnly(header.Wavelengths.ToArray()),
            Objects = Array.AsReadOnly(header.Objects.ToArray())
        };

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
        if (count < 0
            || count > NonSequentialRayDatabaseWriter.MaximumIndexEntryCount
            || indexOffset + 8L + count * 24L + 20 != _stream.Length)
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
            var minimumOffset = FileHeaderLength + compressedLength;
            if (!entries[index].IsValid(minimumOffset, indexOffset)
                || index > 0 && entries[index].Offset
                    < entries[index - 1].Offset + ChunkHeaderLength + entries[index - 1].CompressedLength)
            {
                throw new InvalidDataException("光线数据库包含无效、无序或重叠的分块索引。");
            }
            indexedBranches += entries[index].BranchCount;
        }
        if (indexedBranches != branchCount) throw new InvalidDataException("光线数据库分支总数与索引不一致。");
        return (header, entries, branchCount);
    }

    private IReadOnlyList<NonSequentialRayBranch> ReadChunk(ChunkIndex entry)
    {
        lock (_streamGate) return ReadChunkLocked(entry);
    }

    private IReadOnlyList<NonSequentialRayBranch> ReadChunkLocked(ChunkIndex entry)
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
        foreach (var branch in branches) ValidateBranch(branch, _objectIds);
        return branches;
    }

    private static void ValidateHeader(NonSequentialRayDatabaseHeader header)
    {
        if (header.FormatVersion != NonSequentialRayDatabaseWriter.CurrentVersion
            || header.SceneHash is not { Length: 64 } || !header.SceneHash.All(Uri.IsHexDigit)
            || header.SourceRevision < 0 || header.LengthUnit != "mm" || header.TraceSettings is null
            || header.Wavelengths is null || header.Wavelengths.Any(w => w is null)
            || header.Objects is null || header.Objects.Count > NonSequentialDocument.MaximumObjectCount
            || header.Objects.Any(o => o is null || o.Id == Guid.Empty || o.Name is null || !Enum.IsDefined(o.Kind))
            || header.Objects.Select(o => o.Id).Distinct().Count() != header.Objects.Count
            || !header.Objects.Select(o => o.ObjectNumber).SequenceEqual(Enumerable.Range(1, header.Objects.Count))
            || header.SplittingMode is { } mode && !Enum.IsDefined(mode))
            throw new InvalidDataException("光线数据库文件头的结构或场景标识无效。");
        try
        {
            _ = new NonSequentialDocument("header validation", header.Wavelengths, traceSettings: header.TraceSettings);
            _ = NonSequentialPathFilter.Parse(header.PathFilterExpression);
        }
        catch (Exception error) when (error is ArgumentException or FormatException or InvalidOperationException)
        { throw new InvalidDataException("光线数据库文件头参数无效。", error); }
    }

    private static void ValidateBranch(NonSequentialRayBranch? branch, HashSet<Guid> objects)
    {
        static bool Nonnegative(double value) => double.IsFinite(value) && value >= 0;
        static bool Vector(Backend.Vector3D? value) => value is null
            || double.IsFinite(value.Value.X) && double.IsFinite(value.Value.Y) && double.IsFinite(value.Value.Z);
        if (branch is null || branch.Id <= 0 || branch.Level < 0
            || branch.ParentId is { } parent && (parent <= 0 || parent >= branch.Id)
            || branch.SourceObjectId is { } source && !objects.Contains(source)
            || !Enum.IsDefined(branch.TerminationReason) || !Nonnegative(branch.FinalIntensity)
            || !Nonnegative(branch.WavelengthNanometers) || !Vector(branch.FinalOrigin) || !Vector(branch.FinalDirection)
            || branch.Segments is null || branch.Segments.Count > NonSequentialDocument.MaximumSegmentsPerRay)
            throw new InvalidDataException("光线数据库分支结构无效。");
        foreach (var segment in branch.Segments)
            if (segment is null || segment.BranchId <= 0 || segment.BranchId > branch.Id
                || segment.ObjectId is { } objectId && !objects.Contains(objectId) || segment.FaceNumber < 0
                || !Vector(segment.Start) || !Vector(segment.End) || !Vector(segment.OutgoingDirection) || !Vector(segment.SurfaceNormal)
                || !Nonnegative(segment.Intensity) || !Nonnegative(segment.SegmentLength)
                || !Nonnegative(segment.CumulativePathLength) || !Nonnegative(segment.CumulativeOpticalPathLength)
                || !double.IsFinite(segment.WavelengthNanometers) || segment.WavelengthNanometers <= 0
                || segment.InteractionKind is { } kind && !Enum.IsDefined(kind))
                throw new InvalidDataException("光线数据库分段对象或数值无效。");
    }

    public NonSequentialDocument CreateHeaderDocument()
    {
        var primary = Header.Wavelengths.Any(item => item.IsPrimary)
            ? Header.Wavelengths
            : Header.Wavelengths.Select((item, index) => item with { IsPrimary = index == 0 }).ToArray();
        var objects = Header.Objects.Select(item =>
            NonSequentialObjectDefinition.Create(
                item.Kind == NonSequentialObjectKind.DetectorRectangle
                    ? NonSequentialObjectKind.DetectorRectangle
                    : NonSequentialObjectKind.SourceRay,
                item.Name,
                item.Id)).ToArray();
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
            var output = new byte[expectedLength];
            var offset = 0;
            while (offset < output.Length)
            {
                var read = brotli.Read(output, offset, output.Length - offset);
                if (read == 0) throw new InvalidDataException("光线数据库分块已截断。");
                offset += read;
            }
            if (brotli.ReadByte() >= 0) throw new InvalidDataException("光线数据库分块解压超过声明长度。");
            return output;
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

    private readonly record struct ChunkIndex(long Offset, int BranchCount, int UncompressedLength, int CompressedLength)
    {
        public bool IsValid(long minimumOffset, long indexOffset) => Offset >= minimumOffset
            && BranchCount > 0 && BranchCount <= 512
            && UncompressedLength > 0
            && UncompressedLength <= NonSequentialRayDatabaseWriter.MaximumChunkUncompressedBytes
            && CompressedLength > 0
            && CompressedLength <= NonSequentialRayDatabaseWriter.MaximumChunkCompressedBytes
            && Offset <= indexOffset - ChunkHeaderLength - CompressedLength;
    }
}

public static class NonSequentialSceneHasher
{
    private static readonly Lazy<MaterialRegistry> DefaultMaterials = new(() => new MaterialRegistry());

    public static string Compute(NonSequentialDocument document, MaterialRegistry? materials = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }
        Add("non-sequential-scene/v3");
        Add(document.Name);
        Add(document.AmbientMaterial);
        materials ??= DefaultMaterials.Value;
        var materialNames = document.Objects.SelectMany(item => item.Parameters switch
        {
            PlaneRectangleParameters p => new[] { p.MaterialBefore, p.MaterialAfter },
            SphereParameters p => new[] { p.Material },
            CylinderParameters p => new[] { p.Material },
            BoxParameters p => new[] { p.Material },
            StandardLensParameters p => new[] { p.Material },
            MeshObjectParameters p => new[] { p.Material },
            _ => Array.Empty<string>()
        }).Append(document.AmbientMaterial).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase);
        foreach (var name in materialNames)
        {
            Add(name);
            if (!materials.TryResolve(name, null, out var material)) { Add("unresolved"); continue; }
            var model = ComponentSnapshotFactory.FromMaterial(material);
            if (model.Kind == "catalog")
                throw new NotSupportedException($"Material '{name}' has no serializable optical model identity.");
            Add(JsonSerializer.Serialize(model));
        }
        Add(JsonSerializer.Serialize(document.TraceSettings));
        Add(JsonSerializer.Serialize(document.Wavelengths.Count));
        foreach (var wavelength in document.Wavelengths) Add(JsonSerializer.Serialize(wavelength));
        Add(JsonSerializer.Serialize(document.Objects.Count));
        foreach (var item in document.Objects)
        {
            Add(item.Id.ToString("N"));
            Add(item.Name);
            Add(item.Kind.ToString());
            Add(item.Enabled.ToString());
            Add(item.Visible.ToString());
            Add(JsonSerializer.Serialize(item.LocalCoordinateSystem));
            Add(item.ReferenceObjectId?.ToString("N") ?? string.Empty);
            Add(item.ContainingObjectId?.ToString("N") ?? string.Empty);
            Add(JsonSerializer.Serialize(item.Parameters, item.Parameters.GetType()));
        }
        Add(JsonSerializer.Serialize(document.MeshAssets.Count));
        foreach (var asset in document.MeshAssets.OrderBy(item => item.Id))
        {
            Add(asset.Id.ToString("N"));
            // Metadata (including unit scale and closed-volume identity) changes
            // scene interpretation even when the mesh byte hash is unchanged.
            Add(JsonSerializer.Serialize(asset));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
