using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace OptilandWorkbench.App;

internal static class BrandAssets
{
    private const string CompanyLogoResourceName =
        "OptilandWorkbench.App.Assets.Brand.CompanyLogo.png";
    private static readonly Uri AppIconUri = new(
        "avares://OptilandWorkbench.App/Assets/Brand/AppIcon.png");
    private static readonly Uri SplashUri = new(
        "avares://OptilandWorkbench.App/Assets/Brand/Splash.png");
    private static readonly Lazy<byte[]> PreparedCompanyLogoPng = new(PrepareCompanyLogoPng);

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

    public static Bitmap LoadCompanyLogoBitmap()
    {
        using var stream = new MemoryStream(PreparedCompanyLogoPng.Value, writable: false);
        return new Bitmap(stream);
    }

    public static Bitmap LoadCompanyLogoBitmap(Color color)
    {
        using var stream = new MemoryStream(GetThemeColoredCompanyLogoPng(color), writable: false);
        return new Bitmap(stream);
    }

    internal static Bitmap? TryLoadCompanyLogoBitmap()
    {
        try
        {
            return LoadCompanyLogoBitmap();
        }
        catch (InvalidOperationException exception)
            when (exception.Message.Contains("IPlatformRenderInterface", StringComparison.Ordinal))
        {
            return null;
        }
    }

    internal static Bitmap? TryLoadCompanyLogoBitmap(Color color)
    {
        var png = GetThemeColoredCompanyLogoPng(color);
        try
        {
            using var stream = new MemoryStream(png, writable: false);
            return new Bitmap(stream);
        }
        catch (InvalidOperationException exception)
            when (exception.Message.Contains("IPlatformRenderInterface", StringComparison.Ordinal))
        {
            return null;
        }
    }

    internal static byte[] GetThemeColoredCompanyLogoPng(Color color)
    {
        using var source = SKBitmap.Decode(PreparedCompanyLogoPng.Value)
            ?? throw new InvalidOperationException("The prepared company logo could not be decoded.");
        using var tinted = new SKBitmap(
            new SKImageInfo(
                source.Width,
                source.Height,
                SKColorType.Rgba8888,
                SKAlphaType.Premul));
        tinted.Erase(SKColors.Transparent);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var alpha = source.GetPixel(x, y).Alpha;
                if (alpha > 0)
                {
                    tinted.SetPixel(x, y, new SKColor(color.R, color.G, color.B, alpha));
                }
            }
        }

        using var image = SKImage.FromBitmap(tinted);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("The tinted company logo could not be encoded.");
        return encoded.ToArray();
    }

    internal static byte[] GetPreparedCompanyLogoPng() => PreparedCompanyLogoPng.Value;

    internal static Stream OpenCompanyLogoStream()
    {
        return typeof(BrandAssets).Assembly.GetManifestResourceStream(CompanyLogoResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded brand asset '{CompanyLogoResourceName}' was not found.");
    }

    private static byte[] PrepareCompanyLogoPng()
    {
        using var stream = OpenCompanyLogoStream();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        using var decoded = SKBitmap.Decode(memory.ToArray())
            ?? throw new InvalidOperationException("The embedded company logo could not be decoded.");
        using var prepared = RemoveLightBackgroundAndCrop(decoded);
        using var image = SKImage.FromBitmap(prepared);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("The embedded company logo could not be encoded.");
        return encoded.ToArray();
    }

    private static SKBitmap RemoveLightBackgroundAndCrop(SKBitmap source)
    {
        var left = source.Width;
        var top = source.Height;
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                if (IsLightNeutral(source.GetPixel(x, y)))
                {
                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        if (right < left || bottom < top)
        {
            return new SKBitmap(1, 1, true);
        }

        const int padding = 12;
        left = Math.Max(0, left - padding);
        top = Math.Max(0, top - padding);
        right = Math.Min(source.Width - 1, right + padding);
        bottom = Math.Min(source.Height - 1, bottom + padding);
        var result = new SKBitmap(
            new SKImageInfo(
                right - left + 1,
                bottom - top + 1,
                SKColorType.Rgba8888,
                SKAlphaType.Premul));
        result.Erase(SKColors.Transparent);
        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                var color = source.GetPixel(x, y);
                if (!IsLightNeutral(color))
                {
                    result.SetPixel(x - left, y - top, color);
                }
            }
        }

        return result;
    }

    private static bool IsLightNeutral(SKColor color)
    {
        var minimum = Math.Min(color.Red, Math.Min(color.Green, color.Blue));
        var maximum = Math.Max(color.Red, Math.Max(color.Green, color.Blue));
        return color.Alpha == 0 || (minimum >= 232 && maximum - minimum <= 10);
    }
}
