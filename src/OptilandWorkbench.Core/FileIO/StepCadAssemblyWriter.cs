using System.Globalization;
using System.Text;
using OptilandWorkbench.Core.Backend;

namespace OptilandWorkbench.Core.FileIO;

public sealed record StepCadDocument(
    string Content,
    int PartCount,
    int VertexCount,
    int TriangleCount,
    IReadOnlyList<string> Warnings);

internal static class StepCadAssemblyWriter
{
    public static StepCadDocument Build(
        Optic optic,
        StepCadExportOptions options,
        CancellationToken cancellationToken)
    {
        var meshes = CadLensMeshBuilder.Build(optic, options, cancellationToken);
        var productName = options.ProductName ?? optic.Name;
        var createdUtc = options.CreatedUtc ?? DateTimeOffset.UtcNow;
        var model = new StepModel();
        var applicationContext = model.Add(
            "APPLICATION_CONTEXT('configuration controlled 3d designs of mechanical parts and assemblies')");
        model.Add(
            $"APPLICATION_PROTOCOL_DEFINITION('international standard','config_control_design',1994,#{applicationContext})");
        var productContext = model.Add($"PRODUCT_CONTEXT('',#{applicationContext},'mechanical')");
        var definitionContext = model.Add(
            $"PRODUCT_DEFINITION_CONTEXT('part definition',#{applicationContext},'design')");

        var representationContext = AddRepresentationContext(model);

        var root = AddProductDefinition(
            model,
            "optical-system",
            productName,
            productContext,
            definitionContext);
        var sharedPartOrigin = AddIdentityPlacement(model, "Part origin");
        var pendingParts = new List<PendingPart>(meshes.Parts.Count);

        var totalVertices = 0;
        var totalTriangles = 0;
        for (var partIndex = 0; partIndex < meshes.Parts.Count; partIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mesh = meshes.Parts[partIndex];
            var name = $"Lens S{mesh.FrontSurfaceNumber}-S{mesh.BackSurfaceNumber} {mesh.Material}";
            var brep = AddPlanarBrep(model, mesh, name, cancellationToken);
            var part = AddProductDefinition(
                model,
                $"lens-{partIndex + 1}",
                name,
                productContext,
                definitionContext);
            var assemblyPlacement = AddIdentityPlacement(model, $"Placement of {name}");
            var partRepresentationContext = AddRepresentationContext(model);
            var partRepresentation = model.Add(
                $"ADVANCED_BREP_SHAPE_REPRESENTATION('',(#{sharedPartOrigin},#{brep}),#{partRepresentationContext})");
            model.Add(
                $"SHAPE_DEFINITION_REPRESENTATION(#{part.DefinitionShape},#{partRepresentation})");
            pendingParts.Add(new PendingPart(
                partIndex + 1,
                name,
                part.Definition,
                partRepresentation,
                assemblyPlacement));

            totalVertices += mesh.Vertices.Count;
            totalTriangles += mesh.Triangles.Count;
        }

        var rootItems = new[] { sharedPartOrigin }
            .Concat(pendingParts.Select(part => part.AssemblyPlacement))
            .ToArray();
        var rootRepresentation = model.Add(
            $"SHAPE_REPRESENTATION('',({References(rootItems)}),#{representationContext})");
        model.Add(
            $"SHAPE_DEFINITION_REPRESENTATION(#{root.DefinitionShape},#{rootRepresentation})");
        foreach (var part in pendingParts)
        {
            AddAssemblyOccurrence(
                model,
                part.Index,
                part.Name,
                root.Definition,
                part.Definition,
                rootRepresentation,
                part.Representation,
                part.AssemblyPlacement,
                sharedPartOrigin);
        }

        return new StepCadDocument(
            model.Render(productName, createdUtc),
            meshes.Parts.Count,
            totalVertices,
            totalTriangles,
            meshes.Warnings);
    }

