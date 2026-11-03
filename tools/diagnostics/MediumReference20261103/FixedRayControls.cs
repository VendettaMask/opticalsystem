using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Diagnostics;
using OpticalSystemDesign.ZemaxComparison.Workbench;

internal static class FixedRayControls
{
    internal static async Task Run(string repo, string root, string coreHash)
    {
        string Hash(string p) { using var s = File.OpenRead(p); return Convert.ToHexStringLower(SHA256.HashData(s)); }
        var prior = Path.Combine(repo, "artifacts/zemax-sequential-corpus/20261021/full-v1");
        var authority = Path.Combine(repo, "validation/repairs/2026-10-21/sequential-corpus-full-evidence.zip");
        if (Hash(authority) != "1dc562461d4e39ac9bf54f3c2309a0bb81fdeccfb092554cd8e9c14aa80760ad") throw new InvalidDataException("Frozen authority changed");
        using var zip = ZipFile.OpenRead(authority);
        using var manifestStream = zip.GetEntry("payload-manifest.json")!.Open();
        using var manifest = JsonDocument.Parse(manifestStream);
        var lookup = manifest.RootElement.EnumerateArray().ToDictionary(e => e.GetProperty("path").GetString()!);
        var verified = new Dictionary<string, string>();
        T Read<T>(string path)
        {
            var relative = "run/" + Path.GetRelativePath(prior, path).Replace('\\', '/');
            var sha = Hash(path);
            if (lookup[relative].GetProperty("sha256").GetString() != sha) throw new InvalidDataException("Native input changed");
            verified[relative] = sha; return JsonFiles.Read<T>(path);
        }
        var records = new List<object>();
        foreach (var lens in new[] { "Miscellaneous/Toroid.zmx", "Objectives/Gaussian Quadrature over Annular Pupils.zmx" })
        {
            var source = Path.Combine(repo, "src/OpticalSystemDesign.App/Assets/SequentialExamples", lens);
            var sha = Hash(source); var optic = (await WorkbenchExecutor.Import(source)).Configurations[0];
            foreach (var dir in Directory.GetDirectories(Path.Combine(prior, "rays")))
            {
                var jobPath = Path.Combine(dir, "job.json"); if (!File.Exists(jobPath)) continue;
                var job = JsonFiles.Read<RayAuditJob>(jobPath); if (job.SourceSha256 != sha) continue;
                job = Read<RayAuditJob>(jobPath);
                var id = Path.GetFileName(dir); var native = Path.Combine(dir, "native");
                if (Read<JsonElement>(Path.Combine(native, "batch-result.json")).GetProperty("exitCode").GetInt32() != 0)
                    throw new InvalidDataException("No valid original native ray reference");
                var values = Read<JsonElement>(Path.Combine(native, "ray-audit.json"));
                var inputs = Read<JsonElement>(Path.Combine(dir, "inputs.json"));
                if (!Equivalent(inputs, values.GetProperty("inputs")))
                    throw new InvalidDataException("Native input identity differs");
                var surfaces = values.GetProperty("surfaces").EnumerateArray().ToArray();
                if (surfaces.Length != optic.SurfaceGroup.Items.Count - 1) throw new InvalidDataException("Explicit native/Core surface mapping required");
                var model = Read<JsonElement>(Path.Combine(native, "model.json"));
                if (model.GetProperty("units").GetProperty("LensUnits").GetString() != "Millimeters") throw new InvalidDataException("Undeclared control units");
                var signs = NativeReflectionConvention.PropagationSigns(model);
                var histories = new List<IReadOnlyList<RayTraceSample>>();
                foreach (var input in inputs.EnumerateArray())
                {
                    var ray = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(input.GetProperty("hx").GetDouble(), input.GetProperty("hy").GetDouble(),
                        input.GetProperty("px").GetDouble(), input.GetProperty("py").GetDouble(), optic.Wavelengths[job.Wavelength - 1].Micrometers, optic.RayAimingEnabled);
                    histories.Add(optic.SequentialRayTracer.Trace(ray).RayHistories.Single());
                }
                int pairs = 0, coordinateFailures = 0, directionFailures = 0, acceptanceDifferences = 0, firstClipDifferences = 0;
                double maxCoordinate = 0, maxDirection = 0;
                for (var i = 0; i < histories.Count; i++)
                {
                    var a = surfaces[^1].GetProperty("rays")[i]; var b = histories[i].Last();
                    var accepted = a.GetProperty("error").GetInt32() == 0 && a.GetProperty("vignette").GetInt32() == 0;
                    if (accepted != (b.SurfaceNumber == optic.SurfaceGroup.Items.Count - 1 && !b.Vignetted && b.Intensity > 0)) acceptanceDifferences++;
                    if (a.GetProperty("error").GetInt32() == 0 && a.GetProperty("vignette").GetInt32() > 0 && b.Vignetted && b.RefractiveIndexBefore.HasValue
                        && a.GetProperty("vignette").GetInt32() != b.SurfaceNumber) firstClipDifferences++;
                }
                foreach (var surface in surfaces)
                {
                    var si = surface.GetProperty("surface").GetInt32(); var rays = surface.GetProperty("rays");
                    if (rays.GetArrayLength() != histories.Count) throw new InvalidDataException("Incomplete native ray coverage");
                    for (var i = 0; i < histories.Count; i++)
                    {
                        var a = rays[i]; var b = histories[i].FirstOrDefault(s => s.SurfaceNumber == si);
                        if (a.GetProperty("number").GetInt32() != i + 1) throw new InvalidDataException("Native ray order differs");
                        if (a.GetProperty("error").GetInt32() != 0 || a.GetProperty("vignette").GetInt32() != 0 || b is null || b.Vignetted || !b.RefractiveIndexBefore.HasValue) continue;
                        var frame = optic.SurfaceGroup.Items[si].CoordinateSystem; var p = frame.ToLocalPoint(b.Position); var d = frame.ToLocalDirection(b.Direction); pairs++;
                        foreach (var pair in new[] { ("x", p.X), ("y", p.Y), ("z", p.Z) })
                        { var target = a.GetProperty(pair.Item1).GetDouble(); var error = Math.Abs(target - pair.Item2); maxCoordinate = Math.Max(maxCoordinate, error); if (error > 1e-6 + 1e-6 * Math.Abs(target)) coordinateFailures++; }
                        foreach (var pair in new[] { ("l", d.X), ("m", d.Y), ("n", d.Z) })
                        { var target = a.GetProperty(pair.Item1).GetDouble() * signs[si]; var error = Math.Abs(target - pair.Item2); maxDirection = Math.Max(maxDirection, error); if (error > 1e-8 + 1e-6 * Math.Abs(target)) directionFailures++; }
                    }
                }
                JsonFiles.Write(Path.Combine(root, "core", id + ".json"), histories);
                var conclusion = pairs > 0 && coordinateFailures + directionFailures + acceptanceDifferences + firstClipDifferences == 0 ? "Pass" : "Difference";
                records.Add(new { id, lens, conclusion, inputs = histories.Count, pairs, maxCoordinate, maxDirection, coordinateFailures, directionFailures, acceptanceDifferences, firstClipDifferences, coreSha256 = coreHash, sourceSha256 = sha });
                Console.WriteLine($"{id} {lens}: {conclusion}; {pairs} pairs");
            }
        }
        if (records.Count == 0) throw new InvalidDataException("No controls found");
        JsonFiles.Write(Path.Combine(root, "results.json"), records);
        JsonFiles.Write(Path.Combine(root, "identity.json"), new { coreSha256 = coreHash, authority, verified, newNativeCapture = false });
    }

    private static bool Equivalent(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;
        return a.ValueKind switch
        {
            JsonValueKind.Number => a.GetDouble() == b.GetDouble(),
            JsonValueKind.Object => a.EnumerateObject().Count() == b.EnumerateObject().Count()
                && a.EnumerateObject().All(p => b.TryGetProperty(p.Name, out var value) && Equivalent(p.Value, value)),
            JsonValueKind.Array => a.GetArrayLength() == b.GetArrayLength()
                && a.EnumerateArray().Zip(b.EnumerateArray()).All(p => Equivalent(p.First, p.Second)),
            _ => a.ToString() == b.ToString()
        };
    }
}
