using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Differentiation;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.InitialStructure.Contracts;
using OpticalSystemDesign.InitialStructure.Engine;

internal static class PerformanceProbe
{
    public static int Run(string input, string output, bool refinement = false)
    {
        var json = new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var state = JsonSerializer.Deserialize<AutoLensManagedState>(File.ReadAllText(input), json)!;
        var optic = Optic.FromSnapshot(state.Optic);
        AutoLensReferenceMaterials.Register(optic.Materials);
        var parameters = state.Parameters;
        if (refinement)
        {
            var preset = AutoLensReferenceExamples.Mobile();
            foreach (var surface in optic.SurfaceGroup.Items)
                if (surface.MaterialAfter is not AirMaterial)
                    surface.MaterialAfter = AutoLensReferenceMaterials.Match(optic.Materials, surface.MaterialAfter,
                        preset.Options.AllowedGlassNames, [656, 587, 486]).Material;
            OpticalDesignParameters.SynchronizeIncidentMedia(optic);
            parameters = AutoLensReferenceStart.Parameters(optic, preset.Specification, materials: false);
        }
        var imageInputs = FixedInputOpticalMetrics.SampleRingArm(refinement ? optic : Optic.FromSnapshot(state.ReferenceSampling!.Optic),
            state.ReferenceHalfFieldRadians, state.ReferenceFocalLength, 8, refinement ? 2 : 1, refinement ? 1024 : 512, 1.05, state.ReferenceSampling!.Seed);
        var craInputs = FixedInputOpticalMetrics.SampleRingArm(optic, state.ReferenceHalfFieldRadians, state.ReferenceFocalLength,
            16, 1, 1024, .1, 42, primaryOnly: true);
        var bendInputs = FixedInputOpticalMetrics.SampleRingArm(optic, state.ReferenceHalfFieldRadians, state.ReferenceFocalLength,
            16, 1, 1024, 1, 43, primaryOnly: true);
        var profiles = new List<object>();
        // Warm-up includes kernel compilation. Measured samples use the same immutable optic/rays.
        for (var repetition = 0; repetition < 3; repetition++)
        {
            var allocations = GC.GetTotalAllocatedBytes(true);
            var total = Stopwatch.StartNew();
            var watch = Stopwatch.StartNew();
            var images = FixedInputOpticalMetrics.Evaluate(optic, parameters, imageInputs, Math.Cos(20*Math.PI/180), DesignComputeMode.Gpu);
            var imageSeconds = watch.Elapsed.TotalSeconds; watch.Restart();
            var cra = FixedInputOpticalMetrics.Evaluate(optic, parameters, craInputs, Math.Cos(20*Math.PI/180), DesignComputeMode.Gpu);
            var craSeconds = watch.Elapsed.TotalSeconds; watch.Restart();
            var bend = FixedInputOpticalMetrics.Evaluate(optic, parameters, bendInputs, Math.Cos(20*Math.PI/180), DesignComputeMode.Gpu);
            var bendSeconds = watch.Elapsed.TotalSeconds; watch.Restart();
            var geometry = AutoLensReferenceLoss.Geometry(optic, parameters, compact: true, optimizeMaterials: !refinement);
            var geometrySeconds = watch.Elapsed.TotalSeconds; watch.Restart();
            var objective = AutoLensReferenceLoss.Image(images, state.ReferenceCentroid) + .1*(geometry +
                AutoLensReferenceLoss.RayRegularization(cra, bend, Math.Cos(40*Math.PI/180)));
            var reductionSeconds = watch.Elapsed.TotalSeconds;
            var gradient = Enumerable.Range(0, parameters.Count).Select(objective.Derivative).ToArray();
            var observation = new { Repetition = repetition, Warmup = repetition == 0, TotalSeconds = total.Elapsed.TotalSeconds,
                ImageSeconds = imageSeconds, CraSeconds = craSeconds, BendSeconds = bendSeconds, GeometrySeconds = geometrySeconds,
                ReductionSeconds = reductionSeconds, AllocatedBytes = GC.GetTotalAllocatedBytes(true) - allocations,
                Loss = objective.Value, Gradient = gradient,
                RayValues = images.Fields.Concat(cra.Fields).Concat(bend.Fields).SelectMany(f => f.Rays).Select(r => new
                { r.Valid, X = r.X.Value, Y = r.Y.Value, Z = r.DirectionZ.Value, Bend = r.BendPenalty.Value }).ToArray(),
                Images = images.Execution, Cra = cra.Execution, Bend = bend.Execution };
            profiles.Add(observation);
            Console.WriteLine($"{repetition}: total={observation.TotalSeconds:F4}s images={imageSeconds:F4} cra={craSeconds:F4} bend={bendSeconds:F4} geometry={geometrySeconds:F4} reduction={reductionSeconds:F4} allocated={observation.AllocatedBytes/1e6:F1} MB");
        }
        File.WriteAllText(output, JsonSerializer.Serialize(new { InputSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))),
            Environment.ProcessorCount, Device = DesignGpu.Status, Parameters = parameters.Count, RefinementSizedCatalogWorkload = refinement,
            RaysPerUpdate = imageInputs.Fields.Sum(f => f.Rays.Count) + craInputs.Fields.Sum(f => f.Rays.Count) + bendInputs.Fields.Sum(f => f.Rays.Count),
            CoreSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Optic).Assembly.Location))), Profiles = profiles }, json));
        return 0;
    }
}
