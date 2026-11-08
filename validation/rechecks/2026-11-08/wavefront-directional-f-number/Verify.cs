using System.IO.Compression;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Backend;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Metrics;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.ZemaxComparison.Workbench;

var root = Path.GetFullPath(args[0]);
var work = Path.GetFullPath(args[1]);
var replay = Path.GetFullPath(args[2]);
var rows = JsonFiles.Read<JsonElement[]>(Path.Combine(replay, "rows.json"));
var controls = Path.Combine(root, "artifacts/validation/axis-controls-20261108");
var evidence = Path.Combine(root, "validation/rechecks/2026-11-07/curvature-image-space-reference/native-controls.zip");
using var archive = ZipFile.OpenRead(evidence);
var wavefront = new[] { VerifyWavefront(1070), VerifyWavefront(1246) };
var mtf = new[] { VerifyMtf(1073, "cylinder-axis"), VerifyMtf(1249, "mems-original") };
var etalon = new[] { VerifyEtalon(2216), VerifyEtalon(2220) };
JsonFiles.Write(Path.Combine(work, "verification.json"), new
{
    coreSha256 = Hash(typeof(Optic).Assembly.Location),
    originalPolicySha256 = Hash(Path.Combine(root, "tools/OpticalSystemDesign.ZemaxComparison/comparison-settings.json")),
    etalonEvidenceArchiveSha256 = Hash(evidence),
    wavefront,
    mtf,
    etalon
});

object VerifyWavefront(int index)
{
    var (job, path) = Replay(index);
    var values = Read(path).GetProperty("values");
    var grid = ResultNormalizer.Workbench(path, AnalysisComparisonRegistry.Get("Wavefront"), job.Request).Grids.Single();
    var meridianPath = Path.Combine(controls, $"{index}-meridian", "point-reference.json");
    var native = Read(meridianPath);
    var rays = ImageRays(native);
    var inputs = native.GetProperty("inputs").EnumerateArray().ToArray();
    if (inputs.Length != 63 || inputs.Length != rays.Length || !rays.All(Valid))
        throw new InvalidDataException("Incomplete native meridian.");
    var zero = Array.IndexOf(grid.X, 0d);
    if (grid.Z.Any(row => row.Where((_, x) => x != zero).Any(v => v.HasValue)))
        throw new InvalidDataException("The formal projection contains off-meridian samples.");
    var result = grid with { Z = grid.Y.Select(y =>
    {
        var row = new double?[grid.X.Length];
        var k = Array.FindIndex(inputs, p => N(p, "px") == 0 && Math.Abs(N(p, "py") - y) < 1e-12);
        if (k >= 0) row[zero] = N(rays[k], "opdWaves");
        return row;
    }).ToArray() };
    var metric = ComparisonMetrics.Calculate("Wavefront", "Wave", ComparisonMetrics.Align(grid, result, []), Gate(index, "WavefrontError"));
    var operandPath = Path.Combine(controls, $"{index}-working-f-number", "model-controls.json");
    var operands = Read(operandPath).GetProperty("rows");
    var nativeX = N(operands[0], "value");
    var nativeY = N(operands[1], "value");
    var nativeProjectionX = nativeY / nativeX;
    var workbenchProjectionX = N(values, "ExitPupilDisplayScaleX");
    return new { index, request = job.Request.Fingerprint, nativeX, nativeY, nativeProjectionX, workbenchProjectionX,
        workbenchX = N(values, "ExitPupilWorkingFNumberX"), workbenchY = N(values, "ExitPupilWorkingFNumberY"),
        projectionAbsoluteError = Math.Abs(nativeProjectionX - workbenchProjectionX), metric,
        sourceSha256 = Read(Path.Combine(controls, $"{index}-meridian.json")).GetProperty("inputSha256").GetString(),
        meridianSha256 = Hash(meridianPath), nativeOperandsSha256 = Hash(operandPath), workbenchSha256 = Hash(path) };
}

