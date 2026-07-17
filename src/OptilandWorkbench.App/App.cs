using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace OptilandWorkbench.App;

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
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
