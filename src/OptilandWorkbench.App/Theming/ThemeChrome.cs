using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace OptilandWorkbench.App.Theming;

internal enum ThemeChromeRole
{
    Ribbon,
    Workspace,
    SettingsCard,
    SurfaceCard,
    ControlFrame,
    StatusBar,
    Dialog,
    Viewport
}

internal sealed record ThemeChromeStyle(
    Color BorderColor,
    Thickness BorderThickness,
    CornerRadius CornerRadius,
    BoxShadows BoxShadow);

internal sealed class ThemeChromeProfile
{
    private readonly IReadOnlyDictionary<ThemeChromeRole, ThemeChromeStyle> _styles;

    private ThemeChromeProfile(IReadOnlyDictionary<ThemeChromeRole, ThemeChromeStyle> styles)
    {
        var missing = Enum.GetValues<ThemeChromeRole>()
            .Where(role => !styles.ContainsKey(role))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new ArgumentException(
                $"主题 Chrome 缺少角色：{string.Join(", ", missing)}。",
                nameof(styles));
        }

        _styles = styles;
    }

    public ThemeChromeStyle this[ThemeChromeRole role] => _styles[role];

    public void AddResources(ResourceDictionary resources)
    {
        foreach (var (role, style) in _styles)
        {
            resources[ThemeChromeResources.BorderBrush(role)] = new SolidColorBrush(style.BorderColor);
            resources[ThemeChromeResources.BorderThickness(role)] = style.BorderThickness;
            resources[ThemeChromeResources.CornerRadius(role)] = style.CornerRadius;
            resources[ThemeChromeResources.BoxShadow(role)] = style.BoxShadow;
        }
    }

    public static ThemeChromeProfile Create(
        IReadOnlyDictionary<ThemeChromeRole, ThemeChromeStyle> styles) =>
        new(styles);

    public static ThemeChromeProfile CreateStandard(Color border)
    {
        var none = default(BoxShadows);
        var cardShadow = BoxShadows.Parse("0 5 16 0 #20000000");
        return new ThemeChromeProfile(new Dictionary<ThemeChromeRole, ThemeChromeStyle>
        {
            [ThemeChromeRole.Ribbon] = new(border, new Thickness(0, 0, 0, 1), new CornerRadius(0), BoxShadows.Parse("0 3 8 0 #14000000")),
            [ThemeChromeRole.Workspace] = new(border, new Thickness(0), new CornerRadius(0), none),
            [ThemeChromeRole.SettingsCard] = new(border, new Thickness(1), new CornerRadius(8), cardShadow),
            [ThemeChromeRole.SurfaceCard] = new(border, new Thickness(1), new CornerRadius(8), cardShadow),
            [ThemeChromeRole.ControlFrame] = new(border, new Thickness(1), new CornerRadius(5), none),
            [ThemeChromeRole.StatusBar] = new(border, new Thickness(0, 1, 0, 0), new CornerRadius(0), none),
            [ThemeChromeRole.Dialog] = new(border, new Thickness(1), new CornerRadius(8), cardShadow),
            [ThemeChromeRole.Viewport] = new(border, new Thickness(1), new CornerRadius(0), none)
        });
    }

    public static ThemeChromeProfile CreateIsekai()
    {
        var gold = Color.FromRgb(181, 132, 54);
        var bronze = Color.FromRgb(116, 86, 43);
        var none = default(BoxShadows);
        var cardShadow = BoxShadows.Parse("0 6 18 0 #5C130D08");
        return new ThemeChromeProfile(new Dictionary<ThemeChromeRole, ThemeChromeStyle>
        {
            // Structural thickness stays aligned with the standard themes so a theme
            // switch cannot move content. Extra Isekai strokes belong to overlays.
            [ThemeChromeRole.Ribbon] = new(gold, new Thickness(0, 0, 0, 1), new CornerRadius(0), BoxShadows.Parse("0 4 10 0 #66130D08")),
            [ThemeChromeRole.Workspace] = new(gold, new Thickness(0), new CornerRadius(0), none),
            [ThemeChromeRole.SettingsCard] = new(gold, new Thickness(1), new CornerRadius(3), cardShadow),
            [ThemeChromeRole.SurfaceCard] = new(bronze, new Thickness(1), new CornerRadius(3), cardShadow),
            [ThemeChromeRole.ControlFrame] = new(bronze, new Thickness(1), new CornerRadius(2), none),
            [ThemeChromeRole.StatusBar] = new(gold, new Thickness(0, 1, 0, 0), new CornerRadius(0), none),
            [ThemeChromeRole.Dialog] = new(gold, new Thickness(1), new CornerRadius(3), cardShadow),
            [ThemeChromeRole.Viewport] = new(gold, new Thickness(1), new CornerRadius(0), none)
        });
    }
}

