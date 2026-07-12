using System.Numerics;
using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Analysis;

public sealed record PsfResult(
    double[,] Values,
    int PupilSampling,
    int GridSize,
    double WorkingFNumber,
    double SampleSpacingMicrometers)
{
    public double StrehlRatio => Values[GridSize / 2, GridSize / 2] / 100;
}

public sealed record MtfResult(
    IReadOnlyList<double> Frequency,
    IReadOnlyList<double> Tangential,
    IReadOnlyList<double> Sagittal,
    double CutoffFrequency);

public static class DiffractionEngine
{
    public static PsfResult ComputeFftPsf(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        int pupilSampling,
        int gridSize)
    {
        if (gridSize < pupilSampling || !IsPowerOfTwo(gridSize))
        {
            throw new ArgumentException("FFT grid size must be a power of two and at least as large as the pupil sampling.");
        }

        var wavefront = WavefrontEngine.GenerateChiefRayUniform(optic, field, wavelength, pupilSampling);
        var pupil = new Complex[pupilSampling, pupilSampling];
        var valid = wavefront.Samples.Where(sample => sample.Intensity > 0).ToArray();
        var meanIntensity = valid.Select(sample => sample.Intensity).DefaultIfEmpty(0).Average();
        foreach (var sample in wavefront.Samples)
        {
            var column = (int)Math.Round((sample.NormalizedPupilX + 1) / 2 * (pupilSampling - 1));
            var row = (int)Math.Round((sample.NormalizedPupilY + 1) / 2 * (pupilSampling - 1));
            var amplitude = meanIntensity <= 1e-30 ? 0 : sample.Intensity / meanIntensity;
            var phase = -2 * Math.PI * sample.OpdWaves;
            pupil[row, column] = Complex.FromPolarCoordinates(amplitude, phase);
        }

        var nonzeroCount = pupil.Cast<Complex>().Count(value => value.Magnitude > 0);
        var normalization = Math.Max(1, nonzeroCount * nonzeroCount);
        var padded = new Complex[gridSize, gridSize];
        var offset = (gridSize - pupilSampling) / 2;
        for (var row = 0; row < pupilSampling; row++)
        {
            for (var column = 0; column < pupilSampling; column++)
            {
                padded[row + offset, column + offset] = pupil[row, column];
            }
        }

        Fft2D(padded);
        var shifted = FftShift(padded);
        var psf = new double[gridSize, gridSize];
        for (var row = 0; row < gridSize; row++)
        {
            for (var column = 0; column < gridSize; column++)
            {
                psf[row, column] = shifted[row, column].Magnitude * shifted[row, column].Magnitude / normalization * 100;
            }
        }

        var fNumber = WorkingFNumber(optic, field, wavelength);
        var q = gridSize / (double)(pupilSampling - 1);
        var sampleSpacing = wavelength.Micrometers * fNumber / q;
        return new PsfResult(psf, pupilSampling, gridSize, fNumber, sampleSpacing);
    }

    public static MtfResult ComputeFftMtf(
        PsfResult psf,
        Optic optic,
        Wavelength wavelength)
    {
        var complex = new Complex[psf.GridSize, psf.GridSize];
        for (var row = 0; row < psf.GridSize; row++)
        {
            for (var column = 0; column < psf.GridSize; column++)
            {
                complex[row, column] = new Complex(psf.Values[row, column], 0);
            }
        }

        Fft2D(complex);
        var shifted = FftShift(complex);
        var center = psf.GridSize / 2;
        var tangential = Enumerable.Range(center, psf.GridSize - center)
            .Select(row => shifted[row, center].Magnitude)
            .ToArray();
        var sagittal = Enumerable.Range(center, psf.GridSize - center)
            .Select(column => shifted[center, column].Magnitude)
            .ToArray();
        var tangentMaximum = tangential.DefaultIfEmpty(1).Max();
        var sagittalMaximum = sagittal.DefaultIfEmpty(1).Max();
        tangential = tangential.Select(value => value / Math.Max(1e-30, tangentMaximum)).ToArray();
        sagittal = sagittal.Select(value => value / Math.Max(1e-30, sagittalMaximum)).ToArray();
        var frequencyFNumber = WorkingFNumber(optic, (0, 0), wavelength);
        var frequencyStep = 1 / ((psf.PupilSampling - 1) * wavelength.Micrometers * 1e-3 * frequencyFNumber);
        var frequency = Enumerable.Range(0, tangential.Length).Select(index => index * frequencyStep).ToArray();
        var cutoff = 1 / (wavelength.Micrometers * 1e-3 * frequencyFNumber);
        return new MtfResult(frequency, tangential, sagittal, cutoff);
    }

