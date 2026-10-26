using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace OpticalSystemDesign.App;

internal static partial class BrandAssets
{
    private static readonly Uri AppIconUri = new(
        "avares://OpticalSystemDesign.App/Assets/Brand/AppIcon.png");
    private static readonly Uri SplashUri = new(
        "avares://OpticalSystemDesign.App/Assets/Brand/Splash.png");

    public static WindowIcon LoadWindowIcon()
    {
        using var stream = AssetLoader.Open(AppIconUri);
        return new WindowIcon(stream);
    }

    public static Bitmap LoadAppIconBitmap()
    {
        using var stream = AssetLoader.Open(AppIconUri);
        return new Bitmap(stream);
    }

    public static Bitmap LoadSplashBitmap()
    {
        using var stream = AssetLoader.Open(SplashUri);
        return new Bitmap(stream);
    }
}
