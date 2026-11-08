using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;

var root = Path.GetFullPath(args[0]);
var directory = Path.GetFullPath(args[1]);
var nativePath = Path.Combine(directory, "native", "direct-grin.json");
var policy = Path.Combine(Path.GetFullPath(args[2]), "tools", "OpticalSystemDesign.ZemaxComparison", "comparison-settings.json");
var source = Path.Combine(directory, "GRIN interconnect.ZMX");
var raw = JsonFiles.Read<JsonElement>(nativePath);
var wavelength = raw.GetProperty("wavelength").GetInt32();
var originalStep = Number(raw, "originalStep");
var objectDistance = Number(raw, "objectDistance");
var rodLength = Number(raw, "rodLength");
var index = Number(raw, "surface1BaseIndex");
var radial = Number(raw, "surface1RadialQuadratic");
var g = Math.Sqrt(-2 * radial / index);
var analyticEfl = 1 / (index * g * Math.Sin(rodLength * g));
var rows = JsonFiles.Read<JsonElement>(Path.Combine(root, "rows.json"));
var row = rows.EnumerateArray().Single(x => x.GetProperty("index").GetInt32() == 825);
var frozenDataPath = Path.Combine(row.GetProperty("legacyPath").GetString()!, "native", "data.json");
var frozenData = JsonFiles.Read<JsonElement>(frozenDataPath);
var frozenSource = frozenData.GetProperty("metadata").GetProperty("LensFile").GetString()!;
if (Hash(source) != Hash(frozenSource))
    throw new InvalidDataException("Menu and frozen source prescriptions differ.");
var jobPath = row.GetProperty("job").GetString()!;
var job = JsonFiles.Read<WorkbenchJob>(jobPath);
var configuration = row.GetProperty("settings").Deserialize<AnalysisConfiguration>(JsonFiles.Options)!;
var entry = AnalysisComparisonRegistry.Get("First Order");
if (job.Request.Wavelength != wavelength || job.Request.Configuration != 1
    || !configuration.Quantities.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(["EffectiveFocalLength", "FNumber"]))
    throw new InvalidDataException("Original analysis settings or quantity coverage differ.");

var samples = raw.GetProperty("launch").GetArrayLength();
var records = raw.GetProperty("records").EnumerateArray().Select(record =>
{
    var input = record.GetProperty("surfaces")[0].GetProperty("rays").EnumerateArray().ToArray();
    var output = record.GetProperty("surfaces")[1].GetProperty("rays").EnumerateArray().ToArray();
    if (input.Length != samples || output.Length != samples
        || input.Concat(output).Any(ray => Number(ray, "error") != 0 || Number(ray, "vignette") != 0)
        || !input.Select(x => Number(x, "number")).SequenceEqual(output.Select(x => Number(x, "number"))))
        throw new InvalidDataException("Unaligned, incomplete or invalid native rays.");

    var perturbations = Enumerable.Range(0, 6).Select(i =>
    {
        var left = input[2 * i];
        var right = input[2 * i + 1];
        var height = Number(right, "y");
        var span = height - Number(left, "y");
        if (span != 2 * height || height <= 0 || Number(left, "m") != 0 || Number(right, "m") != 0)
            throw new InvalidDataException("Invalid symmetric collimated height perturbation.");
        var c = (Slope(output[2 * i + 1]) - Slope(output[2 * i])) / span;
        return new { height, c, focalLength = -1 / c };
    }).ToArray();
    var extrapolations = Enumerable.Range(1, perturbations.Length - 1).Select(i =>
    {
        var low = perturbations[i];
        var high = perturbations[i - 1];
        if (high.height != 2 * low.height) throw new InvalidDataException("Height step did not halve.");
        var c = (4 * low.c - high.c) / 3;
        return new { high.height, fineHeight = low.height, c, focalLength = -1 / c };
    }).ToArray();

    var pupil = Number(input[^1], "y") - Number(input[^2], "y");
    if (!(pupil > 0)) throw new InvalidDataException("Invalid marginal-pupil diameter.");
    var effectiveFocalLength = extrapolations[^1].focalLength;
    return new
    {
        maximumStep = Number(record, "maximumStep"),
        nativeBuiltInEfl = Number(record, "effl"),
        nativeBuiltInEpdi = Number(record, "entrancePupilDiameter"),
        perturbations,
        extrapolations,
        directRayEfl = effectiveFocalLength,
        directRayFNumber = effectiveFocalLength / pupil,
        directRayEpdi = pupil
    };
}).ToArray();

