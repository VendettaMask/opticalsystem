using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Services;

public enum ContrastDirection { Sagittal, Tangential, Average }

public readonly record struct ContrastRayPair((double X, double Y) First, (double X, double Y) Second);
public readonly record struct ContrastPairResult(double DifferenceWaves, double FirstWaves, double SecondWaves, double? CenterWaves)
{
    public double Loss => Math.Clamp(.5 * (1 - Math.Cos(2 * Math.PI * DifferenceWaves)), 0, 1);
}

/// <summary>Moore–Elliott ray pairs evaluated with the formal common-reference wavefront engine.</summary>
public static class ContrastMetrics
{
    public static double CutoffFrequency(Optic optic, (double Hx, double Hy) field, Wavelength wavelength)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        var cutoff = optic.ImageSpaceAfocal
            ? ImageSpaceAnalysisSupport.AfocalCutoffFrequencyCyclesPerMilliradian(optic, wavelength)
            : 1 / (wavelength.Micrometers * .001 * DiffractionEngine.WorkingFNumber(optic, field, wavelength, optic.RayAimingEnabled));
        if (!double.IsFinite(cutoff) || cutoff <= 0)
            throw new InvalidOperationException("Moore–Elliott 需要有限正衍射截止频率。");
        return cutoff;
    }

    public static double PupilSeparation(Optic optic, (double Hx, double Hy) field, Wavelength wavelength, double frequency)
    {
        return PupilSeparation(frequency, CutoffFrequency(optic, field, wavelength));
    }

    public static double PupilSeparation(double frequency, double cutoff)
    {
        if (!double.IsFinite(cutoff) || cutoff <= 0)
            throw new ArgumentOutOfRangeException(nameof(cutoff), "截止频率必须为有限正数。");
        if (!double.IsFinite(frequency) || frequency < 0)
            throw new ArgumentOutOfRangeException(nameof(frequency), "空间频率必须为有限非负数。");
        // Coordinates span the full pupil diameter from -1 to +1.
        var separation = 2 * (frequency / cutoff);
        if (!double.IsFinite(separation) || separation > 2)
            throw new ArgumentOutOfRangeException(nameof(frequency), "频率超过衍射截止值，没有重叠瞳孔光线对。");
        return separation;
    }

    public static bool TryPair(double px, double py, double separation, bool sagittal, bool centered, out ContrastRayPair pair)
    {
        var half = centered ? separation / 2 : 0;
        pair = sagittal
            ? new((px - separation + half, py), (px + half, py))
            : new((px, py - separation + half), (px, py + half));
        return double.IsFinite(px) && double.IsFinite(py) && double.IsFinite(separation) && separation >= 0
            && Inside(pair.First) && Inside(pair.Second);
        static bool Inside((double X, double Y) point) => point.X * point.X + point.Y * point.Y <= 1 + 1e-12;
    }

    public static ContrastPairResult EvaluatePair(Optic optic, (double Hx, double Hy) field, Wavelength wavelength,
        ContrastRayPair pair, (double X, double Y)? center = null)
    {
        ComputationCancellation.ThrowIfCancellationRequested();
        (double X, double Y)[] points = center.HasValue ? [pair.First, pair.Second, center.Value] : [pair.First, pair.Second];
        if (points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || p.X * p.X + p.Y * p.Y > 1 + 1e-12))
            throw new ArgumentOutOfRangeException(nameof(pair), "移位光线必须位于归一化单位瞳孔内。");
        var wavefront = WavefrontEngine.GenerateChiefRaySamples(optic, field, wavelength, points, aimAtStop: optic.RayAimingEnabled);
        if (wavefront.Samples.Count != points.Length || wavefront.Samples.Take(2).Any(s => !double.IsFinite(s.Intensity) || s.Intensity <= 0 || !double.IsFinite(s.OpdWaves)))
            throw new InvalidOperationException("Moore–Elliott 光线对未得到有效波前。");
        var first = wavefront.Samples[0].OpdWaves; var second = wavefront.Samples[1].OpdWaves;
        var unshifted = points.Length > 2 && double.IsFinite(wavefront.Samples[2].Intensity)
            && wavefront.Samples[2].Intensity > 0 && double.IsFinite(wavefront.Samples[2].OpdWaves)
            ? wavefront.Samples[2].OpdWaves : (double?)null;
        return new(second - first, first, second, unshifted);
    }

    public static double Evaluate(Optic optic, int wave, int fieldNumber, double frequency, double px, double py, ContrastDirection direction)
    {
        if (!Enum.IsDefined(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (wave < 0 || wave > optic.Wavelengths.Count || optic.Wavelengths.Count == 0) throw new ArgumentOutOfRangeException(nameof(wave));
        if (fieldNumber < 1 || fieldNumber > optic.Fields.Count) throw new ArgumentOutOfRangeException(nameof(fieldNumber));
        var wavelength = wave == 0 ? optic.Wavelengths.FirstOrDefault(w => w.IsPrimary) ?? optic.Wavelengths[0] : optic.Wavelengths[wave - 1];
        var point = optic.Fields[fieldNumber - 1];
        var field = FieldCoordinates.Normalize(optic.Fields, point.X, point.Y);
        var separation = PupilSeparation(optic, field, wavelength, frequency);
        double Direction(bool sagittal)
        {
            if (!TryPair(px, py, separation, sagittal, centered: false, out var pair))
                throw new InvalidOperationException("Moore–Elliott 原始或移位光线超出单位瞳孔。");
            return EvaluatePair(optic, field, wavelength, pair).DifferenceWaves;
        }
        return direction switch
        {
            ContrastDirection.Sagittal => Direction(true),
            ContrastDirection.Tangential => Direction(false),
            _ => .5 * Direction(true) + .5 * Direction(false)
        };
    }
}