    private static ProductDefinitionIds AddProductDefinition(
        StepModel model,
        string identifier,
        string name,
        int productContext,
        int definitionContext)
    {
        var product = model.Add(
            $"PRODUCT({StepString(identifier)},{StepString(name)},'',(#{productContext}))");
        model.Add(
            $"PRODUCT_RELATED_PRODUCT_CATEGORY('part',$,(#{product}))");
        var formation = model.Add(
            $"PRODUCT_DEFINITION_FORMATION_WITH_SPECIFIED_SOURCE('','',#{product},.MADE.)");
        var definition = model.Add(
            $"PRODUCT_DEFINITION('design','',#{formation},#{definitionContext})");
        var definitionShape = model.Add(
            $"PRODUCT_DEFINITION_SHAPE('','',#{definition})");
        return new ProductDefinitionIds(definition, definitionShape);
    }

    private static void AddAssemblyOccurrence(
        StepModel model,
        int index,
        string name,
        int assemblyDefinition,
        int partDefinition,
        int assemblyRepresentation,
        int partRepresentation,
        int assemblyPlacement,
        int partPlacement)
    {
        var occurrence = model.Add(
            $"NEXT_ASSEMBLY_USAGE_OCCURRENCE({StepString(index.ToString(CultureInfo.InvariantCulture))},"
            + $"{StepString(name)},'',#{assemblyDefinition},#{partDefinition},$)");
        var occurrenceShape = model.Add(
            $"PRODUCT_DEFINITION_SHAPE('Placement',{StepString($"Placement of {name}")},#{occurrence})");
        var transformation = model.Add(
            $"ITEM_DEFINED_TRANSFORMATION('','',#{partPlacement},#{assemblyPlacement})");
        var relationship = model.Add(
            $"(REPRESENTATION_RELATIONSHIP('','',#{partRepresentation},#{assemblyRepresentation}) "
            + $"REPRESENTATION_RELATIONSHIP_WITH_TRANSFORMATION(#{transformation}) "
            + "SHAPE_REPRESENTATION_RELATIONSHIP())");
        model.Add(
            $"CONTEXT_DEPENDENT_SHAPE_REPRESENTATION(#{relationship},#{occurrenceShape})");
    }

    private static int AddRepresentationContext(StepModel model)
    {
        var millimeterUnit = model.Add(
            "(LENGTH_UNIT() NAMED_UNIT(*) SI_UNIT(.MILLI.,.METRE.))");
        var radianUnit = model.Add(
            "(NAMED_UNIT(*) PLANE_ANGLE_UNIT() SI_UNIT($,.RADIAN.))");
        var steradianUnit = model.Add(
            "(NAMED_UNIT(*) SI_UNIT($,.STERADIAN.) SOLID_ANGLE_UNIT())");
        var uncertainty = model.Add(
            $"UNCERTAINTY_MEASURE_WITH_UNIT(LENGTH_MEASURE(1.E-6),#{millimeterUnit},'distance_accuracy_value','model accuracy')");
        return model.Add(
            $"(GEOMETRIC_REPRESENTATION_CONTEXT(3) "
            + $"GLOBAL_UNCERTAINTY_ASSIGNED_CONTEXT((#{uncertainty})) "
            + $"GLOBAL_UNIT_ASSIGNED_CONTEXT((#{millimeterUnit},#{radianUnit},#{steradianUnit})) "
            + "REPRESENTATION_CONTEXT('','3D millimetre context'))");
    }
    private static int AddIdentityPlacement(StepModel model, string name)
    {
        var origin = model.Add("CARTESIAN_POINT('',(0.E+0,0.E+0,0.E+0))");
        var z = model.Add("DIRECTION('',(0.E+0,0.E+0,1.E+0))");
        var x = model.Add("DIRECTION('',(1.E+0,0.E+0,0.E+0))");
        return model.Add(
            $"AXIS2_PLACEMENT_3D({StepString(name)},#{origin},#{z},#{x})");
    }