NumericResult Reference(double efl, double fNumber) => new()
{
    Scalars = [new("EffectiveFocalLength", efl, "Millimeter"), new("FNumber", fNumber, "Dimensionless")]
};

var coarse = records[^2];
var fine = records[^1];
var outputDirectory = Path.Combine(directory, "replay");
WorkbenchExecutor.Execute(job with { OutputDirectory = outputDirectory });
var workbenchPath = Path.Combine(outputDirectory, "data.json");
var workbench = ResultNormalizer.Workbench(workbenchPath, entry, job.Request);
var reference = Reference(fine.directRayEfl, fine.directRayFNumber);
var previousHeightEfl = fine.extrapolations[^2].focalLength;
var comparisons = new[]
{
    Compare("native-step", Reference(coarse.directRayEfl, coarse.directRayFNumber), reference),
    Compare("native-height", Reference(previousHeightEfl, previousHeightEfl / fine.directRayEpdi), reference),
    Compare("workbench-native", workbench, reference),
    Compare("workbench-built-in", workbench,
        Reference(fine.nativeBuiltInEfl, fine.nativeBuiltInEfl / fine.nativeBuiltInEpdi)),
    Compare("workbench-frozen", workbench, ResultNormalizer.Zemax(frozenDataPath, entry, job.Request, workbenchPath))
};
var pass = comparisons.Take(3).All(run => run.Conclusion == Conclusion.Pass);
JsonFiles.Write(Path.Combine(directory, "verification.json"), new
{
    index = 825,
    lens = "Interconnects/GRIN interconnect.ZMX",
    canonicalRequestFingerprint = job.Request.Fingerprint,
    configuration = job.Request.Configuration,
    wavelength,
    sourceSha256 = Hash(source),
    frozenSourceSha256 = Hash(frozenSource),
    frozenNativeReportSha256 = Hash(frozenDataPath),
    snapshotSha256 = Hash(job.SnapshotPath),
    nativeRayCaptureSha256 = Hash(nativePath),
    policySha256 = Hash(policy),
    coreSha256 = Hash(typeof(Optic).Assembly.Location),
    workbenchReportSha256 = Hash(workbenchPath),
    originalStep,
    objectDistance,
    rodLength,
    baseIndex = index,
    radialQuadratic = radial,
    analyticEfl,
    analyticError = Math.Abs(fine.directRayEfl - analyticEfl),
    directRayCount = samples * records.Length,
    nativeSurfaceRecords = samples * 2 * records.Length,
    nativeCaptureStatus = "Host.Main exit 0; post-capture native batch teardown returned FRU__delta_init internal error",
    originalTolerances = configuration.Quantities,
    records,
    workbench = workbench.Scalars,
    comparisons = comparisons.Select(run => new { check = run.Directory, run.Conclusion, run.Metrics }),
    independentPass = pass,
    frozenVerdict = comparisons[^1].Conclusion
});
foreach (var run in comparisons)
    Console.WriteLine($"{run.Directory}: {run.Conclusion} {run.Reason}");
return pass ? 0 : 1;

AnalysisRun Compare(string id, NumericResult actual, NumericResult expected)
{
    var run = new AnalysisRun
    {
        Directory = id,
        Key = job.Request.CanonicalAnalysisKey,
        Request = job.Request,
        WorkbenchStatus = CaptureStatus.Captured,
        ZemaxStatus = CaptureStatus.Captured
    };
    ComparisonRunner.Compare(run, entry, configuration, actual, expected, outputDirectory);
    return run;
}

static double Number(JsonElement value, string key) => value.GetProperty(key).GetDouble();
static double Slope(JsonElement ray) => Number(ray, "m") / Number(ray, "n");
static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));


