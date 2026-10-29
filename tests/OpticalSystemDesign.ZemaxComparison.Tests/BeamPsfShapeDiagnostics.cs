using System.Reflection;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.ZemaxComparison;
using OpticalSystemDesign.ZemaxComparison.Configuration;
using OpticalSystemDesign.ZemaxComparison.Normalization;

namespace OpticalSystemDesign.ZemaxComparison.Tests;

// Opt-in, offline diagnosis; does not alter the source prescription or expected arrays.
internal static class BeamPsfShapeDiagnostics
{
    internal static void WriteIfRequested(string id, Optic optic, CanonicalAnalysisRequest request,
        Grid2DResult expected, Grid2DResult actual)
    {
        var output = Environment.GetEnvironmentVariable("OPTICAL_FFT_PSF_DIAGNOSTICS");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        var wave = optic.Wavelengths[request.Wavelength - 1];
        var psf = DiffractionEngine.ComputeFftPsf(optic, (0, 0), wave, request.PupilSampling,
            request.ImageSampling, cellCenteredPupil: true, zemaxFftSampling: true,
            aimAtStop: optic.RayAimingEnabled, referenceWavelength: wave,
            imageDelta: request.ImageDeltaMicrometers, ignoreScalarTransmission: true);
        var grid = new PupilGridSpecification(request.PupilSampling, true, true, psf.PupilGridStretch);
        var raw = WavefrontEngine.GenerateChiefRayUniform(optic, (0, 0), wave, request.PupilSampling,
            cellCentered: true, aimAtStop: optic.RayAimingEnabled,
            pupilGridStretch: psf.PupilGridStretch, zemaxCentered: true, referenceWavelength: wave);
        raw = raw with { Samples = raw.Samples.Select(s => s.Intensity > 0 && s.LaunchIntensity is { } launch
            ? s with { Intensity = launch } : s).ToArray() };
        var smoothed = (WavefrontResult)typeof(DiffractionEngine).GetMethod("InterpolateFftPupilPhase",
            BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [raw, grid])!;
        JsonFiles.Write(Path.Combine(output, id + ".json"), new
        {
            id, request, n = request.PupilSampling, g = request.ImageSampling,
            psf.SampleSpacingMicrometers, psf.PupilGridStretch,
            points = raw.Samples.Zip(smoothed.Samples, (r, s) => new
            {
                column = (int)Math.Round(r.NormalizedPupilX / grid.Stretch * (request.PupilSampling / 2d - 1)
                    + request.PupilSampling / 2d),
                row = (int)Math.Round(r.NormalizedPupilY / grid.Stretch * (request.PupilSampling / 2d - 1)
                    + request.PupilSampling / 2d),
                r.NormalizedPupilX, r.NormalizedPupilY, r.Intensity,
                rawPhase = r.OpdWaves, phase = s.OpdWaves
            }).ToArray(),
            native = new { expected.X, expected.Y, expected.Z },
            actual = new { actual.X, actual.Y, actual.Z }
        });
    }
}