    private static int AddPlanarBrep(
        StepModel model,
        CadLensMesh mesh,
        string name,
        CancellationToken cancellationToken)
    {
        var pointIds = new int[mesh.Vertices.Count];
        for (var index = 0; index < mesh.Vertices.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var point = mesh.Vertices[index];
            pointIds[index] = model.Add(
                $"CARTESIAN_POINT('',({Number(point.X)},{Number(point.Y)},{Number(point.Z)}))");
        }

        var vertexIds = new int[mesh.Vertices.Count];
        for (var index = 0; index < vertexIds.Length; index++)
        {
            vertexIds[index] = model.Add(
                $"VERTEX_POINT('',#{pointIds[index]})");
        }

        var edgeCurves = new Dictionary<(int A, int B), int>();

        int AddOrientedEdge(int first, int second)
        {
            var start = Math.Min(first, second);
            var end = Math.Max(first, second);
            var key = (start, end);
            if (!edgeCurves.TryGetValue(key, out var edgeCurve))
            {
                var delta = mesh.Vertices[end] - mesh.Vertices[start];
                var length = delta.Length;
                var direction = delta / length;
                var directionId = model.Add(
                    $"DIRECTION('',({Number(direction.X)},{Number(direction.Y)},{Number(direction.Z)}))");
                var vector = model.Add(
                    $"VECTOR('',#{directionId},{Number(length)})");
                var line = model.Add(
                    $"LINE('',#{pointIds[start]},#{vector})");
                edgeCurve = model.Add(
                    $"EDGE_CURVE('',#{vertexIds[start]},#{vertexIds[end]},#{line},.T.)");
                edgeCurves.Add(key, edgeCurve);
            }

            var orientation = first == start ? ".T." : ".F.";
            return model.Add($"ORIENTED_EDGE('',*,*,#{edgeCurve},{orientation})");
        }

        var faceIds = new List<int>(mesh.Triangles.Count);
        foreach (var triangle in mesh.Triangles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var abEdge = AddOrientedEdge(triangle.A, triangle.B);
            var bcEdge = AddOrientedEdge(triangle.B, triangle.C);
            var caEdge = AddOrientedEdge(triangle.C, triangle.A);
            var loop = model.Add(
                $"EDGE_LOOP('',({References(new[] { abEdge, bcEdge, caEdge })}))");
            var bound = model.Add($"FACE_OUTER_BOUND('',#{loop},.T.)");
            var a = mesh.Vertices[triangle.A];
            var b = mesh.Vertices[triangle.B];
            var c = mesh.Vertices[triangle.C];
            var reference = b - a;
            reference /= reference.Length;
            var ab = b - a;
            var ac = c - a;
            var normal = new Vector3D(
                (ab.Y * ac.Z) - (ab.Z * ac.Y),
                (ab.Z * ac.X) - (ab.X * ac.Z),
                (ab.X * ac.Y) - (ab.Y * ac.X));
            normal /= normal.Length;
            var normalDirection = model.Add(
                $"DIRECTION('',({Number(normal.X)},{Number(normal.Y)},{Number(normal.Z)}))");
            var referenceDirection = model.Add(
                $"DIRECTION('',({Number(reference.X)},{Number(reference.Y)},{Number(reference.Z)}))");
            var placement = model.Add(
                $"AXIS2_PLACEMENT_3D('',#{pointIds[triangle.A]},#{normalDirection},#{referenceDirection})");
            var plane = model.Add($"PLANE('',#{placement})");
            faceIds.Add(model.Add($"ADVANCED_FACE('',(#{bound}),#{plane},.T.)"));
        }

        var shell = model.Add($"CLOSED_SHELL('',({References(faceIds, 16)}))");
        return model.Add($"MANIFOLD_SOLID_BREP({StepString(name)},#{shell})");
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
            throw new InvalidOperationException("CAD 几何包含非有限坐标。");
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
            builder.AppendLine(
                "FILE_DESCRIPTION(('Optical lens assembly; faceted B-rep; millimetres'),'2;1');");
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

    private sealed record ProductDefinitionIds(int Definition, int DefinitionShape);

    private sealed record PendingPart(
        int Index,
        string Name,
        int Definition,
        int Representation,
        int AssemblyPlacement);
}
