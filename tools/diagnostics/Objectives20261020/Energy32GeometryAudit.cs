using System.Globalization;
using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;

// Disposable geometry experiments retain the formal Core hit verification and
// tracing. Launch rays are generated before wrapping geometry, so paraxial
// type dispatch cannot alter the input rays during arithmetic isolation.
internal static class Energy32GeometryAudit
{
    public static void Run(string output, bool vertexPlane = false, bool activeParameters = false, bool nativeIndices = false, bool discriminant = false)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required.");
        Directory.CreateDirectory(output);
        using var items = JsonDocument.Parse(File.ReadAllText("artifacts/validation/objectives-20261020/final-v7-intersection-fallback/items.json"));
        using var controls = JsonDocument.Parse(File.ReadAllText("artifacts/validation/objectives32-repair-20261021/coordinate-audit-v4/inputs.json"));
        var rows = new List<object>();
        foreach (var control in controls.RootElement.EnumerateArray())
        {
            var lens = control.GetProperty("lens").GetString()!;
            var id = Path.GetFileNameWithoutExtension(lens).Replace(' ', '-').ToLowerInvariant();
            var item = items.RootElement.EnumerateArray().Single(i => i.GetProperty("lens").GetString() == lens
                && i.GetProperty("run").GetProperty("key").GetString() == "Diffraction Encircled Energy");
            var request = item.GetProperty("run").GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
            var sourceLines = File.ReadAllLines(Path.Combine("artifacts/validation/objectives32-repair-20261021/marginals-v1", id, "source.zmx"));
            var unit = sourceLines.Single(line => line.StartsWith("UNIT ", StringComparison.Ordinal)).Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
            var factor = unit == "CM" ? 10 : unit == "IN" ? 25.4 : 1;
            var cs = new Dictionary<int, double>(); var solved = new HashSet<int>(); var number = -1;
            foreach (var line in sourceLines)
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                if (parts[0] == "SURF") number = int.Parse(parts[1], CultureInfo.InvariantCulture);
                if (parts[0] == "CURV")
                {
                    cs[number] = double.Parse(parts[1], CultureInfo.InvariantCulture) / factor;
                    if (parts.Length > 2 && parts[2] == "11") solved.Add(number);
                }
            }
            using var nativeModel = JsonDocument.Parse(File.ReadAllText(Path.Combine(item.GetProperty("path").GetString()!, "native/model.json")));
            using var active = activeParameters ? JsonDocument.Parse(File.ReadAllText(Path.Combine(
                "artifacts/validation/objectives32-continuation-20261021/native-active-parameters-v2/capture", id, "refractive-indices.json"))) : null;
            using var nativeLaunches = activeParameters ? JsonDocument.Parse(File.ReadAllText(Path.Combine(
                "artifacts/validation/objectives32-continuation-20261021/native-active-parameters-v2/capture", id, "ray-audit.json"))) : null;
            foreach (var root in vertexPlane ? new[] { "vertex-r", "vertex-c", "vertex-scaled", "vertex-r-ratio" } : new[] { "existing", "stable-r", "stable-c", "unit-c" })
            foreach (var normal in vertexPlane ? new[] { "existing", "implicit", "sdk" } : new[] { "existing", "implicit" })
            foreach (var sourceCurvature in activeParameters ? new[] { "native-cvva" } : new[] { "radius", "source", "native-active" })
            foreach (var normalize in new[] { false, true })
            {
                var audit = new List<object>();
                var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(item.GetProperty("snapshot").GetString()!));
                var wave = optic.Wavelengths[request.Wavelength - 1];
                (double X, double Y)[] points = [(0, 0), (0, 1), (0, -1), (1, 0), (-1, 0)];
                var launch = points.Select(p => optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, p.X, p.Y,
                    wave.Micrometers, optic.RayAimingEnabled).Rays[0]).ToArray();
                if (nativeLaunches is not null)
                {
                    var frame = double.IsPositiveInfinity(optic.SurfaceGroup.Items[0].Thickness)
                        ? optic.SurfaceGroup.Items[1].CoordinateSystem : optic.SurfaceGroup.Items[0].CoordinateSystem;
                    launch = launch.Select((r, i) =>
                    {
                        var n = nativeLaunches.RootElement.GetProperty("launches")[i];
                        return r with
                        {
                            Origin = frame.ToGlobalPoint(new Vector3D(n.GetProperty("x").GetDouble(), n.GetProperty("y").GetDouble(), n.GetProperty("z").GetDouble())),
                            Direction = frame.ToGlobalDirection(new Vector3D(n.GetProperty("l").GetDouble(), n.GetProperty("m").GetDouble(), n.GetProperty("n").GetDouble())),
                            IsNormalized = true
                        };
                    }).ToArray();
                }
                foreach (var (surface, index) in optic.SurfaceGroup.Items.Select((s, i) => (s, i)))
                {
                    if (surface.Geometry is StandardGeometry g && double.IsFinite(g.Radius) && Math.Abs(g.Radius) > 1e-12)
                    {
                        var c = sourceCurvature == "radius" ? 1 / g.Radius : cs[index];
                        if (sourceCurvature == "native-cvva")
                        {
                            c = active!.RootElement[index].GetProperty("activeCurvature").GetDouble();
                            g.Radius = 1 / c;
                        }
                        if (sourceCurvature == "native-active" && (solved.Contains(index) || factor != 1))
                        {
                            // External active geometry is a causal control only.
                            // It must never be used by a product implementation.
                            g.Radius = nativeModel.RootElement.GetProperty("surfaces")[index].GetProperty("Radius").GetDouble();
                            c = 1 / g.Radius;
                        }
                        surface.Geometry = new Geometry(g, root, normal, c);
                    }
                    if (surface.InteractionModel is RefractiveReflectiveInteractionModel original)
                        surface.InteractionModel = discriminant
                            ? new DiscriminantVariant(original, normalize,
                                nativeIndices && index > 0 ? Math.Abs(active!.RootElement[index - 1].GetProperty("indices")[request.Wavelength - 1].GetDouble()) : null,
                                nativeIndices && index > 0 ? Math.Abs(active!.RootElement[index].GetProperty("indices")[request.Wavelength - 1].GetDouble()) : null,
                                root == "vertex-scaled" && normal == "implicit" && !normalize ? audit : null, index)
                            : new Energy32SnellAudit.Variant(original, "existing", "existing", normalize, false,
                            nativeIndices && index > 0 ? Math.Abs(active!.RootElement[index - 1].GetProperty("indices")[request.Wavelength - 1].GetDouble()) : null,
                            nativeIndices && index > 0 ? Math.Abs(active!.RootElement[index].GetProperty("indices")[request.Wavelength - 1].GetDouble()) : null, 0);
                }
                var trace = optic.SequentialRayTracer.GetType().GetMethod("TraceToSurface", BindingFlags.NonPublic | BindingFlags.Instance)!;
                var directions = launch.Select(ray => ((RayTraceSample?)trace.Invoke(optic.SequentialRayTracer,
                    [ray, optic.SurfaceGroup.Items.Count - 1]))?.Direction ?? throw new InvalidOperationException($"Marginal missing: {lens}/{root}/{normal}/{sourceCurvature}/{normalize}")).ToArray();
                var chief = directions[0];
                var f = .5 / Math.Sqrt(directions.Skip(1).Select(d => 1 - Math.Pow(chief.X * d.X + chief.Y * d.Y + chief.Z * d.Z, 2)).Average());
                var nativeF = control.GetProperty("nativeF").GetDouble();
                var max = directions.Select((d, i) => Math.Abs(d.Z - control.GetProperty("native")[i].GetProperty("z").GetDouble())).Max();
                rows.Add(new { lens, root, normal, sourceCurvature, normalize, nativeIndices, nativeLaunch = nativeLaunches is not null, f, nativeF, fError = f - nativeF, maxNError = max, audit });
            }
        }
        JsonFiles.Write(Path.Combine(output, "rows.json"), rows);
    }

    private sealed class DiscriminantVariant(RefractiveReflectiveInteractionModel original, bool normalize, double? before, double? after, List<object>? audit, int index) : IInteractionModel
    {
        public string Kind => original.Kind;
        public IInteractionModel Clone() => new DiscriminantVariant((RefractiveReflectiveInteractionModel)original.Clone(), normalize, before, after, audit, index);
        public ParaxialRay Interact(ParaxialRay ray, SurfaceInteractionContext context) => original.Interact(ray, context);
        public RealRayInteractionResult Interact(RealRay ray, SurfaceInteractionContext context)
        {
            var n1 = before ?? context.RefractiveIndexBefore; var n2 = after ?? context.RefractiveIndexAfter;
            if (original.IsReflective || context.IsReflective) return original.Interact(ray, context);
            var n = context.SurfaceNormal; var d = ray.Direction;
            var plane = context.Geometry is PlaneGeometry or StandardGeometry { Radius: double.PositiveInfinity };
            if (context.Geometry is not Geometry && !plane) return original.Interact(ray, context);
            if (d.X * n.X + d.Y * n.Y + d.Z * n.Z > 0) n = -n;
            var cosSquared = context.Geometry is Geometry g ? g.IncidenceCosineSquared : d.Z * d.Z;
            var reflected = original.IsReflective || context.IsReflective;
            if (!reflected && n1 == n2) return new(ray, RayInteractionKind.Transmitted);
            var eta = n1 / n2;
            var direction = reflected ? d + (2 * Math.Sqrt(cosSquared)) * n
                : eta * d + (eta * Math.Sqrt(cosSquared) - Math.Sqrt(1 - eta * eta * (1 - cosSquared))) * n;
            if (normalize) direction /= direction.Length;
            audit?.Add(new { surface = index, hit = new { ray.Origin.X, ray.Origin.Y, ray.Origin.Z }, incoming = new { d.X, d.Y, d.Z }, cosSquared,
                outgoing = new { direction.X, direction.Y, direction.Z } });
            return new(ray with { Direction = direction }, reflected ? RayInteractionKind.Reflected : RayInteractionKind.Transmitted);
        }
    }

    private sealed class Geometry(StandardGeometry original, string rootMode, string normalMode, double curvature) : IGeometry
    {
        public double IncidenceCosineSquared { get; private set; }
        public Vector3D Vertex { get; private set; }
        private static readonly MethodInfo Verify = typeof(StandardGeometry).Assembly.GetType("OpticalSystemDesign.Core.Geometries.GeometryIntersectionSolver")!
            .GetMethod("CreateVerifiedResult", BindingFlags.Static | BindingFlags.Public)!;
        public string Kind => original.Kind;
        public IGeometry Clone() => new Geometry((StandardGeometry)original.Clone(), rootMode, normalMode, curvature);
        public double Sag(double x, double y) => curvature * (x * x + y * y)
            / (1 + Math.Sqrt(1 - (1 + original.Conic) * curvature * curvature * (x * x + y * y)));
        public Vector3D SurfaceNormal(Vector3D point)
        {
            if (normalMode == "existing") return original.SurfaceNormal(point);
            if (normalMode == "sdk")
            {
                var zc = point.Z * curvature;
                var factor = curvature / Math.Sqrt(zc * original.Conic * (zc * (original.Conic + 1) - 2) + 1);
                return new Vector3D(point.X * factor, point.Y * factor,
                    (point.Z - (1 / curvature - point.Z * original.Conic)) * factor);
            }
            var n = new Vector3D(-curvature * point.X, -curvature * point.Y, 1 - (1 + original.Conic) * curvature * point.Z)
                * Math.CopySign(1, original.Radius);
            return original.Conic == 0 ? n : n / n.Length;
        }
        public IntersectionResult DistanceToIntersection(Vector3D o, Vector3D d)
        {
            if (rootMode == "existing") return original.DistanceToIntersection(o, d);
            if (rootMode.StartsWith("vertex", StringComparison.Ordinal))
            {
                if (d.Z == 0) return original.DistanceToIntersection(o, d);
                var toPlane = -o.Z / d.Z;
                var p = rootMode.EndsWith("ratio", StringComparison.Ordinal)
                    ? new Vector3D(o.X - o.Z * (d.X / d.Z), o.Y - o.Z * (d.Y / d.Z), 0)
                    : new Vector3D(o.X + d.X * toPlane, o.Y + d.Y * toPlane, 0);
                var av = 1 + original.Conic * d.Z * d.Z;
                Vertex = p;
                var bv = d.Z / curvature - p.X * d.X - p.Y * d.Y;
                var cv = p.X * p.X + p.Y * p.Y;
                var bs = d.Z - curvature * (p.X * d.X + p.Y * d.Y);
                IncidenceCosineSquared = bs * bs - curvature * curvature * cv;
                if (rootMode == "vertex-c") { av *= curvature; bv *= curvature; cv *= curvature; }
                if (rootMode == "vertex-scaled") { av *= curvature; bv = bs; cv *= curvature; }
                var radical = rootMode == "vertex-scaled" && original.Conic == 0 ? IncidenceCosineSquared : bv * bv - av * cv;
                if (radical < 0) return IntersectionResult.Failure(IntersectionStatus.NoRoot);
                var sagDistance = cv / (bv + Math.CopySign(Math.Sqrt(radical), bv));
                var hit = p + d * sagDistance;
                var total = toPlane + sagDistance;
                if (total < 0) return IntersectionResult.Failure(IntersectionStatus.NoRoot);
                var verified = (IntersectionResult)Verify.Invoke(null,
                    [hit, d, 0d, (Func<double, double, double>)Sag, (Func<Vector3D, Vector3D>)SurfaceNormal, 0, null])!;
                return verified with { Distance = total };
            }
            var k = 1 + original.Conic;
            var a = rootMode == "unit-c" ? curvature * (1 + original.Conic * d.Z * d.Z)
                : curvature * (d.X * d.X + d.Y * d.Y + k * d.Z * d.Z);
            var b = curvature * (o.X * d.X + o.Y * d.Y + k * o.Z * d.Z) - d.Z;
            var c = curvature * (o.X * o.X + o.Y * o.Y + k * o.Z * o.Z) - 2 * o.Z;
            if (rootMode == "stable-r")
            {
                a = d.X * d.X + d.Y * d.Y + k * d.Z * d.Z;
                b = o.X * d.X + o.Y * d.Y + k * o.Z * d.Z - original.Radius * d.Z;
                c = o.X * o.X + o.Y * o.Y + k * o.Z * o.Z - 2 * original.Radius * o.Z;
            }
            if (a == 0) return Validate(-c / (2 * b));
            var discriminant = b * b - a * c;
            if (discriminant < 0) return IntersectionResult.Failure(IntersectionStatus.NoRoot);
            var q = -(b + Math.CopySign(Math.Sqrt(discriminant), b));
            var candidates = new[] { q / a, c / q }.Where(t => double.IsFinite(t) && t >= 0).Order().ToArray();
            foreach (var t in candidates)
            {
                var hit = Validate(t);
                if (hit.IsHit) return hit;
            }
            return IntersectionResult.Failure(IntersectionStatus.NoRoot);
            IntersectionResult Validate(double t) => (IntersectionResult)Verify.Invoke(null,
                [o, d, t, (Func<double, double, double>)Sag, (Func<Vector3D, Vector3D>)SurfaceNormal, 0, null])!;
        }
    }
}
