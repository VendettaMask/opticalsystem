using System.Numerics;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public static partial class DiffractionEngine
{
    internal static (double Total, double X, double Y) FourierImageMoments(Complex[,] pupil, int transformSize)
    {
        var size = pupil.GetLength(0);
        ValidateSeparatedWindow(size, transformSize, 2, 2);
        if (pupil.GetLength(1) != size) throw new ArgumentException("Square pupil required");
        var power = pupil.Cast<Complex>().Sum(v => v.Real * v.Real + v.Imaginary * v.Imaginary);
        if (!(power > 0)) throw new AnalysisDataUnavailableException("FFT energy", "no illuminated pupil");
        var correlationSize = 1; while (correlationSize < 2 * size) correlationSize *= 2;
        var correlation = FourierPupilAutocorrelation(pupil, correlationSize);
        return (transformSize * (double)transformSize * power, Moment(correlation.Sagittal), Moment(correlation.Tangential));

        double Moment(Complex[] normalizedCorrelation)
        {
            // Exact first moment on the independent interval -N/2+1..N/2.
            // Expanding |F(k)|² gives the finite geometric-series kernel
            // sum(k*exp(-2πikd/N))/N = (-1)^d*(1+i*cot(πd/N))/2.
            // The d=0 kernel is 1/2. The existing axis correlation has the
            // opposite lag orientation, hence its conjugate here. Every image
            // pixel contributes analytically, including power outside readout.
            var moment = .5;
            for (var lag = 1; lag < size; lag++)
            {
                var sign = lag % 2 == 0 ? 1d : -1d;
                moment += sign * (normalizedCorrelation[lag].Real
                    + normalizedCorrelation[lag].Imaginary / Math.Tan(Math.PI * lag / transformSize));
            }
            return moment;
        }
    }

    private static long FourierImageMomentButterflies(Complex[,] pupil)
    {
        var size = pupil.GetLength(0); var transform = 1; while (transform < 2 * size) transform *= 2;
        var rows = Enumerable.Range(0, size).Count(y => Enumerable.Range(0, size).Any(x => pupil[y, x] != Complex.Zero));
        var columns = Enumerable.Range(0, size).Count(x => Enumerable.Range(0, size).Any(y => pupil[y, x] != Complex.Zero));
        return (long)(rows + columns + 2) * transform * (int)Math.Log2(transform) / 2;
    }

    private static void ValidateSeparatedWindow(int size, int transformSize, int width, int height)
    {
        if (size < 2 || size > AnalysisResourceLimits.MaximumFftGridSize
            || transformSize < size || transformSize > MaximumBoundedFourierTransformSize || (transformSize & (transformSize - 1)) != 0
            || width < 1 || height < 1 || width > AnalysisResourceLimits.MaximumFftGridSize || height > AnalysisResourceLimits.MaximumFftGridSize
            || (long)size * size + (long)size * width + (long)width * height > AnalysisResourceLimits.MaximumAggregateGridCells)
            throw new ArgumentOutOfRangeException(nameof(transformSize), "Separated Fourier window exceeds its source, readout or aggregate budget");
    }

    // Chirp convolution evaluates contiguous exact DFT frequencies with an FFT
    // length based on source+readout, rather than the enormous logical image.
    internal static Complex[,] FourierTransformWindow(Complex[,] pupil, int transformSize,
        int firstX, int firstY, int width, int height, long additionalButterflies = 0)
    {
        var size = pupil.GetLength(0);
        ValidateSeparatedWindow(size, transformSize, width, height);
        if (pupil.GetLength(1) != size || firstX <= -transformSize / 2 || firstY <= -transformSize / 2
            || (long)firstX + width - 1 > transformSize / 2 || (long)firstY + height - 1 > transformSize / 2)
            throw new ArgumentOutOfRangeException(nameof(firstX), "Window must stay inside the physical independent Fourier interval");
        var xPlan = new FourierInterval(size, transformSize, firstX, width);
        var yPlan = new FourierInterval(size, transformSize, firstY, height);
        // Each interval uses two FFTs, each with L*log2(L)/2 complex butterflies.
        var activeRows = Enumerable.Range(0, size).Count(y => Enumerable.Range(0, size).Any(x => pupil[y, x] != Complex.Zero));
        if (additionalButterflies < 0 || additionalButterflies + (long)activeRows * xPlan.Size * (int)Math.Log2(xPlan.Size) + (long)width * yPlan.Size * (int)Math.Log2(yPlan.Size)
            + ((long)xPlan.Size * (int)Math.Log2(xPlan.Size) + (long)yPlan.Size * (int)Math.Log2(yPlan.Size)) / 2
            > AnalysisResourceLimits.MaximumPsfBasisOperations)
            throw new ArgumentOutOfRangeException(nameof(width), "Separated Fourier window exceeds its work budget");
        var intermediate = new Complex[size, width]; var input = new Complex[size];
        for (var row = 0; row < size; row++)
        {
            for (var x = 0; x < size; x++) input[x] = pupil[row, x];
            if (input.All(v => v == Complex.Zero)) continue;
            var values = xPlan.Evaluate(input);
            for (var x = 0; x < width; x++) intermediate[row, x] = values[x];
        }
        var output = new Complex[height, width];
        for (var x = 0; x < width; x++)
        {
            for (var row = 0; row < size; row++) input[row] = intermediate[row, x];
            var values = yPlan.Evaluate(input);
            for (var row = 0; row < height; row++) output[row, x] = values[row];
        }
        return output;
    }

    private sealed class FourierInterval
    {
        private readonly Complex[] _inputChirp, _outputChirp, _kernel;
        public int Size => _kernel.Length;
        public FourierInterval(int count, int logicalSize, int first, int length)
        {
            var size = 1; while (size < count + length - 1) size *= 2;
            _kernel = new Complex[size]; _inputChirp = new Complex[count]; _outputChirp = new Complex[length];
            var theta = 2 * Math.PI / logicalSize; var offset = (logicalSize - count) / 2;
            for (var n = 0; n < count; n++)
                _inputChirp[n] = Complex.FromPolarCoordinates(1, -theta * (.5 * n * (double)n + first * (double)n));
            for (var k = 0; k < length; k++)
                _outputChirp[k] = Complex.FromPolarCoordinates(1, -theta * (.5 * k * (double)k + (first + k) * (double)offset));
            for (var m = -(count - 1); m < length; m++)
                _kernel[(m + size) % size] = Complex.FromPolarCoordinates(1, theta * .5 * m * (double)m);
            Fft(_kernel);
        }
        public Complex[] Evaluate(Complex[] input)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            var buffer = new Complex[Size];
            for (var i = 0; i < input.Length; i++) buffer[i] = input[i] * _inputChirp[i];
            Fft(buffer);
            for (var i = 0; i < Size; i++) buffer[i] = Complex.Conjugate(buffer[i] * _kernel[i]);
            Fft(buffer);
            return Enumerable.Range(0, _outputChirp.Length)
                .Select(i => Complex.Conjugate(buffer[i]) * _outputChirp[i] / Size).ToArray();
        }
    }
}
