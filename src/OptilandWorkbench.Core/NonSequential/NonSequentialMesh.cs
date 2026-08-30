using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Rays;

namespace OptilandWorkbench.Core.NonSequential;

public enum NonSequentialMeshUnit
{
    Millimeter,
    Centimeter,
    Meter,
    Inch
}

public readonly record struct NonSequentialMeshTriangle(int A, int B, int C, int FaceNumber = 1);

public sealed record NonSequentialMeshHit(
    double Distance,
    Vector3D Point,
    Vector3D Normal,
    int FaceNumber,
    bool Entering);

public sealed record NonSequentialMeshAsset(
    Guid Id,
    string OriginalFileName,
    string SourceFormat,
    string Sha256,
    double UnitScaleToMillimeters,
    int VertexCount,
    int TriangleCount,
    Vector3D BoundsMinimum,
    Vector3D BoundsMaximum,
    bool IsClosed,
    bool IsManifold,
    bool IsConnected,
    bool IsOrientable,
    bool HasSelfIntersections,
    double SignedVolumeCubicMillimeters,
    IReadOnlyList<string>? Warnings = null,
    [property: JsonIgnore] byte[]? CanonicalData = null)
{
    private NonSequentialMeshGeometry? _geometry;

    [JsonIgnore]
    public bool HasGeometry => CanonicalData is { Length: > 0 };

    public NonSequentialMeshGeometry GetGeometry()
    {
        if (_geometry is not null)
        {
            return _geometry;
        }

        if (CanonicalData is not { Length: > 0 } data)
        {
            throw new InvalidDataException($"网格资产“{OriginalFileName}”缺少内嵌几何数据。");
        }

        var decoded = NonSequentialMeshCodec.Decode(data);
        if (decoded.Vertices.Count != VertexCount || decoded.Triangles.Count != TriangleCount)
        {
            throw new InvalidDataException($"网格资产“{OriginalFileName}”的元数据与几何数据不一致。");
        }

        Interlocked.CompareExchange(ref _geometry, decoded, null);
        return _geometry!;
    }

    public NonSequentialMeshAsset AttachCanonicalData(byte[] canonicalData)
    {
        ArgumentNullException.ThrowIfNull(canonicalData);
        var hash = Convert.ToHexString(SHA256.HashData(canonicalData));
        if (!hash.Equals(Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"网格资产“{OriginalFileName}”的 SHA-256 校验失败。");
        }

        return this with { CanonicalData = canonicalData };
    }
}

public sealed class NonSequentialMeshGeometry
{
    private readonly Lazy<NonSequentialTriangleBvh> _bvh;

    public NonSequentialMeshGeometry(
        IReadOnlyList<Vector3D> vertices,
        IReadOnlyList<NonSequentialMeshTriangle> triangles)
    {
        Vertices = vertices?.ToArray() ?? throw new ArgumentNullException(nameof(vertices));
        Triangles = triangles?.ToArray() ?? throw new ArgumentNullException(nameof(triangles));
        _bvh = new Lazy<NonSequentialTriangleBvh>(
            () => new NonSequentialTriangleBvh(Vertices, Triangles),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public IReadOnlyList<Vector3D> Vertices { get; }
    public IReadOnlyList<NonSequentialMeshTriangle> Triangles { get; }

    public NonSequentialMeshHit? Intersect(Vector3D origin, Vector3D direction, bool twoSided)
    {
        return _bvh.Value.Intersect(origin, direction, twoSided);
    }
}

public static class NonSequentialMeshCodec
{
    private const ushort Version = 1;
    private const int HeaderLength = 20;
    private static readonly byte[] Magic = "STARMESH"u8.ToArray();

    public static byte[] Encode(
        IReadOnlyList<Vector3D> vertices,
        IReadOnlyList<NonSequentialMeshTriangle> triangles)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(triangles);
        if (vertices.Count is < 1 or > NonSequentialStlImporter.MaximumVertexCount
            || triangles.Count is < 1 or > NonSequentialStlImporter.MaximumTriangleCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(vertices),
                "STARMESH vertex or triangle count exceeds the supported range.");
        }

        var length = HeaderLength + ((long)vertices.Count * 24) + ((long)triangles.Count * 16);
        if (length > NonSequentialStlImporter.MaximumInputBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(vertices),
                $"STARMESH data cannot exceed {NonSequentialStlImporter.MaximumInputBytes:N0} bytes.");
        }

