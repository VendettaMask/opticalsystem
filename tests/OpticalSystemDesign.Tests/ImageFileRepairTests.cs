using System.Buffers.Binary;
using Avalonia;
using Avalonia.Headless;
using OpticalSystemDesign.Application.Runtime;
using OpticalSystemDesign.Application.Services;
using OpticalSystemDesign.Core;
using OpticalSystemDesign.Core.Analysis;
using OpticalSystemDesign.Core.Domain;
using SkiaSharp;

namespace OpticalSystemDesign.Tests;

public sealed class ImageFileRepairTests
{
    [Theory]
    [InlineData("png")]
    [InlineData("jpg")]
    [InlineData("jpeg")]
    [InlineData("bmp")]
    public void LoadRgbReadsSupportedFilesAndReleasesTheirHandles(string extension)
    {
        using var files = new ImageFileTestDirectory();
        var path = files.Source(extension);
        using var expected = SKBitmap.Decode(path);
        var actual = ImageFileLoader.LoadRgb(path);
        Assert.Equal(3, actual.Channels);
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        for (var y = 0; y < actual.Height; y++)
            for (var x = 0; x < actual.Width; x++)
            {
                var color = expected.GetPixel(x, y);
                Assert.Equal(color.Red / 255d, actual[0, y, x], 12);
                Assert.Equal(color.Green / 255d, actual[1, y, x], 12);
                Assert.Equal(color.Blue / 255d, actual[2, y, x], 12);
            }
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public void TransparentPngIsStillCompositedAgainstBlack()
    {
        using var files = new ImageFileTestDirectory();
        var path = files.PathFor("alpha.png");
        using (var bitmap = new SKBitmap(2, 2, SKColorType.Rgba8888, SKAlphaType.Unpremul))
        {
            bitmap.Erase(new SKColor(255, 0, 0, 128));
            ImageFileTestDirectory.WriteImage(bitmap, path, SKEncodedImageFormat.Png);
        }
        var image = ImageFileLoader.LoadRgb(path);
        Assert.Equal(128 / 255d, image[0, 0, 0], 8);
        Assert.Equal(0, image[1, 0, 0]);
        Assert.Equal(0, image[2, 0, 0]);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("tiny")]
    [InlineData("large-dimensions")]
    [InlineData("large-file")]
    public void InvalidAndOverBudgetInputIsRejectedAndReleased(string kind)
    {
        using var files = new ImageFileTestDirectory();
        var path = files.PathFor("rejected.png");
        if (kind == "invalid") File.WriteAllText(path, "not a bitmap");
        else if (kind == "large-file")
        {
            using var stream = File.Create(path);
            stream.SetLength(OpticalSystemDesign.Core.FileIO.BoundedFile.MaximumImageDataBytes + 1);
        }
        else
        {
            using var bitmap = new SKBitmap(kind == "tiny" ? 1 : 513, kind == "tiny" ? 1 : 513);
            bitmap.Erase(SKColors.Red);
            ImageFileTestDirectory.WriteImage(bitmap, path, SKEncodedImageFormat.Png);
        }
        if (kind == "large-dimensions")
            Assert.Throws<ArgumentOutOfRangeException>(() => ImageFileLoader.LoadRgb(path));
        else
            Assert.Throws<InvalidDataException>(() => ImageFileLoader.LoadRgb(path));
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpg")]
    [InlineData("jpeg")]
    [InlineData("bmp")]
    public void ExternalModeUsesTheSelectedFileInTheRealRuntime(string extension)
    {
        using var files = new ImageFileTestDirectory();
        var settings = Settings();
        settings["SourceMode"] = "外部位图";
        settings["SourceFile"] = files.Source(extension);
        var data = new WorkbenchRuntime(Cone()).BuildAnalysisData("Image Simulation", settings);
        Assert.Equal(AnalysisOutcome.Success, data.Outcome);
        Assert.Equal("External bitmap", data.Values["SourceMode"]);
        Assert.Equal(settings["SourceFile"], data.Values["SourceFile"]);
        var pane = Assert.Single(data.PlotPanes!);
        Assert.Equal("Source Bitmap", pane.Title);
        Assert.Equal(6, Assert.Single(pane.Series).Points.Count);
        using var expected = SKBitmap.Decode(settings["SourceFile"]);
        Assert.Equal(expected.GetPixel(0, 0).Red / 255d, pane.Series[0].Points[0].Red);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void BuiltInModeIgnoresRetainedValidOrMissingExternalPaths(bool explicitMode, bool missingFile)
    {
        using var files = new ImageFileTestDirectory();
        var settings = Settings();
        if (explicitMode) settings["SourceMode"] = "内置图像";
        settings["SourceFile"] = missingFile ? files.PathFor("missing.png") : files.Source("png");
        var runtime = new WorkbenchRuntime(Cone());
        var actual = runtime.BuildAnalysisData("Image Simulation", settings);
        var expected = runtime.BuildAnalysisData("Image Simulation", Settings());
        Assert.Equal(AnalysisOutcome.Success, actual.Outcome);
        Assert.Equal("Built-in", actual.Values["SourceMode"]);
        Assert.Equal(string.Empty, actual.Values["SourceFile"]);
        Assert.Equal(expected.PlotPanes![0].Series[0].Points, actual.PlotPanes![0].Series[0].Points);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("missing.png")]
    public void ExternalModeDoesNotSilentlyFallBackAndPreservesExistingOutput(string input)
    {
        using var files = new ImageFileTestDirectory();
        var output = files.PathFor("existing.png");
        var original = new byte[] { 1, 2, 3, 4 };
        File.WriteAllBytes(output, original);
        var settings = Settings();
        settings["SourceMode"] = "外部位图";
        settings["SourceFile"] = string.IsNullOrWhiteSpace(input) ? input : files.PathFor(input);
        settings["OutputFile"] = output;
        var runtime = new WorkbenchRuntime(Cone());
        if (string.IsNullOrWhiteSpace(input))
        {
            var exception = Assert.Throws<InvalidOperationException>(() => runtime.BuildAnalysisData("Image Simulation", settings));
            Assert.Contains("requires an input image file", exception.Message, StringComparison.Ordinal);
        }
        else Assert.Throws<FileNotFoundException>(() => runtime.BuildAnalysisData("Image Simulation", settings));
        Assert.Equal(original, File.ReadAllBytes(output));
    }

    [Fact]
    public void SwitchingModesDoesNotRequireDeletingTheRetainedPath()
    {
        using var files = new ImageFileTestDirectory();
        var settings = Settings();
        settings["SourceMode"] = "外部位图";
        settings["SourceFile"] = files.Source("png");
        var runtime = new WorkbenchRuntime(Cone());
        var external = runtime.BuildAnalysisData("Image Simulation", settings);
        settings["SourceMode"] = "内置图像";
        var builtIn = runtime.BuildAnalysisData("Image Simulation", settings);
        settings["SourceMode"] = "外部位图";
        var externalAgain = runtime.BuildAnalysisData("Image Simulation", settings);
        Assert.Equal("Built-in", builtIn.Values["SourceMode"]);
        Assert.Equal(256, builtIn.PlotPanes![0].Series[0].Points.Count);
        Assert.Equal(external.PlotPanes![0].Series[0].Points, externalAgain.PlotPanes![0].Series[0].Points);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 2)]
    public void BmpHasRealHeadersRowPaddingBgrChannelsAndBottomUpRows(int width, int height)
    {
        using var files = new ImageFileTestDirectory();
        var path = files.PathFor("raster.BMP");
        var data = Raster(width, height);
        ImageFileLoader.SaveAnalysisRaster(data, path);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal("BM", System.Text.Encoding.ASCII.GetString(bytes, 0, 2));
        Assert.Equal(bytes.Length, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(2)));
        Assert.Equal(54, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(10)));
        Assert.Equal(40, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(14)));
        Assert.Equal(width, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(18)));
        Assert.Equal(height, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(22)));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(26)));
        Assert.Equal(24, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(28)));
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(30)));
        var stride = ((width * 3 + 3) / 4) * 4;
        Assert.Equal(stride * height, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(34)));
        Assert.Equal(54 + stride * height, bytes.Length);
        using var decoded = SKBitmap.Decode(path);
        Assert.NotNull(decoded);
        for (var row = 0; row < height; row++)
        {
            for (var x = 0; x < width; x++)
            {
                var expected = Color(x, height - 1 - row);
                Assert.Equal(expected, decoded.GetPixel(x, row));
                var offset = 54 + (height - 1 - row) * stride + x * 3;
                Assert.Equal(new[] { expected.Blue, expected.Green, expected.Red }, bytes[offset..(offset + 3)]);
            }
            Assert.All(bytes[(54 + row * stride + width * 3)..(54 + (row + 1) * stride)], value => Assert.Equal(0, value));
        }
    }

    [Theory]
    [InlineData("png", "89504E47")]
    [InlineData("jpg", "FFD8FFE0")]
    [InlineData("jpeg", "FFD8FFE0")]
    [InlineData("bmp", "424D")]
    public void RuntimeExportsRealSupportedFormatsThatCanBeReadBack(string extension, string magic)
    {
        using var files = new ImageFileTestDirectory();
        var output = files.PathFor("runtime." + extension);
        File.WriteAllBytes(output, [1, 2, 3, 4]);
        var settings = Settings();
        settings["OutputFile"] = output;
        var data = new WorkbenchRuntime(Cone()).BuildAnalysisData("Image Simulation", settings);
        Assert.Equal(AnalysisOutcome.Success, data.Outcome);
        Assert.StartsWith(magic, Convert.ToHexString(File.ReadAllBytes(output)), StringComparison.Ordinal);
        var imported = ImageFileLoader.LoadRgb(output);
        Assert.Equal(16, imported.Width);
        Assert.Equal(16, imported.Height);
        if (extension is "png" or "bmp")
        {
            var point = data.PlotPanes![0].Series[0].Points[0];
            Assert.Equal(Math.Round(point.Red!.Value * 255) / 255d, imported[0, 0, 0], 12);
        }
        Assert.Single(Directory.GetFiles(files.DirectoryPath));
    }

    [Theory]
    [InlineData("png")]
    [InlineData("bmp")]
    public void InvalidRasterCannotReplaceExistingOutput(string extension)
    {
        using var files = new ImageFileTestDirectory();
        var output = files.PathFor("existing." + extension);
        var original = new byte[] { 1, 2, 3, 4 };
        File.WriteAllBytes(output, original);
        var series = new AnalysisSeries("", "", [new AnalysisPoint(double.NaN, 0)], AnalysisSeriesKind.Raster);
        var data = new AnalysisData("Image Simulation", new Dictionary<string, object>(), series, [series]);
        Assert.Throws<InvalidDataException>(() => ImageFileLoader.SaveAnalysisRaster(data, output));
        Assert.Equal(original, File.ReadAllBytes(output));
        Assert.Single(Directory.GetFiles(files.DirectoryPath));
    }

    private static AnalysisData Raster(int width, int height)
    {
        var points = new List<AnalysisPoint>();
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var color = Color(x, y);
                points.Add(new AnalysisPoint(x, y, Red: color.Red / 255d, Green: color.Green / 255d, Blue: color.Blue / 255d));
            }
        var series = new AnalysisSeries("", "", points, AnalysisSeriesKind.Raster);
        return new AnalysisData("Image Simulation", new Dictionary<string, object>(), series, [series]);
    }

    private static SKColor Color(int x, int y) => new((byte)(20 + 31 * x), (byte)(40 + 51 * y), (byte)(90 + 11 * x + 13 * y));

    private static Optic Cone()
    {
        var optic = new Optic { FieldDefinition = FieldDefinitionKind.ObjectHeight };
        optic.Aperture.Value = 2;
        optic.Fields.Add(new FieldPoint { Y = 2 });
        optic.Wavelengths.Add(new Wavelength { Nanometers = 550, IsPrimary = true });
        optic.SurfaceGroup.Replace([
            new OpticalSurface { Thickness = 10 },
            new OpticalSurface { IsStop = true, SemiDiameter = 1, Thickness = 2 },
            new OpticalSurface { Thickness = 8 },
            new OpticalSurface()
        ]);
        return optic;
    }

    private static Dictionary<string, string> Settings() => new()
    {
        ["ImageWidth"] = "16",
        ["ImageHeight"] = "16",
        ["GuardBand"] = "无",
        ["PsfSize"] = "8 x 8",
        ["NumRays"] = "8 x 8",
        ["PsfGridRows"] = "1",
        ["PsfGridColumns"] = "1",
        ["EigenPsfComponents"] = "1",
        ["DistortionGridSize"] = "9",
        ["DistortionPolynomialDegree"] = "1",
        ["PixelSize"] = "0.01",
        ["FieldHeight"] = "1",
        ["AberrationMode"] = "无",
        ["RelativeIllumination"] = "false",
        ["DisplayAs"] = "源位图"
    };
}

