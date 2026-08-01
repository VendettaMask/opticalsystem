using System.Globalization;

namespace OptilandWorkbench.Core.Analysis;

public sealed record ExtendedSourceImage(int Width, int Height, IReadOnlyList<double> Values)
{
    public static ExtendedSourceImage ParseZemaxTextIma(string content)
    {
        var lines = content.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.None);
        if (lines.Length == 0
            || !int.TryParse(lines[0].Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var size)
            || size is < 1 or > 8000)
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

        return new ExtendedSourceImage(size, size, values);
    }

    public double Value(int row, int column)
    {
        if (Width < 1 || Height < 1 || Values.Count != Width * Height)
        {
            throw new InvalidDataException("Extended-source image dimensions are inconsistent.");
        }

        return Values[(row * Width) + column];
    }
}
