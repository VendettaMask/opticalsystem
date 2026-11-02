using System.Numerics;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis.Imaging;

public static partial class MonochromeSlantedEdge
{
    public static IReadOnlyList<SlantedEdgeSpectralBand> LensSpectrum(Optic optic)
    {
        ArgumentNullException.ThrowIfNull(optic);
        if (optic.Wavelengths.Count is < 1 or > 16)
            throw new ArgumentException("斜边光谱需要 1–16 个当前镜头波长。");
        if (optic.Wavelengths.Any(w => !double.IsFinite(w.Nanometers) || !double.IsFinite(w.Weight) || w.Weight < 0
            || w.Weight > 0 && w.Nanometers is < 200 or > 20000))
            throw new ArgumentException("镜头波长或权重无效；参与计算的波长需为 200–20000 nm。");
        var maximum = optic.Wavelengths.Max(w => w.Weight);
        if (!(maximum > 0)) throw new ArgumentException("镜头的波长权重全为零，无法合成线性斜边。");
        // Scale first so even very large finite weights cannot overflow their sum.
        var total = optic.Wavelengths.Sum(w => w.Weight / maximum);
        return Array.AsReadOnly(optic.Wavelengths.Select((w, i) => new SlantedEdgeSpectralBand(i + 1,
            w.Nanometers, w.Weight, (w.Weight / maximum) / total, w.IsPrimary)).ToArray());
    }

    private static SlantedEdgeFrame RenderSpectrum(Optic optic, SlantedEdgeSettings settings)
    {
        var spectrum = LensSpectrum(optic);
        var reference = optic.Wavelengths.FirstOrDefault(w => w.IsPrimary) ?? optic.Wavelengths[0];
        if (reference.Nanometers is < 200 or > 20000) throw new ArgumentException("主波长超出局部斜边模型的波长范围。");
        var selected = optic.Fields[settings.FieldNumber - 1];
        var field = FieldCoordinates.Normalize(optic.Fields, selected.X, selected.Y);
        var grid = checked(2 * settings.PsfRadiusPixels * settings.ImageSamplesPerPixel + 1);
        var active = spectrum.Count(b => b.Weight > 0);
        if ((active + 4L) * settings.PupilSampling * settings.PupilSampling + 2L * grid * grid
            + 2L * grid * settings.PupilSampling > AnalysisResourceLimits.MaximumAggregateGridCells)
            throw new ArgumentException("多波长瞳孔与 PSF 超出共享累计内存预算；请减少显式采样或波长数量。");
        var accumulated = new double[grid, grid]; PsfResult? template = null;
        var pupils = new List<(SlantedEdgeSpectralBand Band, Complex[,] Pupil, PsfResult Parameters)>();
        foreach (var band in spectrum.Where(b => b.Weight > 0))
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var psf = DiffractionEngine.ComputeLocalIncoherentPsf(optic, (field.X, field.Y),
                optic.Wavelengths[band.WavelengthNumber - 1], settings.PupilSampling, grid,
                settings.PixelPitchMicrometers / settings.ImageSamplesPerPixel, reference, out var pupil);
            var parameters = psf with { Values = new double[0, 0] };
            template ??= parameters;
            pupils.Add((band, pupil, parameters));
            // Each kernel is normalized to its FULL sampled pupil power. Never
            // normalize each cropped wavelength separately before mixing.
            AccumulateSpectralPsf(accumulated, psf.Values, band.NormalizedWeight);
        }
        var frame = RenderPsf(template! with { Values = accumulated }, settings);
        var norm = Math.Sqrt(1 + frame.Measurement.EdgeSlope * frame.Measurement.EdgeSlope);
        var nx = 1 / norm; var ny = -frame.Measurement.EdgeSlope / norm;
        var frequencies = frame.Measurement.Curve.Select(p => p.CyclesPerMillimeter).ToArray();
        var otfs = new List<Complex[]>(); var weights = new List<double>();
        foreach (var (band, pupil, parameters) in pupils)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            otfs.Add(DiffractionEngine.ComputeLocalPupilOtf(pupil, parameters,
                optic.Wavelengths[band.WavelengthNumber - 1], frequencies, nx, ny));
            weights.Add(band.NormalizedWeight);
        }
        var full = CombineSpectralOtf(otfs, weights);
        var optical = frame.Measurement.Curve.Select((p, i) => p with { Response = full[i] }).ToArray();
        var integrated = frame.Measurement.Curve.Select((p, i) => p with { Response = full[i]
            * SlantedEdgeSfr.Sinc(p.CyclesPerPixel * nx) * SlantedEdgeSfr.Sinc(p.CyclesPerPixel * ny) }).ToArray();
        return frame with { Spectrum = spectrum, ReferenceWavelengthNanometers = reference.Nanometers,
            FullPupilMtf = Array.AsReadOnly(optical), FullPupilPixelIntegratedMtf = Array.AsReadOnly(integrated),
            MaximumSfrFullPupilDifference = frame.Measurement.Curve.Zip(integrated, (a, b) => Math.Abs(a.Response - b.Response)).Max(),
            MaximumWindowFullPupilDifference = frame.OpticalMtf.Zip(optical, (a, b) => Math.Abs(a.Response - b.Response)).Max() };
    }

    internal static void AccumulateSpectralPsf(double[,] sum, double[,] band, double weight)
    {
        if (sum.GetLength(0) != band.GetLength(0) || sum.GetLength(1) != band.GetLength(1)
            || !double.IsFinite(weight) || weight < 0) throw new ArgumentException("Spectral PSF lattice or weight differs.");
        for (var y = 0; y < sum.GetLength(0); y++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var x = 0; x < sum.GetLength(1); x++)
            {
                var value = band[y, x];
                if (!double.IsFinite(value) || value < 0) throw new ArgumentException("Spectral PSF intensity must be finite and nonnegative.");
                sum[y, x] += weight * value;
            }
        }
    }

    internal static double[] CombineSpectralOtf(IReadOnlyList<Complex[]> otfs, IReadOnlyList<double> weights)
    {
        if (otfs.Count is < 1 or > 16 || weights.Count != otfs.Count || otfs[0].Length is < 1 or > 1025
            || otfs.Any(o => o.Length != otfs[0].Length || o.Any(z => !double.IsFinite(z.Real) || !double.IsFinite(z.Imaginary)))
            || weights.Any(w => !double.IsFinite(w) || w < 0) || !(weights.Sum() > 0))
            throw new ArgumentException("Invalid bounded spectral OTF mixture.");
        var combined = new Complex[otfs[0].Length];
        for (var b = 0; b < otfs.Count; b++) for (var i = 0; i < combined.Length; i++) combined[i] += weights[b] * otfs[b][i];
        var dc = combined[0].Magnitude;
        if (!double.IsFinite(dc) || !(dc > 0)) throw new InvalidOperationException("Spectral OTF has no finite positive DC.");
        return combined.Select(z => z.Magnitude / dc).ToArray();
    }
}
