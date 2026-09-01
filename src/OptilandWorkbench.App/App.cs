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
    internal const int StartupTimeoutMilliseconds = 30_000;
    private static readonly TimeSpan MinimumSplashDisplay = TimeSpan.FromMilliseconds(900);

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
            .Template()
            .OfType<ContentPresenter>()
            .Name("PART_ContentPresenter"))
        {
            Setters =
            {
                new Setter(
                    ContentPresenter.BoxShadowProperty,
                    new DynamicResourceExtension(ThemeChromeResources.BoxShadow(ThemeChromeRole.ControlFrame)))
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
                    new DynamicResourceExtension(ThemeResourceBindings.SectionHeaderBackground)),
                new Setter(
                    Button.BorderBrushProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.Border)),
                new Setter(
                    TextElement.ForegroundProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.SectionHeaderForeground))
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
                    new DynamicResourceExtension(ThemeResourceBindings.SectionHeaderEmphasizedBackground)),
                new Setter(
                    Button.BorderBrushProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.HoverBorder)),
                new Setter(
                    TextElement.ForegroundProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.SectionHeaderEmphasizedForeground))
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
                    new DynamicResourceExtension(ThemeResourceBindings.SectionHeaderEmphasizedForeground))
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
                    new DynamicResourceExtension(ThemeResourceBindings.SectionHeaderEmphasizedForeground))
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
                    new DynamicResourceExtension(ThemeResourceBindings.RibbonHoverBorder)),
                new Setter(ContentPresenter.BorderThicknessProperty, new Thickness(1))
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
        Styles.Add(new Style(selector => selector
            .OfType<TextBox>()
            .Template()
            .OfType<Border>()
            .Name("PART_BorderElement"))
        {
            Setters =
            {
                new Setter(
                    Border.BoxShadowProperty,
                    new DynamicResourceExtension(ThemeChromeResources.BoxShadow(ThemeChromeRole.ControlFrame)))
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
        Styles.Add(new Style(selector => selector
            .OfType<ComboBox>()
            .Template()
            .OfType<Border>()
            .Name("Background"))
        {
            Setters =
            {
                new Setter(
                    Border.BoxShadowProperty,
                    new DynamicResourceExtension(ThemeChromeResources.BoxShadow(ThemeChromeRole.ControlFrame)))
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
                new Setter(
                    DataGridColumnHeader.BackgroundProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.SectionHeaderBackground)),
                new Setter(
                    TextElement.ForegroundProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.SectionHeaderForeground)),
                new Setter(
                    DataGridColumnHeader.BorderBrushProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.Border)),
                new Setter(DataGridColumnHeader.BorderThicknessProperty, new Thickness(0, 0, 1, 1)),
                new Setter(DataGridColumnHeader.PaddingProperty, new Thickness(8, 3))
            }
        });
        Styles.Add(new Style(selector => selector.OfType<ListBox>())
        {
            Setters =
            {
                new Setter(
                    ListBox.BackgroundProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.SettingsSurface)),
                new Setter(
                    ListBox.BorderBrushProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.Border)),
                new Setter(
                    ListBox.BorderThicknessProperty,
                    new DynamicResourceExtension(ThemeChromeResources.BorderThickness(ThemeChromeRole.ControlFrame)))
            }
        });
        Styles.Add(new Style(selector => selector.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(ListBoxItem.BackgroundProperty, Brushes.Transparent),
                new Setter(
                    ListBoxItem.ForegroundProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.TextPrimary)),
                new Setter(
                    ListBoxItem.BorderBrushProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.Border)),
                new Setter(ListBoxItem.BorderThicknessProperty, new Thickness(0, 0, 0, 1)),
                new Setter(ListBoxItem.CornerRadiusProperty, new CornerRadius(0)),
                new Setter(ListBoxItem.PaddingProperty, new Thickness(7, 4))
            }
        });
        Styles.Add(new Style(selector => selector
            .OfType<ListBoxItem>()
            .Class(":pointerover"))
        {
            Setters =
            {
                new Setter(
                    ListBoxItem.BackgroundProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.Hover))
            }
        });
        Styles.Add(new Style(selector => selector
            .OfType<ListBoxItem>()
            .Class(":selected"))
        {
            Setters =
            {
                new Setter(
                    ListBoxItem.BackgroundProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.SelectionBackground)),
                new Setter(
                    ListBoxItem.ForegroundProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.SelectionForeground))
            }
        });
        Styles.Add(new Style(DataGridSelectedRowSelector)
        {
            Setters =
            {
                new Setter(
                    DataGridRow.BackgroundProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.SelectionBackground)),
                new Setter(
                    DataGridRow.BorderBrushProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.SelectionBackground)),
                new Setter(
                    TextElement.ForegroundProperty,
                    new DynamicResourceExtension(ThemeResourceBindings.SelectionForeground))
            }
        });
        AddDataGridSelectionVisualStyles();
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

    private void AddDataGridSelectionVisualStyles()
    {
        var selectors = new[]
        {
            DataGridSelectionRectangleSelector(null, pointerOver: false, focused: false),
            DataGridSelectionRectangleSelector(null, pointerOver: true, focused: false),
            DataGridSelectionRectangleSelector(null, pointerOver: false, focused: true),
            DataGridSelectionRectangleSelector(null, pointerOver: true, focused: true)
        };
        foreach (var selector in selectors)
        {
            Styles.Add(new Style(_ => selector)
            {
                Setters =
                {
                    new Setter(
                        Avalonia.Controls.Shapes.Shape.FillProperty,
                        new DynamicResourceExtension(ThemeResourceBindings.SelectionBackground)),
                    new Setter(Visual.OpacityProperty, 1d)
                }
            });
        }
    }

    private static Selector DataGridSelectionRectangleSelector(
        Selector? selector,
        bool pointerOver,
        bool focused)
    {
        selector = selector
            .OfType<DataGridRow>()
            .Class(":selected");
        if (pointerOver)
        {
            selector = selector.Class(":pointerover");
        }
        if (focused)
        {
            selector = selector.Class(":focus");
        }

        return selector
            .Template()
            .OfType<Avalonia.Controls.Shapes.Rectangle>()
            .Name("BackgroundRectangle");
    }

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
                () => StartMainWindowStartup(desktop, splash),
                DispatcherPriority.Background);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void StartMainWindowStartup(
        IClassicDesktopStyleApplicationLifetime desktop,
        SplashWindow splash)
    {
        var startupTask = OpenMainWindowAsync(desktop, splash);
        _ = startupTask.ContinueWith(
            task => System.Diagnostics.Trace.TraceError(
                $"Unhandled startup task fault after failure handling: {task.Exception}"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private static async Task OpenMainWindowAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        SplashWindow splash)
    {
        using var startupCancellation = new CancellationTokenSource();
        var startupTimeout = TimeSpan.FromMilliseconds(StartupTimeoutMilliseconds);
        var minimumDisplay = Task.Delay(MinimumSplashDisplay, startupCancellation.Token);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        MainWindow? mainWindow = null;
        EventHandler? readyHandler = null;
        EventHandler? closedHandler = null;
        EventHandler? splashClosedHandler = null;
        var splashClosed = false;
        try
        {
            splash.ReportProgress(28, "正在创建主工作区...");
            mainWindow = new MainWindow
            {
                Opacity = 0,
                ShowInTaskbar = false
            };
            var window = mainWindow;
            readyHandler = (_, _) =>
            {
                window.StartupCompleted -= readyHandler;
                splash.ReportProgress(92, "正在完成界面准备...");
                ready.TrySetResult();
            };
            closedHandler = (_, _) =>
                ready.TrySetException(new OperationCanceledException("主窗口在启动完成前关闭。"));
            splashClosedHandler = (_, _) =>
            {
                splashClosed = true;
                startupCancellation.Cancel();
            };
            mainWindow.StartupCompleted += readyHandler;
            mainWindow.Closed += closedHandler;
            splash.Closed += splashClosedHandler;
            desktop.MainWindow = mainWindow;
            mainWindow.Show();
            splash.ReportProgress(58, "正在恢复工作区与分析页面...");

            await Task.WhenAll(
                minimumDisplay,
                AwaitStartupCompletedAsync(ready.Task, startupTimeout, startupCancellation.Token));
            splash.Complete();
            await Task.Delay(120);
            mainWindow.ShowInTaskbar = true;
            mainWindow.Opacity = 1;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            splash.Close();
            mainWindow.Activate();
        }
        catch (Exception exception)
        {
            var diagnosticsPath = WriteStartupFailureLog(exception);
            if (splashClosed)
            {
                desktop.Shutdown(-1);
                return;
            }

            if (mainWindow is not null)
            {
                mainWindow.Opacity = 0;
                mainWindow.ShowInTaskbar = false;
            }

            desktop.MainWindow = splash;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            if (!splash.IsVisible)
            {
                splash.Show();
            }
            splash.ReportFailure(StartupFailureMessage(exception), diagnosticsPath);
            splash.Activate();
        }
        finally
        {
            if (mainWindow is not null)
            {
                if (readyHandler is not null)
                {
                    mainWindow.StartupCompleted -= readyHandler;
                }
                if (closedHandler is not null)
                {
                    mainWindow.Closed -= closedHandler;
                }
            }
            if (splashClosedHandler is not null)
            {
                splash.Closed -= splashClosedHandler;
            }
        }
    }

    internal static async Task AwaitStartupCompletedAsync(
        Task startupCompleted,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startupCompleted);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var timeoutTask = Task.Delay(timeout, cancellationToken);
        var completed = await Task.WhenAny(startupCompleted, timeoutTask);
        if (completed == startupCompleted)
        {
            await startupCompleted;
            return;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        throw new TimeoutException(
            $"Main window startup timed out after {timeout.TotalSeconds:0.#} seconds.");
    }

    internal static string StartupDiagnosticsLogPath()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            localApplicationData = AppContext.BaseDirectory;
        }

        return Path.Combine(
            localApplicationData,
            "Optical System Design",
            "Logs",
            "startup.log");
    }

    private static string? WriteStartupFailureLog(Exception exception)
    {
        try
        {
            var path = StartupDiagnosticsLogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(
                path,
                $"{DateTimeOffset.Now:O} Startup failed{Environment.NewLine}{exception}{Environment.NewLine}");
            return path;
        }
        catch (Exception logException)
        {
            System.Diagnostics.Trace.TraceError($"Failed to write startup diagnostics: {logException}");
            return null;
        }
    }

    private static string StartupFailureMessage(Exception exception) => exception switch
    {
        TimeoutException => "主窗口启动超过 30 秒仍未完成。请退出后重试；如果仍然失败，请带上诊断日志反馈。",
        OperationCanceledException => "启动在主窗口准备完成前被取消或关闭。",
        _ when !string.IsNullOrWhiteSpace(exception.Message) => exception.Message,
        _ => "应用初始化时发生未知错误。"
    };
}
