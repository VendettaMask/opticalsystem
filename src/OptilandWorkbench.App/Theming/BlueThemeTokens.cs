using Avalonia.Media;

namespace OptilandWorkbench.App.Theming;

/// <summary>Semantic colors for the blue / mist-blue light theme.</summary>
internal static class BlueThemeTokens
{
    public static readonly Color AppBackground = Color.Parse("#F2F5FB");
    public static readonly Color Surface = Color.Parse("#FFFFFF");
    public static readonly Color Sidebar = Color.Parse("#F5F7FC");
    public static readonly Color HeaderBackground = Color.Parse("#2F6FD1");
    public static readonly Color HeaderHoverBackground = Color.Parse("#2864C3");
    public static readonly Color HeaderPressedBackground = Color.Parse("#235AB3");
    public static readonly Color TitleText = Color.Parse("#FFFFFF");
    public static readonly Color HeaderMenuText = Color.Parse("#F5F8FF");
    public static readonly Color HeaderMuted = Color.Parse("#D1DDF0");
    public static readonly Color HeaderDivider = Color.Parse("#4A82D8");
    public static readonly Color TableHeader = Color.Parse("#EBF0F9");
    public static readonly Color TextPrimary = Color.Parse("#22334C");
    public static readonly Color TextSecondary = Color.Parse("#5C6F8A");
    public static readonly Color TextDisabled = Color.Parse("#9BA9BF");
    public static readonly Color EmphasisText = Color.Parse("#2446F0");
    public static readonly Color EmphasisTextHover = Color.Parse("#1C3ED8");
    public static readonly Color EmphasisTextPressed = Color.Parse("#1734BB");
    public static readonly Color Divider = Color.Parse("#DDE5F0");
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
