using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using OpticalSystemDesign.Core.Scattering;

if (args.Length > 0 && args[0] == "--runtime-review") { RuntimeReview.Run(Path.GetFullPath(args[1])); return; }
if (args.Length > 0 && args[0] == "--calibration-explore") { CalibrationExploration.Run(Path.GetFullPath(args[1]),Path.GetFullPath(args[2]),args.Length>3?args[3]:"mystique"); return; }
if (args.Length > 0 && args[0] == "--probe") { RuntimeReview.Probe(Path.GetFullPath(args[1]),Path.GetFullPath(args[2]),double.Parse(args[3],CultureInfo.InvariantCulture),double.Parse(args[4],CultureInfo.InvariantCulture)); return; }
if (args.Length > 0 && args[0] == "--paired550") { MeasuredPairCalibration.Run(Path.GetFullPath(args[1])); return; }
if (args.Length > 0 && args[0] == "--runtime-power-audit") { RuntimePowerAudit.Run(Path.GetFullPath(args[1]), args.Length > 2 ? Path.GetFullPath(args[2]) : null); return; }
if (args.Length > 0 && args[0] == "--native") { NativeComparison.Run(Path.GetFullPath(args[1])); return; }
if (args.Length > 0 && args[0] == "--direct") { DirectReview.Run(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]), args.Length > 3 && args[3] == "white"); return; }
if (args.Length > 0 && args[0] == "--derived-green") { DerivedGreenCalibration.Run(Path.GetFullPath(args[1])); return; }
var root = Path.GetFullPath(args.Length > 0 ? args[0] : "validation/rechecks/2026-10-31/commercial-bsdf-calibration");
var spectralPath = Path.Combine(root, "mystique.astm"); var xyzPath = Path.Combine(root, "mystique_XYZ.astm");
static (string[] Columns, double[][] Rows) Read(string path)
{
    var lines = File.ReadAllLines(path); var header = lines.Single(l => l.StartsWith("VARS ", StringComparison.Ordinal))[5..].Split(',');
    var count = int.Parse(lines.Single(l => l.StartsWith("NUM_POINTS ", StringComparison.Ordinal))[11..], CultureInfo.InvariantCulture);
    var rows = lines.Where(l => l.Length > 0 && (char.IsDigit(l[0]) || l[0] == '-')).Select(l => l.Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray()).ToArray();
    if (count != rows.Length || rows.Any(r => r.Length != header.Length)) throw new InvalidDataException("ASTM source count mismatch"); return (header, rows);
}
var spectral = Read(spectralPath); var xyz = Read(xyzPath);
string Key(double[] row) => string.Join("/", row.Take(4).Select(v => v.ToString("G17", CultureInfo.InvariantCulture)));
var xy = xyz.Rows.ToDictionary(Key); var keys = spectral.Rows.Select(Key).Order(StringComparer.Ordinal).ToArray();
if (!keys.ToHashSet().SetEquals(xy.Keys)) throw new InvalidDataException("Measured spectral and XYZ geometries differ");
var heldout = keys.Where((_, i) => i % 3 == 0).ToHashSet(); var observations = new List<XyzRadiometricObservation>();
var execution = new { CoreAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(XyzRadiometricCalibrator).Assembly.Location))).ToLowerInvariant(),
    DiagnosticAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(System.Reflection.Assembly.GetExecutingAssembly().Location))).ToLowerInvariant() };
foreach (var row in spectral.Rows)
{
    var key = Key(row); var values = xy[key];
    for (var i = 4; i < spectral.Columns.Length; i++) observations.Add(new(key, double.Parse(spectral.Columns[i][..^2], CultureInfo.InvariantCulture), values[4], values[5], values[6], row[i], heldout.Contains(key)));
}
var provenance = new XyzCalibrationProvenance(XyzCalibrationDataKind.PublishedPairedMeasurements,
    "Cornell PCG / Mystic lacquer / published gonioreflectometer spectral BRDF and CIE XYZ; measurement date UNKNOWN; observer/illuminant not specified in downloaded pair",
    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(xyzPath))).ToLowerInvariant(), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(spectralPath))).ToLowerInvariant(), false);
// Explicit internal exploratory policy; not a measured uncertainty or commercial acceptance gate.
var policy = new XyzCalibrationFitPolicy(.01, .01, 1e-6);
try
{
    var result = XyzRadiometricCalibrator.Fit(observations, provenance, policy);
    bool exportRejected = false; try { _ = result.CreateCalibration("Cornell calibration candidate", "Unspecified", "Unspecified"); } catch (InvalidDataException) { exportRejected = true; }
    var output = new { Source = provenance, Execution = execution, Units = "1/sr; source ASTM angles radians; sample identity uses all four original angles", Samples = spectral.Rows.Length, TrainingDirections = keys.Length - heldout.Count, ValidationDirections = heldout.Count,
        WavelengthCount = spectral.Columns.Length - 4, PolicyIsInternalExplorationOnly = true, ConditionsKnown = false, result.ModelValidationPassed, ApprovedCalibrationExportRejected = exportRejected, Result = result,
        CommercialPointwiseComparisonComplete = false, RealZemaxXyzCalibrationComplete = false };
    File.WriteAllText(Path.Combine(root, "cornell-paired-fit.json"), JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine(JsonSerializer.Serialize(new { Samples = spectral.Rows.Length, Bands = result.Bands.Count, PassedBands = result.Evidence.Bands.Count(b => b.Passed), WorstValidationNrmse = result.Evidence.Bands.Max(b => b.ValidationNormalizedRms), WorstAbsolute = result.Evidence.Bands.Max(b => b.ValidationMaximumAbsoluteError), ApprovedExportRejected = exportRejected }));
}
catch (InvalidDataException ex)
{
    File.WriteAllText(Path.Combine(root, "cornell-paired-fit.json"), JsonSerializer.Serialize(new { Source = provenance, Execution = execution, Samples = spectral.Rows.Length, Rejected = true, Reason = ex.Message, CommercialPointwiseComparisonComplete = false, RealZemaxXyzCalibrationComplete = false }, new JsonSerializerOptions { WriteIndented = true })); throw;
}
