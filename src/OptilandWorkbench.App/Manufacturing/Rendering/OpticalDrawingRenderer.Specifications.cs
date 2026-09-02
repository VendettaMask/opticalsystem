using System.Collections.Concurrent;
using OptilandWorkbench.Application.Contracts;
using SkiaSharp;

namespace OptilandWorkbench.App.Manufacturing;

internal static partial class OpticalDrawingRendererCore
{
    internal static string ScaleDesignation(OpticalDrawingSheet sheet) =>
        ScaleDesignation(DrawingScaleRatio(sheet.Element));

    private static double DrawingScaleRatio(OpticalDrawingElementDefinition element)
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
        return PreferredDrawingScales.FirstOrDefault(scale => scale <= maximum, PreferredDrawingScales[^1]);
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
        IsLegacyGb1991(standard)
            ? LegacyGbOpticalGlassHatchHalfLengths
            : CurrentOpticalGlassHatchHalfLengths;

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
        if (sheet.Standard == OpticalDrawingStandard.GbT13323_1991)
        {
            DrawGb1991SpecificationTable(
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

        if (sheet.Element.IsCemented)
        {
            DrawCementedSpecificationTable(
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

        if (sheet.Standard == OpticalDrawingStandard.GbT13323_2009)
        {
            DrawGb2009SpecificationTable(
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
    private static void DrawCementedSpecificationTable(
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
        var columnCount = (sheet.Element.Components.Count * 2) + 1;
        var columnWidth = width / columnCount;
        canvas.DrawRect(x, y, width, headerHeight, headerFill);
        canvas.DrawRect(x, y, width, height, medium);
        canvas.DrawLine(x, y + headerHeight, x + width, y + headerHeight, thin);
        for (var column = 1; column < columnCount; column++)
        {
            var columnX = x + (column * columnWidth);
            canvas.DrawLine(columnX, y, columnX, y + height, thin);
        }

        var bodyTop = y + headerHeight + 14;
        for (var column = 0; column < columnCount; column++)
        {
            var columnX = x + (column * columnWidth);
            if (column % 2 == 0)
            {
                var surfaceIndex = column / 2;
                DrawText(
                    canvas,
                    $"S{surfaceIndex + 1}",
                    columnX + (columnWidth / 2),
                    y + 16,
                    8,
                    SKTextAlign.Center,
                    true);
                DrawColumnLines(
                    canvas,
                    SurfaceSpecificationLines(
                        sheet,
                        sheet.Element.Surfaces[surfaceIndex],
                        isFront: surfaceIndex == 0),
                    columnX + 5,
                    bodyTop,
                    columnWidth - 10);
                continue;
            }

            var componentIndex = column / 2;
            var component = sheet.Element.Components[componentIndex];
            var material = sheet.ComponentMaterialData?.ElementAtOrDefault(componentIndex)
                ?? (componentIndex == 0 ? sheet.MaterialData : null);
            DrawText(
                canvas,
                CementedComponentLabel(componentIndex),
                columnX + (columnWidth / 2),
                y + 16,
                8,
                SKTextAlign.Center,
                true);
            DrawColumnLines(
                canvas,
                ComponentMaterialSpecificationLines(sheet, component, material),
                columnX + 5,
                bodyTop,
                columnWidth - 10);
        }
    }

    internal static string CementedComponentLabel(int componentIndex)
    {
        if (componentIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(componentIndex));
        }

        return $"L{componentIndex + 1}";
    }

    private static IReadOnlyList<string> ComponentMaterialSpecificationLines(
        OpticalDrawingSheet sheet,
        OpticalElementDefinition component,
        GlassMaterialDto? material) =>
        new[]
        {
            $"GLASS  {component.Material}",
            $"MAKER  {material?.Manufacturer ?? "CATALOG"}",
            material is null
                ? "n[d]  CATALOG"
                : $"n[d]  {material.RefractiveIndexD:0.000000} +/-{sheet.RefractiveIndexTolerance:0.000000}",
            material is null
                ? "V[d]  CATALOG"
                : $"V[d]  {material.AbbeNumber:0.###} +/-{sheet.AbbeNumberTolerance:0.###}",
            $"CT  {component.CenterThickness:0.###} mm",
            $"0/  {sheet.StressBirefringence}",
            $"1/  {sheet.BubblesAndInclusions}",
            $"2/  {sheet.HomogeneityAndStriae}"
        };


    private static void DrawGb1991SpecificationTable(
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
        const float titleHeight = 22;
        const float sectionWidth = 66;
        const float itemWidth = 82;
        const float materialHeight = 66;
        const float partHeaderHeight = 17;
        var titleBottom = y + titleHeight;
        var materialBottom = titleBottom + materialHeight;
        var partTop = materialBottom;
        var partBodyTop = partTop + partHeaderHeight;
        var materialItemX = x + sectionWidth;
        var materialValueX = materialItemX + itemWidth;

        canvas.DrawRect(x, y, width, titleHeight, headerFill);
        canvas.DrawRect(x, y, width, height, medium);
        canvas.DrawLine(x, titleBottom, x + width, titleBottom, thin);
        canvas.DrawLine(x, materialBottom, x + width, materialBottom, medium);
        canvas.DrawLine(materialItemX, titleBottom, materialItemX, materialBottom, thin);
        canvas.DrawLine(materialValueX, titleBottom, materialValueX, materialBottom, thin);

        DrawText(
            canvas,
            "GB/T 13323—1991 旧版光学零件技术要求",
            x + (width / 2),
            y + 15,
            8.4f,
            SKTextAlign.Center,
            true);
        DrawText(canvas, "对材料的要求", x + (sectionWidth / 2), titleBottom + 38, 7.2f, SKTextAlign.Center, true);

        var materialRows = Gb1991MaterialSpecificationRows(sheet);
        var materialRowHeight = materialHeight / materialRows.Count;
        for (var index = 0; index < materialRows.Count; index++)
        {
            var rowTop = titleBottom + (index * materialRowHeight);
            if (index > 0)
            {
                canvas.DrawLine(materialItemX, rowTop, x + width, rowTop, thin);
            }

            DrawFittedText(
                canvas,
                materialRows[index].Item,
                materialItemX + 5,
                rowTop + (materialRowHeight / 2) + 3,
                itemWidth - 10,
                6.7f,
                SKTextAlign.Left,
                true);
            DrawFittedText(
                canvas,
                materialRows[index].Value,
                materialValueX + 6,
                rowTop + (materialRowHeight / 2) + 3,
                x + width - materialValueX - 12,
                6.6f,
                SKTextAlign.Left);
        }

        var surfaceX = x + sectionWidth;
        var apertureX = surfaceX + 39;
        var radiusX = apertureX + 70;
        var requirementX = radiusX + 82;
        canvas.DrawLine(surfaceX, partTop, surfaceX, y + height, thin);
        canvas.DrawLine(apertureX, partTop, apertureX, y + height, thin);
        canvas.DrawLine(radiusX, partTop, radiusX, y + height, thin);
        canvas.DrawLine(requirementX, partTop, requirementX, y + height, thin);
        DrawHorizontalRule(partBodyTop);

        DrawText(canvas, "对零件的要求", x + (sectionWidth / 2), partTop + ((height - (partTop - y)) / 2), 7.2f, SKTextAlign.Center, true);
        DrawText(canvas, "表面", surfaceX + 19.5f, partTop + 12, 6.6f, SKTextAlign.Center, true);
        DrawText(canvas, "D", apertureX + 35, partTop + 12, 6.6f, SKTextAlign.Center, true);
        DrawText(canvas, "R", radiusX + 41, partTop + 12, 6.6f, SKTextAlign.Center, true);
        DrawText(canvas, "旧版项目与要求", requirementX + ((x + width - requirementX) / 2), partTop + 12, 6.6f, SKTextAlign.Center, true);

        var partRows = Gb1991PartSpecificationRows(sheet);
        var partRowHeight = (y + height - partBodyTop) / partRows.Count;
        for (var index = 0; index < partRows.Count; index++)
        {
            var rowTop = partBodyTop + (index * partRowHeight);
            if (index > 0)
            {
                canvas.DrawLine(surfaceX, rowTop, x + width, rowTop, thin);
            }

            DrawText(canvas, partRows[index].Surface, surfaceX + 19.5f, rowTop + (partRowHeight / 2) + 3, 7, SKTextAlign.Center, true);
            DrawFittedText(canvas, partRows[index].Aperture, apertureX + 5, rowTop + (partRowHeight / 2) + 3, 60, 6.5f, SKTextAlign.Left);
            DrawFittedText(canvas, partRows[index].Radius, radiusX + 5, rowTop + (partRowHeight / 2) + 3, 72, 6.5f, SKTextAlign.Left);
            DrawFittedText(
                canvas,
                partRows[index].Requirement,
                requirementX + 6,
                rowTop + (partRowHeight / 2) + 3,
                x + width - requirementX - 12,
                6.2f,
                SKTextAlign.Left);
        }

        void DrawHorizontalRule(float lineY) => canvas.DrawLine(x, lineY, x + width, lineY, thin);
    }

    private static void DrawGb2009SpecificationTable(
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

    private static IReadOnlyList<(string Item, string Value)> Gb1991MaterialSpecificationRows(
        OpticalDrawingSheet sheet)
    {
        var material = sheet.MaterialData;
        return new[]
        {
            ("玻璃牌号", $"{sheet.Element.Material}；{material?.Manufacturer ?? "当前玻璃库"}"),
            material is null
                ? ("折射率/阿贝数", "n[d]、V[d] 由玻璃库解析")
                : (
                    "折射率/阿贝数",
                    $"n[d] {material.RefractiveIndexD:0.000000} ±{sheet.RefractiveIndexTolerance:0.000000}；V[d] {material.AbbeNumber:0.###} ±{sheet.AbbeNumberTolerance:0.###}"),
            ("均匀性/条纹", sheet.HomogeneityAndStriae),
            ("应力/气泡", $"{sheet.StressBirefringence}；{sheet.BubblesAndInclusions}")
        };
    }

    private static IReadOnlyList<(string Surface, string Aperture, string Radius, string Requirement)> Gb1991PartSpecificationRows(
        OpticalDrawingSheet sheet)
    {
        var surfaces = sheet.Element.Surfaces;
        var rows = new List<(string Surface, string Aperture, string Radius, string Requirement)>(surfaces.Count);
        for (var index = 0; index < surfaces.Count; index++)
        {
            var surface = surfaces[index];
            var form = index == 0
                ? sheet.FrontSurfaceFormNanometers
                : index == surfaces.Count - 1
                    ? sheet.BackSurfaceFormNanometers
                    : Math.Max(sheet.FrontSurfaceFormNanometers, sheet.BackSurfaceFormNanometers);
            var coating = string.IsNullOrWhiteSpace(surface.Coating)
                || surface.Coating.Equals("None", StringComparison.OrdinalIgnoreCase)
                    ? sheet.Coating
                    : surface.Coating;
            rows.Add((
                $"S{index + 1}",
                $"⌀{sheet.Element.ClearSemiDiameter * 2:0.###}",
                RadiusText(surface.Radius),
                $"面形 {form:0.#} nm；偏心 {sheet.CenteringToleranceArcMinutes:0.###}′；B {sheet.SurfaceImperfection}；Rq {sheet.SurfaceTextureNanometers:0.###} nm；膜 {coating}；边 {sheet.EdgeTreatment}"));
        }

        return rows;
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
}
