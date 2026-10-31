using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;

if (args.Length != 3) throw new ArgumentException("compute32|compute64|compute128|assess <repository> <study-root>");
var repository = Path.GetFullPath(args[1]); var root = Path.GetFullPath(args[2]);
string Hash(string p) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p)));
var coreHash = Hash(typeof(Optic).Assembly.Location);
var cases = JsonFiles.Read<Case[]>(Path.Combine(root, "cases.json"));
double[] frequencies = [.1, .2, .4, .8, 1.6, 3.2];
if (args[0].StartsWith("compute", StringComparison.Ordinal))
{
    var n = int.Parse(args[0][7..]);
    if (n is not (32 or 64 or 128 or 256)) throw new ArgumentException("Bounded sampling required");
    var folder = Path.Combine(root, "core-" + coreHash[..12], "n" + n);
    if (Directory.Exists(folder)) throw new IOException("Fresh Core sampling cohort required");
    Directory.CreateDirectory(folder); var status = new List<object>();
    foreach (var row in cases)
    {
        if (Hash(row.Source) != row.SourceSha256 || Hash(row.Snapshot) != row.SnapshotSha256) throw new InvalidDataException("Original physical input changed");
        var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(row.Snapshot));
        var method = row.Id.EndsWith("fourier", StringComparison.Ordinal) ? MtfComputationMethod.Fourier : MtfComputationMethod.Huygens;
        var timer = Stopwatch.StartNew(); using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            var data = new MtfVsFieldAnalysis(optic, method, spatialFrequencies: frequencies, fieldPointCount: 10, wavelengthNumber: 0,
                removeVignettingFactors: true, zemaxCompatibleOutput: true,
                settings: new(PupilSampling: n, ImageSize: n, PixelPitchMillimeters: 0, ZemaxCompatible: true,
                    UseZemaxHuygensSemantics: method == MtfComputationMethod.Huygens)).GenerateData(deadline.Token);
            JsonFiles.Write(Path.Combine(folder, method.ToString().ToLowerInvariant() + ".json"), data);
            status.Add(new { method, sampling = n, data.Outcome, seconds = timer.Elapsed.TotalSeconds });
            Console.WriteLine($"{method}/N{n}: {data.Outcome}; {timer.Elapsed.TotalSeconds:F2}s");
        }
        catch (Exception error) { status.Add(new { method, sampling = n, error = error.ToString(), seconds = timer.Elapsed.TotalSeconds }); Console.WriteLine(error); }
        JsonFiles.Write(Path.Combine(folder, "status.json"), status);
    }
    JsonFiles.Write(Path.Combine(folder, "identity.json"), new { coreSha256 = coreHash, sampling = n });
    return;
}
if (args[0] != "assess") throw new ArgumentException("Unknown mode");
var policy = Path.Combine(repository, "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json");
var prep = JsonFiles.Read<JsonElement>(Path.Combine(root, "preparation.json"));
if (Hash(policy) != prep.GetProperty("policySha256").GetString()) throw new InvalidDataException("Original policy changed");
var configuration = ComparisonConfiguration.Load(policy);
var cohortPath = Path.Combine(root, "core-cohort.json");
var cohort = File.Exists(cohortPath) ? JsonFiles.Read<JsonElement>(cohortPath).GetProperty("coreSha256").GetString()! : coreHash;
var comparisons = new List<object>();
foreach (var method in new[] { "fourier", "huygens" })
{
    var title = method == "fourier" ? "Fourier MTF vs Field" : "Huygens MTF vs Field";
    var gate = configuration.Analyses[title].Quantities["Modulation"];
    for (var f = 0; f < 6; f++) for (var d = 0; d < 2; d++)
    {
        void Compare(string kind, Func<Series1DResult> a, Func<Series1DResult> b)
        {
            try
            {
                var aligned = ComparisonMetrics.Align(a(), b(), []);
                var metric = ComparisonMetrics.Calculate(kind, "Dimensionless", aligned, gate);
                var violations = aligned.Workbench.Zip(aligned.Zemax, (x, y) => Math.Abs(x - y) > gate.Absolute + gate.Relative * Math.Abs(y)).Count(v => v);
                comparisons.Add(new { method, frequency = frequencies[f], direction = d == 0 ? "T" : "S", kind, metric, pointwiseViolations = violations });
                Console.WriteLine($"{method}/{frequencies[f]}/{d}/{kind}: {metric.Conclusion}; NRMSE={metric.Nrmse:R}; max={metric.MaxAbsolute:R}");
            }
            catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException)
            { comparisons.Add(new { method, frequency = frequencies[f], direction = d == 0 ? "T" : "S", kind, error = error.Message }); }
        }
        Series1DResult Native(int n) => NativeCurves.Read(root, method, n, cases.Single(c => c.Id == "m1-" + method).SourceSha256, frequencies)[2 * f + d];
        Series1DResult Core(int n)
        {
            var folder = Path.Combine(root, "core-" + cohort[..12], "n" + n);
            if (JsonFiles.Read<JsonElement>(Path.Combine(folder, "identity.json")).GetProperty("coreSha256").GetString() != cohort) throw new InvalidDataException("Core cohort mismatch");
            var data = JsonFiles.Read<JsonElement>(Path.Combine(folder, method + ".json"));
            if (data.GetProperty("outcome").GetString() != "Success") throw new InvalidDataException("Core unavailable");
            var points = data.GetProperty("seriesList")[2 * f + d].GetProperty("points").EnumerateArray().ToArray();
            return new("curve", "Core", points.Select(v => v.GetProperty("x").GetDouble()).ToArray(), points.Select(v => v.GetProperty("y").GetDouble()).ToArray(),
                new("NormalizedField", "Dimensionless"), new("Modulation", "Dimensionless"));
        }
        Compare("Native64To128", () => Native(64), () => Native(128));
        Compare("Native128To256", () => Native(128), () => Native(256));
        Compare("Core32To64", () => Core(32), () => Core(64));
        Compare("Core32VsNative256", () => Core(32), () => Native(256));
        Compare("Core64VsNative256", () => Core(64), () => Native(256));
    }
}
JsonFiles.Write(Path.Combine(root, "assessment-" + cohort[..12] + ".json"), new { coreSha256 = cohort, policySha256 = Hash(policy), comparisons });
internal sealed record Case(string Id, string Source, string SourceSha256, string Snapshot, string SnapshotSha256);
