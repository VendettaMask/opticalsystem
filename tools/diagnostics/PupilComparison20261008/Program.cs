using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.FileIO;
using OptilandWorkbench.Core.Raytrace;
using OptilandWorkbench.Core.Serialization;

// Diagnostic only: all optical calculations come from the formal product Core.
// Native fan knots are compared exactly, never interpolated onto a wavefront map.
if (args.Length is < 2 or > 3 || (args.Length == 3 && args[2] != "--expect-source-aiming"))
    throw new ArgumentException("Usage: repository-root fresh-output-directory [--expect-source-aiming]");
var expectSourceAiming = args.Length == 3;
var root = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
    throw new IOException("A fresh diagnostic output directory is required.");
Directory.CreateDirectory(output);
var options = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
};
var evidence = new Dictionary<string, string>();
var opdRoot = Path.Combine(root, "validation", "zemax", "2026-r1", "standard-sample-opd-2026-10-06");
using var manifest = JsonDocument.Parse(Read(Path.Combine(opdRoot, "manifest.json")));
var aimingRows = new List<object>();
foreach (var fixture in manifest.RootElement.GetProperty("fixtures").EnumerateArray())
{
    var lens = fixture.GetProperty("lens").GetString()!;
    var setting = fixture.GetProperty("setting").GetString()!;
    var snapshot = Path.Combine(opdRoot, lens, "snapshot.json");
    var nativePath = Path.Combine(opdRoot, lens, setting + "-native.json");
    var optic = Load(snapshot);
    using var native = JsonDocument.Parse(Read(nativePath));
    Require(Hash(snapshot) == fixture.GetProperty("snapshotSha256").GetString(), "Snapshot integrity");
    Require(Hash(nativePath) == fixture.GetProperty("nativeSha256").GetString(), "Native fan integrity");
    Require(native.RootElement.GetProperty("messages").GetArrayLength() == 0, "Native fan diagnostics");
    var request = fixture.GetProperty("request");
    var fieldNumber = request.GetProperty("field").GetInt32();
    var waveNumber = request.GetProperty("wavelength").GetInt32();
    var definedField = optic.Fields[fieldNumber - 1];
    var field = FieldCoordinates.Normalize(optic.Fields, definedField.X, definedField.Y);
    var wave = optic.Wavelengths[waveNumber - 1];
    var fans = native.RootElement.GetProperty("dataSeries");
    var coordinates = fans.EnumerateArray().SelectMany((fan, axis) =>
        fan.GetProperty("x").EnumerateArray().Select(x => axis == 0
            ? (X: 0d, Y: x.GetDouble()) : (X: x.GetDouble(), Y: 0d))).ToArray();
    var reference = fans.EnumerateArray().SelectMany(fan => fan.GetProperty("y").EnumerateArray()
        .Select(y => y[0].ValueKind == JsonValueKind.Null ? (double?)null : y[0].GetDouble())).ToArray();
    var forced = WavefrontEngine.GenerateChiefRaySamples(optic, field, wave, coordinates, aimAtStop: true);
    var source = WavefrontEngine.GenerateChiefRaySamples(optic, field, wave, coordinates, aimAtStop: optic.RayAimingEnabled);
    var zeroDefocus = DiffractionEngine.GenerateDefocusedWavefront(optic, field, wave, coordinates, 0);
    var map = new WavefrontAnalysis(optic, pupilSampling: 64, wavelengthNumber: waveNumber,
        fieldNumber: fieldNumber, useExitPupilShape: true).GenerateData();
    var displayPoints = map.PlotSeries.Single().Points;
    var signedOffset = Convert.ToDouble(map.Values["MeanOpticalPathDifference"]) / (wave.Micrometers * 1e-3)
        - displayPoints.Average(p => p.Value!.Value);
    var forcedGrid = WavefrontEngine.GenerateChiefRayUniform(optic, field, wave, 64,
        aimAtStop: true, zemaxCentered: true);
    var gridValues = forcedGrid.Samples.Where(s => s.Intensity > 0)
        .ToDictionary(s => (s.NormalizedPupilX, s.NormalizedPupilY), s => s.OpdWaves);
    var mapPathError = displayPoints.All(p => gridValues.ContainsKey((p.X, p.Y)))
        ? displayPoints.Max(p => Math.Abs(p.Value!.Value + signedOffset - gridValues[(p.X, p.Y)]))
        : double.PositiveInfinity;
    var sourceGrid = WavefrontEngine.GenerateChiefRayUniform(optic, field, wave, 64,
        aimAtStop: optic.RayAimingEnabled, zemaxCentered: true);
    var sourceGridValues = sourceGrid.Samples.Where(s => s.Intensity > 0)
        .ToDictionary(s => (s.NormalizedPupilX, s.NormalizedPupilY), s => s.OpdWaves);
    Require(displayPoints.Count == (expectSourceAiming ? sourceGridValues.Count : gridValues.Count),
        "Actual wavefront map must retain the expected valid pupil mask");
    var sourcePathError = displayPoints.All(p => sourceGridValues.ContainsKey((p.X, p.Y)))
        ? displayPoints.Max(p => Math.Abs(p.Value!.Value + signedOffset - sourceGridValues[(p.X, p.Y)]))
        : double.PositiveInfinity;
    Require((expectSourceAiming ? sourcePathError : mapPathError) < 1e-10,
        expectSourceAiming ? "Actual wavefront map must follow source aiming" : "Historical map must use forced aiming");
    if (expectSourceAiming)
    {
        var noShapeMap = new WavefrontAnalysis(optic, pupilSampling: 64, wavelengthNumber: waveNumber,
            fieldNumber: fieldNumber, useExitPupilShape: false).GenerateData();
        Require(map.PlotSeries.Single().Points.SequenceEqual(noShapeMap.PlotSeries.Single().Points),
            "Exit pupil display option must not change physical wavefront samples");
        Require((bool)map.Values["UseRayAiming"] == optic.RayAimingEnabled, "Reported actual aiming");
    }
    var shared = coordinates.Select((p, i) => new { p, i })
        .Where(row => displayPoints.Any(p => Math.Abs(p.X - row.p.X) < 1e-13 && Math.Abs(p.Y - row.p.Y) < 1e-13)
            && reference[row.i].HasValue).Select(row => new
        {
            pupilX = row.p.X, pupilY = row.p.Y, nativeOpdWaves = reference[row.i],
            actualMapOpdWaves = displayPoints.Single(p => Math.Abs(p.X - row.p.X) < 1e-13
                && Math.Abs(p.Y - row.p.Y) < 1e-13).Value!.Value + signedOffset,
            sourceAimingOpdWaves = source.Samples[row.i].Intensity > 0 ? (double?)source.Samples[row.i].OpdWaves : null
        }).ToArray();
    var row = new
    {
        lens, setting, sourceRayAiming = optic.RayAimingEnabled, fieldNumber, wavelengthNumber = waveNumber,
        wavelengthMicrometers = wave.Micrometers, snapshotSha256 = Hash(snapshot), nativeSha256 = Hash(nativePath),
        nativeFanKnotCount = reference.Length,
        forcedAiming = Metric(forced.Samples, reference), sourceAiming = Metric(source.Samples, reference),
        zeroDefocusDiffractionHelper = Metric(zeroDefocus.Samples, reference),
        actualMapMatchesForcedAimingMaximumWaves = mapPathError,
        actualMapMatchesSourceAimingMaximumWaves = sourcePathError,
        mapValidCount = displayPoints.Count, sharedExactMapFanKnots = shared,
        sharedExactMapFanMaximumErrorWaves = shared.Max(p => Math.Abs(p.actualMapOpdWaves - p.nativeOpdWaves!.Value)),
        sharedSourceAimingMaximumErrorWaves = shared.Where(p => p.sourceAimingOpdWaves.HasValue)
            .Max(p => Math.Abs(p.sourceAimingOpdWaves!.Value - p.nativeOpdWaves!.Value))
    };
    aimingRows.Add(row);
    Console.WriteLine($"Aiming: {lens}/{setting}, source={optic.RayAimingEnabled}, map/fan exact knots={shared.Length}, forced max={Metric(forced.Samples, reference).MaximumErrorWaves:G9}, source max={Metric(source.Samples, reference).MaximumErrorWaves:G9}");
}

