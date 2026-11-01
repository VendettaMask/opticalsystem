namespace OpticalSystemDesign.Core.Coatings;

public sealed record ThinFilmBandSensitivity(ThinFilmBandMetrics Metrics, double[] PeakTransmittance,
    double[]? LeftHalfMaximumNanometers, double[]? RightHalfMaximumNanometers, double[]? FwhmNanometers)
{
    public double? PeakWavelengthEstimateNanometers { get; init; }
    public double[]? PeakWavelengthNanometers { get; init; }
}

public static partial class ThinFilmSpectrum
{
    public const string BandSensitivityVersion = "sampled-log-half-maximum-adjoint/1";

    /// <summary>
    /// Piecewise derivatives per nm of film thickness on a fixed, increasing wavelength grid.
    /// The selected peak and crossing brackets are held fixed locally. The sampled peak wavelength
    /// has no derivative; the separate local three-point vertex estimate has a piecewise derivative.
    /// Missing crossings or unavailable log derivatives produce null edge/width derivatives.
    /// </summary>
    public static ThinFilmBandSensitivity MeasureThicknessSensitivity(IReadOnlyList<double> wavelengths,
        IReadOnlyList<ThinFilmThicknessSensitivity> sensitivities)
    {
        if (wavelengths.Count < 3 || wavelengths.Count != sensitivities.Count)
            throw new ArgumentException("光谱与导数必须包含同一组至少三个采样点。");
        var count = sensitivities[0].Layers.Length;
        for (var i = 0; i < wavelengths.Count; i++)
            if (!double.IsFinite(wavelengths[i]) || wavelengths[i] <= 0 || i > 0 && wavelengths[i] <= wavelengths[i - 1]
                || sensitivities[i].Layers.Length != count)
                throw new ArgumentException("光谱导数要求递增波长及一致的膜层。");
        var samples = wavelengths.Select((w, i) => new ThinFilmSample(w, sensitivities[i].Power)).ToArray();
        var metrics = Measure(samples); var (peak, left, right, relative) = LocateBand(samples);
        double[]? Edge(int a)
        {
            if (a < 0 || !sensitivities[peak].HasLogTransmissionDerivative) return null;
            var b = a + 1;
            if (relative[a] > 0 && !sensitivities[a].HasLogTransmissionDerivative
                || relative[b] > 0 && !sensitivities[b].HasLogTransmissionDerivative) return null;
            var denominator = relative[b] - relative[a];
            var fraction = (.5 - relative[a]) / denominator;
            return Enumerable.Range(0, count).Select(j =>
            {
                double ShapeDerivative(int i) => relative[i] == 0 ? 0 : relative[i]
                    * (sensitivities[i].Layers[j].LogTransmittance - sensitivities[peak].Layers[j].LogTransmittance);
                var da = ShapeDerivative(a); var db = ShapeDerivative(b);
                return (wavelengths[b] - wavelengths[a]) * (-da - fraction * (db - da)) / denominator;
            }).ToArray();
        }
        var leftDerivative = Edge(left); var rightDerivative = Edge(right);
        var estimate = EstimatePeak(samples, peak);
        double[]? peakDerivative = null;
        if (estimate is not null && sensitivities.Skip(peak - 1).Take(3).All(s => s.HasLogTransmissionDerivative))
        {
            var hl = wavelengths[peak] - wavelengths[peak - 1]; var hr = wavelengths[peak + 1] - wavelengths[peak];
            var sl = (samples[peak].Power.LogTransmittance - samples[peak - 1].Power.LogTransmittance) / hl;
            var sr = (samples[peak + 1].Power.LogTransmittance - samples[peak].Power.LogTransmittance) / hr;
            var a = (sr - sl) / (hl + hr); var b = sl + a * hl;
            peakDerivative = Enumerable.Range(0, count).Select(j =>
            {
                var dl = (sensitivities[peak].Layers[j].LogTransmittance - sensitivities[peak - 1].Layers[j].LogTransmittance) / hl;
                var dr = (sensitivities[peak + 1].Layers[j].LogTransmittance - sensitivities[peak].Layers[j].LogTransmittance) / hr;
                var da = (dr - dl) / (hl + hr); var db = dl + da * hl;
                return -(db - b * da / a) / (2 * a);
            }).ToArray();
        }
        return new(metrics, sensitivities[peak].Layers.Select(l => l.Transmittance).ToArray(), leftDerivative, rightDerivative,
            leftDerivative is null || rightDerivative is null ? null : rightDerivative.Zip(leftDerivative, (r, l) => r - l).ToArray())
        { PeakWavelengthEstimateNanometers = estimate, PeakWavelengthNanometers = peakDerivative };
    }

    /// <summary>Local three-point log-spectrum vertex for optimization guidance only; formal metrics retain the sampled peak.</summary>
    public static double? EstimatePeakWavelength(IReadOnlyList<ThinFilmSample> samples)
    {
        if (samples.Count < 3) throw new ArgumentException("光谱至少需要三个采样点。");
        return EstimatePeak(samples, LocateBand(samples).Peak);
    }

    private static double? EstimatePeak(IReadOnlyList<ThinFilmSample> samples, int peak)
    {
        if (peak == 0 || peak == samples.Count - 1 || !samples.Skip(peak - 1).Take(3).All(s => double.IsFinite(s.Power.LogTransmittance))) return null;
        var hl = samples[peak].WavelengthNanometers - samples[peak - 1].WavelengthNanometers;
        var hr = samples[peak + 1].WavelengthNanometers - samples[peak].WavelengthNanometers;
        if (hl <= 0 || hr <= 0) throw new ArgumentException("峰位估计要求递增波长。");
        var sl = (samples[peak].Power.LogTransmittance - samples[peak - 1].Power.LogTransmittance) / hl;
        var sr = (samples[peak + 1].Power.LogTransmittance - samples[peak].Power.LogTransmittance) / hr;
        var a = (sr - sl) / (hl + hr); var b = sl + a * hl;
        if (a >= 0) return null;
        var shift = -b / (2 * a);
        return shift >= -hl && shift <= hr ? samples[peak].WavelengthNanometers + shift : null;
    }
}
