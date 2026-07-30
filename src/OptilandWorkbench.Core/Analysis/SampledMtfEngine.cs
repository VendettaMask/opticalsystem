using System.Numerics;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Analysis;

public static class SampledMtfEngine
{
    public static SampledMtfEvaluator Create(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        int pupilSampling = 128,
        int zernikeTerms = 37)
    {
        var wavefront = WavefrontEngine.GenerateChiefRayUniform(optic, field, wavelength, pupilSampling);
        return new SampledMtfEvaluator(
            wavefront.Samples.ToArray(),
            ZernikeFitEngine.FitFringe(wavefront.Samples, zernikeTerms),
            optic.Paraxial.EstimateExitPupilDiameter(),
            -optic.Paraxial.EstimateExitPupilLocation(),
            wavelength.Micrometers * 1e-3);
    }

    public static double Calculate(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        double frequencyX,
        double frequencyY,
        int pupilSampling = 128,
        int zernikeTerms = 37)
    {
        return Create(optic, field, wavelength, pupilSampling, zernikeTerms)
            .Calculate(frequencyX, frequencyY);
    }

}

public sealed class SampledMtfEvaluator
{
    private readonly IReadOnlyList<WavefrontSample> _samples;
    private readonly IReadOnlyList<ZernikeCoefficient> _coefficients;
    private readonly double _exitPupilDiameter;
    private readonly double _exitPupilDistance;
    private readonly double _wavelengthMillimeters;
    private readonly Complex _otfAtZero;

    internal SampledMtfEvaluator(
        IReadOnlyList<WavefrontSample> samples,
        IReadOnlyList<ZernikeCoefficient> coefficients,
        double exitPupilDiameter,
        double exitPupilDistance,
        double wavelengthMillimeters)
    {
        _samples = samples;
        _coefficients = coefficients;
        _exitPupilDiameter = exitPupilDiameter;
        _exitPupilDistance = exitPupilDistance;
        _wavelengthMillimeters = wavelengthMillimeters;
        _otfAtZero = ComputeOtf(0, 0);
    }

    public double Calculate(double frequencyX, double frequencyY)
    {
        if (Math.Abs(_exitPupilDiameter) <= 1e-30)
        {
            return Math.Abs(frequencyX) <= 1e-30 && Math.Abs(frequencyY) <= 1e-30 ? 1 : 0;
        }

        var shiftX = _exitPupilDistance * _wavelengthMillimeters * frequencyX / (_exitPupilDiameter / 2);
        var shiftY = _exitPupilDistance * _wavelengthMillimeters * frequencyY / (_exitPupilDiameter / 2);
        if (_otfAtZero.Magnitude <= 1e-30)
        {
            return 0;
        }

        var otf = ComputeOtf(shiftX, shiftY);
        return Math.Clamp(otf.Magnitude / _otfAtZero.Magnitude, 0, 1);
    }

    private Complex ComputeOtf(double shiftX, double shiftY)
    {
        var otf = Complex.Zero;
        foreach (var sample in _samples)
        {
            if (sample.Intensity <= 0)
            {
                continue;
            }

            var shiftedX = sample.NormalizedPupilX - shiftX;
            var shiftedY = sample.NormalizedPupilY - shiftY;
            if ((shiftedX * shiftedX) + (shiftedY * shiftedY) > 1)
            {
                continue;
            }

            var sourceOpd = ZernikeFitEngine.Evaluate(
                _coefficients,
                sample.NormalizedPupilX,
                sample.NormalizedPupilY);
            var shiftedOpd = ZernikeFitEngine.Evaluate(_coefficients, shiftedX, shiftedY);
            var phase = 2 * Math.PI * (sourceOpd - shiftedOpd);
            otf += sample.Intensity * Complex.FromPolarCoordinates(1, phase);
        }

        return otf;
    }

}
