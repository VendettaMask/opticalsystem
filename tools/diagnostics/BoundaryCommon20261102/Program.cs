using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Diagnostics;
using OpticalSystemDesign.ZemaxComparison.Workbench;
using OpticalSystemDesign.ZemaxComparison.Zemax;

var mode = args[0]; var repo = Path.GetFullPath(args[1]); var output = Path.GetFullPath(args[2]);
if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Use a fresh output folder.");
Directory.CreateDirectory(output);
var source = Path.Combine(repo, "src/OpticalSystemDesign.App/Assets/SequentialExamples/Miscellaneous/Bi-ocular lens.ZMX");
var imported = await WorkbenchExecutor.Import(source);
var coreHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(Optic).Assembly.Location)));
var records = new List<object>();
var pupils = new[] { -1d, -1 + 1e-12, -1 + 1e-9, -.95, 0, .95, 1 - 1e-9, 1d };
if (mode is "probe" or "compare")
{
    for (var c = 0; c < 2; c++) foreach (var wave in imported.Configurations[c].Wavelengths)
    {
        var optic = imported.Configurations[c]; var stop = optic.SurfaceGroup.Items.Single(s => s.IsStop);
        foreach (var px in pupils)
        {
            var sourceRay = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(-1, 0, px, 0, wave.Micrometers, true).Rays[0];
            var history = optic.SequentialRayTracer.Trace(new([sourceRay])).RayHistories[0];
            var diagnostic = optic.SequentialRayTracer.DiagnoseApertures(sourceRay);
            var circular = optic.SequentialRayTracer.DiagnoseCircularApertures(sourceRay);
            var stopHit = history.FirstOrDefault(s => s.SurfaceNumber == stop.Number);
            var localSamples = history.Select(hit => new { hit.SurfaceNumber, hit.Vignetted, hit.Intensity,
                position = optic.SurfaceGroup.Items[hit.SurfaceNumber].CoordinateSystem.ToLocalPoint(hit.Position),
                direction = optic.SurfaceGroup.Items[hit.SurfaceNumber].CoordinateSystem.ToLocalDirection(hit.Direction) }).ToArray();
            records.Add(new { configuration = c + 1, wave.Nanometers, px, sourceRay, history, localSamples, diagnostic, circular,
                stopHit = stopHit is null ? null : new { local = stop.CoordinateSystem.ToLocalPoint(stopHit.Position), stop.SemiDiameter }, coreHash });
        }
    }
    JsonFiles.Write(Path.Combine(output, "results.json"), records);
    if (mode == "compare") CompareFixedRays();
    return;
}
if (mode is "native" or "native-solves")
{
    using var lease = new FileStream(Path.Combine(repo, "artifacts/validation/.background-small-native-controls.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    var native = await ZemaxExecutor.Build(ZemaxExecutor.Locate(null), output, CancellationToken.None);
    foreach (var c in mode == "native" ? new[] { 1, 2 } : new[] { 2 }) foreach (var w in mode == "native" ? new[] { 1, 2, 3 } : new[] { 2 })
    {
        var processes = Process.GetProcessesByName("OpticStudio"); var count = processes.Length; foreach (var p in processes) p.Dispose();
        if (count > 1) throw new InvalidOperationException("No independent native slot.");
        var folder = Path.Combine(output, $"c{c}-w{w}");
        var job = new RayAuditJob { Source = source, SourceSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source))),
            Output = folder, Configuration = c, Wavelength = w, RayDensity = 6, Fields = [[-1, 0]], TimeoutSeconds = 120, RemoveVignettingFactors = false };
        var inputs = pupils.Select((p, i) => new { fieldIndex = 0, pupilIndex = i, hx = -1d, hy = 0d, px = p, py = 0d, weight = 1d }).ToArray();
        var result = await native.CaptureRayAudit(source, Path.Combine(folder, "native"), job, inputs, CancellationToken.None);
        records.Add(new { c, w, result, coreHash, job.SourceSha256 }); JsonFiles.Write(Path.Combine(output, "executions.json"), records);
        if (result.ExitCode != 0) throw new InvalidOperationException("Native capture failed; retained.");
        Console.WriteLine($"c{c}-w{w}: native fixed boundary rays captured.");
    }
    return;
}
throw new ArgumentException("Unknown mode");

