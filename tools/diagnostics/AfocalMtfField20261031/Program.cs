using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;
using OpticalSystemDesign.ZemaxComparison.Normalization;

if (args.Length != 3) throw new ArgumentException("prepare|compute|compare <repository> <fresh run root>");
var repository = Path.GetFullPath(args[1]); var root = Path.GetFullPath(args[2]);
var policyPath = Path.Combine(repository, "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json");
string Hash(string path) { using var s = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(s)); }
var coreHash = Hash(typeof(Optic).Assembly.Location);
if (args[0] == "probe")
{
    var row = JsonFiles.Read<Case[]>(Path.Combine(root, "cases.json")).Single(r => r.Id == "m1-huygens");
    var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(row.Snapshot));
    var type = typeof(Optic).Assembly.GetType("OpticalSystemDesign.Core.Analysis.MtfMethodEvaluator")!;
    var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
    var compute = type.GetMethod("ComputeHuygensPolychromaticPsf", flags)!;
    var sample = type.GetMethod("SampleHuygensMtf", flags)!;
    var results = new List<object>();
    foreach (var centroid in new[] { false, true }) foreach (var pitch in new[] { 0d, .02 })
    {
        var settings = new MtfComputationSettings(PupilSampling: 32, ImageSize: 32, PixelPitchMillimeters: pitch,
            ZemaxCompatible: true, UseZemaxHuygensSemantics: centroid);
        var psf = (PsfResult)compute.Invoke(null, new object[] { optic, (0d, 1d), new[] { optic.Wavelengths[1] }, settings, 0d })!;
        var mtf = DiffractionEngine.ComputePsfMtf(psf, true);
        var values = new[] { .1, .2, .4, .8, 1.6, 3.2 }.Select(f => (ValueTuple<double, double>)sample.Invoke(null, new object[] { mtf, 64, f, true })!).ToArray();
        results.Add(new { centroid, pitch, resolvedPitch = psf.SampleSpacingMicrometers, values = values.Select(v => new { T = v.Item1, S = v.Item2 }).ToArray() });
    }
    JsonFiles.Write(Path.Combine(root, "centering-probe-" + coreHash[..12] + ".json"), results);
    Console.WriteLine(JsonSerializer.Serialize(results)); return;
}
if (args[0] == "prepare")
{
    if (Directory.Exists(root)) throw new IOException("Fresh output required");
    Directory.CreateDirectory(Path.Combine(root, "native"));
    var prior = JsonFiles.Read<JsonElement>(Path.Combine(repository, "artifacts/validation/afocal-result-audit-20261031-final/items.json"));
    var models = prior.EnumerateArray().Where(r => r.GetProperty("set").GetString() == "original-150" && r.GetProperty("key").GetString() == "Huygens MTF").ToArray();
    if (models.Length != 5) throw new InvalidDataException("Original four files/five configurations required");
    var rows = new List<Case>(); var jobs = new List<object>();
    var frequencies = new[] { .1, .2, .4, .8, 1.6, 3.2 };
    for (var model = 0; model < models.Length; model++)
    {
        var row = models[model]; var basis = row.GetProperty("request").Deserialize<CanonicalAnalysisRequest>(JsonFiles.Options)!;
        var source = row.GetProperty("source").GetString()!; var snapshot = row.GetProperty("snapshot").GetString()!;
        if (Hash(source) != row.GetProperty("sourceSha256").GetString()) throw new InvalidDataException("Source changed");
        var unit = File.ReadLines(source).Select(l => l.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Single(t => t.Length > 0 && t[0] == "UNIT")[6];
        var nativeAngle = unit switch { "MR" => "Milliradian", "AM" => "ArcMinute", "AS" => "ArcSecond", _ => throw new InvalidDataException("Unverified angular unit") };
        var angleMrad = PhysicalNormalization.UnitScale(nativeAngle, "Milliradian");
        foreach (var method in new[] { "Fourier", "Huygens", "Geometric" })
        {
            var id = $"m{model + 1}-{method.ToLowerInvariant()}"; var key = method + " MTF vs Field";
            var workbench = new Dictionary<string, string>
            {
                ["Sampling"] = "32", ["PupilSampling"] = "32", ["ImageSampling"] = "32", ["ImageDeltaMilliradians"] = "0",
                ["ImageDeltaMicrometers"] = "0", ["WavelengthNumber"] = "0", ["FieldDensity"] = "10", ["ScanType"] = "+y",
                ["RemoveVignettingFactors"] = "True", ["ScaleByDiffractionLimit"] = "False", ["Distribution"] = "uniform", ["UsePolarization"] = "False"
            };
            var native = new Dictionary<string, object>
            {
                ["Wavelength"] = 0, ["UsePolarization"] = false, ["SampleSize"] = "S_32x32", ["FieldDensity"] = 10,
                ["ScanType"] = "Plus_Y", ["RemoveVignetting"] = true, ["UseDashes"] = false
            };
            for (var f = 0; f < 6; f++) { workbench["Frequency" + (f + 1)] = frequencies[f].ToString("R", CultureInfo.InvariantCulture); native["Freq_" + (f + 1)] = frequencies[f] * angleMrad; }
            if (method == "Huygens") native["ImageDelta"] = 0d;
            if (method == "Geometric") { native["MultiplyByDiffractionLimit"] = false; native["ScatterRays"] = false; }
            var request = basis with { CanonicalAnalysisKey = key, Wavelength = 0, NativeAfocalAngleUnit = nativeAngle,
                PupilSampling = 32, ImageSampling = 32, ImageDeltaMicrometers = 0, WorkbenchSettings = workbench, ZemaxSettings = native, FieldScanDirection = "+y", WavelengthScope = "All" };
            var settings = ComparisonConfiguration.Load(policyPath).Analyses[key];
            rows.Add(new(id, row.GetProperty("lens").GetString()!, source, Hash(source), snapshot, Hash(snapshot), request, settings.Quantities));
            var payload = JsonSerializer.SerializeToElement(request, JsonFiles.Options).EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());
            payload["input"] = source; payload["inputSha256"] = Hash(source); payload["zosApiPath"] = "D:/Program Files/ANSYS Inc/v261/Zemax OpticStudio";
            payload["zemaxVersion"] = "2026 R1"; payload["adapter"] = "contract"; payload["captureScreenshots"] = false; payload["formalAcceptanceRequest"] = false;
            payload["analysisType"] = method switch { "Fourier" => "FftMtfvsField", "Huygens" => "HuygensMtfvsField", _ => "GeometricMtfvsField" };
            if (source.Contains("Binocular", StringComparison.OrdinalIgnoreCase))
                payload["polygonDependencies"] = new[] { new { name = "PORRO_PRISM-LH.POB", sha256 = "8ba3b4bfe0092319a97e6169de6987132c0f9e2cda99f0b6a19a70eecc3a74f4" },
                    new { name = "PORRO_PRISM-RH.POB", sha256 = "c247ee42b9ec39fed6d0411e5f8b7c780ad3a6ff938bd6a642450d1e40dcef68" } };
            JsonFiles.Write(Path.Combine(root, "native", id + ".request.json"), payload);
            jobs.Add(new { id, request = id + ".request.json", inputSha256 = Hash(source) });
        }
    }
    JsonFiles.Write(Path.Combine(root, "cases.json"), rows); JsonFiles.Write(Path.Combine(root, "native/jobs.json"), jobs);
    JsonFiles.Write(Path.Combine(root, "identity.json"), new { coreHash, policyPath, policySha256 = Hash(policyPath), frequenciesCyclesPerMilliradian = frequencies,
        nativeFrequencyConversion = "Declared native angle unit to milliradian, no fitting", backgroundOnly = true });
    Console.WriteLine("Prepared 15 original-file/configuration/method cases"); return;
}
var cases = JsonFiles.Read<Case[]>(Path.Combine(root, "cases.json"));
if (args[0] == "compute")
{
    var output = Path.Combine(root, "core-" + coreHash[..12]); if (Directory.Exists(output)) throw new IOException("Fresh Core cohort output required");
    Directory.CreateDirectory(output); var status = new List<object>();
    foreach (var row in cases)
    {
        if (Hash(row.Source) != row.SourceSha256 || Hash(row.Snapshot) != row.SnapshotSha256) throw new InvalidDataException("Input changed");
        var dir = Path.Combine(output, row.Id); Directory.CreateDirectory(dir);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(row.Snapshot));
            var data = new WorkbenchRuntime(optic).BuildAnalysisData(row.Request.CanonicalAnalysisKey, row.Request.WorkbenchSettings, deadline.Token);
            JsonFiles.Write(Path.Combine(dir, "data.json"), data); status.Add(new { row.Id, data.Outcome, error = (string?)null });
            Console.WriteLine(row.Id + ": " + data.Outcome);
        }
        catch (Exception e) { status.Add(new { row.Id, Outcome = AnalysisOutcome.Unavailable, error = e.ToString() }); Console.WriteLine(row.Id + ": " + e.Message); }
        JsonFiles.Write(Path.Combine(output, "status.json"), status);
    }
    JsonFiles.Write(Path.Combine(output, "identity.json"), new { coreHash, applicationHash = Hash(typeof(WorkbenchRuntime).Assembly.Location), comparisonHash = Hash(typeof(ComparisonRunner).Assembly.Location) }); return;
}
if (args[0] != "compare") throw new ArgumentException("Unknown mode");
var coreRoot = Path.Combine(root, "core-" + coreHash[..12]);
var summary = new List<object>();
foreach (var row in cases)
{
    var native = Path.Combine(root, "native", row.Id, "data.json"); var actualPath = Path.Combine(coreRoot, row.Id, "data.json");
    var output = Path.Combine(coreRoot, row.Id, "comparison");
    if (!File.Exists(native) || !File.Exists(actualPath)) { summary.Add(new { row.Id, status = "MissingResult" }); continue; }
    try
    {
        var entry = AnalysisComparisonRegistry.Get(row.Request.CanonicalAnalysisKey);
        var w = ResultNormalizer.Workbench(actualPath, entry, row.Request); var z = ResultNormalizer.Zemax(native, entry, row.Request);
        var settings = ComparisonConfiguration.Load(policyPath).Analyses[row.Request.CanonicalAnalysisKey] with { Quantities = row.Gates };
        var run = new AnalysisRun { Key = row.Request.CanonicalAnalysisKey, Directory = row.Id, Request = row.Request };
        ComparisonRunner.Compare(run, entry, settings, w, z, output);
        var curves = new List<object>();
        foreach (var expected in z.Series)
        {
            var matched = ComparisonMetrics.Align(w.Series.Single(s => s.Id == expected.Id), expected, []); var gate = row.Gates[expected.YAxis.Quantity];
            var errors = matched.Workbench.Zip(matched.Zemax, (a, b) => Math.Abs(a - b)).ToArray();
            var violations = errors.Where((e, i) => e > gate.Absolute + gate.Relative * Math.Abs(matched.Zemax[i])).Count();
            curves.Add(new { expected.Id, count = errors.Length, maximum = errors.Max(), violations, gate });
        }
        summary.Add(new { row.Id, status = run.Conclusion.ToString(), run.Reason, curves });
        Console.WriteLine($"{row.Id}: {run.Conclusion}; {JsonSerializer.Serialize(curves)}");
    }
    catch (Exception e) { summary.Add(new { row.Id, status = "ComparisonUnavailable", error = e.ToString() }); Console.WriteLine(row.Id + ": " + e.Message); }
}
JsonFiles.Write(Path.Combine(coreRoot, "summary.json"), summary);

internal sealed record Case(string Id, string Lens, string Source, string SourceSha256, string Snapshot, string SnapshotSha256,
    CanonicalAnalysisRequest Request, Dictionary<string, Tolerances> Gates);
