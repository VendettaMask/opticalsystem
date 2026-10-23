using System.Text.Json;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Serialization;
using OpticalSystemDesign.Core.Services;

if (args.Length == 3 && args[0] == "objectives-completion") { ObjectivesCompletion.Compare(args[1],args[2]); return; }

if (args.Length == 2 && args[0] == "dense-energy-prepare") { DenseEnergyAudit.Prepare(args[1]); return; }
if (args.Length == 2 && args[0] == "dense-energy-inspect") { DenseEnergyAudit.Inspect(args[1]); return; }
if (args.Length == 2 && args[0] == "dense-energy-circular-controls") { DenseEnergyAudit.CircularIntervalControls(args[1]); return; }

if (args.Length == 3 && args[0] == "objectives-sampling-controls")
{
    ObjectivesRepair.SamplingControls(args[1], args[2]);
    return;
}

if (args.Length >= 2 && args[0] == "objectives-repair")
{
    ObjectivesRepair.Run(args[1], args.Length > 2 && args[2] != "fresh" ? args[2] : null, args.Length > 3 ? args[3] : null);
    return;
}

if (args.Length == 2 && args[0] == "spot-apodization-stage")
{
    RemainingLensControls.SpotApodization(args[1]);
    return;
}

if (args.Length >= 2 && args[0] == "wide-schmidt-recheck")
{
    WideSchmidtRepair.Recheck(args[1], args.Length > 2 ? args[2] : "targets");
    return;
}

if (args.Length == 2 && args[0] == "wide-schmidt-stage")
{
    WideSchmidtRepair.Stage(args[1]);
    return;
}

if (args.Length == 2 && args[0] == "schmidt-grid-stage")
{
    WideSchmidtRepair.Schmidt(args[1]);
    return;
}

if (args.Length == 2 && args[0] == "wide-schmidt-rays")
{
    WideSchmidtRays.Run(args[1]);
    return;
}

if (args.Length == 2 && args[0] == "gregorian-stage")
{
    WideSchmidtRays.Gregorian(args[1]);
    return;
}

if (args.Length == 2 && args[0] == "energy32-perturbed-recheck")
{
    Energy32Recheck.RunPerturbed(args[1]);
    return;
}

if (args.Length == 2 && args[0] == "energy32-conic-audit")
{
    Energy32ConicAudit.Run(args[1]);
    return;
}

if (args.Length == 2 && args[0] == "energy32-dispersion-audit")
{
    Energy32DispersionAudit.Run(args[1]);
    return;
}

if (args.Length == 2 && args[0] == "energy32-current-marginals")
{
    Energy32MarginalAudit.Prepare(args[1], importSource: true);
    return;
}

if (args.Length == 2 && args[0] == "energy32-expression-audit")
{
    Energy32ExpressionAudit.Run(args[1]);
    return;
}

if (args.Length == 2 && args[0] == "energy32-frequency-boundary-audit")
{
    Energy32FrequencyBoundaryAudit.Run(args[1]);
    return;
}

if (args.Length == 2 && args[0] == "energy32-grid-stage-audit")
{
    Energy32GridStageAudit.Run(args[1]);
    return;
}

if (args.Length == 2 && args[0] == "energy32-delta-sweep-audit")
{
    Energy32DeltaSweepAudit.Run(args[1]);
    return;
}

if (args.Length == 2 && args[0] == "energy32-fourier-audit")
{
    Energy32FourierAudit.Run(args[1]);
    return;
}

if (args.Length == 2 && args[0] == "energy32-discriminant-control")
{
    Energy32GeometryAudit.Run(args[1], vertexPlane: true, activeParameters: true, nativeIndices: true, discriminant: true);
    return;
}

if (args.Length == 2 && args[0] == "energy32-discriminant-audit")
{
    Energy32GeometryAudit.Run(args[1], vertexPlane: true, discriminant: true);
    return;
}

if (args.Length == 2 && args[0] == "energy32-surface-stage-audit")
{
    Energy32SurfaceStageAudit.Run(args[1]);
    return;
}

if (args.Length == 2 && args[0] == "energy32-active-vertex-audit")
{
    Energy32GeometryAudit.Run(args[1], vertexPlane: true, activeParameters: true, nativeIndices: true);
    return;
}

if (args.Length == 2 && args[0] == "energy32-active-geometry-audit")
{
    Energy32GeometryAudit.Run(args[1], activeParameters: true, nativeIndices: true);
    return;
}

