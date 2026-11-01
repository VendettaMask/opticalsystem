using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Workbench;

internal static class TestingRayControls
{
    internal static async Task Run(string repository, string output, string coreHash)
    {
        var results = new List<object>();
        var reference = Path.Combine(repository, "validation/rechecks/2026-11-01/testing-three/native-rays");
        foreach (var execution in JsonFiles.Read<JsonElement[]>(Path.Combine(reference, "executions.json")))
        {
            var id = execution.GetProperty("id").GetString()!;
            var lens = execution.GetProperty("lens").GetString()!;
            var config = execution.GetProperty("c").GetInt32();
            if (!execution.GetProperty("unchanged").GetBoolean() || execution.GetProperty("result").GetProperty("exitCode").GetInt32() != 0)
                throw new InvalidDataException("Native capture failed.");
            var nativePath = Path.Combine(reference, id, "native/ray-audit.json");
            var native = JsonFiles.Read<JsonElement>(nativePath);
            var model = JsonFiles.Read<JsonElement>(Path.Combine(reference, id, "native/model.json"));
            var optic = (await WorkbenchExecutor.Import(Path.Combine(repository, "src/OpticalSystemDesign.App/Assets/SequentialExamples", lens))).Configurations[config - 1];
            var physical = model.GetProperty("surfaces").EnumerateArray().Where(s => s.GetProperty("Type").GetString() != "CoordinateBreak"
                || s.GetProperty("Thickness").ValueKind == JsonValueKind.Number && s.GetProperty("Thickness").GetDouble() != 0).ToArray();
            if (physical.Length != optic.SurfaceGroup.Items.Count || !physical[0].GetProperty("IsObject").GetBoolean() || !physical[^1].GetProperty("IsImage").GetBoolean())
                throw new InvalidDataException("Native/Core physical surface mapping differs.");
            var map = physical.Select((s, i) => (Number: s.GetProperty("SurfaceNumber").GetInt32(), Index: i)).ToDictionary(p => p.Number, p => p.Index);
            var signs = NativeReflectionConvention.PropagationSigns(model);
            var inputs = native.GetProperty("inputs").EnumerateArray().ToArray();
            var histories = inputs.Select(p => optic.SequentialRayTracer.Trace(optic.SequentialRayTracer.RayGenerator.GenerateGeneric(
                p.GetProperty("hx").GetDouble(), p.GetProperty("hy").GetDouble(), p.GetProperty("px").GetDouble(), p.GetProperty("py").GetDouble(),
                optic.Wavelengths[0].Micrometers, optic.RayAimingEnabled)).RayHistories.Single()).ToArray();
            var final = native.GetProperty("surfaces").EnumerateArray().Last().GetProperty("rays");
            var acceptance = 0; var clips = 0; var coords = 0; var dirs = 0; var compared = 0;
            double maxCoord = 0, maxDir = 0;
            for (var i = 0; i < inputs.Length; i++)
            {
                var n = final[i]; var b = histories[i].Last();
                var accepted = b.SurfaceNumber == physical.Length - 1 && !b.Vignetted && b.Intensity > 0;
                if (accepted != (n.GetProperty("error").GetInt32() == 0 && n.GetProperty("vignette").GetInt32() == 0)) acceptance++;
                var clip = n.GetProperty("vignette").GetInt32();
                if (clip > 0 && (!map.TryGetValue(clip, out var ci) || !b.Vignetted || b.SurfaceNumber != ci)) clips++;
            }
            var perSurface = new List<object>();
            foreach (var surface in native.GetProperty("surfaces").EnumerateArray())
            {
                var number = surface.GetProperty("surface").GetInt32();
                if (!map.TryGetValue(number, out var si)) continue;
                var rays = surface.GetProperty("rays");
                if (rays.GetArrayLength() != inputs.Length) throw new InvalidDataException("Incomplete fixed-ray coverage.");
                var cf = 0; var df = 0; var count = 0; double mc = 0, md = 0;
                for (var i = 0; i < inputs.Length; i++)
                {
                    var n = rays[i]; var sample = histories[i].FirstOrDefault(s => s.SurfaceNumber == si);
                    if (n.GetProperty("error").GetInt32() != 0 || n.GetProperty("vignette").GetInt32() != 0 || sample is null || sample.Vignetted || !sample.RefractiveIndexBefore.HasValue) continue;
                    var p = optic.SurfaceGroup.Items[si].CoordinateSystem.ToLocalPoint(sample.Position);
                    var d = optic.SurfaceGroup.Items[si].CoordinateSystem.ToLocalDirection(sample.Direction); count++;
                    foreach (var pair in new[] { ("x", p.X), ("y", p.Y), ("z", p.Z) })
                    { var v = n.GetProperty(pair.Item1).GetDouble(); var e = Math.Abs(pair.Item2 - v); mc = Math.Max(mc, e); if (e > 1e-6 + 1e-6 * Math.Abs(v)) cf++; }
                    foreach (var pair in new[] { ("l", d.X), ("m", d.Y), ("n", d.Z) })
                    { var v = n.GetProperty(pair.Item1).GetDouble() * signs[number]; var e = Math.Abs(pair.Item2 - v); md = Math.Max(md, e); if (e > 1e-8 + 1e-6 * Math.Abs(v)) df++; }
                }
                coords += cf; dirs += df; compared += count; maxCoord = Math.Max(maxCoord, mc); maxDir = Math.Max(maxDir, md);
                perSurface.Add(new { nativeSurface = number, coreSurface = si, count, coordinateFailures = cf, directionFailures = df, maxCoordinate = mc, maxDirection = md });
            }
            var conclusion = acceptance + clips + coords + dirs == 0 && compared > 0 ? "Pass" : "Difference";
            var folder = Path.Combine(output, id); Directory.CreateDirectory(folder);
            JsonFiles.Write(Path.Combine(folder, "histories.json"), histories);
            JsonFiles.Write(Path.Combine(folder, "surfaces.json"), perSurface);
            JsonFiles.Write(Path.Combine(folder, "model.optic.json"), optic.ToSnapshot());
            results.Add(new { id, lens, config, conclusion, inputs = inputs.Length, compared, acceptanceDifferences = acceptance, firstClipDifferences = clips,
                coordinateFailures = coords, directionFailures = dirs, maximumCoordinate = maxCoord, maximumDirection = maxDir, coreSha256 = coreHash,
                nativeReference = nativePath, nativeSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(nativePath))) });
            Console.WriteLine($"{id}: {conclusion}, acceptance={acceptance}, firstClip={clips}, coordinates={coords}, directions={dirs}");
        }
        JsonFiles.Write(Path.Combine(output, "results.json"), results);
    }
}
