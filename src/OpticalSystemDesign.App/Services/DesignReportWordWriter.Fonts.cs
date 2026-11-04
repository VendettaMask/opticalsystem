using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace OpticalSystemDesign.App.Services;

internal static partial class DesignReportWordWriter
{
    private static void WriteFonts(ZipArchive zip, DocumentTypography typography, Action<string, XElement> save)
    {
        var table = new XElement(W + "fonts");
        var relationships = new XElement(Rel + "Relationships");
        var index = 0;
        foreach (var family in typography.Assets.GroupBy(asset => asset.Family))
        {
            var entry = new XElement(W + "font", Attribute("name", family.Key));
            foreach (var asset in family)
            {
                var id = "font" + ++index;
                var key = new Guid(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(asset.Path)).AsSpan(0, 16));
                var bytes = (byte[])asset.Bytes.Clone();
                var mask = Convert.FromHexString(key.ToString("N")); Array.Reverse(mask);
                for (var i = 0; i < Math.Min(32, bytes.Length); i++) bytes[i] ^= mask[i % 16];
                using (var stream = zip.CreateEntry("word/fonts/" + id + ".odttf", CompressionLevel.Optimal).Open()) stream.Write(bytes);
                var variant = asset.Weight >= 600 ? asset.Italic ? "embedBoldItalic" : "embedBold" : asset.Italic ? "embedItalic" : "embedRegular";
                entry.Add(new XElement(W + variant, new XAttribute(R + "id", id), Attribute("fontKey", key.ToString("B").ToUpperInvariant()), Attribute("subsetted", "false")));
                relationships.Add(Relationship(id, "font", "fonts/" + id + ".odttf"));
            }
            table.Add(entry);
        }
        save("word/fontTable.xml", table);
        save("word/_rels/fontTable.xml.rels", relationships);
        using var licenseStream = zip.CreateEntry("word/fonts/LICENSES.txt", CompressionLevel.Optimal).Open();
        using var licenseWriter = new StreamWriter(licenseStream, new System.Text.UTF8Encoding(false));
        foreach (var family in typography.Assets.GroupBy(asset => asset.Family))
        {
            licenseWriter.WriteLine(family.Key);
            licenseWriter.WriteLine(ProductFonts.License(family.First()));
        }
    }
}
