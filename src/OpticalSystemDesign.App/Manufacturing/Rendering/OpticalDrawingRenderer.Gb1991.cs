using SkiaSharp;

namespace OpticalSystemDesign.App.Manufacturing;

internal static partial class OpticalDrawingRendererCore
{
    internal sealed record Gb1991PageRegions(SKRect Frame, SKRect Geometry, SKRect Requirements, SKRect Notes, SKRect Title);

    private static OpticalDrawingGb1991Template Gb1991Template(OpticalDrawingSheet sheet) =>
        OpticalDrawingTemplateCatalog.For(sheet.Standard).Specification.Gb1991
        ?? throw new InvalidDataException("老国标模板缺少表2/表3设置。");

    internal static Gb1991PageRegions Gb1991Regions(OpticalDrawingSheet sheet)
    {
        var template = OpticalDrawingTemplateCatalog.For(sheet.Standard);
        var gb = Gb1991Template(sheet);
        var (width, height) = PageDimensions(sheet.PageSize);
        var inner = template.Page.InnerMargin;
        var titleTop = height - (A4Height - template.Page.TitleTop);
        var notesTop = height - (A4Height - template.Page.SpecificationTop);
        var rows = sheet.Element.IsCemented ? gb.AssemblyRows.Count : gb.MaterialRows.Count + gb.PartRows.Count;
        var headers = sheet.Element.IsCemented ? 1 : 2;
        var tableHeight = (headers * gb.HeaderHeightMillimeters + rows * gb.RowHeightMillimeters) * MillimetersToPoints;
        var tableWidth = gb.TableWidthMillimeters * MillimetersToPoints;
        return new Gb1991PageRegions(
            new SKRect(inner, inner, width - inner, height - inner),
            new SKRect(inner + template.Geometry.HorizontalInset, inner + template.Geometry.TopInset,
                width - inner - tableWidth - 12, notesTop - template.Geometry.SpecificationGap),
            new SKRect(width - inner - tableWidth, inner, width - inner, inner + tableHeight),
            new SKRect(inner + 8, notesTop + 8, width - inner - 8, titleTop - 8),
            new SKRect(inner, titleTop, width - inner, height - inner));
    }

    private static void RenderGb1991Layout(SKCanvas canvas, OpticalDrawingSheet sheet, float pageWidth, float pageHeight)
    {
        var template = OpticalDrawingTemplateCatalog.For(sheet.Standard);
        var regions = Gb1991Regions(sheet);
        using var thin = Stroke(SKColors.Black, 0.65f);
        using var medium = Stroke(SKColors.Black, 1.05f);
        using var heavy = Stroke(SKColors.Black, 1.7f);
        using var dimension = Stroke(new SKColor(43, 43, 46), 0.7f);
        using var hatch = Stroke(new SKColor(112, 142, 164), 0.42f);
        using var axis = Stroke(new SKColor(110, 110, 116), 0.6f);
        axis.PathEffect = SKPathEffect.CreateDash(OpticalAxisDashPattern(sheet.Standard), 0);
        var outer = template.Page.OuterMargin;
        canvas.DrawRect(outer, outer, pageWidth - 2 * outer, pageHeight - 2 * outer, heavy);
        canvas.DrawRect(regions.Frame, thin);
        var scale = DrawElementGeometry(canvas, sheet, regions.Geometry, medium, dimension, axis, hatch);
        DrawGb1991Requirements(canvas, sheet, regions.Requirements, thin, medium);
        DrawText(canvas, "技术要求：", regions.Notes.Left, regions.Notes.Top + 10, 8, SKTextAlign.Left, true);
        var lineY = regions.Notes.Top + 23;
        foreach (var line in Gb1991TechnicalRequirementLines(sheet).SelectMany(line => WrapGb1991Text(line, regions.Notes.Width, 7)))
        {
            DrawText(canvas, line, regions.Notes.Left, lineY, 7, SKTextAlign.Left);
            lineY += 11;
        }
        DrawGb1991Roughness(canvas, sheet, regions.Notes.Left, lineY, thin);
        DrawTitleBlock(canvas, sheet, regions.Title.Left, regions.Title.Top, regions.Title.Width, regions.Title.Height,
            thin, medium, scale);
    }

