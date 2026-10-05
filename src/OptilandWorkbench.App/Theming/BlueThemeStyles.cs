using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Dock.Avalonia.Controls;
using OptilandWorkbench.App.Controls;
using static OptilandWorkbench.App.Theming.BlueThemeTokens;

namespace OptilandWorkbench.App.Theming;

/// <summary>
/// Only the ordinary light variant is affected. State selectors augment the existing
/// Fluent templates; commands, selection models and keyboard handlers stay native.
/// </summary>
internal sealed class BlueThemeStyles : Styles
{
    private static IBrush B(Color color) => new ImmutableSolidColorBrush(color);
    private static Selector Light<T>(Selector? selector) where T : Control =>
        selector.OfType<T>().PropertyEquals(ThemeVariantScope.ActualThemeVariantProperty, ThemeVariant.Light);
    private static Selector Enabled(Selector selector) => selector.Not(s => s.Class(":disabled"));
    private static Selector InState(Selector selector, string states)
    {
        foreach (var state in states.Split(':', StringSplitOptions.RemoveEmptyEntries)) selector = selector.Class(":" + state);
        return selector;
    }
    private void Rule(Func<Selector?, Selector> selector, params Setter[] setters)
    {
        var style = new Style(selector);
        foreach (var setter in setters) style.Setters.Add(setter);
        Add(style);
    }

    public BlueThemeStyles()
    {
        Inputs();
        Buttons();
        Choices();
        Tables();
        Shell();
        SelectedSurfaces();
        EmphasizedText();
        // Keep the platform font and enable tabular figures where the font supports them.
        Rule(s => Light<Window>(s), new Setter(TextElement.FontFeaturesProperty, FontFeatureCollection.Parse("tnum")));
    }

    private void Inputs()
    {
        Rule(s => Light<TextBox>(s),
            new Setter(TextBox.SelectionBrushProperty, B(TextSelectionBackground)),
            new Setter(TextBox.SelectionForegroundBrushProperty, B(TextSelectionForeground)));
        Rule(s => Enabled(Light<TextBox>(s)).Class(":focus").Template().OfType<Border>().Name("PART_BorderElement"),
            new Setter(Border.BorderThicknessProperty, new Thickness(2)), new Setter(Border.BorderBrushProperty, B(FocusBorder)));
        Rule(s => Enabled(Light<TextBox>(s)).Class(":error").Template().OfType<Border>().Name("PART_BorderElement"),
            new Setter(Border.BorderBrushProperty, B(Error)), new Setter(Border.BackgroundProperty, B(ErrorBackground)));
        Rule(s => Enabled(Light<TextBox>(s)).Class(":error").Class(":focus").Template().OfType<Border>().Name("PART_BorderElement"),
            new Setter(Border.BoxShadowProperty, new BoxShadows(new BoxShadow { Color = FocusBorder, Spread = 2 })));
        Rule(s => Light<TextBox>(s).Class(":disabled").Template().OfType<Border>().Name("PART_BorderElement"),
            new Setter(Border.BackgroundProperty, B(DisabledBackground)), new Setter(Border.BorderBrushProperty, B(Divider)));
        Rule(s => Light<TextBox>(s).Class(":disabled"), new Setter(TextBox.ForegroundProperty, B(TextDisabled)));

        foreach (var state in new[] { ":focus-visible", ":dropdownopen" })
        {
            Rule(s => Enabled(Light<ComboBox>(s)).Class(state).Template().OfType<Border>().Name("Background"),
                new Setter(Border.BorderBrushProperty, B(FocusBorder)), new Setter(Border.BorderThicknessProperty, new Thickness(2)));
            // The native focus overlay otherwise hides the expanded/pressed background.
            Rule(s => Enabled(Light<ComboBox>(s)).Class(state).Template().OfType<Border>().Name("HighlightBackground"),
                new Setter(Border.BackgroundProperty, Brushes.Transparent), new Setter(Border.BorderThicknessProperty, new Thickness(2)));
        }
        Rule(s => Enabled(Light<ComboBox>(s)).Class(":error").Template().OfType<Border>().Name("Background"),
            new Setter(Border.BorderBrushProperty, B(Error)), new Setter(Border.BackgroundProperty, B(ErrorBackground)));
        Rule(s => Enabled(Light<ComboBox>(s)).Class(":error").Class(":focus-visible").Template().OfType<Border>().Name("HighlightBackground"),
            new Setter(Border.BorderBrushProperty, B(Error)),
            new Setter(Border.BoxShadowProperty, new BoxShadows(new BoxShadow { Color = FocusBorder, Spread = 2 })));
        Rule(s => Light<ComboBox>(s).Class(":disabled").Template().OfType<Border>().Name("Background"),
            new Setter(Border.BackgroundProperty, B(DisabledBackground)), new Setter(Border.BorderBrushProperty, B(Divider)));

        var focus = new FuncTemplate<Control>(() => new Border
        {
            BorderBrush = B(FocusBorder),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(5),
            Margin = new Thickness(-3),
            IsHitTestVisible = false
        });
        Rule(s => s.Is<Button>().PropertyEquals(ThemeVariantScope.ActualThemeVariantProperty, ThemeVariant.Light),
            new Setter(Control.FocusAdornerProperty, focus));
        Rule(s => Light<CheckBox>(s), new Setter(Control.FocusAdornerProperty, focus));
        Rule(s => Light<RadioButton>(s), new Setter(Control.FocusAdornerProperty, focus));
        Rule(s => Light<TabItem>(s), new Setter(Control.FocusAdornerProperty, focus));
    }

