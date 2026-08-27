using OptilandWorkbench.Core.Backend;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Geometries;

namespace OptilandWorkbench.Core.FileIO;

internal sealed record CadLensMesh(
    int FrontSurfaceNumber,
    int BackSurfaceNumber,
    string Material,
    IReadOnlyList<Vector3D> Vertices,
    IReadOnlyList<CadTriangle> Triangles,
    double MaximumChordErrorMillimeters);

internal sealed record CadLensMeshBuildResult(
    IReadOnlyList<CadLensMesh> Parts,
    IReadOnlyList<string> Warnings);

internal readonly record struct CadTriangle(int A, int B, int C);

internal static class CadLensMeshBuilder
{
    private const double VertexResolutionMillimeters = 1e-9;
    private const double DegenerateTriangleTolerance = 1e-20;
    private const double IntersectionTolerance = 1e-9;

    public static CadLensMeshBuildResult Build(
        Optic optic,
        StepCadExportOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(optic);
        ArgumentNullException.ThrowIfNull(options);

        var surfaces = optic.SurfaceGroup.Items;
        var wavelength = optic.Wavelengths.FirstOrDefault(item => item.IsPrimary)?.Nanometers
            ?? optic.Wavelengths.FirstOrDefault()?.Nanometers
            ?? 587.6;
        var parts = new List<CadLensMesh>();
        var omittedReflectors = new List<int>();
        var usedSurfaceNumbers = new HashSet<int>();

        for (var index = 1; index < surfaces.Count - 1; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var front = surfaces[index];
            if (front.IsReflective)
            {
                var isBackOfSolid = index > 1
                    && !surfaces[index - 1].IsReflective
                    && HasOpticalMaterialAfter(surfaces[index - 1], wavelength);
                if (!isBackOfSolid)
                {
                    omittedReflectors.Add(front.Number);
                }

                continue;
            }

            if (!HasOpticalMaterialAfter(front, wavelength))
            {
                continue;
            }

            var back = surfaces[index + 1];
            parts.Add(BuildPart(front, back, options, cancellationToken));
            usedSurfaceNumbers.Add(front.Number);
            usedSurfaceNumbers.Add(back.Number);
        }

        if (parts.Count == 0)
        {
            throw new InvalidOperationException("当前光学系统没有可导出的镜片实体。");
        }

        var warnings = new List<string>();
        if (omittedReflectors.Count > 0)
        {
            warnings.Add(
                $"未导出没有基底实体定义的反射面：{string.Join(", ", omittedReflectors)}。");
        }

        var nonSolidSurfaces = surfaces
            .Where((surface, index) =>
                !usedSurfaceNumbers.Contains(surface.Number)
                && !surface.IsReflective
                && (index == 0 || index == surfaces.Count - 1 || surface.IsStop))
            .Select(surface => surface.Number)
            .ToArray();
        if (nonSolidSurfaces.Length > 0)
        {
            warnings.Add(
                $"未为物面、像面或独立光阑生成实体：{string.Join(", ", nonSolidSurfaces)}。");
        }

        return new CadLensMeshBuildResult(parts, warnings);
    }

    private static bool HasOpticalMaterialAfter(OpticalSurface surface, double wavelengthNanometers) =>
        surface.MaterialAfter.RefractiveIndex(wavelengthNanometers) > 1.0001;

