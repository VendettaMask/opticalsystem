using System.Numerics;
using OpticalSystemDesign.Core.Services;

namespace OpticalSystemDesign.Core.Analysis;

public static partial class DiffractionEngine
{
    /// <summary>Full-pupil FFT MTF axes without allocating the two-dimensional zero-padded image.</summary>
    public static MtfResult ComputeFftMtfFromPupil(Optic optic, (double Hx, double Hy) field,
        Domain.Wavelength wavelength, int pupilSampling, int transformSize,
        bool usePolarization = false, bool cellCenteredPupil = false,
        bool zemaxFftSampling = false, Domain.Wavelength? referenceWavelength = null)
    {
        if (transformSize < 2 * pupilSampling)
            throw new ArgumentOutOfRangeException(nameof(transformSize), "Linear pupil correlation requires at least double padding.");
        // Reuse the complete optical pupil preparation, including its reference,
        // acceptance, amplitude and launch convention. The tiny image readout
        // supplies metadata only; it never supplies MTF power or normalization.
        var metadata = ComputeFftPsfCore(optic, field, wavelength, pupilSampling, transformSize,
            usePolarization, cellCenteredPupil, 0, null, null, zemaxFftSampling, false, null,
            referenceWavelength, zemaxFftSampling ? -1 : 0, false, out var pupil, croppedOutputSize: 2);
        if (zemaxFftSampling) metadata = metadata with { FrequencySampleCount = pupilSampling - 1 };
        var (tangential, sagittal) = FourierPupilAutocorrelation(pupil, transformSize);
        return CreateFftMtfResult(metadata, optic, wavelength, tangential, sagittal);
    }

    internal static (Complex[] Tangential, Complex[] Sagittal) FourierPupilAutocorrelation(Complex[,] pupil, int transformSize)
    {
        var size = pupil.GetLength(0);
        if (pupil.GetLength(1) != size) throw new ArgumentException("A square pupil is required", nameof(pupil));
        ValidateCroppedFft(size, transformSize, 2);
        if (transformSize < 2 * size)
            throw new ArgumentOutOfRangeException(nameof(transformSize), "Linear pupil correlation requires at least double padding.");
        return (Axis(true), Axis(false));

        Complex[] Axis(bool vertical)
        {
            var power = new Complex[transformSize];
            var buffer = new Complex[transformSize];
            for (var line = 0; line < size; line++)
            {
                ComputationCancellation.ThrowIfCancellationRequested();
                Array.Clear(buffer);
                var illuminated = false;
                for (var i = 0; i < size; i++)
                {
                    var value = vertical ? pupil[i, line] : pupil[line, i];
                    buffer[i] = value; illuminated |= value != Complex.Zero;
                }
                if (!illuminated) continue;
                Fft(buffer);
                for (var i = 0; i < transformSize; i++)
                    power[i] += new Complex(buffer[i].Real * buffer[i].Real + buffer[i].Imaginary * buffer[i].Imaginary, 0);
            }
            // The FFT of each line's Fourier power is its linear correlation.
            // Summing independent lines gives the full two-dimensional pupil's
            // axis correlations, including every illuminated and obscured cell.
            Fft(power);
            var dc = power[0].Magnitude <= 1e-30 ? Complex.One : power[0];
            return power.Take(transformSize / 2).Select(value => value / dc).ToArray();
        }
    }

    internal static void ValidateCroppedFft(int pupilSize, int transformSize, int outputSize)
    {
        AnalysisResourceLimits.ValidateFftGrid(pupilSize, AnalysisResourceLimits.RoundUpPowerOfTwo(pupilSize, nameof(pupilSize)));
        if (transformSize < pupilSize || transformSize > 2 * AnalysisResourceLimits.MaximumFftGridSize
            || (transformSize & (transformSize - 1)) != 0 || outputSize < 2 || outputSize > transformSize
            || (long)pupilSize * pupilSize + (long)pupilSize * outputSize + (long)outputSize * outputSize
                > AnalysisResourceLimits.MaximumAggregateGridCells)
            throw new ArgumentOutOfRangeException(nameof(transformSize), "Cropped Fourier transform exceeds its bounded source, intermediate or output allocation.");
    }

    // A separable transform evaluates only the requested physical frequencies.
    // The largest 2-D arrays are the original pupil and pupilSize*outputSize;
    // the zero-padded transformSize² array is never allocated. This is the same
    // discrete Fourier transform, without a cropped-window renormalization.
    internal static Complex[,] FourierTransformCropped(Complex[,] pupil, int transformSize, int outputSize, bool nativeCenteredOutput)
    {
        var pupilSize = pupil.GetLength(0);
        if (pupil.GetLength(1) != pupilSize) throw new ArgumentException("A square pupil is required", nameof(pupil));
        ValidateCroppedFft(pupilSize, transformSize, outputSize);
        var center = nativeCenteredOutput ? (outputSize - 1) / 2 : outputSize / 2;
        var frequencies = Enumerable.Range(0, outputSize).Select(i => (i - center + transformSize) % transformSize).ToArray();
        var offset = (transformSize - pupilSize) / 2;
        var intermediate = new Complex[pupilSize, outputSize];
        var buffer = new Complex[transformSize];
        for (var row = 0; row < pupilSize; row++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            Array.Clear(buffer);
            var illuminated = false;
            for (var column = 0; column < pupilSize; column++)
            {
                buffer[offset + column] = pupil[row, column];
                illuminated |= pupil[row, column] != Complex.Zero;
            }
            if (!illuminated) continue;
            Fft(buffer);
            for (var column = 0; column < outputSize; column++) intermediate[row, column] = buffer[frequencies[column]];
        }
        var result = new Complex[outputSize, outputSize];
        for (var column = 0; column < outputSize; column++)
        {
            ComputationCancellation.ThrowIfCancellationRequested();
            Array.Clear(buffer);
            for (var row = 0; row < pupilSize; row++) buffer[offset + row] = intermediate[row, column];
            Fft(buffer);
            for (var row = 0; row < outputSize; row++) result[row, column] = buffer[frequencies[row]];
        }
        return result;
    }
}
