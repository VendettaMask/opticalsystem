using System.Collections.Concurrent;
using System.Buffers.Binary;
using System.Text;
using Avalonia.Media;
using Avalonia.Platform;
using SkiaSharp;

namespace OpticalSystemDesign.App.Services;

public enum ProductFontRole { Interface, Numeric, Engineering, Document, Code, Math }

internal sealed record ProductFontAsset(string Family, string Path, int Weight = 400, bool Italic = false, bool Embedded = false)
{
    internal byte[] Bytes => ProductFonts.ReadBytes(this);
    internal SKTypeface Typeface => ProductFonts.LoadTypeface(this);
}

public static partial class ProductFonts
{
    private static readonly ConcurrentDictionary<string, Lazy<byte[]>> FontBytes = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Lazy<SKTypeface>> SkiaFaces = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Lazy<IReadOnlyList<ProductFontAsset>>> SystemFaces = new(StringComparer.OrdinalIgnoreCase);
    private static readonly StandardAssetLoader Assets = new();
    internal static readonly ProductFontAsset DocumentAsset = new(DocumentFamilyName, "Assets/Fonts/NotoSansCJKsc-Regular.otf", Embedded: true);
    internal static readonly ProductFontAsset MathAsset = new(MathFamilyName, "Assets/Fonts/Typography/Math/STIXTwoMath-Regular.ttf");

    internal static IReadOnlyList<ProductFontAsset> Faces(string family)
    {
        string File(string relative) => "Assets/Fonts/Typography/" + relative;
        return family switch
        {
            "DM Sans" => [new(family, File("Latin/DMSans.ttf"))],
            "Source Han Sans SC" => [new(family, File("Cjk/SourceHanSansSC-Medium.otf"), 500), new(family, File("Cjk/SourceHanSansSC-Bold.otf"), 700)],
            "JetBrains Mono" => [new(family, File("Numeric/JetBrainsMono-Regular.ttf")), new(family, File("Numeric/JetBrainsMono-Bold.ttf"), 700)],
            "Almendra" => [new(family, File("Almendra/Almendra-Regular.ttf")), new(family, File("Almendra/Almendra-Bold.ttf"), 700),
                new(family, File("Almendra/Almendra-Italic.ttf"), Italic: true), new(family, File("Almendra/Almendra-BoldItalic.ttf"), 700, true)],
            DocumentFamilyName => [DocumentAsset],
            MathFamilyName => [MathAsset],
            "Fusion Pixel 10px Mono zh_hans" => [new(family, "Assets/Fonts/fusion-pixel-10px-monospaced-zh_hans.ttf")],
            _ => SystemFaces.GetOrAdd(family, name => new(() => ReadSystemFaces(name))).Value
        };
    }

    private static IReadOnlyList<ProductFontAsset> ReadSystemFaces(string family)
    {
        var faces = new List<ProductFontAsset>();
        foreach (var (bold, italic) in new[] { (false, false), (true, false), (false, true), (true, true) })
        {
            using var face = SKFontManager.Default.MatchFamily(family,
                new SKFontStyle(bold ? 700 : 400, 5, italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright));
            if (face is null || !string.Equals(face.FamilyName, family, StringComparison.OrdinalIgnoreCase)) continue;
            using var stream = face.OpenStream(out var collectionIndex);
            if (stream is null || collectionIndex != 0 || stream.Length > 64 * 1024 * 1024) continue;
            var bytes = new byte[stream.Length];
            if (stream.Read(bytes, bytes.Length) != bytes.Length || !AllowsDocumentEmbedding(bytes)) continue;
            var path = "system://" + family + "/" + face.FontStyle.Weight + "/" + face.FontStyle.Slant;
            FontBytes.TryAdd(path, new(() => bytes));
            faces.Add(new(family, path, face.FontStyle.Weight, face.FontStyle.Slant != SKFontStyleSlant.Upright));
        }
        return faces.DistinctBy(face => face.Path).ToArray();
    }

    private static bool AllowsDocumentEmbedding(byte[] bytes)
    {
        if (bytes.Length < 12 || Encoding.ASCII.GetString(bytes, 0, 4) == "ttcf") return false;
        var count = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4, 2));
        for (var index = 0; index < count; index++)
        {
            var table = 12 + 16 * index;
            if (table + 16 > bytes.Length) return false;
            if (Encoding.ASCII.GetString(bytes, table, 4) != "OS/2") continue;
            var offset = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(table + 8, 4));
            if (offset + 10 > bytes.Length) return false;
            var flags = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan((int)offset + 8, 2));
            return (flags & 0x0202) == 0; // No restricted-license or bitmap-only embedding.
        }
        return true;
    }

    internal static string[] FamilyNames(FontFamily family) => family.Name.Split(',').Select(name =>
    {
        name = name.Trim(); var hash = name.LastIndexOf('#');
        return hash < 0 ? name : name[(hash + 1)..];
    }).Where(name => name.Length > 0).ToArray();

    internal static byte[] ReadBytes(ProductFontAsset asset) => FontBytes.GetOrAdd(asset.Path, _ => new(() =>
    {
        using var stream = asset.Embedded
            ? typeof(ProductFonts).Assembly.GetManifestResourceStream("OpticalSystemDesign.App." + asset.Path.Replace('/', '.'))
            : Assets.Open(new Uri("avares://OpticalSystemDesign.App/" + asset.Path));
        if (stream is null) throw new InvalidDataException("Bundled font unavailable: " + asset.Path);
        using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray();
    })).Value;

    internal static SKTypeface LoadTypeface(ProductFontAsset asset) => SkiaFaces.GetOrAdd(asset.Path, _ => new(() =>
    {
        using var data = SKData.CreateCopy(asset.Bytes);
        return SKTypeface.FromData(data) ?? throw new InvalidDataException("Invalid bundled font: " + asset.Path);
    })).Value;

    internal static ProductFontAsset Face(string family, bool bold, bool italic) => Faces(family)
        .OrderBy(face => (face.Italic == italic ? 0 : 1000) + Math.Abs(face.Weight - (bold ? 700 : 400)))
        .FirstOrDefault() ?? throw new InvalidDataException("Font is not registered for portable export: " + family);

    internal static string CssNames(IEnumerable<string> names) => string.Join(",", names.Select(name => "\"" + name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\""));

    internal static string License(ProductFontAsset asset)
    {
        var path = asset.Family switch
        {
            DocumentFamilyName => "Assets/Fonts/OFL-LICENSE.txt",
            "Fusion Pixel 10px Mono zh_hans" => "Assets/Fonts/FUSION-PIXEL-FONT-OFL.txt",
            "Almendra" => "Assets/Fonts/Typography/Almendra-OFL.txt",
            "DM Sans" => "Assets/Fonts/Typography/DM-Sans-OFL.txt",
            "Source Han Sans SC" => "Assets/Fonts/Typography/Source-Han-Sans-OFL.txt",
            "JetBrains Mono" => "Assets/Fonts/Typography/JetBrains-Mono-OFL.txt",
            MathFamilyName => "Assets/Fonts/Typography/STIX-Two-Math-OFL.txt",
            _ => ""
        };
        if (path.Length == 0) return "User-selected system font: " + asset.Family + ". Original font copyright and document-embedding permissions are retained in the embedded font metadata.";
        using var stream = Assets.Open(new Uri("avares://OpticalSystemDesign.App/" + path));
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
}
