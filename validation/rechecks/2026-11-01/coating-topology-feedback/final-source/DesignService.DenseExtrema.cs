using OpticalSystemDesign.Core.Coatings;

namespace OpticalSystemDesign.CoatingDesign.Engine;

public sealed partial class DesignService
{
    // Bounded orchestration of shared Core extrema. A locally largest sample is
    // not the physical peak, especially beside a hard centre or OD gate.
    private static (ThinFilmSample[] Samples, int Evaluations, bool Complete) RefineDenseExtrema(
        Experiment d, CoherentThinFilmSolver solver, ThinFilmSample[] samples, CancellationToken token)
    {
        const int maximumPeaks = 64;
        var domain = PassbandMetricDomain(d);
        var bands = new[] { new SpectralBand(domain.Minimum, domain.Maximum) }.Concat(d.Target.StopBands ?? []);
        var peaks = new List<ThinFilmSample>(); var evaluations = 0; var complete = true;
        foreach (var band in bands)
        {
            var local = samples.Where(s => s.WavelengthNanometers >= band.MinimumNm && s.WavelengthNanometers <= band.MaximumNm).ToArray();
            if (local.Length < 3) { complete = false; continue; }
            for (var i = 1; i < local.Length - 1; i++)
            {
                token.ThrowIfCancellationRequested();
                var left = local[i - 1].Power.LogTransmittance; var center = local[i].Power.LogTransmittance; var right = local[i + 1].Power.LogTransmittance;
                if (!(center >= left && center >= right && (center > left || center > right))) continue;
                if (peaks.Count == maximumPeaks) { complete = false; continue; }
                var refined = ThinFilmSpectrum.RefineTransmissionPeak(solver, [local[i - 1], local[i], local[i + 1]], d.Target.AngleDegrees, d.Target.Polarization);
                evaluations += refined.Evaluations; peaks.Add(refined.Peak);
            }
        }
        return (samples.Concat(peaks).GroupBy(s => s.WavelengthNanometers).Select(g => g.First()).OrderBy(s => s.WavelengthNanometers).ToArray(), evaluations, complete);
    }
}
