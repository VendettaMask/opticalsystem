namespace OpticalSystemDesign.Core.Analysis.Imaging;

public sealed record SensorResponseSample(double WavelengthNanometers, double RedQe, double GreenQe, double BlueQe);

/// <summary>Absolute total QE (including the CFA). Linear interpolation; no extrapolation or peak normalization.</summary>
public sealed class SensorSpectralResponse
{
    public const int MaximumSamples = 4096;
    public IReadOnlyList<SensorResponseSample> Samples { get; }

    public SensorSpectralResponse(IEnumerable<SensorResponseSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var frozen = samples.Take(MaximumSamples + 1).ToArray();
        if (frozen.Length is < 2 or > MaximumSamples) throw new ArgumentException("QE curve requires 2–4096 samples.");
        double previous = double.NegativeInfinity;
        foreach (var sample in frozen)
        {
            if (sample is null || !double.IsFinite(sample.WavelengthNanometers)
                || sample.WavelengthNanometers is < 200 or > 20000 || sample.WavelengthNanometers <= previous)
                throw new ArgumentException("QE wavelengths must be finite, strictly increasing and within 200–20000 nm.");
            foreach (var qe in new[] { sample.RedQe, sample.GreenQe, sample.BlueQe })
                if (!double.IsFinite(qe) || qe is < 0 or > 1) throw new ArgumentException("Absolute QE must be in [0,1].");
            previous = sample.WavelengthNanometers;
        }
        Samples = Array.AsReadOnly(frozen);
    }

    public double Evaluate(int channel, double wavelengthNanometers)
    {
        if (channel is < 0 or > 2 || !double.IsFinite(wavelengthNanometers)) throw new ArgumentOutOfRangeException(nameof(channel));
        if (wavelengthNanometers < Samples[0].WavelengthNanometers || wavelengthNanometers > Samples[^1].WavelengthNanometers)
            throw new ArgumentOutOfRangeException(nameof(wavelengthNanometers), "QE data does not cover this wavelength; extrapolation is disabled.");
        var low = 0; var high = Samples.Count - 1;
        while (high - low > 1)
        {
            var middle = (low + high) / 2;
            if (Samples[middle].WavelengthNanometers <= wavelengthNanometers) low = middle; else high = middle;
        }
        var fraction = (wavelengthNanometers - Samples[low].WavelengthNanometers)
            / (Samples[high].WavelengthNanometers - Samples[low].WavelengthNanometers);
        return Value(Samples[low]) * (1 - fraction) + Value(Samples[high]) * fraction;
        double Value(SensorResponseSample sample) => channel switch { 0 => sample.RedQe, 1 => sample.GreenQe, _ => sample.BlueQe };
    }

    public void ValidateCameraCoverage()
    {
        foreach (var wavelength in CameraSensorPipeline.BandWavelengthsNanometers) _ = Evaluate(0, wavelength);
    }
}