void CompareFixedRays()
{
    var core = JsonFiles.Read<JsonElement[]>(Path.Combine(output, "results.json")); var comparisons = new List<object>();
    foreach (var c in new[] { 1, 2 }) foreach (var w in new[] { 1, 2, 3 })
    {
        var folder = Path.Combine(repo, "validation/rechecks/2026-11-02/boundary-common/native-rays", $"c{c}-w{w}", "native");
        var native = JsonFiles.Read<JsonElement>(Path.Combine(folder, "ray-audit.json"));
        var model = JsonFiles.Read<JsonElement>(Path.Combine(folder, "model.json"));
        var physical = model.GetProperty("surfaces").EnumerateArray().Where(s => s.GetProperty("Type").GetString() != "CoordinateBreak").ToArray();
        if (physical.Length != imported.Configurations[c - 1].SurfaceGroup.Items.Count) throw new InvalidDataException("Ambiguous native surface map.");
        var map = physical.Select((s, i) => (Number: s.GetProperty("SurfaceNumber").GetInt32(), Index: i)).ToDictionary(p => p.Number, p => p.Index);
        var wave = imported.Configurations[c - 1].Wavelengths[w - 1].Nanometers;
        var selected = core.Where(r => r.GetProperty("configuration").GetInt32() == c && r.GetProperty("nanometers").GetDouble() == wave).ToArray();
        int acceptance = 0, clipping = 0, coordinates = 0, directions = 0, pairs = 0; double maxPosition = 0, maxDirection = 0;
        var last = native.GetProperty("surfaces").EnumerateArray().Last().GetProperty("rays");
        for (var i = 0; i < pupils.Length; i++)
        {
            var row = last[i]; var accepted = selected[i].GetProperty("diagnostic").GetProperty("insideAllApertures").GetBoolean();
            if (accepted != (row.GetProperty("error").GetInt32() == 0 && row.GetProperty("vignette").GetInt32() == 0)) acceptance++;
            var clip = row.GetProperty("vignette").GetInt32();
            var blocked = selected[i].GetProperty("diagnostic").GetProperty("firstBlockedSurfaceIndex");
            if (clip == 0 ? blocked.ValueKind != JsonValueKind.Null : blocked.ValueKind == JsonValueKind.Null || !map.TryGetValue(clip, out var index) || blocked.GetInt32() != index) clipping++;
        }
        foreach (var surface in native.GetProperty("surfaces").EnumerateArray())
        {
            if (!map.TryGetValue(surface.GetProperty("surface").GetInt32(), out var index)) continue;
            for (var i = 0; i < pupils.Length; i++)
            {
                var n = surface.GetProperty("rays")[i];
                var hit = selected[i].GetProperty("localSamples").EnumerateArray().FirstOrDefault(h => h.GetProperty("surfaceNumber").GetInt32() == index);
                if (n.GetProperty("error").GetInt32() != 0 || n.GetProperty("vignette").GetInt32() != 0 || hit.ValueKind == JsonValueKind.Undefined || hit.GetProperty("vignetted").GetBoolean()) continue;
                pairs++;
                foreach (var axis in new[] { "x", "y", "z" }) { var value = n.GetProperty(axis).GetDouble(); var e = Math.Abs(hit.GetProperty("position").GetProperty(axis).GetDouble() - value); maxPosition = Math.Max(maxPosition, e); if (e > 1e-6 + 1e-6 * Math.Abs(value)) coordinates++; }
                foreach (var pair in new[] { ("l", "x"), ("m", "y"), ("n", "z") }) { var value = n.GetProperty(pair.Item1).GetDouble(); var e = Math.Abs(hit.GetProperty("direction").GetProperty(pair.Item2).GetDouble() - value); maxDirection = Math.Max(maxDirection, e); if (e > 1e-8 + 1e-6 * Math.Abs(value)) directions++; }
            }
        }
        comparisons.Add(new { c, w, rays = pupils.Length, surfacePairs = pairs, acceptanceDifferences = acceptance, firstClipDifferences = clipping,
            coordinateFailures = coordinates, directionFailures = directions, maximumPositionDifference = maxPosition, maximumDirectionDifference = maxDirection,
            conclusion = acceptance + clipping + coordinates + directions == 0 && pairs > 0 ? "Pass" : "Difference", coreHash,
            nativeSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder, "ray-audit.json")))) });
    }
    JsonFiles.Write(Path.Combine(output, "fixed-ray-comparison.json"), comparisons);
}
