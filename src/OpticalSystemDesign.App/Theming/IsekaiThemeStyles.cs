using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Dock.Avalonia.Controls;
using Dock.Controls.DeferredContentControl;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.App.Theming;

/// <summary>Material on existing Fluent bodies; all command and Dock behavior stays native.</summary>
internal sealed class IsekaiThemeStyles : Styles
{
    internal static readonly Color LockedSettingInk = Color.Parse("#A0A0A0");
    private static readonly IBrush LockedSettingSurface = new ImmutableSolidColorBrush(Color.Parse("#363636"));
    private static readonly IBrush LockedSettingBorder = new ImmutableSolidColorBrush(Color.Parse("#626262"));

    internal IsekaiThemeStyles()
    {
        // Popup typography uses the same dynamic resources as all other themes.
        LockedSettingStyles();
        // A scientific page must not be composited at the deferred host's
        // 85% reveal opacity. Keep lazy creation, with an opaque first frame.
        Add(new Style(s => Isekai<DocumentControl>(s))
        {
            Setters = { new Setter(DeferredContentScheduling.TimelineProperty,
                new DeferredContentPresentationTimeline { RevealDuration = TimeSpan.Zero }) }
        });
        var paperInk = new SolidColorBrush(Color.Parse("#513723"));
        Add(new Style(s => Command(s))
        { Setters = { new Setter(Button.ForegroundProperty, paperInk) } });
        CommandTextStyles(paperInk, disabled: false);
        Add(new Style(s => Command(s).Child().OfType<LocalIcon>())
        { Setters = { new Setter(LocalIcon.StrokeProperty, new SolidColorBrush(IsekaiTheme.Palette.TextOnAccent)) } });
        Add(new Style(s => Command(s).Child().OfType<LocalIconLabel>().Child().OfType<LocalIcon>())
        { Setters = { new Setter(LocalIcon.StrokeProperty, new SolidColorBrush(IsekaiTheme.Palette.TextOnAccent)) } });
        DocumentTextStyles(new SolidColorBrush(Color.Parse("#F4ECE5")), selected: false);
        DocumentTextStyles(paperInk, selected: true);
        DocumentTextStyles(new SolidColorBrush(IsekaiTheme.Palette.TextDisabled), selected: null);
        foreach (var (selected, hovered, state) in new[]
        {
            (false, false, 0), (false, true, 3), (true, false, 1), (true, true, 1)
        })
        {
            Add(new Style(s =>
            {
                var tab = Isekai<DocumentTabStripItem>(s);
                tab = selected ? tab.Class(":selected") : tab.Not(x => x.Class(":selected"));
                tab = hovered ? tab.Class(":pointerover") : tab.Not(x => x.Class(":pointerover"));
                return tab.Template().OfType<Border>().Name("PART_TabBody");
            })
            {
                Setters = { new Setter(Border.BackgroundProperty, IsekaiCraftsmanship.PlaqueBrush(state)),
                    new Setter(Border.BorderBrushProperty, Brushes.Transparent) }
            });
        }
        foreach (var hovered in new[] { false, true })
        {
            Add(new Style(s =>
            {
                var command = Command(s);
                command = hovered ? command.Class(":pointerover") : command.Not(x => x.Class(":pointerover"));
                return command.Template().OfType<ContentPresenter>().Name("PART_ContentPresenter");
            }) { Setters =
            {
                new Setter(ContentPresenter.BackgroundProperty, hovered ? new SolidColorBrush(Color.Parse("#2071502C")) : Brushes.Transparent),
                new Setter(ContentPresenter.ForegroundProperty, paperInk),
                new Setter(ContentPresenter.BorderBrushProperty, Brushes.Transparent)
            } });
            Add(new Style(s =>
            {
                var command = Command(s);
                command = hovered ? command.Class(":pointerover") : command.Not(x => x.Class(":pointerover"));
                return command.Template().OfType<Border>().Name("RootBorder");
            }) { Setters =
            {
                new Setter(Border.BackgroundProperty, hovered ? new SolidColorBrush(Color.Parse("#2071502C")) : Brushes.Transparent),
                new Setter(Border.BorderBrushProperty, Brushes.Transparent)
            } });
        }
        var disabledInk = new SolidColorBrush(Color.Parse("#A18A68"));
        Add(new Style(s => Command(s).Class(":disabled"))
        { Setters = { new Setter(Button.ForegroundProperty, disabledInk) } });
        CommandTextStyles(disabledInk, disabled: true);
        Add(new Style(s => Command(s).Class(":disabled")
            .Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
        { Setters = { new Setter(ContentPresenter.ForegroundProperty, disabledInk) } });
    }

    private void LockedSettingStyles()
    {
        var ink = new SolidColorBrush(LockedSettingInk);
        // The marker follows the selected Isekai policy, including its preview
        // in an otherwise unchanged settings dialog. Other themes never add it.
        Selector Locked(Selector? s) => s.Is<Control>().Class(ThemeRegistry.LockedSettingClass).Class(":disabled");
        Add(new Style(s => Locked(s)) { Setters =
        {
            new Setter(TemplatedControl.ForegroundProperty, ink)
        } });
        Add(new Style(s => Locked(s).Descendant().OfType<TextBlock>())
        { Setters = { new Setter(TextBlock.ForegroundProperty, ink) } });
        Add(new Style(s => Locked(s).Descendant().OfType<LocalIcon>())
        { Setters = { new Setter(LocalIcon.DisabledColorProperty, LockedSettingInk) } });
        Add(new Style(s => s.OfType<ComboBox>().Class(ThemeRegistry.LockedSettingClass).Class(":disabled")
            .Template().OfType<Border>().Name("Background"))
        { Setters = { new Setter(Border.BackgroundProperty, LockedSettingSurface), new Setter(Border.BorderBrushProperty, LockedSettingBorder) } });
        Add(new Style(s => s.OfType<ComboBox>().Class(ThemeRegistry.LockedSettingClass).Class(":disabled")
            .Template().OfType<Avalonia.Controls.Shapes.Path>())
        { Setters = { new Setter(Shape.FillProperty, ink) } });
        Add(new Style(s => s.OfType<DropDownButton>().Class(ThemeRegistry.LockedSettingClass).Class(":disabled")
            .Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
        { Setters =
        {
            new Setter(ContentPresenter.BackgroundProperty, LockedSettingSurface),
            new Setter(ContentPresenter.BorderBrushProperty, LockedSettingBorder),
            new Setter(ContentPresenter.ForegroundProperty, ink)
        } });
    }
    private void DocumentTextStyles(IBrush ink, bool? selected)
    {
        Selector Tab(Selector? s)
        {
            var tab = Isekai<DocumentTabStripItem>(s);
            if (selected is null) return tab.Class(":disabled");
            tab = tab.Not(x => x.Class(":disabled"));
            return selected.Value ? tab.Class(":selected") : tab.Not(x => x.Class(":selected"));
        }
        Add(new Style(s => Tab(s).Template().OfType<ContentPresenter>().Name("PART_HeaderPresenter"))
        { Setters = { new Setter(ContentPresenter.ForegroundProperty, ink) } });
        Add(new Style(s => Tab(s).Template().OfType<ContentPresenter>().Name("PART_HeaderPresenter").Descendant().OfType<TextBlock>())
        { Setters = { new Setter(TextBlock.ForegroundProperty, ink) } });
    }
    private void CommandTextStyles(IBrush ink, bool disabled)
    {
        Selector Button(Selector? s) => disabled ? Command(s).Class(":disabled") : Command(s);
        // Restrict ink to the command's own content/presenter. A flyout can be
        // a logical descendant of its button while retaining a dark surface.
        Add(new Style(s => Button(s).Child().OfType<TextBlock>())
        { Setters = { new Setter(TextBlock.ForegroundProperty, ink) } });
        Add(new Style(s => Button(s).Child().OfType<LocalIconLabel>().Child().OfType<TextBlock>())
        { Setters = { new Setter(TextBlock.ForegroundProperty, ink) } });
        Add(new Style(s => Button(s).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter")
            .Descendant().OfType<TextBlock>())
        { Setters = { new Setter(TextBlock.ForegroundProperty, ink) } });
    }
    private static Selector Command(Selector? s) => Isekai<WrapPanel>(s).Class("analysis-actions").Child().Is<Button>();
    private static Selector Isekai<T>(Selector? s) where T : Control => s.OfType<T>()
        .PropertyEquals(ThemeVariantScope.ActualThemeVariantProperty, IsekaiTheme.Variant);
}
