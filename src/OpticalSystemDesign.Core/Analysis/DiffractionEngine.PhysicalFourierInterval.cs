using System.Numerics;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public static partial class DiffractionEngine
{
    // Exact finite Fourier sum at equally spaced arbitrary physical frequencies.
    // Bluestein's identity: j*k = (j²+k²-(j-k)²)/2. No interpolation or resampling.
    private sealed class PhysicalFourierInterval
    {
        private readonly Complex[] _input, _output, _kernel;
        public int Size => _kernel.Length;
        public PhysicalFourierInterval(int count, int length, double step, double first, double center)
        {
            var size = AnalysisResourceLimits.RoundUpPowerOfTwo(checked(count + length - 1), nameof(length));
            _input = new Complex[count]; _output = new Complex[length]; _kernel = new Complex[size];
            for (var j = 0; j < count; j++)
                _input[j] = Complex.FromPolarCoordinates(1, -2 * Math.PI * (first * j + step * j * (double)j / 2));
            for (var k = 0; k < length; k++)
                _output[k] = Complex.FromPolarCoordinates(1, 2 * Math.PI * ((first + step * k) * center - step * k * (double)k / 2));
            for (var d = -(count - 1); d < length; d++)
                _kernel[(d + size) % size] = Complex.FromPolarCoordinates(1, Math.PI * step * d * (double)d);
            Fft(_kernel);
        }
        public Complex[] Evaluate(Complex[] input)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            if (input.Length != _input.Length) throw new ArgumentException("Fourier interval source length differs.");
            var buffer = new Complex[Size];
            for (var j = 0; j < input.Length; j++) buffer[j] = input[j] * _input[j];
            Fft(buffer);
            for (var j = 0; j < Size; j++) buffer[j] = Complex.Conjugate(buffer[j] * _kernel[j]);
            Fft(buffer);
            var output = new Complex[_output.Length];
            for (var k = 0; k < output.Length; k++) output[k] = Complex.Conjugate(buffer[k]) * _output[k] / Size;
            return output;
        }
    }

    internal static Complex[,] PhysicalFourierWindow(Complex[,] pupil, int length, double stepX, double stepY)
    {
        ArgumentNullException.ThrowIfNull(pupil);
        var n = pupil.GetLength(0);
        if (n < 2 || n != pupil.GetLength(1) || length < 1 || !double.IsFinite(stepX) || !double.IsFinite(stepY))
            throw new ArgumentException("Physical Fourier window requires a square pupil and finite steps.");
        var planSize = AnalysisResourceLimits.RoundUpPowerOfTwo(checked(n + length - 1), nameof(length));
        if ((long)n * n + (long)n * length + (long)length * length > AnalysisResourceLimits.MaximumAggregateGridCells
            || (long)(n + length + 1) * planSize * (3 * (int)Math.Log2(planSize) + 2)
                + 2L * (n + length) * (n + length) > AnalysisResourceLimits.MaximumDirectFourierOperations)
            throw new ArgumentException("Physical Fourier window exceeds shared storage or transform work bounds.");
        var first = -length / 2;
        var xPlan = new PhysicalFourierInterval(n, length, stepX, first * stepX, (n - 1) / 2d);
        var yPlan = new PhysicalFourierInterval(n, length, stepY, first * stepY, (n - 1) / 2d);
        var intermediate = new Complex[n, length]; var input = new Complex[n];
        for (var y = 0; y < n; y++)
        {
            for (var x = 0; x < n; x++) input[x] = pupil[y, x];
            var row = xPlan.Evaluate(input);
            for (var x = 0; x < length; x++) intermediate[y, x] = row[x];
        }
        var output = new Complex[length, length];
        for (var x = 0; x < length; x++)
        {
            for (var y = 0; y < n; y++) input[y] = intermediate[y, x];
            var column = yPlan.Evaluate(input);
            for (var y = 0; y < length; y++) output[y, x] = column[y];
        }
        return output;
    }
}