        for (var index = 0; index < vertices.Count; index++)
        {
            if (!Finite(vertices[index]))
            {
                throw new ArgumentException($"STARMESH vertex {index + 1} is not finite.", nameof(vertices));
            }
        }
        for (var index = 0; index < triangles.Count; index++)
        {
            var triangle = triangles[index];
            if (triangle.A < 0 || triangle.A >= vertices.Count
                || triangle.B < 0 || triangle.B >= vertices.Count
                || triangle.C < 0 || triangle.C >= vertices.Count
                || triangle.A == triangle.B || triangle.B == triangle.C || triangle.A == triangle.C
                || triangle.FaceNumber <= 0)
            {
                throw new ArgumentException(
                    $"STARMESH triangle {index + 1} has invalid indices or face number.",
                    nameof(triangles));
            }
        }

        var data = new byte[checked((int)length)];
        Magic.CopyTo(data, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(8, 2), Version);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(10, 2), 0);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12, 4), vertices.Count);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16, 4), triangles.Count);
        var offset = HeaderLength;
        foreach (var vertex in vertices)
        {
            BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(offset, 8), BitConverter.DoubleToInt64Bits(vertex.X));
            BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(offset + 8, 8), BitConverter.DoubleToInt64Bits(vertex.Y));
            BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(offset + 16, 8), BitConverter.DoubleToInt64Bits(vertex.Z));
            offset += 24;
        }

        foreach (var triangle in triangles)
        {
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, 4), triangle.A);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset + 4, 4), triangle.B);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset + 8, 4), triangle.C);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset + 12, 4), triangle.FaceNumber);
            offset += 16;
        }

        return data;
    }

    public static NonSequentialMeshGeometry Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderLength
            || data.Length > NonSequentialStlImporter.MaximumInputBytes
            || !data[..Magic.Length].SequenceEqual(Magic))
        {
            throw new InvalidDataException("内嵌网格数据缺少有效的 STARMESH 文件头。");
        }

        var version = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8, 2));
        var flags = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(10, 2));
        var vertexCount = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(12, 4));
        var triangleCount = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(16, 4));
        if (version != Version || flags != 0
            || vertexCount <= 0 || vertexCount > NonSequentialStlImporter.MaximumVertexCount
            || triangleCount <= 0
            || triangleCount > NonSequentialStlImporter.MaximumTriangleCount)
        {
            throw new InvalidDataException("内嵌网格数据的版本或集合大小无效。");
        }

        var expectedLength = HeaderLength + ((long)vertexCount * 24) + ((long)triangleCount * 16);
        if (data.Length != expectedLength)
        {
            throw new InvalidDataException("内嵌网格数据已截断或包含多余字节。");
        }

        var vertices = new Vector3D[vertexCount];
        var offset = HeaderLength;
        for (var index = 0; index < vertices.Length; index++)
        {
            vertices[index] = new Vector3D(
                BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(data.Slice(offset, 8))),
                BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(data.Slice(offset + 8, 8))),
                BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(data.Slice(offset + 16, 8))));
            if (!Finite(vertices[index]))
            {
                throw new InvalidDataException($"内嵌网格顶点 {index + 1} 包含非有限坐标。");
            }
            offset += 24;
        }

        var triangles = new NonSequentialMeshTriangle[triangleCount];
        for (var index = 0; index < triangles.Length; index++)
        {
            triangles[index] = new NonSequentialMeshTriangle(
                BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset + 4, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset + 8, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset + 12, 4)));
            if (triangles[index].A < 0 || triangles[index].A >= vertexCount
                || triangles[index].B < 0 || triangles[index].B >= vertexCount
                || triangles[index].C < 0 || triangles[index].C >= vertexCount
                || triangles[index].A == triangles[index].B
                || triangles[index].B == triangles[index].C
                || triangles[index].A == triangles[index].C
                || triangles[index].FaceNumber <= 0)
            {
                throw new InvalidDataException($"内嵌网格三角形 {index + 1} 的索引或面编号无效。");
            }
            offset += 16;
        }

        return new NonSequentialMeshGeometry(vertices, triangles);
    }

    private static bool Finite(Vector3D value) =>
        double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);
}

public static class NonSequentialStlImporter
{
    public const int MaximumTriangleCount = 2_000_000;
    public const int MaximumVertexCount = MaximumTriangleCount * 3;
    public const long MaximumInputBytes = 256L * 1024 * 1024;

    public static NonSequentialMeshAsset Import(
        string path,
        NonSequentialMeshUnit unit = NonSequentialMeshUnit.Millimeter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Import(
            BoundedFile.ReadAllBytes(path, MaximumInputBytes, "STL mesh"),
            Path.GetFileName(path),
            unit);
    }

