using OptilandWorkbench.Core.Domain;

namespace OptilandWorkbench.Core.Analysis;

public enum ImageSimulationSourcePattern
{
    ColorChart,
    ResolutionTarget,
    DistortionGrid,
    SiemensStar
}

public sealed class ImageSimulationConfig
{
    public ImageSimulationSourcePattern SourcePattern { get; init; } = ImageSimulationSourcePattern.ColorChart;

    public IReadOnlyList<double> WavelengthsMicrometers { get; init; } = new[] { 0.65, 0.55, 0.45 };

    public int PsfGridRows { get; init; } = 5;

    public int PsfGridColumns { get; init; } = 5;

    public int PsfSize { get; init; } = 128;

    public int NumRays { get; init; } = 64;

    public int Components { get; init; } = 3;

    public int Padding { get; init; } = 64;

    public int DistortionGridSize { get; init; } = 25;

    public int DistortionPolynomialDegree { get; init; } = 5;
}

public sealed record RgbImage(double[,,] Values)
{
    public int Channels => Values.GetLength(0);

    public int Height => Values.GetLength(1);

    public int Width => Values.GetLength(2);

    public double this[int channel, int row, int column] => Values[channel, row, column];
}

public sealed record PsfBasisResult(
    double[][,] EigenPsfs,
    double[,,] CoefficientGrid,
    double[,] MeanPsf,
    int GridRows,
    int GridColumns);

public sealed record ImageSimulationResult(
    RgbImage Source,
    RgbImage Simulated,
    ImageSimulationConfig Config,
    double MeanAbsoluteChange,
    double MaximumValue);

public static class ImageSimulationEngine
{
    public static RgbImage CreateSourceImage(
        ImageSimulationSourcePattern pattern,
        int width = 96,
        int height = 64)
    {
        return pattern switch
        {
            ImageSimulationSourcePattern.ResolutionTarget => CreateResolutionTarget(width, height),
            ImageSimulationSourcePattern.DistortionGrid => CreateDistortionGrid(width, height),
            ImageSimulationSourcePattern.SiemensStar => CreateSiemensStar(width, height),
            _ => CreateTestChart(width, height)
        };
    }

    public static ImageSimulationResult Simulate(Optic optic, RgbImage source, ImageSimulationConfig? config = null)
    {
        config ??= new ImageSimulationConfig();
        if (source.Channels is not (1 or 3))
        {
            throw new ArgumentException("Source image must contain one or three channels.", nameof(source));
        }

        var padded = ReflectPad(source.Values, Math.Max(0, config.Padding));
        var output = new double[config.WavelengthsMicrometers.Count, padded.GetLength(1), padded.GetLength(2)];
        for (var channel = 0; channel < config.WavelengthsMicrometers.Count; channel++)
        {
            var wavelength = ResolveWavelength(optic, config.WavelengthsMicrometers[channel]);
            var basis = GenerateBasis(optic, wavelength, config);
            var coefficientMaps = ResizeCoefficientMaps(
                basis.CoefficientGrid,
                padded.GetLength(1),
                padded.GetLength(2));
            var sourceChannel = SliceChannel(padded, Math.Min(channel, source.Channels - 1));
            var blurred = SpatiallyVariableConvolution(
                sourceChannel,
                basis.EigenPsfs,
                coefficientMaps,
                basis.MeanPsf);
            var distortionGrid = GenerateDistortionGrid(
                optic,
                wavelength,
                blurred.GetLength(0),
                blurred.GetLength(1),
                config.DistortionGridSize,
                config.DistortionPolynomialDegree);
            var warped = WarpImage(blurred, distortionGrid);
            CopyChannel(output, channel, warped);
        }

        var cropped = Crop(output, config.Padding, source.Height, source.Width);
        var maximum = 0.0;
        var totalChange = 0.0;
        var count = 0;
        for (var channel = 0; channel < cropped.GetLength(0); channel++)
        {
            for (var row = 0; row < cropped.GetLength(1); row++)
            {
                for (var column = 0; column < cropped.GetLength(2); column++)
                {
                    cropped[channel, row, column] = Math.Max(0, cropped[channel, row, column]);
                    maximum = Math.Max(maximum, cropped[channel, row, column]);
                    var sourceValue = source.Values[Math.Min(channel, source.Channels - 1), row, column];
                    totalChange += Math.Abs(cropped[channel, row, column] - sourceValue);
                    count++;
                }
            }
        }

        return new ImageSimulationResult(source, new RgbImage(cropped), config, totalChange / Math.Max(1, count), maximum);
    }

