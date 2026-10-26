using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using OpticalSystemDesign.App.Controls;
using OpticalSystemDesign.App.Services;

namespace OpticalSystemDesign.App.Theming;

/// <summary>
/// Paints behind platform caption buttons; native window actions and non-client
/// hit testing stay with Avalonia. Search is an explicit interactive client area.
/// </summary>
internal sealed class MainWindowTitleBar : Border
{
    internal const double CaptionHeight = 36;
    internal const double WindowsCaptionButtonsWidth = 144;
    internal const double TrafficLightDiameter = 22;
    internal static Color MinimizeButtonColor => Color.Parse("#FFBD2E");
    internal static Color MaximizeButtonColor => Color.Parse("#28C840");
    internal static Color CloseButtonColor => Color.Parse("#FF5F57");
    internal static Color CaptionButtonGlyphColor => Color.Parse("#301C16");
    private static readonly AttachedProperty<IBrush?> TrafficLightFillProperty =
        AvaloniaProperty.RegisterAttached<MainWindowTitleBar, Button, IBrush?>("TrafficLightFill");
    private static readonly AttachedProperty<BoxShadows> TrafficLightShadowProperty =
        AvaloniaProperty.RegisterAttached<MainWindowTitleBar, Button, BoxShadows>("TrafficLightShadow");
    private readonly Window _window;
    private readonly TextBlock _title;
    private readonly Bitmap _iconBitmap;
    private ThemeVariant? _decorationVariant;
    private readonly Control? _search;