    public static NonSequentialMeshAsset Import(
        ReadOnlySpan<byte> bytes,
        string originalFileName,
        NonSequentialMeshUnit unit = NonSequentialMeshUnit.Millimeter)
    {
        if (bytes.Length == 0)
        {
            throw new InvalidDataException("STL 文件为空。");
        }
        if (bytes.Length > MaximumInputBytes)
        {
            throw new InvalidDataException("STL 文件超过 256 MiB 输入上限。");
        }

        var scale = unit switch
        {
            NonSequentialMeshUnit.Millimeter => 1.0,
            NonSequentialMeshUnit.Centimeter => 10.0,
            NonSequentialMeshUnit.Meter => 1000.0,
            NonSequentialMeshUnit.Inch => 25.4,
            _ => throw new ArgumentOutOfRangeException(nameof(unit))
        };
        var binary = LooksLikeBinary(bytes);
        var raw = binary ? ParseBinary(bytes, scale) : ParseAscii(bytes, scale);
        if (raw.Count == 0)
        {
            throw new InvalidDataException("STL 文件没有可用三角形。");
        }

        var normalized = Normalize(raw);
        var data = NonSequentialMeshCodec.Encode(normalized.Vertices, normalized.Triangles);
        var hash = Convert.ToHexString(SHA256.HashData(data));
        return new NonSequentialMeshAsset(
            Guid.NewGuid(),
            string.IsNullOrWhiteSpace(originalFileName) ? "Imported.stl" : originalFileName.Trim(),
            binary ? "Binary STL" : "ASCII STL",
            hash,
            scale,
            normalized.Vertices.Count,
            normalized.Triangles.Count,
            normalized.Minimum,
            normalized.Maximum,
            normalized.IsClosed,
            normalized.IsManifold,
            normalized.IsConnected,
            normalized.IsOrientable,
            normalized.HasSelfIntersections,
            normalized.Volume,
            normalized.Warnings,
            data);
    }

    private static bool LooksLikeBinary(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 84)
        {
            return false;
        }

