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
        var color = pixels[..4];
        Assert.Equal(255, color[3]);
        Assert.False(color[0] == 255 && color[1] == 255 && color[2] == 255, "The outer border must be visible on a white page.");
        foreach (var x in Enumerable.Range(0, width))
        {
            Assert.Equal(color, pixels[(x * 4)..(x * 4 + 4)]);
            var index = ((height - 1) * width + x) * 4;
            Assert.Equal(color, pixels[index..(index + 4)]);
        }
        foreach (var y in Enumerable.Range(0, height))
        {
            var first = y * width * 4;
            var last = (y * width + width - 1) * 4;
            Assert.Equal(color, pixels[first..(first + 4)]);
            Assert.Equal(color, pixels[last..(last + 4)]);
        }
    }
}
