using System.Numerics;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public static partial class DiffractionEngine
{
    /// <summary>Direct Fourier section along a unit direction, normalized by the supplied window's power.
    /// For a cropped PSF this is explicitly a conditional finite-window reference, not a full-image MTF.</summary>
    public static double[] ComputePsfMtfAlongDirection(PsfResult psf, IReadOnlyList<double> frequencies,
        double normalX, double normalY)
    {
        ArgumentNullException.ThrowIfNull(psf); ArgumentNullException.ThrowIfNull(frequencies);
        ValidatePsfGrid(psf with { CroppedImageCenterIndex = null });
        if (psf.Values.Cast<double>().Any(v => !double.IsFinite(v) || v < 0))
            throw new ArgumentException("PSF intensities must be finite and nonnegative.");
        if (!double.IsFinite(normalX) || !double.IsFinite(normalY) || Math.Abs(normalX * normalX + normalY * normalY - 1) > 1e-10
            || frequencies.Any(f => !double.IsFinite(f) || f < 0)) throw new ArgumentException("MTF needs a unit direction and finite nonnegative frequencies.");
        var total = psf.Values.Cast<double>().Sum();
        if (!double.IsFinite(total) || total <= 0) throw new InvalidOperationException("PSF has no positive finite power.");
        var spacing = psf.SampleSpacingMicrometers / (psf.SampleSpacingUnit == AnalysisAxisUnit.Micrometer ? 1000 : 1);
        var center = (psf.GridSize - 1) / 2.0;
        if (2L * psf.GridSize * psf.GridSize * frequencies.Count > AnalysisResourceLimits.MaximumDirectMtfOperations)
        {
            if (frequencies.Count < 2 || frequencies.Count > 1025
                || frequencies.Where((f, i) => Math.Abs(f - frequencies[0] - i * (frequencies[1] - frequencies[0])) > 1e-10).Any())
                throw new ArgumentException("Large directional MTF requires a bounded uniform frequency interval.");
            var n = psf.GridSize;
            var length = AnalysisResourceLimits.RoundUpPowerOfTwo(checked(n + frequencies.Count - 1), nameof(frequencies));
            if ((long)(n + 1) * length * (3 * (int)Math.Log2(length) + 2) + 4L * n * frequencies.Count > AnalysisResourceLimits.MaximumDirectFourierOperations)
                throw new ArgumentException("Directional MTF exceeds shared transform work bounds.");
            var plan = new PhysicalFourierInterval(n, frequencies.Count, (frequencies[1] - frequencies[0]) * normalX * spacing,
                frequencies[0] * normalX * spacing, center);
            var sums = new Complex[frequencies.Count]; var row = new Complex[n];
            for (var y = 0; y < n; y++)
            {
                for (var x = 0; x < n; x++) row[x] = psf.Values[y, x];
                var values = plan.Evaluate(row);
                for (var k = 0; k < sums.Length; k++)
                    sums[k] += values[k] * Complex.FromPolarCoordinates(1, -2 * Math.PI * frequencies[k] * normalY * (y - center) * spacing);
            }
            return sums.Select(z => z.Magnitude / total).ToArray();
        }
        AnalysisResourceLimits.ValidateDirectMtfWork(psf.GridSize, frequencies.Count);
        return frequencies.Select(f =>
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var x = Enumerable.Range(0, psf.GridSize).Select(i => Complex.FromPolarCoordinates(1, -2 * Math.PI * f * normalX * (i - center) * spacing)).ToArray();
            var y = Enumerable.Range(0, psf.GridSize).Select(i => Complex.FromPolarCoordinates(1, -2 * Math.PI * f * normalY * (i - center) * spacing)).ToArray();
            var sum = Complex.Zero;
            for (var row = 0; row < psf.GridSize; row++)
                for (var column = 0; column < psf.GridSize; column++) sum += psf.Values[row, column] * x[column] * y[row];
            return sum.Magnitude / total;
        }).ToArray();
    }
}