        var count = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(80, 4));
        return count <= MaximumTriangleCount && 84L + count * 50L == bytes.Length;
    }

    private static List<RawTriangle> ParseBinary(ReadOnlySpan<byte> bytes, double scale)
    {
        var count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(80, 4)));
        if (count <= 0 || count > MaximumTriangleCount || 84L + count * 50L != bytes.Length)
        {
            throw new InvalidDataException("Binary STL 的三角形数量或文件长度无效。");
        }

        var result = new List<RawTriangle>(count);
        var offset = 84;
        for (var index = 0; index < count; index++)
        {
            offset += 12;
            var a = ReadVector(bytes, offset, scale);
            var b = ReadVector(bytes, offset + 12, scale);
            var c = ReadVector(bytes, offset + 24, scale);
            result.Add(new RawTriangle(a, b, c));
            offset += 38;
        }
        return result;
    }

    private static Vector3D ReadVector(ReadOnlySpan<byte> bytes, int offset, double scale)
    {
        var value = new Vector3D(
            BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(offset, 4)) * scale,
            BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(offset + 4, 4)) * scale,
            BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(offset + 8, 4)) * scale);
        if (!Finite(value))
        {
            throw new InvalidDataException("STL 包含非有限顶点坐标。");
        }
        return value;
    }

    private static List<RawTriangle> ParseAscii(ReadOnlySpan<byte> bytes, double scale)
    {
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("STL 既不是有效 Binary STL，也不是 UTF-8 ASCII STL。", exception);
        }

        var vertices = new List<Vector3D>();
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("vertex", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                || !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
            {
                throw new InvalidDataException($"ASCII STL 顶点行无效：“{trimmed}”。");
            }

            var value = new Vector3D(x * scale, y * scale, z * scale);
            if (!Finite(value))
            {
                throw new InvalidDataException("ASCII STL 包含非有限顶点坐标。");
            }
            vertices.Add(value);
            if (vertices.Count / 3 > MaximumTriangleCount)
            {
                throw new InvalidDataException($"STL 超过 {MaximumTriangleCount} 个三角形上限。");
            }
        }

        if (vertices.Count == 0 || vertices.Count % 3 != 0)
        {
            throw new InvalidDataException("ASCII STL 的顶点数量不是三角形所需的三倍数。");
        }

        var result = new List<RawTriangle>(vertices.Count / 3);
        for (var index = 0; index < vertices.Count; index += 3)
        {
            result.Add(new RawTriangle(vertices[index], vertices[index + 1], vertices[index + 2]));
        }
        return result;
    }

    private static NormalizedMesh Normalize(IReadOnlyList<RawTriangle> raw)
    {
        var all = raw.SelectMany(item => new[] { item.A, item.B, item.C }).ToArray();
        var minimum = new Vector3D(all.Min(item => item.X), all.Min(item => item.Y), all.Min(item => item.Z));
        var maximum = new Vector3D(all.Max(item => item.X), all.Max(item => item.Y), all.Max(item => item.Z));
        var extent = (maximum - minimum).Length;
        var tolerance = Math.Max(1e-9, extent * 1e-12);
        var vertices = new List<Vector3D>();
        var lookup = new Dictionary<VertexKey, int>();
        var triangles = new List<NonSequentialMeshTriangle>(raw.Count);
        var uniqueFaces = new HashSet<FaceKey>();
        var warnings = new List<string>();
        var degenerate = 0;
        var duplicate = 0;

        foreach (var item in raw)
        {
            var a = Vertex(item.A);
            var b = Vertex(item.B);
            var c = Vertex(item.C);
            if (a == b || b == c || c == a
                || Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).Length <= tolerance * tolerance)
            {
                degenerate++;
                continue;
            }

            var faceKey = FaceKey.Create(a, b, c);
            if (!uniqueFaces.Add(faceKey))
            {
                duplicate++;
                continue;
            }
            triangles.Add(new NonSequentialMeshTriangle(a, b, c));
        }

        if (triangles.Count == 0)
        {
            throw new InvalidDataException("STL 清理退化面和重复面后没有剩余三角形。");
        }
        if (degenerate > 0) warnings.Add($"已移除 {degenerate} 个退化三角形。");
        if (duplicate > 0) warnings.Add($"已移除 {duplicate} 个重复三角形。");

        var edgeMap = BuildEdges(triangles);
        var manifold = edgeMap.Values.All(items => items.Count <= 2);
        var closed = manifold && edgeMap.Values.All(items => items.Count == 2);
        var (connected, orientable) = Orient(triangles, edgeMap);
        var volume = SignedVolume(vertices, triangles);
        if (closed && orientable && volume < 0)
        {
            for (var index = 0; index < triangles.Count; index++)
            {
                var triangle = triangles[index];
                triangles[index] = triangle with { B = triangle.C, C = triangle.B };
            }
            volume = -volume;
            warnings.Add("已统一翻转网格朝向以获得正体积和外法线。");
        }

        var selfIntersections = HasSelfIntersections(vertices, triangles);
        if (!manifold) warnings.Add("网格包含非流形边。");
        if (!closed) warnings.Add("网格未闭合，只能用于吸收或反射交互。");
        if (!connected) warnings.Add("网格包含多个互不连接的区域。");
        if (!orientable) warnings.Add("网格局部朝向冲突。");
        if (selfIntersections) warnings.Add("网格包含三角形自相交。");

        minimum = new Vector3D(vertices.Min(item => item.X), vertices.Min(item => item.Y), vertices.Min(item => item.Z));
        maximum = new Vector3D(vertices.Max(item => item.X), vertices.Max(item => item.Y), vertices.Max(item => item.Z));
        return new NormalizedMesh(vertices, triangles, minimum, maximum, closed, manifold, connected, orientable, selfIntersections, volume, warnings);

        int Vertex(Vector3D value)
        {
            var key = new VertexKey(
                checked((long)Math.Round(value.X / tolerance)),
                checked((long)Math.Round(value.Y / tolerance)),
                checked((long)Math.Round(value.Z / tolerance)));
            if (lookup.TryGetValue(key, out var existing)) return existing;
            var index = vertices.Count;
            vertices.Add(value);
            lookup.Add(key, index);
            return index;
        }
    }

    private static Dictionary<EdgeKey, List<EdgeOccurrence>> BuildEdges(
        IReadOnlyList<NonSequentialMeshTriangle> triangles)
    {
        var result = new Dictionary<EdgeKey, List<EdgeOccurrence>>();
        for (var index = 0; index < triangles.Count; index++)
        {
            var triangle = triangles[index];
            Add(triangle.A, triangle.B);
            Add(triangle.B, triangle.C);
            Add(triangle.C, triangle.A);

            void Add(int from, int to)
            {
                var key = EdgeKey.Create(from, to);
                if (!result.TryGetValue(key, out var occurrences))
                {
                    occurrences = new List<EdgeOccurrence>(2);
                    result.Add(key, occurrences);
                }
                occurrences.Add(new EdgeOccurrence(index, from == key.A));
            }
        }
        return result;
    }

    private static (bool Connected, bool Orientable) Orient(
        List<NonSequentialMeshTriangle> triangles,
        IReadOnlyDictionary<EdgeKey, List<EdgeOccurrence>> edgeMap)
    {
        var adjacency = Enumerable.Range(0, triangles.Count).Select(_ => new List<(int Other, bool Flip)>()).ToArray();
        foreach (var occurrences in edgeMap.Values.Where(value => value.Count == 2))
        {
            var first = occurrences[0];
            var second = occurrences[1];
            var flip = first.Forward == second.Forward;
            adjacency[first.Triangle].Add((second.Triangle, flip));
            adjacency[second.Triangle].Add((first.Triangle, flip));
        }

        var states = new int[triangles.Count];
        var components = 0;
        var orientable = true;
        for (var start = 0; start < triangles.Count; start++)
        {
            if (states[start] != 0) continue;
            components++;
            states[start] = 1;
            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var (other, flip) in adjacency[current])
                {
                    var expected = flip ? -states[current] : states[current];
                    if (states[other] == 0)
                    {
                        states[other] = expected;
                        queue.Enqueue(other);
                    }
                    else if (states[other] != expected)
                    {
                        orientable = false;
                    }
                }
            }
        }

        if (orientable)
        {
            for (var index = 0; index < triangles.Count; index++)
            {
                if (states[index] >= 0) continue;
                var triangle = triangles[index];
                triangles[index] = triangle with { B = triangle.C, C = triangle.B };
            }
        }
        return (components == 1, orientable);
    }

    private static bool HasSelfIntersections(
        IReadOnlyList<Vector3D> vertices,
        IReadOnlyList<NonSequentialMeshTriangle> triangles)
    {
        var index = new NonSequentialTriangleBvh(vertices, triangles);
        for (var first = 0; first < triangles.Count; first++)
        {
            var a = triangles[first];
            foreach (var second in index.Candidates(first))
            {
                if (second <= first) continue;
                var b = triangles[second];
                if (TrianglesIntersect(vertices, a, b)) return true;
            }
        }
        return false;
    }

    private static bool TrianglesIntersect(
        IReadOnlyList<Vector3D> vertices,
        NonSequentialMeshTriangle first,
        NonSequentialMeshTriangle second)
    {
        var a = new[] { vertices[first.A], vertices[first.B], vertices[first.C] };
        var b = new[] { vertices[second.A], vertices[second.B], vertices[second.C] };
        var firstNormal = Cross(a[1] - a[0], a[2] - a[0]);
        var secondNormal = Cross(b[1] - b[0], b[2] - b[0]);
        if (firstNormal.Length > 1e-15 && secondNormal.Length > 1e-15)
        {
            var normalizedFirst = firstNormal / firstNormal.Length;
            var normalizedSecond = secondNormal / secondNormal.Length;
            if (Cross(normalizedFirst, normalizedSecond).Length <= 1e-10
                && Math.Abs(Dot(normalizedFirst, b[0] - a[0])) <= 1e-9)
            {
                return SharesVertex(first, second)
                    ? CoplanarTriangleInteriorsOverlap(a, b, normalizedFirst)
                    : CoplanarTrianglesOverlap(a, b, normalizedFirst);
            }
        }
        for (var index = 0; index < 3; index++)
        {
            if (SegmentTriangle(a[index], a[(index + 1) % 3], b[0], b[1], b[2])
                || SegmentTriangle(b[index], b[(index + 1) % 3], a[0], a[1], a[2]))
            {
                return true;
            }
        }
        return false;
    }

    private static bool CoplanarTriangleInteriorsOverlap(
        IReadOnlyList<Vector3D> first,
        IReadOnlyList<Vector3D> second,
        Vector3D normal)
    {
        var axis = Math.Abs(normal.X) >= Math.Abs(normal.Y) && Math.Abs(normal.X) >= Math.Abs(normal.Z)
            ? 0
            : Math.Abs(normal.Y) >= Math.Abs(normal.Z) ? 1 : 2;
        var a = first.Select(Project).ToArray();
        var b = second.Select(Project).ToArray();
        for (var firstEdge = 0; firstEdge < 3; firstEdge++)
        {
            for (var secondEdge = 0; secondEdge < 3; secondEdge++)
            {
                if (SegmentsCrossStrictly(
                    a[firstEdge], a[(firstEdge + 1) % 3],
                    b[secondEdge], b[(secondEdge + 1) % 3]))
                {
                    return true;
                }
            }
        }
        return a.Any(point => PointInTriangleStrict(point, b))
            || b.Any(point => PointInTriangleStrict(point, a));

        Point2 Project(Vector3D point) => axis switch
        {
            0 => new Point2(point.Y, point.Z),
            1 => new Point2(point.X, point.Z),
            _ => new Point2(point.X, point.Y)
        };
    }

    private static bool SegmentsCrossStrictly(Point2 a, Point2 b, Point2 c, Point2 d) =>
        Orientation(a, b, c) * Orientation(a, b, d) < -1e-20
        && Orientation(c, d, a) * Orientation(c, d, b) < -1e-20;

    private static bool PointInTriangleStrict(Point2 point, IReadOnlyList<Point2> triangle)
    {
        var first = Orientation(triangle[0], triangle[1], point);
        var second = Orientation(triangle[1], triangle[2], point);
        var third = Orientation(triangle[2], triangle[0], point);
        return first > 1e-10 && second > 1e-10 && third > 1e-10
            || first < -1e-10 && second < -1e-10 && third < -1e-10;
    }

    private static bool CoplanarTrianglesOverlap(
        IReadOnlyList<Vector3D> first,
        IReadOnlyList<Vector3D> second,
        Vector3D normal)
    {
        var axis = Math.Abs(normal.X) >= Math.Abs(normal.Y) && Math.Abs(normal.X) >= Math.Abs(normal.Z)
            ? 0
            : Math.Abs(normal.Y) >= Math.Abs(normal.Z) ? 1 : 2;
        var a = first.Select(Project).ToArray();
        var b = second.Select(Project).ToArray();
        for (var firstEdge = 0; firstEdge < 3; firstEdge++)
        {
            for (var secondEdge = 0; secondEdge < 3; secondEdge++)
            {
                if (SegmentsOverlap(
                    a[firstEdge], a[(firstEdge + 1) % 3],
                    b[secondEdge], b[(secondEdge + 1) % 3]))
                {
                    return true;
                }
            }
        }
        return PointInTriangle(a[0], b) || PointInTriangle(b[0], a);

        Point2 Project(Vector3D point) => axis switch
        {
            0 => new Point2(point.Y, point.Z),
            1 => new Point2(point.X, point.Z),
            _ => new Point2(point.X, point.Y)
        };
    }

    private static bool SegmentsOverlap(Point2 a, Point2 b, Point2 c, Point2 d)
    {
        var abC = Orientation(a, b, c);
        var abD = Orientation(a, b, d);
        var cdA = Orientation(c, d, a);
        var cdB = Orientation(c, d, b);
        if (abC * abD < -1e-20 && cdA * cdB < -1e-20) return true;
        return Math.Abs(abC) <= 1e-10 && OnSegment(a, b, c)
            || Math.Abs(abD) <= 1e-10 && OnSegment(a, b, d)
            || Math.Abs(cdA) <= 1e-10 && OnSegment(c, d, a)
            || Math.Abs(cdB) <= 1e-10 && OnSegment(c, d, b);
    }

    private static bool PointInTriangle(Point2 point, IReadOnlyList<Point2> triangle)
    {
        var first = Orientation(triangle[0], triangle[1], point);
        var second = Orientation(triangle[1], triangle[2], point);
        var third = Orientation(triangle[2], triangle[0], point);
        var hasNegative = first < -1e-10 || second < -1e-10 || third < -1e-10;
        var hasPositive = first > 1e-10 || second > 1e-10 || third > 1e-10;
        return !(hasNegative && hasPositive);
    }

    private static double Orientation(Point2 a, Point2 b, Point2 c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static bool OnSegment(Point2 a, Point2 b, Point2 point) =>
        point.X >= Math.Min(a.X, b.X) - 1e-10 && point.X <= Math.Max(a.X, b.X) + 1e-10
        && point.Y >= Math.Min(a.Y, b.Y) - 1e-10 && point.Y <= Math.Max(a.Y, b.Y) + 1e-10;

    private static bool SegmentTriangle(Vector3D start, Vector3D end, Vector3D a, Vector3D b, Vector3D c)
    {
        var direction = end - start;
        var edge1 = b - a;
        var edge2 = c - a;
        var p = Cross(direction, edge2);
        var determinant = Dot(edge1, p);
        if (Math.Abs(determinant) <= 1e-12) return false;
        var inverse = 1 / determinant;
        var t = start - a;
        var u = Dot(t, p) * inverse;
        if (u <= 1e-10 || u >= 1 - 1e-10) return false;
        var q = Cross(t, edge1);
        var v = Dot(direction, q) * inverse;
        if (v <= 1e-10 || u + v >= 1 - 1e-10) return false;
        var distance = Dot(edge2, q) * inverse;
        return distance > 1e-10 && distance < 1 - 1e-10;
    }

    private static double SignedVolume(
        IReadOnlyList<Vector3D> vertices,
        IReadOnlyList<NonSequentialMeshTriangle> triangles) =>
        triangles.Sum(triangle => Dot(vertices[triangle.A], Cross(vertices[triangle.B], vertices[triangle.C])) / 6.0);

    private static bool SharesVertex(NonSequentialMeshTriangle first, NonSequentialMeshTriangle second) =>
        first.A == second.A || first.A == second.B || first.A == second.C
        || first.B == second.A || first.B == second.B || first.B == second.C
        || first.C == second.A || first.C == second.B || first.C == second.C;

    private static bool Finite(Vector3D value) =>
        double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);
    private static Vector3D Cross(Vector3D left, Vector3D right) => new(
        left.Y * right.Z - left.Z * right.Y,
        left.Z * right.X - left.X * right.Z,
        left.X * right.Y - left.Y * right.X);
    private static double Dot(Vector3D left, Vector3D right) =>
        left.X * right.X + left.Y * right.Y + left.Z * right.Z;

    private readonly record struct RawTriangle(Vector3D A, Vector3D B, Vector3D C);
    private readonly record struct Point2(double X, double Y);
    private readonly record struct VertexKey(long X, long Y, long Z);
    private readonly record struct FaceKey(int A, int B, int C)
    {
        public static FaceKey Create(int a, int b, int c)
        {
            Span<int> values = stackalloc[] { a, b, c };
            values.Sort();
            return new FaceKey(values[0], values[1], values[2]);
        }
    }
    private readonly record struct EdgeKey(int A, int B)
    {
        public static EdgeKey Create(int a, int b) => a < b ? new EdgeKey(a, b) : new EdgeKey(b, a);
    }
    private readonly record struct EdgeOccurrence(int Triangle, bool Forward);
    private sealed record NormalizedMesh(
        IReadOnlyList<Vector3D> Vertices,
        IReadOnlyList<NonSequentialMeshTriangle> Triangles,
        Vector3D Minimum,
        Vector3D Maximum,
        bool IsClosed,
        bool IsManifold,
        bool IsConnected,
        bool IsOrientable,
        bool HasSelfIntersections,
        double Volume,
        IReadOnlyList<string> Warnings);
}

