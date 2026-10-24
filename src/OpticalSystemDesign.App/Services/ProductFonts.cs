using Avalonia.Media;

namespace OpticalSystemDesign.App.Services;

/// <summary>Bundled, platform-independent UI families. Drawing fonts are managed separately.</summary>
public static class ProductFonts
{
    private const string Source = "avares://OpticalSystemDesign.App/Assets/Fonts/Typography/";
    private const string Latin = Source + "Latin#DM Sans";
    private const string Cjk = Source + "Cjk#Source Han Sans SC";
    private const string Mono = Source + "Numeric#JetBrains Mono";
    public const string NumericClass = "numeric-text";
    public const string InterfaceResource = "OpticalSystemDesignInterfaceFontFamily";
    public const string NumericResource = "OpticalSystemDesignNumericFontFamily";

    public static FontFamily Interface { get; } = new(Latin + ", " + Cjk);
    public static FontFamily Numeric { get; } = new(Mono + ", " + Cjk);

    public static IReadOnlyList<string> SelectableFamilies { get; } =
        new[] { Cjk, Latin, Mono };
}
