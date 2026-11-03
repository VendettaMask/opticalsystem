using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;

namespace OpticalSystemDesign.App.Services;

/// <summary>Retains lossless PNG pixels and publishes the captured bitmap's actual DPI.</summary>
internal static class PngImageWriter
{
    private sealed record PrintResolution(double Dpi);
    private static readonly ConditionalWeakTable<Bitmap, PrintResolution> Resolutions = new();
    internal static void SetPrintDpi(Bitmap bitmap, double dpi) => Resolutions.AddOrUpdate(bitmap, new(dpi));
    // PNG pHYs stores integer pixels/metre, unit=1; see W3C PNG 11.3.4.3.
    internal static void Write(Bitmap bitmap, Stream destination)
    {
        using var encoded = new MemoryStream();
        bitmap.Save(encoded, PngBitmapEncoderOptions.Default);
        var bytes = encoded.GetBuffer().AsSpan(0, checked((int)encoded.Length));
        destination.Write(bytes[..8]);
        Span<byte> chunk = stackalloc byte[21];
        var dpiX = Resolutions.TryGetValue(bitmap, out var print) ? print.Dpi : bitmap.Dpi.X;
        var dpiY = print?.Dpi ?? bitmap.Dpi.Y;
        for (var offset = 8; offset < bytes.Length;)
        {
            if (bytes.Length - offset < 12) throw new InvalidDataException("Invalid encoded PNG chunk.");
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4)));
            if (length > bytes.Length - offset - 12) throw new InvalidDataException("Invalid encoded PNG length.");
            var type = bytes.Slice(offset + 4, 4);
            if (!type.SequenceEqual("pHYs"u8)) destination.Write(bytes.Slice(offset, length + 12));
            if (type.SequenceEqual("IHDR"u8))
            {
                BinaryPrimitives.WriteUInt32BigEndian(chunk, 9);
                "pHYs"u8.CopyTo(chunk[4..]);
                BinaryPrimitives.WriteUInt32BigEndian(chunk[8..], checked((uint)Math.Round(dpiX / .0254)));
                BinaryPrimitives.WriteUInt32BigEndian(chunk[12..], checked((uint)Math.Round(dpiY / .0254)));
                chunk[16] = 1;
                var crc = uint.MaxValue;
                foreach (var value in chunk.Slice(4, 13))
                {
                    crc ^= value;
                    for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320u : crc >> 1;
                }
                BinaryPrimitives.WriteUInt32BigEndian(chunk[17..], ~crc);
                destination.Write(chunk);
            }
            offset += length + 12;
        }
    }
}
