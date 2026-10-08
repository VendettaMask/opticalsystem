using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptilandWorkbench.Core;
using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;
using OptilandWorkbench.Core.Serialization;
using OptilandWorkbench.Core.Services;
using OptilandWorkbench.Application.Runtime;

// Diagnostic only. Optical samples, fits, PSFs and MTFs come exclusively from
// the formal shared Core. No native values are used to fit or adjust a result.
if (args.Length is < 2 or > 3 || (args.Length == 3 && args[2] != "--translation-only"))
    throw new ArgumentException("Usage: repository-root fresh-output-directory [--translation-only]");
var root = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
    throw new IOException("A fresh output directory is required.");
Directory.CreateDirectory(output);
var json = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
};
if (args.Length == 3)
{
    await TranslationDiagnostic.Run(root, output, json);
    return;
}
var inputs = new Dictionary<string, string>();
var fixedProductFiles = new[]
{
    "src/OptilandWorkbench.Core/Analysis/Wavefront/WavefrontAnalyses.cs",
    "src/OptilandWorkbench.Core/Analysis/WavefrontEngine.cs",
    "src/OptilandWorkbench.Core/Analysis/ReferenceSphereWavefrontAnalysis.cs",
    "src/OptilandWorkbench.Core/Analysis/ReferenceSphereWavefrontEngine.cs",
    "src/OptilandWorkbench.Core/Analysis/DiffractionEngine.cs",
    "src/OptilandWorkbench.Core/Analysis/JonesPupilEngine.cs",
    "src/OptilandWorkbench.Core/Analysis/MtfScanAnalysis.cs",
    "src/OptilandWorkbench.Core/Analysis/Fields/FieldSweepAnalyses.cs",
    "src/OptilandWorkbench.Core/Services/ZernikeMetrics.cs",
    "src/OptilandWorkbench.Application/Runtime/WorkbenchRuntime.Analysis.cs"
}.ToDictionary(p => p, p => Hash(Path.Combine(root, p)));
using var manifest = JsonDocument.Parse(Read("validation/zemax/2026-r1/standard-sample-opd-2026-10-06/manifest.json"));
var rows = new List<object>();
foreach (var fixture in manifest.RootElement.GetProperty("fixtures").EnumerateArray())
{
    var lens = fixture.GetProperty("lens").GetString()!;
    var setting = fixture.GetProperty("setting").GetString()!;
    var directory = "validation/zemax/2026-r1/standard-sample-opd-2026-10-06/" + lens + "/";
    var snapshotText = Read(directory + "snapshot.json");
    Require(Hash(Path.Combine(root, directory + "snapshot.json")) == fixture.GetProperty("snapshotSha256").GetString(), "snapshot hash");
    var nativeText = Read(directory + setting + "-native.json");
    Require(Hash(Path.Combine(root, directory + setting + "-native.json")) == fixture.GetProperty("nativeSha256").GetString(), "native hash");
    using var native = JsonDocument.Parse(nativeText);
    Require(native.RootElement.GetProperty("messages").GetArrayLength() == 0, "native diagnostics");
    var snapshot = JsonSerializer.Deserialize<OpticSnapshot>(snapshotText, json)!;
    var original = Optic.FromSnapshot(snapshot);
    var request = fixture.GetProperty("request");
    var fieldNumber = request.GetProperty("field").GetInt32();
    var waveNumber = request.GetProperty("wavelength").GetInt32();
    var fieldPoint = original.Fields[fieldNumber - 1];
    var field = FieldCoordinates.Normalize(original.Fields, fieldPoint.X, fieldPoint.Y);
    var wave = original.Wavelengths[waveNumber - 1];
    var fans = native.RootElement.GetProperty("dataSeries");
    var coordinates = fans.EnumerateArray().SelectMany((fan, axis) => fan.GetProperty("x").EnumerateArray()
        .Select(x => axis == 0 ? (X: 0d, Y: x.GetDouble()) : (X: x.GetDouble(), Y: 0d))).ToArray();
    var nativeValues = fans.EnumerateArray().SelectMany(f => f.GetProperty("y").EnumerateArray()
        .Select(y => y[0].ValueKind == JsonValueKind.Null ? (double?)null : y[0].GetDouble())).ToArray();

    var flagRows = new List<object>();
    foreach (var systemAim in new[] { false, true })
    {
        var optic = Optic.FromSnapshot(snapshot);
        optic.RayAimingEnabled = systemAim;
        var offHex = WavefrontEngine.GenerateChiefRay(optic, field, wave, 8, aimAtStop: false);
        var onHex = WavefrontEngine.GenerateChiefRay(optic, field, wave, 8, aimAtStop: true);
        var hexContract = systemAim ? onHex : offHex;
        var zernikeRows = new List<object>();
        foreach (var kind in new[] { ZernikeAnalysisKind.Fringe, ZernikeAnalysisKind.Standard, ZernikeAnalysisKind.Annular })
        {
            var basis = kind == ZernikeAnalysisKind.Standard ? ZernikeBasisKind.Standard
                : kind == ZernikeAnalysisKind.Annular ? ZernikeBasisKind.Annular : ZernikeBasisKind.Fringe;
            var actual = new ZernikeAnalysis(optic, kind, numRings: 8, numTerms: 37,
                mapSize: 17, wavelengthNumber: waveNumber, fieldNumber: fieldNumber).GenerateData();
            var actualCoefficients = (double[])actual.Values["CoefficientsWaves"];
            var expected = ZernikeMetrics.Fit(hexContract.Samples, basis, 37, .5, requireFullRank: false);
            var unAimed = ZernikeMetrics.Fit(offHex.Samples, basis, 37, .5, requireFullRank: false);
            var uniformOperand = kind != ZernikeAnalysisKind.Standard ? null
                : ZernikeMetrics.Evaluate(optic, waveNumber, fieldNumber, 1, basis, 37);
            zernikeRows.Add(new
            {
                kind = kind.ToString(), sampling = actual.Values["Sampling"],
                actualChiefRmsWaves = actual.Values["RmsChiefWaves"],
                sourceAimedChiefRmsWaves = expected.Chief.Rms,
                coefficientErrorAgainstSameHexSourceAimingWaves = MaxDifference(actualCoefficients, expected.Coefficients.Select(c => c.Value).ToArray()),
                coefficientErrorAgainstUnAimedHexWaves = MaxDifference(actualCoefficients, unAimed.Coefficients.Select(c => c.Value).ToArray()),
                standardUniformOperandCoefficientDifferenceWaves = uniformOperand is null ? (double?)null
                    : MaxDifference(actualCoefficients, uniformOperand.Coefficients.Select(c => c.Value).ToArray())
            });
        }
        // Already-correct uniform Fringe and ZERN are negative controls.
        var uniform = WavefrontEngine.GenerateChiefRayUniform(optic, field, wave, 32,
            aimAtStop: systemAim, zemaxCentered: true);
        var fringe = new ZernikeAnalysis(optic, ZernikeAnalysisKind.ZemaxFringe, numRings: 32,
            numTerms: 37, mapSize: 17, wavelengthNumber: waveNumber, fieldNumber: fieldNumber).GenerateData();
        var fringeFit = ZernikeMetrics.Fit(uniform.Samples, ZernikeBasisKind.Fringe, 37, requireFullRank: false);
        var standardFit = ZernikeMetrics.Fit(uniform.Samples, ZernikeBasisKind.Standard, 37);
        var standardOperand = ZernikeMetrics.Evaluate(optic, waveNumber, fieldNumber, 1, ZernikeBasisKind.Standard, 37);
        var sweep = new ZernikeVsFieldAnalysis(optic, fieldDensity: 2, numRings: 8, numTerms: 37,
            wavelengthNumber: waveNumber).GenerateData();
        var edgePoint = optic.Fields.MaxBy(f => Math.Sqrt(f.X * f.X + f.Y * f.Y))!;
        var edgeField = FieldCoordinates.Normalize(optic.Fields, edgePoint.X, edgePoint.Y);
        var edgeSource = ZernikeFitEngine.FitFringe(WavefrontEngine.GenerateChiefRay(optic, edgeField, wave, 8,
            aimAtStop: systemAim).Samples, 37);
        var sweepValues = sweep.PlotSeries.Select(s => s.Points[^1].Y).ToArray();

        var sourceFan = WavefrontEngine.GenerateChiefRaySamples(optic, field, wave, coordinates, aimAtStop: systemAim);
        var forcedFan = WavefrontEngine.GenerateChiefRaySamples(optic, field, wave, coordinates, aimAtStop: true);
        var zero = DiffractionEngine.GenerateDefocusedWavefront(optic, field, wave, coordinates, 0);

        // Same exact even pupil nodes: zemaxCentered takes precedence over the
        // cellCentered coordinate rule. Only the aiming side effect is isolated.
        var offGrid = WavefrontEngine.GenerateChiefRayUniform(optic, field, wave, 32,
            aimAtStop: false, zemaxCentered: true);
        var onGrid = WavefrontEngine.GenerateChiefRayUniform(optic, field, wave, 32,
            aimAtStop: true, zemaxCentered: true);
        var fft = DiffractionEngine.ComputeFftPsf(optic, field, wave, 32, 64,
            cellCenteredPupil: true, zemaxFftSampling: true, aimAtStop: systemAim);
        var fftPreparedOn = DiffractionEngine.ComputeFftPsf(optic, field, wave, 32, 64,
            cellCenteredPupil: true, zemaxFftSampling: true, aimAtStop: true, preparedWavefront: onGrid);
        var fftPreparedOff = DiffractionEngine.ComputeFftPsf(optic, field, wave, 32, 64,
            cellCenteredPupil: true, zemaxFftSampling: true, aimAtStop: false, preparedWavefront: offGrid);
        var mismatchRejected = Rejects<InvalidOperationException>(() => DiffractionEngine.ComputeFftPsf(
            optic, field, wave, 32, 64, cellCenteredPupil: true, zemaxFftSampling: true,
            aimAtStop: systemAim, preparedWavefront: systemAim ? offGrid : onGrid));
        Require(mismatchRejected, "mismatched prepared aiming rejected");
        var noCell = DiffractionEngine.ComputeFftPsf(optic, field, wave, 32, 64,
            cellCenteredPupil: false, zemaxFftSampling: true, aimAtStop: systemAim);
        var sourceWorking = DiffractionEngine.WorkingFNumber(optic, field, wave, aimAtStop: systemAim);
        var forcedWorking = DiffractionEngine.WorkingFNumber(optic, field, wave, aimAtStop: true);
        var jonesOff = JonesPupilEngine.Generate(optic, field, wave, 32,
            cellCentered: false, aimAtStop: false, zemaxCentered: true);
        var jonesCell = JonesPupilEngine.Generate(optic, field, wave, 32,
            cellCentered: true, aimAtStop: false, zemaxCentered: true);
        var jonesOn = JonesPupilEngine.Generate(optic, field, wave, 32,
            cellCentered: false, aimAtStop: true, zemaxCentered: true);
        var fastMtf = new[] { false, true }.Select(polarization =>
        {
            var data = new MtfThroughFocusAnalysis(optic, MtfComputationMethod.Fourier,
                spatialFrequency: 20, focusPlaneCount: 1,
                settings: new(PupilSampling: 16, ImageSize: 32, UsePolarization: polarization, ZemaxCompatible: true),
                wavelengthNumber: waveNumber, fieldNumber: fieldNumber).GenerateData();
            return new { polarization, tangential = data.PlotSeries[0].Points[0].Y, sagittal = data.PlotSeries[1].Points[0].Y };
        }).ToArray();
        var spheres = Enum.GetValues<ReferenceSphereStrategy>().Select(strategy =>
        {
            var result = new ReferenceSphereWavefrontAnalysis(optic, strategy, numRings: 8, mapSize: 17,
                wavelengthNumber: waveNumber, fieldNumber: fieldNumber).GenerateData();
            return new { strategy = strategy.ToString(), rmsWaves = result.Values["RmsWaves"],
                referenceOpticalPath = result.Values["ReferenceOpticalPathLength"],
                rayCount = result.Values["RayCount"], vignettedRayCount = result.Values["VignettedRayCount"] };
        }).ToArray();
        // Synthetic mode-switch controls, not frozen native afocal references.
        var afocal = Optic.FromSnapshot(snapshot);
        afocal.RayAimingEnabled = systemAim;
        afocal.ImageSpaceAfocal = true;
        var afocalOff = WavefrontEngine.GenerateChiefRay(afocal, field, wave, 8, aimAtStop: false);
        var afocalOn = WavefrontEngine.GenerateChiefRay(afocal, field, wave, 8, aimAtStop: true);
        var afocalRows = Enum.GetValues<ReferenceSphereStrategy>().Select(strategy =>
        {
            var result = new ReferenceSphereWavefrontAnalysis(afocal, strategy, numRings: 8, mapSize: 17,
                wavelengthNumber: waveNumber, fieldNumber: fieldNumber).GenerateData();
            var actualRms = Convert.ToDouble(result.Values["RmsWaves"]);
            return new { strategy = strategy.ToString(), actualRmsWaves = actualRms,
                unAimedRmsWaves = afocalOff.Rms, aimedRmsWaves = afocalOn.Rms,
                rmsErrorAgainstSourceAimingWaves = Math.Abs(actualRms - (systemAim ? afocalOn.Rms : afocalOff.Rms)),
                referenceGeometry = result.Values["ReferenceGeometry"] };
        }).ToArray();
        flagRows.Add(new
        {
            systemAim, hexWavefrontAimingDifference = WaveDifference(offHex, onHex), zernike = zernikeRows,
            controls = new
            {
                uniformFringeCoefficientErrorWaves = MaxDifference((double[])fringe.Values["CoefficientsWaves"], fringeFit.Coefficients.Select(c => c.Value).ToArray()),
                uniformStandardOperandCoefficientErrorWaves = MaxDifference(standardFit.Coefficients.Select(c => c.Value).ToArray(), standardOperand.Coefficients.Select(c => c.Value).ToArray())
            },
            zernikeSweepEdgeCoefficientErrorAgainstSourceAimingWaves = MaxDifference(sweepValues, edgeSource.Select(c => c.Value).ToArray()),
            zeroDefocus = new { againstSystemAiming = WaveDifference(zero, sourceFan), againstForcedAiming = WaveDifference(zero, forcedFan),
                againstFrozenNative = FanDifference(zero, nativeValues), sourceAgainstFrozenNative = FanDifference(sourceFan, nativeValues),
                frozenNativeAiming = original.RayAimingEnabled },
            fft = new
            {
                actualUseRayAiming = fft.UseRayAiming, fallbackUsed = fft.StopAimingFallbackUsed,
                mismatchRejected,
                actualVsPreparedSystemAiming = PsfDifference(fft, systemAim ? fftPreparedOn : fftPreparedOff),
                actualVsPreparedAimed = PsfDifference(fft, fftPreparedOn), actualVsPreparedUnAimed = PsfDifference(fft, fftPreparedOff),
                actualVsSameNodesWithoutCellFlag = PsfDifference(fft, noCell), actualWorkingFNumber = fft.WorkingFNumber,
                sourceWorkingFNumber = sourceWorking, forcedWorkingFNumber = forcedWorking,
                tangentialWorkingFNumber = fft.TangentialWorkingFNumber, sagittalWorkingFNumber = fft.SagittalWorkingFNumber,
                sampleSpacingMicrometers = fft.SampleSpacingMicrometers,
                sampleCounts = new { unAimed = offGrid.Samples.Count(s => s.Intensity > 0), aimed = onGrid.Samples.Count(s => s.Intensity > 0) }
            },
            jones = new { cellFlagVsAimedSameNodes = JonesDifference(jonesCell, jonesOn), cellFlagVsUnAimedSameNodes = JonesDifference(jonesCell, jonesOff) },
            fastMtf, focalReferenceSpheres = spheres, syntheticAfocal = afocalRows,
            systemFlagUnchanged = optic.RayAimingEnabled == systemAim
        });
        Require(optic.RayAimingEnabled == systemAim, "system flag preserved");
        Console.WriteLine($"{lens}/{setting} systemAim={systemAim}: Zernike/spheres/FFT/Jones/zero-defocus/fast-MTF inspected.");
    }
    rows.Add(new { lens, setting, fieldNumber, waveNumber, wavelengthMicrometers = wave.Micrometers,
        originalSystemAiming = original.RayAimingEnabled, checks = flagRows });
}