// Mirror only the signed Y field table and its Y decenter in an independent snapshot.
// The compression comparison is an internal symmetry/continuity check, not new native evidence.
var tessarSource = Path.Combine(root, "validation", "zemax", "2026-r1", "ra256-field-sampling-2026-10-07",
    "tessar-lens-using-vignetting-factors-ra-256-retain", "source.zmx");
evidence.TryAdd(tessarSource, Hash(tessarSource));
Require(Hash(tessarSource) == "c7ed96a7688abdf2fae197cd67be96bc69f397826a036a7dab424be6394f2644", "Tessar original source integrity");
var tessar = (await new ZemaxZmxImporter().ImportConfigurationSetFileAsync(tessarSource)).Configurations[0];
// The captured snapshot predates the automatic-STOP import repair; do not reuse its hard aperture.
Require(tessar.SurfaceGroup.Items.Single(s => s.IsStop).PhysicalAperture is null, "Fresh automatic-STOP import");
var negative = Optic.FromSnapshot(tessar.ToSnapshot());
foreach (var f in negative.Fields) { f.Y = -f.Y; f.VignetteDecenterY = -f.VignetteDecenterY; }
var vignettingRows = new[] { .1999999999, .2000000001, 4d / 15, .4, .55, .7, .85, 1d }
    .Select(hy => new
    {
        absoluteNormalizedY = hy,
        positiveTable = tessar.SequentialRayTracer.RayGenerator.GetPupilVignetting(0, hy),
        mirroredNegativeTable = negative.SequentialRayTracer.RayGenerator.GetPupilVignetting(0, -hy)
    }).ToArray();
Console.WriteLine("Signed-Y: mirrored compression factors and nearest-row jump retained in summary.");