internal sealed class NonSequentialTriangleBvh
{
    private const double RayEpsilon = 1e-9;
    private readonly IReadOnlyList<Vector3D> _vertices;
    private readonly IReadOnlyList<NonSequentialMeshTriangle> _triangles;
    private readonly Node _root;

    public NonSequentialTriangleBvh(
        IReadOnlyList<Vector3D> vertices,
        IReadOnlyList<NonSequentialMeshTriangle> triangles)
    {
        _vertices = vertices;
        _triangles = triangles;
        _root = Build(Enumerable.Range(0, triangles.Count).ToArray());
    }

    public NonSequentialMeshHit? Intersect(Vector3D origin, Vector3D direction, bool twoSided)
    {
        var best = double.PositiveInfinity;
        NonSequentialMeshHit? hit = null;
        foreach (var index in RayCandidates(_root, origin, direction))
        {
            var triangle = _triangles[index];
            var a = _vertices[triangle.A];
            var b = _vertices[triangle.B];
            var c = _vertices[triangle.C];
            var edge1 = b - a;
            var edge2 = c - a;
            var p = Cross(direction, edge2);
            var determinant = Dot(edge1, p);
            if (twoSided ? Math.Abs(determinant) <= 1e-14 : determinant <= 1e-14) continue;
            var inverse = 1 / determinant;
            var t = origin - a;
            var u = Dot(t, p) * inverse;
            if (u < -1e-12 || u > 1 + 1e-12) continue;
            var q = Cross(t, edge1);
            var v = Dot(direction, q) * inverse;
            if (v < -1e-12 || u + v > 1 + 1e-12) continue;
            var distance = Dot(edge2, q) * inverse;
            if (distance <= RayEpsilon || distance >= best) continue;
            var normal = Cross(edge1, edge2);
            if (normal.Length <= 1e-15) continue;
            normal /= normal.Length;
            best = distance;
            hit = new NonSequentialMeshHit(
                distance,
                origin + direction * distance,
                normal,
                triangle.FaceNumber,
                Dot(direction, normal) < 0);
        }
        return hit;
    }

