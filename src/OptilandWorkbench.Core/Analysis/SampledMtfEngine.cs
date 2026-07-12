using System.Numerics;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Analysis;

public static class SampledMtfEngine
{
    public static double Calculate(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        double frequencyX,
        double frequencyY,
        int pupilSampling = 128,
        int zernikeTerms = 37)
    {
        var wavefront = WavefrontEngine.GenerateChiefRayUniform(optic, field, wavelength, pupilSampling);
        var samples = wavefront.Samples.ToArray();
        var coefficients = ZernikeFitEngine.FitFringe(samples, zernikeTerms);
        var exitPupilDiameter = optic.Paraxial.EstimateExitPupilDiameter();
        if (Math.Abs(exitPupilDiameter) <= 1e-30)
        {
            return Math.Abs(frequencyX) <= 1e-30 && Math.Abs(frequencyY) <= 1e-30 ? 1 : 0;
        }

        var exitPupilDistance = -optic.Paraxial.EstimateExitPupilLocation();
        var wavelengthMillimeters = wavelength.Micrometers * 1e-3;
        var shiftX = exitPupilDistance * wavelengthMillimeters * frequencyX / (exitPupilDiameter / 2);
        var shiftY = exitPupilDistance * wavelengthMillimeters * frequencyY / (exitPupilDiameter / 2);
        var otfAtZero = samples.Sum(sample => sample.Intensity);
        if (otfAtZero <= 1e-30)
        {
            return 0;
        }

        var otf = Complex.Zero;
        foreach (var sample in samples)
        {
            var shiftedX = sample.NormalizedPupilX - shiftX;
            var shiftedY = sample.NormalizedPupilY - shiftY;
            if ((shiftedX * shiftedX) + (shiftedY * shiftedY) > 1)
            {
                continue;
            }

            var shiftedOpd = ZernikeFitEngine.Evaluate(coefficients, shiftedX, shiftedY);
            var phase = 2 * Math.PI * (sample.OpdWaves - shiftedOpd);
            otf += sample.Intensity * Complex.FromPolarCoordinates(1, phase);
        }

        return Complex.Abs(otf / otfAtZero);
    }
}
