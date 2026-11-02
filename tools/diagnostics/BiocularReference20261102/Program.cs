using System.Security.Cryptography;
using System.Diagnostics;
using System.Text.Json;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Workbench;
using OpticalSystemDesign.ZemaxComparison.Zemax;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;

var mode = args[0]; var repo = Path.GetFullPath(args[1]); var output = Path.GetFullPath(args[2]);
if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Fresh output required.");
Directory.CreateDirectory(output);
var source = Path.Combine(repo, "src/OpticalSystemDesign.App/Assets/SequentialExamples/Miscellaneous/Bi-ocular lens.ZMX");
var imported = await WorkbenchExecutor.Import(source);
var coreHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(WavefrontEngine).Assembly.Location)));
if (mode == "phase-controls")
{
    var rows = new List<object>();
    foreach (var config in new[] { 1, 2 })
    {
        var optic = imported.Configurations[config - 1];
        var nativePath = Path.Combine(Path.GetDirectoryName(output)!, "native-phase-corrected", "c" + config, "model-controls.json");
        var nativeRows = JsonFiles.Read<JsonElement>(nativePath).GetProperty("rows").EnumerateArray().ToArray();
        foreach (var native in nativeRows.Where(r => r.GetProperty("input").GetProperty("code").GetString() == "OPTH"))
        {
            var input = native.GetProperty("input"); var w = input.GetProperty("int2").GetInt32(); var wave = optic.Wavelengths[w - 1];
            var px = input.GetProperty("data3").GetDouble(); var py = input.GetProperty("data4").GetDouble();
            var history = optic.SequentialRayTracer.Trace(optic.SequentialRayTracer.RayGenerator.GenerateGeneric(-1, 0, px, py, wave.Micrometers, true)).RayHistories.Single();
            var corePath = history.Last().CumulativeOpticalPathLength - history[1].CumulativeOpticalPathLength;
            var coreOpd = WavefrontEngine.GenerateChiefRaySamples(optic, (-1, 0), wave, [(px, py)], true).Samples.Single().OpdWaves;
            var ownSphere = WavefrontEngine.CreateChiefRayReferenceSphere(optic, (-1, 0), wave, true);
            var primarySphere = WavefrontEngine.CreateChiefRayReferenceSphere(optic, (-1, 0), optic.Wavelengths.First(w => w.IsPrimary), true);
            var primaryCentered = ownSphere with { CenterX = primarySphere.CenterX, CenterY = primarySphere.CenterY, CenterZ = primarySphere.CenterZ };
            var corePrimaryChiefOpd = WavefrontEngine.GenerateChiefRaySamples(optic, (-1, 0), wave, [(px, py)], true, referenceSphere: primaryCentered).Samples.Single().OpdWaves;
            var chiefHistory = optic.SequentialRayTracer.Trace(optic.SequentialRayTracer.RayGenerator.GenerateGeneric(-1, 0, 0, 0, wave.Micrometers, true)).RayHistories.Single();
            var incident = optic.SequentialRayTracer.RayGenerator.GenerateGeneric(-1, 0, px, py, wave.Micrometers, true).Rays.Single().Direction;
            var displacement = history[1].Position - chiefHistory[1].Position;
            var coreWavefrontPath = corePath + displacement.X * incident.X + displacement.Y * incident.Y + displacement.Z * incident.Z;
            var nativeOpd = nativeRows.Single(r => r.GetProperty("input").GetProperty("code").GetString() == "OPDX"
                && r.GetProperty("input").GetProperty("int2").GetInt32() == w
                && r.GetProperty("input").GetProperty("data3").GetDouble() == px
                && r.GetProperty("input").GetProperty("data4").GetDouble() == py).GetProperty("value").GetDouble();
            rows.Add(new { config, w, px, py, corePath, coreWavefrontPath, nativePath = native.GetProperty("value").GetDouble(), coreOpd, corePrimaryChiefOpd, nativeOpd, coreHash });
        }
    }
    JsonFiles.Write(Path.Combine(output, "results.json"), rows); return;
}
if (mode == "native-phase")
{
    using var lease = new FileStream(Path.Combine(repo, "artifacts/validation/.background-small-native-controls.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    var api = ZemaxExecutor.Locate(null); await ZemaxExecutor.Build(api, output, CancellationToken.None);
    var host = Path.Combine(output, "host/ZemaxHost.exe"); var records = new List<object>();
    foreach (var config in new[] { 1, 2 })
    {
        var processes = Process.GetProcessesByName("OpticStudio"); var count = processes.Length; foreach (var p in processes) p.Dispose();
        if (count > 1) throw new InvalidOperationException("Independent native slot unavailable.");
        var operands = new List<object>();
        object Operand(string code, int a, int b, double hx, double hy, double px, double py) => new { code, int1 = a, int2 = b, data1 = hx, data2 = hy, data3 = px, data4 = py, data5 = 0d, data6 = 0d };
        operands.Add(Operand("EXPP", 0, 0, 0, 0, 0, 0));
        foreach (var w in new[] { 1, 2, 3 }) foreach (var p in new[] { (0d, 0d), (-1d, 0d), (-.5, 0d), (.5, 0d), (1d, 0d), (0d, -.5), (0d, .5) })
        {
            operands.Add(Operand("OPDX", 0, w, -1, 0, p.Item1, p.Item2));
            operands.Add(Operand("OPTH", 11, w, -1, 0, p.Item1, p.Item2));
        }
        var folder = Path.Combine(output, "c" + config); Directory.CreateDirectory(folder);
        var request = Path.Combine(folder, "request.json");
        JsonFiles.Write(request, new { zosApiPath = api, input = source, adapter = "surface-model-controls", configuration = config,
            zemaxVersion = "2026 R1", captureScreenshots = false, formalAcceptanceRequest = false, modelOperands = operands });
        var result = await ProcessIsolation.Run(host, [request, folder], Path.GetDirectoryName(host)!, 120, CancellationToken.None,
            new Dictionary<string, string> { ["PATH"] = api + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH") });
        File.WriteAllText(Path.Combine(folder, "process.log"), result.StandardOutput + result.StandardError);
        records.Add(new { config, result, coreHash, sourceSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source))) });
        JsonFiles.Write(Path.Combine(output, "executions.json"), records);
        if (result.ExitCode != 0) throw new InvalidOperationException("Native control failed; retained.");
    }
    return;
}
if (mode is "mtf-convergence" or "mtf-convergence-replay")
{
    using var lease = new FileStream(Path.Combine(repo, "artifacts/validation/.background-small-native-controls.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    var native = mode == "mtf-convergence" ? await ZemaxExecutor.Build(ZemaxExecutor.Locate(null), output, CancellationToken.None) : null;
    var all = JsonFiles.Read<AnalysisRow[]>(Path.Combine(repo, "artifacts/validation/all-example-accuracy-20261031/rows.json"));
    var comparisons = new List<object>();
    foreach (var index in new[] { 1013, 1043 })
    {
        var row = all.Single(r => r.Index == index); var entry = AnalysisComparisonRegistry.Get(row.Key);
        var optic = imported.Configurations[row.Configuration - 1];
        var snapshot = Path.Combine(output, index.ToString(), "source.optic.json"); JsonFiles.Write(snapshot, optic.ToSnapshot());
        string? previousCore = null, previousNative = null; CanonicalAnalysisRequest? previous = null;
        foreach (var sampling in new[] { 64, 128, 256 })
        {
            var original = row.Run.Request!;
            var request = original with { PupilSampling = sampling, GridSize = sampling, SurfaceCount = optic.SurfaceGroup.Items.Count,
                WorkbenchSettings = new Dictionary<string, string>(original.WorkbenchSettings) { ["Sampling"] = sampling.ToString() } };
            var folder = Path.Combine(output, index.ToString(), sampling.ToString());
            var core = Path.Combine(folder, "core"); var job = new WorkbenchJob(snapshot, core, request);
            JsonFiles.Write(Path.Combine(folder, "request.json"), job); WorkbenchExecutor.Execute(job);
            var reference = Path.Combine(row.LegacyPath, "native");
            if (sampling > 64 && mode == "mtf-convergence-replay")
                reference = Path.Combine(Path.GetDirectoryName(output)!, "mtf-convergence", index.ToString(), sampling.ToString(), "native");
            else if (sampling > 64)
            {
                var processes = Process.GetProcessesByName("OpticStudio"); var count = processes.Length; foreach (var p in processes) p.Dispose();
                if (count > 1) throw new InvalidOperationException("Independent native slot unavailable.");
                reference = Path.Combine(folder, "native");
                var capture = await native!.Capture(source, reference, "2026 R1", entry.ZemaxSettingsMapper, entry.ZemaxAnalysisType, request, row.Configuration, 120, false, CancellationToken.None);
                JsonFiles.Write(Path.Combine(folder, "execution.json"), new { capture, coreHash });
                if (capture.ExitCode != 0) throw new InvalidOperationException("Native capture failed; retained.");
            }
            Compare("same-sampling", core, false, request, reference, true, request);
            if (previous is not null)
            {
                Compare("core-convergence", previousCore!, false, previous, core, false, request);
                Compare("native-convergence", previousNative!, true, previous, reference, true, request);
            }
            previous = request; previousCore = core; previousNative = reference;
            void Compare(string kind, string a, bool nativeA, CanonicalAnalysisRequest ra, string b, bool nativeB, CanonicalAnalysisRequest rb)
            {
                var x = nativeA ? ResultNormalizer.Zemax(Path.Combine(a, "data.json"), entry, ra) : ResultNormalizer.Workbench(Path.Combine(a, "data.json"), entry, ra);
                var y = nativeB ? ResultNormalizer.Zemax(Path.Combine(b, "data.json"), entry, rb) : ResultNormalizer.Workbench(Path.Combine(b, "data.json"), entry, rb);
                var destination = Path.Combine(output, "comparisons", $"{index}-{sampling}-{kind}");
                var run = new AnalysisRun { Key = row.Key, Directory = destination, Request = rb, Tolerances = row.Run.Tolerances, Support = row.Run.Support };
                ComparisonRunner.Compare(run, entry, row.Settings, x, y, destination);
                JsonFiles.Write(Path.Combine(destination, "comparison.json"), run);
                comparisons.Add(new { index, sampling, kind, run.Conclusion, run.Metrics, a, b, coreHash });
                JsonFiles.Write(Path.Combine(output, "results.json"), comparisons); Console.WriteLine($"{index} N{sampling} {kind}: {run.Conclusion}");
            }
        }
    }
    return;
}
if (mode == "mtf")
{
    var results = new List<object>();
    foreach (var config in new[] { 1, 2 }) foreach (var sampling in new[] { 64, 128, 256 })
    {
        var optic = imported.Configurations[config - 1]; var wave = optic.Wavelengths.First(w => w.IsPrimary);
        var psf = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, sampling, 2 * sampling,
            cellCenteredPupil: true, zemaxFftSampling: true, imageDelta: -1, referenceWavelength: wave) with { FrequencySampleCount = sampling - 1 };
        var frame = optic.SurfaceGroup.Items[^1].CoordinateSystem;
        var rays = new[] { (0d, 0d), (0d, 1d), (0d, -1d), (1d, 0d), (-1d, 0d) }.Select(p =>
            optic.SequentialRayTracer.DiagnoseApertures(optic.SequentialRayTracer.RayGenerator.GenerateGeneric(0, 0, p.Item1, p.Item2, wave.Micrometers, true).Rays[0]).UnclippedImage!).ToArray();
        var directions = rays.Select(r => frame.ToLocalDirection(r.Direction)).ToArray();
        var index = optic.SurfaceGroup.Items[^1].MaterialAfter.RefractiveIndex(wave.Nanometers);
        var t = 1 / (index * (Math.Abs(directions[1].Y - directions[0].Y) + Math.Abs(directions[2].Y - directions[0].Y)));
        var s = 1 / (index * (Math.Abs(directions[3].X - directions[0].X) + Math.Abs(directions[4].X - directions[0].X)));
        foreach (var (name, sourcePsf) in new[] { ("current", psf), ("projected-cosines", psf with { TangentialWorkingFNumber = t, SagittalWorkingFNumber = s }) })
        {
            var mtf = DiffractionEngine.ComputeFftMtf(sourcePsf, optic, wave);
            var values = new[] { 10d, 20, 30, 50 }.Select(f => new { frequency = f, t = Interpolate(mtf.TangentialFrequency!, mtf.Tangential, f), s = Interpolate(mtf.SagittalFrequency!, mtf.Sagittal, f) }).ToArray();
            results.Add(new { config, sampling, name, psf.TangentialWorkingFNumber, psf.SagittalWorkingFNumber, projectedT = t, projectedS = s, chief = directions[0], values, coreHash });
            Console.WriteLine($"c{config} N{sampling} {name}: S50={values[^1].s:G9}");
        }
    }
    JsonFiles.Write(Path.Combine(output, "results.json"), results); return;
}
if (mode == "sphere")
{
    var all = JsonFiles.Read<JsonElement[]>(Path.Combine(repo, "artifacts/validation/all-example-accuracy-20261031/rows.json"));
    var results = new List<object>();
    for (var c = 0; c < 2; c++) foreach (var wave in imported.Configurations[c].Wavelengths)
    {
        var optic = imported.Configurations[c];
        var w = optic.Wavelengths.IndexOf(wave) + 1;
        var row = all.Single(r => r.GetProperty("lens").GetString() == "Miscellaneous/Bi-ocular lens.ZMX"
            && r.GetProperty("configuration").GetInt32() == c + 1 && r.GetProperty("key").GetString() == "Optical Path Difference"
            && r.GetProperty("run").GetProperty("request").GetProperty("field").GetInt32() == 4
            && r.GetProperty("run").GetProperty("request").GetProperty("wavelength").GetInt32() == w);
        var referencePath = Path.Combine(row.GetProperty("legacyPath").GetString()!, "native/data.json");
        var native = JsonFiles.Read<JsonElement>(referencePath).GetProperty("dataSeries");
        var knots = native[0].GetProperty("x").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        var pupils = knots.Select(x => (X: 0d, Y: x)).Concat(knots.Select(x => (X: x, Y: 0d))).ToArray();
        var expected = native.EnumerateArray().SelectMany(s => s.GetProperty("y").EnumerateArray().Select(y => y[0].GetDouble())).ToArray();
        var chief = optic.SequentialRayTracer.TraceFinalSamples(optic.SequentialRayTracer.RayGenerator.GenerateGeneric(-1, 0, 0, 0, wave.Micrometers, true)).Single()!;
        var sphere = WavefrontEngine.CreateChiefRayReferenceSphere(optic, (-1, 0), wave, true);
        var pb = ParabasalPupilEngine.Compute(optic, wave);
        var primaryPb = ParabasalPupilEngine.Compute(optic, optic.Wavelengths.First(w => w.IsPrimary));
        var frame = optic.SurfaceGroup.Items[^1].CoordinateSystem;
        var distance = optic.Paraxial.EstimateExitPupilLocation(wave.Micrometers);
        var variants = new[] {
            ("current", sphere),
            ("chief-to-paraxial-plane", sphere with { Radius = Math.Abs(distance / frame.ToLocalDirection(chief.Direction).Z) }),
            ("parabasal-point", sphere with { Radius = (chief.Position - pb.ConjugatePosition).Length }),
            ("chief-to-parabasal-plane", sphere with { Radius = Math.Abs(pb.ImageAxialDistanceMillimeters / frame.ToLocalDirection(chief.Direction).Z) })
            ,("chief-to-primary-parabasal-plane", sphere with { Radius = Math.Abs(primaryPb.ImageAxialDistanceMillimeters / frame.ToLocalDirection(chief.Direction).Z) })
        };
        foreach (var (name, candidate) in variants)
        {
            var values = WavefrontEngine.GenerateChiefRaySamples(optic, (-1, 0), wave, pupils, true, referenceSphere: candidate).Samples.Select(s => s.OpdWaves).ToArray();
            var range = expected.Max() - expected.Min();
            var errors = values.Zip(expected, (a, b) => a - b).ToArray();
            var nrmse = Math.Sqrt(errors.Sum(e => e * e) / errors.Length) / range;
            results.Add(new { c = c + 1, w, name, nrmse, maximum = errors.Max(Math.Abs), candidate, pb, distance, chief = chief.Position, direction = chief.Direction, values, coreHash });
            Console.WriteLine($"c{c + 1} w{w} {name}: {nrmse:G8}, max={errors.Max(Math.Abs):G8}");
        }
    }
    JsonFiles.Write(Path.Combine(output, "results.json"), results); return;
}
throw new ArgumentException("Unknown mode");

static double Interpolate(IReadOnlyList<double> x, IReadOnlyList<double> y, double f)
{
    var i = 0; while (i < x.Count - 1 && x[i] < f) i++; if (i == 0) return y[0];
    var fraction = (f - x[i - 1]) / (x[i] - x[i - 1]); return y[i - 1] * (1 - fraction) + y[i] * fraction;
}

internal sealed record AnalysisRow(int Index, string Key, string Lens, int Configuration, string LegacyPath, AnalysisConfiguration Settings, AnalysisRun Run);
