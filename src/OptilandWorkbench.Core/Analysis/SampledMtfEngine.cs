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
        int zernikeTerms = 37,
        double defocus = 0)
    {
        var wavefront = WavefrontEngine.GenerateChiefRayUniform(optic, field, wavelength, pupilSampling);
        var pupilDiameter = optic.ImageSpaceAfocal
            ? ImageSpaceAnalysisSupport.AfocalDiffractionPupilDiameterMillimeters(optic)
            : optic.Paraxial.EstimateExitPupilDiameter();
        return new SampledMtfEvaluator(
            wavefront.Samples.ToArray(),
            ZernikeFitEngine.FitFringe(wavefront.Samples, zernikeTerms),
            pupilDiameter,
            -optic.Paraxial.EstimateExitPupilLocation(),
            wavelength.Micrometers * 1e-3,
            optic.ImageSpaceAfocal,
            defocus);
    }

    public static double Calculate(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        double frequencyX,
        double frequencyY,
        int pupilSampling = 128,
        int zernikeTerms = 37,
        double defocus = 0)
    {
        return Create(optic, field, wavelength, pupilSampling, zernikeTerms, defocus)
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
    private readonly bool _afocalImageSpace;
    private readonly double _defocus;
    private readonly Complex _otfAtZero;

    internal SampledMtfEvaluator(
        IReadOnlyList<WavefrontSample> samples,
        IReadOnlyList<ZernikeCoefficient> coefficients,
        double exitPupilDiameter,
        double exitPupilDistance,
        double wavelengthMillimeters,
        bool afocalImageSpace = false,
        double defocus = 0)
    {
        _samples = samples;
        _coefficients = coefficients;
        _exitPupilDiameter = exitPupilDiameter;
        _exitPupilDistance = exitPupilDistance;
        _wavelengthMillimeters = wavelengthMillimeters;
        _afocalImageSpace = afocalImageSpace;
        _defocus = defocus;
        _otfAtZero = ComputeOtf(0, 0);
    }

    public double Calculate(double frequencyX, double frequencyY)
        => Math.Clamp(CalculateOtf(frequencyX, frequencyY).Magnitude, 0, 1);

    public Complex CalculateOtf(double frequencyX, double frequencyY)
    {
        if (Math.Abs(_exitPupilDiameter) <= 1e-30)
        {
            return Math.Abs(frequencyX) <= 1e-30 && Math.Abs(frequencyY) <= 1e-30 ? 1 : 0;
        }

        var shiftX = FrequencyToNormalizedPupilShift(frequencyX);
        var shiftY = FrequencyToNormalizedPupilShift(frequencyY);
        if (_otfAtZero.Magnitude <= 1e-30)
        {
            return 0;
        }

        var otf = ComputeOtf(shiftX, shiftY);
        return otf / _otfAtZero;
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

            var sourceOpd = OpdAt(sample.NormalizedPupilX, sample.NormalizedPupilY);
            var shiftedOpd = OpdAt(shiftedX, shiftedY);
            var phase = 2 * Math.PI * (sourceOpd - shiftedOpd);
            otf += sample.Intensity * Complex.FromPolarCoordinates(1, phase);
        }

        return otf;
    }

    private double FrequencyToNormalizedPupilShift(double frequency)
    {
        if (_afocalImageSpace)
        {
            return 2 * _wavelengthMillimeters * frequency * 1_000.0 / _exitPupilDiameter;
        }

        return _exitPupilDistance * _wavelengthMillimeters * frequency / (_exitPupilDiameter / 2);
    }

    private double OpdAt(double normalizedX, double normalizedY)
    {
        var opd = ZernikeFitEngine.Evaluate(_coefficients, normalizedX, normalizedY);
        if (!_afocalImageSpace || Math.Abs(_defocus) <= 1e-30)
        {
            return opd;
        }

        var pupilRadius = _exitPupilDiameter / 2;
        var radiusSquared = pupilRadius * pupilRadius
            * ((normalizedX * normalizedX) + (normalizedY * normalizedY));
        var defocusOpdMillimeters = _defocus * radiusSquared / 2_000.0;
        return opd + (defocusOpdMillimeters / _wavelengthMillimeters);
    }

}
