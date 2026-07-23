using System.Numerics;
using OptilandWorkbench.Core.Backend;
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

    public double PeakStrehlRatio => Values.Cast<double>().DefaultIfEmpty(0).Max() / 100;
}

public sealed record MtfResult(
    IReadOnlyList<double> Frequency,
    IReadOnlyList<double> Tangential,
    IReadOnlyList<double> Sagittal,
    double CutoffFrequency);

public static class DiffractionEngine
{
    public static MtfResult LimitFrequency(MtfResult result, double? maximumFrequency)
    {
        if (!maximumFrequency.HasValue
            || !double.IsFinite(maximumFrequency.Value)
            || maximumFrequency.Value <= 0
            || result.Frequency.Count == 0
            || maximumFrequency.Value >= result.Frequency[^1])
        {
            return result;
        }

        var limit = maximumFrequency.Value;
        var frequency = new List<double>();
        var tangential = new List<double>();
        var sagittal = new List<double>();
        var index = 0;
        while (index < result.Frequency.Count && result.Frequency[index] <= limit)
        {
            frequency.Add(result.Frequency[index]);
            tangential.Add(result.Tangential[index]);
            sagittal.Add(result.Sagittal[index]);
            index++;
        }

        if (frequency.Count > 0
            && frequency[^1] < limit
            && index < result.Frequency.Count)
        {
            var leftFrequency = result.Frequency[index - 1];
            var rightFrequency = result.Frequency[index];
            var fraction = (limit - leftFrequency) / (rightFrequency - leftFrequency);
            frequency.Add(limit);
            tangential.Add(result.Tangential[index - 1]
                + ((result.Tangential[index] - result.Tangential[index - 1]) * fraction));
            sagittal.Add(result.Sagittal[index - 1]
                + ((result.Sagittal[index] - result.Sagittal[index - 1]) * fraction));
        }

        return new MtfResult(
            frequency,
            tangential,
            sagittal,
            result.CutoffFrequency);
    }

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

    public static PsfResult ComputeMmdftPsf(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        int numRays,
        int imageSize,
        double? pixelPitchMicrometers = null)
    {
        if (numRays < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(numRays), "MMDFT requires at least two pupil samples.");
        }