    private static void DrawGb1991Requirements(SKCanvas canvas, OpticalDrawingSheet sheet, SKRect bounds, SKPaint thin, SKPaint medium)
    {
        var gb = Gb1991Template(sheet);
        var y = bounds.Top;
        if (!sheet.Element.IsCemented)
            DrawSection("对材料的要求", Gb1991MaterialSpecificationRows(sheet), gb.MaterialLabelWidthMillimeters);
        DrawSection(sheet.Element.IsCemented ? "对胶合件的要求" : "对零件的要求",
            Gb1991PartSpecificationRows(sheet), gb.PartLabelWidthMillimeters);
        canvas.DrawRect(bounds, medium);

        void DrawSection(string title, IReadOnlyList<(string Item, string Value)> rows, float labelWidthMillimeters)
        {
            var headerHeight = gb.HeaderHeightMillimeters * MillimetersToPoints;
            DrawFittedText(canvas, title, bounds.MidX, y + (headerHeight + 7.8f) / 2 - 1,
                bounds.Width - 8, 7.8f, SKTextAlign.Center, true);
            canvas.DrawLine(bounds.Left, y, bounds.Right, y, medium);
            y += headerHeight;
            canvas.DrawLine(bounds.Left, y, bounds.Right, y, thin);
            var split = bounds.Left + labelWidthMillimeters * MillimetersToPoints;
            var sectionTop = y;
            var rowHeight = gb.RowHeightMillimeters * MillimetersToPoints;
            foreach (var (item, value) in rows)
            {
                DrawGb1991ItemLabel(canvas, item, new SKRect(bounds.Left, y, split, y + rowHeight));
                DrawGb1991Cell(canvas, value, new SKRect(split, y, bounds.Right, y + rowHeight));
                y += rowHeight;
                canvas.DrawLine(bounds.Left, y, bounds.Right, y, thin);
            }
            canvas.DrawLine(split, sectionTop, split, y, thin);
        }
    }

    private static void DrawGb1991ItemLabel(SKCanvas canvas, string text, SKRect bounds)
    {
        var split = text.IndexOf('_');
        if (split < 0)
        {
            DrawFittedText(canvas, text, bounds.MidX, bounds.MidY + 2.5f, bounds.Width - 5, 7.4f, SKTextAlign.Center);
            return;
        }
        var prefix = text[..split];
        var suffix = text[(split + 1)..];
        var prefixWidth = MeasureText(prefix, 7.4f, false);
        var suffixWidth = MeasureText(suffix, 5, false);
        var x = bounds.MidX - (prefixWidth + suffixWidth) / 2;
        DrawText(canvas, prefix, x, bounds.MidY + 2, 7.4f, SKTextAlign.Left);
        DrawText(canvas, suffix, x + prefixWidth, bounds.MidY + 4, 5, SKTextAlign.Left);
    }

    private static void DrawGb1991Cell(SKCanvas canvas, string value, SKRect bounds)
    {
        const float size = 6.8f;
        var lines = WrapGb1991Text(value, bounds.Width - 6, size);
        var baseline = lines.Count == 1 ? bounds.MidY + 2.3f : bounds.Top + size + 0.5f;
        canvas.Save();
        canvas.ClipRect(new SKRect(bounds.Left + 2, bounds.Top + 0.5f, bounds.Right - 2, bounds.Bottom - 0.5f));
        foreach (var line in lines)
        {
            DrawText(canvas, line, bounds.MidX, baseline, size, SKTextAlign.Center);
            baseline += 7.8f;
        }
        canvas.Restore();
    }

    private static IReadOnlyList<string> WrapGb1991Text(string text, float width, float size)
    {
        var lines = new List<string>();
        var current = "";
        foreach (var character in text.Replace("\r", "", StringComparison.Ordinal))
        {
            if (character == '\n') { lines.Add(current); current = ""; continue; }
            var next = current + character;
            if (current.Length > 0 && MeasureText(next, size, false) > width)
            {
                lines.Add(current);
                current = character.ToString();
            }
            else current = next;
        }
        if (current.Length > 0 || lines.Count == 0) lines.Add(current);
        return lines;
    }

    internal static IReadOnlyList<(string Item, string Value)> Gb1991MaterialSpecificationRows(OpticalDrawingSheet sheet) =>
        Gb1991Template(sheet).MaterialRows.Select(row => (row.Item, ResolveGb1991Field(row.ValueBinding, sheet))).ToArray();

    internal static IReadOnlyList<(string Item, string Value)> Gb1991PartSpecificationRows(OpticalDrawingSheet sheet) =>
        (sheet.Element.IsCemented ? Gb1991Template(sheet).AssemblyRows : Gb1991Template(sheet).PartRows)
            .Select(row => (row.Item, ResolveGb1991Field(row.ValueBinding, sheet))).ToArray();

