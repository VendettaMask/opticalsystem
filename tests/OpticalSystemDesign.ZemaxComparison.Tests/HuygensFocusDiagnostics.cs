using System.Reflection;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

internal static class HuygensFocusDiagnostics
{
    internal static void WriteIfRequested(string id, Optic optic, CanonicalAnalysisRequest request,
        AnalysisData data, NumericResult actual, NumericResult expected)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_HUYGENS_FOCUS_DIAGNOSTICS");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var rows = new List<object>();
        WavefrontResult? pupil = null;
        if (id.Contains("beam-expander"))
        {
            var settings = new MtfComputationSettings(request.PupilSampling, request.ImageSampling,
                request.ImageDeltaMicrometers, UsePolarization: request.Polarization,
                ZemaxCompatible: true, UseZemaxHuygensSemantics: true);
            var evaluator = typeof(MtfThroughFocusAnalysis).Assembly.GetType("OpticalSystemDesign.Core.Analysis.MtfMethodEvaluator")!;
            var nodes = (IReadOnlyList<OpticalSystemDesign.Core.Raytrace.PupilSample>)typeof(DiffractionEngine)
                .GetMethod("HuygensPupilSamples", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [request.PupilSampling])!;
            pupil = WavefrontEngine.GenerateChiefRaySamples(optic, (0, 0), optic.Wavelengths[0],
                nodes.Select(p => (p.X, p.Y)).ToArray(), aimAtStop: optic.RayAimingEnabled);
            var method = evaluator.GetMethod("ComputeHuygensPolychromaticPsf", BindingFlags.Static | BindingFlags.NonPublic)!;
            foreach (var focus in new[] { -.01, -.005, 0, .005, .01 })
            {
                var psf = (PsfResult)method.Invoke(null, [optic, (0d, 0d), optic.Wavelengths.ToArray(), settings, focus])!;
                var n = psf.GridSize;
                rows.Add(new { focus, psf.SampleSpacingMicrometers, n,
                    values = Enumerable.Range(0, n).Select(row => Enumerable.Range(0, n).Select(col => psf.Values[row, col]).ToArray()).ToArray(),
                    mtf = DiffractionEngine.ComputePsfMtf(psf),
                    doubleMtf = DiffractionEngine.ComputePsfMtf(psf, doubleTransformSize: true) });
            }
        }
        JsonFiles.Write(Path.Combine(directory, id + ".json"), new { id, request, data.Values, actual, expected, rows, pupil });
    }
}