internal static class ThemeChromeResources
{
    public static string BorderBrush(ThemeChromeRole role) => $"OptilandChrome{role}BorderBrush";
    public static string BorderThickness(ThemeChromeRole role) => $"OptilandChrome{role}BorderThickness";
    public static string CornerRadius(ThemeChromeRole role) => $"OptilandChrome{role}CornerRadius";
    public static string BoxShadow(ThemeChromeRole role) => $"OptilandChrome{role}BoxShadow";
}

internal static class ThemeChrome
{
    public static void Apply(
        Border border,
        ThemeChromeRole role,
        bool shadow = true,
        bool borderBrush = true)
    {
        if (borderBrush)
        {
            border.Bind(Border.BorderBrushProperty, new DynamicResourceExtension(ThemeChromeResources.BorderBrush(role)));
        }
        border.Bind(Border.BorderThicknessProperty, new DynamicResourceExtension(ThemeChromeResources.BorderThickness(role)));
        border.Bind(Border.CornerRadiusProperty, new DynamicResourceExtension(ThemeChromeResources.CornerRadius(role)));
        if (shadow)
        {
            border.Bind(Border.BoxShadowProperty, new DynamicResourceExtension(ThemeChromeResources.BoxShadow(role)));
        }
        else
        {
            border.BoxShadow = default;
        }
    }

    public static Control WrapWithDecoration(Control content, ThemeChromeRole role) =>
        new ThemeChromeLayer(content, role);

    public static void ApplyDialogDecoration(Window window)
    {
        if (window.Content is not Control content || content is ThemeChromeLayer)
        {
            return;
        }

        // Detach first. Building a new logical parent while the control still
        // belongs to Window makes Avalonia reject controls such as ScrollViewer.
        window.Content = null;
        window.Content = WrapWithDecoration(content, ThemeChromeRole.Dialog);
    }
}

internal sealed class ThemeChromeLayer : Grid
{
    public ThemeChromeLayer(Control content, ThemeChromeRole role)
    {
        Children.Add(content);
        Children.Add(new ThemeChromeOverlay { Role = role });
    }
}

internal interface IThemeDecorationRenderer
{
    void Render(ThemeChromeRole role, DrawingContext context, Rect bounds);
}

internal sealed class NoThemeDecorationRenderer : IThemeDecorationRenderer
{
    public static NoThemeDecorationRenderer Instance { get; } = new();

    private NoThemeDecorationRenderer()
    {
    }

    public void Render(ThemeChromeRole role, DrawingContext context, Rect bounds)
    {
    }
}

internal sealed class ThemeChromeOverlay : Control
{
    public static readonly StyledProperty<ThemeChromeRole> RoleProperty =
        AvaloniaProperty.Register<ThemeChromeOverlay, ThemeChromeRole>(nameof(Role));

    public ThemeChromeOverlay()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    static ThemeChromeOverlay()
    {
        AffectsRender<ThemeChromeOverlay>(RoleProperty);
    }

    public ThemeChromeRole Role
    {
        get => GetValue(RoleProperty);
        set => SetValue(RoleProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        ThemeRegistry.FromActualVariant(ActualThemeVariant)
            .DecorationRenderer
            .Render(Role, context, new Rect(Bounds.Size));
    }
}