[Collection(HeadlessAvaloniaCollection.Name)]
public sealed class BitmapViewerFileRepairTests
{
    private static bool UseRealDecoding => Environment.GetEnvironmentVariable("OPTICALSYSTEMDESIGN_IMAGE_FILE_DECODE_TESTS") == "1";

    public static AppBuilder BuildAvaloniaApp()
    {
        // Run real decoding separately: Avalonia's cached typefaces cannot mix Skia and placeholder renderers.
        var builder = AppBuilder.Configure<HeadlessTestApplication>();
        if (UseRealDecoding) builder.UseSkia();
        return builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !UseRealDecoding });
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpg")]
    [InlineData("jpeg")]
    [InlineData("bmp")]
    public async Task ViewerOpensValidBitmapsAndReleasesTheirHandles(string extension)
    {
        using var files = new ImageFileTestDirectory();
        var path = files.Source(extension);
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(BitmapViewerFileRepairTests));
        await session.Dispatch(() =>
        {
            var window = new OpticalSystemDesign.App.Panels.ImageFileViewerWindow(path, zemaxImage: false);
            try
            {
                window.Show();
                var images = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.Image>();
                var bitmap = Assert.IsType<Avalonia.Media.Imaging.Bitmap>(Assert.Single(images).Source);
                Assert.Equal(UseRealDecoding ? new PixelSize(3, 2) : new PixelSize(1, 1), bitmap.PixelSize);
                using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("large-dimensions")]
    [InlineData("large-file")]
    public async Task ViewerKeepsMetadataAndFileSizeLimitsAndReleasesRejectedFiles(string kind)
    {
        using var files = new ImageFileTestDirectory();
        var path = files.PathFor("rejected.png");
        if (kind == "invalid") File.WriteAllText(path, "not a bitmap");
        else if (kind == "large-file")
        {
            using var stream = File.Create(path);
            stream.SetLength(BoundedApplicationFile.MaximumImageDataBytes + 1);
        }
        else
        {
            using var bitmap = new SKBitmap(32_769, 1);
            bitmap.Erase(SKColors.Red);
            ImageFileTestDirectory.WriteImage(bitmap, path, SKEncodedImageFormat.Png);
        }
        using var session = SafeHeadlessUnitTestSession.StartNew(typeof(BitmapViewerFileRepairTests));
        await session.Dispatch(() =>
        {
            OpticalSystemDesign.App.Panels.ImageFileViewerWindow? window = null;
            try
            {
                Assert.Throws<InvalidDataException>(() => window = new(path, zemaxImage: false));
                using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            finally { window?.Close(); }
        }, CancellationToken.None);
    }
}

internal sealed class ImageFileTestDirectory : IDisposable
{
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "opticalsystemdesign-image-files-" + Guid.NewGuid().ToString("N"));

    public ImageFileTestDirectory() => Directory.CreateDirectory(DirectoryPath);

    public string PathFor(string name) => Path.Combine(DirectoryPath, name);

    public string Source(string extension)
    {
        var path = PathFor("source." + extension);
        if (extension == "bmp")
        {
            // Independent 3x2, 24-bit BI_RGB fixture, including padded rows.
            File.WriteAllBytes(path, Convert.FromHexString(
                "424D4E0000000000000036000000280000000300000002000000010018000000000018000000130B0000130B00000000000000000000"
                + "FF000000FFFFFFFF000000000000FF00FF00FF00FF000000"));
        }
        else
        {
            using var bitmap = new SKBitmap(3, 2);
            bitmap.Erase(new SKColor(25, 100, 200));
            bitmap.SetPixel(2, 1, SKColors.Yellow);
            WriteImage(bitmap, path, extension is "jpg" or "jpeg" ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png);
        }
        return path;
    }

    internal static void WriteImage(SKBitmap bitmap, string path, SKEncodedImageFormat format)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(format, 95);
        using var stream = File.Create(path);
        encoded.SaveTo(stream);
    }

    public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
}
