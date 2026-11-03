namespace OpticalSystemDesign.Application.Services;

/// <summary>Output policy only. Input decoders may continue to accept other formats.</summary>
public static class LosslessImageOutput
{
    public static void RequirePngOrBmp(string path)
    {
        var extension = Path.GetExtension(path);
        if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Image output supports lossless PNG or BMP only.");
    }

    public static void RequirePng(string path)
    {
        if (!Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("PNG image export requires a .png filename.");
    }
}
