using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using OpticalSystemDesign.App.Services;
using OpticalSystemDesign.App.Controls;

namespace OpticalSystemDesign.App.Theming;

/// <summary>
/// Paints behind platform caption buttons; native window actions and non-client
/// hit testing stay with Avalonia. Search is an explicit interactive client area.
/// </summary>
internal sealed class MainWindowTitleBar : Border
{
    internal const double CaptionHeight = 36;
    internal const double WindowsCaptionButtonsWidth = 144;
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
            if (_window.ActualThemeVariant == ThemeVariant.Light)
            {
                theme.Resources["CaptionButtonForeground"] = new SolidColorBrush(BlueThemeTokens.TitleText);
                theme.Resources["CaptionButtonBackground"] = new SolidColorBrush(BlueThemeTokens.CaptionSearchHover);
                theme.Resources["CaptionButtonBorderBrush"] = new SolidColorBrush(BlueThemeTokens.CaptionButtonPressed);
            }
            _window.WindowDecorationsTheme = theme;
            _decorationVariant = _window.ActualThemeVariant;
        }
    }

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
