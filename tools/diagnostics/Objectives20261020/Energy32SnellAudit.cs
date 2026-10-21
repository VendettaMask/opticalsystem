using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;

// Isolate equivalent Snell arithmetic on disposable optics through formal Core
// tracing. This is an offline numerical experiment, not a selectable engine.
internal static class Energy32SnellAudit
{
    public static void Run(string output)
    {
        if (Directory.Exists(output)) throw new IOException("Fresh output required.");
        Directory.CreateDirectory(output);
        using var items = JsonDocument.Parse(File.ReadAllText("artifacts/validation/objectives-20261020/final-v7-intersection-fallback/items.json"));
        using var controls = JsonDocument.Parse(File.ReadAllText("artifacts/validation/objectives32-repair-20261021/coordinate-audit-v4/inputs.json"));
        var rows = new List<Row>();
        foreach (var control in controls.RootElement.EnumerateArray())
        {
            var lens = control.GetProperty("lens").GetString()!;
            var item = items.RootElement.EnumerateArray().Single(i => i.GetProperty("lens").GetString() == lens
                && i.GetProperty("run").GetProperty("key").GetString() == "Diffraction Encircled Energy");
            var request = item.GetProperty("run").GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
            var id = Path.GetFileNameWithoutExtension(lens).Replace(' ', '-').ToLowerInvariant();
            using var nativeIndices = JsonDocument.Parse(File.ReadAllText(Path.Combine("artifacts/validation/objectives32-repair-20261021/marginals-v1/native/capture", id, "refractive-indices.json")));
            var sourceLines = File.ReadAllLines(Path.Combine("artifacts/validation/objectives32-repair-20261021/marginals-v1", id, "source.zmx"));
            var unit = sourceLines.Single(line => line.StartsWith("UNIT ", StringComparison.Ordinal)).Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
            var unitFactor = unit == "CM" ? 10 : unit == "IN" ? 25.4 : 1;
            var sourceCurvatures = new Dictionary<int, double>();
            var sourceSurface = -1;
            foreach (var line in sourceLines)
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                if (parts[0] == "SURF") sourceSurface = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                if (parts[0] == "CURV") sourceCurvatures[sourceSurface] = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) / unitFactor;
            }
            foreach (var capturedIndices in new[] { false, true })
            foreach (var normalizeIncoming in new[] { false, true })
            foreach (var normal in new[] { "existing", "curvature-sqrt", "radius-sqrt", "curvature-gradient", "source-sqrt", "source-gradient", "curvature-implicit", "source-implicit", "radius-implicit" })
            foreach (var snell in new[] { "existing", "scaled-index", "scaled-expanded", "ratio-expanded", "ratio-cross" })
            foreach (var normalize in new[] { false, true })
            {
                var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(item.GetProperty("snapshot").GetString()!));
                foreach (var (s, i) in optic.SurfaceGroup.Items.Select((s, i) => (s, i)))
                    if (s.InteractionModel is RefractiveReflectiveInteractionModel { IsReflective: false } original)
                        s.InteractionModel = new Variant(original, normal, snell, normalize, normalizeIncoming,
                            capturedIndices && i > 0 ? nativeIndices.RootElement[i - 1].GetProperty("indices")[request.Wavelength - 1].GetDouble() : null,
                            capturedIndices && i > 0 ? nativeIndices.RootElement[i].GetProperty("indices")[request.Wavelength - 1].GetDouble() : null,
                            sourceCurvatures.GetValueOrDefault(i));
                var wave = optic.Wavelengths[request.Wavelength - 1];
                var trace = optic.SequentialRayTracer.GetType().GetMethod("TraceToSurface", BindingFlags.NonPublic | BindingFlags.Instance)!;
                (double X, double Y)[] points = [(0, 0), (0, 1), (0, -1), (1, 0), (-1, 0)];
                var directions = points.Select(p =>
                {
                    var ray = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, p.X, p.Y, wave.Micrometers, optic.RayAimingEnabled).Rays[0];
                    return ((RayTraceSample?)trace.Invoke(optic.SequentialRayTracer, [ray, optic.SurfaceGroup.Items.Count - 1]))?.Direction
                        ?? throw new InvalidOperationException("Marginal unavailable.");
                }).ToArray();
                var chief = directions[0];
                var f = .5 / Math.Sqrt(directions.Skip(1).Select(d => 1 - Math.Pow(chief.X * d.X + chief.Y * d.Y + chief.Z * d.Z, 2)).Average());
                var nativeF = control.GetProperty("nativeF").GetDouble();
                var nativeDirections = control.GetProperty("native").EnumerateArray().ToArray();
                var max = directions.Select((d, i) => Math.Abs(d.Z - nativeDirections[i].GetProperty("z").GetDouble())).Max();
                var step = 1 / (32 * (wave.Micrometers * .001 / Math.Sqrt(32) * f) / (wave.Micrometers * .001) / f);
                rows.Add(new(lens, normal, snell, normalize, normalizeIncoming, capturedIndices, f, nativeF, f - nativeF, max,
                    2 * Math.Pow(4 * step, 2) <= 1 ? 101 : 97, control.GetProperty("expected").GetInt32()));
            }
        }
        JsonFiles.Write(Path.Combine(output, "rows.json"), rows);
        var ranked = rows.GroupBy(r => (r.Normal, r.Snell, r.Normalize, r.NormalizeIncoming, r.CapturedIndices)).Select(g => new
        {
            g.Key.Normal, g.Key.Snell, g.Key.Normalize, g.Key.NormalizeIncoming, g.Key.CapturedIndices,
            exactF = g.Count(r => r.F == r.NativeF), supportMatches = g.Count(r => r.ActualCount == r.ExpectedCount),
            maxFError = g.Max(r => Math.Abs(r.FError)), maxNError = g.Max(r => r.MaxNError), rows = g.ToArray()
        }).OrderByDescending(g => g.supportMatches).ThenByDescending(g => g.exactF).ToArray();
        JsonFiles.Write(Path.Combine(output, "methods.json"), ranked);
        foreach (var r in ranked.Take(12)) Console.WriteLine($"{r.supportMatches}/10 support; {r.exactF}/10 exact F; {r.Normal}/{r.Snell}/normalize={r.Normalize}; incoming={r.NormalizeIncoming}; native indices={r.CapturedIndices}; N max={r.maxNError:G4}");
    }

    private sealed record Row(string Lens, string Normal, string Snell, bool Normalize, bool NormalizeIncoming, bool CapturedIndices, double F, double NativeF,
        double FError, double MaxNError, int ActualCount, int ExpectedCount);

    internal sealed class Variant(RefractiveReflectiveInteractionModel original, string normalMode, string snellMode, bool normalize,
        bool normalizeIncoming, double? before, double? after, double sourceCurvature) : IInteractionModel
    {
        public string Kind => original.Kind;
        public IInteractionModel Clone() => new Variant((RefractiveReflectiveInteractionModel)original.Clone(), normalMode, snellMode, normalize, normalizeIncoming, before, after, sourceCurvature);
        public ParaxialRay Interact(ParaxialRay ray, SurfaceInteractionContext context) => original.Interact(ray, context);
        public RealRayInteractionResult Interact(RealRay ray, SurfaceInteractionContext context)
        {
            var n = context.SurfaceNormal;
            if (normalMode != "existing" && context.Geometry is StandardGeometry g && double.IsFinite(g.Radius) && Math.Abs(g.Radius) > 1e-12)
            {
                var c = normalMode.StartsWith("source", StringComparison.Ordinal) ? sourceCurvature : 1 / g.Radius;
                var x = normalMode.StartsWith("radius", StringComparison.Ordinal) ? -ray.Origin.X / g.Radius : -c * ray.Origin.X;
                var y = normalMode.StartsWith("radius", StringComparison.Ordinal) ? -ray.Origin.Y / g.Radius : -c * ray.Origin.Y;
                var z = normalMode == "radius-implicit" ? (g.Radius - (1 + g.Conic) * ray.Origin.Z) / g.Radius
                    : normalMode.EndsWith("implicit", StringComparison.Ordinal) || normalMode.EndsWith("gradient", StringComparison.Ordinal) ? 1 - (1 + g.Conic) * c * ray.Origin.Z
                    : Math.Sqrt(1 - (1 + g.Conic) * (x * x + y * y));
                n = new Vector3D(x, y, z) * Math.CopySign(1, g.Radius);
                if (g.Conic != 0 || normalMode.EndsWith("gradient", StringComparison.Ordinal)) n /= n.Length;
            }
            var d = ray.Direction;
            var n1 = before ?? context.RefractiveIndexBefore; var n2 = after ?? context.RefractiveIndexAfter;
            if (original.IsReflective || context.IsReflective)
            {
                var reflected = d - 2 * (d.X * n.X + d.Y * n.Y + d.Z * n.Z) * n;
                if (normalize) reflected /= reflected.Length;
                return new(ray with { Direction = reflected }, RayInteractionKind.Reflected);
            }
            if (n1 == n2) return new(ray, RayInteractionKind.Transmitted);
            if (normalizeIncoming) d /= d.Length;
            if (d.X * n.X + d.Y * n.Y + d.Z * n.Z > 0) n = -n;
            var cos = -(d.X * n.X + d.Y * n.Y + d.Z * n.Z);
            var eta = n1 / n2;
            Vector3D outgoing;
            if (snellMode == "existing")
                outgoing = (((Vector3D Direction, RayInteractionKind Kind))typeof(RefractiveReflectiveInteractionModel)
                    .GetMethod("Refract", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [d, n, n1, n2])!).Direction;
            else if (snellMode == "scaled-index")
                outgoing = (n1 * d + (n1 * cos - Math.Sqrt(n2 * n2 - n1 * n1 * (1 - cos * cos))) * n) / n2;
            else if (snellMode == "scaled-expanded")
                outgoing = (n1 * d + (n1 * cos - Math.Sqrt(n2 * n2 - n1 * n1 + (n1 * cos) * (n1 * cos))) * n) / n2;
            else if (snellMode == "ratio-expanded")
                outgoing = eta * d + (eta * cos - Math.Sqrt(1 - eta * eta + (eta * cos) * (eta * cos))) * n;
            else
            {
                var cross = new Vector3D(d.Y * n.Z - d.Z * n.Y, d.Z * n.X - d.X * n.Z, d.X * n.Y - d.Y * n.X);
                outgoing = eta * d + (eta * cos - Math.Sqrt(1 - eta * eta * (cross.X * cross.X + cross.Y * cross.Y + cross.Z * cross.Z))) * n;
            }
            if (normalize) outgoing /= outgoing.Length;
            return new(ray with { Direction = outgoing }, RayInteractionKind.Transmitted);
        }
    }
}