    public IEnumerable<int> Candidates(int triangleIndex)
    {
        var bounds = Bounds(_triangles[triangleIndex]);
        return BoxCandidates(_root, bounds);
    }

    private Node Build(int[] indices)
    {
        var bounds = Union(indices.Select(index => Bounds(_triangles[index])));
        if (indices.Length <= 8)
        {
            return new Node(bounds, null, null, indices);
        }

        var extent = bounds.Maximum - bounds.Minimum;
        var axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
        Array.Sort(indices, (left, right) => Center(Bounds(_triangles[left]), axis).CompareTo(Center(Bounds(_triangles[right]), axis)));
        var middle = indices.Length / 2;
        return new Node(bounds, Build(indices[..middle]), Build(indices[middle..]), null);
    }

    private IEnumerable<int> RayCandidates(Node node, Vector3D origin, Vector3D direction)
    {
        if (!node.Bounds.Hits(origin, direction)) yield break;
        if (node.Indices is not null)
        {
            foreach (var index in node.Indices) yield return index;
            yield break;
        }
        if (node.Left is not null) foreach (var index in RayCandidates(node.Left, origin, direction)) yield return index;
        if (node.Right is not null) foreach (var index in RayCandidates(node.Right, origin, direction)) yield return index;
    }

    private IEnumerable<int> BoxCandidates(Node node, Bounds3 bounds)
    {
        if (!node.Bounds.Overlaps(bounds)) yield break;
        if (node.Indices is not null)
        {
            foreach (var index in node.Indices) yield return index;
            yield break;
        }
        if (node.Left is not null) foreach (var index in BoxCandidates(node.Left, bounds)) yield return index;
        if (node.Right is not null) foreach (var index in BoxCandidates(node.Right, bounds)) yield return index;
    }

