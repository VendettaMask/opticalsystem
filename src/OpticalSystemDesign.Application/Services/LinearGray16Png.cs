using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace OpticalSystemDesign.Application.Services;

/// <summary>Unsigned 16-bit grayscale PNG, filter 0, no color/gamma/ICC/alpha chunks.
/// Physical decoding is DN * commonMaximum / 65535. Floating-point power remains authoritative.</summary>
internal static class LinearGray16Png
{
    internal static void Validate(byte[] bytes, IReadOnlyList<double> irradiance, int width, int height, double maximum, CancellationToken token)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes.Length < 45 || !bytes.AsSpan(0, 8).SequenceEqual(signature)) throw new InvalidDataException("Invalid linear PNG signature.");
        var headerSeen = false; var endSeen = false; var dataSeen = false; var offset = 8;
        using var compressed = new MemoryStream();
        while (offset < bytes.Length)
        {
            token.ThrowIfCancellationRequested();
            if (bytes.Length - offset < 12) throw new InvalidDataException("Truncated PNG chunk.");
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset)));
            if (length > bytes.Length - offset - 12) throw new InvalidDataException("PNG chunk exceeds its input.");
            var type = bytes.AsSpan(offset + 4, 4); var name = Encoding.ASCII.GetString(type); var data = bytes.AsSpan(offset + 8, length);
            var crc = uint.MaxValue; foreach (var value in type) crc = Update(crc, value); foreach (var value in data) crc = Update(crc, value);
            if ((crc ^ uint.MaxValue) != BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 8 + length))) throw new InvalidDataException("PNG chunk CRC differs.");
            if (endSeen) throw new InvalidDataException("PNG has trailing data.");
            if (name == "IHDR")
            {
                if (headerSeen || offset != 8 || length != 13 || BinaryPrimitives.ReadUInt32BigEndian(data) != width
                    || BinaryPrimitives.ReadUInt32BigEndian(data[4..]) != height || data[8] != 16 || data[9] != 0 || data[10] != 0 || data[11] != 0 || data[12] != 0)
                    throw new InvalidDataException("PNG is not an unfiltered, non-interlaced Gray16 full raster.");
                headerSeen = true;
            }
            else if (name == "IDAT") { if (!headerSeen) throw new InvalidDataException("PNG data precedes its header."); dataSeen = true; compressed.Write(data); }
            else if (name == "IEND") { if (!dataSeen || length != 0) throw new InvalidDataException("Invalid PNG trailer."); endSeen = true; }
            else throw new InvalidDataException("Linear PNG contains unpermitted color/gamma/auxiliary chunks.");
            offset += length + 12;
        }
        if (!endSeen) throw new InvalidDataException("Missing PNG trailer.");
        compressed.Position = 0; using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        var row = new byte[checked(1 + 2 * width)];
        for (var y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested(); zlib.ReadExactly(row);
            if (row[0] != 0) throw new InvalidDataException("Unexpected linear PNG filter.");
            for (var x = 0; x < width; x++)
            {
                var dn = BinaryPrimitives.ReadUInt16BigEndian(row.AsSpan(1 + x * 2));
                var reconstructed = dn / 65535d * maximum;
                if (Math.Abs(reconstructed - irradiance[y * width + x]) > maximum / 65535 * .50000001)
                    throw new InvalidDataException("PNG pixels differ from the authoritative linear irradiance.");
            }
        }
        if (zlib.ReadByte() != -1) throw new InvalidDataException("PNG has extra raster rows.");
    }

    internal static byte[] Encode(IReadOnlyList<double> irradiance, int width, int height, double commonMaximum, CancellationToken token)
    {
        if (width < 1 || height < 1 || irradiance.Count != checked(width * height) || !double.IsFinite(commonMaximum) || commonMaximum <= 0)
            throw new ArgumentException("Invalid linear exchange image.");
        using var output = new MemoryStream(); output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13]; BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width); BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = 16; // Grayscale, compression 0, filter 0, no interlace; remaining bytes are zero.
        Chunk(output, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true))
        {
            var row = new byte[checked(1 + width * 2)];
            for (var y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x < width; x++)
                {
                    var value = irradiance[y * width + x];
                    if (!double.IsFinite(value) || value < 0 || value > commonMaximum)
                        throw new InvalidDataException("Linear PNG would clip or encode invalid irradiance.");
                    var dn = (ushort)Math.Round(value / commonMaximum * ushort.MaxValue, MidpointRounding.ToEven);
                    BinaryPrimitives.WriteUInt16BigEndian(row.AsSpan(1 + x * 2), dn);
                }
                zlib.Write(row);
            }
        }
        var buffer = compressed.GetBuffer();
        for (var first = 0; first < compressed.Length; first += 65536)
            Chunk(output, "IDAT", buffer.AsSpan(first, (int)Math.Min(65536, compressed.Length - first)));
        Chunk(output, "IEND", []); return output.ToArray();
    }

    private static void Chunk(Stream output, string name, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(word, (uint)data.Length); output.Write(word);
        var type = Encoding.ASCII.GetBytes(name); output.Write(type); output.Write(data);
        var crc = uint.MaxValue;
        foreach (var value in type) crc = Update(crc, value);
        foreach (var value in data) crc = Update(crc, value);
        BinaryPrimitives.WriteUInt32BigEndian(word, crc ^ uint.MaxValue); output.Write(word);
    }
    private static uint Update(uint crc, byte value)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        return crc;
    }
}
