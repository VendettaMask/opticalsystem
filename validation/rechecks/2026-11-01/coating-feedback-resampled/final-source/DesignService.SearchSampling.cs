using OpticalSystemDesign.Core.Coatings;

namespace OpticalSystemDesign.CoatingDesign.Engine;

public sealed partial class DesignService
{
    public const string SearchSamplingProtocol = "search-peak-reference/2";
    // Preserve the established search peak bracket independently of formal metric-domain
    // sampling. These samples guide a direction only; every candidate uses Evaluate for
    // its current physical metrics, convergence and acceptance. No cached optics are used.
    private static ThinFilmSample[] SearchPeakReferenceSamples(Experiment d, CoherentThinFilmSolver solver, GeneralCounter work)
    {
        work.PeakReferenceBatches++;
        var t = d.Target;
        ThinFilmBandMetrics Measure(ThinFilmSample[] s) => ThinFilmSpectrum.Measure(s.Where(x =>
            Math.Abs(x.WavelengthNanometers - t.CenterNm) <= 2 * t.FwhmNm).ToArray());
        ThinFilmSample[] Scan(int count)
        {
            var samples = ThinFilmSpectrum.Sample(solver, GridWithAnchors(d, count), t.AngleDegrees, t.Polarization);
            work.Spectrum += samples.Length;
            var m = Measure(samples); var step = (t.MaximumNm - t.MinimumNm) / count; var points = new List<double>();
            foreach (var center in new double?[] { m.PeakWavelengthNanometers, m.LeftHalfMaximumNanometers, m.RightHalfMaximumNanometers })
                if (center.HasValue) points.AddRange(ThinFilmSpectrum.Grid(Math.Max(t.MinimumNm, center.Value - 2 * step), Math.Min(t.MaximumNm, center.Value + 2 * step), 32));
            var local = ThinFilmSpectrum.Sample(solver, points, t.AngleDegrees, t.Polarization); work.Spectrum += local.Length;
            return samples.Concat(local).GroupBy(s => s.WavelengthNanometers).Select(g => g.First()).OrderBy(s => s.WavelengthNanometers).ToArray();
        }
        bool Stable(ThinFilmSample[] a, ThinFilmSample[] b)
        {
            var x = Measure(a); var y = Measure(b);
            return Math.Abs(x.MinimumReflectance - y.MinimumReflectance) <= 1e-5 && Math.Abs(x.MaximumReflectance - y.MaximumReflectance) <= 1e-5
                && x.FwhmNanometers.HasValue && y.FwhmNanometers.HasValue && Math.Abs(x.PeakTransmittance - y.PeakTransmittance) <= 1e-5
                && Math.Abs(x.PeakWavelengthNanometers - y.PeakWavelengthNanometers) <= Math.Min(0.02, t.CenterToleranceNm / 10)
                && Math.Abs(x.FwhmNanometers.Value - y.FwhmNanometers.Value) <= Math.Min(0.02, t.FwhmToleranceNm / 10)
                && (t.StopBands ?? []).All(band => Math.Abs(MinimumOd(a, band) - MinimumOd(b, band)) <= 0.005);
        }
        var intervals = Math.Max(d.Settings.PreviewIntervals, (int)Math.Ceiling((t.MaximumNm - t.MinimumNm) / t.FwhmNm * 64));
        var samples = Scan(intervals);
        for (var attempt = 0; attempt < 4 && intervals * 2 < ThinFilmSpectrum.MaximumSamples - 512; attempt++)
        {
            intervals *= 2; var next = Scan(intervals); var stable = Stable(samples, next); samples = next;
            if (stable) break;
        }
        return samples;
    }
}
