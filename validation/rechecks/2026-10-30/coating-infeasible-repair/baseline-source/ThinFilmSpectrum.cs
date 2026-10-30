using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Coatings;

public sealed record ThinFilmSample(double WavelengthNanometers, ThinFilmPower Power);
public sealed record ThinFilmBandMetrics(double PeakWavelengthNanometers, double PeakTransmittance,
    double? LeftHalfMaximumNanometers, double? RightHalfMaximumNanometers,
    double? FwhmNanometers, double MinimumReflectance, double MaximumReflectance);

public static class ThinFilmSpectrum
{
    public const string MetricsVersion = "log-scaled-half-maximum/2";
    public const int MaximumSamples = 200001;

    public static double[] Grid(double minimum, double maximum, int intervals)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum <= 0 || maximum <= minimum
            || intervals < 2 || intervals >= MaximumSamples)
            throw new ArgumentException("光谱范围或采样数无效；每次最多 200001 个点。");
        return Enumerable.Range(0, intervals + 1).Select(i => minimum + (maximum - minimum) * i / intervals).ToArray();
    }

    public static ThinFilmSample[] Sample(CoherentThinFilmSolver solver, IEnumerable<double> wavelengths,
        double angleDegrees, ThinFilmPolarization polarization)
    {
        var grid = wavelengths.Distinct().Order().Take(MaximumSamples + 1).ToArray();
        if (grid.Length > MaximumSamples) throw new ArgumentException("光谱采样超过资源上限，请缩小范围或放宽带宽。");
        return grid.Select(w =>
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            return new ThinFilmSample(w, solver.Evaluate(w, angleDegrees, polarization));
        }).ToArray();
    }

    public static ThinFilmBandMetrics Measure(IReadOnlyList<ThinFilmSample> samples)
    {
        if (samples.Count < 3) throw new ArgumentException("光谱至少需要三个采样点。");
        var peak = 0;
        for (var i = 1; i < samples.Count; i++)
            if (samples[i].Power.LogTransmittance > samples[peak].Power.LogTransmittance) peak = i;
        // Use the solver's untruncated log(T) for relative shape. Absolute T can underflow,
        // and even a representable subnormal peak can lose its half-height when divided by two.
        var logPeak = samples[peak].Power.LogTransmittance;
        double Relative(int i) => Math.Exp(samples[i].Power.LogTransmittance - logPeak);
        double? left = null, right = null;
        // A truly evanescent exit has log(T)=-infinity everywhere: it has no transmission peak or FWHM.
        if (!double.IsNegativeInfinity(logPeak))
        {
            for (var i = peak - 1; i >= 0; i--)
                if (Relative(i) <= .5) { left = Crossing(samples[i], samples[i + 1], Relative(i), Relative(i + 1)); break; }
            for (var i = peak + 1; i < samples.Count; i++)
                if (Relative(i) <= .5) { right = Crossing(samples[i - 1], samples[i], Relative(i - 1), Relative(i)); break; }
        }
        return new(samples[peak].WavelengthNanometers, samples[peak].Power.Transmittance, left, right,
            left is not null && right is not null ? right - left : null,
            samples.Min(x => x.Power.Reflectance), samples.Max(x => x.Power.Reflectance));
    }

    public static AnalysisSeries[] ToSeries(IReadOnlyList<ThinFilmSample> samples) =>
        new[] { "R", "T", "A" }.Select((name, i) => new AnalysisSeries("波长 (nm)", "能量 (%)",
            samples.Select(x => new AnalysisPoint(x.WavelengthNanometers, 100 * (i == 0 ? x.Power.Reflectance
                : i == 1 ? x.Power.Transmittance : x.Power.Absorptance))).ToArray(), Name: name, ColorIndex: i,
            XQuantity: AnalysisAxisQuantity.Wavelength, XUnit: AnalysisAxisUnit.Nanometer,
            YQuantity: AnalysisAxisQuantity.EnergyFraction, YUnit: AnalysisAxisUnit.Percent)).ToArray();

    private static double Crossing(ThinFilmSample a, ThinFilmSample b, double relativeA, double relativeB) =>
        a.WavelengthNanometers + (b.WavelengthNanometers - a.WavelengthNanometers)
        * ((.5 - relativeA) / (relativeB - relativeA));
}
