using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Workbench;
using OpticalSystemDesign.ZemaxComparison.Zemax;

internal static class ToroidNativeControls
{
    internal static async Task Run(string repo, string root, string coreHash)
    {
        string Hash(string p) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p)));
        using var lease = new FileStream(Path.Combine(repo, "artifacts/validation/.background-small-native-controls.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var api = ZemaxExecutor.Locate(@"D:\Program Files\ANSYS Inc\v261\Zemax OpticStudio");
        _ = await ZemaxExecutor.Build(api, root, CancellationToken.None);
        var host = Path.Combine(root, "host/ZemaxHost.exe");
        var cases = new List<object>(); var results = new List<object>();
        foreach (var radii in new[] { (50d, 80d), (50d, -80d), (-50d, 80d), (-50d, -80d), (0d, 80d), (50d, 0d), (50d, 100000d) })
        {
            var engines = Process.GetProcessesByName("OpticStudio"); var count = engines.Length; foreach (var p in engines) p.Dispose();
            if (count > 1) throw new InvalidOperationException("Independent background native slot unavailable; no application was closed");
            var id = $"toroid-{radii.Item1:G}-{radii.Item2:G}";
            var source = FormattableString.Invariant($"MODE SEQ\nUNIT MM X W X CM MR CPMM\nGCAT SCHOTT\nENPD 4\nFTYP 0 0 2 2 0 0 0\nXFLN 0 0\nYFLN 0 5\nWAVM 1 .55 1\nWAVM 2 .65 1\nPWAV 1\nSURF 0\nTYPE STANDARD\nDISZ INFINITY\nSURF 1\nSTOP\nTYPE STANDARD\nDISZ 5\nDIAM 20 1 0 0 1\nSURF 2\nTYPE TOROIDAL\nCURV {(radii.Item1 == 0 ? 0 : 1 / radii.Item1):R}\nPARM 0 0\nPARM 1 {radii.Item2:R}\nPARM 2 0\nGLAS N-BK7\nDISZ 8\nDIAM 20 1 0 0 1\nSURF 3\nTYPE STANDARD\nDISZ 5\nDIAM 20 1 0 0 1\nSURF 4\nTYPE STANDARD\nDIAM 20 1 0 0 1\n");
            source = "VERS 200426 262 0\n" + source.Replace("TYPE STANDARD\n", "TYPE STANDARD\nCURV 0\n", StringComparison.Ordinal);
            var sourcePath = Path.Combine(root, id + ".ZMX"); File.WriteAllText(sourcePath, source);
            var sourceHash = Hash(sourcePath);
            var operands = new List<object>();
            object Operand(string code, int surface, int wave, double a, double b, double c, double d) =>
                new { code, int1 = surface, int2 = wave, data1 = a, data2 = b, data3 = c, data4 = d, data5 = 0d, data6 = 0d };
            foreach (var x in new[] { -.7, 0, .9 }) foreach (var y in new[] { -.8, 0, 1.1 }) operands.Add(Operand("SSAG", 2, 0, x, y, 0, 0));
            foreach (var wave in new[] { 1, 2 }) foreach (var hy in new[] { 0d, 1d }) foreach (var px in new[] { 0d, .4 }) foreach (var py in new[] { -.8, 0, .8 })
                foreach (var surface in new[] { 1, 2, 3 }) foreach (var code in new[] { "OPTH", "REAX", "REAY", "REAZ", "REAA", "REAB", "REAC" })
                    operands.Add(Operand(code, surface, wave, 0, hy, px, py));
            var folder = Path.Combine(root, id); var request = Path.Combine(root, id + ".request.json");
            JsonFiles.Write(request, new { zosApiPath = api, input = sourcePath, inputSha256 = sourceHash, configuration = 1, wavelength = 1,
                zemaxVersion = "2026 R1", adapter = "surface-model-controls", captureScreenshots = false, formalAcceptanceRequest = false, modelOperands = operands });
            var result = await ProcessIsolation.Run(host, [request, folder], root, 120, CancellationToken.None,
                new Dictionary<string, string> { ["PATH"] = api + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH") });
            JsonFiles.Write(Path.Combine(folder, "execution.json"), result);
            if (result.ExitCode != 0 || result.TimedOut || Hash(sourcePath) != sourceHash) throw new InvalidDataException("Native control failed or source changed");
            var rows = JsonFiles.Read<JsonElement>(Path.Combine(folder, "model-controls.json")).GetProperty("rows");
            cases.Add(new { id, source, sourceSha256 = sourceHash, rows });
            var optic = (await WorkbenchExecutor.Import(sourcePath)).Configurations[0];
            int failures = 0, comparisons = 0; double maxPosition = 0, maxDirection = 0, maxPath = 0, maxSag = 0;
            foreach (var row in rows.EnumerateArray().Where(r => r.GetProperty("input").GetProperty("code").GetString() == "SSAG"))
            {
                var p = row.GetProperty("input"); var error = Math.Abs(optic.SurfaceGroup.Items[2].Geometry.Sag(p.GetProperty("data1").GetDouble(), p.GetProperty("data2").GetDouble()) - row.GetProperty("value").GetDouble());
                maxSag = Math.Max(maxSag, error); if (error > 1e-10) failures++; comparisons++;
            }
            foreach (var group in rows.EnumerateArray().Where(r => r.GetProperty("input").GetProperty("code").GetString() != "SSAG").GroupBy(r =>
                (Wave: r.GetProperty("input").GetProperty("int2").GetInt32(), Hy: r.GetProperty("input").GetProperty("data2").GetDouble(),
                 Px: r.GetProperty("input").GetProperty("data3").GetDouble(), Py: r.GetProperty("input").GetProperty("data4").GetDouble())))
            {
                foreach (var si in new[] { 1, 2, 3 })
                {
                    var directionNorm = group.Where(r => r.GetProperty("input").GetProperty("int1").GetInt32() == si
                        && r.GetProperty("input").GetProperty("code").GetString() is "REAA" or "REAB" or "REAC")
                        .Sum(r => Math.Pow(r.GetProperty("value").GetDouble(), 2));
                    if (Math.Abs(directionNorm - 1) > 1e-8) throw new InvalidDataException("Native control has no valid unit ray direction");
                }
                var bundle = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, group.Key.Hy, group.Key.Px, group.Key.Py, optic.Wavelengths[group.Key.Wave - 1].Micrometers);
                using var trace = optic.SequentialRayTracer.Trace(bundle, TraceRequest.Selected([1, 2, 3]));
                if (!trace.TryGetSample(0, 1, out var first)) throw new InvalidDataException("Missing first sample");
                var nativeFirst = group.Single(r => r.GetProperty("input").GetProperty("int1").GetInt32() == 1 && r.GetProperty("input").GetProperty("code").GetString() == "OPTH").GetProperty("value").GetDouble();
                foreach (var row in group)
                {
                    var p = row.GetProperty("input"); var si = p.GetProperty("int1").GetInt32(); var code = p.GetProperty("code").GetString();
                    if (!trace.TryGetSample(0, si, out var sample) || sample.Vignetted) throw new InvalidDataException("Incomplete Core ray");
                    var frame = optic.SurfaceGroup.Items[si].CoordinateSystem; var v = frame.ToLocalPoint(sample.Position); var d = frame.ToLocalDirection(sample.Direction);
                    var actual = code switch { "REAX" => v.X, "REAY" => v.Y, "REAZ" => v.Z, "REAA" => d.X, "REAB" => d.Y, "REAC" => d.Z,
                        "OPTH" => sample.PhaseInclusiveOpticalPathLength!.Value - first.PhaseInclusiveOpticalPathLength!.Value, _ => throw new InvalidDataException(code) };
                    var expected = row.GetProperty("value").GetDouble() - (code == "OPTH" ? nativeFirst : 0); var error = Math.Abs(actual - expected);
                    var direction = code is "REAA" or "REAB" or "REAC"; var tolerance = code == "OPTH" ? 1e-7 : direction ? 1e-11 : 1e-9;
                    if (error > tolerance) failures++; comparisons++;
                    if (code == "OPTH") maxPath = Math.Max(maxPath, error); else if (direction) maxDirection = Math.Max(maxDirection, error); else maxPosition = Math.Max(maxPosition, error);
                }
            }
            results.Add(new { id, conclusion = failures == 0 ? "Pass" : "Difference", comparisons, failures, maxSag, maxPosition, maxDirection, maxPath, coreSha256 = coreHash, sourceHash });
            JsonFiles.Write(Path.Combine(root, "results.json"), results);
            JsonFiles.Write(Path.Combine(root, "toroidal-native-v1.json"), new { authority = "OpticStudio 2026 R1 independent background surface-model controls", scope = "Toroidal rotation radius native PARM 1; planar and signed curvatures; actual glass dispersion", cases });
            Console.WriteLine($"{id}: {failures} failures / {comparisons} values");
        }
    }
}
