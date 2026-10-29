using System.Reflection;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Metrics;
using OpticalSystemDesign.ZemaxComparison.Normalization;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

internal static class FftMtfDiagnostics
{
    internal static void WriteIfRequested(string id, Optic optic, CanonicalAnalysisRequest request,
        AnalysisData data, NumericResult actual, NumericResult expected)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_FFT_MTF_DIAGNOSTICS");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var n = Convert.ToInt32(data.Values["PupilSampling"]);
        var g = Convert.ToInt32(data.Values["GridSize"]);
        var waves = optic.Wavelengths.ToArray();
        var reference = waves.FirstOrDefault(w => w.IsPrimary) ?? waves[0];
        var fields = (IReadOnlyList<(double Hx, double Hy)>)typeof(MtfAnalysis).Assembly
            .GetType("OpticalSystemDesign.Core.Analysis.SpotAnalysisEngine")!
            .GetMethod("DefinedFields", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
            .Invoke(null, [optic])!;
        var variants = new List<object>();
        foreach (var mode in new[] { "baseline", "omit-transmission", "amplitude", "dual-grid" })
        {
            var rows = new List<object>();
            for (var fi = 0; fi < fields.Count; fi++)
            {
                var results = waves.Select(w =>
                {
                    var psf = DiffractionEngine.ComputeFftPsf(optic, fields[fi], w, n, g,
                        cellCenteredPupil: true, zemaxFftSampling: true, imageDelta: -1,
                        referenceWavelength: reference, ignoreScalarTransmission: mode is "omit-transmission" or "amplitude",
                        interpolateScalarPupilAmplitude: mode == "amplitude");
                    psf = psf with { FrequencySampleCount = mode == "dual-grid" ? n - 2 : n - 1 };
                    return (w, mtf: DiffractionEngine.ComputeFftMtf(psf, optic, w));
                }).ToArray();
                var evaluator = typeof(MtfAnalysis).Assembly.GetType("OpticalSystemDesign.Core.Analysis.MtfMethodEvaluator")!;
                var combined = (MtfResult)evaluator.GetMethod("CombinePolychromatic", BindingFlags.Static | BindingFlags.Public)!
                    .Invoke(null, [results])!;
                var resampled = (MtfResult)typeof(MtfAnalysis).GetMethod("Resample", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, [combined, request.MaximumFrequency, FftMtfDataType.Modulation, 300])!;
                var comparisons = new List<object>();
                for (var axis = 0; axis < 2; axis++)
                {
                    var template = actual.Series[2 * fi + axis];
                    var curve = template with { X = resampled.Frequency.ToArray(),
                        Y = (axis == 0 ? resampled.Tangential : resampled.Sagittal).ToArray() };
                    var matched = ComparisonMetrics.Align(curve, expected.Series[2 * fi + axis], []);
                    comparisons.Add(new { template.Id, maximum = matched.Workbench.Zip(matched.Zemax, (a, b) => Math.Abs(a - b)).Max() });
                }
                rows.Add(new { fieldIndex = fi, comparisons, monochromatic = results.Select(r => new
                    { r.w.Micrometers, r.w.Weight, r.mtf.Frequency,
                        tangential = r.mtf.TangentialOtf!.Select(z => new { z.Real, z.Imaginary }).ToArray(),
                        sagittal = r.mtf.SagittalOtf!.Select(z => new { z.Real, z.Imaginary }).ToArray() }).ToArray(),
                    combined = new { combined.Frequency,
                        tangential = combined.TangentialOtf!.Select(z => new { z.Real, z.Imaginary }).ToArray(),
                        sagittal = combined.SagittalOtf!.Select(z => new { z.Real, z.Imaginary }).ToArray() } });
            }
            variants.Add(new { mode, rows });
        }
        JsonFiles.Write(Path.Combine(directory, id + ".json"), new { id, request, n, g, actual, expected, variants });
    }
}