    private void Buttons()
    {
        // Explicit text/icon content does not inherit ContentPresenter.Foreground.
        Rule(s => Enabled(Light<Button>(s)).Class("accent").Descendant().OfType<TextBlock>(),
            new Setter(TextBlock.ForegroundProperty, B(PrimaryButtonText)));
        Rule(s => Enabled(Light<Button>(s)).Class("accent").Descendant().OfType<LocalIcon>(),
            new Setter(LocalIcon.StrokeProperty, B(PrimaryButtonText)));
        Rule(s => s.Is<Button>().PropertyEquals(ThemeVariantScope.ActualThemeVariantProperty, ThemeVariant.Light).Class(":disabled").Descendant().OfType<TextBlock>(),
            new Setter(TextBlock.ForegroundProperty, B(TextDisabled)));
        Rule(s => s.Is<Button>().PropertyEquals(ThemeVariantScope.ActualThemeVariantProperty, ThemeVariant.Light).Class(":disabled").Descendant().OfType<LocalIcon>(),
            new Setter(LocalIcon.StrokeProperty, B(TextDisabled)));

        foreach (var (state, background, border) in new[]
        {
            (":pointerover", HoverBackground, ControlBorder),
            (":pressed", PressedBackground, PressedBorder),
            (":flyout-open", SelectedBackground, Accent),
            (":flyout-open:pointerover", SelectedHover, Accent),
            (":flyout-open:pressed", SelectedPressed, Accent)
        })
        {
            Rule(s => InState(Enabled(s.Is<Button>().PropertyEquals(ThemeVariantScope.ActualThemeVariantProperty, ThemeVariant.Light))
                .Class("ribbon-command"), state).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
                new Setter(ContentPresenter.BackgroundProperty, B(background)), new Setter(ContentPresenter.BorderBrushProperty, B(border)),
                new Setter(ContentPresenter.BorderThicknessProperty, new Thickness(1)));
            Rule(s => InState(Enabled(Light<DropDownButton>(s)), state).Template().OfType<Border>().Name("RootBorder"),
                new Setter(Border.BackgroundProperty, B(background)), new Setter(Border.BorderBrushProperty, B(border)),
                new Setter(Border.BorderThicknessProperty, new Thickness(1)));
        }
        Rule(s => s.Is<Button>().PropertyEquals(ThemeVariantScope.ActualThemeVariantProperty, ThemeVariant.Light).Class("ribbon-command").Class(":disabled")
            .Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
            new Setter(ContentPresenter.BackgroundProperty, B(DisabledBackground)), new Setter(ContentPresenter.OpacityProperty, 1d));
        Rule(s => Light<DropDownButton>(s).Class(":disabled").Template().OfType<Border>().Name("RootBorder"),
            new Setter(Border.BackgroundProperty, B(DisabledBackground)), new Setter(Border.BorderBrushProperty, B(Divider)));
    }

