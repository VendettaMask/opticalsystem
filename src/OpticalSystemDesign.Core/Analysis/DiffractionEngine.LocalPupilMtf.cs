using System.Numerics;
using OpticalSystemDesign.Core.Domain;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public static partial class DiffractionEngine
{
    /// <summary>Independent full-image MTF from the shared scalar complex pupil, without a PSF readout window.
    /// Integrates the autocorrelation of piecewise-constant pupil cells exactly at arbitrary physical shifts.</summary>
    public static double[] ComputeLocalPupilMtfAlongDirection(Optic optic, (double Hx, double Hy) field,
        Wavelength wavelength, int pupilSampling, IReadOnlyList<double> cyclesPerMillimeter, double normalX, double normalY)
        => ComputeLocalPupilOtfAlongDirection(optic, field, wavelength, pupilSampling, cyclesPerMillimeter, normalX, normalY)
            .Select(z => z.Magnitude).ToArray();

    /// <summary>Normalized complex OTF with the negative image-Fourier convention. A shared reference
    /// wavelength preserves chromatic displacement; combine spectral OTFs before taking magnitude.</summary>
    public static Complex[] ComputeLocalPupilOtfAlongDirection(Optic optic, (double Hx, double Hy) field,
        Wavelength wavelength, int pupilSampling, IReadOnlyList<double> cyclesPerMillimeter, double normalX, double normalY,
        Wavelength? referenceWavelength = null)
    {
        ArgumentNullException.ThrowIfNull(cyclesPerMillimeter);
        var (pupil, parameters) = PrepareLocalScalarPupil(optic, field, wavelength, pupilSampling, referenceWavelength);
        return ComputeLocalPupilOtf(pupil, parameters, wavelength, cyclesPerMillimeter, normalX, normalY);
    }

    internal static Complex[] ComputeLocalPupilOtf(Complex[,] pupil, PsfResult parameters, Wavelength wavelength,
        IReadOnlyList<double> cyclesPerMillimeter, double normalX, double normalY)
        => ComputeCellPupilOtf(pupil, cyclesPerMillimeter, normalX, normalY,
            wavelength.Micrometers * .001 * parameters.SagittalWorkingFNumber * pupil.GetLength(0),
            wavelength.Micrometers * .001 * parameters.TangentialWorkingFNumber * pupil.GetLength(0));

    internal static double[] ComputeCellPupilMtf(Complex[,] pupil, IReadOnlyList<double> frequencies,
        double normalX, double normalY, double cellsPerFrequencyX, double cellsPerFrequencyY)
        => ComputeCellPupilOtf(pupil, frequencies, normalX, normalY, cellsPerFrequencyX, cellsPerFrequencyY).Select(z => z.Magnitude).ToArray();

    internal static Complex[] ComputeCellPupilOtf(Complex[,] pupil, IReadOnlyList<double> frequencies,
        double normalX, double normalY, double cellsPerFrequencyX, double cellsPerFrequencyY)
    {
        ArgumentNullException.ThrowIfNull(pupil); ArgumentNullException.ThrowIfNull(frequencies);
        var n = pupil.GetLength(0);
        if (n < 2 || n != pupil.GetLength(1) || n > AnalysisResourceLimits.MaximumFftGridSize
            || !double.IsFinite(normalX) || !double.IsFinite(normalY) || Math.Abs(normalX * normalX + normalY * normalY - 1) > 1e-10
            || !double.IsFinite(cellsPerFrequencyX) || cellsPerFrequencyX <= 0 || !double.IsFinite(cellsPerFrequencyY) || cellsPerFrequencyY <= 0
            || frequencies.Count > 1025 || frequencies.Any(f => !double.IsFinite(f) || f < 0))
            throw new ArgumentException("Pupil MTF requires a bounded square pupil, unit direction and finite physical frequency scale.");
        double power = 0;
        foreach (var p in pupil)
        {
            if (!double.IsFinite(p.Real) || !double.IsFinite(p.Imaginary)) throw new ArgumentException("Pupil amplitudes must be finite.");
            power += p.Real * p.Real + p.Imaginary * p.Imaginary;
        }
        if (!double.IsFinite(power) || !(power > 0)) throw new InvalidOperationException("Pupil has no finite transmitted power.");
        var shifts = frequencies.Select(f => (X: f * normalX * cellsPerFrequencyX, Y: f * normalY * cellsPerFrequencyY)).ToArray();
        var needed = new HashSet<(int X, int Y)>();
        foreach (var shift in shifts)
        {
            if (Math.Abs(shift.X) >= n || Math.Abs(shift.Y) >= n) continue;
            var x = (int)Math.Floor(shift.X); var y = (int)Math.Floor(shift.Y);
            needed.Add((x, y)); needed.Add((x + 1, y)); needed.Add((x, y + 1)); needed.Add((x + 1, y + 1));
        }
        var correlation = new Dictionary<(int X, int Y), Complex>();
        if (2L * n * n * needed.Count > AnalysisResourceLimits.MaximumDirectMtfOperations)
        {
            // Zero padding makes this a linear correlation, without pupil wrap-around.
            // Reuse the bounded shared FFT rather than raising the direct-work budget.
            var size = AnalysisResourceLimits.RoundUpPowerOfTwo(checked(2 * n), nameof(pupil));
            var spectrum = new Complex[size, size];
            for (var y = 0; y < n; y++) for (var x = 0; x < n; x++) spectrum[y, x] = pupil[y, x];
            FourierTransform2D(spectrum);
            for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
                spectrum[y, x] = spectrum[y, x].Real * spectrum[y, x].Real + spectrum[y, x].Imaginary * spectrum[y, x].Imaginary;
            FourierTransform2D(spectrum, inverse: true);
            foreach (var shift in needed)
                correlation[shift] = spectrum[(shift.Y + size) % size, (shift.X + size) % size] / power;
        }
        else foreach (var shift in needed)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            Complex sum = 0;
            for (var y = Math.Max(0, -shift.Y); y < Math.Min(n, n - shift.Y); y++)
                for (var x = Math.Max(0, -shift.X); x < Math.Min(n, n - shift.X); x++)
                    sum += pupil[y + shift.Y, x + shift.X] * Complex.Conjugate(pupil[y, x]);
            correlation[shift] = sum / power;
        }
        return shifts.Select(s =>
        {
            if (Math.Abs(s.X) >= n || Math.Abs(s.Y) >= n) return Complex.Zero;
            var x = (int)Math.Floor(s.X); var y = (int)Math.Floor(s.Y); var fx = s.X - x; var fy = s.Y - y;
            return Complex.Conjugate((1 - fy) * ((1 - fx) * correlation[(x, y)] + fx * correlation[(x + 1, y)])
                + fy * ((1 - fx) * correlation[(x, y + 1)] + fx * correlation[(x + 1, y + 1)]));
        }).ToArray();
    }
}
