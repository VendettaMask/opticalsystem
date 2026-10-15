using System.Numerics;
using OpticalSystemDesign.Core.Rays;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

/// <summary>Electric/magnetic fields use exp(-i omega t). Magnetic values are Z0 H.
/// Flux is 2 Z0 integral(Sz dA); norm is integral(|E|² dA), not flux.</summary>
public sealed record VectorAngularSpectrumResult(ComplexElectricField[,] Electric,
    ComplexElectricField[,] Magnetic, double ElectricNormIntegral, double NormalFluxIntegral,
    double ProjectionDifferenceNormIntegral);

/// <summary>Forward monochromatic propagation between parallel planes in a
/// homogeneous isotropic passive nonmagnetic medium (mu_r=1).
/// This is not a lens/GRIN POP solver.</summary>
public static class AngularSpectrumEngine
{
    public static Complex[,] PropagateScalar(Complex[,] input, double pitchXMillimeters,
        double pitchYMillimeters, double wavelengthMillimeters, Complex refractiveIndex, double distanceMillimeters)
    {
        ArgumentNullException.ThrowIfNull(input);
        var n = Validate(input.GetLength(0), input.GetLength(1), pitchXMillimeters,
            pitchYMillimeters, wavelengthMillimeters, refractiveIndex, distanceMillimeters);
        var spectrum = (Complex[,])input.Clone();
        foreach (var value in spectrum) Validate(value);
        DiffractionEngine.FourierTransform2D(spectrum);
        var k = 2 * Math.PI * refractiveIndex / wavelengthMillimeters;
        for (var row = 0; row < n; row++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var column = 0; column < n; column++)
            {
                var kx = Frequency(column, n, pitchXMillimeters);
                var ky = Frequency(row, n, pitchYMillimeters);
                spectrum[row, column] *= Transfer(Normal(k * k - kx * kx - ky * ky), distanceMillimeters);
            }
        }
        DiffractionEngine.FourierTransform2D(spectrum, inverse: true);
        if (spectrum.Cast<Complex>().Any(v => !double.IsFinite(v.Real) || !double.IsFinite(v.Imaginary)))
            throw new ArithmeticException("Non-finite scalar angular-spectrum result.");
        return spectrum;
    }

    public static VectorAngularSpectrumResult PropagateVector(ComplexElectricField[,] input,
        double pitchXMillimeters, double pitchYMillimeters, double wavelengthMillimeters,
        Complex refractiveIndex, double distanceMillimeters, bool projectTransverse = true)
    {
        ArgumentNullException.ThrowIfNull(input);
        var n = Validate(input.GetLength(0), input.GetLength(1), pitchXMillimeters,
            pitchYMillimeters, wavelengthMillimeters, refractiveIndex, distanceMillimeters);
        if (12L * n * n > AnalysisResourceLimits.MaximumAggregateGridCells)
            throw new ArgumentOutOfRangeException(nameof(input), "Vector angular-spectrum field exceeds the aggregate grid budget.");
        var e = Enumerable.Range(0, 3).Select(_ => new Complex[n, n]).ToArray();
        var h = Enumerable.Range(0, 3).Select(_ => new Complex[n, n]).ToArray();
        for (var row = 0; row < n; row++)
        for (var column = 0; column < n; column++)
        {
            var value = input[row, column]; Validate(value.X); Validate(value.Y); Validate(value.Z);
            e[0][row, column] = value.X; e[1][row, column] = value.Y; e[2][row, column] = value.Z;
        }
        foreach (var component in e) DiffractionEngine.FourierTransform2D(component);
        var k = 2 * Math.PI * refractiveIndex / wavelengthMillimeters;
        double removed = 0;
        for (var row = 0; row < n; row++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            for (var column = 0; column < n; column++)
            {
                var x = Frequency(column, n, pitchXMillimeters) / k;
                var y = Frequency(row, n, pitchYMillimeters) / k;
                var z = Normal(k * k - Frequency(column, n, pitchXMillimeters) * Frequency(column, n, pitchXMillimeters)
                    - Frequency(row, n, pitchYMillimeters) * Frequency(row, n, pitchYMillimeters)) / k;
                var ex = e[0][row, column]; var ey = e[1][row, column]; var ez = e[2][row, column];
                var longitudinal = x * ex + y * ey + z * ez;
                var difference = new ComplexElectricField(x * longitudinal, y * longitudinal, z * longitudinal);
                removed += difference.SquaredNorm;
                if (!projectTransverse && difference.SquaredNorm > 1e-20 * Math.Max(1, ex.Magnitude * ex.Magnitude + ey.Magnitude * ey.Magnitude + ez.Magnitude * ez.Magnitude))
                    throw new ArgumentException("Input angular spectrum violates k dot E = 0; request explicit transverse projection.", nameof(input));
                if (projectTransverse) { ex -= difference.X; ey -= difference.Y; ez -= difference.Z; }
                var transfer = Transfer(k * z, distanceMillimeters);
                ex *= transfer; ey *= transfer; ez *= transfer;
                e[0][row, column] = ex; e[1][row, column] = ey; e[2][row, column] = ez;
                h[0][row, column] = refractiveIndex * (y * ez - z * ey);
                h[1][row, column] = refractiveIndex * (z * ex - x * ez);
                h[2][row, column] = refractiveIndex * (x * ey - y * ex);
            }
        }
        foreach (var component in e.Concat(h)) DiffractionEngine.FourierTransform2D(component, inverse: true);
        var electric = new ComplexElectricField[n, n]; var magnetic = new ComplexElectricField[n, n];
        double norm = 0, flux = 0;
        for (var row = 0; row < n; row++)
        for (var column = 0; column < n; column++)
        {
            var value = new ComplexElectricField(e[0][row, column], e[1][row, column], e[2][row, column]);
            var magnet = new ComplexElectricField(h[0][row, column], h[1][row, column], h[2][row, column]);
            electric[row, column] = value; magnetic[row, column] = magnet;
            norm += value.SquaredNorm;
            flux += (value.X * Complex.Conjugate(magnet.Y) - value.Y * Complex.Conjugate(magnet.X)).Real;
        }
        var area = pitchXMillimeters * pitchYMillimeters;
        if (!double.IsFinite(norm) || !double.IsFinite(flux) || !double.IsFinite(removed))
            throw new ArithmeticException("Non-finite vector angular-spectrum result.");
        return new(electric, magnetic, norm * area, flux * area, removed * area / (n * n));
    }

    private static int Validate(int rows, int columns, double dx, double dy, double lambda, Complex index, double distance)
    {
        if (rows != columns) throw new ArgumentException("Angular-spectrum grids must be square.");
        AnalysisResourceLimits.ValidateFftGrid(rows, columns);
        if (!double.IsFinite(dx) || !double.IsFinite(dy) || dx <= 0 || dy <= 0
            || !double.IsFinite(lambda) || lambda <= 0 || !double.IsFinite(distance) || distance < 0
            || !double.IsFinite(index.Real) || !double.IsFinite(index.Imaginary) || index.Real <= 0 || index.Imaginary < 0)
            throw new ArgumentOutOfRangeException(nameof(distance), "Positive finite pitches/wavelength and passive index, nonnegative forward distance required.");
        if (!double.IsFinite(2 * Math.PI * index.Real / lambda) || !double.IsFinite(2 * Math.PI * index.Imaginary / lambda)
            || !double.IsFinite(2 * Math.PI / (rows * dx)) || !double.IsFinite(2 * Math.PI / (rows * dy)))
            throw new ArgumentOutOfRangeException(nameof(lambda), "Wave numbers exceed the finite arithmetic range.");
        return rows;
    }
    private static void Validate(Complex value)
    {
        if (!double.IsFinite(value.Real) || !double.IsFinite(value.Imaginary))
            throw new ArgumentException("Angular-spectrum field must be finite.");
    }
    private static double Frequency(int i, int n, double pitch) => 2 * Math.PI * (i < n / 2 ? i : i - n) / (n * pitch);
    private static Complex Normal(Complex squared)
    {
        var value = Complex.Sqrt(squared);
        return value.Imaginary < 0 || (value.Imaginary == 0 && value.Real < 0) ? -value : value;
    }
    private static Complex Transfer(Complex kz, double distance)
    {
        if (!double.IsFinite(kz.Real) || !double.IsFinite(kz.Imaginary) || !double.IsFinite(kz.Real * distance))
            throw new ArithmeticException("Propagation phase exceeds the finite arithmetic range.");
        return Complex.FromPolarCoordinates(Math.Exp(-kz.Imaginary * distance), Math.IEEERemainder(kz.Real * distance, 2 * Math.PI));
    }
}