    internal static IReadOnlyList<string> Gb1991TechnicalRequirementLines(OpticalDrawingSheet sheet)
    {
        var lines = new List<string>();
        for (var index = 0; index < sheet.Element.Components.Count; index++)
        {
            var component = sheet.Element.Components[index];
            var material = sheet.ComponentMaterialData?.ElementAtOrDefault(index) ?? (index == 0 ? sheet.MaterialData : null);
            var label = sheet.Element.IsCemented ? $"L{index + 1} " : "";
            lines.Add($"{label}材料：{component.Material}；{material?.Manufacturer ?? "目录未解析"}");
            var indexTolerance = string.IsNullOrWhiteSpace(sheet.LegacyGb1991?.RefractiveIndexGrade)
                ? $" ±{sheet.RefractiveIndexTolerance:0.000000}" : "";
            var abbeTolerance = string.IsNullOrWhiteSpace(sheet.LegacyGb1991?.AbbeNumberGrade)
                ? $" ±{sheet.AbbeNumberTolerance:0.###}" : "";
            lines.Add(label + (material is null ? "n[d]、V[d] 未解析"
                : $"n[d] {material.RefractiveIndexD:0.000000}{indexTolerance}；V[d] {material.AbbeNumber:0.###}{abbeTolerance}"));
        }
        lines.Add("检验依据：面形 GB 2831—1981；定心 GB 7242—1987；疵病 GB 1185—1989；另有约定按技术要求。");
        var requirements = sheet.LegacyGb1991 ?? new LegacyGb1991Requirements();
        if (new[] { requirements.RefractiveIndexGrade, requirements.AbbeNumberGrade, requirements.HomogeneityGrade,
            requirements.AbsorptionGrade, requirements.StressGrade, requirements.StriaeGrade, requirements.BubbleGrade }
            .Any(value => !string.IsNullOrWhiteSpace(value)))
            lines.Add($"材料等级依据：{(string.IsNullOrWhiteSpace(requirements.MaterialQualityStandard) ? "未规定" : requirements.MaterialQualityStandard.Trim())}");
        lines.Add($"光学检验区域为D0；分面要求按S面号；N、ΔN检验波长：{(requirements.FringeReferenceWavelengthNanometers is { } wavelength ? $"{wavelength:0.######} nm" : "未规定")}。");
        lines.Add($"χ为光学表面面倾角；定心基准：{(string.IsNullOrWhiteSpace(requirements.CenteringDatum) ? "未规定" : requirements.CenteringDatum.Trim())}。");
        lines.Add($"补充面形限值：S1 {sheet.FrontSurfaceFormNanometers:0.###} nm；S{sheet.Element.Surfaces.Count} {sheet.BackSurfaceFormNanometers:0.###} nm（不换算为N、ΔN）");
        lines.Add($"补充表面纹理：Rq {sheet.SurfaceTextureNanometers:0.######} nm（独立于Ra）");
        lines.AddRange(Gb1991GeometryDefinitionLines(sheet));
        var coatings = sheet.Element.Surfaces.Select(surface => CoatingText(sheet, surface)).Distinct(StringComparer.Ordinal).ToArray();
        lines.Add(coatings.Length == 1 ? $"镀膜：{coatings[0]}" : "镀膜：" + string.Join("；", sheet.Element.Surfaces.Select((surface, index) => $"S{index + 1} {CoatingText(sheet, surface)}")));
        lines.Add($"边缘：{sheet.EdgeTreatment}");
        if (!string.IsNullOrWhiteSpace(requirements.TechnicalRequirements))
            lines.Add(requirements.TechnicalRequirements.Trim());
        return lines;
    }

    private static string ResolveGb1991Field(string binding, OpticalDrawingSheet sheet)
    {
        var values = sheet.LegacyGb1991 ?? new LegacyGb1991Requirements();
        return binding switch
        {
            "gb1991.material.indexTolerance" => Specified(values.RefractiveIndexGrade),
            "gb1991.material.abbeTolerance" => Specified(values.AbbeNumberGrade),
            "gb1991.material.homogeneity" => Specified(values.HomogeneityGrade),
            "gb1991.material.absorption" => Specified(values.AbsorptionGrade),
            "gb1991.material.stress" => Specified(values.StressGrade),
            "gb1991.material.striae" => Specified(values.StriaeGrade),
            "gb1991.material.bubbles" => Specified(values.BubbleGrade),
            "gb1991.part.powerFringes" => Specified(values.PowerFringes),
            "gb1991.part.irregularityFringes" => Specified(values.IrregularityFringes),
            "gb1991.part.testPlate" => Specified(values.TestPlateGrade).ToUpperInvariant().Replace("：", ":", StringComparison.Ordinal),
            "gb1991.part.centering" => $"{sheet.CenteringToleranceArcMinutes:0.###}′",
            "gb1991.part.imperfection" => Specified(values.SurfaceImperfection),
            "gb1991.part.prismNotApplicable" => "不适用",
            "gb1991.part.centerThickness" => $"{sheet.Element.CenterThickness:0.###}\n{sheet.CenterThicknessUpperDeviation:+0.###;-0.###;0}/{sheet.CenterThicknessLowerDeviation:+0.###;-0.###;0}",
            "gb1991.part.focalLength" => Specified(values.FocalLength),
            "gb1991.part.frontFocalDistance" => Specified(values.FrontFocalDistance),
            "gb1991.part.backFocalDistance" => Specified(values.BackFocalDistance),
            "gb1991.part.clearAperture" => TryGb1991EffectiveSemiDiameter(sheet, out var aperture, out _)
                ? "⌀" + (aperture * 2).ToString("G12", System.Globalization.CultureInfo.InvariantCulture) : "未定义",
            _ => throw new InvalidDataException($"未注册的老国标字段：{binding}")
        };
        static string Specified(string? text, string fallback = "未规定") => string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();
    }

    private static IReadOnlyList<string> ValidateGb1991Layout(OpticalDrawingSheet sheet)
    {
        var issues = new List<string>();
        var gb = Gb1991Template(sheet);
        var regions = Gb1991Regions(sheet);
        if (gb.MaterialRows.Count != 7 || gb.PartRows.Count != 14 || gb.AssemblyRows.Count != 11)
            issues.Add("老国标表2/表3的材料、零件或胶合件栏目不完整。");
        if (gb.TableWidthMillimeters != 40 || gb.MaterialLabelWidthMillimeters != 25 || gb.PartLabelWidthMillimeters != 15
            || gb.HeaderHeightMillimeters != 8 || gb.RowHeightMillimeters != 6)
            issues.Add("老国标表2/表3的40/25/15 mm列宽或8/6 mm行高不符。");
        if (regions.Geometry.Right >= regions.Requirements.Left || regions.Requirements.Bottom >= regions.Notes.Top
            || regions.Notes.Bottom >= regions.Title.Top || regions.Geometry.Width < 250)
            issues.Add("老国标视图、右上要求表、技术要求或标题栏发生重叠。");
        if (!sheet.Element.IsCemented) CheckRows(Gb1991MaterialSpecificationRows(sheet), gb.MaterialLabelWidthMillimeters);
        CheckRows(Gb1991PartSpecificationRows(sheet), gb.PartLabelWidthMillimeters);
        var noteLines = Gb1991TechnicalRequirementLines(sheet).SelectMany(line => WrapGb1991Text(line, regions.Notes.Width, 7)).Count();
        if (23 + noteLines * 11 + Gb1991RoughnessHeight(sheet) > regions.Notes.Height)
            issues.Add("老国标技术要求文字过长，请精简说明或另附技术文件。");
        issues.AddRange(ValidateGb1991Annotations(sheet));
        return issues;

        void CheckRows(IReadOnlyList<(string Item, string Value)> rows, float labelWidth)
        {
            var width = (gb.TableWidthMillimeters - labelWidth) * MillimetersToPoints - 6;
            foreach (var (item, value) in rows)
                if (WrapGb1991Text(value, width, 6.8f).Count > 2)
                    issues.Add($"老国标 {item} 单元格超过两行，请将详细要求移到技术说明。");
        }
    }

    private static readonly IReadOnlySet<string> KnownGb1991Fields = new HashSet<string>(StringComparer.Ordinal)
    {
        "gb1991.material.indexTolerance", "gb1991.material.abbeTolerance", "gb1991.material.homogeneity",
        "gb1991.material.absorption", "gb1991.material.stress", "gb1991.material.striae", "gb1991.material.bubbles",
        "gb1991.part.powerFringes", "gb1991.part.irregularityFringes", "gb1991.part.testPlate", "gb1991.part.centering",
        "gb1991.part.imperfection", "gb1991.part.prismNotApplicable", "gb1991.part.centerThickness", "gb1991.part.focalLength",
        "gb1991.part.frontFocalDistance", "gb1991.part.backFocalDistance", "gb1991.part.clearAperture"
    };
}