    private void Choices()
    {
        // Add a selected check without changing the ComboBox selection/keyboard model.
        Rule(s => Light<ComboBoxItem>(s), new Setter(TemplatedControl.TemplateProperty,
            new FuncControlTemplate<ComboBoxItem>((item, scope) =>
            {
                var presenter = new ContentPresenter { Name = "PART_ContentPresenter", Padding = new Thickness(28, 6, 12, 6) };
                presenter.Bind(ContentPresenter.ContentProperty, item.GetObservable(ContentControl.ContentProperty), BindingPriority.Template);
                presenter.Bind(ContentPresenter.ContentTemplateProperty, item.GetObservable(ContentControl.ContentTemplateProperty), BindingPriority.Template);
                presenter.Bind(ContentPresenter.BackgroundProperty, item.GetObservable(TemplatedControl.BackgroundProperty), BindingPriority.Template);
                presenter.Bind(ContentPresenter.ForegroundProperty, item.GetObservable(TemplatedControl.ForegroundProperty), BindingPriority.Template);
                var check = new TextBlock
                {
                    Text = "✓",
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Foreground = B(Accent),
                    IsHitTestVisible = false
                };
                check.Bind(Control.IsVisibleProperty, item.GetObservable(ListBoxItem.IsSelectedProperty));
                check.Bind(TextBlock.ForegroundProperty, item.GetObservable(TemplatedControl.ForegroundProperty));
                return new Grid { Children = { presenter, check } };
            })));
        foreach (var (state, color) in new[]
        {
            (":pointerover", HoverBackground), (":pressed", PressedBackground), (":selected", SelectedBackground)
        })
            Rule(s => Enabled(Light<ListBoxItem>(s)).Class(state).Template().OfType<ContentPresenter>(),
                new Setter(ContentPresenter.BackgroundProperty, B(color)), new Setter(ContentPresenter.ForegroundProperty, B(TextPrimary)));
        Rule(s => Enabled(Light<ListBoxItem>(s)).Class(":selected").Class(":pointerover").Template().OfType<ContentPresenter>(),
            new Setter(ContentPresenter.BackgroundProperty, B(SelectedHover)));
        Rule(s => Enabled(Light<ListBoxItem>(s)).Class(":selected").Class(":pressed").Template().OfType<ContentPresenter>(),
            new Setter(ContentPresenter.BackgroundProperty, B(SelectedPressed)));
        // Disabled is final and includes combined selected/hover states.
        foreach (var type in new[] { typeof(ListBoxItem), typeof(ComboBoxItem), typeof(TreeViewItem), typeof(ToggleButton) })
        {
            Rule(s => s.OfType(type).PropertyEquals(ThemeVariantScope.ActualThemeVariantProperty, ThemeVariant.Light).Class(":disabled").Template().OfType<ContentPresenter>(),
                new Setter(ContentPresenter.BackgroundProperty, B(DisabledBackground)),
                new Setter(ContentPresenter.ForegroundProperty, B(TextDisabled)), new Setter(ContentPresenter.BorderBrushProperty, B(Divider)));
            Rule(s => s.OfType(type).PropertyEquals(ThemeVariantScope.ActualThemeVariantProperty, ThemeVariant.Light).Class(":disabled"),
                new Setter(TemplatedControl.ForegroundProperty, B(TextDisabled)));
        }
        Rule(s => Light<MenuItem>(s).Class(":disabled").Descendant().OfType<TextBlock>(),
            new Setter(TextBlock.ForegroundProperty, B(TextDisabled)));
        Rule(s => Light<MenuItem>(s).Class(":disabled").Descendant().OfType<LocalIcon>(),
            new Setter(LocalIcon.StrokeProperty, B(TextDisabled)));
    }