if (args.Length == 2 && args[0] == "energy32-vertex-plane-audit")
{
    Energy32GeometryAudit.Run(args[1], vertexPlane: true);
    return;
}

if (args.Length == 2 && args[0] == "energy32-width-holdout")
{
    Energy32WidthHoldout.Run(args[1]);
    return;
}

if (args.Length == 2 && args[0] == "energy32-momentum-audit")
{
    Energy32MomentumAudit.Run(args[1]);
    return;
}

if (args.Length == 2 && args[0] == "energy32-incidence-audit")
{
    Energy32IncidentCosineAudit.Run(args[1]);
    return;
}

if (args.Length == 2 && args[0] == "energy32-recheck")
{
    Energy32Recheck.Run(args[1]);
    return;
}

if (args.Length == 2 && args[0] == "energy32-frequency-control")
{
    Energy32FrequencyControl.Run(args[1]);
    return;
}

if (args.Length == 2 && args[0] == "energy32-extended-snell-audit")
{
    Energy32ExtendedSnellAudit.Run(args[1]);
    return;
}

if (args.Length == 2 && args[0] == "energy32-geometry-audit")
{
    Energy32GeometryAudit.Run(args[1]);
    return;
}

if (args.Length == 2 && args[0] == "energy32-native-step-audit")
{
    Energy32NativeStepAudit.Run(args[1]);
    return;
}

if (args.Length == 2 && args[0] == "energy32-snell-audit")
{
    Energy32SnellAudit.Run(args[1]);
    return;
}

if (args.Length == 2 && args[0] == "energy32-marginal-prepare")
{
    Energy32MarginalAudit.Prepare(args[1]);
    return;
}
if (args.Length == 2 && args[0] == "energy32-marginal-compare")
{
    Energy32MarginalAudit.Compare(args[1]);
    return;
}

if (args.Length == 2 && args[0] == "energy32-plane-audit")
{
    Energy32PlaneAudit.Run(args[1]);
    return;
}

if (args.Length == 2 && args[0] == "energy32-coordinate-audit")
{
    Energy32CoordinateAudit.Run(args[1]);
    return;
}

if (args.Length == 2 && args[0] == "energy128-rays-prepare")
{
    Energy128RayControls.Prepare(args[1]);
    return;
}
if (args.Length == 2 && args[0] == "energy128-rays-compare")
{
    Energy128RayControls.Compare(args[1]);
    return;
}

if (args.Length == 2 && args[0] == "energy-grid-controls")
{
    EnergyGridControls.Run(args[1]);
    return;
}
if (args.Length == 2 && args[0] == "precision-controls")
{
    PrecisionControls.Run(args[1]);
    return;
}
if (args.Length == 2 && args[0] == "psf-plane-controls")
{
    PrecisionControls.PsfPlane(args[1]);
    return;
}
if (args.Length == 2 && args[0] == "energy-convergence-controls")
{
    PrecisionControls.EnergyConvergence(args[1]);
    return;
}
if (args.Length == 2 && args[0] == "native-complex-controls")
{
    PrecisionControls.ComplexPupil(args[1]);
    return;
}
if (args.Length == 2 && args[0] == "raw-snell-controls")
{
    RawSnellControls.Run(args[1]);
    return;
}

