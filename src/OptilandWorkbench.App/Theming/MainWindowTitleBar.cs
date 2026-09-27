using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;

namespace OptilandWorkbench.App.Theming;

/// <summary>
/// Paints behind the native macOS caption buttons while leaving their actions and
/// title-bar hit testing with Avalonia's platform implementation.
/// </summary>
internal sealed class MainWindowTitleBar : Border
{
    internal const double CaptionHeight = 40;
    private readonly Window _window;

    private MainWindowTitleBar(Window window)
    {
        _window = window;
        Name = "MainWindowTitleBar";
        Height = CaptionHeight;
        Background = new ImmutableSolidColorBrush(BlueThemeTokens.HeaderBackground);
        BorderBrush = new ImmutableSolidColorBrush(BlueThemeTokens.HeaderDivider);
        BorderThickness = new Thickness(0, 0, 0, 1);
        var title = new TextBlock
        {
            Name = "MainWindowTitle",
            Foreground = new ImmutableSolidColorBrush(BlueThemeTokens.TitleText),
            FontWeight = FontWeight.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(128, 0),
            IsHitTestVisible = false
        };
        title.Bind(TextBlock.TextProperty, new Binding(nameof(Window.Title)) { Source = window });
        Child = title;
        WindowDecorationProperties.SetElementRole(this, WindowDecorationsElementRole.TitleBar);
        window.ActualThemeVariantChanged += OnThemeChanged;
        window.PropertyChanged += OnWindowChanged;
        window.Closed += OnWindowClosed;
        UpdateChrome();
    }

    internal static Control Wrap(Window window, Control content)
    {
        var titleBar = new MainWindowTitleBar(window);
        DockPanel.SetDock(titleBar, Avalonia.Controls.Dock.Top);
        return new DockPanel { Children = { titleBar, content } };
    }

    private void UpdateChrome()
    {
        // Pixel derives from Light, so use exact variant identity. Other platforms
        // keep their native decorations until their caption layout is verified.
        var enabled = OperatingSystem.IsMacOS() && _window.ActualThemeVariant == ThemeVariant.Light;
        _window.ExtendClientAreaTitleBarHeightHint = enabled ? CaptionHeight : -1;
        _window.ExtendClientAreaToDecorationsHint = enabled;
        IsVisible = enabled && _window.WindowState != WindowState.FullScreen;
    }

    private void OnThemeChanged(object? sender, EventArgs args) => UpdateChrome();

    private void OnWindowChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Window.WindowStateProperty) UpdateChrome();
    }

    private void OnWindowClosed(object? sender, EventArgs args)
    {
        _window.ActualThemeVariantChanged -= OnThemeChanged;
        _window.PropertyChanged -= OnWindowChanged;
        _window.Closed -= OnWindowClosed;
    }
}
