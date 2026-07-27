using System.Globalization;
using System.Text;
using OptilandWorkbench.Core.Visualization;

namespace OptilandWorkbench.Core.FileIO;

public sealed record StepCadExportOptions(
    int SurfaceSamples = 33,
    int AngularSamples = 64,
    string? ProductName = null,
    DateTimeOffset? CreatedUtc = null);

public static class StepCadExporter
{
    private const double VertexResolutionMillimeters = 1e-9;
    private const double DegenerateTriangleTolerance = 1e-20;

    public static string Serialize(
        Optic optic,
        StepCadExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(optic);
        options ??= new StepCadExportOptions();

        var surfaceSamples = Math.Clamp(options.SurfaceSamples, 9, 129);
        var angularSamples = Math.Clamp(options.AngularSamples, 32, 192);
        var scene = new Layout2DBuilder(optic).Build3D(
            surfaceSamples,
            angularSamples,
            new LayoutBuildOptions(RayCount: 0));
        if (scene.LensElements.Count == 0)
        {
            throw new InvalidOperationException("当前光学系统没有可导出的镜片实体。");
        }

        return SerializeScene(
            scene,
            options.ProductName ?? optic.Name,
            options.CreatedUtc ?? DateTimeOffset.UtcNow,
            cancellationToken);
    }

    internal static string SerializeScene(
        Layout3DScene scene,
        string productName,
        DateTimeOffset createdUtc,
        CancellationToken cancellationToken)
    {
        var model = new StepModel();
        var applicationContext = model.Add(
            "APPLICATION_CONTEXT('configuration controlled 3d designs of mechanical parts and assemblies')");
        model.Add(
            $"APPLICATION_PROTOCOL_DEFINITION('international standard','config_control_design',1994,#{applicationContext})");
        var productContext = model.Add($"PRODUCT_CONTEXT('',#{applicationContext},'mechanical')");
        var product = model.Add(
            $"PRODUCT('optical-system',{StepString(productName)},'',(#{productContext}))");
        var formation = model.Add(
            $"PRODUCT_DEFINITION_FORMATION_WITH_SPECIFIED_SOURCE('','',#{product},.MADE.)");
        var definitionContext = model.Add(
            $"PRODUCT_DEFINITION_CONTEXT('part definition',#{applicationContext},'design')");
        var definition = model.Add(
            $"PRODUCT_DEFINITION('design','',#{formation},#{definitionContext})");
        var definitionShape = model.Add($"PRODUCT_DEFINITION_SHAPE('','',#{definition})");

        var millimeterUnit = model.Add(
            "(LENGTH_UNIT() NAMED_UNIT(*) SI_UNIT(.MILLI.,.METRE.))");
        var radianUnit = model.Add(
            "(NAMED_UNIT(*) PLANE_ANGLE_UNIT() SI_UNIT($,.RADIAN.))");
        var steradianUnit = model.Add(
            "(NAMED_UNIT(*) SI_UNIT($,.STERADIAN.) SOLID_ANGLE_UNIT())");
        var uncertainty = model.Add(
            $"UNCERTAINTY_MEASURE_WITH_UNIT(LENGTH_MEASURE(1.E-6),#{millimeterUnit},'distance_accuracy_value','model accuracy')");
        var representationContext = model.Add(
            $"(GEOMETRIC_REPRESENTATION_CONTEXT(3) " +
            $"GLOBAL_UNCERTAINTY_ASSIGNED_CONTEXT((#{uncertainty})) " +
            $"GLOBAL_UNIT_ASSIGNED_CONTEXT((#{millimeterUnit},#{radianUnit},#{steradianUnit})) " +
            "REPRESENTATION_CONTEXT('','3D millimetre context'))");

        var breps = new List<int>(scene.LensElements.Count);
        foreach (var element in scene.LensElements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            breps.Add(AddFacetedBrep(model, element, cancellationToken));
        }

        var representation = model.Add(
            $"FACETED_BREP_SHAPE_REPRESENTATION('',({References(breps)}),#{representationContext})");
        model.Add($"SHAPE_DEFINITION_REPRESENTATION(#{definitionShape},#{representation})");

        return model.Render(productName, createdUtc);
    }