    public static double WorkingFNumber(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength)
    {
        var pupil = new[] { (0.0, 0.0), (0.0, 1.0), (0.0, -1.0), (1.0, 0.0), (-1.0, 0.0) };
        var directions = pupil.Select(item =>
        {
            var history = optic.TraceGeneric(field.Hx, field.Hy, item.Item1, item.Item2, wavelength.Micrometers).RayHistories.Single();
            return history[^1].Direction;
        }).ToArray();
        var chief = directions[0];
        var imageIndex = optic.Materials.Resolve(optic.SurfaceGroup.Items[^1].MaterialAfterName).RefractiveIndex(wavelength.Nanometers);
        var averageNaSquared = directions.Skip(1).Average(direction =>
        {
            var dot = Math.Clamp(
                (chief.X * direction.X) + (chief.Y * direction.Y) + (chief.Z * direction.Z),
                -1,
                1);
            var angle = Math.Acos(dot);
            var numericalAperture = imageIndex * Math.Sin(angle);
            return numericalAperture * numericalAperture;
        });
        return averageNaSquared <= 0 ? 10000 : Math.Min(10000, 1 / (2 * Math.Sqrt(averageNaSquared)));
    }

    private static void Fft2D(Complex[,] data)
    {
        var size = data.GetLength(0);
        var buffer = new Complex[size];
        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
            {
                buffer[column] = data[row, column];
            }

            Fft(buffer);
            for (var column = 0; column < size; column++)
            {
                data[row, column] = buffer[column];
            }
        }

        for (var column = 0; column < size; column++)
        {
            for (var row = 0; row < size; row++)
            {
                buffer[row] = data[row, column];
            }

            Fft(buffer);
            for (var row = 0; row < size; row++)
            {
                data[row, column] = buffer[row];
            }
        }
    }

    private static void Fft(Complex[] values)
    {
        var count = values.Length;
        for (int index = 1, reversed = 0; index < count; index++)
        {
            var bit = count >> 1;
            for (; (reversed & bit) != 0; bit >>= 1)
            {
                reversed ^= bit;
            }

            reversed ^= bit;
            if (index < reversed)
            {
                (values[index], values[reversed]) = (values[reversed], values[index]);
            }
        }

        for (var length = 2; length <= count; length <<= 1)
        {
            var root = Complex.FromPolarCoordinates(1, -2 * Math.PI / length);
            for (var start = 0; start < count; start += length)
            {
                var factor = Complex.One;
                for (var offset = 0; offset < length / 2; offset++)
                {
                    var even = values[start + offset];
                    var odd = values[start + offset + (length / 2)] * factor;
                    values[start + offset] = even + odd;
                    values[start + offset + (length / 2)] = even - odd;
                    factor *= root;
                }
            }
        }
    }

    private static Complex[,] FftShift(Complex[,] source)
    {
        var size = source.GetLength(0);
        var shifted = new Complex[size, size];
        var half = size / 2;
        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
            {
                shifted[row, column] = source[(row + half) % size, (column + half) % size];
            }
        }

        return shifted;
    }

    private static bool IsPowerOfTwo(int value)
    {
        return value > 0 && (value & (value - 1)) == 0;
    }
}
