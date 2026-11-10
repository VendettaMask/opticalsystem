using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Theming;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.App.Controls;

public sealed class LocalIcon : Control
{
    private TemplatedControl? _colourHost;
    public static readonly StyledProperty<string> IconNameProperty =
        AvaloniaProperty.Register<LocalIcon, string>(nameof(IconName), "circle-question-mark");

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<LocalIcon, IBrush?>(nameof(Stroke), Brushes.Black);

    public static readonly StyledProperty<double> StrokeWidthProperty =
        AvaloniaProperty.Register<LocalIcon, double>(nameof(StrokeWidth), ScientificSvgArtwork.SourceStrokeWidth);

    public static readonly StyledProperty<IBrush?> AccentStrokeProperty =
        AvaloniaProperty.Register<LocalIcon, IBrush?>(nameof(AccentStroke));

    public static readonly StyledProperty<Color?> DisabledColorProperty =
        AvaloniaProperty.Register<LocalIcon, Color?>(nameof(DisabledColor));

    static LocalIcon()
    {
        AffectsRender<LocalIcon>(
            IconNameProperty,
            StrokeProperty,
            StrokeWidthProperty,
            AccentStrokeProperty,
            DisabledColorProperty);
    }

    public string IconName
    {
        get => GetValue(IconNameProperty);
        set => SetValue(IconNameProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeWidth
    {
        get => GetValue(StrokeWidthProperty);
        set => SetValue(StrokeWidthProperty, value);
    }

    public IBrush? AccentStroke
    {
        get => GetValue(AccentStrokeProperty);
        set => SetValue(AccentStrokeProperty, value);
    }

    public Color? DisabledColor
    {
        get => GetValue(DisabledColorProperty);
        set => SetValue(DisabledColorProperty, value);
    }

    public LocalIcon()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _colourHost = this.GetVisualAncestors().OfType<TemplatedControl>().FirstOrDefault();
        if (_colourHost is not null) _colourHost.PropertyChanged += HostColourChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_colourHost is not null) _colourHost.PropertyChanged -= HostColourChanged;
        _colourHost = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void HostColourChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TemplatedControl.BackgroundProperty || e.Property == TemplatedControl.ForegroundProperty ||
            e.Property == IsEnabledProperty) InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(24, 24);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Stroke is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        if (!ThemeIconResolver.TryResolve(ActualThemeVariant, IconName, out var definition))
        {
            return;
        }

        if (definition.Artwork is { } artwork)
        {
            var theme = ThemeRegistry.FromActualVariant(ActualThemeVariant);
            var palette = OnSurfacePalette(theme);
            if (theme.Palette is { } colours)
            {
                if (!IsEffectivelyEnabled || Stroke is ISolidColorBrush disabled && disabled.Color == colours.TextDisabled)
                    palette = ScientificIconPalette.Monochrome(!IsEffectivelyEnabled && DisabledColor is { } disabledColour
                        ? disabledColour : colours.TextDisabled);
                else if (Stroke is ISolidColorBrush foreground &&
                    (foreground.Color == Colors.White || foreground.Color == colours.TextOnAccent ||
                     foreground.Color == colours.TextError || foreground.Color == colours.TextWarning || foreground.Color == colours.TextSuccess))
                    palette = ScientificIconPalette.Monochrome(foreground.Color);
            }
            artwork.Draw(context, Bounds, palette, StrokeWidth);
            return;
        }

        if (definition.Image is not null)
        {
            var image = definition.Image.Value;
            var side = Math.Min(Bounds.Width, Bounds.Height);
            var destination = new Rect(
                (Bounds.Width - side) / 2.0,
                (Bounds.Height - side) / 2.0,
                side,
                side);
            using var imageRenderOptions = context.PushRenderOptions(new RenderOptions
            {
                BitmapInterpolationMode = BitmapInterpolationMode.None,
                EdgeMode = EdgeMode.Aliased
            });
            context.DrawImage(image, new Rect(image.Size), destination);
            return;
        }

