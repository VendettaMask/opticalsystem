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
