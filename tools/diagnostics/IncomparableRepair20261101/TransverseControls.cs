using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Materials;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.ZemaxComparison;

internal static class TransverseControls
{
    internal static async Task Run(string study)
    {
        var output = Path.Combine(study, "shared-core-chief.json");
        if (File.Exists(output)) throw new IOException("Fresh Core chief assessment required");
        var records = new List<object>();
        foreach (var model in JsonFiles.Read<JsonElement[]>(Path.Combine(study, "models.json")))
        {
            var input = model.GetProperty("input").GetString()!;
            var optic = await new ZemaxZmxImporter().ImportFileAsync(input);
            var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, 0, 0, .55, false);
            using var trace = optic.SequentialRayTracer.Trace(bundle, TraceRequest.Selected([1, 2]));
            var entrance = trace.GetSurfaceSamples(1)[0]!.Value;
            var exit = trace.GetSurfaceSamples(2)[0]!.Value;
            var profile = (GridGradientIndexProfile)((GradientIndexMaterial)optic.SurfaceGroup.Items[1].MaterialAfter).Profile;
            var bodyPath = (exit.PhaseInclusiveOpticalPathLength ?? exit.CumulativeOpticalPathLength)
                - (entrance.PhaseInclusiveOpticalPathLength ?? entrance.CumulativeOpticalPathLength);
            records.Add(new { model, bodyPath, exit.Position, exit.Direction,
                volumeIndexAtOrigin = profile.Evaluate(Vector3D.Zero, 550).Index,
                interfaceIndexAtOrigin = profile.InterfaceRefractiveIndex(Vector3D.Zero, 550),
                sourceSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(input))) });
        }
        JsonFiles.Write(output, new { diagnosticOnly = true, runtimeUsesNativeData = false,
            coreSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(Optic).Assembly.Location))), records });
    }
}