    public static RgbImage CreateTestChart(int width = 96, int height = 64)
    {
        width = Math.Max(16, width);
        height = Math.Max(16, height);
        var values = new double[3, height, width];
        var patches = new[]
        {
            (0.85, 0.12, 0.10), (0.12, 0.62, 0.22), (0.10, 0.30, 0.88),
            (0.95, 0.72, 0.10), (0.72, 0.16, 0.78), (0.08, 0.72, 0.78)
        };
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                var checker = ((row / 4) + (column / 4)) % 2 == 0 ? 0.08 : 0.92;
                var color = row < height / 2
                    ? patches[Math.Min(patches.Length - 1, column * patches.Length / width)]
                    : (checker, checker, checker);
                values[0, row, column] = color.Item1;
                values[1, row, column] = color.Item2;
                values[2, row, column] = color.Item3;
            }
        }

        var centerX = width / 2;
        var centerY = height * 3 / 4;
        var radius = Math.Min(width, height) / 7.0;
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                var distance = Math.Sqrt(((column - centerX) * (column - centerX)) + ((row - centerY) * (row - centerY)));
                if (Math.Abs(distance - radius) <= 1.2 || Math.Abs(column - centerX) <= 1 || Math.Abs(row - centerY) <= 1)
                {
                    values[0, row, column] = 1;
                    values[1, row, column] = 1;
                    values[2, row, column] = 1;
                }
            }
        }

        return new RgbImage(values);
    }

    public static RgbImage CreateResolutionTarget(int width = 96, int height = 64)
    {
        width = Math.Max(16, width);
        height = Math.Max(16, height);
        var values = new double[3, height, width];
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                var band = Math.Min(3, column * 4 / width);
                var period = Math.Max(2, 8 - (band * 2));
                var verticalBars = ((column / period) % 2) == 0;
                var horizontalBars = ((row / period) % 2) == 0;
                var value = row < height / 2
                    ? verticalBars ? 0.06 : 0.94
                    : horizontalBars ? 0.06 : 0.94;
                if (Math.Abs(column - (width / 2)) <= 1 || Math.Abs(row - (height / 2)) <= 1)
                {
                    value = 0.5;
                }

                SetRgb(values, row, column, value, value, value);
            }
        }

        return new RgbImage(values);
    }

    public static RgbImage CreateDistortionGrid(int width = 96, int height = 64)
    {
        width = Math.Max(16, width);
        height = Math.Max(16, height);
        var values = new double[3, height, width];
        var spacing = Math.Max(4, Math.Min(width, height) / 8);
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                var onGrid = row % spacing <= 1 || column % spacing <= 1;
                var value = onGrid ? 0.05 : 0.96;
                var centerLine = Math.Abs(column - (width / 2)) <= 1 || Math.Abs(row - (height / 2)) <= 1;
                SetRgb(
                    values,
                    row,
                    column,
                    centerLine ? 0.82 : value,
                    centerLine ? 0.12 : value,
                    centerLine ? 0.10 : value);
            }
        }

        return new RgbImage(values);
    }

    public static RgbImage CreateSiemensStar(int width = 96, int height = 64)
    {
        width = Math.Max(16, width);
        height = Math.Max(16, height);
        var values = new double[3, height, width];
        var centerX = (width - 1) / 2.0;
        var centerY = (height - 1) / 2.0;
        var radius = Math.Min(width, height) * 0.44;
        const int sectorPairs = 18;
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                var dx = column - centerX;
                var dy = row - centerY;
                var distance = Math.Sqrt((dx * dx) + (dy * dy));
                var angle = Math.Atan2(dy, dx);
                var value = distance <= radius
                    ? Math.Sin(angle * sectorPairs) >= 0 ? 0.04 : 0.96
                    : 0.82;
                if (Math.Abs(distance - radius) <= 1)
                {
                    value = 0.35;
                }

                SetRgb(values, row, column, value, value, value);
            }
        }

        return new RgbImage(values);
    }

    private static void SetRgb(double[,,] values, int row, int column, double red, double green, double blue)
    {
        values[0, row, column] = red;
        values[1, row, column] = green;
        values[2, row, column] = blue;
    }

    public static PsfBasisResult GenerateBasis(Optic optic, Wavelength wavelength, ImageSimulationConfig config)
    {
        var rows = Math.Max(2, config.PsfGridRows);
        var columns = Math.Max(2, config.PsfGridColumns);
        var psfCount = rows * columns;
        var featureCount = config.PsfSize * config.PsfSize;
        var psfs = new double[psfCount, featureCount];
        var sample = 0;
        for (var row = 0; row < rows; row++)
        {
            var hy = -1 + (2.0 * row / (rows - 1));
            for (var column = 0; column < columns; column++)
            {
                var hx = -1 + (2.0 * column / (columns - 1));
                var psf = DiffractionEngine.ComputeFftPsf(optic, (hx, hy), wavelength, config.NumRays, config.PsfSize);
                var sum = psf.Values.Cast<double>().Sum();
                for (var y = 0; y < config.PsfSize; y++)
                {
                    for (var x = 0; x < config.PsfSize; x++)
                    {
                        psfs[sample, (y * config.PsfSize) + x] = psf.Values[y, x] / Math.Max(1e-30, sum);
                    }
                }

                sample++;
            }
        }

        var mean = new double[featureCount];
        for (var feature = 0; feature < featureCount; feature++)
        {
            for (sample = 0; sample < psfCount; sample++)
            {
                mean[feature] += psfs[sample, feature] / psfCount;
            }

            for (sample = 0; sample < psfCount; sample++)
            {
                psfs[sample, feature] -= mean[feature];
            }
        }

        var gram = new double[psfCount, psfCount];
        for (var left = 0; left < psfCount; left++)
        {
            for (var right = left; right < psfCount; right++)
            {
                var value = 0.0;
                for (var feature = 0; feature < featureCount; feature++)
                {
                    value += psfs[left, feature] * psfs[right, feature];
                }

                gram[left, right] = value;
                gram[right, left] = value;
            }
        }

        var eigen = SymmetricEigen(gram);
        var componentCount = Math.Min(Math.Max(1, config.Components), psfCount);
        var eigenPsfs = new double[componentCount][,];
        var coefficients = new double[componentCount, rows, columns];
        for (var component = 0; component < componentCount; component++)
        {
            var singular = Math.Sqrt(Math.Max(0, eigen.Values[component]));
            var basis = new double[config.PsfSize, config.PsfSize];
            if (singular > 1e-15)
            {
                for (var feature = 0; feature < featureCount; feature++)
                {
                    var value = 0.0;
                    for (sample = 0; sample < psfCount; sample++)
                    {
                        value += psfs[sample, feature] * eigen.Vectors[sample, component];
                    }

                    basis[feature / config.PsfSize, feature % config.PsfSize] = value / singular;
                }
            }

            for (sample = 0; sample < psfCount; sample++)
            {
                coefficients[component, sample / columns, sample % columns] = eigen.Vectors[sample, component] * singular;
            }

            eigenPsfs[component] = basis;
        }

        var meanPsf = new double[config.PsfSize, config.PsfSize];
        for (var feature = 0; feature < featureCount; feature++)
        {
            meanPsf[feature / config.PsfSize, feature % config.PsfSize] = mean[feature];
        }

        return new PsfBasisResult(eigenPsfs, coefficients, meanPsf, rows, columns);
    }

    public static double[,] SpatiallyVariableConvolution(
        double[,] source,
        IReadOnlyList<double[,]> eigenPsfs,
        double[,,] coefficientMaps,
        double[,] meanPsf)
    {
        var output = ConvolveSame(source, meanPsf);
        for (var component = 0; component < eigenPsfs.Count; component++)
        {
            var weighted = new double[source.GetLength(0), source.GetLength(1)];
            for (var row = 0; row < source.GetLength(0); row++)
            {
                for (var column = 0; column < source.GetLength(1); column++)
                {
                    weighted[row, column] = source[row, column] * coefficientMaps[component, row, column];
                }
            }

            var convolved = ConvolveSame(weighted, eigenPsfs[component]);
            for (var row = 0; row < source.GetLength(0); row++)
            {
                for (var column = 0; column < source.GetLength(1); column++)
                {
                    output[row, column] += convolved[row, column];
                }
            }
        }

        return output;
    }

    public static (double X, double Y)[,] GenerateDistortionGrid(
        Optic optic,
        Wavelength wavelength,
        int height,
        int width,
        int numGridPoints = 25,
        int degree = 5)
    {
        optic = RealImageFieldConversion.ForImageSimulation(optic);
        numGridPoints = Math.Max(degree + 1, numGridPoints);
        var count = numGridPoints * numGridPoints;
        var realX = new double[count];
        var realY = new double[count];
        var fieldX = new double[count];
        var fieldY = new double[count];
        var index = 0;
        for (var row = 0; row < numGridPoints; row++)
        {
            var hy = -1 + (2.0 * row / (numGridPoints - 1));
            for (var column = 0; column < numGridPoints; column++)
            {
                var hx = -1 + (2.0 * column / (numGridPoints - 1));
                var final = optic.TraceGeneric(hx, hy, 0, 0, wavelength.Micrometers).RayHistories.Single()[^1];
                realX[index] = final.Position.X;
                realY[index] = final.Position.Y;
                fieldX[index] = hx;
                fieldY[index] = hy;
                index++;
            }
        }

        var center = optic.TraceGeneric(0, 0, 0, 0, wavelength.Micrometers).RayHistories.Single()[^1].Position;
        for (index = 0; index < count; index++)
        {
            realX[index] -= center.X;
            realY[index] -= center.Y;
        }

        var scaleX = realX.Select(Math.Abs).DefaultIfEmpty(1).Max();
        var scaleY = realY.Select(Math.Abs).DefaultIfEmpty(1).Max();
        scaleX = scaleX <= 0 ? 1 : scaleX;
        scaleY = scaleY <= 0 ? 1 : scaleY;
        var featureCount = (degree + 1) * (degree + 2) / 2;
        var design = new double[count, featureCount];
        for (index = 0; index < count; index++)
        {
            FillPolynomialFeatures(design, index, realX[index] / scaleX, realY[index] / scaleY, degree);
        }

        var coefficientX = LeastSquares(design, fieldX);
        var coefficientY = LeastSquares(design, fieldY);
        var minX = realX.Min();
        var maxX = realX.Max();
        var minY = realY.Min();
        var maxY = realY.Max();
        var grid = new (double X, double Y)[height, width];
        var features = new double[featureCount];
        for (var row = 0; row < height; row++)
        {
            var y = height == 1 ? maxY : maxY + ((minY - maxY) * row / (height - 1.0));
            for (var column = 0; column < width; column++)
            {
                var x = width == 1 ? minX : minX + ((maxX - minX) * column / (width - 1.0));
                FillPolynomialFeatures(features, x / scaleX, y / scaleY, degree);
                grid[row, column] = (Dot(features, coefficientX), -Dot(features, coefficientY));
            }
        }

        return grid;
    }

    public static double[,] WarpImage(double[,] source, (double X, double Y)[,] grid)
    {
        var height = grid.GetLength(0);
        var width = grid.GetLength(1);
        var output = new double[height, width];
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                var sourceX = ((grid[row, column].X + 1) * source.GetLength(1) - 1) / 2;
                var sourceY = ((grid[row, column].Y + 1) * source.GetLength(0) - 1) / 2;
                output[row, column] = BilinearWithZeroPadding(source, sourceY, sourceX);
            }
        }

        return output;
    }

    private static Wavelength ResolveWavelength(Optic optic, double micrometers)
    {
        return new Wavelength
        {
            Label = $"{micrometers:0.0000} um",
            Nanometers = micrometers * 1000,
            Weight = 1
        };
    }

    public static double[,,] ResizeCoefficientMaps(double[,,] source, int height, int width)
    {
        var output = new double[source.GetLength(0), height, width];
        for (var component = 0; component < source.GetLength(0); component++)
        {
            for (var row = 0; row < height; row++)
            {
                var y = height == 1 ? 0 : row * (source.GetLength(1) - 1.0) / (height - 1.0);
                for (var column = 0; column < width; column++)
                {
                    var x = width == 1 ? 0 : column * (source.GetLength(2) - 1.0) / (width - 1.0);
                    output[component, row, column] = Bilinear(source, component, y, x);
                }
            }
        }

        return output;
    }

    private static double[,] ConvolveSame(double[,] source, double[,] kernel)
    {
        var output = new double[source.GetLength(0), source.GetLength(1)];
        var startY = (kernel.GetLength(0) - 1) / 2;
        var startX = (kernel.GetLength(1) - 1) / 2;
        for (var row = 0; row < output.GetLength(0); row++)
        {
            for (var column = 0; column < output.GetLength(1); column++)
            {
                var sum = 0.0;
                for (var kernelY = 0; kernelY < kernel.GetLength(0); kernelY++)
                {
                    var sourceY = row + startY - kernelY;
                    if (sourceY < 0 || sourceY >= source.GetLength(0))
                    {
                        continue;
                    }

                    for (var kernelX = 0; kernelX < kernel.GetLength(1); kernelX++)
                    {
                        var sourceX = column + startX - kernelX;
                        if (sourceX >= 0 && sourceX < source.GetLength(1))
                        {
                            sum += source[sourceY, sourceX] * kernel[kernelY, kernelX];
                        }
                    }
                }

                output[row, column] = sum;
            }
        }

        return output;
    }

    public static double[,,] ReflectPad(double[,,] source, int padding)
    {
        var output = new double[source.GetLength(0), source.GetLength(1) + (2 * padding), source.GetLength(2) + (2 * padding)];
        for (var channel = 0; channel < output.GetLength(0); channel++)
        {
            for (var row = 0; row < output.GetLength(1); row++)
            {
                var sourceY = ReflectIndex(row - padding, source.GetLength(1));
                for (var column = 0; column < output.GetLength(2); column++)
                {
                    var sourceX = ReflectIndex(column - padding, source.GetLength(2));
                    output[channel, row, column] = source[channel, sourceY, sourceX];
                }
            }
        }

        return output;
    }

    private static int ReflectIndex(int index, int size)
    {
        if (size <= 1)
        {
            return 0;
        }

        var period = (2 * size) - 2;
        index %= period;
        if (index < 0)
        {
            index += period;
        }

        return index < size ? index : period - index;
    }

    private static double[,] SliceChannel(double[,,] source, int channel)
    {
        var output = new double[source.GetLength(1), source.GetLength(2)];
        for (var row = 0; row < output.GetLength(0); row++)
        {
            for (var column = 0; column < output.GetLength(1); column++)
            {
                output[row, column] = source[channel, row, column];
            }
        }

        return output;
    }

    private static void CopyChannel(double[,,] destination, int channel, double[,] source)
    {
        for (var row = 0; row < source.GetLength(0); row++)
        {
            for (var column = 0; column < source.GetLength(1); column++)
            {
                destination[channel, row, column] = source[row, column];
            }
        }
    }

    private static double[,,] Crop(double[,,] source, int padding, int height, int width)
    {
        var output = new double[source.GetLength(0), height, width];
        for (var channel = 0; channel < output.GetLength(0); channel++)
        {
            for (var row = 0; row < height; row++)
            {
                for (var column = 0; column < width; column++)
                {
                    output[channel, row, column] = source[channel, row + padding, column + padding];
                }
            }
        }

        return output;
    }

    private static double Bilinear(double[,,] source, int component, double y, double x)
    {
        var y0 = Math.Clamp((int)Math.Floor(y), 0, source.GetLength(1) - 1);
        var x0 = Math.Clamp((int)Math.Floor(x), 0, source.GetLength(2) - 1);
        var y1 = Math.Min(y0 + 1, source.GetLength(1) - 1);
        var x1 = Math.Min(x0 + 1, source.GetLength(2) - 1);
        var fy = y - y0;
        var fx = x - x0;
        return ((1 - fy) * (((1 - fx) * source[component, y0, x0]) + (fx * source[component, y0, x1])))
            + (fy * (((1 - fx) * source[component, y1, x0]) + (fx * source[component, y1, x1])));
    }

    private static double BilinearWithZeroPadding(double[,] source, double y, double x)
    {
        var y0 = (int)Math.Floor(y);
        var x0 = (int)Math.Floor(x);
        var fy = y - y0;
        var fx = x - x0;
        double Value(int row, int column) => row >= 0 && row < source.GetLength(0) && column >= 0 && column < source.GetLength(1)
            ? source[row, column]
            : 0;
        return ((1 - fy) * (((1 - fx) * Value(y0, x0)) + (fx * Value(y0, x0 + 1))))
            + (fy * (((1 - fx) * Value(y0 + 1, x0)) + (fx * Value(y0 + 1, x0 + 1))));
    }

    private static void FillPolynomialFeatures(double[,] destination, int row, double x, double y, int degree)
    {
        var feature = 0;
        for (var order = 0; order <= degree; order++)
        {
            for (var xPower = 0; xPower <= order; xPower++)
            {
                destination[row, feature++] = Math.Pow(x, xPower) * Math.Pow(y, order - xPower);
            }
        }
    }

    private static void FillPolynomialFeatures(double[] destination, double x, double y, int degree)
    {
        var feature = 0;
        for (var order = 0; order <= degree; order++)
        {
            for (var xPower = 0; xPower <= order; xPower++)
            {
                destination[feature++] = Math.Pow(x, xPower) * Math.Pow(y, order - xPower);
            }
        }
    }

    private static double[] LeastSquares(double[,] design, double[] values)
    {
        var rows = design.GetLength(0);
        var columns = design.GetLength(1);
        var q = new double[rows, columns];
        var r = new double[columns, columns];
        var vector = new double[rows];
        for (var column = 0; column < columns; column++)
        {
            for (var row = 0; row < rows; row++)
            {
                vector[row] = design[row, column];
            }

            for (var previous = 0; previous < column; previous++)
            {
                var projection = 0.0;
                for (var row = 0; row < rows; row++)
                {
                    projection += q[row, previous] * vector[row];
                }

                r[previous, column] = projection;
                for (var row = 0; row < rows; row++)
                {
                    vector[row] -= projection * q[row, previous];
                }
            }

            var norm = Math.Sqrt(vector.Sum(value => value * value));
            r[column, column] = norm;
            for (var row = 0; row < rows; row++)
            {
                q[row, column] = vector[row] / Math.Max(1e-30, norm);
            }
        }

        var projected = new double[columns];
        for (var column = 0; column < columns; column++)
        {
            for (var row = 0; row < rows; row++)
            {
                projected[column] += q[row, column] * values[row];
            }
        }

        var result = new double[columns];
        for (var row = columns - 1; row >= 0; row--)
        {
            var value = projected[row];
            for (var column = row + 1; column < columns; column++)
            {
                value -= r[row, column] * result[column];
            }

            result[row] = value / Math.Max(1e-30, r[row, row]);
        }

        return result;
    }

    private static (double[] Values, double[,] Vectors) SymmetricEigen(double[,] source)
    {
        var size = source.GetLength(0);
        var matrix = (double[,])source.Clone();
        var vectors = new double[size, size];
        for (var index = 0; index < size; index++)
        {
            vectors[index, index] = 1;
        }

        for (var iteration = 0; iteration < 100 * size * size; iteration++)
        {
            var p = 0;
            var q = 1;
            var maximum = 0.0;
            for (var row = 0; row < size; row++)
            {
                for (var column = row + 1; column < size; column++)
                {
                    if (Math.Abs(matrix[row, column]) > maximum)
                    {
                        maximum = Math.Abs(matrix[row, column]);
                        p = row;
                        q = column;
                    }
                }
            }

            if (maximum <= 1e-14)
            {
                break;
            }

            var angle = 0.5 * Math.Atan2(2 * matrix[p, q], matrix[q, q] - matrix[p, p]);
            var sine = Math.Sin(angle);
            var cosine = Math.Cos(angle);
            for (var index = 0; index < size; index++)
            {
                if (index != p && index != q)
                {
                    var left = matrix[index, p];
                    var right = matrix[index, q];
                    matrix[index, p] = matrix[p, index] = (cosine * left) - (sine * right);
                    matrix[index, q] = matrix[q, index] = (sine * left) + (cosine * right);
                }

                var vectorP = vectors[index, p];
                var vectorQ = vectors[index, q];
                vectors[index, p] = (cosine * vectorP) - (sine * vectorQ);
                vectors[index, q] = (sine * vectorP) + (cosine * vectorQ);
            }

            var app = matrix[p, p];
            var aqq = matrix[q, q];
            var apq = matrix[p, q];
            matrix[p, p] = (cosine * cosine * app) - (2 * sine * cosine * apq) + (sine * sine * aqq);
            matrix[q, q] = (sine * sine * app) + (2 * sine * cosine * apq) + (cosine * cosine * aqq);
            matrix[p, q] = matrix[q, p] = 0;
        }

        var order = Enumerable.Range(0, size).OrderByDescending(index => matrix[index, index]).ToArray();
        var values = order.Select(index => matrix[index, index]).ToArray();
        var sortedVectors = new double[size, size];
        for (var column = 0; column < size; column++)
        {
            for (var row = 0; row < size; row++)
            {
                sortedVectors[row, column] = vectors[row, order[column]];
            }
        }

        return (values, sortedVectors);
    }

    private static double Dot(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        var value = 0.0;
        for (var index = 0; index < left.Count; index++)
        {
            value += left[index] * right[index];
        }

        return value;
    }
}
