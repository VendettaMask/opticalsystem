using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using OpticalSystemDesign.Application.Services;
using SkiaSharp;

namespace OpticalSystemDesign.App.Services;

/// <summary>Portable, editable OOXML with embedded PNGs; no Office installation is needed.</summary>
internal static class DesignReportWordWriter
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Pic = "http://schemas.openxmlformats.org/drawingml/2006/picture";
    private static readonly XNamespace Wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private const int ContentWidth = 9866;

    internal static void Write(Stream output, DesignReportContent content, string images, CancellationToken token)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var body = new XElement(W + "body");
        var relationships = new XElement(Rel + "Relationships", Relationship("styles", "styles", "styles.xml"),
            Relationship("header", "header", "header.xml"), Relationship("footer", "footer", "footer.xml"));
        var headings = content.Blocks.OfType<ReportHeading>().Where(heading => heading.Level == 2).ToArray();
        var section = 0;
        var imageNumber = 0;
        long imageBytes = 0;
        foreach (var block in content.Blocks)
        {
            token.ThrowIfCancellationRequested();
            switch (block)
            {
                case ReportHeading heading:
                    var headingParagraph = Paragraph(heading.Text, heading.Level == 1 ? "Title" : "Heading" + (heading.Level - 1), heading.NewPage);
                    if (heading.Level == 2)
                    {
                        headingParagraph.Elements().First().AddAfterSelf(new XElement(W + "bookmarkStart", Attribute("id", ++section), Attribute("name", $"section{section:D3}")));
                        headingParagraph.Add(new XElement(W + "bookmarkEnd", Attribute("id", section)));
                    }
                    body.Add(headingParagraph);
                    break;
                case ReportParagraph paragraph:
                    body.Add(Paragraph(paragraph.Text, paragraph.Style.ToString()));
                    break;
                case ReportContents contents:
                    body.Add(Paragraph("目录", "Heading2"));
                    foreach (var entry in contents.Entries)
                        body.Add(new XElement(W + "p", new XElement(W + "pPr", new XElement(W + "pStyle", Attribute("val", "Contents"))),
                            new XElement(W + "hyperlink", Attribute("anchor", $"section{Array.IndexOf(headings, entry) + 1:D3}"),
                                new XElement(W + "r", new XElement(W + "t", entry.Text)))));
                    break;
                case ReportCode code:
                    foreach (var line in code.Text.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
                    {
                        token.ThrowIfCancellationRequested();
                        body.Add(Paragraph(line, "Code"));
                    }
                    break;
                case ReportTable table:
                    body.Add(Table(table, token));
                    body.Add(Paragraph(""));
                    break;
                case ReportPicture picture:
                    using (var source = DesignReportImage.Open(images, picture.FileName))
                    {
                        imageBytes += source.Length;
                        if (imageBytes > BoundedApplicationFile.MaximumExportBytes) throw new InvalidDataException("报告图片超过大小上限。");
                        using var data = SKData.Create(source);
                        using var image = DesignReportImage.Decode(data);
                        var number = ++imageNumber;
                        var id = "image" + number;
                        relationships.Add(Relationship(id, "image", "media/" + id + ".png"));
                        source.Position = 0;
                        using var target = zip.CreateEntry("word/media/" + id + ".png", CompressionLevel.Fastest).Open();
                        var buffer = new byte[81920];
                        int read;
                        while ((read = source.Read(buffer)) > 0) { token.ThrowIfCancellationRequested(); target.Write(buffer, 0, read); }
                        body.Add(Picture(picture.Title, id, number, image.Width, image.Height));
                        body.Add(Paragraph($"图 {imageNumber}　{picture.Title}", "Caption"));
                    }
                    break;
            }
        }
        body.Add(new XElement(W + "sectPr",
            new XElement(W + "headerReference", Attribute("type", "default"), new XAttribute(R + "id", "header")),
            new XElement(W + "footerReference", Attribute("type", "default"), new XAttribute(R + "id", "footer")),
            new XElement(W + "pgSz", Attribute("w", 11906), Attribute("h", 16838)),
            new XElement(W + "pgMar", Attribute("top", 1020), Attribute("right", 1020), Attribute("bottom", 1020), Attribute("left", 1020), Attribute("header", 480), Attribute("footer", 480))));
        Save("word/document.xml", new XElement(W + "document", new XAttribute(XNamespace.Xmlns + "w", W),
            new XAttribute(XNamespace.Xmlns + "r", R), body));
        Save("word/styles.xml", Styles());
        Save("word/header.xml", new XElement(W + "hdr", Paragraph("Optical System Design　|　光学设计报告", "Header")));
        Save("word/footer.xml", new XElement(W + "ftr", new XElement(W + "p",
            new XElement(W + "pPr", new XElement(W + "pStyle", Attribute("val", "Footer"))),
            new XElement(W + "fldSimple", Attribute("instr", "PAGE"), new XElement(W + "r", new XElement(W + "t", "1"))),
            new XElement(W + "r", new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), " / ")),
            new XElement(W + "fldSimple", Attribute("instr", "NUMPAGES"), new XElement(W + "r", new XElement(W + "t", "1"))))));
        Save("word/_rels/document.xml.rels", relationships);
        Save("_rels/.rels", new XElement(Rel + "Relationships", Relationship("document", "officeDocument", "word/document.xml")));
        XNamespace types = "http://schemas.openxmlformats.org/package/2006/content-types";
        Save("[Content_Types].xml", new XElement(types + "Types",
            new XElement(types + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(types + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            new XElement(types + "Default", new XAttribute("Extension", "png"), new XAttribute("ContentType", "image/png")),
            new XElement(types + "Override", new XAttribute("PartName", "/word/document.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")),
            new XElement(types + "Override", new XAttribute("PartName", "/word/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml")),
            new XElement(types + "Override", new XAttribute("PartName", "/word/header.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml")),
            new XElement(types + "Override", new XAttribute("PartName", "/word/footer.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml"))));
        token.ThrowIfCancellationRequested();

        void Save(string name, XElement xml)
        {
            token.ThrowIfCancellationRequested();
            using var stream = zip.CreateEntry(name, CompressionLevel.Fastest).Open();
            using var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false) });
            xml.Save(writer);
        }
    }

    private static XElement Paragraph(string text, string style = "Normal", bool newPage = false) => new(W + "p",
        new XElement(W + "pPr", new XElement(W + "pStyle", Attribute("val", style)), newPage ? new XElement(W + "pageBreakBefore") : null),
        text.Replace("\r", "", StringComparison.Ordinal).Split('\n').Select((line, i) => new XElement(W + "r",
            i == 0 ? null : new XElement(W + "br"),
            new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), line))));

    private static XElement Table(ReportTable table, CancellationToken token)
    {
        var widths = DesignReportImage.ColumnWeights(table).Select(weight => (int)Math.Floor(weight * ContentWidth)).ToArray();
        widths[^1] += ContentWidth - widths.Sum();
        var result = new XElement(W + "tbl",
            new XElement(W + "tblPr", new XElement(W + "tblW", Attribute("w", ContentWidth), Attribute("type", "dxa")),
                new XElement(W + "tblBorders", new[] { "top", "left", "bottom", "right", "insideH", "insideV" }.Select(edge =>
                    new XElement(W + edge, Attribute("val", "single"), Attribute("sz", 4), Attribute("color", "D9E3DE")))),
                new XElement(W + "tblLayout", Attribute("type", "fixed")),
                new XElement(W + "tblCellMar", new[] { "top", "left", "bottom", "right" }.Select(edge =>
                    new XElement(W + edge, Attribute("w", 80), Attribute("type", "dxa"))))),
            new XElement(W + "tblGrid", widths.Select(width => new XElement(W + "gridCol", Attribute("w", width)))));
        AddRow(table.Columns, true, 0);
        for (var i = 0; i < table.Rows.Length; i++) { token.ThrowIfCancellationRequested(); AddRow(table.Rows[i], false, i); }
        return result;

        void AddRow(string[] cells, bool header, int index)
        {
            result.Add(new XElement(W + "tr", header ? new XElement(W + "trPr", new XElement(W + "tblHeader")) : null,
                cells.Select((cell, column) => new XElement(W + "tc",
                    new XElement(W + "tcPr", new XElement(W + "tcW", Attribute("w", widths[column]), Attribute("type", "dxa")),
                        new XElement(W + "shd", Attribute("fill", header ? "EAF1EF" : index % 2 == 0 ? "FFFFFF" : "F7FAF8")),
                        new XElement(W + "vAlign", Attribute("val", "center"))),
                    Paragraph(cell, header ? "TableHeader" : "TableBody")))));
        }
    }

    private static XElement Picture(string title, string id, int number, int width, int height)
    {
        var scale = Math.Min(ContentWidth / 20d / width, 650d / height);
        var cx = (long)(width * scale * 12700); var cy = (long)(height * scale * 12700);
        var picture = new XElement(Pic + "pic",
            new XElement(Pic + "nvPicPr", new XElement(Pic + "cNvPr", new XAttribute("id", number), new XAttribute("name", title)), new XElement(Pic + "cNvPicPr")),
            new XElement(Pic + "blipFill", new XElement(A + "blip", new XAttribute(R + "embed", id)), new XElement(A + "stretch", new XElement(A + "fillRect"))),
            new XElement(Pic + "spPr",
                new XElement(A + "xfrm", new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)), new XElement(A + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy))),
                new XElement(A + "prstGeom", new XAttribute("prst", "rect"), new XElement(A + "avLst"))));
        var inline = new XElement(Wp + "inline",
            new XElement(Wp + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
            new XElement(Wp + "docPr", new XAttribute("id", number), new XAttribute("name", title), new XAttribute("descr", title)),
            new XElement(Wp + "cNvGraphicFramePr", new XElement(A + "graphicFrameLocks", new XAttribute("noChangeAspect", 1))),
            new XElement(A + "graphic", new XElement(A + "graphicData", new XAttribute("uri", Pic.NamespaceName), picture)));
        return new XElement(W + "p", new XElement(W + "pPr", new XElement(W + "keepNext")),
            new XElement(W + "r", new XElement(W + "drawing", inline)));
    }

    private static XElement Styles()
    {
        var styles = new XElement(W + "styles",
            new XElement(W + "docDefaults", new XElement(W + "rPrDefault", new XElement(W + "rPr",
                new XElement(W + "rFonts", Attribute("ascii", "Arial"), Attribute("hAnsi", "Arial"), Attribute("eastAsia", "Microsoft YaHei")),
                new XElement(W + "color", Attribute("val", "000000")), new XElement(W + "sz", Attribute("val", 21)))),
                new XElement(W + "pPrDefault", new XElement(W + "pPr", new XElement(W + "spacing", Attribute("after", 120), Attribute("line", 300), Attribute("lineRule", "auto"))))));
        Style("Normal", 21);
        Style("Title", 50, true, true);
        Style("Heading1", 34, true, true);
        Style("Heading2", 26, true, true);
        Style("Subtitle", 30);
        Style("Muted", 18);
        Style("Warning", 21, true);
        Style("Contents", 21);
        Style("Header", 16);
        Style("Footer", 16);
        Style("Code", 17);
        Style("TableBody", 17);
        Style("TableHeader", 18, true);
        Style("Caption", 18);
        return styles;

        void Style(string id, int size, bool bold = false, bool keep = false)
        {
            styles.Add(new XElement(W + "style", Attribute("type", "paragraph"), Attribute("styleId", id),
                id == "Normal" ? Attribute("default", 1) : null,
                new XElement(W + "name", Attribute("val", id)), id == "Normal" ? null : new XElement(W + "basedOn", Attribute("val", "Normal")),
                new XElement(W + "pPr", keep ? new XElement(W + "keepNext") : null,
                    new XElement(W + "spacing", Attribute("before", keep ? 240 : 0), Attribute("after", id == "Code" ? 0 : id == "Normal" ? 160 : 120)),
                    id is "Caption" or "Footer" ? new XElement(W + "jc", Attribute("val", "center")) : null,
                    id == "Heading1" || id == "Heading2" ? new XElement(W + "outlineLvl", Attribute("val", id == "Heading1" ? 0 : 1)) : null),
                new XElement(W + "rPr", bold ? new XElement(W + "b") : null,
                    id == "Code" ? new XElement(W + "rFonts", Attribute("ascii", "Courier New"), Attribute("hAnsi", "Courier New")) : null,
                    new XElement(W + "sz", Attribute("val", size)), new XElement(W + "color", Attribute("val", id switch
                    { "TableHeader" => "2F4B42", "TableBody" => "2D3431", "Muted" or "Caption" or "Header" or "Footer" => "637186", "Warning" => "9B352F", "Code" => "38485B", _ => "000000" })))));
        }
    }

    private static XAttribute Attribute(string name, object value) => new(W + name, Convert.ToString(value, CultureInfo.InvariantCulture)!);
    private static XElement Relationship(string id, string type, string target) => new(Rel + "Relationship",
        new XAttribute("Id", id), new XAttribute("Type", R.NamespaceName + "/" + type), new XAttribute("Target", target));
}

internal static class DesignReportImage
{
    internal static FileStream Open(string directory, string fileName)
    {
        if (Path.GetFileName(fileName) != fileName) throw new InvalidDataException("报告图片路径无效。");
        return BoundedApplicationFile.OpenRead(Path.Combine(directory, fileName), BoundedApplicationFile.MaximumImageDataBytes, "Report image");
    }

    internal static SKImage Decode(SKData data)
    {
        var image = SKImage.FromEncodedData(data) ?? throw new InvalidDataException("报告图片无效。");
        if (image.Width <= 0 || image.Height <= 0 || image.Width > 32768 || image.Height > 32768 || (long)image.Width * image.Height > 33_554_432)
        { image.Dispose(); throw new InvalidDataException("报告图片超过尺寸上限。"); }
        return image;
    }

    internal static double[] ColumnWeights(ReportTable table)
    {
        var weights = table.Columns.Select((column, i) => Math.Clamp(Math.Sqrt(Math.Max(column.Length * 2,
            table.Rows.Select(row => row[i].Length).DefaultIfEmpty(0).Max())), 2.5, 7)).ToArray();
        var total = weights.Sum();
        return weights.Select(weight => weight / total).ToArray();
    }
}