    private Bounds3 Bounds(NonSequentialMeshTriangle triangle)
    {
        var a = _vertices[triangle.A];
        var b = _vertices[triangle.B];
        var c = _vertices[triangle.C];
        return new Bounds3(
            new Vector3D(Math.Min(a.X, Math.Min(b.X, c.X)), Math.Min(a.Y, Math.Min(b.Y, c.Y)), Math.Min(a.Z, Math.Min(b.Z, c.Z))),
            new Vector3D(Math.Max(a.X, Math.Max(b.X, c.X)), Math.Max(a.Y, Math.Max(b.Y, c.Y)), Math.Max(a.Z, Math.Max(b.Z, c.Z))));
    }

    private static Bounds3 Union(IEnumerable<Bounds3> bounds)
    {
        var values = bounds.ToArray();
        return new Bounds3(
            new Vector3D(values.Min(item => item.Minimum.X), values.Min(item => item.Minimum.Y), values.Min(item => item.Minimum.Z)),
            new Vector3D(values.Max(item => item.Maximum.X), values.Max(item => item.Maximum.Y), values.Max(item => item.Maximum.Z)));
    }

    private static double Center(Bounds3 bounds, int axis) => axis switch
    {
        0 => bounds.Minimum.X + bounds.Maximum.X,
        1 => bounds.Minimum.Y + bounds.Maximum.Y,
        _ => bounds.Minimum.Z + bounds.Maximum.Z
    };