    private static CadLensMesh BuildPart(
        OpticalSurface front,
        OpticalSurface back,
        StepCadExportOptions options,
        CancellationToken cancellationToken)
    {
        var radialSegments = Math.Max(4, (NormalizeSurfaceSamples(options.SurfaceSamples) - 1) / 2);
        var angularSegments = Math.Clamp(options.AngularSamples, 32, 192);
        var chordTolerance = options.MaximumChordErrorMillimeters;
        if (!double.IsFinite(chordTolerance) || chordTolerance <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.MaximumChordErrorMillimeters),
                "STEP 最大弦高误差必须是有限正数。");
        }

        if (options.MaximumTrianglesPerPart < 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.MaximumTrianglesPerPart),
                "STEP 单零件三角形上限至少为 4。");
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var extendedSurfaceCount = 0;
            var mechanicalSemiDiameter = Math.Max(front.SemiDiameter, back.SemiDiameter);
            if (front.SemiDiameter < mechanicalSemiDiameter - VertexResolutionMillimeters)
            {
                extendedSurfaceCount++;
            }

            if (back.SemiDiameter < mechanicalSemiDiameter - VertexResolutionMillimeters)
            {
                extendedSurfaceCount++;
            }

            var expectedTriangles = checked(
                (4L * radialSegments * angularSegments)
                + (2L * extendedSurfaceCount * angularSegments));
            if (expectedTriangles > options.MaximumTrianglesPerPart)
            {
                throw new InvalidOperationException(
                    $"镜片 S{front.Number}-S{back.Number} 为满足 {chordTolerance:G6} mm 弦高误差需要超过 "
                    + $"{options.MaximumTrianglesPerPart} 个三角形。请放宽精度或提高单零件上限。");
            }

            var candidate = BuildCandidate(
                front,
                back,
                radialSegments,
                angularSegments,
                cancellationToken);
            if (candidate.MaximumChordErrorMillimeters <= chordTolerance)
            {
                return candidate;
            }

            radialSegments = checked(radialSegments * 2);
            angularSegments = checked(angularSegments * 2);
        }
    }

    private static CadLensMesh BuildCandidate(
        OpticalSurface front,
        OpticalSurface back,
        int radialSegments,
        int angularSegments,
        CancellationToken cancellationToken)
    {
        var assembler = new MeshAssembler(VertexResolutionMillimeters);
        var frontGrid = BuildSurfaceGrid(
            assembler,
            front,
            radialSegments,
            angularSegments,
            cancellationToken);
        var backGrid = BuildSurfaceGrid(
            assembler,
            back,
            radialSegments,
            angularSegments,
            cancellationToken);

        AddSurfaceTriangles(assembler, frontGrid, outwardPositiveNormal: false);
        AddSurfaceTriangles(assembler, backGrid, outwardPositiveNormal: true);
        var mechanicalSemiDiameter = Math.Max(front.SemiDiameter, back.SemiDiameter);
        var frontMechanicalRim = AddMechanicalExtension(
            assembler,
            front,
            frontGrid.Rings[radialSegments - 1],
            mechanicalSemiDiameter,
            outwardPositiveNormal: false);
        var backMechanicalRim = AddMechanicalExtension(
            assembler,
            back,
            backGrid.Rings[radialSegments - 1],
            mechanicalSemiDiameter,
            outwardPositiveNormal: true);
        AddSideTriangles(assembler, frontMechanicalRim, backMechanicalRim);

        var maximumChordError = Math.Max(
            MeasureChordError(front, radialSegments, angularSegments, cancellationToken),
            MeasureChordError(back, radialSegments, angularSegments, cancellationToken));
        var mesh = assembler.BuildAndValidate(
            $"S{front.Number}-S{back.Number}",
            cancellationToken);
        return new CadLensMesh(
            front.Number,
            back.Number,
            front.MaterialAfterName,
            mesh.Vertices,
            mesh.Triangles,
            maximumChordError);
    }

    private static SurfaceGrid BuildSurfaceGrid(
        MeshAssembler assembler,
        OpticalSurface surface,
        int radialSegments,
        int angularSegments,
        CancellationToken cancellationToken)
    {
        var center = assembler.Vertex(EvaluateSurfacePoint(surface, 0, 0));
        var rings = new int[radialSegments][];
        for (var radialIndex = 1; radialIndex <= radialSegments; radialIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var radius = surface.SemiDiameter * radialIndex / radialSegments;
            var ring = new int[angularSegments];
            for (var angularIndex = 0; angularIndex < angularSegments; angularIndex++)
            {
                var angle = 2.0 * Math.PI * angularIndex / angularSegments;
                ring[angularIndex] = assembler.Vertex(EvaluateSurfacePoint(
                    surface,
                    radius * Math.Cos(angle),
                    radius * Math.Sin(angle)));
            }

            rings[radialIndex - 1] = ring;
        }

        return new SurfaceGrid(center, rings);
    }

    private static void AddSurfaceTriangles(
        MeshAssembler assembler,
        SurfaceGrid grid,
        bool outwardPositiveNormal)
    {
        var angularSegments = grid.Rings[0].Length;
        for (var angularIndex = 0; angularIndex < angularSegments; angularIndex++)
        {
            var next = (angularIndex + 1) % angularSegments;
            AddOriented(
                assembler,
                grid.Center,
                grid.Rings[0][angularIndex],
                grid.Rings[0][next],
                outwardPositiveNormal);
        }

        for (var radialIndex = 0; radialIndex < grid.Rings.Count - 1; radialIndex++)
        {
            var inner = grid.Rings[radialIndex];
            var outer = grid.Rings[radialIndex + 1];
            for (var angularIndex = 0; angularIndex < angularSegments; angularIndex++)
            {
                var next = (angularIndex + 1) % angularSegments;
                AddOriented(
                    assembler,
                    inner[angularIndex],
                    outer[angularIndex],
                    outer[next],
                    outwardPositiveNormal);
                AddOriented(
                    assembler,
                    inner[angularIndex],
                    outer[next],
                    inner[next],
                    outwardPositiveNormal);
            }
        }
    }

    private static void AddOriented(
        MeshAssembler assembler,
        int a,
        int b,
        int c,
        bool outwardPositiveNormal)
    {
        if (outwardPositiveNormal)
        {
            assembler.AddTriangle(a, b, c);
        }
        else
        {
            assembler.AddTriangle(a, c, b);
        }
    }

    private static IReadOnlyList<int> AddMechanicalExtension(
        MeshAssembler assembler,
        OpticalSurface surface,
        IReadOnlyList<int> opticalRim,
        double mechanicalSemiDiameter,
        bool outwardPositiveNormal)
    {
        if (mechanicalSemiDiameter <= surface.SemiDiameter + VertexResolutionMillimeters)
        {
            return opticalRim;
        }

        var angularSegments = opticalRim.Count;
        var mechanicalRim = new int[angularSegments];
        for (var angularIndex = 0; angularIndex < angularSegments; angularIndex++)
        {
            var angle = 2.0 * Math.PI * angularIndex / angularSegments;
            var cosine = Math.Cos(angle);
            var sine = Math.Sin(angle);
            var edgeX = surface.SemiDiameter * cosine;
            var edgeY = surface.SemiDiameter * sine;
            var sag = EvaluateSag(surface, edgeX, edgeY);
            var point = surface.CoordinateSystem.ToGlobalPoint(new Vector3D(
                mechanicalSemiDiameter * cosine,
                mechanicalSemiDiameter * sine,
                sag));
            EnsureFiniteGlobalPoint(surface, edgeX, edgeY, point);
            mechanicalRim[angularIndex] = assembler.Vertex(point);
        }

        for (var angularIndex = 0; angularIndex < angularSegments; angularIndex++)
        {
            var next = (angularIndex + 1) % angularSegments;
            AddOriented(
                assembler,
                opticalRim[angularIndex],
                mechanicalRim[angularIndex],
                mechanicalRim[next],
                outwardPositiveNormal);
            AddOriented(
                assembler,
                opticalRim[angularIndex],
                mechanicalRim[next],
                opticalRim[next],
                outwardPositiveNormal);
        }

        return mechanicalRim;
    }

    private static void AddSideTriangles(
        MeshAssembler assembler,
        IReadOnlyList<int> frontRim,
        IReadOnlyList<int> backRim)
    {
        var angularSegments = frontRim.Count;
        for (var angularIndex = 0; angularIndex < angularSegments; angularIndex++)
        {
            var next = (angularIndex + 1) % angularSegments;
            assembler.AddTriangle(frontRim[angularIndex], frontRim[next], backRim[next]);
            assembler.AddTriangle(frontRim[angularIndex], backRim[next], backRim[angularIndex]);
        }
    }

    private static double MeasureChordError(
        OpticalSurface surface,
        int radialSegments,
        int angularSegments,
        CancellationToken cancellationToken)
    {
        var maximum = 0.0;
        for (var radialIndex = 0; radialIndex < radialSegments; radialIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var innerRadius = surface.SemiDiameter * radialIndex / radialSegments;
            var outerRadius = surface.SemiDiameter * (radialIndex + 1) / radialSegments;
            for (var angularIndex = 0; angularIndex < angularSegments; angularIndex++)
            {
                var angle = 2.0 * Math.PI * angularIndex / angularSegments;
                var nextAngle = 2.0 * Math.PI * (angularIndex + 1) / angularSegments;
                var innerA = Polar(innerRadius, angle);
                var outerA = Polar(outerRadius, angle);
                var outerB = Polar(outerRadius, nextAngle);
                var innerB = Polar(innerRadius, nextAngle);

                if (radialIndex == 0)
                {
                    maximum = Math.Max(maximum, TriangleChordError(
                        surface,
                        (0, 0),
                        outerA,
                        outerB));
                }
                else
                {
                    maximum = Math.Max(maximum, TriangleChordError(surface, innerA, outerA, outerB));
                    maximum = Math.Max(maximum, TriangleChordError(surface, innerA, outerB, innerB));
                }
            }
        }

        return maximum;
    }

    private static double TriangleChordError(
        OpticalSurface surface,
        (double X, double Y) a,
        (double X, double Y) b,
        (double X, double Y) c)
    {
        var ga = EvaluateSurfacePoint(surface, a.X, a.Y);
        var gb = EvaluateSurfacePoint(surface, b.X, b.Y);
        var gc = EvaluateSurfacePoint(surface, c.X, c.Y);
        var maximum = ChordError(surface, a, b, ga, gb);
        maximum = Math.Max(maximum, ChordError(surface, b, c, gb, gc));
        maximum = Math.Max(maximum, ChordError(surface, c, a, gc, ga));

        var centroid = ((a.X + b.X + c.X) / 3.0, (a.Y + b.Y + c.Y) / 3.0);
        var actual = EvaluateSurfacePoint(surface, centroid.Item1, centroid.Item2);
        var linear = (ga + gb + gc) / 3.0;
        return Math.Max(maximum, (actual - linear).Length);
    }

    private static double ChordError(
        OpticalSurface surface,
        (double X, double Y) a,
        (double X, double Y) b,
        Vector3D ga,
        Vector3D gb)
    {
        var midpoint = ((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);
        var actual = EvaluateSurfacePoint(surface, midpoint.Item1, midpoint.Item2);
        return (actual - ((ga + gb) / 2.0)).Length;
    }

    private static (double X, double Y) Polar(double radius, double angle) =>
        (radius * Math.Cos(angle), radius * Math.Sin(angle));

    private static Vector3D EvaluateSurfacePoint(OpticalSurface surface, double x, double y)
    {
        var sag = EvaluateSag(surface, x, y);
        var point = surface.CoordinateSystem.ToGlobalPoint(new Vector3D(x, y, sag));
        EnsureFiniteGlobalPoint(surface, x, y, point);
        return point;
    }

    private static double EvaluateSag(OpticalSurface surface, double x, double y)
    {
        double sag;
        try
        {
            sag = surface.Geometry.Sag(x, y);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"镜片表面 {surface.Number} 在 ({x:G8}, {y:G8}) mm 处无法计算 Sag：{exception.Message}",
                exception);
        }

        if (!double.IsFinite(sag))
        {
            throw new InvalidOperationException(
                $"镜片表面 {surface.Number} 在 ({x:G8}, {y:G8}) mm 处产生非有限 Sag。");
        }

        return sag;
    }

    private static void EnsureFiniteGlobalPoint(
        OpticalSurface surface,
        double x,
        double y,
        Vector3D point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(point.Z))
        {
            throw new InvalidOperationException(
                $"镜片表面 {surface.Number} 在 ({x:G8}, {y:G8}) mm 处产生非有限全局坐标。");
        }
    }

    private static int NormalizeSurfaceSamples(int samples)
    {
        samples = Math.Clamp(samples, 9, 129);
        return samples % 2 == 0 ? samples + 1 : samples;
    }

    private sealed record SurfaceGrid(int Center, IReadOnlyList<int[]> Rings);

    private sealed class MeshAssembler
    {
        private readonly double _resolution;
        private readonly Dictionary<VertexKey, int> _vertexLookup = new();
        private readonly List<Vector3D> _vertices = new();
        private readonly List<CadTriangle> _triangles = new();

        public MeshAssembler(double resolution)
        {
            _resolution = resolution;
        }

        public int Vertex(Vector3D point)
        {
            var key = new VertexKey(
                Quantize(point.X),
                Quantize(point.Y),
                Quantize(point.Z));
            if (_vertexLookup.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var index = _vertices.Count;
            _vertices.Add(point);
            _vertexLookup.Add(key, index);
            return index;
        }

        public void AddTriangle(int a, int b, int c)
        {
            if (a == b || b == c || c == a)
            {
                return;
            }

            var normal = Cross(_vertices[b] - _vertices[a], _vertices[c] - _vertices[a]);
            if (Dot(normal, normal) <= DegenerateTriangleTolerance)
            {
                return;
            }

            _triangles.Add(new CadTriangle(a, b, c));
        }

        public ValidatedMesh BuildAndValidate(string label, CancellationToken cancellationToken)
        {
            if (_triangles.Count < 4)
            {
                throw new InvalidOperationException($"镜片 {label} 无法形成有效 CAD 实体。");
            }

            var edgeUse = new Dictionary<EdgeKey, EdgeUse>();
            foreach (var triangle in _triangles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CountEdge(edgeUse, triangle.A, triangle.B);
                CountEdge(edgeUse, triangle.B, triangle.C);
                CountEdge(edgeUse, triangle.C, triangle.A);
            }

            var invalidEdgeCount = edgeUse.Count(pair =>
                pair.Value.Count != 2 || pair.Value.DirectionBalance != 0);
            if (invalidEdgeCount > 0)
            {
                throw new InvalidOperationException(
                    $"镜片 {label} 的网格不是闭合流形（{invalidEdgeCount} 条边无效）。");
            }

            var signedVolume = SignedVolume(_vertices, _triangles);
            if (!double.IsFinite(signedVolume) || Math.Abs(signedVolume) <= 1e-12)
            {
                throw new InvalidOperationException($"镜片 {label} 的 CAD 实体体积为零。");
            }

            if (signedVolume < 0)
            {
                for (var index = 0; index < _triangles.Count; index++)
                {
                    var triangle = _triangles[index];
                    _triangles[index] = new CadTriangle(triangle.A, triangle.C, triangle.B);
                }
            }

            MeshIntersectionValidator.ThrowIfSelfIntersecting(
                label,
                _vertices,
                _triangles,
                cancellationToken);
            return new ValidatedMesh(_vertices.ToArray(), _triangles.ToArray());
        }

        private long Quantize(double value)
        {
            if (!double.IsFinite(value))
            {
                throw new InvalidOperationException("CAD 几何包含非有限坐标。");
            }

            return checked((long)Math.Round(
                value / _resolution,
                MidpointRounding.AwayFromZero));
        }

        private static void CountEdge(
            IDictionary<EdgeKey, EdgeUse> edges,
            int first,
            int second)
        {
            var key = first < second ? new EdgeKey(first, second) : new EdgeKey(second, first);
            var direction = first < second ? 1 : -1;
            var current = edges.TryGetValue(key, out var use) ? use : default;
            edges[key] = new EdgeUse(current.Count + 1, current.DirectionBalance + direction);
        }
    }

    private static class MeshIntersectionValidator
    {
        public static void ThrowIfSelfIntersecting(
            string label,
            IReadOnlyList<Vector3D> vertices,
            IReadOnlyList<CadTriangle> triangles,
            CancellationToken cancellationToken)
        {
            var entries = triangles
                .Select((triangle, index) => new TriangleEntry(
                    index,
                    Bounds(vertices, triangle),
                    Centroid(vertices, triangle)))
                .ToArray();
            var root = BuildNode(entries, 0, entries.Length);
            if (Intersects(root, root, vertices, triangles, cancellationToken, sameNode: true))
            {
                throw new InvalidOperationException($"镜片 {label} 的网格存在自相交。");
            }
        }

        private static BvhNode BuildNode(TriangleEntry[] entries, int start, int count)
        {
            var bounds = entries[start].Bounds;
            for (var index = start + 1; index < start + count; index++)
            {
                bounds = bounds.Union(entries[index].Bounds);
            }

            if (count <= 8)
            {
                return new BvhNode(bounds, entries[start..(start + count)], null, null);
            }

            var extent = bounds.Maximum - bounds.Minimum;
            var axis = extent.X >= extent.Y && extent.X >= extent.Z
                ? 0
                : extent.Y >= extent.Z ? 1 : 2;
            Array.Sort(
                entries,
                start,
                count,
                Comparer<TriangleEntry>.Create((left, right) =>
                    Coordinate(left.Centroid, axis).CompareTo(Coordinate(right.Centroid, axis))));
            var leftCount = count / 2;
            var left = BuildNode(entries, start, leftCount);
            var right = BuildNode(entries, start + leftCount, count - leftCount);
            return new BvhNode(bounds, null, left, right);
        }

        private static bool Intersects(
            BvhNode left,
            BvhNode right,
            IReadOnlyList<Vector3D> vertices,
            IReadOnlyList<CadTriangle> triangles,
            CancellationToken cancellationToken,
            bool sameNode)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!left.Bounds.Overlaps(right.Bounds))
            {
                return false;
            }

            if (left.Entries is not null && right.Entries is not null)
            {
                foreach (var first in left.Entries)
                {
                    foreach (var second in right.Entries)
                    {
                        if ((sameNode && first.Index >= second.Index)
                            || SharesVertex(triangles[first.Index], triangles[second.Index]))
                        {
                            continue;
                        }

                        if (first.Bounds.Overlaps(second.Bounds)
                            && TrianglesIntersect(
                                vertices,
                                triangles[first.Index],
                                triangles[second.Index]))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            if (sameNode)
            {
                return Intersects(left.Left!, left.Left!, vertices, triangles, cancellationToken, true)
                    || Intersects(left.Left!, left.Right!, vertices, triangles, cancellationToken, false)
                    || Intersects(left.Right!, left.Right!, vertices, triangles, cancellationToken, true);
            }

            if (left.Entries is null
                && (right.Entries is not null || left.Bounds.Volume >= right.Bounds.Volume))
            {
                return Intersects(left.Left!, right, vertices, triangles, cancellationToken, false)
                    || Intersects(left.Right!, right, vertices, triangles, cancellationToken, false);
            }

            return Intersects(left, right.Left!, vertices, triangles, cancellationToken, false)
                || Intersects(left, right.Right!, vertices, triangles, cancellationToken, false);
        }

        private static bool TrianglesIntersect(
            IReadOnlyList<Vector3D> vertices,
            CadTriangle first,
            CadTriangle second)
        {
            var a = new[] { vertices[first.A], vertices[first.B], vertices[first.C] };
            var b = new[] { vertices[second.A], vertices[second.B], vertices[second.C] };
            for (var index = 0; index < 3; index++)
            {
                if (SegmentIntersectsTriangle(a[index], a[(index + 1) % 3], b[0], b[1], b[2])
                    || SegmentIntersectsTriangle(b[index], b[(index + 1) % 3], a[0], a[1], a[2]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SegmentIntersectsTriangle(
            Vector3D start,
            Vector3D end,
            Vector3D a,
            Vector3D b,
            Vector3D c)
        {
            var direction = end - start;
            var edge1 = b - a;
            var edge2 = c - a;
            var p = Cross(direction, edge2);
            var determinant = Dot(edge1, p);
            if (Math.Abs(determinant) <= IntersectionTolerance)
            {
                return false;
            }

            var inverse = 1.0 / determinant;
            var offset = start - a;
            var u = Dot(offset, p) * inverse;
            if (u <= IntersectionTolerance || u >= 1.0 - IntersectionTolerance)
            {
                return false;
            }

            var q = Cross(offset, edge1);
            var v = Dot(direction, q) * inverse;
            if (v <= IntersectionTolerance || u + v >= 1.0 - IntersectionTolerance)
            {
                return false;
            }

            var t = Dot(edge2, q) * inverse;
            return t > IntersectionTolerance && t < 1.0 - IntersectionTolerance;
        }

        private static bool SharesVertex(CadTriangle left, CadTriangle right) =>
            left.A == right.A || left.A == right.B || left.A == right.C
            || left.B == right.A || left.B == right.B || left.B == right.C
            || left.C == right.A || left.C == right.B || left.C == right.C;

        private static Bounds3 Bounds(IReadOnlyList<Vector3D> vertices, CadTriangle triangle)
        {
            var a = vertices[triangle.A];
            var b = vertices[triangle.B];
            var c = vertices[triangle.C];
            return new Bounds3(
                new Vector3D(
                    Math.Min(a.X, Math.Min(b.X, c.X)),
                    Math.Min(a.Y, Math.Min(b.Y, c.Y)),
                    Math.Min(a.Z, Math.Min(b.Z, c.Z))),
                new Vector3D(
                    Math.Max(a.X, Math.Max(b.X, c.X)),
                    Math.Max(a.Y, Math.Max(b.Y, c.Y)),
                    Math.Max(a.Z, Math.Max(b.Z, c.Z))));
        }

        private static Vector3D Centroid(IReadOnlyList<Vector3D> vertices, CadTriangle triangle) =>
            (vertices[triangle.A] + vertices[triangle.B] + vertices[triangle.C]) / 3.0;

        private static double Coordinate(Vector3D point, int axis) =>
            axis == 0 ? point.X : axis == 1 ? point.Y : point.Z;

        private sealed record BvhNode(
            Bounds3 Bounds,
            TriangleEntry[]? Entries,
            BvhNode? Left,
            BvhNode? Right);

        private readonly record struct TriangleEntry(int Index, Bounds3 Bounds, Vector3D Centroid);

        private readonly record struct Bounds3(Vector3D Minimum, Vector3D Maximum)
        {
            public double Volume
            {
                get
                {
                    var extent = Maximum - Minimum;
                    return Math.Max(0, extent.X) * Math.Max(0, extent.Y) * Math.Max(0, extent.Z);
                }
            }

            public bool Overlaps(Bounds3 other) =>
                Minimum.X <= other.Maximum.X + IntersectionTolerance
                && Maximum.X + IntersectionTolerance >= other.Minimum.X
                && Minimum.Y <= other.Maximum.Y + IntersectionTolerance
                && Maximum.Y + IntersectionTolerance >= other.Minimum.Y
                && Minimum.Z <= other.Maximum.Z + IntersectionTolerance
                && Maximum.Z + IntersectionTolerance >= other.Minimum.Z;

            public Bounds3 Union(Bounds3 other) => new(
                new Vector3D(
                    Math.Min(Minimum.X, other.Minimum.X),
                    Math.Min(Minimum.Y, other.Minimum.Y),
                    Math.Min(Minimum.Z, other.Minimum.Z)),
                new Vector3D(
                    Math.Max(Maximum.X, other.Maximum.X),
                    Math.Max(Maximum.Y, other.Maximum.Y),
                    Math.Max(Maximum.Z, other.Maximum.Z)));
        }
    }

    private static double SignedVolume(
        IReadOnlyList<Vector3D> vertices,
        IReadOnlyList<CadTriangle> triangles)
    {
        var volume = 0.0;
        foreach (var triangle in triangles)
        {
            volume += Dot(
                vertices[triangle.A],
                Cross(vertices[triangle.B], vertices[triangle.C])) / 6.0;
        }

        return volume;
    }

    private static Vector3D Cross(Vector3D left, Vector3D right) => new(
        (left.Y * right.Z) - (left.Z * right.Y),
        (left.Z * right.X) - (left.X * right.Z),
        (left.X * right.Y) - (left.Y * right.X));

    private static double Dot(Vector3D left, Vector3D right) =>
        (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

    private sealed record ValidatedMesh(
        IReadOnlyList<Vector3D> Vertices,
        IReadOnlyList<CadTriangle> Triangles);

    private readonly record struct EdgeKey(int A, int B);

    private readonly record struct EdgeUse(int Count, int DirectionBalance);

    private readonly record struct VertexKey(long X, long Y, long Z);
}
