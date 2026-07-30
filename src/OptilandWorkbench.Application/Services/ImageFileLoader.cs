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

    internal static void SaveAnalysisRaster(AnalysisData data, string path)
    {
        var series = data.PlotPanes?
            .SelectMany(pane => pane.Series)
            .FirstOrDefault(item => item.Kind == AnalysisSeriesKind.Raster)
            ?? data.PlotSeries.FirstOrDefault(item => item.Kind == AnalysisSeriesKind.Raster)
            ?? throw new InvalidOperationException("Image simulation did not produce a raster result.");
        var width = Math.Max(1, (int)Math.Round(series.Points.Select(point => point.X).DefaultIfEmpty(0).Max()) + 1);
        var height = Math.Max(1, (int)Math.Round(series.Points.Select(point => point.Y).DefaultIfEmpty(0).Max()) + 1);
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        foreach (var point in series.Points)
        {
            var column = Math.Clamp((int)Math.Round(point.X), 0, width - 1);
            var row = Math.Clamp(height - 1 - (int)Math.Round(point.Y), 0, height - 1);
            bitmap.SetPixel(column, row, new SKColor(
                Channel(point.Red),
                Channel(point.Green),
                Channel(point.Blue),
                255));
        }

        var fullPath = Path.GetFullPath(path);
        var format = Path.GetExtension(fullPath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
            ".bmp" => SKEncodedImageFormat.Bmp,
            _ => SKEncodedImageFormat.Png
        };
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(format, 95)
            ?? throw new InvalidOperationException("Unable to encode the image simulation output.");
        using var stream = File.Create(fullPath);
        encoded.SaveTo(stream);

        static byte Channel(double? value) => (byte)Math.Round(
            255 * Math.Clamp(value ?? 0, 0, 1));
    }
}
