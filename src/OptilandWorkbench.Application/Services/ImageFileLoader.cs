using OptilandWorkbench.Core.Analysis;
using SkiaSharp;

namespace OptilandWorkbench.Application.Services;

internal static class ImageFileLoader
{
    private const int MaximumDimension = 16_000;

    internal static RgbImage LoadRgb(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("External bitmap mode requires an input image file.");
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The image simulation input bitmap does not exist.", fullPath);
        }

        using var stream = File.OpenRead(fullPath);
        using var bitmap = SKBitmap.Decode(stream)
            ?? throw new InvalidDataException("The image simulation input file is not a supported bitmap.");
        if (bitmap.Width is < 2 or > MaximumDimension
            || bitmap.Height is < 2 or > MaximumDimension)
        {
            throw new InvalidDataException(
                $"Image dimensions must be between 2 and {MaximumDimension} pixels.");
        }

        var values = new double[3, bitmap.Height, bitmap.Width];
        for (var row = 0; row < bitmap.Height; row++)
        {
            for (var column = 0; column < bitmap.Width; column++)
            {
                var color = bitmap.GetPixel(column, row);
                var alpha = color.Alpha / 255.0;
                values[0, row, column] = color.Red / 255.0 * alpha;
                values[1, row, column] = color.Green / 255.0 * alpha;
                values[2, row, column] = color.Blue / 255.0 * alpha;
            }
        }

        return new RgbImage(values);
    }
}
