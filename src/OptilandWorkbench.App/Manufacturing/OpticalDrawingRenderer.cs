using System.Collections.Concurrent;
using OptilandWorkbench.Application.Contracts;
using SkiaSharp;

namespace OptilandWorkbench.App.Manufacturing;

public static class OpticalDrawingRenderer
{
    private const float MillimetersToPoints = 72f / 25.4f;
    private const float A4Width = 210 * MillimetersToPoints;
    private const float A4Height = 297 * MillimetersToPoints;
    private static readonly Lazy<SKTypeface> ChineseTypeface = new(ResolveChineseTypeface);
    private static readonly ConcurrentDictionary<int, SKTypeface> FallbackTypefaces = new();

    public static byte[] RenderPreview(OpticalDrawingSheet sheet, int width = 1500)
    {
        var (pageWidth, pageHeight) = PageDimensions(sheet.PageSize);
        var height = Math.Max(1, (int)Math.Round(width * pageHeight / pageWidth));
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Scale(width / pageWidth, height / pageHeight);
        Render(canvas, sheet, pageWidth, pageHeight);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static void ExportPdf(string path, OpticalDrawingSheet sheet)
    {
        var (pageWidth, pageHeight) = PageDimensions(sheet.PageSize);
        using var stream = File.Create(path);
        using var document = SKDocument.CreatePdf(stream);
        var canvas = document.BeginPage(pageWidth, pageHeight);
        Render(canvas, sheet, pageWidth, pageHeight);
        document.EndPage();
        document.Close();
    }

    public static (float Width, float Height) PageDimensions(OpticalDrawingPageSize pageSize) =>
        pageSize == OpticalDrawingPageSize.A3
            ? (297 * MillimetersToPoints, 420 * MillimetersToPoints)
            : (A4Width, A4Height);

    private static void Render(
        SKCanvas canvas,
        OpticalDrawingSheet sheet,
        float pageWidth,
        float pageHeight)
    {
        canvas.Clear(SKColors.White);
        canvas.Save();
        canvas.Scale(pageWidth / A4Width, pageHeight / A4Height);
        RenderA4Layout(canvas, sheet);
        canvas.Restore();
    }

    private static void RenderA4Layout(SKCanvas canvas, OpticalDrawingSheet sheet)
    {
        using var thin = Stroke(SKColors.Black, 0.65f);
        using var medium = Stroke(SKColors.Black, 1.05f);
        using var heavy = Stroke(SKColors.Black, 1.7f);
        using var dimension = Stroke(new SKColor(43, 43, 46), 0.7f);
        using var hatch = Stroke(new SKColor(112, 142, 164), 0.42f);
        using var axis = Stroke(new SKColor(110, 110, 116), 0.6f);
        axis.PathEffect = SKPathEffect.CreateDash(new[] { 9f, 4f, 2f, 4f }, 0);
        using var headerFill = new SKPaint
        {
            Color = new SKColor(242, 244, 247),
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };

        const float outer = 12;
        const float inner = 18;
        const float specificationTop = 576;
        const float titleTop = 754;
        var right = A4Width - inner;
        var bottom = A4Height - inner;

        canvas.DrawRect(outer, outer, A4Width - (outer * 2), A4Height - (outer * 2), heavy);
        canvas.DrawRect(inner, inner, A4Width - (inner * 2), A4Height - (inner * 2), thin);

        DrawText(canvas, "光学零件图（ISO 10110 系列参考）", inner + 10, inner + 22, 12, SKTextAlign.Left, true);
        DrawText(
            canvas,
            "单位：mm    投影：第一角法    未注公差按企业工艺规范",
            right - 8,
            inner + 21,
            6.8f,
            SKTextAlign.Right);

        DrawElementGeometry(
            canvas,
            sheet,
            new SKRect(inner + 12, inner + 31, right - 12, specificationTop - 7),
            medium,
            dimension,
            axis,
            hatch);

        DrawSpecificationTable(
            canvas,
            sheet,
            inner,
            specificationTop,
            right - inner,
            titleTop - specificationTop,
            thin,
            medium,
            headerFill);
        DrawTitleBlock(
            canvas,
            sheet,
            inner,
            titleTop,
            right - inner,
            bottom - titleTop,
            thin,
            medium);
    }

    private static void DrawElementGeometry(
        SKCanvas canvas,
        OpticalDrawingSheet sheet,
        SKRect area,
        SKPaint outline,
        SKPaint dimension,
        SKPaint axis,
        SKPaint hatch)
    {
        var element = sheet.Element;
        var centerX = area.MidX;
        var centerY = area.Top + (area.Height * 0.50f);
        var semiDiameter = Math.Max(0.1, element.ClearSemiDiameter);
        var drawingScale = Math.Clamp(160f / (float)(semiDiameter * 2), 0.4f, 24f);
        var yScale = drawingScale;
        var xScale = drawingScale;
        var centerThickness = Math.Max(0.1, element.CenterThickness);
        var frontVertexX = centerX - ((float)centerThickness * xScale / 2);
        var backVertexX = centerX + ((float)centerThickness * xScale / 2);
        var frontPoints = SurfacePoints(
            element.FrontSurface.Radius,
            element.FrontSurface.Conic,
            semiDiameter,
            frontVertexX,
            centerY,
            xScale,
            yScale);
        var backPoints = SurfacePoints(
            element.BackSurface.Radius,
            element.BackSurface.Conic,
            semiDiameter,
            backVertexX,
            centerY,
            xScale,
            yScale);

        using var lens = new SKPath();
        lens.MoveTo(frontPoints[0]);
        foreach (var point in frontPoints.Skip(1))
        {
            lens.LineTo(point);
        }

        foreach (var point in backPoints.Reverse())
        {
            lens.LineTo(point);
        }

        lens.Close();
        DrawOpticalGlassHatch(canvas, lens, hatch);
        canvas.DrawPath(lens, outline);
        canvas.DrawLine(area.Left + 8, centerY, area.Right - 8, centerY, axis);
        DrawText(canvas, "光轴", area.Right - 9, centerY - 5, 6.2f, SKTextAlign.Right);

        var topY = centerY - ((float)semiDiameter * yScale);
        var bottomY = centerY + ((float)semiDiameter * yScale);
        var leftEdge = Math.Min(frontPoints[0].X, frontPoints[^1].X);
        var rightEdge = Math.Max(backPoints[0].X, backPoints[^1].X);

        DrawVerticalDimension(
            canvas,
            leftEdge - 30,
            topY,
            bottomY,
            leftEdge,
            $"⌀{element.Diameter:0.###} ±{sheet.DiameterTolerance:0.###}",
            dimension);
        DrawVerticalDimension(
            canvas,
            rightEdge + 34,
            topY + 9,
            bottomY - 9,
            rightEdge,
            $"(⌀{element.ClearSemiDiameter * 2:0.###})",
            dimension);

        DrawHorizontalDimension(
            canvas,
            frontVertexX,
            backVertexX,
            bottomY + 35,
            bottomY,
            $"中心厚度 {element.CenterThickness:0.###} ±{sheet.CenterThicknessTolerance:0.###}",
            dimension);

        var edgeThickness = OpticalManufacturingModel.MinimumEdgeThickness(element);
        DrawHorizontalDimension(
            canvas,
            frontPoints[0].X,
            backPoints[0].X,
            topY - 22,
            topY,
            $"(边厚 {(edgeThickness ?? double.NaN):0.###})",
            dimension);

        DrawSurfaceCallout(
            canvas,
            frontPoints[18],
            centerX - 126,
            area.Top + 122,
            $"S1  R {RadiusText(element.FrontSurface.Radius)}",
            $"3/ {sheet.FrontSurfaceFormNanometers:0.#} nm    7/ Rq {sheet.SurfaceTextureNanometers:0.###} nm",
            dimension,
            alignRight: false);
        DrawSurfaceCallout(
            canvas,
            backPoints[18],
            centerX + 126,
            area.Top + 122,
            $"S2  R {RadiusText(element.BackSurface.Radius)}",
            $"3/ {sheet.BackSurfaceFormNanometers:0.#} nm    7/ Rq {sheet.SurfaceTextureNanometers:0.###} nm",
            dimension,
            alignRight: true);

    }

    private static void DrawOpticalGlassHatch(SKCanvas canvas, SKPath lens, SKPaint hatch)
    {
        var bounds = lens.Bounds;
        const float halfLength = 4.6f;
        const float markOffset = 3.2f;
        const float clusterSpacingX = 27f;
        const float clusterSpacingY = 24f;
        var row = 0;

        for (var y = bounds.Top + 9; y <= bounds.Bottom - 7; y += clusterSpacingY, row++)
        {
            var rowOffset = row % 2 == 0 ? 0 : clusterSpacingX * 0.5f;
            for (var x = bounds.Left + 9 + rowOffset; x <= bounds.Right - 7; x += clusterSpacingX)
            {
                for (var mark = -1; mark <= 1; mark++)
                {
                    var centerX = x - (mark * markOffset);
                    var centerY = y + (mark * markOffset);
                    var startX = centerX - halfLength;
                    var startY = centerY - halfLength;
                    var endX = centerX + halfLength;
                    var endY = centerY + halfLength;
                    if (lens.Contains(startX, startY) && lens.Contains(endX, endY))
                    {
                        canvas.DrawLine(startX, startY, endX, endY, hatch);
                    }
                }
            }
        }
    }

    private static void DrawSpecificationTable(
        SKCanvas canvas,
        OpticalDrawingSheet sheet,
        float x,
        float y,
        float width,
        float height,
        SKPaint thin,
        SKPaint medium,
        SKPaint headerFill)
    {
        const float headerHeight = 24;
        var leftWidth = width * 0.35f;
        var materialWidth = width * 0.30f;
        var materialX = x + leftWidth;
        var rightX = materialX + materialWidth;
        canvas.DrawRect(x, y, width, headerHeight, headerFill);
        canvas.DrawRect(x, y, width, height, medium);
        canvas.DrawLine(x, y + headerHeight, x + width, y + headerHeight, thin);
        canvas.DrawLine(materialX, y, materialX, y + height, thin);
        canvas.DrawLine(rightX, y, rightX, y + height, thin);

        DrawText(canvas, "左表面（S1）", x + (leftWidth / 2), y + 16, 9, SKTextAlign.Center, true);
        DrawText(canvas, "材料", materialX + (materialWidth / 2), y + 16, 9, SKTextAlign.Center, true);
        DrawText(canvas, "右表面（S2）", rightX + ((width - leftWidth - materialWidth) / 2), y + 16, 9, SKTextAlign.Center, true);

        var bodyTop = y + headerHeight + 14;
        var leftLines = SurfaceSpecificationLines(sheet, sheet.Element.FrontSurface, isFront: true);
        var rightLines = SurfaceSpecificationLines(sheet, sheet.Element.BackSurface, isFront: false);
        var materialLines = MaterialSpecificationLines(sheet);
        DrawColumnLines(canvas, leftLines, x + 9, bodyTop, leftWidth - 18);
        DrawColumnLines(canvas, materialLines, materialX + 9, bodyTop, materialWidth - 18);
        DrawColumnLines(canvas, rightLines, rightX + 9, bodyTop, width - leftWidth - materialWidth - 18);
    }

    private static IReadOnlyList<string> SurfaceSpecificationLines(
        OpticalDrawingSheet sheet,
        SurfaceRowDto surface,
        bool isFront)
    {
        var coating = string.IsNullOrWhiteSpace(surface.Coating)
            || surface.Coating.Equals("None", StringComparison.OrdinalIgnoreCase)
                ? sheet.Coating
                : surface.Coating;
        return new[]
        {
            $"R      {RadiusText(surface.Radius)}    面型：{surface.GeometryKind}",
            $"⌀e     {sheet.Element.ClearSemiDiameter * 2:0.###}",
            $"保护倒边  {sheet.EdgeTreatment}",
            $"3/ 面形  {(isFront ? sheet.FrontSurfaceFormNanometers : sheet.BackSurfaceFormNanometers):0.#} nm",
            $"4/ 偏心/倾斜  ≤ {sheet.CenteringToleranceArcMinutes:0.###}′",
            $"5/ 表面疵病  {sheet.SurfaceImperfection}",
            $"7/ 表面纹理  Rq ≤ {sheet.SurfaceTextureNanometers:0.###} nm",
            $"λ/ 膜层  {coating}"
        };
    }

    private static IReadOnlyList<string> MaterialSpecificationLines(OpticalDrawingSheet sheet)
    {
        var material = sheet.MaterialData;
        return new[]
        {
            $"制造商  {material?.Manufacturer ?? "当前玻璃库"}",
            $"玻璃牌号  {sheet.Element.Material}",
            material is null ? "n[d]  由玻璃库解析" : $"n[d]  {material.RefractiveIndexD:0.000000}",
            material is null ? "V[d]  由玻璃库解析" : $"V[d]  {material.AbbeNumber:0.###}",
            material?.Density is { } density ? $"密度   {density:0.###} g/cm³" : "密度   玻璃库未提供",
            $"0/ 应力双折射  {sheet.StressBirefringence}",
            $"1/ 气泡和夹杂  {sheet.BubblesAndInclusions}",
            $"2/ 均匀性和条纹  {sheet.HomogeneityAndStriae}"
        };
    }

    private static void DrawColumnLines(
        SKCanvas canvas,
        IReadOnlyList<string> lines,
        float x,
        float y,
        float maxWidth)
    {
        const float lineHeight = 19;
        for (var index = 0; index < lines.Count; index++)
        {
            var lineY = y + (index * lineHeight);
            if (lines[index].StartsWith("n[d]", StringComparison.Ordinal)
                || lines[index].StartsWith("V[d]", StringComparison.Ordinal))
            {
                DrawSubscriptLine(canvas, lines[index][0], 'd', lines[index][4..].TrimStart(), x, lineY, maxWidth);
            }
            else
            {
                DrawFittedText(canvas, lines[index], x, lineY, maxWidth, 7.5f, SKTextAlign.Left);
            }
        }
    }

    private static void DrawSubscriptLine(
        SKCanvas canvas,
        char symbol,
        char subscript,
        string value,
        float x,
        float y,
        float maxWidth)
    {
        const float symbolSize = 7.5f;
        const float subscriptSize = 5.1f;
        DrawText(canvas, symbol.ToString(), x, y, symbolSize, SKTextAlign.Left);
        var symbolWidth = MeasureText(symbol.ToString(), symbolSize, bold: false);
        DrawText(canvas, subscript.ToString(), x + symbolWidth, y + 2.2f, subscriptSize, SKTextAlign.Left);
        var subscriptWidth = MeasureText(subscript.ToString(), subscriptSize, bold: false);
        var valueX = x + symbolWidth + subscriptWidth + 10;
        DrawFittedText(canvas, value, valueX, y, maxWidth - (valueX - x), symbolSize, SKTextAlign.Left);
    }

    private static void DrawTitleBlock(
        SKCanvas canvas,
        OpticalDrawingSheet sheet,
        float x,
        float y,
        float width,
        float height,
        SKPaint thin,
        SKPaint medium)
    {
        var brandWidth = width * 0.35f;
        var approvalWidth = width * 0.27f;
        var approvalX = x + brandWidth;
        var detailX = approvalX + approvalWidth;
        var detailWidth = width - brandWidth - approvalWidth;
        canvas.DrawRect(x, y, width, height, medium);
        canvas.DrawLine(approvalX, y, approvalX, y + height, thin);
        canvas.DrawLine(detailX, y, detailX, y + height, thin);

        DrawText(canvas, "ISO 10110", x + (brandWidth / 2), y + 24, 15, SKTextAlign.Center, true);
        DrawText(canvas, "光学零件图格式", x + (brandWidth / 2), y + 42, 10.5f, SKTextAlign.Center, true);
        DrawFittedText(
            canvas,
            "本图按设计数据自动生成；投产前须审核材料、尺寸链和检验方法。",
            x + 7,
            y + height - 8,
            brandWidth - 14,
            5.2f,
            SKTextAlign.Left);

        var approvalRow = height / 4;
        for (var index = 1; index < 4; index++)
        {
            canvas.DrawLine(approvalX, y + (approvalRow * index), detailX, y + (approvalRow * index), thin);
        }

        var approvalSplit = approvalX + (approvalWidth * 0.42f);
        canvas.DrawLine(approvalSplit, y + approvalRow, approvalSplit, y + height, thin);
        DrawText(canvas, "ISO 10110 系列参考", approvalX + (approvalWidth / 2), y + 13, 6.6f, SKTextAlign.Center, true);
        DrawText(canvas, "设计", approvalX + 7, y + approvalRow + 13, 6.4f, SKTextAlign.Left);
        DrawFittedText(canvas, sheet.Designer, approvalSplit + 5, y + approvalRow + 13, detailX - approvalSplit - 10, 6.4f, SKTextAlign.Left);
        DrawText(canvas, "审核", approvalX + 7, y + (approvalRow * 2) + 13, 6.4f, SKTextAlign.Left);
        DrawFittedText(canvas, sheet.Reviewer, approvalSplit + 5, y + (approvalRow * 2) + 13, detailX - approvalSplit - 10, 6.4f, SKTextAlign.Left);
        DrawText(canvas, "批准", approvalX + 7, y + (approvalRow * 3) + 13, 6.4f, SKTextAlign.Left);
        DrawText(canvas, "投影：第一角法", approvalSplit + 5, y + (approvalRow * 3) + 13, 6.1f, SKTextAlign.Left);

        var titleRow = 27f;
        var numberRow = 24f;
        canvas.DrawLine(detailX, y + titleRow, x + width, y + titleRow, thin);
        canvas.DrawLine(detailX, y + titleRow + numberRow, x + width, y + titleRow + numberRow, thin);
        var revisionX = x + width - 45;
        canvas.DrawLine(revisionX, y, revisionX, y + titleRow + numberRow, thin);
        DrawFittedText(canvas, sheet.PartName, detailX + 8, y + 18, revisionX - detailX - 16, 10.5f, SKTextAlign.Left, true);
        DrawText(canvas, $"版本 {sheet.Revision}", revisionX + 5, y + 17, 6.4f, SKTextAlign.Left);
        DrawText(canvas, "图号", detailX + 7, y + titleRow + 15, 6.3f, SKTextAlign.Left);
        DrawFittedText(canvas, sheet.DrawingNumber, detailX + 48, y + titleRow + 17, revisionX - detailX - 54, 9.5f, SKTextAlign.Left, true);
        DrawText(canvas, sheet.PageSize == OpticalDrawingPageSize.A3 ? "A3" : "A4", revisionX + 15, y + titleRow + 17, 9, SKTextAlign.Center, true);

        var bottomTop = y + titleRow + numberRow;
        var bottomWidth = detailWidth / 3;
        canvas.DrawLine(detailX + bottomWidth, bottomTop, detailX + bottomWidth, y + height, thin);
        canvas.DrawLine(detailX + (bottomWidth * 2), bottomTop, detailX + (bottomWidth * 2), y + height, thin);
        DrawText(canvas, "尺寸：mm", detailX + 7, bottomTop + 14, 6.5f, SKTextAlign.Left);
        DrawText(canvas, "比例：自动", detailX + bottomWidth + 7, bottomTop + 14, 6.5f, SKTextAlign.Left);
        DrawText(canvas, "页码：1 / 1", detailX + (bottomWidth * 2) + 7, bottomTop + 14, 6.5f, SKTextAlign.Left);
    }

    private static IReadOnlyList<SKPoint> SurfacePoints(
        double radius,
        double conic,
        double semiDiameter,
        float centerX,
        float centerY,
        float xScale,
        float yScale)
    {
        var points = new List<SKPoint>(65);
        for (var sample = 0; sample <= 64; sample++)
        {
            var height = -semiDiameter + ((2 * semiDiameter * sample) / 64);
            var sag = OpticalManufacturingModel.Sag(radius, conic, Math.Abs(height)) ?? 0;
            points.Add(new SKPoint(
                centerX + ((float)sag * xScale),
                centerY + ((float)height * yScale)));
        }

        return points;
    }

    private static void DrawVerticalDimension(
        SKCanvas canvas,
        float x,
        float top,
        float bottom,
        float extensionX,
        string text,
        SKPaint paint)
    {
        canvas.DrawLine(x, top, extensionX, top, paint);
        canvas.DrawLine(x, bottom, extensionX, bottom, paint);
        canvas.DrawLine(x, top, x, bottom, paint);
        Arrow(canvas, new SKPoint(x, top), new SKPoint(x, top + 12), paint);
        Arrow(canvas, new SKPoint(x, bottom), new SKPoint(x, bottom - 12), paint);
        canvas.Save();
        canvas.RotateDegrees(-90, x - 8, (top + bottom) / 2);
        DrawText(canvas, text, x - 8, (top + bottom) / 2, 7, SKTextAlign.Center);
        canvas.Restore();
    }

    private static void DrawHorizontalDimension(
        SKCanvas canvas,
        float left,
        float right,
        float y,
        float extensionY,
        string text,
        SKPaint paint)
    {
        canvas.DrawLine(left, extensionY, left, y, paint);
        canvas.DrawLine(right, extensionY, right, y, paint);
        canvas.DrawLine(left, y, right, y, paint);
        Arrow(canvas, new SKPoint(left, y), new SKPoint(left + 12, y), paint);
        Arrow(canvas, new SKPoint(right, y), new SKPoint(right - 12, y), paint);
        DrawText(canvas, text, (left + right) / 2, y - 7, 7, SKTextAlign.Center);
    }

    private static void DrawSurfaceCallout(
        SKCanvas canvas,
        SKPoint surfacePoint,
        float textX,
        float textY,
        string radius,
        string surfaceRequirements,
        SKPaint paint,
        bool alignRight)
    {
        var alignment = alignRight ? SKTextAlign.Right : SKTextAlign.Left;
        var elbowX = alignRight ? textX - 70 : textX + 70;
        var elbow = new SKPoint(elbowX, textY + 19);
        canvas.DrawLine(surfacePoint, elbow, paint);
        canvas.DrawLine(elbow, new SKPoint(textX, textY + 19), paint);
        Arrow(canvas, surfacePoint, elbow, paint);
        DrawText(canvas, radius, textX, textY, 7.4f, alignment, true);
        DrawText(canvas, surfaceRequirements, textX, textY + 12, 6.2f, alignment);
    }

    private static void Arrow(SKCanvas canvas, SKPoint tip, SKPoint toward, SKPaint paint)
    {
        var dx = toward.X - tip.X;
        var dy = toward.Y - tip.Y;
        var length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length < 1e-3f)
        {
            return;
        }

        dx /= length;
        dy /= length;
        var normalX = -dy;
        var normalY = dx;
        canvas.DrawLine(tip, new SKPoint(tip.X + (dx * 7) + (normalX * 3), tip.Y + (dy * 7) + (normalY * 3)), paint);
        canvas.DrawLine(tip, new SKPoint(tip.X + (dx * 7) - (normalX * 3), tip.Y + (dy * 7) - (normalY * 3)), paint);
    }

