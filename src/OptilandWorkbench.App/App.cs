using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Dock.Avalonia.Controls;
using Dock.Avalonia.Themes.Fluent;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App;

public sealed class App : Avalonia.Application
{
    public override void Initialize()
    {
        Name = "Optical System Design";
        Styles.Add(new FluentTheme());
        Styles.Add(new DockFluentTheme());
        ApplyBlueAccent();
        DataTemplates.Add(new WorkspaceViewLocator());
        Styles.Add(new StyleInclude(new Uri("avares://Avalonia.Controls.DataGrid"))
        {
            Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml")
        });

        var controlBorder = new SolidColorBrush(Color.FromRgb(199, 199, 204));
        var controlBackground = new SolidColorBrush(Color.FromRgb(255, 255, 255));
        Styles.Add(new Style(selector => selector.OfType<Button>())
        {
            Setters =
            {
                new Setter(Button.MinHeightProperty, 29d),
                new Setter(Button.PaddingProperty, new Thickness(10, 4)),
                new Setter(Button.CornerRadiusProperty, new CornerRadius(5)),
                new Setter(Button.BackgroundProperty, new SolidColorBrush(Color.FromRgb(242, 242, 247))),
                new Setter(Button.BorderBrushProperty, controlBorder),
                new Setter(Button.BorderThicknessProperty, new Thickness(1))
            }
        });
        Styles.Add(new Style(selector => selector.OfType<TextBox>())
        {
            Setters =
            {
                new Setter(TextBox.MinHeightProperty, 29d),
                new Setter(TextBox.CornerRadiusProperty, new CornerRadius(5)),
                new Setter(TextBox.BackgroundProperty, controlBackground),
                new Setter(TextBox.BorderBrushProperty, controlBorder)
            }
        });
        Styles.Add(new Style(selector => selector.OfType<ComboBox>())
        {
            Setters =
            {
                new Setter(ComboBox.MinHeightProperty, 29d),
                new Setter(ComboBox.CornerRadiusProperty, new CornerRadius(5)),
                new Setter(ComboBox.BackgroundProperty, controlBackground),
                new Setter(ComboBox.BorderBrushProperty, controlBorder)
            }
        });
        Styles.Add(new Style(selector => selector.OfType<NumericUpDown>())
        {
            Setters =
            {
                new Setter(NumericUpDown.ShowButtonSpinnerProperty, false),
                new Setter(NumericUpDown.MinHeightProperty, 29d),
                new Setter(NumericUpDown.CornerRadiusProperty, new CornerRadius(5)),
                new Setter(NumericUpDown.BackgroundProperty, controlBackground),
                new Setter(NumericUpDown.BorderBrushProperty, controlBorder)
            }
        });
        Styles.Add(new Style(selector => selector.OfType<DataGridColumnHeader>())
        {
            Setters =
            {
                new Setter(DataGridColumnHeader.BackgroundProperty, new SolidColorBrush(Color.FromRgb(242, 242, 247))),
                new Setter(DataGridColumnHeader.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(209, 209, 214))),
                new Setter(DataGridColumnHeader.BorderThicknessProperty, new Thickness(0, 0, 1, 1)),
                new Setter(DataGridColumnHeader.PaddingProperty, new Thickness(8, 3))
            }
        });
        AddDockIconStyles();
    }

    private void ApplyBlueAccent()
    {
        var accent = Color.FromRgb(0, 122, 255);
        Resources["SystemAccentColor"] = accent;
        Resources["SystemAccentColorDark1"] = Color.FromRgb(0, 102, 204);
        Resources["SystemAccentColorDark2"] = Color.FromRgb(0, 82, 164);
        Resources["SystemAccentColorDark3"] = Color.FromRgb(0, 64, 128);
        Resources["SystemAccentColorLight1"] = Color.FromRgb(64, 156, 255);
        Resources["SystemAccentColorLight2"] = Color.FromRgb(128, 190, 255);
        Resources["SystemAccentColorLight3"] = Color.FromRgb(204, 229, 255);
        Resources["AccentFillColorDefaultBrush"] = new SolidColorBrush(accent);
        Resources["AccentFillColorSecondaryBrush"] = new SolidColorBrush(Color.FromRgb(0, 112, 235));
        Resources["AccentFillColorTertiaryBrush"] = new SolidColorBrush(Color.FromRgb(0, 102, 214));
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
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var splash = new SplashWindow();
            desktop.MainWindow = splash;
            MacOsBranding.TryApplyApplicationIcon();
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
        var minimumDisplay = Task.Delay(750);
        try
        {
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
                ready.TrySetResult();
            };
            mainWindow.StartupCompleted += readyHandler;
            desktop.MainWindow = mainWindow;
            mainWindow.Show();

            await Task.WhenAll(minimumDisplay, ready.Task);
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
