using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace OptilandWorkbench.App;

internal static class BrandAssets
{
    private static readonly Uri AppIconUri = new(
        "avares://OptilandWorkbench.App/Assets/Brand/AppIcon.png");
    private static readonly Uri SplashUri = new(
        "avares://OptilandWorkbench.App/Assets/Brand/Splash.png");

    public static WindowIcon LoadWindowIcon()
    {
        using var stream = AssetLoader.Open(AppIconUri);
        return new WindowIcon(stream);
    }

    public static Bitmap LoadSplashBitmap()
    {
        using var stream = AssetLoader.Open(SplashUri);
        return new Bitmap(stream);
    }
}
