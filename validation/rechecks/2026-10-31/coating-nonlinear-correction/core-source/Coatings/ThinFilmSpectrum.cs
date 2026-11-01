using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Coatings;

public sealed record ThinFilmSample(double WavelengthNanometers, ThinFilmPower Power);
public sealed record ThinFilmBandMetrics(double PeakWavelengthNanometers, double PeakTransmittance,
    double? LeftHalfMaximumNanometers, double? RightHalfMaximumNanometers,
    double? FwhmNanometers, double MinimumReflectance, double MaximumReflectance);

public static partial class ThinFilmSpectrum
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
        var (peak, leftIndex, rightIndex, relative) = LocateBand(samples);
        // Use the solver's untruncated log(T) for relative shape. Absolute T can underflow,
        // and even a representable subnormal peak can lose its half-height when divided by two.
        double? Edge(int i) => i < 0 ? null : Crossing(samples[i], samples[i + 1], relative[i], relative[i + 1]);
        var left = Edge(leftIndex); var right = Edge(rightIndex);
        return new(samples[peak].WavelengthNanometers, samples[peak].Power.Transmittance, left, right,
            left is not null && right is not null ? right - left : null,
            samples.Min(x => x.Power.Reflectance), samples.Max(x => x.Power.Reflectance));
    }

    private static (int Peak, int Left, int Right, double[] Relative) LocateBand(IReadOnlyList<ThinFilmSample> samples)
    {
        var peak = 0;
        for (var i = 1; i < samples.Count; i++)
            if (samples[i].Power.LogTransmittance > samples[peak].Power.LogTransmittance) peak = i;
        var relative = new double[samples.Count]; var left = -1; var right = -1;
        var logPeak = samples[peak].Power.LogTransmittance;
        // A truly evanescent exit has no transmission peak or FWHM.
        if (!double.IsNegativeInfinity(logPeak))
        {
            for (var i = 0; i < samples.Count; i++) relative[i] = Math.Exp(samples[i].Power.LogTransmittance - logPeak);
            for (var i = peak - 1; i >= 0; i--) if (relative[i] <= .5) { left = i; break; }
            for (var i = peak + 1; i < samples.Count; i++) if (relative[i] <= .5) { right = i - 1; break; }
        }
        return (peak, left, right, relative);
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
