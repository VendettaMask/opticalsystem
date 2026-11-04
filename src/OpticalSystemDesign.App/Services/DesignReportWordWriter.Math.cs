using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using OpticalSystemDesign.App.Controls;
using SkiaSharp;

namespace OpticalSystemDesign.App.Services;

internal static partial class DesignReportWordWriter
{
    private static readonly XNamespace M = "http://schemas.openxmlformats.org/officeDocument/2006/math";

    private static XElement FormulaParagraph(string expression, DocumentTypography typography)
    {
        var math = new XElement(M + "oMath");
        XElement MathRun(string value) => new(M + "r", new XElement(W + "rPr",
            new XElement(W + "rFonts", Attribute("ascii", typography.Primary(ProductFontRole.Math)), Attribute("hAnsi", typography.Primary(ProductFontRole.Math))),
            new XElement(W + "sz", Attribute("val", 17))), new XElement(M + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value));
        foreach (Match token in Regex.Matches(expression, @"([ρu])\^(\d+)|√(\d+)|[^ρu√]+|[ρu]"))
        {
            if (token.Groups[1].Success)
                math.Add(new XElement(M + "sSup", new XElement(M + "e", MathRun(token.Groups[1].Value)), new XElement(M + "sup", MathRun(token.Groups[2].Value))));
            else if (token.Groups[3].Success)
                math.Add(new XElement(M + "rad", new XElement(M + "radPr", new XElement(M + "degHide", new XAttribute(M + "val", 1))), new XElement(M + "deg"), new XElement(M + "e", MathRun(token.Groups[3].Value))));
            else math.Add(MathRun(token.Value));
        }
        return new XElement(W + "p", new XElement(W + "pPr", new XElement(W + "pStyle", Attribute("val", "TableBody"))), math);
    }

    private static XElement DirectoryEntry(ReportDirectoryEntry entry) => new(W + "p",
        new XElement(W + "pPr", new XElement(W + "pStyle", Attribute("val", "Contents")),
            new XElement(W + "tabs", new XElement(W + "tab", Attribute("val", "right"), Attribute("leader", "dot"), Attribute("pos", ContentWidth)))),
        new XElement(W + "hyperlink", Attribute("anchor", $"section{entry.SectionNumber:D3}"),
            new XElement(W + "r", new XElement(W + "t", $"{entry.SectionNumber:D2}　{entry.Title}")), new XElement(W + "r", new XElement(W + "tab")),
            new XElement(W + "fldSimple", Attribute("instr", $"PAGEREF section{entry.SectionNumber:D3} \\h"), Attribute("dirty", "true"),
                new XElement(W + "r", new XElement(W + "t", entry.Page)))));

    private static void WriteBrandHeader(ZipArchive zip, DesignReportPdfModel? report, Action<string, XElement> save)
    {
        var svg = DesignReportHtmlWriter.Resource("Assets.Brand.CompanyLogo.svg");
        using (var stream = zip.CreateEntry("word/media/brand.svg", CompressionLevel.Fastest).Open())
        { var bytes = Encoding.UTF8.GetBytes(svg); stream.Write(bytes); }
        using (var surface = SKSurface.Create(new SKImageInfo(1183, 104)))
        {
            surface.Canvas.Clear(SKColors.Transparent);
            BrandAssets.DrawCompanyLogo(surface.Canvas, new SKRect(0, 0, 1183, 104), new SKColor(50, 90, 119));
            using var image = surface.Snapshot(); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = zip.CreateEntry("word/media/brand.png", CompressionLevel.Fastest).Open(); data.SaveTo(stream);
        }
        var picture = Picture("S.T.A.R.Labs", "brand", 1_000_000, 1183, 104, 100);
        XNamespace svgNamespace = "http://schemas.microsoft.com/office/drawing/2016/SVG/main";
        picture.Descendants(A + "blip").Single().Add(new XElement(A + "extLst", new XElement(A + "ext", new XAttribute("uri", "{96DAC541-7B7A-43D3-8B79-37D633B846F1}"),
            new XElement(svgNamespace + "svgBlip", new XAttribute(R + "embed", "brandVector")))));
        picture.Element(W + "pPr")!.Add(new XElement(W + "pStyle", Attribute("val", "Header")),
            new XElement(W + "pBdr", new XElement(W + "bottom", Attribute("val", "single"), Attribute("sz", 4), Attribute("space", 8), Attribute("color", "A9B8C5"))));
        picture.Add(new XElement(W + "r", new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), "　　　" + (report?.Name ?? "Optical System Design") + "　光学设计分析")));
        save("word/header.xml", new XElement(W + "hdr", picture));
        save("word/_rels/header.xml.rels", new XElement(Rel + "Relationships", Relationship("brand", "image", "media/brand.png"), Relationship("brandVector", "image", "media/brand.svg")));
    }
}