    private void SelectedSurfaces()
    {
        // Use existing selected/expanded states; hovering alone never selects a field.
        foreach (var (state, background) in new[]
        {
            (":pointerover", HoverBackground), (":pressed", PressedBackground),
            (":selected", SelectedBackground), (":selected:pointerover", SelectedHover),
            (":selected:pressed", SelectedPressed)
        })
            foreach (var type in new[] { typeof(DocumentTabStripItem), typeof(ToolTabStripItem) })
                Rule(s => InState(Enabled(s.OfType(type).PropertyEquals(ThemeVariantScope.ActualThemeVariantProperty, ThemeVariant.Light)), state),
                    new Setter(TemplatedControl.BackgroundProperty, B(background)));

        Rule(s => Enabled(Light<MenuItem>(s)).Class(":pressed").Template().OfType<Border>().Name("PART_LayoutRoot").Class(":pointerover"),
            new Setter(Border.BackgroundProperty, B(PressedBackground)));
        foreach (var (phase, background) in new[]
        {
            ("", SelectedBackground), (":pointerover", SelectedHover), (":pressed", SelectedPressed)
        })
        {
            Rule(s => InState(Enabled(Light<Button>(s)).Class("field-editor-header").Class("field-editor-expanded"), phase)
                .Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
                new Setter(ContentPresenter.BackgroundProperty, B(background)));
            foreach (var state in new[] { ":checked", ":open" })
                Rule(s => InState(Enabled(Light<MenuItem>(s)).Class(state), phase)
                    .Template().OfType<Border>().Name("PART_LayoutRoot"),
                    new Setter(Border.BackgroundProperty, B(background)));
        }
    }