    private static int AddFacetedBrep(
        StepModel model,
        Layout3DLensElement element,
        CancellationToken cancellationToken)
    {
        var mesh = BuildClosedMesh(element, cancellationToken);
        var pointIds = new int[mesh.Vertices.Count];
        for (var index = 0; index < mesh.Vertices.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var point = mesh.Vertices[index];
            pointIds[index] = model.Add(
                $"CARTESIAN_POINT('',({Number(point.X)},{Number(point.Y)},{Number(point.Z)}))");
        }

        var faceIds = new List<int>(mesh.Triangles.Count);
        foreach (var triangle in mesh.Triangles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var loop = model.Add(
                $"POLY_LOOP('',(#{pointIds[triangle.A]},#{pointIds[triangle.B]},#{pointIds[triangle.C]}))");
            var bound = model.Add($"FACE_OUTER_BOUND('',#{loop},.T.)");
            faceIds.Add(model.Add($"FACE('',(#{bound}))"));
        }

        var shell = model.Add($"CLOSED_SHELL('',({References(faceIds, 16)}))");
        var name = $"Lens S{element.FrontSurfaceNumber}-S{element.BackSurfaceNumber} {element.Material}";
        return model.Add($"FACETED_BREP({StepString(name)},#{shell})");
    }

    private static ClosedTriangleMesh BuildClosedMesh(
        Layout3DLensElement element,
        CancellationToken cancellationToken)
    {
        var builder = new TriangleMeshBuilder(VertexResolutionMillimeters);
        var frontCenter = AverageRim(element.FrontRim);
        var backCenter = AverageRim(element.BackRim);
        var axis = Normalize(Subtract(backCenter, frontCenter));
        if (LengthSquared(axis) <= DegenerateTriangleTolerance)
        {
            axis = new Layout3DPoint(0, 0, 1);
        }

        foreach (var face in element.FrontFaces)
        {
            AddFace(builder, face.Points, axis, desiredAxisSign: -1);
        }

        foreach (var face in element.BackFaces)
        {
            AddFace(builder, face.Points, axis, desiredAxisSign: 1);
        }

        var rimCount = Math.Min(element.FrontRim.Count, element.BackRim.Count) - 1;
        for (var index = 0; index < rimCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<Layout3DPoint> side = new[]
            {
                element.FrontRim[index],
                element.FrontRim[index + 1],
                element.BackRim[index + 1],
                element.BackRim[index]
            };
            AddSideFace(builder, side, frontCenter, axis);
        }

        return builder.BuildAndValidate(
            $"S{element.FrontSurfaceNumber}-S{element.BackSurfaceNumber}");
    }

    private static void AddFace(
        TriangleMeshBuilder builder,
        IReadOnlyList<Layout3DPoint> polygon,
        Layout3DPoint axis,
        int desiredAxisSign)
    {
        if (polygon.Count < 3)
        {
            return;
        }

        for (var index = 1; index < polygon.Count - 1; index++)
        {
            var a = polygon[0];
            var b = polygon[index];
            var c = polygon[index + 1];
            var normal = Cross(Subtract(b, a), Subtract(c, a));
            if (Dot(normal, axis) * desiredAxisSign < 0)
            {
                (b, c) = (c, b);
            }

            builder.AddTriangle(a, b, c);
        }
    }

    private static void AddSideFace(
        TriangleMeshBuilder builder,
        IReadOnlyList<Layout3DPoint> polygon,
        Layout3DPoint frontCenter,
        Layout3DPoint axis)
    {
        if (polygon.Count < 3)
        {
            return;
        }

        for (var index = 1; index < polygon.Count - 1; index++)
        {
            var a = polygon[0];
            var b = polygon[index];
            var c = polygon[index + 1];
            var normal = Cross(Subtract(b, a), Subtract(c, a));
            var centroid = Scale(Add(Add(a, b), c), 1.0 / 3.0);
            var axialDistance = Dot(Subtract(centroid, frontCenter), axis);
            var radial = Subtract(Subtract(centroid, frontCenter), Scale(axis, axialDistance));
            if (Dot(normal, radial) < 0)
            {
                (b, c) = (c, b);
            }

            builder.AddTriangle(a, b, c);
        }
    }