object VerifyMtf(int index, string nativeId)
{
    var row = rows[index - 1];
    var job = JsonFiles.Read<WorkbenchJob>(row.GetProperty("job").GetString()!);
    var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(job.SnapshotPath));
    var nativePath = Path.Combine(work, nativeId, "point-reference.json");
    var native = Read(nativePath);
    var rays = ImageRays(native);
    var inputs = native.GetProperty("inputs").EnumerateArray().ToArray();
    if (rays.Length != inputs.Length || !rays.All(Valid)
        || inputs.Any(p => N(p, "hx") != 0 || N(p, "hy") != 0)
        || job.Request is not { Field: 1, Wavelength: 1, ImageSpaceAfocal: false })
        throw new InvalidDataException("Expected complete on-axis focal controls.");
    var chief = Direction(rays[0]);
    var sagittalAngularSpan = Enumerable.Range(1, rays.Length - 1)
        .Where(i => N(inputs[i], "py") == 0).Max(i => Math.Abs(Direction(rays[i]).X - chief.X));
    var tangentialAngularSpan = Enumerable.Range(1, rays.Length - 1)
        .Where(i => N(inputs[i], "px") == 0).Max(i => Math.Abs(Direction(rays[i]).Y - chief.Y));
    string? workbenchUnavailableReason;
    try
    {
        var (_, path) = Replay(index);
        workbenchUnavailableReason = Read(path).GetProperty("outcomeReason").GetString();
    }
    catch (AnalysisDataUnavailableException e)
    {
        workbenchUnavailableReason = e.Message;
    }
    if (sagittalAngularSpan > 1e-14 || tangentialAngularSpan <= 0
        || workbenchUnavailableReason?.Contains("frequency scale", StringComparison.Ordinal) != true)
        throw new InvalidDataException("Expected a rank-deficient focal frequency scale.");
    var nativeFrozenPath = Path.Combine(row.GetProperty("legacyPath").GetString()!, "native", "data.json");
    return new { index, request = job.Request.Fingerprint, sagittalAngularSpan, tangentialAngularSpan, nativeImageRays = rays.Length,
        nativeRaySha256 = Hash(nativePath), nativeFrozenSha256 = Hash(nativeFrozenPath),
        nativeFrozenDiagnostics = Read(nativeFrozenPath).GetProperty("messages"), workbenchUnavailableReason,
        zeroSagittalFrequencySupport = true, wavelengthMicrometers = optic.Wavelengths[0].Micrometers };
}

