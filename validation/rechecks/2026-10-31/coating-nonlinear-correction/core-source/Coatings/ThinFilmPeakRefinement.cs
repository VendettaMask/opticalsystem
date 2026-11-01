using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Coatings;

public sealed record ThinFilmPeakRefinement(ThinFilmSample Peak, int Evaluations);

public static partial class ThinFilmSpectrum
{
    public const string PeakRefinementVersion = "bounded-log-peak-refinement/1";

    /// <summary>Bounded local refinement of the largest supplied transmission peak, using actual solver powers.
    /// The enclosing sample bracket is fixed; this is neither a global spectral certificate nor a replacement for Measure.</summary>
    public static ThinFilmPeakRefinement RefineTransmissionPeak(CoherentThinFilmSolver solver, IReadOnlyList<ThinFilmSample> samples,
        double angleDegrees, ThinFilmPolarization polarization, int iterations = 32)
    {
        ArgumentNullException.ThrowIfNull(solver);
        if (samples.Count < 3 || iterations < 1 || iterations > 128) throw new ArgumentException("峰值细化要求至少三个点，迭代范围为1–128。");
        for (var i = 0; i < samples.Count; i++)
            if (!double.IsFinite(samples[i].WavelengthNanometers) || samples[i].WavelengthNanometers <= 0
                || i > 0 && samples[i].WavelengthNanometers <= samples[i - 1].WavelengthNanometers)
                throw new ArgumentException("峰值细化要求正的递增波长。");
        var peak = LocateBand(samples).Peak; var best = samples[peak]; var evaluations = 0;
        if (peak == 0 || peak == samples.Count - 1 || double.IsNegativeInfinity(best.Power.LogTransmittance)) return new(best, 0);
        var left = samples[peak - 1].WavelengthNanometers; var right = samples[peak + 1].WavelengthNanometers;
        var ratio = (Math.Sqrt(5) - 1) / 2;
        ThinFilmSample Read(double wavelength)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var sample = new ThinFilmSample(wavelength, solver.Evaluate(wavelength, angleDegrees, polarization)); evaluations++;
            if (sample.Power.LogTransmittance > best.Power.LogTransmittance) best = sample;
            return sample;
        }
        var a = Read(right - ratio * (right - left)); var b = Read(left + ratio * (right - left));
        for (var i = 0; i < iterations; i++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            if (a.Power.LogTransmittance < b.Power.LogTransmittance)
            { left = a.WavelengthNanometers; a = b; b = Read(left + ratio * (right - left)); }
            else { right = b.WavelengthNanometers; b = a; a = Read(right - ratio * (right - left)); }
        }
        return new(best, evaluations);
    }
}
