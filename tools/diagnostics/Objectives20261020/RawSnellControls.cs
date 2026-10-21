using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Interactions;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;

// Reuse the formal Snell operation to isolate its final normalization only.
// This intentionally changed diagnostic optic must never enter product output.
internal static class RawSnellControls
{
    public static void Run(string output)
    {
        if (Directory.Exists(output)) throw new IOException("Use a fresh output directory.");
        Directory.CreateDirectory(output);
        using var items = JsonDocument.Parse(File.ReadAllText("artifacts/validation/objectives-20261020/final-v7-intersection-fallback/items.json"));
        var rows = new List<object>();
        foreach (var item in items.RootElement.EnumerateArray())
        {
            var run = item.GetProperty("run").Deserialize<AnalysisRun>(JsonFiles.Options)!;
            if (run.Key != "Diffraction Encircled Energy") continue;
            foreach (var raw in new[] { false, true })
            {
                var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(item.GetProperty("snapshot").GetString()!));
                if (raw) foreach (var surface in optic.SurfaceGroup.Items)
                    if (surface.InteractionModel is RefractiveReflectiveInteractionModel { IsReflective: false } model)
                        surface.InteractionModel = new RawSnell(model);
                var wave = optic.Wavelengths[run.Request!.Wavelength - 1];
                var directions = new List<Vector3D>();
                foreach (var (px, py) in new[] { (0d, 0d), (0d, 1d), (0d, -1d), (1d, 0d), (-1d, 0d) })
                {
                    var ray = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, px, py, wave.Micrometers, optic.RayAimingEnabled).Rays[0];
                    var traced = (RayTraceSample?)optic.SequentialRayTracer.GetType().GetMethod("TraceToSurface", BindingFlags.NonPublic | BindingFlags.Instance)!
                        .Invoke(optic.SequentialRayTracer, [ray, optic.SurfaceGroup.Items.Count - 1]);
                    if (traced is null) throw new InvalidOperationException("Cone ray unavailable.");
                    directions.Add(traced.Direction);
                }
                var chief = directions[0];
                var cosineF = .5 / Math.Sqrt(directions.Skip(1).Select(d =>
                {
                    var dot = chief.X * d.X + chief.Y * d.Y + chief.Z * d.Z;
                    return 1 - dot * dot;
                }).Average());
                rows.Add(new { lens = item.GetProperty("lens").GetString(), raw, cosineF, directions });
                Console.WriteLine($"{item.GetProperty("lens").GetString()} raw={raw}: F={cosineF:R}, n={directions[1].Z:R}");
            }
        }
        JsonFiles.Write(Path.Combine(output, "summary.json"), rows);
    }

    private sealed class RawSnell(RefractiveReflectiveInteractionModel original) : IInteractionModel
    {
        public string Kind => original.Kind;
        public IInteractionModel Clone() => new RawSnell((RefractiveReflectiveInteractionModel)original.Clone());
        public ParaxialRay Interact(ParaxialRay ray, SurfaceInteractionContext context) => original.Interact(ray, context);
        public RealRayInteractionResult Interact(RealRay ray, SurfaceInteractionContext context)
        {
            var n = context.SurfaceNormal;
            if (ray.Direction.X * n.X + ray.Direction.Y * n.Y + ray.Direction.Z * n.Z > 0) n = -n;
            var refracted = ((Vector3D Direction, RayInteractionKind Kind))typeof(RefractiveReflectiveInteractionModel)
                .GetMethod("Refract", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [ray.Direction, n, context.RefractiveIndexBefore, context.RefractiveIndexAfter])!;
            return new(ray with { Direction = refracted.Direction }, refracted.Kind);
        }
    }
}
