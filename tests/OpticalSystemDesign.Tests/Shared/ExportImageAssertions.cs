using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;

namespace OpticalSystemDesign.Tests;

internal static class ExportImageAssertions
{
    internal static void HasCompleteBorder(Bitmap image)
    {
        var width = image.PixelSize.Width;
        var height = image.PixelSize.Height;
        var pixels = new byte[width * height * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { image.CopyPixels(new PixelRect(image.PixelSize), handle.AddrOfPinnedObject(), pixels.Length, width * 4); }
        finally { handle.Free(); }
        var frame = FrameBounds(image, pixels);
        var color = pixels[((frame.Y * width + frame.X) * 4)..((frame.Y * width + frame.X) * 4 + 4)];
        Assert.Equal(255, color[3]);
        Assert.False(color[0] == 255 && color[1] == 255 && color[2] == 255, "The outer border must be visible on a white page.");
        foreach (var x in Enumerable.Range(frame.X, frame.Width))
        {
            var top = (frame.Y * width + x) * 4;
            Assert.Equal(color, pixels[top..(top + 4)]);
            var index = ((frame.Bottom - 1) * width + x) * 4;
            Assert.Equal(color, pixels[index..(index + 4)]);
        }
        foreach (var y in Enumerable.Range(frame.Y, frame.Height))
        {
            var first = (y * width + frame.X) * 4;
            var last = (y * width + frame.Right - 1) * 4;
            Assert.Equal(color, pixels[first..(first + 4)]);
            Assert.Equal(color, pixels[last..(last + 4)]);
        }
    }

    internal static PixelRect FrameBounds(Bitmap image)
    {
        var bytes = Read(image, new PixelRect(image.PixelSize));
        return FrameBounds(image, bytes);
    }

    private static PixelRect FrameBounds(Bitmap image, byte[] bytes)
    {
        var w = image.PixelSize.Width; var h = image.PixelSize.Height;
        bool Same(int x, int y, int otherX = 0, int otherY = 0) =>
            bytes.AsSpan((y * w + x) * 4, 4).SequenceEqual(bytes.AsSpan((otherY * w + otherX) * 4, 4));
        if (Same(w / 2, 0) && Same(w / 2, h - 1) && Same(0, h / 2) && Same(w - 1, h / 2))
            return new PixelRect(image.PixelSize);
        var top = 0; while (top < h && Same(w / 2, top)) top++;
        var bottom = h - 1; while (bottom >= top && Same(w / 2, bottom)) bottom--;
        var left = 0; while (left < w && Same(left, h / 2)) left++;
        var right = w - 1; while (right >= left && Same(right, h / 2)) right--;
        Assert.True(left <= right && top <= bottom, "The centered source frame must be visible within the canvas.");
        return new(left, top, right - left + 1, bottom - top + 1);
    }

    internal static byte[] FramePixels(Bitmap image) => Read(image, FrameBounds(image));
    private static byte[] Read(Bitmap image, PixelRect rect)
    {
        var bytes = new byte[rect.Width * rect.Height * 4];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { image.CopyPixels(rect, handle.AddrOfPinnedObject(), bytes.Length, rect.Width * 4); }
        finally { handle.Free(); }
        return bytes;
    }

    internal static SkiaSharp.SKBitmap FrameBitmap(Bitmap image)
    {
        using var stream = new MemoryStream(); image.Save(stream, PngBitmapEncoderOptions.Default);
        using var decoded = SkiaSharp.SKBitmap.Decode(stream.ToArray());
        var rect = FrameBounds(image); var cropped = new SkiaSharp.SKBitmap();
        Assert.True(decoded.ExtractSubset(cropped, new SkiaSharp.SKRectI(rect.X, rect.Y, rect.Right, rect.Bottom)));
        return cropped;
    }
}
