using Avalonia.Media;

namespace OpticalSystemDesign.App.Services;

/// <summary>Single registry for UI, scientific drawings, engineering sheets and document fonts.</summary>
public static partial class ProductFonts
{
    private const string Source = "avares://OpticalSystemDesign.App/Assets/Fonts/Typography/";
    private const string Latin = Source + "Latin#DM Sans";
    // This collection contains real Medium/Bold faces. Normal CJK text resolves to
    // Medium without changing the requested weight of the Latin/numeric families.
    private const string Cjk = Source + "Cjk#Source Han Sans SC";
    private const string Mono = Source + "Numeric#JetBrains Mono";
    public const string AlmendraFamily = Source + "Almendra#Almendra";
    public const string MathFamilyName = "STIX Two Math";
    public const string DocumentFamilyName = "Noto Sans CJK SC";
    private const string MathFallback = Source + "Math#" + MathFamilyName;
    public const string NumericClass = "numeric-text";
    public const string InterfaceResource = "OpticalSystemDesignInterfaceFontFamily";
    public const string NumericResource = "OpticalSystemDesignNumericFontFamily";

    public static FontFamily Interface { get; } = new(Latin + ", " + Cjk + ", " + MathFallback);
    public static FontFamily Numeric { get; } = new(Mono + ", " + Cjk + ", " + MathFallback);
    // Keep user-entered CJK names readable without changing the English theme UI.
    public static FontFamily Isekai { get; } = new(AlmendraFamily + ", " + Cjk + ", " + MathFallback);
    public static FontFamily Pixel { get; } = new("avares://OpticalSystemDesign.App/Assets/Fonts/fusion-pixel-10px-monospaced-zh_hans.ttf#Fusion Pixel 10px Mono zh_hans, " + MathFallback + ", " + Cjk);

    public static IReadOnlyList<string> SelectableFamilies { get; } =
        new[] { Cjk, Latin, Mono, AlmendraFamily };
}
