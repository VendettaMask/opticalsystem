using Avalonia;

namespace OpticalSystemDesign.App.Services;

/// <summary>Presentation dimensions only; changing the sheet never rebuilds an optical result.</summary>
public static class DiagramSheetLayout
{
    public const string Automatic = "Auto";
    public const string Standard = "4:3";
    public const string Wide = "16:9";
    public static string AspectRatio { get; private set; } = Automatic;
    private static double _exportWidth = 1600;
    public static double Width => AspectRatio switch { Standard => 1200, Wide => 1600, _ => _exportWidth };
    internal static event Action? Changed;

    public static string Normalize(string? value) => value is Standard or Wide ? value : Automatic;

    // Equal unused area for the two sheets. A small dead band prevents a scrollbar
    // or a one-pixel layout change from repeatedly switching the selected sheet.
    internal static double AutomaticWidth(Size viewport, double currentWidth = 0)
    {
        if (!double.IsFinite(viewport.Width) || !double.IsFinite(viewport.Height)
            || viewport.Width <= 0 || viewport.Height <= 0)
            return currentWidth is 1200 or 1600 ? currentWidth : 1600;
        var aspect = viewport.Width / viewport.Height;
        var threshold = Math.Sqrt((4d / 3) * (16d / 9));
        if (currentWidth == 1600 && aspect >= threshold - .03) return 1600;
        if (currentWidth == 1200 && aspect <= threshold + .03) return 1200;
        return aspect >= threshold ? 1600 : 1200;
    }

    internal static void Configure(AppSettings settings)
    {
        var next = Normalize(settings.DiagramAspectRatio);
        var exportWidth = AutomaticWidth(new Size(ImageExportResolution.NormalizeWidth(settings.ImageExportPixelWidth),
            ImageExportResolution.NormalizeHeight(settings.ImageExportPixelHeight)));
        if (next == AspectRatio && exportWidth == _exportWidth) return;
        AspectRatio = next;
        _exportWidth = exportWidth;
        Changed?.Invoke();
    }
}
