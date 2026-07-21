using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace OptilandWorkbench.App;

internal sealed class SplashWindow : Window
{
    private readonly Bitmap _image = BrandAssets.LoadSplashBitmap();

    public SplashWindow()
    {
        Title = "Optical System Design";
        Width = 800;
        Height = 450;
        CanResize = false;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(247, 249, 252));
        Icon = BrandAssets.LoadWindowIcon();
        Content = new Image
        {
            Source = _image,
            Stretch = Stretch.UniformToFill
        };
        Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        Closed -= OnClosed;
        _image.Dispose();
    }
}
