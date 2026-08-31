using System.Globalization;

namespace OptilandWorkbench.Core.Analysis;

public sealed record ExtendedSourceImage
{
    public const int MaximumDimension = 8_000;
    public const long MaximumPixelCount = 64_000_000;

    public ExtendedSourceImage(int width, int height, IReadOnlyList<double> values)
        : this(width, height, values, copyValues: true, valuesAlreadyValidated: false)
    {
    }

    private ExtendedSourceImage(
        int width,
        int height,
        IReadOnlyList<double> values,
        bool copyValues,
        bool valuesAlreadyValidated)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (width is < 1 or > MaximumDimension)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }
        if (height is < 1 or > MaximumDimension)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        var pixelCount = checked((long)width * height);
        if (pixelCount > MaximumPixelCount || values.Count != pixelCount)
        {
            throw new ArgumentException("Extended-source image dimensions are inconsistent.", nameof(values));
        }
        if (!valuesAlreadyValidated && values.Any(value => !double.IsFinite(value) || value < 0))
        {
            throw new ArgumentException("Extended-source image intensities must be finite and non-negative.", nameof(values));
        }

        Width = width;
        Height = height;
        Values = copyValues ? Array.AsReadOnly(values.ToArray()) : values;
    }

    public int Width { get; }

    public int Height { get; }

    public IReadOnlyList<double> Values { get; }

    public static ExtendedSourceImage ParseZemaxTextIma(string content)
    {
        var lines = content.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.None);
        if (lines.Length == 0
            || !int.TryParse(lines[0].Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var size)
            || size is < 1 or > MaximumDimension)
        {
            throw new InvalidDataException("Text IMA is missing a valid image size.");
        }

        var rows = lines.Skip(1).Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(size).ToArray();
        if (rows.Length != size)
        {
            throw new InvalidDataException("Text IMA does not contain the declared pixel rows.");
        }

        var values = new double[checked(size * size)];
        for (var row = 0; row < size; row++)
        {
            var pixels = new string(rows[row].Where(character => !char.IsWhiteSpace(character)).ToArray());
            if (pixels.Length != size || pixels.Any(character => character is < '0' or > '9'))
            {
                throw new InvalidDataException(
                    $"Text IMA row {row + 1} must contain exactly {size} intensity digits.");
            }

            for (var column = 0; column < size; column++)
            {
                values[(row * size) + column] = pixels[column] - '0';
            }
        }

        return new ExtendedSourceImage(
            size,
            size,
            Array.AsReadOnly(values),
            copyValues: false,
            valuesAlreadyValidated: true);
    }

    public double Value(int row, int column)
    {
        if ((uint)row >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }
        if ((uint)column >= (uint)Width)
        {
            throw new ArgumentOutOfRangeException(nameof(column));
        }

        return Values[checked((row * Width) + column)];
    }

    public void Deconstruct(out int width, out int height, out IReadOnlyList<double> values)
    {
        width = Width;
        height = Height;
        values = Values;
    }
}