object VerifyEtalon(int index)
{
    var (job, path) = Replay(index);
    var core = ResultNormalizer.Workbench(path, AnalysisComparisonRegistry.Get("Optical Path Difference"), job.Request).Series;
    var absolute = ReadZip($"native/{index}-absolute-rays/point-reference.json");
    var focal = ReadZip($"native/{index}-rays/point-reference.json");
    var model = ReadZip($"native/{index}-rays/model.json");
    var surface = model.GetProperty("surfaces")[1];
    if (!surface.GetProperty("IsStop").GetBoolean() || N(surface, "Thickness") != 0
        || model.GetProperty("advanced").GetProperty("ReferenceOPD").GetString() != "ExitPupil"
        || model.GetProperty("aperture").GetProperty("AFocalImageSpace").GetBoolean())
        throw new InvalidDataException("Expected the original contact-plane focal model.");
    var rays = ImageRays(focal);
    var abs = ImageRays(absolute);
    var inputs = focal.GetProperty("inputs").EnumerateArray().ToArray();
    if (rays.Length != 82 || abs.Length != rays.Length || !rays.Concat(abs).All(Valid)
        || focal.GetProperty("inputs").GetRawText() != absolute.GetProperty("inputs").GetRawText())
        throw new InvalidDataException("Incomplete or different focal and absolute ray inputs.");
    var wave = N(model.GetProperty("wavelengths")[job.Request.Wavelength - 1].GetProperty("data"), "Wavelength") * .001;
    var chiefIndex = Array.FindIndex(inputs, p => N(p, "px") == 0 && N(p, "py") == 0);
    var chiefDirection = Direction(rays[chiefIndex]);
    var chiefPosition = Position(rays[chiefIndex]);
    var nodes = Enumerable.Range(0, rays.Length).Select(i =>
    {
        if (Direction(rays[i]) != chiefDirection || Direction(abs[i]) != chiefDirection || Position(rays[i]) != Position(abs[i]))
            throw new InvalidDataException("Matched native real rays are not parallel or their image geometry changed.");
        var displacement = Position(rays[i]) - chiefPosition;
        var axial = Dot(displacement, chiefDirection);
        var transverseSquared = Math.Max(0, Dot(displacement, displacement) - axial * axial);
        return new { index = i, px = N(inputs[i], "px"), py = N(inputs[i], "py"),
            axial, transverseSquared, planeWaves = N(abs[i], "opdWaves") + axial / wave,
            nativeReportedWaves = N(rays[i], "opdWaves"), absoluteWaves = N(abs[i], "opdWaves") };
    }).ToArray();
    // The 1e10 mm correction explains the frozen quantization, but one observed
    // ULP identifies only a binary exponent bin, not an exact native radius.
    var roundingScale = 1e10;
    var ulp = Math.BitIncrement(roundingScale) - roundingScale;
    var minSurrogateRadius = Math.ScaleB(1, Math.ILogB(roundingScale));
    var gate = Gate(index, "WavefrontError");
    var comparisons = new[] { minSurrogateRadius, roundingScale, 2 * minSurrogateRadius }.SelectMany(radius =>
        new[] { -1, 1 }.Select(sign =>
        {
            var metrics = core.Select(curve =>
            {
                var selected = nodes.Skip(curve.Id == "sagittal" ? 0 : 41).Take(41).ToArray();
                var x = selected.Select(n => curve.Id == "sagittal" ? n.px : n.py).ToArray();
                var stable = selected.Select(n => n.planeWaves + sign * n.transverseSquared
                    / ((Math.Sqrt(radius * radius - n.transverseSquared) + radius) * wave)).ToArray();
                return ComparisonMetrics.Calculate(curve.Id, "Wave", ComparisonMetrics.Align(curve, curve with { X = x, Y = stable }, []), gate);
            }).ToArray();
            return new { radius, sign, metrics };
        })).ToArray();
    var maxSagWaves = nodes.Max(n => n.transverseSquared
        / ((Math.Sqrt(minSurrogateRadius * minSurrogateRadius - n.transverseSquared) + minSurrogateRadius) * wave));
    return new { index, request = job.Request.Fingerprint, wave, roundingScale, ulp, minSurrogateRadius, maxSagWaves,
        minimumNonChiefTransverseDistance = Math.Sqrt(nodes.Where(n => n.px != 0 || n.py != 0).Min(n => n.transverseSquared)),
        nonChiefRaysMissingZeroRadiusSphere = nodes.Count(n => n.transverseSquared > 0),
        maxStablePlaneResidual = nodes.Max(n => Math.Abs(n.planeWaves)),
        maxOriginalNativeResidual = nodes.Max(n => Math.Abs(n.nativeReportedWaves)),
        maxQuantizationModelResidual = nodes.Max(n => Math.Abs(n.nativeReportedWaves
            - (n.absoluteWaves + ((roundingScale + n.axial) - roundingScale) / wave))),
        comparisons, passesEveryTestedSurrogate = comparisons.All(c => c.metrics.All(m => m.Conclusion == Conclusion.Pass)),
        nodes, workbenchSha256 = Hash(path) };
}

(WorkbenchJob Job, string Path) Replay(int index)
{
    var job = JsonFiles.Read<WorkbenchJob>(rows[index - 1].GetProperty("job").GetString()!);
    var folder = Path.Combine(work, "formal-core", index.ToString());
    WorkbenchExecutor.Execute(job with { OutputDirectory = folder });
    return (job, Path.Combine(folder, "data.json"));
}
Tolerances Gate(int index, string quantity) => rows[index - 1].GetProperty("settings").GetProperty("quantities")
    .GetProperty(quantity).Deserialize<Tolerances>(JsonFiles.Options)!;
JsonElement ReadZip(string path)
{
    using var stream = archive.GetEntry(path)!.Open();
    using var data = JsonDocument.Parse(stream);
    return data.RootElement.Clone();
}
static JsonElement Read(string path) => JsonFiles.Read<JsonElement>(path);
static JsonElement[] ImageRays(JsonElement data) => data.GetProperty("surfaces").EnumerateArray().Last()
    .GetProperty("rays").EnumerateArray().ToArray();
static double N(JsonElement data, string name) => data.GetProperty(name).GetDouble();
static bool Valid(JsonElement ray) => N(ray, "error") == 0 && N(ray, "vignette") == 0 && N(ray, "intensity") > 0;
static Vector3D Position(JsonElement ray) => new(N(ray, "x"), N(ray, "y"), N(ray, "z"));
static Vector3D Direction(JsonElement ray) => new(N(ray, "l"), N(ray, "m"), N(ray, "n"));
static double Dot(Vector3D a, Vector3D b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
static string Hash(string path) => JsonFiles.Hash(File.ReadAllBytes(path));
