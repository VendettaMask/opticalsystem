using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.ZemaxComparison.Normalization;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

internal static class DiffractionEnergyDiagnostics
{
    internal static void WriteIfRequested(string id, Optic optic, CanonicalAnalysisRequest request,
        AnalysisData data, NumericResult actual, NumericResult expected)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_DIFFRACTION_ENERGY_DIAGNOSTICS");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var controls = new List<object>();
        if (id.Contains("beam-expander"))
        {
            var wave = optic.Wavelengths[request.Wavelength - 1];
            var reference = optic.Wavelengths.First(w => w.IsPrimary);
            var n = request.PupilSampling;
            var size = 2 * n;
            var step = 1 / (2 * Math.Sqrt(n));
            var grid = new PupilGridSpecification(n, true, true, step * (n / 2d - 1));
            foreach (var name in new[] { "ExplicitCoordinateStep", "ExplicitCoordinateStepY" })
                typeof(PupilGridSpecification).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(grid, step);
            foreach (var ignoreOpd in new[] { true, false })
            {
                var spectrum = (Array)typeof(DiffractionEngine).GetMethod("ComputeFftCumulativeEnergySpectrum",
                    BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [optic, (0d, 0d), new[] { wave }, n, ignoreOpd])!;
                var current = (PsfResult)((ITuple)spectrum.GetValue(0)!)[1]!;
                foreach (var interpolate in new[] { false, true })
                {
                    object?[] arguments = [optic, (0d, 0d), wave, n, size, false, true, 0d,
                        null, null, true, ignoreOpd, optic.RayAimingEnabled, reference,
                        current.SampleSpacingMicrometers, true, null, null, grid, interpolate];
                    var psf = (PsfResult)typeof(DiffractionEngine).GetMethod("ComputeFftPsfCore",
                        BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, arguments)!;
                    var pupil = (Complex[,])arguments[16]!;
                    controls.Add(new { ignoreOpd, interpolate, psf.SampleSpacingMicrometers,
                        maximumDifferenceFromProduction = Enumerable.Range(0, size).SelectMany(y => Enumerable.Range(0, size)
                            .Select(x => Math.Abs(current.Values[y, x] - psf.Values[y, x]))).Max(),
                        psf = Enumerable.Range(0, size).Select(y => Enumerable.Range(0, size).Select(x => psf.Values[y, x]).ToArray()).ToArray(),
                        pupil = Enumerable.Range(0, pupil.GetLength(0)).SelectMany(y => Enumerable.Range(0, pupil.GetLength(1)).Select(x =>
                            new { x, y, real = pupil[y, x].Real, imaginary = pupil[y, x].Imaginary })).ToArray() });
                }
            }
        }
        JsonFiles.Write(Path.Combine(directory, id + ".json"), new { id, request, data.Values, actual, expected, controls });
    }
}
