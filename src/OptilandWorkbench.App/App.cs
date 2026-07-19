using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Dock.Avalonia.Controls;
using Dock.Avalonia.Themes.Fluent;
using OptilandWorkbench.App.Services;

namespace OptilandWorkbench.App;

public sealed class App : Avalonia.Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new DockFluentTheme());
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
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