        var scale = Math.Min(Bounds.Width, Bounds.Height) / 24.0;
        var offsetX = (Bounds.Width - (24 * scale)) / 2.0;
        var offsetY = (Bounds.Height - (24 * scale)) / 2.0;
        var transform = Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY);
        using var renderOptions = definition.UseAliasedEdges
            ? context.PushRenderOptions(new RenderOptions { EdgeMode = EdgeMode.Aliased })
            : default;
        using (context.PushTransform(transform))
        {
            var pen = new Pen(
                Stroke,
                Math.Max(0.5, StrokeWidth * definition.StrokeWidthScale),
                lineCap: definition.LineCap,
                lineJoin: definition.LineJoin);
            using (context.PushTransform(definition.ContentTransform))
            {
                foreach (var primitive in definition.Primitives)
                {
                    primitive.Draw(context, pen);
                }
            }

            if (AccentStroke is not null && definition.AccentPrimitives.Count > 0)
            {
                var accentPen = new Pen(
                    AccentStroke,
                    Math.Max(0.5, StrokeWidth * 0.78),
                    lineCap: PenLineCap.Square,
                    lineJoin: PenLineJoin.Miter);
                foreach (var primitive in definition.AccentPrimitives)
                {
                    primitive.Draw(context, accentPen);
                }
            }
        }
    }

    private ScientificIconPalette OnSurfacePalette(ThemeDefinition theme)
    {
        var palette = theme.IconPalette;
        foreach (var host in this.GetVisualAncestors().OfType<Control>())
        {
            var brush = host switch
            {
                TemplatedControl control => control.Background,
                Border border => border.Background,
                Panel panel => panel.Background,
                _ => null
            };
            var point = this.TranslatePoint(new Point(Bounds.Width / 2, Bounds.Height / 2), host) ?? default;
            if (!IconBackgroundSampling.TrySample(brush, point, host.Bounds.Size, out var background)) continue;
            if (ScientificIconPalette.Contrast(palette.Signal, background) >= 3) return palette;
            var candidates = new List<Color>();
            if (host is TemplatedControl templated && templated.Foreground is ISolidColorBrush foreground) candidates.Add(foreground.Color);
            if (Stroke is ISolidColorBrush stroke) candidates.Add(stroke.Color);
            if (theme.Palette is { } colours)
                candidates.AddRange(new[] { colours.TextPrimary, colours.TextOnAccent, colours.Surface });
            if (host.Classes.Contains("theme-emphasized") && this.TryFindResource(
                Services.ThemeResourceBindings.SectionHeaderEmphasizedForeground, ActualThemeVariant, out var emphasis)
                && emphasis is ISolidColorBrush emphasisInk) candidates.Insert(0, emphasisInk.Color);
            var readable = candidates.FirstOrDefault(colour => ScientificIconPalette.Contrast(colour, background) >= 3);
            return ScientificIconPalette.Contrast(readable, background) >= 3
                ? ScientificIconPalette.Monochrome(readable) : palette;
        }
        return palette;
    }
}

public sealed class LocalIconLabel : StackPanel
{
    public LocalIconLabel(string iconName, string text, double iconSize = 16)
    {
        Orientation = Avalonia.Layout.Orientation.Horizontal;
        Spacing = 6;
        Children.Add(new LocalIcon
        {
            IconName = iconName,
            Width = iconSize,
            Height = iconSize,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });
        var label = new TextBlock { Text = text, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        UiLocalization.LocalizeChoiceLabel(label);
        Children.Add(label);
    }
}

public static class LocalIconLibrary
{
    public static IReadOnlyCollection<string> Names => ScientificIconLibrary.Names;

    public static bool Contains(string iconName) => ScientificIconLibrary.Contains(iconName);
}

internal sealed record IconDefinition(
    IReadOnlyList<IconPrimitive> Primitives,
    IReadOnlyList<IconPrimitive> AccentPrimitives,
    Matrix ContentTransform,
    PenLineCap LineCap,
    PenLineJoin LineJoin,
    double StrokeWidthScale,
    bool UseAliasedEdges,
    Lazy<IImage>? Image = null,
    ScientificSvgArtwork? Artwork = null)
{
    public static IconDefinition Standard(IReadOnlyList<IconPrimitive> primitives) => new(
        primitives,
        Array.Empty<IconPrimitive>(),
        Matrix.Identity,
        PenLineCap.Round,
        PenLineJoin.Round,
        1,
        false);
}

internal interface IThemeIconPack
{
    string Id { get; }

    bool TryResolve(string iconName, out IconDefinition definition);
}

internal static class ThemeIconResolver
{
    public static string PackId(Avalonia.Styling.ThemeVariant? variant) =>
        ThemeRegistry.FromActualVariant(variant).IconPack.Id;

    public static bool TryResolve(
        Avalonia.Styling.ThemeVariant? variant,
        string iconName,
        out IconDefinition definition) =>
        ThemeRegistry.FromActualVariant(variant).IconPack.TryResolve(iconName, out definition!);
}

internal abstract record IconPrimitive
{
    public abstract void Draw(DrawingContext context, Pen pen);
}

internal sealed record PathPrimitive(string PathData) : IconPrimitive
{
    private readonly Lazy<Geometry> _geometry = new(() => Geometry.Parse(PathData));

    public override void Draw(DrawingContext context, Pen pen) =>
        context.DrawGeometry(null, pen, _geometry.Value);
}

internal sealed record FilledPathPrimitive(string PathData) : IconPrimitive
{
    private readonly Lazy<Geometry> _geometry = new(() => Geometry.Parse(PathData));

    public override void Draw(DrawingContext context, Pen pen) =>
        context.DrawGeometry(pen.Brush, null, _geometry.Value);
}

internal sealed record LinePrimitive(Point Start, Point End) : IconPrimitive
{
    public override void Draw(DrawingContext context, Pen pen) => context.DrawLine(pen, Start, End);
}

internal sealed record EllipsePrimitive(Point Center, double RadiusX, double RadiusY) : IconPrimitive
{
    public override void Draw(DrawingContext context, Pen pen) =>
        context.DrawEllipse(null, pen, Center, RadiusX, RadiusY);
}

internal sealed record RectanglePrimitive(Rect Rect, double RadiusX, double RadiusY) : IconPrimitive
{
    public override void Draw(DrawingContext context, Pen pen) =>
        context.DrawRectangle(null, pen, new RoundedRect(Rect, RadiusX, RadiusY));
}

internal sealed record PolylinePrimitive(IReadOnlyList<Point> Points, bool Close) : IconPrimitive
{
    public override void Draw(DrawingContext context, Pen pen)
    {
        for (var index = 1; index < Points.Count; index++)
        {
            context.DrawLine(pen, Points[index - 1], Points[index]);
        }

        if (Close && Points.Count > 2)
        {
            context.DrawLine(pen, Points[^1], Points[0]);
        }
    }
}
