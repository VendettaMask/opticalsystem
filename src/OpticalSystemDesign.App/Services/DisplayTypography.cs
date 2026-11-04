using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Media;
using OpticalSystemDesign.App.Theming;

namespace OpticalSystemDesign.App.Services;

public static class DisplayTypography
{
    private static readonly ConditionalWeakTable<Control, LocalFontSizeState> LocalFontSizes = new();
    private static string _fontFamily = string.Empty;
    private static string _fontShape = "Regular";
    private static double _fontSize = AppSettings.DefaultFontSize;
    private static ThemeDefinition _appliedTheme = ThemeRegistry.FromSettings(AppSettings.DefaultTheme);

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
        if (global::Avalonia.Application.Current is { } application)
            PublishFontResources(application);
    }

    public static void Apply(TemplatedControl control)
    {
        // Newly created panel roots have no inherited variant until they enter
        // the workspace; use the applied application theme during construction.
        var theme = ThemeRegistry.FromActualVariant(TopLevel.GetTopLevel(control) is null
            ? global::Avalonia.Application.Current?.ActualThemeVariant : control.ActualThemeVariant);
        control.FontFamily = FontFamily(theme);
        control.FontSize = _fontSize;
        control.FontStyle = FontStyle();
        control.FontWeight = FontWeight();
        TextOptions.SetTextRenderingMode(control, theme.TextRenderingMode);
    }

    public static void ApplyRecursively(Control root, double previousFontSize)
    {
        ArgumentNullException.ThrowIfNull(root);
        var normalizedPrevious = double.IsFinite(previousFontSize) && previousFontSize > 0
            ? previousFontSize
            : AppSettings.DefaultFontSize;
        RescaleLocalFontSize(root, normalizedPrevious);
        foreach (var descendant in root.GetLogicalDescendants().OfType<Control>())
        {
            RescaleLocalFontSize(descendant, normalizedPrevious);
        }

        if (root is TemplatedControl templated)
        {
            Apply(templated);
        }
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

    private static void RescaleLocalFontSize(Control control, double previousFontSize)
    {
        if (!control.IsSet(TextElement.FontSizeProperty))
        {
            return;
        }

        var current = TextElement.GetFontSize(control);
        if (!double.IsFinite(current) || current <= 0)
        {
            return;
        }

        var state = LocalFontSizes.GetOrCreateValue(control);
        if (!state.Initialized || Math.Abs(current - state.LastAppliedSize) > 1e-9)
        {
            state.DesignedSize = current * AppSettings.DefaultFontSize / previousFontSize;
            state.Initialized = true;
        }

        var target = Scale(state.DesignedSize);
        TextElement.SetFontSize(control, target);
        state.LastAppliedSize = target;
    }

    internal static void PublishFontResources(global::Avalonia.Application application, ThemeDefinition? theme = null)
    {
        theme ??= ThemeRegistry.FromActualVariant(application.ActualThemeVariant);
        Volatile.Write(ref _appliedTheme, theme);
        application.Resources[ProductFonts.InterfaceResource] = ResolveFontFamily(_fontFamily, theme);
        application.Resources[ProductFonts.NumericResource] = ResolveFontFamily(_fontFamily, theme, numeric: true);
    }

    public static FontFamily ResolveFontFamily(string? family, string? theme, bool numeric = false)
    {
        var visual = ThemeRegistry.FromSettings(theme).ResolveVisual(
            global::Avalonia.Application.Current?.ActualThemeVariant);
        return ResolveFontFamily(family, visual, numeric);
    }

    private static FontFamily ResolveFontFamily(string? family, ThemeDefinition theme, bool numeric = false)
    {
        if (theme.LocksFontFamily) return theme.UiFontFamily;
        if (!string.IsNullOrWhiteSpace(family)) return new FontFamily(family);
        return numeric && theme.UiFontFamily == ProductFonts.Interface ? ProductFonts.Numeric : theme.UiFontFamily;
    }

    internal static string[] FontNames(ProductFontRole role)
    {
        var theme = global::Avalonia.Application.Current is null ? ThemeRegistry.FromSettings(AppSettings.DefaultTheme) : Volatile.Read(ref _appliedTheme);
        return FontNames(role, theme, _fontFamily);
    }

    internal static Dictionary<ProductFontRole, string[]> CaptureDocumentFonts()
    {
        var theme = global::Avalonia.Application.Current is null ? ThemeRegistry.FromSettings(AppSettings.DefaultTheme) : Volatile.Read(ref _appliedTheme);
        var custom = _fontFamily;
        return Enum.GetValues<ProductFontRole>().ToDictionary(role => role, role => FontNames(role, theme, custom));
    }

    private static string[] FontNames(ProductFontRole role, ThemeDefinition theme, string custom)
    {
        if (role == ProductFontRole.Math) return [ProductFonts.MathFamilyName, ProductFonts.DocumentFamilyName];
        var portable = role is ProductFontRole.Engineering or ProductFontRole.Document or ProductFontRole.Code;
        var selected = theme.LocksFontFamily || !string.IsNullOrWhiteSpace(custom) || !portable
            ? ProductFonts.FamilyNames(ResolveFontFamily(custom, theme, role is ProductFontRole.Numeric or ProductFontRole.Code))
            : role == ProductFontRole.Code ? ["JetBrains Mono"] : [ProductFonts.DocumentFamilyName];
        // Portable exports use registered bytes; unavailable/custom machine fonts
        // have a deterministic bundled replacement selected here for every writer.
        var registered = selected.Where(name => ProductFonts.Faces(name).Count != 0).ToList();
        if (registered.Count == 0) registered.Add("DM Sans");
        if (portable && registered.Count > 1) registered.RemoveAll(name => name == "Source Han Sans SC");
        registered.Add(ProductFonts.MathFamilyName); registered.Add(ProductFonts.DocumentFamilyName);
        return registered.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static FontFamily FontFamily(ThemeDefinition? theme = null) => ResolveFontFamily(_fontFamily,
        theme ?? ThemeRegistry.FromActualVariant(global::Avalonia.Application.Current?.ActualThemeVariant));

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

    private sealed class LocalFontSizeState
    {
        public bool Initialized { get; set; }

        public double DesignedSize { get; set; }

        public double LastAppliedSize { get; set; }
    }
}