// Enumerate both existing formal samplers, then evaluate them with the same wavefront engine.
// No assertion about the unknown native RMS-RA nodes is made.
var densityRows = new List<object>();
foreach (var density in new[] { 32, 64, 128, 256 })
{
    var endpoints = ApertureSampler.Generate(density * density, PupilSampling.UniformGrid);
    var centers = ApertureSampler.GenerateRectangularArray(density);
    var optic = Optic.FromSnapshot(tessar.ToSnapshot());
    foreach (var f in optic.Fields) PupilVignetting.Clear(f);
    var wave = optic.Wavelengths.First(w => w.IsPrimary);
    var endpointWave = WavefrontEngine.GenerateChiefRaySamples(optic, (0, 1), wave,
        endpoints.Select(s => (s.X, s.Y)).ToArray(), aimAtStop: optic.RayAimingEnabled);
    var centerWave = WavefrontEngine.GenerateChiefRaySamples(optic, (0, 1), wave,
        centers.Select(s => (s.X, s.Y)).ToArray(), aimAtStop: optic.RayAimingEnabled);
    var endpointStats = WavefrontStatistics.Measure(endpointWave.Samples, endpoints, WavefrontReferenceKind.ChiefRay);
    var centerStats = WavefrontStatistics.Measure(centerWave.Samples, centers, WavefrontReferenceKind.ChiefRay);
    var product = new RmsWavefrontVsFieldAnalysis(optic, numRings: density, fieldDensity: 1,
        method: "RA", reference: "chief", wavelengthNumber: Array.IndexOf(optic.Wavelengths.ToArray(), wave) + 1,
        removeVignettingFactors: true, zemaxCompatibleOutput: true).GenerateData();
    var productEdgeRms = product.PlotSeries.Single().Points.Last().Y;
    Require(Math.Abs(productEdgeRms - endpointStats.Rms) < 1e-10, "Actual RMS wavefront endpoint sampling");
    densityRows.Add(new
    {
        density, endpointCandidates = endpoints.Count, cellCenterCandidates = centers.Count,
        endpointValid = endpointStats.SampleCount, cellCenterValid = centerStats.SampleCount,
        endpointRmsWaves = endpointStats.Rms, cellCenterRmsWaves = centerStats.Rms,
        actualProductRmsWaves = productEdgeRms,
        relativeRmsDifference = (endpointStats.Rms - centerStats.Rms) / centerStats.Rms,
        nativeSamplingCertified = false
    });
    Console.WriteLine($"RA{density}: endpoints {endpointStats.Rms:G9} ({endpointStats.SampleCount}/{endpoints.Count}), centers {centerStats.Rms:G9} ({centerStats.SampleCount}/{centers.Count}) waves");
}
foreach (var (path, expected) in evidence) Require(Hash(path) == expected, "Evidence unchanged: " + path);
var result = new
{
    createdUtc = DateTimeOffset.UtcNow,
    scope = "Frozen native OPD fans versus exact formal Core samples and actual wavefront-map shared knots; internal signed-Y and RA sensitivity controls. No full native Wavefront Map recapture or original 132-case reclassification.",
    nativeRecaptured = false, productCodeChanged = expectSourceAiming, nativeEvidenceUnchanged = true,
    expectedMapAiming = expectSourceAiming ? "source system setting" : "historical forced aiming",
    coreAssemblySha256 = Hash(typeof(Optic).Assembly.Location),
    tessarControlModelOrigin = "Fresh formal import of unchanged ZMX, not the pre-repair captured snapshot",
    probeSourceSha256 = Hash(Path.Combine(root, "tools", "diagnostics", "PupilComparison20261008", "Program.cs")),
    aiming = aimingRows, signedY = vignettingRows, rectangularSampling = densityRows,
    evidence = evidence.ToDictionary(p => Path.GetRelativePath(root, p.Key).Replace('\\', '/'), p => p.Value)
};
using (var stream = File.Create(Path.Combine(output, "summary.json"))) JsonSerializer.Serialize(stream, result, options);
Console.WriteLine("Diagnostic summary: " + Path.Combine(output, "summary.json"));

Optic Load(string path) => Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(Read(path), options)
    ?? throw new InvalidDataException("Missing snapshot"));
string Read(string path) { evidence.TryAdd(path, Hash(path)); return File.ReadAllText(path); }
string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
void Require(bool condition, string context) { if (!condition) throw new InvalidDataException(context); }
FanMetric Metric(IReadOnlyList<WavefrontSample> samples, double?[] reference)
{
    var errors = samples.Select((s, i) => s.Intensity > 0 && reference[i].HasValue ? (double?)(s.OpdWaves - reference[i]!.Value) : null)
        .Where(v => v.HasValue).Select(v => v!.Value).ToArray();
    return new(errors.Length, reference.Length - errors.Length, errors.Max(Math.Abs), Math.Sqrt(errors.Select(v => v * v).Average()));
}
sealed record FanMetric(int ComparedKnots, int MissingKnots, double MaximumErrorWaves, double RmseWaves);
