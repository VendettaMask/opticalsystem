using OptilandWorkbench.Core.Analysis;
using OptilandWorkbench.Core.FileIO;
using SkiaSharp;

namespace OptilandWorkbench.Application.Services;

internal static class ImageFileLoader
{
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

        using var stream = BoundedFile.OpenRead(
            fullPath,
            BoundedFile.MaximumImageDataBytes,
            "Image-simulation bitmap");
        using (var codec = SKCodec.Create(stream)
            ?? throw new InvalidDataException("The image simulation input file is not a supported bitmap."))
        {
            AnalysisResourceLimits.ValidateImageDimensions(codec.Info.Width, codec.Info.Height, "Input bitmap");
        }

        stream.Position = 0;
        using var bitmap = SKBitmap.Decode(stream)
            ?? throw new InvalidDataException("The image simulation input file is not a supported bitmap.");
        if (bitmap.Width < 2 || bitmap.Height < 2)
        {
            throw new InvalidDataException("Image dimensions must be at least 2 by 2 pixels.");
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
        if (series.Points.Count > AnalysisResourceLimits.MaximumImagePixels
            || series.Points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
        {
            throw new InvalidDataException("Image simulation raster coordinates exceed the supported range.");
        }
        var width = Math.Max(1, (int)Math.Round(series.Points.Select(point => point.X).DefaultIfEmpty(0).Max()) + 1);
        var height = Math.Max(1, (int)Math.Round(series.Points.Select(point => point.Y).DefaultIfEmpty(0).Max()) + 1);
        AnalysisResourceLimits.ValidateImageDimensions(width, height, "Analysis raster");
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
        BoundedFile.WriteAllBytesAtomic(
            fullPath,
            encoded.ToArray(),
            BoundedFile.MaximumImageDataBytes,
            "Analysis raster");

        static byte Channel(double? value) => (byte)Math.Round(
            255 * Math.Clamp(value ?? 0, 0, 1));
    }
}
