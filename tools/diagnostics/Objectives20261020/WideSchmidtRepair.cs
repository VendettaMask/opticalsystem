using System.Text.Json;
using System.Reflection;
using System.Numerics;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.FileIO;
using OpticalSystemDesign.Core.Geometries;
using OpticalSystemDesign.Core.Raytrace;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;

internal static class WideSchmidtRepair
{
    internal const string Baseline = "artifacts/zemax-sequential-corpus/20261021/full-v1";
    internal static readonly string[] Lenses = ["Objectives/Wide angle lens 200 degree field.zmx", "Objectives/Wide angle lens 210 degree field.zmx", "Telescopes/Flat-field Schmidt.zmx"];

    internal static void Recheck(string output, string scope)
    {
        Fresh(output);
        var snapshot = scope.StartsWith("snapshot:", StringComparison.Ordinal);
        if (snapshot) scope = scope[9..];
        using var all = JsonDocument.Parse(File.ReadAllText(Baseline + "/analysis-results.json"));
        var targets = new HashSet<string>();
        foreach (var file in new[] { "prior-pass-preservation.json", "objectives-prior-pass-preservation.json" })
        {
            using var audit = JsonDocument.Parse(File.ReadAllText("artifacts/zemax-sequential-corpus/20261021/" + file));
            foreach (var row in audit.RootElement.GetProperty("rows").EnumerateArray().Where(r => r.GetProperty("status").GetString() == "PreviouslyPassNowNotPass"))
                targets.Add(row.GetProperty("key").GetString()!);
        }
        var policyPath = "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json";
        var policy = ComparisonConfiguration.Load(policyPath);
        var results = new List<object>();
        var selectedIndex = 0;
        foreach (var item in all.RootElement.EnumerateArray())
        {
            var lens = item.GetProperty("lens").GetString()!;
            var original = item.GetProperty("run");
            if (original.GetProperty("request").ValueKind != JsonValueKind.Object) continue;
            var request = original.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
            var key = $"{lens}|{item.GetProperty("configuration").GetInt32()}|{item.GetProperty("scope").GetString()}|{request.CanonicalAnalysisKey}|{request.Field}|{request.Wavelength}|{request.PupilSampling}";
            if (!(scope == "targets" ? targets.Contains(key) : scope == "three" ? Lenses.Contains(lens)
                : scope.StartsWith("guard", StringComparison.Ordinal) ? targets.Contains(key) || original.GetProperty("conclusion").GetString() == "Pass"
                : scope == "passes" ? original.GetProperty("conclusion").GetString() == "Pass" : key.Contains(scope, StringComparison.OrdinalIgnoreCase))) continue;
            if (scope.StartsWith("guard-", StringComparison.Ordinal) && selectedIndex++ % 2 != int.Parse(scope[6..])) continue;
            var folder = Path.Combine(output, original.GetProperty("directory").GetString()!);
            Directory.CreateDirectory(folder);
            var run = new AnalysisRun { Key = request.CanonicalAnalysisKey, Directory = Path.GetFileName(folder), Request = request };
            string? error = null;
            try
            {
                var source = Path.Combine("src/OpticalSystemDesign.App/Assets/SequentialExamples", lens);
                var optic = snapshot ? Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(item.GetProperty("snapshot").GetString()!))
                    : WorkbenchExecutor.Import(source).GetAwaiter().GetResult().Configurations[request.Configuration - 1];
                var data = new WorkbenchRuntime(optic).BuildAnalysisData(request.CanonicalAnalysisKey, request.WorkbenchSettings);
                var dataPath = Path.Combine(folder, "data.json"); JsonFiles.Write(dataPath, data);
                var entry = AnalysisComparisonRegistry.Get(request.CanonicalAnalysisKey);
                var actual = ResultNormalizer.Workbench(dataPath, entry, request);
                var reference = ResultNormalizer.Zemax(Path.Combine(item.GetProperty("path").GetString()!, "native/data.json"), entry, request);
                ComparisonRunner.Compare(run, entry, policy.Analyses[request.CanonicalAnalysisKey], actual, reference, folder);
                JsonFiles.Write(Path.Combine(folder, "comparison.json"), run);
            }
            catch (Exception e) { error = e.ToString(); File.WriteAllText(Path.Combine(folder, "error.txt"), error); }
            results.Add(new { key, target = targets.Contains(key), previous = original.GetProperty("conclusion").GetString(), conclusion = error is null ? run.Conclusion.ToString() : "Error", metrics = run.Metrics, error });
            Console.WriteLine($"{results.Count}: {key}: {(error is null ? run.Conclusion.ToString() : "Error")}");
            JsonFiles.Write(Path.Combine(output, "summary.json"), results);
        }
        JsonFiles.Write(Path.Combine(output, "identity.json"), new { scope, targets = targets.Count, policySha256 = JsonFiles.Hash(File.ReadAllBytes(policyPath)), coreSha256 = JsonFiles.Hash(File.ReadAllBytes(typeof(StandardGeometry).Assembly.Location)) });
    }

    internal static void Stage(string output)
    {
        Fresh(output);
        var rows = new List<object>();
        foreach (var lens in Lenses.Take(2))
        {
            var id = lens.Contains("200") ? "064-wide-angle-lens-200-degree-field-c1-w2" : "065-wide-angle-lens-210-degree-field-c1-w2";
            var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine("src/OpticalSystemDesign.App/Assets/SequentialExamples", lens)), ".zmx");
            using var native = JsonDocument.Parse(File.ReadAllText($"{Baseline}/rays/{id}/native/ray-audit.json"));
            var probes = ApertureSampler.GenerateGaussianQuadrature(6, 12);
            var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0, 1, optic.Wavelengths[1].Micrometers, probes, aimAtStop: true);
            var history = optic.SequentialRayTracer.Trace(bundle).RayHistories[0];
            JsonFiles.Write(Path.Combine(output, id + "-current-ray.json"), history);
            var geometry = (StandardGeometry)optic.SurfaceGroup.Items[1].Geometry;
            var nativeIndex = native.RootElement.GetProperty("launches").GetArrayLength() - 72;
            foreach (var ray in native.RootElement.GetProperty("launches").EnumerateArray().Skip(nativeIndex).Take(1))
            {
                var scale = lens.Contains("200") ? 25.4 : 1;
                var point = V(ray, "x", "y", "z") * scale;
                var direction = V(ray, "l", "m", "n");
                // Native launch is on the first vertex plane. Move upstream to
                // exercise the actual forward intersection, retaining its line.
                var origin = point - direction * 1500;
                var hit = geometry.DistanceToIntersection(origin, direction);
                rows.Add(new { lens, geometry.Radius, origin, direction, hit, expected = native.RootElement.GetProperty("surfaces")[0].GetProperty("rays")[nativeIndex].Clone() });
            }
            var ownOrigin = optic.SurfaceGroup.Items[1].CoordinateSystem.ToLocalPoint(history[0].Position);
            var ownDirection = optic.SurfaceGroup.Items[1].CoordinateSystem.ToLocalDirection(history[0].Direction);
            rows.Add(new { lens, method = "same-launch-scalar-vs-batch", ownOrigin, ownDirection,
                scalar = geometry.DistanceToIntersection(ownOrigin, ownDirection), batch = history[1] });
        }
        JsonFiles.Write(Path.Combine(output, "intersections.json"), rows);
    }

    internal static void Schmidt(string output)
    {
        Fresh(output);
        const BindingFlags stat = BindingFlags.Static | BindingFlags.NonPublic;
        const BindingFlags inst = BindingFlags.Instance | BindingFlags.NonPublic;
        var optic = OpticalFormatCatalog.Import(File.ReadAllText(Path.Combine("src/OpticalSystemDesign.App/Assets/SequentialExamples", Lenses[2])), ".zmx");
        var wave = optic.Wavelengths[1];
        var marginalBundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(0, 0, wave.Micrometers,
            new PupilSample[] { new(0, 0, 1), new(0, 1, 1), new(0, -1, 1), new(1, 0, 1), new(-1, 0, 1) }, aimAtStop: false);
        var histories = optic.SequentialRayTracer.Trace(marginalBundle).RayHistories;
        JsonFiles.Write(Path.Combine(output, "marginals.json"), histories.Select(h => h.Select(s => new { s.SurfaceNumber,
            position = optic.SurfaceGroup.Items[s.SurfaceNumber].CoordinateSystem.ToLocalPoint(s.Position),
            direction = optic.SurfaceGroup.Items[s.SurfaceNumber].CoordinateSystem.ToLocalDirection(s.Direction),
            normal = optic.SurfaceGroup.Items[s.SurfaceNumber].Geometry.SurfaceNormal(optic.SurfaceGroup.Items[s.SurfaceNumber].CoordinateSystem.ToLocalPoint(s.Position)),
            s.RefractiveIndexBefore, s.RefractiveIndexAfter, s.Vignetted, s.Intensity })));
        object?[] probes = [optic, (0d, 0d), wave, false, 1d, null];
        var ok = typeof(DiffractionEngine).GetMethod("TryWorkingPupilDirections", stat)!.Invoke(null, probes);
        var d = (Vector3D[])probes[5]!;
        var chief = d[0];
        var cosineF = .5 / Math.Sqrt(d.Skip(1).Select(v => 1 - Math.Pow(chief.X * v.X + chief.Y * v.Y + chief.Z * v.Z, 2)).Average());
        var workingF = DiffractionEngine.WorkingFNumber(optic, (0, 0), wave);
        var sphere = typeof(DiffractionEngine).GetMethod("CumulativeEnergyReferenceSphere", stat)!.Invoke(null, [optic, (0d, 0d), wave]);
        JsonFiles.Write(Path.Combine(output, "cone.json"), new { ok, directions = d, cosineF, workingF });
        using var all = JsonDocument.Parse(File.ReadAllText(Baseline + "/analysis-results.json"));
        var item = all.RootElement.EnumerateArray().Single(i => i.GetProperty("lens").GetString() == Lenses[2] && i.GetProperty("run").GetProperty("key").GetString() == "Diffraction Encircled Energy");
        var request = item.GetProperty("run").GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
        var native = ResultNormalizer.Zemax(Path.Combine(item.GetProperty("path").GetString()!, "native/data.json"), AnalysisComparisonRegistry.Get(request.CanonicalAnalysisKey), request);
        var analysis = new DiffractionEncircledEnergyAnalysis(optic, 32, 64, 401, 2, 1, maximumDistanceMicrometers: 5);
        var rows = new List<object>();
        foreach (var scale in new[] { "cosine", "working" })
        foreach (var method in new[] { "base-clip", "coordinate-clip", "legacy", "101", "97" })
        {
            var f = scale == "cosine" ? cosineF : workingF;
            var pitch = wave.Micrometers * f / Math.Sqrt(32);
            var divisor = pitch / (wave.Micrometers * f * 30 / 64);
            var legacyPitch = (wave.Micrometers * .001 / Math.Sqrt(32)) * workingF;
            var step = method == "101" ? 1 / Math.Sqrt(32) : method == "97" ? Math.BitIncrement(1 / Math.Sqrt(32)) : 1 / (32 * legacyPitch / (wave.Micrometers * .001) / workingF);
            var grid = new PupilGridSpecification(32, true, true, method == "legacy" || method == "101" || method == "97" ? step * 15 : 1 / divisor);
            if (method == "base-clip")
            {
                typeof(PupilGridSpecification).GetProperty("CoordinateDivisor", inst)!.SetValue(grid, divisor);
                typeof(PupilGridSpecification).GetProperty("ClipInBaseGrid", inst)!.SetValue(grid, true);
            }
            else if (method != "coordinate-clip") typeof(PupilGridSpecification).GetProperty("ExplicitCoordinateStep", inst)!.SetValue(grid, step);
            typeof(PupilGridSpecification).GetProperty("UseCachedFftStopMap", inst)!.SetValue(grid, true);
            if (method == "legacy") pitch = legacyPitch * 1000;
            foreach (var ideal in new[] { true, false })
            {
                object?[] args = [optic, (0d, 0d), wave, 32, 64, false, true, 0d, null, null, true, ideal, false, wave, pitch, true, null, sphere, grid];
                var psf = (PsfResult)typeof(DiffractionEngine).GetMethod("ComputeFftPsfCore", stat)!.Invoke(null, args)!;
                var pupil = (Complex[,])args[16]!;
                var curve = EnergyGridControls.Curve(analysis, psf, pitch);
                var max = curve.Zip(native.Series[ideal ? 0 : 1].Y, (a, b) => Math.Abs(a.Y - b)).Max();
                rows.Add(new { scale, method, ideal, f, pitch, divisor, step, count = pupil.Cast<Complex>().Count(c => c.Magnitude > 0), max });
                Console.WriteLine($"{scale}/{method}/{ideal}: max={max:R}");
                JsonFiles.Write(Path.Combine(output, "rows.json"), rows);
            }
        }
    }
    private static Vector3D V(JsonElement e, string x, string y, string z) => new(e.GetProperty(x).GetDouble(), e.GetProperty(y).GetDouble(), e.GetProperty(z).GetDouble());
    private static void Fresh(string output) { if (Directory.Exists(output)) throw new IOException("Fresh output required."); Directory.CreateDirectory(output); }
}