var cooke = Optic.FromSnapshot(JsonSerializer.Deserialize<OpticSnapshot>(Read(
    "validation/zemax/2026-r1/standard-sample-opd-2026-10-06/cooke-40-degree-field/snapshot.json"), json)!);
var samplingRows = new[] { 32, 64, 128 }.Select(n =>
{
    var noExplicitGrid = new PsfAnalysis(cooke, numRays: n, wavelengthNumber: 2, fieldNumber: 1,
        zemaxCompatible: true).GenerateData();
    var explicitGrid = new PsfAnalysis(cooke, numRays: n, gridSize: 2 * n, wavelengthNumber: 2, fieldNumber: 1,
        zemaxCompatible: true).GenerateData();
    return new { requestedSampling = n, implicitGridPupilSampling = noExplicitGrid.Values["PupilSampling"],
        explicitGridPupilSampling = explicitGrid.Values["PupilSampling"],
        implicitGridSize = noExplicitGrid.Values["GridSize"], explicitGridSize = explicitGrid.Values["GridSize"],
        implicitSpacingMicrometers = noExplicitGrid.Values["ImageDeltaMicrometers"],
        explicitSpacingMicrometers = explicitGrid.Values["ImageDeltaMicrometers"] };
}).ToArray();
var foucaultOff = new FoucaultAnalysis(cooke, sampling: 16, fieldNumber: 3, wavelengthNumber: 1, usePolarization: false).GenerateData();
var foucaultPolarizationRejected = Rejects<NotSupportedException>(() => new FoucaultAnalysis(cooke,
    sampling: 16, fieldNumber: 3, wavelengthNumber: 1, usePolarization: true).GenerateData());
