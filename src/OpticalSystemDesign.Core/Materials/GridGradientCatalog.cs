using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OpticalSystemDesign.Core.Backend;

namespace OpticalSystemDesign.Core.Materials;

internal static class GridGradientCatalog
{
    internal static GridGradientIndexProfile? Resolve(string name, string? assetDirectory, double lengthScale)
    {
        var path = Path.IsPathRooted(name) ? name : assetDirectory is null ? null : Path.Combine(assetDirectory, name);
        if (path is not null && File.Exists(path))
        {
            if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("Grid GRIN file exceeds its bounded size.");
            return Read(File.ReadAllBytes(path), Path.GetFileName(name), lengthScale);
        }
        if (!name.Equals("Sample.GGD", StringComparison.OrdinalIgnoreCase)) return null;
        using var resource = typeof(GridGradientCatalog).Assembly.GetManifestResourceStream("OpticalSystemDesign.Core.Materials.Data.sample-grid-gradient.ggd")
            ?? throw new InvalidDataException("Missing bundled grid-gradient sample.");
        using var buffer = new MemoryStream(); resource.CopyTo(buffer); var bytes = buffer.ToArray();
        if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != "d025dcc149a61dae8a368559ae31e1932b0164611accf90bf3c9403b96025e55")
            throw new InvalidDataException("Bundled grid-gradient data identity changed.");
        return Read(bytes, name, lengthScale);
    }
    internal static GridGradientIndexProfile Read(byte[] bytes, string sourceName, double lengthScale)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        static string[] Tokens(string? line) => line?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) ?? [];
        static double Number(string token) => double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && double.IsFinite(value) ? value : throw new InvalidDataException("Non-finite grid-gradient value.");
        var header = Tokens(reader.ReadLine());
        if (header.Length != 7 || !header[0].Equals("Schott", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Only the declared Schott grid dispersion formula is implemented.");
        var counts = header.Skip(1).Take(3).Select(Number).ToArray();
        if (counts.Any(v => v != Math.Truncate(v) || v is < 6 or > 128) || counts.Aggregate(1d, (a, b) => a * b) > 80_000)
            throw new InvalidDataException("Invalid bounded grid-gradient dimensions.");
        var nx = (int)counts[0]; var ny = (int)counts[1]; var nz = (int)counts[2];
        var spacing = new Vector3D(Number(header[4]) * lengthScale, Number(header[5]) * lengthScale, Number(header[6]) * lengthScale);
        var values = new double[nx * ny * nz * 11]; Vector3D origin = default;
        for (var i = 0; i < nx; i++) for (var j = 0; j < ny; j++) for (var k = 0; k < nz; k++)
        {
            var tokens = Tokens(reader.ReadLine()); if (tokens.Length != 14) throw new InvalidDataException("Grid GRIN requires every ordered fourteen-column row.");
            var row = tokens.Select(Number).ToArray(); var point = new Vector3D(row[0] * lengthScale, row[1] * lengthScale, row[2] * lengthScale);
            if (i == 0 && j == 0 && k == 0) origin = point;
            var expected = origin + new Vector3D(i * spacing.X, j * spacing.Y, k * spacing.Z);
            if ((point - expected).Length > 1e-9 * Math.Max(1, expected.Length)) throw new InvalidDataException("Grid GRIN coordinates must match the declared regular lattice, Z varying fastest.");
            Array.Copy(row, 3, values, 11 * ((i * ny + j) * nz + k), 11);
        }
        while (reader.ReadLine() is { } extra) if (!string.IsNullOrWhiteSpace(extra)) throw new InvalidDataException("Unexpected trailing grid-gradient data.");
        return new(nx, ny, nz, origin, spacing, values, sourceName, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }
}