        if (imageSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(imageSize), "Image size must be positive.");
        }

        var wavefront = WavefrontEngine.GenerateChiefRayUniform(optic, field, wavelength, numRays);
        var pupil = CreateUniformPupil(wavefront, numRays);
        var fNumber = WorkingFNumber(optic, field, wavelength);
        var clearSize = numRays - 1;
        var sampleSpacing = pixelPitchMicrometers ?? wavelength.Micrometers * fNumber * clearSize / imageSize;
        var padSize = wavelength.Micrometers * fNumber * clearSize / sampleSpacing;
        if (imageSize > padSize)
        {
            throw new ArgumentException("Image size exceeds the MMDFT pad size for the requested pixel pitch.");
        }

        var imagePlane = new Complex[imageSize, imageSize];
        for (var imageRow = 0; imageRow < imageSize; imageRow++)
        {
            var imageY = imageRow - (imageSize / 2);
            for (var imageColumn = 0; imageColumn < imageSize; imageColumn++)
            {
                var imageX = imageColumn - (imageSize / 2);
                var sum = Complex.Zero;
                for (var pupilRow = 0; pupilRow < numRays; pupilRow++)
                {
                    var pupilY = pupilRow - (numRays / 2);
                    var left = Complex.FromPolarCoordinates(1, -2 * Math.PI * imageY * pupilY / padSize);
                    for (var pupilColumn = 0; pupilColumn < numRays; pupilColumn++)
                    {
                        var pupilX = pupilColumn - (numRays / 2);
                        var right = Complex.FromPolarCoordinates(1, -2 * Math.PI * pupilX * imageX / padSize);
                        sum += left * pupil[pupilRow, pupilColumn] * right;
                    }
                }

                imagePlane[imageRow, imageColumn] = sum;
            }
        }

        var normalization = Math.Max(1, pupil.Cast<Complex>().Count(value => value.Magnitude > 0));
        normalization *= normalization;
        var psf = new double[imageSize, imageSize];
        for (var row = 0; row < imageSize; row++)
        {
            for (var column = 0; column < imageSize; column++)
            {
                psf[row, column] = imagePlane[row, column].Magnitude * imagePlane[row, column].Magnitude * 100 / normalization;
            }
        }

        return new PsfResult(psf, numRays, imageSize, fNumber, sampleSpacing);
    }

    public static PsfResult ComputeHuygensPsf(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        int numRays,
        int imageSize,
        double pixelPitchMillimeters)
    {
        if (numRays < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(numRays), "Huygens PSF requires at least two pupil samples.");
        }

        if (imageSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(imageSize), "Image size must be positive.");
        }

        if (!double.IsFinite(pixelPitchMillimeters) || pixelPitchMillimeters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelPitchMillimeters), "Pixel pitch must be positive.");
        }

        var wavefront = WavefrontEngine.GenerateChiefRayUniform(optic, field, wavelength, numRays);
        var imageCoordinates = CreateHuygensImageCoordinates(optic, field, wavelength, imageSize, pixelPitchMillimeters);
        var raw = HuygensSummation(imageCoordinates, wavefront, wavelength, idealOpd: false);
        var normalizationWavefront = field.Hx == 0 && field.Hy == 0
            ? wavefront
            : WavefrontEngine.GenerateChiefRayUniform(optic, (0, 0), wavelength, numRays);
        var imageZ = optic.SurfaceGroup.Items.LastOrDefault()?.CoordinateSystem.Origin.Z ?? 0;
        var normalizationPoint = new[,]
        {
            { new Vector3D(0, 0, imageZ) }
        };
        var normalization = HuygensSummation(normalizationPoint, normalizationWavefront, wavelength, idealOpd: true)[0, 0];
        normalization = Math.Max(1e-300, normalization);

        var psf = new double[imageSize, imageSize];
        for (var row = 0; row < imageSize; row++)
        {
            for (var column = 0; column < imageSize; column++)
            {
                psf[row, column] = raw[row, column] / normalization * 100;
            }
        }

        return new PsfResult(
            psf,
            numRays,
            imageSize,
            WorkingFNumber(optic, field, wavelength),
            pixelPitchMillimeters * 1000.0);
    }

    public static MtfResult ComputePsfMtf(PsfResult psf)
    {
        var complex = new Complex[psf.GridSize, psf.GridSize];
        for (var row = 0; row < psf.GridSize; row++)
        {
            for (var column = 0; column < psf.GridSize; column++)
            {
                complex[row, column] = new Complex(psf.Values[row, column], 0);
            }
        }

        Transform2D(complex);
        var shifted = FftShift(complex);
        var center = psf.GridSize / 2;
        var count = psf.GridSize / 2;
        var dc = Math.Max(1e-300, shifted[center, center].Magnitude);
        var tangential = Enumerable.Range(0, count)
            .Select(index => Math.Clamp(shifted[center + index, center].Magnitude / dc, 0, 1))
            .ToArray();
        var sagittal = Enumerable.Range(0, count)
            .Select(index => Math.Clamp(shifted[center, center + index].Magnitude / dc, 0, 1))
            .ToArray();
        var frequencyStep = 1000.0 / (psf.GridSize * psf.SampleSpacingMicrometers);
        var frequency = Enumerable.Range(0, count).Select(index => index * frequencyStep).ToArray();
        var cutoff = count == 0 ? 0 : frequency[^1];
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
        var imageIndex = optic.SurfaceGroup.Items[^1].MaterialAfter.RefractiveIndex(wavelength.Nanometers);
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

    private static void Transform2D(Complex[,] data)
    {
        var size = data.GetLength(0);
        if (IsPowerOfTwo(size))
        {
            Fft2D(data);
            return;
        }

        var transformed = new Complex[size, size];
        for (var u = 0; u < size; u++)
        {
            for (var v = 0; v < size; v++)
            {
                var sum = Complex.Zero;
                for (var row = 0; row < size; row++)
                {
                    for (var column = 0; column < size; column++)
                    {
                        var angle = -2 * Math.PI * ((u * row / (double)size) + (v * column / (double)size));
                        sum += data[row, column] * Complex.FromPolarCoordinates(1, angle);
                    }
                }

                transformed[u, v] = sum;
            }
        }

        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
            {
                data[row, column] = transformed[row, column];
            }
        }
    }

    private static Complex[,] CreateUniformPupil(WavefrontResult wavefront, int numRays)
    {
        var pupil = new Complex[numRays, numRays];
        var valid = wavefront.Samples.Where(sample => sample.Intensity > 0).ToArray();
        var meanIntensity = valid.Select(sample => sample.Intensity).DefaultIfEmpty(0).Average();
        foreach (var sample in wavefront.Samples)
        {
            var column = (int)Math.Round((sample.NormalizedPupilX + 1) / 2 * (numRays - 1));
            var row = (int)Math.Round((sample.NormalizedPupilY + 1) / 2 * (numRays - 1));
            var amplitude = meanIntensity <= 1e-30 ? 0 : sample.Intensity / meanIntensity;
            var phase = -2 * Math.PI * sample.OpdWaves;
            pupil[row, column] = Complex.FromPolarCoordinates(amplitude, phase);
        }

        return pupil;
    }

    private static Vector3D[,] CreateHuygensImageCoordinates(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength,
        int imageSize,
        double pixelPitchMillimeters)
    {
        var imageSurface = optic.SurfaceGroup.Items[^1];
        var (centerX, centerY) = HuygensImageCenter(optic, field, wavelength);
        var extent = 0.5 * imageSize * pixelPitchMillimeters;
        var coordinates = new Vector3D[imageSize, imageSize];
        for (var row = 0; row < imageSize; row++)
        {
            var y = Linspace(-extent + centerY, extent + centerY, imageSize, row);
            for (var column = 0; column < imageSize; column++)
            {
                var x = Linspace(-extent + centerX, extent + centerX, imageSize, column);
                var z = imageSurface.Geometry.Sag(x, y);
                coordinates[row, column] = imageSurface.CoordinateSystem.ToGlobalPoint(new Vector3D(x, y, z));
            }
        }

        return coordinates;
    }

    private static (double X, double Y) HuygensImageCenter(
        Optic optic,
        (double Hx, double Hy) field,
        Wavelength wavelength)
    {
        var imageSurface = optic.SurfaceGroup.Items[^1];
        var pupilSamples = SpotAnalysisEngine.CreatePupilSamples(6, "hexapolar");
        var bundle = optic.SequentialRayTracer.RayGenerator.GenerateNormalizedPupilSamples(
            field.Hx,
            field.Hy,
            wavelength.Micrometers,
            pupilSamples);
        var trace = optic.SequentialRayTracer.Trace(bundle);
        var local = trace.RayHistories
            .Where(history => history.Count > 0)
            .Select(history => history[^1])
            .Where(sample => sample.Intensity > 0)
            .Select(sample => imageSurface.CoordinateSystem.ToLocalPoint(sample.Position))
            .ToArray();
        return local.Length == 0
            ? (0, 0)
            : (local.Average(point => point.X), local.Average(point => point.Y));
    }

    private static double[,] HuygensSummation(
        Vector3D[,] imageCoordinates,
        WavefrontResult wavefront,
        Wavelength wavelength,
        bool idealOpd)
    {
        var rows = imageCoordinates.GetLength(0);
        var columns = imageCoordinates.GetLength(1);
        var psf = new double[rows, columns];
        var wavelengthMillimeters = wavelength.Micrometers * 1e-3;
        var k = 2 * Math.PI / wavelengthMillimeters;
        var radius = wavefront.Radius;
        foreach (var row in Enumerable.Range(0, rows))
        {
            for (var column = 0; column < columns; column++)
            {
                var image = imageCoordinates[row, column];
                var field = Complex.Zero;
                foreach (var sample in wavefront.Samples)
                {
                    var pupil = new Vector3D(sample.PupilX, sample.PupilY, sample.PupilZ);
                    var delta = image - pupil;
                    var distance = delta.Length;
                    if (distance <= 1e-300)
                    {
                        continue;
                    }

                    var wave = Complex.FromPolarCoordinates(1 / distance, k * distance);
                    var obliquity = 0.5 * (1 + (Dot(delta, pupil / radius) / distance));
                    var opd = idealOpd ? 0 : sample.OpdWaves * wavelengthMillimeters;
                    var pupilPhase = Complex.FromPolarCoordinates(1, -k * opd);
                    field += sample.Intensity * pupilPhase * wave * obliquity;
                }

                psf[row, column] = field.Magnitude * field.Magnitude;
            }
        }

        return psf;
    }

    private static double Linspace(double start, double end, int count, int index)
    {
        return count == 1 ? start : start + ((end - start) * index / (count - 1));
    }

    private static double Dot(Vector3D left, Vector3D right)
    {
        return (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
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
        var half = (size + 1) / 2;
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