Require(foucaultPolarizationRejected, "unsupported Foucault polarization rejected");
var runtime = new WorkbenchRuntime(Optic.FromSnapshot(cooke.ToSnapshot()));
var displayRows = new[] { 32, 64, 128 }.Select(display =>
{
    var result = runtime.BuildAnalysisData("PSF", new Dictionary<string, string>
    {
        ["Sampling"] = "64", ["Display"] = display.ToString(), ["WavelengthNumber"] = "2",
        ["FieldNumber"] = "1", ["UsePolarization"] = "false", ["Normalized"] = "false"
    });
    return new { requestedDisplay = display, actualPupilSampling = result.Values["PupilSampling"],
        actualGridSize = result.Values["GridSize"], sampleSpacingMicrometers = result.Values["ImageDeltaMicrometers"],
        heatmapPointCount = result.PlotSeries.Single().Points.Count };
}).ToArray();
var negativeDelta = new PsfAnalysis(cooke, numRays: 64, gridSize: 128, wavelengthNumber: 2, fieldNumber: 1,
    imageDeltaMicrometers: -1, zemaxCompatible: true).GenerateData();
var defaultDelta = new PsfAnalysis(cooke, numRays: 64, gridSize: 128, wavelengthNumber: 2, fieldNumber: 1,
    imageDeltaMicrometers: 0, zemaxCompatible: true).GenerateData();