    private static void DrawFittedText(
        SKCanvas canvas,
        string text,
        float x,
        float y,
        float maxWidth,
        float preferredSize,
        SKTextAlign alignment,
        bool bold = false)
    {
        var size = preferredSize;
        while (size > 4.8f)
        {
            if (MeasureText(text, size, bold) <= maxWidth)
            {
                DrawText(canvas, text, x, y, size, alignment, bold);
                return;
            }

            size -= 0.25f;
        }

        DrawText(canvas, text, x, y, 4.8f, alignment, bold);
    }

    private static void DrawText(
        SKCanvas canvas,
        string text,
        float x,
        float y,
        float size,
        SKTextAlign alignment,
        bool bold = false)
    {
        using var paint = new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = true
        };
        var widths = text.Select(character => CharacterWidth(character, size, bold)).ToArray();
        var totalWidth = widths.Sum();
        var cursor = alignment switch
        {
            SKTextAlign.Center => x - (totalWidth / 2),
            SKTextAlign.Right => x - totalWidth,
            _ => x
        };
        for (var index = 0; index < text.Length; index++)
        {
            using var font = new SKFont(TypefaceFor(text[index]), size) { Embolden = bold };
            canvas.DrawText(text[index].ToString(), cursor, y, SKTextAlign.Left, font, paint);
            cursor += widths[index];
        }
    }

    private static float MeasureText(string text, float size, bool bold) =>
        text.Sum(character => CharacterWidth(character, size, bold));

    private static float CharacterWidth(char character, float size, bool bold)
    {
        using var font = new SKFont(TypefaceFor(character), size) { Embolden = bold };
        return font.MeasureText(character.ToString());
    }

    private static SKTypeface TypefaceFor(char character)
    {
        using var primary = new SKFont(ChineseTypeface.Value, 10);
        if (primary.ContainsGlyphs(character.ToString()))
        {
            return ChineseTypeface.Value;
        }

        return FallbackTypefaces.GetOrAdd(
            character,
            static codepoint => SKFontManager.Default.MatchCharacter(codepoint) ?? SKTypeface.Default);
    }

    private static SKPaint Stroke(SKColor color, float width) => new()
    {
        Color = color,
        StrokeWidth = width,
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeCap = SKStrokeCap.Round,
        StrokeJoin = SKStrokeJoin.Round
    };

    private static string RadiusText(double radius) =>
        Math.Abs(radius) < 1e-12 || !double.IsFinite(radius)
            ? "平面"
            : radius.ToString("0.###");

    private static SKTypeface ResolveChineseTypeface()
    {
        using var embedded = typeof(OpticalDrawingRenderer).Assembly.GetManifestResourceStream(
            "OptilandWorkbench.App.Assets.Fonts.NotoSansCJKsc-Regular.otf");
        if (embedded is not null && SKTypeface.FromStream(embedded) is { } bundled)
        {
            return bundled;
        }

        var families = SKFontManager.Default.FontFamilies.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var family in new[]
                 {
                     "PingFang SC",
                     "Microsoft YaHei",
                     "Noto Sans CJK SC",
                     "Source Han Sans SC",
                     "WenQuanYi Micro Hei",
                     "Arial Unicode MS"
                 })
        {
            if (families.Contains(family))
            {
                return SKTypeface.FromFamilyName(family);
            }
        }

        return SKTypeface.Default;
    }
}