    private MainWindowTitleBar(Window window, Control? search)
    {
        _window = window;
        _search = search;
        Name = "MainWindowTitleBar";
        MinHeight = CaptionHeight;
        this.BindThemeResource(BackgroundProperty, ThemeResourceBindings.HeaderBackground);
        var layout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(search is null ? "Auto,*" : "Auto,*,216"),
            ColumnSpacing = 9,
            Margin = OperatingSystem.IsMacOS()
                ? new Thickness(96, 0, 12, 0)
                : new Thickness(12, 0, WindowsCaptionButtonsWidth, 0)
        };
        _iconBitmap = BrandAssets.LoadAppIconBitmap();
        var icon = new Image
        {
            Name = "MainWindowCaptionIcon",
            Source = _iconBitmap,
            Width = 20,
            Height = 20,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        layout.Children.Add(icon);
        _title = new TextBlock
        {
            Name = "MainWindowTitle",
            FontSize = DisplayTypography.Body,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        _title.Bind(TextBlock.TextProperty, new Binding(nameof(Window.Title)) { Source = window });
        _title.Bind(ToolTip.TipProperty, new Binding(nameof(Window.Title)) { Source = window });
        Grid.SetColumn(_title, 1);
        layout.Children.Add(_title);
        if (search is not null)
        {
            search.VerticalAlignment = VerticalAlignment.Center;
            search.Margin = new Thickness(0, 4);
            Grid.SetColumn(search, 2);
            WindowDecorationProperties.SetElementRole(search, WindowDecorationsElementRole.User);
            layout.Children.Add(search);
        }
        Child = layout;
        WindowDecorationProperties.SetElementRole(this, WindowDecorationsElementRole.TitleBar);
        window.ActualThemeVariantChanged += OnThemeChanged;
        window.PropertyChanged += OnWindowChanged;
        window.Closed += OnWindowClosed;
        PropertyChanged += OnCaptionChanged;
        UpdateChrome();
    }

    internal static Control Wrap(Window window, Control content, Control? search = null)
    {
        var titleBar = new MainWindowTitleBar(window, search);
        DockPanel.SetDock(titleBar, Avalonia.Controls.Dock.Top);
        var root = new DockPanel { Children = { titleBar, content } };
        root.Bind(MarginProperty, new Binding(nameof(Window.OffScreenMargin)) { Source = window });
        return root;
    }

    private void UpdateChrome()
    {
        var enabled = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();
        _window.ExtendClientAreaToDecorationsHint = enabled;
        IsVisible = enabled && _window.WindowState != WindowState.FullScreen;
        if (!IsVisible && _search is CommandSearchControl commandSearch) commandSearch.CloseResults();
        _window.ExtendClientAreaTitleBarHeightHint = enabled ? Math.Max(CaptionHeight, Bounds.Height) : -1;
        _title.Foreground = new SolidColorBrush(_window.ActualThemeVariant == ThemeVariant.Light
            ? BlueThemeTokens.TitleText
            : ThemeRegistry.FromActualVariant(_window.ActualThemeVariant).Palette!.TextPrimary);
        if (enabled && _decorationVariant != _window.ActualThemeVariant
            && _window.TryFindResource(typeof(WindowDrawnDecorations), out var resource)
            && resource is ControlTheme baseTheme)
        {
            var theme = new ControlTheme { TargetType = typeof(WindowDrawnDecorations), BasedOn = baseTheme };
            // Managed decoration overlays normally repeat Window.Title over client
            // content. Keep the framework's caption buttons, but draw our title once.
            theme.Children.Add(new Style(s => s.Nesting().Template().OfType<Panel>().Name("PART_TitleTextPanel")
                .Child().OfType<TextBlock>())
            { Setters = { new Setter(IsVisibleProperty, false) } });
            if (OperatingSystem.IsWindows())
            {
                AddTrafficLightButtonStyle(
                    theme,
                    "PART_MinimizeButton",
                    Color.Parse("#FFD45A"), MinimizeButtonColor,
                    Color.Parse("#FFE47C"), Color.Parse("#FFC13A"),
                    Color.Parse("#E6AA25"), Color.Parse("#D59616"));
                AddTrafficLightButtonStyle(
                    theme,
                    "PART_MaximizeButton",
                    Color.Parse("#58DD70"), MaximizeButtonColor,
                    Color.Parse("#78E98D"), Color.Parse("#37CE55"),
                    Color.Parse("#22AE3B"), Color.Parse("#168F2D"));
                AddTrafficLightButtonStyle(
                    theme,
                    "PART_CloseButton",
                    Color.Parse("#FF8179"), CloseButtonColor,
                    Color.Parse("#FF9A94"), Color.Parse("#FF6961"),
                    Color.Parse("#E4514A"), Color.Parse("#C93E38"));
                theme.Resources["CaptionButtonForeground"] = new SolidColorBrush(CaptionButtonGlyphColor);
            }
            if (_window.ActualThemeVariant == ThemeVariant.Light)
            {
                if (!OperatingSystem.IsWindows())
                {
                    theme.Resources["CaptionButtonForeground"] = new SolidColorBrush(BlueThemeTokens.TitleText);
                }
                theme.Resources["CaptionButtonBackground"] = new SolidColorBrush(BlueThemeTokens.CaptionSearchHover);
                theme.Resources["CaptionButtonBorderBrush"] = new SolidColorBrush(BlueThemeTokens.CaptionButtonPressed);
            }
            _window.WindowDecorationsTheme = theme;
            _decorationVariant = _window.ActualThemeVariant;
        }
    }

    private static void AddTrafficLightButtonStyle(
        ControlTheme theme,
        string buttonName,
        Color top,
        Color bottom,
        Color hoverTop,
        Color hoverBottom,
        Color pressedTop,
        Color pressedBottom)
    {
        var style = new Style(s => s.Nesting().Template().OfType<Button>().Name(buttonName));
        style.Setters.Add(new Setter(Button.ForegroundProperty, new SolidColorBrush(CaptionButtonGlyphColor)));
        style.Setters.Add(new Setter(TrafficLightFillProperty, VerticalGradient(top, bottom)));
        style.Setters.Add(new Setter(TrafficLightShadowProperty, ThemeChromeResources.CaptionButtonShadow));
        style.Setters.Add(new Setter(Button.TemplateProperty, CreateTrafficLightButtonTemplate()));
        theme.Children.Add(style);

        // State setters stay on the button. The disc binds to them rather than
        // crossing a second /template/ boundary, which ControlTheme rejects.
        var hoverStyle = new Style(s => s.Nesting().Template().OfType<Button>().Name(buttonName)
            .Class(":pointerover"));
        hoverStyle.Setters.Add(new Setter(TrafficLightFillProperty, VerticalGradient(hoverTop, hoverBottom)));
        hoverStyle.Setters.Add(new Setter(TrafficLightShadowProperty, ThemeChromeResources.CaptionButtonHoverShadow));
        theme.Children.Add(hoverStyle);

        var pressedStyle = new Style(s => s.Nesting().Template().OfType<Button>().Name(buttonName)
            .Class(":pressed"));
        pressedStyle.Setters.Add(new Setter(TrafficLightFillProperty, VerticalGradient(pressedTop, pressedBottom)));
        pressedStyle.Setters.Add(new Setter(TrafficLightShadowProperty, ThemeChromeResources.CaptionButtonPressedShadow));
        theme.Children.Add(pressedStyle);
    }

    private static FuncControlTemplate<Button> CreateTrafficLightButtonTemplate()
    {
        return new FuncControlTemplate<Button>((button, scope) =>
        {
            if (button.Content is Control content)
                foreach (var glyph in content.GetVisualDescendants().Prepend(content).OfType<Avalonia.Controls.Shapes.Shape>())
                {
                    glyph.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, button.GetObservable(Button.ForegroundProperty));
                    glyph.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, button.GetObservable(Button.ForegroundProperty));
                }
            var presenter = new ContentPresenter
            {
                Name = "PART_ContentPresenter",
                Background = Brushes.Transparent,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            presenter.Bind(ContentPresenter.ContentProperty, button.GetObservable(Button.ContentProperty));
            presenter.Bind(ContentPresenter.ContentTemplateProperty, button.GetObservable(Button.ContentTemplateProperty));
            presenter.Bind(ContentPresenter.ForegroundProperty, button.GetObservable(Button.ForegroundProperty));
            scope.Register("PART_ContentPresenter", presenter);
            var disc = new Border
            {
                Name = "PART_TrafficLightDisc",
                BorderBrush = new SolidColorBrush(Color.Parse("#70FFFFFF")),
                BorderThickness = new Thickness(0.75),
                Width = TrafficLightDiameter,
                Height = TrafficLightDiameter,
                CornerRadius = new CornerRadius(TrafficLightDiameter / 2),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            disc.Bind(BackgroundProperty, button.GetObservable(TrafficLightFillProperty));
            disc.Bind(BoxShadowProperty, button.GetObservable(TrafficLightShadowProperty));
            scope.Register("PART_TrafficLightDisc", disc);
            // The full caption slot remains hittable outside the smaller disc.
            return new Panel { Background = Brushes.Transparent, Children = { disc, presenter } };
        });
    }

    private static IBrush VerticalGradient(Color top, Color bottom) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = new GradientStops
        {
            new(top, 0),
            new(bottom, 1)
        }
    }.ToImmutable();

    private void OnThemeChanged(object? sender, EventArgs args) => UpdateChrome();

    private void OnWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Window.WindowStateProperty) UpdateChrome();
    }

    private void OnCaptionChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == BoundsProperty && IsVisible) UpdateChrome();
    }

    private void OnWindowClosed(object? sender, EventArgs args)
    {
        _window.ActualThemeVariantChanged -= OnThemeChanged;
        _window.PropertyChanged -= OnWindowChanged;
        _window.Closed -= OnWindowClosed;
        PropertyChanged -= OnCaptionChanged;
        _iconBitmap.Dispose();
    }
}