foreach (var (path, hash) in inputs) Require(Hash(Path.Combine(root, path)) == hash, "input unchanged: " + path);
foreach (var (path, hash) in fixedProductFiles) Require(Hash(Path.Combine(root, path)) == hash, "product source unchanged: " + path);
var summary = new
{
    createdUtc = DateTimeOffset.UtcNow,
    scope = "Formal Core path consistency controls on three unchanged frozen snapshots, two captured field/wavelength settings, both system-aiming states. Synthetic afocal switches are internal controls only. Native OPD comparison is authoritative only at its captured aiming setting. No new native capture or full release acceptance.",
    phase = "post-repair verification", productCodeChangedDuringRun = false, toleranceChanged = false, nativeRecaptured = false,
    coreAssemblySha256 = Hash(typeof(Optic).Assembly.Location), probeSourceSha256 = Hash(Path.Combine(root,
        "tools/diagnostics/CalculationPaths20261008/Program.cs")), productSourcesUnchanged = true, inputsUnchanged = true,
    cases = rows, fftSampling = samplingRows, applicationFftDisplay = displayRows,
    negativeFftDelta = new { samePointsAsDefault = negativeDelta.PlotSeries.Single().Points.SequenceEqual(defaultDelta.PlotSeries.Single().Points),
        negativeRequestActualSpacing = negativeDelta.Values["ImageDeltaMicrometers"], defaultSpacing = defaultDelta.Values["ImageDeltaMicrometers"] },
    foucault = new { unsupportedPolarizationRejected = foucaultPolarizationRejected,
        offMetadata = foucaultOff.Values["UsePolarization"], model = foucaultOff.Values["ComputationModel"] },
    inputHashes = inputs, fixedProductSourceHashes = fixedProductFiles
};
using (var stream = File.Create(Path.Combine(output, "summary.json"))) JsonSerializer.Serialize(stream, summary, json);
Console.WriteLine("Summary: " + Path.Combine(output, "summary.json"));