if (args.Length != 3) throw new ArgumentException("<matrix> <selected-workbench-root> <fresh-output>");
var matrix = Path.GetFullPath(args[0]);
var workers = Path.GetFullPath(args[1]);
var output = Path.GetFullPath(args[2]);
if (Directory.Exists(output)) throw new IOException("Use a fresh control output directory.");
Directory.CreateDirectory(output);
using var items = JsonDocument.Parse(File.ReadAllText(Path.Combine(matrix, "items.json")));
var results = new List<object>();
foreach (var item in items.RootElement.EnumerateArray())
{
    var run = item.GetProperty("run").Deserialize<AnalysisRun>(JsonFiles.Options)!;
    if (args[1] == "pupil-controls" && run.Key == "First Order")
    {
        var pupilOptic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(item.GetProperty("snapshot").GetString()!));
        results.Add(new
        {
            lens = item.GetProperty("lens").GetString(),
            entrancePupil = pupilOptic.Paraxial.EstimateEntrancePupilDiameter(),
            entrancePosition = pupilOptic.Paraxial.EstimateEntrancePupilLocation(),
            exitPosition = pupilOptic.Paraxial.EstimateExitPupilLocation(),
            primaryWave = pupilOptic.Wavelengths.First(w => w.IsPrimary).Micrometers
        });
        continue;
    }
    var workbench = Path.Combine(workers, run.Directory, "data.json");
    if (args[1] == "mtf-grid-controls" && run.Key == "MTF"
        && item.GetProperty("lens").GetString() is "Objectives/Even Asphere.zmx" or "Objectives/Petzval.zmx" or "Objectives/Wide angle lens 200 degree field.zmx")
    {
        var optic = Optic.FromSnapshot(JsonFiles.Read<OpticSnapshot>(item.GetProperty("snapshot").GetString()!));
        var mtfRequest = run.Request!;
        var mtfEntry = AnalysisComparisonRegistry.Get(run.Key);
        var nativeMtf = ResultNormalizer.Zemax(Path.Combine(item.GetProperty("path").GetString()!, "native/data.json"), mtfEntry, mtfRequest);
        foreach (var (centered, zemax) in new[] { (true, false), (false, false), (true, true) })
        {
            var wavelength = optic.Wavelengths[mtfRequest.Wavelength - 1];
            var psf = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wavelength, mtfRequest.PupilSampling,
                2 * mtfRequest.PupilSampling, cellCenteredPupil: centered, zemaxFftSampling: zemax,
                imageDelta: zemax ? -1 : 0, aimAtStop: optic.RayAimingEnabled, referenceWavelength: wavelength);
            foreach (var frequencyCount in new[] { mtfRequest.PupilSampling, mtfRequest.PupilSampling - 1, mtfRequest.PupilSampling - 2 })
            {
                var mtf = DiffractionEngine.ComputeFftMtf(psf with { FrequencySampleCount = frequencyCount }, optic, wavelength);
                mtf = (MtfResult)typeof(MtfAnalysis).GetMethod("Resample", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                    .Invoke(null, [mtf, mtfRequest.MaximumFrequency, FftMtfDataType.Modulation, 300])!;
                var result = new NumericResult();
                foreach (var tangential in new[] { true, false })
                {
                    result.Series.Add(new(tangential ? "tangential" : "sagittal", "Control", mtf.Frequency.ToArray(),
                        (tangential ? mtf.Tangential : mtf.Sagittal).ToArray(), mtfEntry.XAxis!, mtfEntry.YAxis!));
                }
                var control = new AnalysisRun { Key = run.Key, Directory = run.Directory };
                var controlDirectory = Path.Combine(output, run.Directory + $"-{centered}-{zemax}-{frequencyCount}");
                Directory.CreateDirectory(controlDirectory);
                ComparisonRunner.Compare(control, mtfEntry, item.GetProperty("settings").Deserialize<AnalysisConfiguration>(JsonFiles.Options)!, result, nativeMtf, controlDirectory);
                results.Add(new
                {
                    lens = item.GetProperty("lens").GetString(),
                    centered,
                    zemax,
                    frequencyCount,
                    control.Conclusion,
                    metrics = control.Metrics.Select(metric => new { metric.Id, metric.MaxAbsolute, metric.Nrmse })
                });
            }
        }
        continue;
    }
    if (!File.Exists(workbench)) continue;
    var entry = AnalysisComparisonRegistry.Get(run.Key);
    var request = run.Request!;
    var native = Path.Combine(item.GetProperty("path").GetString()!, "native/data.json");
    var settings = item.GetProperty("settings").Deserialize<AnalysisConfiguration>(JsonFiles.Options)!;
    var directory = Path.Combine(output, run.Directory);
    Directory.CreateDirectory(directory);
    var n = ResultNormalizer.Zemax(native, entry, request);
    var w = ResultNormalizer.Workbench(workbench, entry, request);
    try { ComparisonRunner.Compare(run, entry, settings, w, n, directory); }
    catch (InvalidDataException exception) { run.Conclusion = Conclusion.Incomparable; run.Reason = exception.Message; }
    JsonFiles.Write(Path.Combine(directory, "comparison.json"), run);
    results.Add(new
    {
        lens = item.GetProperty("lens").GetString(),
        run.Key,
        request.Wavelength,
        run.Conclusion,
        metrics = run.Metrics.Select(metric => new { metric.Id, metric.MaxAbsolute, metric.Nrmse, metric.Conclusion })
    });
}
JsonFiles.Write(Path.Combine(output, "summary.json"), results);
Console.WriteLine(JsonSerializer.Serialize(results, JsonFiles.Options));
