using Avalonia;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace OptilandWorkbench.App.Services;

public static class ThemeResourceBindings
{
    public const string Surface = "OptilandSurfaceBrush";
    public const string SubtleSurface = "OptilandSubtleSurfaceBrush";
    public const string Workspace = "OptilandWorkspaceBrush";
    public const string Border = "OptilandBorderBrush";
    public const string MutedText = "OptilandMutedTextBrush";
    public const string Hover = "OptilandHoverBrush";
    public const string HoverBorder = "OptilandHoverBorderBrush";

    public static IDisposable BindThemeResource(
        this AvaloniaObject target,
        AvaloniaProperty property,
        string resourceKey) =>
        target.Bind(
            property,
            new DynamicResourceExtension(resourceKey));
}