string Read(string relative)
{
    inputs.TryAdd(relative, Hash(Path.Combine(root, relative)));
    return File.ReadAllText(Path.Combine(root, relative));
}
static string Hash(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
}
static void Require(bool condition, string context) { if (!condition) throw new InvalidDataException(context); }
static bool Rejects<T>(Action action) where T : Exception
{
    try { action(); return false; } catch (T) { return true; }
}
static double MaxDifference(double[] a, double[] b)
{
    Require(a.Length == b.Length && a.All(double.IsFinite) && b.All(double.IsFinite), "finite equal-size vectors");
    return a.Zip(b, (x, y) => Math.Abs(x - y)).DefaultIfEmpty(0).Max();
}
static object WaveDifference(WavefrontResult a, WavefrontResult b)
{
    Require(a.Samples.Count == b.Samples.Count, "wavefront count");
    var differences = new List<double>();
    var masks = 0;
    for (var i = 0; i < a.Samples.Count; i++)
    {
        var x = a.Samples[i]; var y = b.Samples[i];
        Require(x.NormalizedPupilX == y.NormalizedPupilX && x.NormalizedPupilY == y.NormalizedPupilY, "exact pupil nodes");
        if ((x.Intensity > 0) != (y.Intensity > 0)) masks++;
        if (x.Intensity > 0 && y.Intensity > 0) differences.Add(Math.Abs(x.OpdWaves - y.OpdWaves));
    }
    return new { candidateCount = a.Samples.Count, commonValidCount = differences.Count, maskMismatchCount = masks,
        maximumCommonValidOpdDifferenceWaves = differences.DefaultIfEmpty(0).Max(),
        referencePathDifferenceMillimeters = Math.Abs(a.ReferenceOpticalPath - b.ReferenceOpticalPath) };
}
static object FanDifference(WavefrontResult a, double?[] b)
{
    Require(a.Samples.Count == b.Length, "native knot count");
    var compared = new List<double>();
    var nativeValid = b.Count(v => v.HasValue);
    for (var i = 0; i < b.Length; i++)
        if (b[i].HasValue && a.Samples[i].Intensity > 0) compared.Add(Math.Abs(a.Samples[i].OpdWaves - b[i]!.Value));
    return new { nativeValidCount = nativeValid, comparedCount = compared.Count, missingNativeValidCount = nativeValid - compared.Count,
        maximumOpdErrorWaves = compared.DefaultIfEmpty(0).Max() };
}
static object PsfDifference(PsfResult a, PsfResult b) => new
{
    maximumRelativeIntensityDifference = MaxDifference(a.Values.Cast<double>().Select(v => v / 100).ToArray(), b.Values.Cast<double>().Select(v => v / 100).ToArray()),
    aPeak = a.PeakStrehlRatio, bPeak = b.PeakStrehlRatio,
    spacingDifferenceMicrometers = Math.Abs(a.SampleSpacingMicrometers - b.SampleSpacingMicrometers)
};
static object JonesDifference(JonesPupilResult a, JonesPupilResult b)
{
    Require(a.Samples.Count == b.Samples.Count, "Jones count");
    var differences = new List<double>();
    var masks = 0;
    for (var i = 0; i < a.Samples.Count; i++)
    {
        var x = a.Samples[i]; var y = b.Samples[i];
        Require(x.Px == y.Px && x.Py == y.Py, "exact Jones pupil nodes");
        if (x.IsValid != y.IsValid) masks++;
        if (x.IsValid && y.IsValid)
            differences.Add(new[] { (x.Jxx - y.Jxx).Magnitude, (x.Jxy - y.Jxy).Magnitude,
                (x.Jyx - y.Jyx).Magnitude, (x.Jyy - y.Jyy).Magnitude }.Max());
    }
    return new { commonValidCount = differences.Count, maskMismatchCount = masks, maximumJonesComponentDifference = differences.DefaultIfEmpty(0).Max() };
}