    private static Layout3DPoint AverageRim(IReadOnlyList<Layout3DPoint> rim)
    {
        var count = rim.Count > 1 ? rim.Count - 1 : rim.Count;
        if (count <= 0)
        {
            return new Layout3DPoint(0, 0, 0);
        }

        var sum = new Layout3DPoint(0, 0, 0);
        for (var index = 0; index < count; index++)
        {
            sum = Add(sum, rim[index]);
        }

        return Scale(sum, 1.0 / count);
    }

    private static string References(IReadOnlyList<int> identifiers, int perLine = 12)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < identifiers.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
                if (index % perLine == 0)
                {
                    builder.AppendLine();
                }
            }

            builder.Append('#').Append(identifiers[index]);
        }

        return builder.ToString();
    }

    private static string Number(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new InvalidOperationException("CAD geometry contains a non-finite coordinate.");
        }

        var text = value.ToString("0.###############E+0", CultureInfo.InvariantCulture);
        return text.Contains('.') ? text : text.Replace("E", ".E", StringComparison.Ordinal);
    }

    private static string StepString(string? value)
    {
        value ??= string.Empty;
        var builder = new StringBuilder(value.Length + 2).Append('\'');
        foreach (var character in value)
        {
            if (character == '\'')
            {
                builder.Append("''");
            }
            else if (character is >= ' ' and <= '~')
            {
                builder.Append(character);
            }
            else
            {
                builder
                    .Append(@"\X2\")
                    .Append(((int)character).ToString("X4", CultureInfo.InvariantCulture))
                    .Append(@"\X0\");
            }
        }

        return builder.Append('\'').ToString();
    }

    private static Layout3DPoint Add(Layout3DPoint left, Layout3DPoint right) =>
        new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    private static Layout3DPoint Subtract(Layout3DPoint left, Layout3DPoint right) =>
        new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    private static Layout3DPoint Scale(Layout3DPoint point, double scale) =>
        new(point.X * scale, point.Y * scale, point.Z * scale);

    private static Layout3DPoint Cross(Layout3DPoint left, Layout3DPoint right) =>
        new(
            (left.Y * right.Z) - (left.Z * right.Y),
            (left.Z * right.X) - (left.X * right.Z),
            (left.X * right.Y) - (left.Y * right.X));

    private static double Dot(Layout3DPoint left, Layout3DPoint right) =>
        (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

    private static double LengthSquared(Layout3DPoint point) => Dot(point, point);

    private static Layout3DPoint Normalize(Layout3DPoint point)
    {
        var length = Math.Sqrt(LengthSquared(point));
        return length <= 1e-15 ? new Layout3DPoint(0, 0, 0) : Scale(point, 1.0 / length);
    }

    private sealed class StepModel
    {
        private readonly List<string> _entities = new();

        public int Add(string entity)
        {
            _entities.Add(entity);
            return _entities.Count;
        }

        public string Render(string productName, DateTimeOffset createdUtc)
        {
            var builder = new StringBuilder(Math.Max(4096, _entities.Count * 80));
            builder.AppendLine("ISO-10303-21;");
            builder.AppendLine("HEADER;");
            builder.AppendLine("FILE_DESCRIPTION(('Optical lens solids; faceted B-rep; millimetres'),'2;1');");
            builder
                .Append("FILE_NAME(")
                .Append(StepString($"{productName}.step"))
                .Append(',')
                .Append(StepString(createdUtc.UtcDateTime.ToString(
                    "yyyy-MM-ddTHH:mm:ss'Z'",
                    CultureInfo.InvariantCulture)))
                .AppendLine(",('S.T.A.R. Labs'),('S.T.A.R. Labs'),'Optical System Design','OptilandWorkbench','');");
            builder.AppendLine("FILE_SCHEMA(('CONFIG_CONTROL_DESIGN'));");
            builder.AppendLine("ENDSEC;");
            builder.AppendLine("DATA;");
            for (var index = 0; index < _entities.Count; index++)
            {
                builder
                    .Append('#')
                    .Append(index + 1)
                    .Append(" = ")
                    .Append(_entities[index])
                    .AppendLine(";");
            }

            builder.AppendLine("ENDSEC;");
            builder.AppendLine("END-ISO-10303-21;");
            return builder.ToString();
        }
    }

    private sealed class TriangleMeshBuilder
    {
        private readonly double _resolution;
        private readonly Dictionary<VertexKey, int> _vertexLookup = new();
        private readonly List<Layout3DPoint> _vertices = new();
        private readonly List<Triangle> _triangles = new();

        public TriangleMeshBuilder(double resolution)
        {
            _resolution = resolution;
        }

        public void AddTriangle(Layout3DPoint a, Layout3DPoint b, Layout3DPoint c)
        {
            var normal = Cross(Subtract(b, a), Subtract(c, a));
            if (LengthSquared(normal) <= DegenerateTriangleTolerance)
            {
                return;
            }

            var ia = Vertex(a);
            var ib = Vertex(b);
            var ic = Vertex(c);
            if (ia == ib || ib == ic || ic == ia)
            {
                return;
            }

            _triangles.Add(new Triangle(ia, ib, ic));
        }

        public ClosedTriangleMesh BuildAndValidate(string label)
        {
            if (_triangles.Count < 4)
            {
                throw new InvalidOperationException($"镜片 {label} 无法形成有效 CAD 实体。");
            }

            var edgeUse = new Dictionary<EdgeKey, EdgeUse>();
            foreach (var triangle in _triangles)
            {
                CountEdge(edgeUse, triangle.A, triangle.B);
                CountEdge(edgeUse, triangle.B, triangle.C);
                CountEdge(edgeUse, triangle.C, triangle.A);
            }

            var invalidEdgeCount = edgeUse.Count(pair =>
                pair.Value.Count != 2 || pair.Value.DirectionBalance != 0);
            if (invalidEdgeCount > 0)
            {
                throw new InvalidOperationException(
                    $"镜片 {label} 的网格不是闭合实体（{invalidEdgeCount} 条边未闭合）。");
            }

            var signedVolume = 0.0;
            foreach (var triangle in _triangles)
            {
                var a = _vertices[triangle.A];
                var b = _vertices[triangle.B];
                var c = _vertices[triangle.C];
                signedVolume += Dot(a, Cross(b, c)) / 6.0;
            }

            if (!double.IsFinite(signedVolume) || Math.Abs(signedVolume) <= 1e-12)
            {
                throw new InvalidOperationException($"镜片 {label} 的 CAD 实体体积为零。");
            }

            return new ClosedTriangleMesh(_vertices.ToArray(), _triangles.ToArray());
        }

        private int Vertex(Layout3DPoint point)
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

        private long Quantize(double value)
        {
            if (!double.IsFinite(value))
            {
                throw new InvalidOperationException("CAD geometry contains a non-finite coordinate.");
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
            var key = first < second
                ? new EdgeKey(first, second)
                : new EdgeKey(second, first);
            var direction = first < second ? 1 : -1;
            var current = edges.TryGetValue(key, out var use) ? use : default;
            edges[key] = new EdgeUse(
                current.Count + 1,
                current.DirectionBalance + direction);
        }
    }

    private sealed record ClosedTriangleMesh(
        IReadOnlyList<Layout3DPoint> Vertices,
        IReadOnlyList<Triangle> Triangles);

    private readonly record struct Triangle(int A, int B, int C);

    private readonly record struct EdgeKey(int A, int B);

    private readonly record struct EdgeUse(int Count, int DirectionBalance);

    private readonly record struct VertexKey(long X, long Y, long Z);
}
