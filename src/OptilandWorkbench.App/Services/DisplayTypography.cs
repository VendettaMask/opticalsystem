using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace OptilandWorkbench.App.Services;

public static class DisplayTypography
{
    private static string _fontFamily = string.Empty;
    private static string _fontShape = "Regular";
    private static double _fontSize = AppSettings.DefaultFontSize;

    public static double FontSize => _fontSize;
    public static double SplashTitle => Scale(29);
    public static double EmptyStateIcon => Scale(27);
    public static double PageTitle => Scale(22);
    public static double LargeTitle => Scale(20);
    public static double WindowTitle => Scale(18);
    public static double EmptyStateTitle => Scale(17);
    public static double SectionTitle => Scale(16);
    public static double CardTitle => Scale(14);
    public static double Body => Scale(13);
    public static double BodySmall => Scale(12);
    public static double CompactBody => Scale(11.5);
    public static double RibbonText => Scale(11);
    public static double Caption => Scale(10.5);
    public static double Micro => Scale(10);

    public static void Configure(AppSettings settings)
    {
        settings.NormalizeDisplaySettings();
        _fontFamily = settings.FontFamily;
        _fontShape = settings.FontShape;
        _fontSize = settings.FontSize;
    }

    public static void Apply(TemplatedControl control)
    {
        control.FontFamily = FontFamily();
        control.FontSize = _fontSize;
        control.FontStyle = FontStyle();
        control.FontWeight = FontWeight();
    }

    public static Typeface Typeface(FontWeight? weight = null)
    {
        return new Typeface(
            FontFamily(),
            FontStyle(),
            weight ?? FontWeight());
    }

    public static double Scale(double designedSize)
    {
        return designedSize * (_fontSize / AppSettings.DefaultFontSize);
    }

    private static FontFamily FontFamily()
    {
        return string.IsNullOrWhiteSpace(_fontFamily)
            ? Avalonia.Media.FontFamily.Default
            : new FontFamily(_fontFamily);
    }

    private static FontStyle FontStyle()
    {
        return _fontShape is "Italic" or "BoldItalic"
            ? Avalonia.Media.FontStyle.Italic
            : Avalonia.Media.FontStyle.Normal;
    }

    private static FontWeight FontWeight()
    {
        return _fontShape is "Bold" or "BoldItalic"
            ? Avalonia.Media.FontWeight.Bold
            : Avalonia.Media.FontWeight.Normal;
    }
}
