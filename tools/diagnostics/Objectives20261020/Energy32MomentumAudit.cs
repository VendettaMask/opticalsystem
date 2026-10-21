using System.Globalization;
using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Coordinates;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;

// Causal control for retaining optical momentum across interfaces. Ray creation,
// intersections and propagation remain the formal Core's scalar trace.
internal static class Energy32MomentumAudit
{
    public static void Run(string output)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required.");
        Directory.CreateDirectory(output);
        using var items = JsonDocument.Parse(File.ReadAllText("artifacts/validation/objectives-20261020/final-v7-intersection-fallback/items.json"));
        using var controls = JsonDocument.Parse(File.ReadAllText("artifacts/validation/objectives32-repair-20261021/coordinate-audit-v7/inputs.json"));
        var rows = new List<object>();
        foreach (var control in controls.RootElement.EnumerateArray())
        {
            var lens = control.GetProperty("lens").GetString()!;
            var id = Path.GetFileNameWithoutExtension(lens).Replace(' ', '-').ToLowerInvariant();
            var item = items.RootElement.EnumerateArray().Single(i => i.GetProperty("lens").GetString() == lens && i.GetProperty("run").GetProperty("key").GetString() == "Diffraction Encircled Energy");
            var request = item.GetProperty("run").GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
            var source = File.ReadAllLines(Path.Combine("artifacts/validation/objectives32-repair-20261021/marginals-v1", id, "source.zmx"));
            var scale = source.Single(l => l.StartsWith("UNIT ", StringComparison.Ordinal)).Split(' ', StringSplitOptions.RemoveEmptyEntries)[1] == "CM" ? 10 : 1d;
            var cs = new Dictionary<int, double>(); var number = 0;
            foreach (var line in source)
            {
                var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 2) continue;
                if (p[0] == "SURF") number = int.Parse(p[1], CultureInfo.InvariantCulture);
                if (p[0] == "CURV") cs[number] = double.Parse(p[1], CultureInfo.InvariantCulture) / scale;
            }
            foreach (var normalMode in new[] { "existing", "implicit-radius", "implicit-source" })
            foreach (var root in new[] { "expanded", "product", "norm" })
            foreach (var retain in new[] { false, true })
            {
                var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(item.GetProperty("snapshot").GetString()!));
                var wave = optic.Wavelengths[request.Wavelength - 1];
                (double X, double Y)[] points = [(0, 0), (0, 1), (0, -1), (1, 0), (-1, 0)];
                var launches = points.Select(p => optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, p.X, p.Y,
                    wave.Micrometers, optic.RayAimingEnabled).Rays[0]).ToArray();
                var state = new State();
                foreach (var (s, i) in optic.SurfaceGroup.Items.Select((s, i) => (s, i)))
                    if (s.InteractionModel is RefractiveReflectiveInteractionModel original)
                        s.InteractionModel = new Interaction(original, state, s.CoordinateSystem, cs.GetValueOrDefault(i), normalMode, root, retain);
                var trace = optic.SequentialRayTracer.GetType().GetMethod("TraceToSurface", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var directions = launches.Select(launch =>
                {
                    state.Momentum = null;
                    return ((RayTraceSample?)trace.Invoke(optic.SequentialRayTracer, [launch, optic.SurfaceGroup.Items.Count - 1]))?.Direction
                        ?? throw new InvalidOperationException("Ray missing.");
                }).ToArray();
                var chief = directions[0];
                var f = .5 / Math.Sqrt(directions.Skip(1).Average(d => 1 - Math.Pow(chief.X * d.X + chief.Y * d.Y + chief.Z * d.Z, 2)));
                var nativeF = control.GetProperty("nativeF").GetDouble();
                var maxN = directions.Select((d, i) => Math.Abs(d.Z - control.GetProperty("native")[i].GetProperty("z").GetDouble())).Max();
                rows.Add(new { lens, normalMode, root, retain, f, nativeF, fError = f - nativeF, maxN });
            }
        }
        JsonFiles.Write(Path.Combine(output, "rows.json"), rows);
    }
    private sealed class State { public Vector3D? Momentum; }
    private sealed class Interaction(RefractiveReflectiveInteractionModel original, State state, CoordinateSystem frame, double sourceCurvature, string normalMode, string rootMode, bool retain) : IInteractionModel
    {
        public string Kind => original.Kind;
        public IInteractionModel Clone() => throw new NotSupportedException("Disposable diagnostic only.");
        public ParaxialRay Interact(ParaxialRay ray, SurfaceInteractionContext context) => original.Interact(ray, context);
        public RealRayInteractionResult Interact(RealRay ray, SurfaceInteractionContext context)
        {
            var n = context.SurfaceNormal;
            if (normalMode != "existing" && context.Geometry is StandardGeometry g && double.IsFinite(g.Radius) && g.Radius != 0)
            {
                var c = normalMode == "implicit-source" ? sourceCurvature : 1 / g.Radius;
                n = new Vector3D(-c * ray.Origin.X, -c * ray.Origin.Y, 1 - (1 + g.Conic) * c * ray.Origin.Z);
                if (g.Conic != 0) n /= n.Length;
            }
            var n1 = context.RefractiveIndexBefore; var n2 = context.RefractiveIndexAfter;
            var p = retain && state.Momentum is { } stored ? frame.ToLocalDirection(stored) : n1 * ray.Direction;
            var dot = p.X * n.X + p.Y * n.Y + p.Z * n.Z;
            if (dot > 0) { n = -n; dot = -dot; }
            var kind = RayInteractionKind.Transmitted;
            if (original.IsReflective || context.IsReflective)
            {
                p -= 2 * dot * n;
                n2 = n1;
                kind = RayInteractionKind.Reflected;
            }
            else if (n1 != n2)
            {
                var square = rootMode switch
                {
                    "product" => n2 * n2 - n1 * n1 * (1 - (dot / n1) * (dot / n1)),
                    "norm" => n2 * n2 - (p.X * p.X + p.Y * p.Y + p.Z * p.Z) + dot * dot,
                    _ => n2 * n2 - n1 * n1 + dot * dot
                };
                if (square < 0) throw new InvalidOperationException("Unexpected diagnostic TIR.");
                p += (-dot - Math.Sqrt(square)) * n;
            }
            state.Momentum = frame.ToGlobalDirection(p);
            return new(ray with { Direction = p / n2, IsNormalized = true }, kind);
        }
    }
}
