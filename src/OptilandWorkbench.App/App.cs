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
        AddThemeResources();
        ThemeApplicationService.Apply(this, AppSettings.DefaultTheme);
        Styles.Add(new Style(selector => selector.OfType<LocalIcon>())
        {
            Setters =
            {
                new Setter(
                    LocalIcon.StrokeProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.MutedText)),
                new Setter(
                    LocalIcon.AccentStrokeProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.TextAccent))
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
                    new DynamicResourceExtension(ThemeResourceBindings.TextPrimary)),
                new Setter(DataGrid.RowHeightProperty, UiDensity.CompactTableRowHeight),
                new Setter(DataGrid.ColumnHeaderHeightProperty, UiDensity.TableHeaderHeight)
            }
        });

        Styles.Add(new Style(selector => selector.OfType<Button>())
        {
            Setters =
            {
                new Setter(Button.MinHeightProperty, UiDensity.StandardControlHeight),
                new Setter(Button.PaddingProperty, new Thickness(10, 4)),
                new Setter(
                    Button.CornerRadiusProperty,
                    new DynamicResourceExtension(ThemeChromeResources.CornerRadius(ThemeChromeRole.ControlFrame))),
                new Setter(
                    Button.BorderThicknessProperty,
                    new DynamicResourceExtension(ThemeChromeResources.BorderThickness(ThemeChromeRole.ControlFrame)))
            }
        });
        Styles.Add(new Style(selector => selector
            .OfType<Button>()
            .Class("system-property-card-header"))
        {
            Setters =
            {
                new Setter(
                    Button.BackgroundProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.Surface)),
                new Setter(
                    Button.BorderBrushProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.Border))
            }
        });
        Styles.Add(new Style(selector => selector
            .OfType<Button>()
            .Class("system-property-card-header")
            .Class("theme-emphasized"))
        {
            Setters =
            {
                new Setter(
                    Button.BackgroundProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.Hover)),
                new Setter(
                    Button.BorderBrushProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.HoverBorder))
            }
        });
        Styles.Add(new Style(selector => selector
            .OfType<Border>()
            .Class("system-property-card"))
        {
            Setters =
            {
                new Setter(
                    Border.BorderBrushProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.Border))
            }
        });
        Styles.Add(new Style(selector => selector
            .OfType<Border>()
            .Class("system-property-card")
            .Class("theme-emphasized"))
        {
            Setters =
            {
                new Setter(
                    Border.BorderBrushProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.HoverBorder))
            }
        });
        Styles.Add(new Style(selector => selector
            .OfType<LocalIcon>()
            .Class("theme-emphasized"))
        {
            Setters =
            {
                new Setter(
                    LocalIcon.StrokeProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.TextAccent))
            }
        });
        Styles.Add(new Style(selector => selector
            .OfType<TextBlock>()
            .Class("theme-emphasized"))
        {
            Setters =
            {
                new Setter(
                    TextBlock.ForegroundProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.TextAccent))
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
                new Setter(
                    Border.CornerRadiusProperty,
                    new DynamicResourceExtension(ThemeChromeResources.CornerRadius(ThemeChromeRole.ControlFrame)))
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
                new Setter(
                    Border.CornerRadiusProperty,
                    new DynamicResourceExtension(ThemeChromeResources.CornerRadius(ThemeChromeRole.ControlFrame)))
            }
        });
        Styles.Add(new Style(selector => selector.OfType<TextBox>())
        {
            Setters =
            {
                new Setter(TextBox.MinHeightProperty, UiDensity.StandardControlHeight),
                new Setter(
                    TextBox.CornerRadiusProperty,
                    new DynamicResourceExtension(ThemeChromeResources.CornerRadius(ThemeChromeRole.ControlFrame)))
            }
        });
        Styles.Add(new Style(selector => selector.OfType<ComboBox>())
        {
            Setters =
            {
                new Setter(ComboBox.MinHeightProperty, UiDensity.StandardControlHeight),
                new Setter(
                    ComboBox.CornerRadiusProperty,
                    new DynamicResourceExtension(ThemeChromeResources.CornerRadius(ThemeChromeRole.ControlFrame)))
            }
        });
        Styles.Add(new Style(selector => selector.OfType<NumericUpDown>())
        {
            Setters =
            {
                new Setter(NumericUpDown.MinHeightProperty, UiDensity.StandardControlHeight),
                new Setter(
                    NumericUpDown.CornerRadiusProperty,
                    new DynamicResourceExtension(ThemeChromeResources.CornerRadius(ThemeChromeRole.ControlFrame)))
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

    internal static Color BrandAccentColor => StandardTheme.AccentColor;

    internal static IReadOnlyList<string> UnifiedDockAccentResourceKeys { get; } =
        StandardTheme.UnifiedDockAccentResourceKeys;

    internal void ApplyTheme(string settingsValue)
    {
        ThemeApplicationService.Apply(this, settingsValue);
    }

    private void AddThemeResources()
    {
        foreach (var theme in ThemeRegistry.ConcreteThemes)
        {
            Resources.ThemeDictionaries[theme.RequestedVariant] = theme.BuildResources();
        }
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
        style.Setters.Add(new Setter(
            Button.CornerRadiusProperty,
            new DynamicResourceExtension(ThemeChromeResources.CornerRadius(ThemeChromeRole.ControlFrame))));
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
