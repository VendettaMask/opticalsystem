using System.IO.Compression;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.ZemaxComparison.Normalization;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

internal static class FftProfileDiagnostics
{
    internal static void WriteIfRequested(string id, Optic optic, CanonicalAnalysisRequest request,
        AnalysisData data, NumericResult actual, NumericResult expected, ZipArchive zip)
    {
        var directory = Environment.GetEnvironmentVariable("OPTICAL_FFT_PROFILE_DIAGNOSTICS");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        object? diagnosis = null;
        if (id.Contains("binocular"))
        {
            var wave = optic.Wavelengths[request.Wavelength - 1];
            var support = typeof(Optic).Assembly.GetType("OpticalSystemDesign.Core.Analysis.ImageSpaceAnalysisSupport")!;
            var aspect = ((double X, double Y))support.GetMethod("AfocalFftPupilAspectStretch", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [optic, (0d, 0d), wave, optic.RayAimingEnabled])!;
            var grid = new PupilGridSpecification(32, true, true, aspect.X);
            typeof(PupilGridSpecification).GetProperty("StretchY", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(grid, aspect.Y);
            var nodes = Enumerable.Range(0, 32).SelectMany(y => Enumerable.Range(0, 32).Select(x =>
                (X: grid.Coordinate(x), Y: aspect.Y * ((y-16)/15d))))
                .Where(p => p.X*p.X+p.Y*p.Y <= 1).ToArray();
            var raw = WavefrontEngine.GenerateChiefRaySamples(optic, (0, 0), wave, nodes,
                aimAtStop: optic.RayAimingEnabled, referenceWavelength: wave);
            var smooth = (WavefrontResult)typeof(DiffractionEngine).GetMethod("InterpolateFftPupilPhase", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [raw, grid])!;
            var pencil = WavefrontEngine.GenerateChiefRaySamples(optic, (0, 0), wave,
                [(-.001, 0d), (.001, 0d), (0d, -.001), (0d, .001)], aimAtStop: optic.RayAimingEnabled, referenceWavelength: wave);
            JsonElement Native(string kind)
            {
                using var input = zip.GetEntry($"artifacts/validation/afocal-fft-energy-20261027/native-binocular/binocular-c{request.Configuration}-{kind}/data.json")!.Open();
                using var document = JsonDocument.Parse(input);
                return document.RootElement.GetProperty("dataGrids")[0].Clone();
            }
            var real = Native("Real"); var imaginary = Native("Imaginary");
            var nativePupil = new Complex[64,64];
            for (var y=0; y<64; y++) for (var x=0; x<64; x++)
                nativePupil[(y+33)%64,(x+33)%64] = Complex.Conjugate(new Complex(
                    real.GetProperty("values")[y][x].GetDouble(), imaginary.GetProperty("values")[y][x].GetDouble()));
            typeof(DiffractionEngine).GetMethod("FourierTransform2D", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [nativePupil, true]);
            var nativeCenter = nativePupil[0,0];
            var source = new PsfAnalysis(optic, 32, 64, request.Wavelength, 1, zemaxCompatible: true).GenerateData();
            var circular = new PupilGridSpecification(32, true, true, 1);
            var circularNodes = Enumerable.Range(0,32).SelectMany(y => Enumerable.Range(0,32).Select(x =>
                (X: circular.Coordinate(x), Y: circular.Coordinate(y))))
                .Where(p => p.X*p.X+p.Y*p.Y <= 1).ToArray();
            var circularRaw = WavefrontEngine.GenerateChiefRaySamples(optic, (0,0), wave, circularNodes,
                aimAtStop: optic.RayAimingEnabled, referenceWavelength: wave);
            var circularSmooth = (WavefrontResult)typeof(DiffractionEngine).GetMethod("InterpolateFftPupilPhase", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [circularRaw, circular])!;
            diagnosis = new { aspect.X, aspect.Y, pencil.Samples, raw = raw.Samples, smooth = smooth.Samples,
                circularRaw = circularRaw.Samples, circularSmooth = circularSmooth.Samples,
                facets = FftProfileFacetDiagnostics.Inspect(optic, request.Configuration),
                paths = FftProfileFacetDiagnostics.CapturePaths(optic),
                source.Values, source = source.PlotSeries,
                pupil = Enumerable.Range(0,64).SelectMany(y => Enumerable.Range(0,64).Select(x =>
                {
                    var value = nativePupil[(y+32)%64,(x+32)%64] / nativeCenter;
                    return new { x, y, amplitude = value.Magnitude, phaseWaves = value.Phase / (2*Math.PI) };
                })).ToArray() };
        }
        JsonFiles.Write(Path.Combine(directory, id+".json"), new { id, request, data.Values, actual, expected, diagnosis });
    }
}