    private static Vector3D Cross(Vector3D left, Vector3D right) => new(
        left.Y * right.Z - left.Z * right.Y,
        left.Z * right.X - left.X * right.Z,
        left.X * right.Y - left.Y * right.X);
    private static double Dot(Vector3D left, Vector3D right) =>
        left.X * right.X + left.Y * right.Y + left.Z * right.Z;

    private sealed record Node(Bounds3 Bounds, Node? Left, Node? Right, int[]? Indices);
    private sealed record Bounds3(Vector3D Minimum, Vector3D Maximum)
    {
        public bool Overlaps(Bounds3 other) =>
            Minimum.X <= other.Maximum.X && Maximum.X >= other.Minimum.X
            && Minimum.Y <= other.Maximum.Y && Maximum.Y >= other.Minimum.Y
            && Minimum.Z <= other.Maximum.Z && Maximum.Z >= other.Minimum.Z;

        public bool Hits(Vector3D origin, Vector3D direction)
        {
            var minimum = 0.0;
            var maximum = double.PositiveInfinity;
            return Axis(origin.X, direction.X, Minimum.X, Maximum.X)
                && Axis(origin.Y, direction.Y, Minimum.Y, Maximum.Y)
                && Axis(origin.Z, direction.Z, Minimum.Z, Maximum.Z);

            bool Axis(double value, double delta, double low, double high)
            {
                if (Math.Abs(delta) < 1e-15) return value >= low && value <= high;
                var first = (low - value) / delta;
                var second = (high - value) / delta;
                if (first > second) (first, second) = (second, first);
                minimum = Math.Max(minimum, first);
                maximum = Math.Min(maximum, second);
                return maximum >= minimum;
            }
        }
    }
}
