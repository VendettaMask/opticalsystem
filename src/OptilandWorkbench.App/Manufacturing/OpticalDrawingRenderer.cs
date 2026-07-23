using System.Collections.Concurrent;
using OptilandWorkbench.Application.Contracts;
using SkiaSharp;

namespace OptilandWorkbench.App.Manufacturing;

public static class OpticalDrawingRenderer
{
    private const float MillimetersToPoints = 72f / 25.4f;
    private const float A4Width = 210 * MillimetersToPoints;
    private const float A4Height = 297 * MillimetersToPoints;
    private const string CompanyLogoResourceName =
        "OptilandWorkbench.App.Assets.Brand.CompanyLogo.png";
    private static readonly Lazy<SKTypeface> ChineseTypeface = new(ResolveChineseTypeface);
    private static readonly Lazy<byte[]?> DefaultCompanyLogoPng = new(LoadDefaultCompanyLogo);
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
        var validationErrors = sheet.Validate();
        if (validationErrors.Count > 0)
        {
            throw new ArgumentException(string.Join("；", validationErrors), nameof(sheet));
        }

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

        DrawText(
            canvas,
            "单位：mm    投影：第一角法",
            right - 8,
            inner + 21,
            6.8f,
            SKTextAlign.Right);
        var scaleDesignation = DrawElementGeometry(
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
            medium,
            scaleDesignation);
    }

    private static string DrawElementGeometry(
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
        var semiDiameter = Math.Max(0.1, element.Diameter / 2);
        var scaleRatio = DrawingScaleRatio(element);
        var drawingScale = (float)scaleRatio * MillimetersToPoints;
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
        DrawOpticalGlassHatch(canvas, lens, hatch, sheet.Standard);
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
            $"⌀{element.Diameter:0.###}",
            dimension,
            sheet.DiameterUpperDeviation,
            sheet.DiameterLowerDeviation);
        DrawHorizontalDimension(
            canvas,
            frontVertexX,
            backVertexX,
            bottomY + 35,
            bottomY,
            $"{element.CenterThickness:0.###}",
            dimension,
            sheet.CenterThicknessUpperDeviation,
            sheet.CenterThicknessLowerDeviation);

        DrawSurfaceRadiusDimension(
            canvas,
            frontPoints,
            frontVertexX,
            centerY,
            element.FrontSurface.Radius,
            sheet.FrontRadiusTolerance,
            xScale,
            area,
            dimension,
            upperSurface: true);
        DrawSurfaceRadiusDimension(
            canvas,
            backPoints,
            backVertexX,
            centerY,
            element.BackSurface.Radius,
            sheet.BackRadiusTolerance,
            xScale,
            area,
            dimension,
            upperSurface: false);

        DrawText(canvas, "S1", frontVertexX - 9, centerY - 7, 7.2f, SKTextAlign.Right, true);
        DrawText(canvas, "S2", backVertexX + 9, centerY - 7, 7.2f, SKTextAlign.Left, true);
        return ScaleDesignation(scaleRatio);

    }

    public static string ScaleDesignation(OpticalDrawingSheet sheet) =>
        ScaleDesignation(DrawingScaleRatio(sheet.Element));

    private static double DrawingScaleRatio(OpticalElementDefinition element)
    {
        var diameter = Math.Max(0.1, element.Diameter);
        var semiDiameter = diameter / 2;
        var frontSag = Math.Abs(OpticalManufacturingModel.Sag(
            element.FrontSurface.Radius,
            element.FrontSurface.Conic,
            semiDiameter) ?? 0);
        var backSag = Math.Abs(OpticalManufacturingModel.Sag(
            element.BackSurface.Radius,
            element.BackSurface.Conic,
            semiDiameter) ?? 0);
        var axialExtent = Math.Max(0.1, element.CenterThickness + frontSag + backSag);
        var maximum = Math.Min(
            330 / (diameter * MillimetersToPoints),
            190 / (axialExtent * MillimetersToPoints));
        var preferred = new[] { 10d, 5d, 2d, 1d, 0.5d, 0.2d, 0.1d };
        return preferred.FirstOrDefault(scale => scale <= maximum, 0.1d);
    }

    private static string ScaleDesignation(double scale) => scale >= 1
        ? $"{scale:0.#}:1"
        : $"1:{1 / scale:0.#}";

    private static void DrawOpticalGlassHatch(
        SKCanvas canvas,
        SKPath lens,
        SKPaint hatch,
        OpticalDrawingStandard standard)
    {
        var bounds = lens.Bounds;
        var halfLengths = OpticalGlassHatchHalfLengths(standard);
        const float markOffset = 3.2f;
        const float clusterSpacingX = 27f;
        const float clusterSpacingY = 24f;
        var row = 0;

        for (var y = bounds.Top + 9; y <= bounds.Bottom - 7; y += clusterSpacingY, row++)
        {
            var rowOffset = row % 2 == 0 ? 0 : clusterSpacingX * 0.5f;
            for (var x = bounds.Left + 9 + rowOffset; x <= bounds.Right - 7; x += clusterSpacingX)
            {
                for (var markIndex = 0; markIndex < halfLengths.Count; markIndex++)
                {
                    var mark = markIndex - 1;
                    var halfLength = halfLengths[markIndex];
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

    internal static IReadOnlyList<float> OpticalGlassHatchHalfLengths(
        OpticalDrawingStandard standard) =>
        standard == OpticalDrawingStandard.Iso10110
            ? new[] { 3.0f, 5.6f, 3.0f }
            : new[] { 4.6f, 4.6f, 4.6f };

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
        if (sheet.Standard == OpticalDrawingStandard.GbT13323_2009)
        {
            DrawGbSpecificationTable(
                canvas,
                sheet,
                x,
                y,
                width,
                height,
                thin,
                medium,
                headerFill);
            return;
        }

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

    private static void DrawGbSpecificationTable(
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
        const float subheaderHeight = 19;
        var materialWidth = width * 0.34f;
        var partX = x + materialWidth;
        var partWidth = width - materialWidth;
        var surfaceWidth = 42f;
        var apertureWidth = 80f;
        var apertureX = partX + surfaceWidth;
        var requirementX = apertureX + apertureWidth;
        var bodyTop = y + headerHeight + subheaderHeight;
        var rowHeight = (height - headerHeight - subheaderHeight) / 2;

        canvas.DrawRect(x, y, width, headerHeight, headerFill);
        canvas.DrawRect(x, y, width, height, medium);
        canvas.DrawLine(partX, y, partX, y + height, thin);
        canvas.DrawLine(x, y + headerHeight, x + width, y + headerHeight, thin);
        canvas.DrawLine(partX, y + headerHeight + subheaderHeight, x + width, y + headerHeight + subheaderHeight, thin);
        canvas.DrawLine(apertureX, y + headerHeight, apertureX, y + height, thin);
        canvas.DrawLine(requirementX, y + headerHeight, requirementX, y + height, thin);
        canvas.DrawLine(partX, bodyTop + rowHeight, x + width, bodyTop + rowHeight, thin);

        DrawText(canvas, "对材料的要求", x + (materialWidth / 2), y + 16, 9, SKTextAlign.Center, true);
        DrawText(canvas, "对零件的要求", partX + (partWidth / 2), y + 16, 9, SKTextAlign.Center, true);
        DrawText(canvas, "表面", partX + (surfaceWidth / 2), y + headerHeight + 13, 7, SKTextAlign.Center, true);
        DrawText(canvas, "D（有效孔径）", apertureX + (apertureWidth / 2), y + headerHeight + 13, 6.7f, SKTextAlign.Center, true);
        DrawText(canvas, "技术要求", requirementX + ((x + width - requirementX) / 2), y + headerHeight + 13, 7, SKTextAlign.Center, true);

        DrawColumnLines(canvas, GbMaterialSpecificationLines(sheet), x + 9, y + headerHeight + 17, materialWidth - 18);
        DrawGbSurfaceRequirement(canvas, sheet, sheet.Element.FrontSurface, true, partX, apertureX, requirementX, bodyTop, rowHeight, x + width);
        DrawGbSurfaceRequirement(canvas, sheet, sheet.Element.BackSurface, false, partX, apertureX, requirementX, bodyTop + rowHeight, rowHeight, x + width);
    }

    private static void DrawGbSurfaceRequirement(
        SKCanvas canvas,
        OpticalDrawingSheet sheet,
        SurfaceRowDto surface,
        bool isFront,
        float partX,
        float apertureX,
        float requirementX,
        float rowTop,
        float rowHeight,
        float right)
    {
        var coating = string.IsNullOrWhiteSpace(surface.Coating)
            || surface.Coating.Equals("None", StringComparison.OrdinalIgnoreCase)
                ? sheet.Coating
                : surface.Coating;
        var surfaceName = isFront ? "S1" : "S2";
        DrawText(canvas, surfaceName, (partX + apertureX) / 2, rowTop + (rowHeight / 2) + 3, 8, SKTextAlign.Center, true);
        DrawText(canvas, $"⌀{sheet.Element.ClearSemiDiameter * 2:0.###}", (apertureX + requirementX) / 2, rowTop + (rowHeight / 2) + 3, 7, SKTextAlign.Center);

        var form = isFront ? sheet.FrontSurfaceFormNanometers : sheet.BackSurfaceFormNanometers;
        var lines = new[]
        {
            $"R {RadiusText(surface.Radius)}",
            $"面形偏差 {form:0.#} nm；偏心/倾斜 {sheet.CenteringToleranceArcMinutes:0.###}′",
            $"表面缺陷 {sheet.SurfaceImperfection}",
            $"表面纹理 Rq {sheet.SurfaceTextureNanometers:0.###} nm",
            $"膜层 {coating}",
            $"边缘 {sheet.EdgeTreatment}"
        };
        var lineHeight = Math.Min(11.5f, (rowHeight - 9) / lines.Length);
        for (var index = 0; index < lines.Length; index++)
        {
            DrawFittedText(
                canvas,
                lines[index],
                requirementX + 7,
                rowTop + 11 + (index * lineHeight),
                right - requirementX - 14,
                6.4f,
                SKTextAlign.Left);
        }
    }

    private static IReadOnlyList<string> GbMaterialSpecificationLines(OpticalDrawingSheet sheet)
    {
        var material = sheet.MaterialData;
        return new[]
        {
            $"光学材料  {sheet.Element.Material}",
            $"制造商  {material?.Manufacturer ?? "当前玻璃库"}",
            material is null
                ? "n[d]  折射率由玻璃库解析"
                : $"n[d]  折射率 {material.RefractiveIndexD:0.000000} ±{sheet.RefractiveIndexTolerance:0.000000}",
            material is null
                ? "V[d]  阿贝数由玻璃库解析"
                : $"V[d]  阿贝数 {material.AbbeNumber:0.###} ±{sheet.AbbeNumberTolerance:0.###}",
            $"应力双折射  {sheet.StressBirefringence}",
            $"气泡和夹杂  {sheet.BubblesAndInclusions}",
            $"均匀性和条纹  {sheet.HomogeneityAndStriae}"
        };
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
            $"R  {RadiusText(surface.Radius)}",
            $"⌀e  {sheet.Element.ClearSemiDiameter * 2:0.###}",
            $"边缘  {sheet.EdgeTreatment}",
            $"3/  {(isFront ? sheet.FrontSurfaceFormNanometers : sheet.BackSurfaceFormNanometers):0.#} nm",
            $"4/  {sheet.CenteringToleranceArcMinutes:0.###}′",
            $"5/  {sheet.SurfaceImperfection}",
            $"7/  Rq {sheet.SurfaceTextureNanometers:0.###} nm",
            $"λ  {coating}"
        };
    }

    private static IReadOnlyList<string> MaterialSpecificationLines(OpticalDrawingSheet sheet)
    {
        var material = sheet.MaterialData;
        return new[]
        {
            $"制造商  {material?.Manufacturer ?? "当前玻璃库"}",
            $"玻璃牌号  {sheet.Element.Material}",
            material is null
                ? "n[d]  由玻璃库解析"
                : $"n[d]  {material.RefractiveIndexD:0.000000} ±{sheet.RefractiveIndexTolerance:0.000000}",
            material is null
                ? "V[d]  由玻璃库解析"
                : $"V[d]  {material.AbbeNumber:0.###} ±{sheet.AbbeNumberTolerance:0.###}",
            $"0/  {sheet.StressBirefringence}",
            $"1/  {sheet.BubblesAndInclusions}",
            $"2/  {sheet.HomogeneityAndStriae}"
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
        SKPaint medium,
        string scaleDesignation)
    {
        var brandWidth = width * 0.35f;
        var approvalWidth = width * 0.27f;
        var approvalX = x + brandWidth;
        var detailX = approvalX + approvalWidth;
        var detailWidth = width - brandWidth - approvalWidth;
        canvas.DrawRect(x, y, width, height, medium);
        canvas.DrawLine(approvalX, y, approvalX, y + height, thin);
        canvas.DrawLine(detailX, y, detailX, y + height, thin);

        DrawCompanyLogo(
            canvas,
            sheet.CompanyLogoPng,
            new SKRect(x + 8, y + 8, x + brandWidth - 8, y + height - 8));

        var approvalRow = height / 4;
        for (var index = 1; index < 4; index++)
        {
            canvas.DrawLine(approvalX, y + (approvalRow * index), detailX, y + (approvalRow * index), thin);
        }

        var approvalSplit = approvalX + (approvalWidth * 0.42f);
        canvas.DrawLine(approvalSplit, y + approvalRow, approvalSplit, y + height, thin);
        DrawFittedText(
            canvas,
            StandardDesignation(sheet.Standard),
            approvalX + (approvalWidth / 2),
            y + 13,
            approvalWidth - 10,
            6.2f,
            SKTextAlign.Center,
            true);
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
        DrawText(canvas, $"比例：{scaleDesignation}", detailX + bottomWidth + 7, bottomTop + 14, 6.5f, SKTextAlign.Left);
        DrawText(canvas, "页码：1 / 1", detailX + (bottomWidth * 2) + 7, bottomTop + 14, 6.5f, SKTextAlign.Left);
    }

    private static void DrawCompanyLogo(SKCanvas canvas, byte[]? png, SKRect bounds)
    {
        if (png is { Length: > 0 } && DrawImportedLogo(canvas, png, bounds))
        {
            return;
        }

        if (DefaultCompanyLogoPng.Value is { Length: > 0 } defaultLogo
            && DrawImportedLogo(canvas, defaultLogo, bounds))
        {
            return;
        }

        var centerX = bounds.MidX;
        var centerY = bounds.MidY;
        DrawText(canvas, "S.T.A.R.", centerX, centerY - 8, 18, SKTextAlign.Center, true);
        using var accent = Stroke(new SKColor(39, 93, 119), 1.2f);
        canvas.DrawLine(bounds.Left + 24, centerY, bounds.Right - 24, centerY, accent);
        DrawText(canvas, "L A B S", centerX, centerY + 14, 8.5f, SKTextAlign.Center, true);
    }

    private static bool DrawImportedLogo(
        SKCanvas canvas,
        byte[] png,
        SKRect bounds)
    {
        try
        {
            using var data = SKData.CreateCopy(png);
            using var decoded = SKBitmap.Decode(data);
            if (decoded is null || decoded.Width <= 0 || decoded.Height <= 0)
            {
                return false;
            }

            using var image = SKImage.FromBitmap(decoded);

            var scale = Math.Min(bounds.Width / image.Width, bounds.Height / image.Height);
            var width = image.Width * scale;
            var height = image.Height * scale;
            var destination = SKRect.Create(
                bounds.MidX - (width / 2),
                bounds.MidY - (height / 2),
                width,
                height);
            canvas.DrawImage(image, destination);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static SKBitmap RemoveLightBackgroundAndCrop(SKBitmap source)
    {
        var left = source.Width;
        var top = source.Height;
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                if (IsLightNeutral(source.GetPixel(x, y)))
                {
                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        if (right < left || bottom < top)
        {
            return new SKBitmap(1, 1, true);
        }

        const int padding = 12;
        left = Math.Max(0, left - padding);
        top = Math.Max(0, top - padding);
        right = Math.Min(source.Width - 1, right + padding);
        bottom = Math.Min(source.Height - 1, bottom + padding);
        var result = new SKBitmap(
            new SKImageInfo(
                right - left + 1,
                bottom - top + 1,
                SKColorType.Rgba8888,
                SKAlphaType.Premul));
        result.Erase(SKColors.Transparent);
        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                var color = source.GetPixel(x, y);
                if (!IsLightNeutral(color))
                {
                    result.SetPixel(x - left, y - top, color);
                }
            }
        }

        return result;
    }

    private static bool IsLightNeutral(SKColor color)
    {
        var minimum = Math.Min(color.Red, Math.Min(color.Green, color.Blue));
        var maximum = Math.Max(color.Red, Math.Max(color.Green, color.Blue));
        return color.Alpha == 0 || (minimum >= 232 && maximum - minimum <= 10);
    }

    private static byte[]? LoadDefaultCompanyLogo()
    {
        using var stream = typeof(OpticalDrawingRenderer).Assembly
            .GetManifestResourceStream(CompanyLogoResourceName);
        if (stream is null)
        {
            return null;
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        using var data = SKData.CreateCopy(memory.ToArray());
        using var decoded = SKBitmap.Decode(data);
        if (decoded is null || decoded.Width <= 0 || decoded.Height <= 0)
        {
            return null;
        }

        using var prepared = RemoveLightBackgroundAndCrop(decoded);
        using var image = SKImage.FromBitmap(prepared);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded?.ToArray();
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
        SKPaint paint,
        double? upperDeviation = null,
        double? lowerDeviation = null)
    {
        DrawExtensionLine(canvas, new SKPoint(extensionX, top), new SKPoint(x, top), paint);
        DrawExtensionLine(canvas, new SKPoint(extensionX, bottom), new SKPoint(x, bottom), paint);
        canvas.DrawLine(x, top, x, bottom, paint);
        Arrow(canvas, new SKPoint(x, top), new SKPoint(x, top + 9), paint);
        Arrow(canvas, new SKPoint(x, bottom), new SKPoint(x, bottom - 9), paint);
        canvas.Save();
        canvas.RotateDegrees(-90, x - 8, (top + bottom) / 2);
        DrawDimensionText(
            canvas,
            text,
            x - 8,
            (top + bottom) / 2,
            upperDeviation,
            lowerDeviation);
        canvas.Restore();
    }

    private static void DrawHorizontalDimension(
        SKCanvas canvas,
        float left,
        float right,
        float y,
        float extensionY,
        string text,
        SKPaint paint,
        double? upperDeviation = null,
        double? lowerDeviation = null)
    {
        DrawExtensionLine(canvas, new SKPoint(left, extensionY), new SKPoint(left, y), paint);
        DrawExtensionLine(canvas, new SKPoint(right, extensionY), new SKPoint(right, y), paint);
        canvas.DrawLine(left, y, right, y, paint);
        Arrow(canvas, new SKPoint(left, y), new SKPoint(left + 9, y), paint);
        Arrow(canvas, new SKPoint(right, y), new SKPoint(right - 9, y), paint);
        DrawDimensionText(
            canvas,
            text,
            (left + right) / 2,
            y - 7,
            upperDeviation,
            lowerDeviation);
    }

    private static void DrawDimensionText(
        SKCanvas canvas,
        string nominal,
        float centerX,
        float baselineY,
        double? upperDeviation,
        double? lowerDeviation)
    {
        const float nominalSize = 7;
        if (upperDeviation is null || lowerDeviation is null)
        {
            DrawText(canvas, nominal, centerX, baselineY, nominalSize, SKTextAlign.Center);
            return;
        }

        const float deviationSize = 5.4f;
        const float gap = 2;
        var upper = FormatDeviation(upperDeviation.Value);
        var lower = FormatDeviation(lowerDeviation.Value);
        var nominalWidth = MeasureText(nominal, nominalSize, false);
        var deviationWidth = Math.Max(
            MeasureText(upper, deviationSize, false),
            MeasureText(lower, deviationSize, false));
        var left = centerX - ((nominalWidth + gap + deviationWidth) / 2);
        var deviationX = left + nominalWidth + gap;

        DrawText(canvas, nominal, left, baselineY + 1.5f, nominalSize, SKTextAlign.Left);
        DrawText(canvas, upper, deviationX, baselineY - 2.5f, deviationSize, SKTextAlign.Left);
        DrawText(canvas, lower, deviationX, baselineY + 4.2f, deviationSize, SKTextAlign.Left);
    }

    private static string FormatDeviation(double value) => value switch
    {
        > 0 => $"+{value:0.###}",
        < 0 => $"{value:0.###}",
        _ => "0"
    };

    private static void DrawSurfaceRadiusDimension(
        SKCanvas canvas,
        IReadOnlyList<SKPoint> surfacePoints,
        float vertexX,
        float axisY,
        double radius,
        double tolerance,
        float xScale,
        SKRect area,
        SKPaint paint,
        bool upperSurface)
    {
        var surfacePoint = surfacePoints[upperSurface ? 8 : 56];
        var isPlane = Math.Abs(radius) < 1e-12 || !double.IsFinite(radius);
        var directionX = 1f;
        var directionY = upperSurface ? 0.28f : 0.18f;
        var center = new SKPoint(float.NaN, float.NaN);
        if (!isPlane)
        {
            center = new SKPoint(vertexX + ((float)radius * xScale), axisY);
            directionX = center.X - surfacePoint.X;
            directionY = center.Y - surfacePoint.Y;
            if (directionX < 0)
            {
                directionX = -directionX;
                directionY = -directionY;
            }
        }

        var directionLength = MathF.Sqrt(
            (directionX * directionX) + (directionY * directionY));
        if (directionLength < 1e-3f)
        {
            directionX = 1;
            directionY = 0;
            directionLength = 1;
        }

        directionX /= directionLength;
        directionY /= directionLength;
        var lineLength = 96f;
        lineLength = Math.Min(
            lineLength,
            AvailableLength(surfacePoint.X, directionX, area.Left + 12, area.Right - 12));
        lineLength = Math.Min(
            lineLength,
            AvailableLength(surfacePoint.Y, directionY, area.Top + 12, area.Bottom - 12));
        lineLength = Math.Max(42, lineLength);

        var centerDistance = isPlane
            ? float.PositiveInfinity
            : Distance(surfacePoint, center);
        var reachesCenter = !isPlane
            && centerDistance >= 42
            && centerDistance <= lineLength
            && area.Contains(center.X, center.Y);
        if (reachesCenter)
        {
            lineLength = centerDistance;
        }

        var lineEnd = reachesCenter
            ? center
            : new SKPoint(
                surfacePoint.X + (directionX * lineLength),
                surfacePoint.Y + (directionY * lineLength));
        canvas.DrawLine(surfacePoint, lineEnd, paint);
        Arrow(canvas, surfacePoint, lineEnd, paint);
        if (reachesCenter)
        {
            DrawCenterMark(canvas, center, paint);
        }

        var labelX = surfacePoint.X + (directionX * lineLength * 0.62f);
        var labelY = surfacePoint.Y + (directionY * lineLength * 0.62f);
        var angle = MathF.Atan2(directionY, directionX) * 180f / MathF.PI;
        canvas.Save();
        canvas.RotateDegrees(angle, labelX, labelY);
        DrawText(
            canvas,
            RadiusDimensionText(radius, tolerance),
            labelX,
            labelY - 4.5f,
            7.2f,
            SKTextAlign.Center);
        canvas.Restore();
    }

    internal static string RadiusDimensionText(double radius, double tolerance) =>
        Math.Abs(radius) < 1e-12 || !double.IsFinite(radius)
            ? "R∞"
            : $"R{Math.Abs(radius):0.###} ±{tolerance:0.###}";

    private static float AvailableLength(float value, float direction, float minimum, float maximum)
    {
        if (Math.Abs(direction) < 1e-4f)
        {
            return float.PositiveInfinity;
        }

        return direction > 0
            ? Math.Max(0, (maximum - value) / direction)
            : Math.Max(0, (minimum - value) / direction);
    }

    private static float Distance(SKPoint first, SKPoint second)
    {
        var dx = second.X - first.X;
        var dy = second.Y - first.Y;
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }

    private static void DrawCenterMark(SKCanvas canvas, SKPoint center, SKPaint paint)
    {
        const float arm = 4;
        canvas.DrawLine(center.X - arm, center.Y, center.X + arm, center.Y, paint);
        canvas.DrawLine(center.X, center.Y - arm, center.X, center.Y + arm, paint);
    }

    private static void DrawExtensionLine(
        SKCanvas canvas,
        SKPoint objectPoint,
        SKPoint dimensionPoint,
        SKPaint paint)
    {
        var dx = dimensionPoint.X - objectPoint.X;
        var dy = dimensionPoint.Y - objectPoint.Y;
        var length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length < 1e-3f)
        {
            return;
        }

        dx /= length;
        dy /= length;
        const float objectGap = 1.5f;
        const float overshoot = 2.5f;
        canvas.DrawLine(
            objectPoint.X + (dx * objectGap),
            objectPoint.Y + (dy * objectGap),
            dimensionPoint.X + (dx * overshoot),
            dimensionPoint.Y + (dy * overshoot),
            paint);
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
        const float arrowLength = 5.8f;
        const float halfWidth = 2.1f;
        using var arrow = new SKPath();
        arrow.MoveTo(tip);
        arrow.LineTo(
            tip.X + (dx * arrowLength) + (normalX * halfWidth),
            tip.Y + (dy * arrowLength) + (normalY * halfWidth));
        arrow.LineTo(
            tip.X + (dx * arrowLength) - (normalX * halfWidth),
            tip.Y + (dy * arrowLength) - (normalY * halfWidth));
        arrow.Close();
        using var fill = new SKPaint
        {
            Color = paint.Color,
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawPath(arrow, fill);
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
        StrokeCap = SKStrokeCap.Butt,
        StrokeJoin = SKStrokeJoin.Miter
    };

    private static string RadiusText(double radius) =>
        Math.Abs(radius) < 1e-12 || !double.IsFinite(radius)
            ? "平面"
            : radius.ToString("0.###");

    public static string StandardDesignation(OpticalDrawingStandard standard) => standard switch
    {
        OpticalDrawingStandard.GbT13323_2009 => "GB/T 13323—2009 光学制图",
        _ => "ISO 10110-1:2019 表格式"
    };

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
