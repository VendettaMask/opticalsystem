using Avalonia;

namespace OpticalSystemDesign.App.Services;

/// <summary>Independent pixel-height and print-DPI preferences; never derive DPI from a screen preset.</summary>
public static class ImageExportResolution
{
    public const double ReferenceHeight = 902;
    public const double DefaultDpi = 150;
    public const int DefaultPixelHeight = 1080;
    public const int DefaultPixelWidth = 1920;
    public const double MinimumDpi = 24;
    public const double MaximumDpi = 600;
    public static double Dpi { get; private set; } = DefaultDpi;
    public static int PixelHeight { get; private set; } = DefaultPixelHeight;
    public static int PixelWidth { get; private set; } = DefaultPixelWidth;
    public static bool PreserveAspectRatio { get; private set; } = true;
    public static double Scale => PixelHeight / ReferenceHeight;
    public static double Normalize(double dpi) => double.IsFinite(dpi) && dpi >= MinimumDpi && dpi <= MaximumDpi
        ? dpi : DefaultDpi;
    public static int NormalizeHeight(int height) => height is >= 128 and <= 8192 ? height : DefaultPixelHeight;
    public static int NormalizeWidth(int width) => width is >= 128 and <= 8192 ? width : DefaultPixelWidth;
    internal static int WidthForHeight(int height) => (int)Math.Round(NormalizeHeight(height) * 16d / 9);
    public static void Configure(AppSettings settings)
    {
        Dpi = Normalize(settings.ImageExportDpi);
        PixelHeight = NormalizeHeight(settings.ImageExportPixelHeight);
        PixelWidth = NormalizeWidth(settings.ImageExportPixelWidth);
        PreserveAspectRatio = settings.ImageExportPreserveAspectRatio;
    }
    internal static int Pixels(double logical, double scale) => checked((int)Math.Ceiling(logical * scale - 1e-8));
    public static PixelSize SheetPixels(int pixelHeight) => new(Pixels(1202, NormalizeHeight(pixelHeight) / ReferenceHeight), NormalizeHeight(pixelHeight));
    public static PixelRect FitFrame(Size source, PixelSize canvas, bool preserveAspectRatio, double border = 1)
    {
        if (!preserveAspectRatio) return new PixelRect(canvas);
        var scale = Math.Min(canvas.Width / (source.Width + 2 * border), canvas.Height / (source.Height + 2 * border));
        var width = Math.Min(canvas.Width, Pixels(source.Width + 2 * border, scale));
        var height = Math.Min(canvas.Height, Pixels(source.Height + 2 * border, scale));
        return new((canvas.Width - width) / 2, (canvas.Height - height) / 2, width, height);
    }
}
