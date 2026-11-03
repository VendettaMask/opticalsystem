using Avalonia;
using Avalonia.Media;

namespace OpticalSystemDesign.App.Theming;

/// <summary>Semantic colors for the blue / mist-blue light theme.</summary>
internal static class BlueThemeTokens
{
    public static readonly Color AppBackground = Color.Parse("#F0F5FC");
    public static readonly Color Surface = Color.Parse("#FFFFFF");
    public static readonly Color Sidebar = Color.Parse("#F5F8FE");
    public static readonly Color HeaderBackground = Color.Parse("#9ACBFA");
    public static readonly Color CaptionSearchBackground = Color.Parse("#80FFFFFF");
    public static readonly Color CaptionSearchHover = Color.Parse("#B0FFFFFF");
    public static readonly Color CaptionSearchBorder = Color.Parse("#8AAED5");
    public static readonly Color CaptionSearchPlaceholder = Color.Parse("#405C7B");
    public static readonly Color CaptionButtonPressed = Color.Parse("#A8C8EE");
    public const double BackdropFadeFraction = .48;
    public static IBrush WorkbenchGradient { get; } = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = new GradientStops
        {
            new(HeaderBackground, 0),
            new(Color.Parse("#CDE6FC"), .23),
            new(Color.Parse("#F1F8FF"), BackdropFadeFraction),
            new(Surface, .62),
            new(Surface, 1)
        }
    }.ToImmutable();
    // These layers reveal the single window-sized gradient instead of restarting it.
    public static IBrush HeaderSurface => LightGlassMaterial.Sheen;
    public static IBrush RibbonSurface => Brushes.Transparent;
    public static readonly Color HeaderHoverBackground = Color.Parse("#C4DCF7");
    public static readonly Color HeaderPressedBackground = Color.Parse("#A8C9F0");
    public static readonly Color TitleText = Color.Parse("#16324F");
    public static readonly Color HeaderMenuText = Color.Parse("#16324F");
    public static readonly Color HeaderMuted = Color.Parse("#50657D");
    public static readonly Color HeaderDivider = Color.Parse("#B7D0EC");
    public static readonly Color TableHeader = Color.Parse("#EAF3FD");
    public static readonly Color LensEditorHeaderBackground = Color.Parse("#CCE3FA");
    public static readonly Color TextPrimary = Color.Parse("#22334C");
    public static readonly Color TextSecondary = Color.Parse("#5C6F8A");
    public static readonly Color TextDisabled = Color.Parse("#9BA9BF");
    public static readonly Color EmphasisText = Color.Parse("#2446F0");
    public static readonly Color EmphasisTextHover = Color.Parse("#1C3ED8");
    public static readonly Color EmphasisTextPressed = Color.Parse("#1734BB");
    public static readonly Color Divider = Color.Parse("#DCE5F2");
    public static readonly Color SidebarInputBorder = Color.Parse("#C6D5EB");
    public static IBrush StatusBarGradient { get; } = VerticalGradient(Color.Parse("#FCFDFF"), Color.Parse("#F0F5FD"));

    private static IBrush VerticalGradient(Color top, Color bottom) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = new GradientStops { new(top, 0), new(bottom, 1) }
    }.ToImmutable();
    public static readonly Color ControlBorder = Color.Parse("#8297B6");
    public static readonly Color Accent = Color.Parse("#2F65CC");
    public static readonly Color AccentHover = Color.Parse("#2554AF");
    public static readonly Color AccentPressed = Color.Parse("#1D448F");
    // Keep filled actions, text and focus outlines independently addressable.
    public static readonly Color PrimaryButtonBackground = Color.Parse("#2F65CC");
    public static readonly Color PrimaryButtonHoverBackground = Color.Parse("#2554AF");
    public static readonly Color PrimaryButtonPressedBackground = Color.Parse("#1D448F");
    public static readonly Color PrimaryButtonText = Color.Parse("#FFFFFF");
    public static readonly Color FocusBorder = Color.Parse("#2F65CC");
    public static readonly Color HoverBackground = Color.Parse("#EAF1FF");
    public static readonly Color PressedBackground = Color.Parse("#CDDDFA");
    public static readonly Color PressedBorder = Color.Parse("#5B80C0");
    public static readonly Color SelectedBackground = Color.Parse("#DFEAFE");
    public static readonly Color SelectedHover = Color.Parse("#D5E3FC");
    public static readonly Color SelectedPressed = Color.Parse("#B9CFF5");
    // Material presence is persistent data styling, independent of row selection.
    public static readonly Color MaterialRowBackground = HoverBackground;
    public static readonly Color MaterialRowHoverBackground = SelectedHover;
    public static readonly Color TextSelectionBackground = Color.Parse("#BED3FA");
    public static readonly Color TextSelectionForeground = Color.Parse("#22334C");
    public static readonly Color DisabledBackground = Color.Parse("#F0F3F8");
    public static readonly Color Error = Color.Parse("#AD3F3B");
    public static readonly Color ErrorBackground = Color.Parse("#FBEDEC");
}