    private void Tables()
    {
        Rule(s => Light<DataGridColumnHeader>(s), new Setter(TemplatedControl.BackgroundProperty, B(TableHeader)));
        Rule(s => Light<DataGridCell>(s), new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent),
            new Setter(TemplatedControl.FontSizeProperty, new Binding("FontSize")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(DataGrid) }
            }));
        Rule(s => Light<DataGridCell>(s).Template().OfType<Rectangle>().Name("CurrencyVisual"),
            new Setter(Shape.StrokeProperty, B(FocusBorder)), new Setter(Shape.StrokeThicknessProperty, 2d));
        foreach (var (state, color) in new[] { (":pointerover", HoverBackground), (":selected", SelectedBackground) })
            Rule(s => Enabled(Light<DataGridRow>(s)).Class(state).Template().OfType<Rectangle>().Name("BackgroundRectangle"),
                new Setter(Shape.FillProperty, B(color)), new Setter(Visual.OpacityProperty, 1d));
        foreach (var focus in new[] { false, true })
            Rule(s =>
            {
                var row = Enabled(Light<DataGridRow>(s)).Class(":selected").Class(":pointerover");
                if (focus) row = row.Class(":focus");
                return row.Template().OfType<Rectangle>().Name("BackgroundRectangle");
            }, new Setter(Shape.FillProperty, B(SelectedHover)), new Setter(Visual.OpacityProperty, 1d));
        // Fluent's opaque rectangle sits above DataGridRow.Background. Paint that
        // actual surface, without treating material presence as a selection.
        foreach (var (state, color) in new[] { ("", MaterialRowBackground), (":pointerover", MaterialRowHoverBackground) })
            Rule(s => InState(Enabled(Light<DataGridRow>(s)).Class("glass-material-row")
                    .Not(s => s.Class(":selected")).Not(s => s.Class(":invalid")), state)
                .Template().OfType<Rectangle>().Name("BackgroundRectangle"),
                new Setter(Shape.FillProperty, B(color)), new Setter(Visual.OpacityProperty, 1d));
        Rule(s => Light<DataGridRow>(s).Class(":disabled").Template().OfType<Rectangle>().Name("BackgroundRectangle"),
            new Setter(Shape.FillProperty, B(DisabledBackground)), new Setter(Visual.OpacityProperty, 1d));
        Rule(s => Light<DataGridRow>(s).Class(":disabled").Descendant().OfType<TextBlock>(),
            new Setter(TextBlock.ForegroundProperty, B(TextDisabled)));
    }

    private void Shell()
    {
        Rule(s => Light<TabControl>(s).Class("ribbon-tabs").Template().OfType<ItemsPresenter>().Name("PART_ItemsPresenter"),
            new Setter(Panel.BackgroundProperty, B(HeaderBackground)),
            new Setter(Control.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
        Rule(s => Light<TabItem>(s).Class("ribbon-tab"),
            new Setter(TemplatedControl.ForegroundProperty, B(HeaderMenuText)), new Setter(TemplatedControl.BackgroundProperty, B(HeaderBackground)));
        Rule(s => Light<TabItem>(s).Class("ribbon-tab").Template().OfType<Border>().Name("PART_LayoutRoot"),
            new Setter(Border.BackgroundProperty, B(HeaderBackground)), new Setter(Border.CornerRadiusProperty, new CornerRadius(0)));
        foreach (var (state, background) in new[]
        {
            (":pointerover", HeaderHoverBackground),
            (":selected", HeaderPressedBackground),
            (":selected:pointerover", HeaderPressedBackground),
            (":pressed", HeaderPressedBackground)
        })
        {
            Rule(s =>
            {
                var item = Enabled(Light<TabItem>(s)).Class("ribbon-tab");
                foreach (var value in state.Split(':', StringSplitOptions.RemoveEmptyEntries)) item = item.Class(":" + value);
                return item.Template().OfType<Border>().Name("PART_LayoutRoot");
            }, new Setter(Border.BackgroundProperty, B(background)), new Setter(TextElement.ForegroundProperty, B(HeaderMenuText)));
        }
        Rule(s => Light<TabItem>(s).Class("ribbon-tab").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
            new Setter(ContentPresenter.ForegroundProperty, B(HeaderMenuText)));
        Rule(s => Light<TabItem>(s).Class("ribbon-tab").Template().OfType<Border>().Name("PART_SelectedPipe"),
            new Setter(Visual.IsVisibleProperty, false));
        Rule(s => Light<TabItem>(s).Class("ribbon-tab").Class(":disabled").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
            new Setter(ContentPresenter.ForegroundProperty, B(HeaderMuted)));
        Rule(s => Light<Border>(s).Class("system-property-card"),
            new Setter(Border.BorderBrushProperty, B(Divider)), new Setter(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1)),
            new Setter(Border.CornerRadiusProperty, new CornerRadius(0)));
        Rule(s => Light<Button>(s).Class("system-property-card-header").Template().OfType<ContentPresenter>(),
            new Setter(ContentPresenter.CornerRadiusProperty, new CornerRadius(0)));
        // Connect outer sections on one white surface. Keep the existing section spacing,
        // header hit area and text alignment; only inset the painted hover/pressed surface.
        Rule(s => Light<StackPanel>(s).Class("system-property-sections"),
            new Setter(Panel.BackgroundProperty, B(Surface)));
        Rule(s => Light<Border>(s).Class("system-property-section"),
            new Setter(Border.BorderThicknessProperty, new Thickness(0)));
        Rule(s => Light<Border>(s).Class("system-section-divider"),
            new Setter(Visual.IsVisibleProperty, true), new Setter(Border.BackgroundProperty, B(Divider)));
        Rule(s => Light<Button>(s).Class("system-section-header").Template().OfType<ContentPresenter>(),
            new Setter(ContentPresenter.CornerRadiusProperty, ThemeChromeResources.StandardSectionHeaderCornerRadius),
            // Match the content's right inset so the overlay scrollbar cannot clip the radius.
            new Setter(Control.MarginProperty, new Thickness(4, 0, 12, 0)),
            new Setter(ContentPresenter.PaddingProperty, new Thickness(6, 0)));
        Rule(s => Enabled(Light<MenuItem>(s)).Class("ribbon-menu-item").Class(":pointerover").Template().OfType<Border>().Name("PART_LayoutRoot"),
            new Setter(Border.BackgroundProperty, B(HoverBackground)), new Setter(TextElement.ForegroundProperty, B(TextPrimary)));
    }

    private void EmphasizedText()
    {
        // Foregrounds only: keep existing chrome, selection and native input behavior.
        // Pressed is transient and follows hover in this list so release restores hover.
        foreach (var (state, color) in new[]
        {
            ("", EmphasisText), (":pointerover", EmphasisTextHover), (":pressed", EmphasisTextPressed)
        })
        {
            Selector Phase(Selector selector) => state.Length == 0 ? selector : selector.Class(state);
            foreach (var kind in new[] { "system-section-header", "field-editor-header" })
            {
                Selector Header(Selector? s)
                {
                    var header = Enabled(Light<Button>(s)).Class(kind);
                    // The existing field editor emphasizes expanded or hovered cards.
                    if (kind == "field-editor-header") header = header.Class("theme-emphasized");
                    return Phase(header);
                }
                Rule(s => Header(s).Descendant().OfType<TextBlock>().Class("emphasis-label"),
                    new Setter(TextBlock.ForegroundProperty, B(color)));
                Rule(s => Header(s).Descendant().OfType<LocalIcon>().Class("emphasis-arrow"),
                    new Setter(LocalIcon.StrokeProperty, B(color)));
            }

            Rule(s => Phase(Enabled(Light<TabItem>(s)).Not(s => s.Class("ribbon-tab")).Class(":selected"))
                .Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
                new Setter(ContentPresenter.ForegroundProperty, B(color)));
            foreach (var type in new[] { typeof(DocumentTabStripItem), typeof(ToolTabStripItem) })
            {
                Rule(s => Phase(Enabled(s.OfType(type).PropertyEquals(ThemeVariantScope.ActualThemeVariantProperty, ThemeVariant.Light)).Class(":selected"))
                    .Template().OfType<ContentPresenter>().Name("PART_HeaderPresenter"),
                    new Setter(ContentPresenter.ForegroundProperty, B(color)));
                // Dock's data-template TextBlock has an application style of its own,
                // so presenter inheritance alone does not reach the visible title.
                Rule(s => Phase(Enabled(s.OfType(type).PropertyEquals(ThemeVariantScope.ActualThemeVariantProperty, ThemeVariant.Light)).Class(":selected"))
                    .Template().OfType<ContentPresenter>().Name("PART_HeaderPresenter").Descendant().OfType<TextBlock>(),
                    new Setter(TextBlock.ForegroundProperty, B(color)));
            }

            foreach (var selection in new[] { ":selected", ":checked", ":open" })
            {
                Selector Menu(Selector? s) => Phase(Enabled(Light<MenuItem>(s)).Class(selection));
                Rule(s => Menu(s).Template().OfType<ContentPresenter>().Name("PART_HeaderPresenter"),
                    new Setter(ContentPresenter.ForegroundProperty, B(color)));
                Rule(s => Menu(s).Child().OfType<LocalIconLabel>().Child().OfType<TextBlock>().Class("ribbon-menu-label"),
                    new Setter(TextBlock.ForegroundProperty, B(color)));
                Rule(s => Menu(s).Template().OfType<Avalonia.Controls.Shapes.Path>().Name("PART_ChevronPath"),
                    new Setter(Shape.FillProperty, B(color)));
            }
        }

        // Explicit content has its own foreground; disabled must also beat expanded state.
        Rule(s => Light<Button>(s).Class("system-property-card-header").Class(":disabled")
            .Descendant().OfType<TextBlock>().Class("emphasis-label"),
            new Setter(TextBlock.ForegroundProperty, B(TextDisabled)));
        Rule(s => Light<Button>(s).Class("system-property-card-header").Class(":disabled")
            .Descendant().OfType<LocalIcon>().Class("emphasis-arrow"),
            new Setter(LocalIcon.StrokeProperty, B(TextDisabled)));
    }
}
