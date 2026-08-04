using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Dock.Avalonia.Controls;
using Dock.Avalonia.Themes.Fluent;
using OptilandWorkbench.App.Controls;
using OptilandWorkbench.App.Services;
using OptilandWorkbench.App.Theming;

namespace OptilandWorkbench.App;

public sealed class App : Avalonia.Application
{
    public override void Initialize()
    {
        Name = "Optical System Design";
        Styles.Add(new FluentTheme());
        Styles.Add(new DockFluentTheme());
        ApplyBlueAccent();
        AddThemeResources();
        Styles.Add(new Style(selector => selector.OfType<LocalIcon>())
        {
            Setters =
            {
                new Setter(
                    LocalIcon.StrokeProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.MutedText))
            }
        });
        DataTemplates.Add(new WorkspaceViewLocator());
        Styles.Add(new StyleInclude(new Uri("avares://Avalonia.Controls.DataGrid"))
        {
            Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml")
        });
        Styles.Add(new Style(selector => selector.OfType<TextBlock>())
        {
            Setters =
            {
                new Setter(
                    TextBlock.ForegroundProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.TextPrimary))
            }
        });
        Styles.Add(new Style(selector => selector.OfType<Label>())
        {
            Setters =
            {
                new Setter(
                    TextElement.ForegroundProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.TextSecondary))
            }
        });
        Styles.Add(new Style(selector => selector.OfType<DataGrid>())
        {
            Setters =
            {
                new Setter(
                    TextElement.ForegroundProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.TextPrimary))
            }
        });

        Styles.Add(new Style(selector => selector.OfType<Button>())
        {
            Setters =
            {
                new Setter(Button.MinHeightProperty, 29d),
                new Setter(Button.PaddingProperty, new Thickness(10, 4)),
                new Setter(Button.CornerRadiusProperty, new CornerRadius(5)),
                new Setter(Button.BorderThicknessProperty, new Thickness(1))
            }
        });
        Styles.Add(new Style(selector => selector
            .OfType<DropDownButton>()
            .Class("ribbon-dropdown")
            .Template()
            .OfType<PathIcon>())
        {
            Setters =
            {
                new Setter(PathIcon.IsVisibleProperty, false)
            }
        });
        Styles.Add(new Style(RibbonCommandPointerOverSelector)
        {
            Setters =
            {
                new Setter(
                    ContentPresenter.BackgroundProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.RibbonHover)),
                new Setter(
                    ContentPresenter.BorderBrushProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.RibbonHoverBorder))
            }
        });
        Styles.Add(new Style(RibbonTabPointerOverSelector)
        {
            Setters =
            {
                new Setter(
                    Border.BackgroundProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.RibbonTabHover)),
                new Setter(
                    TextElement.ForegroundProperty,
                    new DynamicResourceExtension("AccentFillColorDefaultBrush")),
                new Setter(Border.CornerRadiusProperty, SettingsPanelChrome.ControlCornerRadius)
            }
        });
        Styles.Add(new Style(RibbonMenuItemPointerOverSelector)
        {
            Setters =
            {
                new Setter(
                    Border.BackgroundProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.RibbonTabHover)),
                new Setter(Border.BorderBrushProperty, Brushes.Transparent),
                new Setter(Border.BorderThicknessProperty, new Thickness(0)),
                new Setter(
                    TextElement.ForegroundProperty,
                    new DynamicResourceExtension("AccentFillColorDefaultBrush")),
                new Setter(Border.CornerRadiusProperty, SettingsPanelChrome.ControlCornerRadius)
            }
        });
        Styles.Add(new Style(selector => selector.OfType<TextBox>())
        {
            Setters =
            {
                new Setter(TextBox.MinHeightProperty, 29d),
                new Setter(TextBox.CornerRadiusProperty, new CornerRadius(5))
            }
        });
        Styles.Add(new Style(selector => selector.OfType<ComboBox>())
        {
            Setters =
            {
                new Setter(ComboBox.MinHeightProperty, 29d),
                new Setter(ComboBox.CornerRadiusProperty, new CornerRadius(5))
            }
        });
        Styles.Add(new Style(selector => selector.OfType<NumericUpDown>())
        {
            Setters =
            {
                new Setter(NumericUpDown.ShowButtonSpinnerProperty, false),
                new Setter(NumericUpDown.MinHeightProperty, 29d),
                new Setter(NumericUpDown.CornerRadiusProperty, new CornerRadius(5))
            }
        });
        Styles.Add(new Style(selector => selector.OfType<DataGridColumnHeader>())
        {
            Setters =
            {
                new Setter(DataGridColumnHeader.BorderThicknessProperty, new Thickness(0, 0, 1, 1)),
                new Setter(DataGridColumnHeader.PaddingProperty, new Thickness(8, 3))
            }
        });
        Styles.Add(new Style(DataGridSelectedRowSelector)
        {
            Setters =
            {
                new Setter(
                    DataGridRow.BackgroundProperty,
                    new DynamicResourceExtension("AccentFillColorDefaultBrush")),
                new Setter(
                    DataGridRow.BorderBrushProperty,
                    new DynamicResourceExtension("AccentFillColorDefaultBrush")),
                new Setter(TextElement.ForegroundProperty, new DynamicResourceExtension(ThemeResourceBindings.TextOnAccent))
            }
        });
        AddDockIconStyles();
    }

    internal static Selector RibbonCommandPointerOverSelector(Selector? selector) => selector
        .Is<Button>()
        .Class("ribbon-command")
        .Class(":pointerover")
        .Template()
        .OfType<ContentPresenter>()
        .Name("PART_ContentPresenter");

    internal static Selector RibbonTabPointerOverSelector(Selector? selector) => selector
        .OfType<TabItem>()
        .Class("ribbon-tab")
        .Class(":pointerover")
        .Template()
        .OfType<Border>()
        .Name("PART_LayoutRoot");

    internal static Selector RibbonMenuItemPointerOverSelector(Selector? selector) => selector
        .OfType<MenuItem>()
        .Class("ribbon-menu-item")
        .Class(":pointerover")
        .Template()
        .OfType<Border>()
        .Name("PART_LayoutRoot");

    internal static Selector DataGridSelectedRowSelector(Selector? selector) => selector
        .OfType<DataGridRow>()
        .Class(":selected");

    internal static Color BrandAccentColor => Color.FromRgb(0, 122, 255);

    internal static IReadOnlyList<string> UnifiedDockAccentResourceKeys { get; } =
        new[]
        {
            "DockSurfaceHeaderActiveBrush",
            "DockTabActiveBackgroundBrush",
            "DockTabActiveIndicatorBrush",
            "DockTargetIndicatorBrush",
            "DockSplitterDragBrush"
        };

    internal void ApplyThemeAccent(string settingsValue)
    {
        if (settingsValue == IsekaiTheme.SettingsValue)
        {
            IsekaiTheme.ApplyAccentResources(Resources);
            return;
        }

        ApplyBlueAccent();
    }

    private void ApplyBlueAccent()
    {
        var accent = BrandAccentColor;
        var accentBrush = new SolidColorBrush(accent);
        Resources["SystemAccentColor"] = accent;
        Resources["SystemAccentColorDark1"] = Color.FromRgb(0, 102, 204);
        Resources["SystemAccentColorDark2"] = Color.FromRgb(0, 82, 164);
        Resources["SystemAccentColorDark3"] = Color.FromRgb(0, 64, 128);
        Resources["SystemAccentColorLight1"] = Color.FromRgb(64, 156, 255);
        Resources["SystemAccentColorLight2"] = Color.FromRgb(128, 190, 255);
        Resources["SystemAccentColorLight3"] = Color.FromRgb(204, 229, 255);
        Resources["AccentFillColorDefaultBrush"] = accentBrush;
        Resources["AccentFillColorSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0, 112, 235));
        Resources["AccentFillColorTertiaryBrush"] = new SolidColorBrush(Color.FromRgb(0, 102, 214));
        foreach (var resourceKey in UnifiedDockAccentResourceKeys)
        {
            Resources[resourceKey] = accentBrush;
        }

        Resources["DockTabActiveForegroundBrush"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));
        Resources["DockDocumentTabSelectedForegroundBrush"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));
        Resources["DockDocumentTabCloseSelectedForegroundBrush"] = new SolidColorBrush(Color.FromRgb(255, 255, 255));
    }

    private void AddThemeResources()
    {
        Resources.ThemeDictionaries[ThemeVariant.Light] = ThemePalette.Light.ToResourceDictionary();
        Resources.ThemeDictionaries[ThemeVariant.Dark] = ThemePalette.DarkOpticStudio.ToResourceDictionary();
        Resources.ThemeDictionaries[IsekaiTheme.Variant] = IsekaiTheme.CreateResourceDictionary();
    }
    private void AddDockIconStyles()
    {
        var documentCloseStyle = new Style(selector => selector
            .OfType<DocumentTabStripItem>()
            .Descendant()
            .OfType<Button>());
        AddDockButtonSetters(documentCloseStyle, 18, 2, new Thickness(0));
        Styles.Add(documentCloseStyle);
        AddDockChromeButtonStyle("PART_MenuButton");
        AddDockChromeButtonStyle("PART_PinButton");
        AddDockChromeButtonStyle("PART_MaximizeRestoreButton");
        AddDockChromeButtonStyle("PART_CloseButton");
    }

    private void AddDockChromeButtonStyle(string partName)
    {
        var style = new Style(selector => selector
            .OfType<ToolChromeControl>()
            .Template()
            .OfType<Button>()
            .Name(partName));
        AddDockButtonSetters(style, 22, 4, new Thickness(2, 0));
        Styles.Add(style);
    }

    private static void AddDockButtonSetters(
        Style style,
        double size,
        double padding,
        Thickness margin)
    {
        style.Setters.Add(new Setter(Button.WidthProperty, size));
        style.Setters.Add(new Setter(Button.HeightProperty, size));
        style.Setters.Add(new Setter(Button.MinWidthProperty, size));
        style.Setters.Add(new Setter(Button.MinHeightProperty, size));
        style.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(padding)));
        style.Setters.Add(new Setter(Button.MarginProperty, margin));
        style.Setters.Add(new Setter(Button.CornerRadiusProperty, new CornerRadius(5)));
        style.Setters.Add(new Setter(Button.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Button.BorderBrushProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(0)));
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var captureRequest = GuiAnalysisCaptureRequest.Parse(
                Environment.GetCommandLineArgs().Skip(1));
            if (captureRequest is not null)
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Dispatcher.UIThread.Post(
                    () => _ = GuiAnalysisCaptureRunner.RunAndShutdownAsync(
                        desktop,
                        captureRequest),
                    DispatcherPriority.Background);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var splash = new SplashWindow();
            desktop.MainWindow = splash;
            MacOsBranding.TryApplyApplicationIcon();
            splash.ReportProgress(12, "正在初始化应用...");
            splash.Show();
            Dispatcher.UIThread.Post(
                () => OpenMainWindowAsync(desktop, splash),
                DispatcherPriority.Background);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async void OpenMainWindowAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        SplashWindow splash)
    {
        var minimumDisplay = Task.Delay(900);
        try
        {
            splash.ReportProgress(28, "正在创建主工作区...");
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var mainWindow = new MainWindow
            {
                Opacity = 0,
                ShowInTaskbar = false
            };
            EventHandler? readyHandler = null;
            readyHandler = (_, _) =>
            {
                mainWindow.StartupCompleted -= readyHandler;
                splash.ReportProgress(92, "正在完成界面准备...");
                ready.TrySetResult();
            };
            mainWindow.StartupCompleted += readyHandler;
            desktop.MainWindow = mainWindow;
            mainWindow.Show();
            splash.ReportProgress(58, "正在恢复工作区与分析页面...");

            await Task.WhenAll(minimumDisplay, ready.Task);
            splash.Complete();
            await Task.Delay(120);
            mainWindow.ShowInTaskbar = true;
            mainWindow.Opacity = 1;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            splash.Close();
            mainWindow.Activate();
        }
        catch
        {
            splash.Close();
            desktop.Shutdown(-1);
            throw;
        }
    }
}
