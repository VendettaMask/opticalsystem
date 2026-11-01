using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Services;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Diagnostics;
using OpticalSystemDesign.ZemaxComparison.Workbench;

internal static class ObscuredRayControls
{
    internal static async Task Run(string repository, string output, string coreHash)
    {
        var audit = JsonFiles.Read<JsonElement>(Path.Combine(repository,
            "validation/rechecks/2026-10-31/all-example-accuracy/prior-pass-audit.json"));
        var lenses = audit.GetProperty("cases").EnumerateArray().Select(r => r.GetProperty("lens").GetString()!).Distinct().ToArray();
        var sourceMap = lenses.ToDictionary(l => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(
            Path.Combine(repository, "src/OpticalSystemDesign.App/Assets/SequentialExamples", l)))), l => l);
        var results = new List<object>();
        foreach (var directory in Directory.EnumerateDirectories(Path.Combine(repository, "artifacts/zemax-sequential-corpus/20261021/full-v1/rays")).Order())
        {
            var jobPath = Path.Combine(directory, "job.json");
            if (!File.Exists(jobPath)) continue;
            var job = JsonFiles.Read<RayAuditJob>(jobPath);
            if (!sourceMap.TryGetValue(job.SourceSha256.ToLowerInvariant(), out var lens)) continue;
            var id = Path.GetFileName(directory);
            var imported = await WorkbenchExecutor.Import(Path.Combine(repository, "src/OpticalSystemDesign.App/Assets/SequentialExamples", lens));
            var optic = imported.Configurations[job.Configuration - 1];
            var folder = Path.Combine(output, id); Directory.CreateDirectory(folder);
            var nativePath = Path.Combine(directory, "native/ray-audit.json");
            if (JsonFiles.Read<JsonElement>(Path.Combine(directory, "native/batch-result.json")).GetProperty("exitCode").GetInt32() != 0)
                throw new InvalidDataException("Native ray reference did not complete successfully.");
            var native = JsonFiles.Read<JsonElement>(nativePath);
            var surfaces = native.GetProperty("surfaces").EnumerateArray().ToArray();
            if (surfaces.Length != optic.SurfaceGroup.Items.Count - 1)
            {
                results.Add(new { id, lens, conclusion = "Incomparable", reason = "Original folded native/Core surface mapping is not established" });
                continue;
            }
            var inputs = JsonFiles.Read<JsonElement[]>(Path.Combine(directory, "inputs.json"));
            if (!JsonSerializer.SerializeToElement(inputs, JsonFiles.Options).ToString().Equals(native.GetProperty("inputs").ToString()))
            {
                // JSON whitespace/number spelling are not input identities.
                var recorded = native.GetProperty("inputs").EnumerateArray().ToArray();
                if (inputs.Length != recorded.Length || inputs.Zip(recorded).Any(p =>
                    new[] { "fieldIndex", "pupilIndex", "hx", "hy", "px", "py", "weight" }.Any(k => p.First.GetProperty(k).GetDouble() != p.Second.GetProperty(k).GetDouble())))
                    throw new InvalidDataException("Native fixed ray inputs differ.");
            }
            var nativeModel = JsonFiles.Read<JsonElement>(Path.Combine(directory, "native/model.json"));
            var signs = NativeReflectionConvention.PropagationSigns(nativeModel);
            var scale = nativeModel.GetProperty("units").GetProperty("LensUnits").GetString() switch
            { "Millimeters" => 1d, "Centimeters" => 10d, "Meters" => 1000d, "Inches" => 25.4d, _ => throw new InvalidDataException("Unknown units") };
            if (job.RemoveVignettingFactors) foreach (var field in optic.Fields) PupilVignetting.Clear(field);
            var histories = inputs.Select(p => optic.SequentialRayTracer.Trace(optic.SequentialRayTracer.RayGenerator.GenerateGeneric(
                p.GetProperty("hx").GetDouble(), p.GetProperty("hy").GetDouble(), p.GetProperty("px").GetDouble(), p.GetProperty("py").GetDouble(),
                optic.Wavelengths[job.Wavelength - 1].Micrometers, optic.RayAimingEnabled)).RayHistories.Single()).ToArray();
            var acceptance = 0; var clipping = 0; var coordinates = 0; var directions = 0; var compared = 0;
            double maximumCoordinate = 0, maximumDirection = 0;
            var final = surfaces[^1].GetProperty("rays");
            for (var i = 0; i < histories.Length; i++)
            {
                var a = final[i]; var b = histories[i].Last();
                var accepted = b.SurfaceNumber == optic.SurfaceGroup.Items.Count - 1 && !b.Vignetted && b.Intensity > 0;
                if (accepted != (a.GetProperty("error").GetInt32() == 0 && a.GetProperty("vignette").GetInt32() == 0)) acceptance++;
                if (a.GetProperty("error").GetInt32() == 0 && a.GetProperty("vignette").GetInt32() > 0 && b.Vignetted && b.RefractiveIndexBefore.HasValue
                    && a.GetProperty("vignette").GetInt32() != b.SurfaceNumber) clipping++;
            }
            foreach (var surface in surfaces)
            {
                var si = surface.GetProperty("surface").GetInt32(); var rays = surface.GetProperty("rays");
                if (rays.GetArrayLength() != histories.Length) throw new InvalidDataException("Incomplete native ray coverage.");
                for (var i = 0; i < histories.Length; i++)
                {
                    var a = rays[i]; var sample = histories[i].FirstOrDefault(s => s.SurfaceNumber == si);
                    if (a.GetProperty("error").GetInt32() != 0 || a.GetProperty("vignette").GetInt32() != 0
                        || sample is null || sample.Vignetted || !sample.RefractiveIndexBefore.HasValue) continue;
                    var p = optic.SurfaceGroup.Items[si].CoordinateSystem.ToLocalPoint(sample.Position);
                    var d = optic.SurfaceGroup.Items[si].CoordinateSystem.ToLocalDirection(sample.Direction); compared++;
                    foreach (var pair in new[] { ("x", p.X), ("y", p.Y), ("z", p.Z) })
                    { var value = a.GetProperty(pair.Item1).GetDouble() * scale; var error = Math.Abs(pair.Item2 - value); maximumCoordinate = Math.Max(maximumCoordinate, error); if (error > 1e-6 + 1e-6 * Math.Abs(value)) coordinates++; }
                    foreach (var pair in new[] { ("l", d.X), ("m", d.Y), ("n", d.Z) })
                    { var value = a.GetProperty(pair.Item1).GetDouble() * signs[si]; var error = Math.Abs(pair.Item2 - value); maximumDirection = Math.Max(maximumDirection, error); if (error > 1e-8 + 1e-6 * Math.Abs(value)) directions++; }
                }
            }
            var conclusion = acceptance + clipping + coordinates + directions == 0 && compared > 0 ? "Pass" : "Difference";
            JsonFiles.Write(Path.Combine(folder, "acceptance.json"), histories.Select((h, i) => new { inputIndex = i, surface = h.Last().SurfaceNumber, h.Last().Vignetted, h.Last().Intensity }));
            results.Add(new { id, lens, conclusion, inputs = inputs.Length, compared, acceptanceDifferences = acceptance, firstClipDifferences = clipping,
                coordinateFailures = coordinates, directionFailures = directions, maximumCoordinate, maximumDirection, coreSha256 = coreHash,
                nativeReference = nativePath, nativeSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(nativePath))) });
            Console.WriteLine($"{id}: {conclusion}; acceptance={acceptance}, firstClip={clipping}, coordinate={coordinates}, direction={directions}");
        }
        JsonFiles.Write(Path.Combine(output, "results.json"), results);
    }
}
