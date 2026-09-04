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
        var template = OpticalDrawingTemplateCatalog.For(sheet.Standard);
        if (template.Specification.Kind == "gb1991")
        {
            DrawGb1991SpecificationTable(
                canvas,
                template,
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
                template,
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

        if (template.Specification.Kind == "gb2009")
        {
            DrawGb2009SpecificationTable(
                canvas,
                template,
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

        DrawIsoSpecificationTable(
            canvas,
            template,
            sheet,
            x,
            y,
            width,
            height,
            thin,
            medium,
            headerFill);
    }

    private static void DrawIsoSpecificationTable(
        SKCanvas canvas,
        OpticalDrawingTemplate template,
        OpticalDrawingSheet sheet,
        float x,
        float y,
        float width,
        float height,
        SKPaint thin,
        SKPaint medium,
        SKPaint headerFill)
    {
        var headerHeight = template.Specification.HeaderHeight;
        var columns = template.Specification.Columns;
        if (columns.Count != 3)
        {
            throw new InvalidDataException(
                $"ISO optical drawing template '{template.Id}' must define exactly three specification columns.");
        }

        var leftWidth = width * columns[0].WidthRatio;
        var materialWidth = width * columns[1].WidthRatio;
        var materialX = x + leftWidth;
        var rightX = materialX + materialWidth;
        canvas.DrawRect(x, y, width, headerHeight, headerFill);
        canvas.DrawRect(x, y, width, height, medium);
        canvas.DrawLine(x, y + headerHeight, x + width, y + headerHeight, thin);
        canvas.DrawLine(materialX, y, materialX, y + height, thin);
        canvas.DrawLine(rightX, y, rightX, y + height, thin);

        DrawText(canvas, columns[0].Title, x + (leftWidth / 2), y + 16, 9, SKTextAlign.Center, true);
        DrawText(canvas, columns[1].Title, materialX + (materialWidth / 2), y + 16, 9, SKTextAlign.Center, true);
        DrawText(canvas, columns[2].Title, rightX + ((width - leftWidth - materialWidth) / 2), y + 16, 9, SKTextAlign.Center, true);

        var bodyTop = y + headerHeight + 14;
        var leftLines = SurfaceSpecificationLines(sheet, sheet.Element.FrontSurface, isFront: true);
        var rightLines = SurfaceSpecificationLines(sheet, sheet.Element.BackSurface, isFront: false);
        var materialLines = MaterialSpecificationLines(sheet);
        var bodyHeight = y + height - bodyTop;
        DrawColumnLines(canvas, leftLines, x + 9, bodyTop, leftWidth - 18, bodyHeight);
        DrawColumnLines(canvas, materialLines, materialX + 9, bodyTop, materialWidth - 18, bodyHeight);
        DrawColumnLines(canvas, rightLines, rightX + 9, bodyTop, width - leftWidth - materialWidth - 18, bodyHeight);
    }
    private static void DrawCementedSpecificationTable(
        SKCanvas canvas,
        OpticalDrawingTemplate template,
        OpticalDrawingSheet sheet,
        float x,
        float y,
        float width,
        float height,
        SKPaint thin,
        SKPaint medium,
        SKPaint headerFill)
    {
        var headerHeight = template.Specification.HeaderHeight;
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
                    columnWidth - 10,
                    y + height - bodyTop);
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
                columnWidth - 10,
                y + height - bodyTop);
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
        GlassMaterialDto? material)
    {
        var template = OpticalDrawingTemplateCatalog.For(sheet.Standard);
        return ResolveFields(
            template.Specification.ComponentMaterialFields,
            new OpticalDrawingFieldContext(
                sheet,
                Surface: null,
                IsFront: false,
                SurfaceIndex: -1,
                SurfaceCount: sheet.Element.Surfaces.Count,
                Component: component,
                Material: material));
    }


    private static void DrawGb1991SpecificationTable(
        SKCanvas canvas,
        OpticalDrawingTemplate template,
        OpticalDrawingSheet sheet,
        float x,
        float y,
        float width,
        float height,
        SKPaint thin,
        SKPaint medium,
        SKPaint headerFill)
    {
        var titleHeight = template.Specification.TitleHeight;
        var sectionWidth = template.Specification.SectionWidth;
        var itemWidth = template.Specification.ItemWidth;
        var materialHeight = template.Specification.MaterialHeight;
        var partHeaderHeight = template.Specification.PartHeaderHeight;
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
            template.Specification.Title ?? "GB/T 13323—1991 旧版光学零件技术要求",
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
        OpticalDrawingTemplate template,
        OpticalDrawingSheet sheet,
        float x,
        float y,
        float width,
        float height,
        SKPaint thin,
        SKPaint medium,
        SKPaint headerFill)
    {
        var headerHeight = template.Specification.HeaderHeight;
        var subheaderHeight = template.Specification.SubheaderHeight;
        var materialWidth = width * template.Specification.MaterialWidthRatio;
        var partX = x + materialWidth;
        var partWidth = width - materialWidth;
        var surfaceWidth = template.Specification.SurfaceWidth;
        var apertureWidth = template.Specification.ApertureWidth;
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

        DrawColumnLines(
            canvas,
            GbMaterialSpecificationLines(template, sheet),
            x + 9,
            y + headerHeight + 17,
            materialWidth - 18,
            y + height - (y + headerHeight + 17));
        DrawGbSurfaceRequirement(canvas, template, sheet, sheet.Element.FrontSurface, true, partX, apertureX, requirementX, bodyTop, rowHeight, x + width);
        DrawGbSurfaceRequirement(canvas, template, sheet, sheet.Element.BackSurface, false, partX, apertureX, requirementX, bodyTop + rowHeight, rowHeight, x + width);
    }

    private static void DrawGbSurfaceRequirement(
        SKCanvas canvas,
        OpticalDrawingTemplate template,
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
        var surfaceName = isFront ? "S1" : "S2";
        DrawText(canvas, surfaceName, (partX + apertureX) / 2, rowTop + (rowHeight / 2) + 3, 8, SKTextAlign.Center, true);
        DrawText(canvas, $"⌀{sheet.Element.ClearSemiDiameter * 2:0.###}", (apertureX + requirementX) / 2, rowTop + (rowHeight / 2) + 3, 7, SKTextAlign.Center);

        var lines = ResolveFields(
            template.Specification.Gb2009SurfaceRequirementFields,
            new OpticalDrawingFieldContext(
                sheet,
                surface,
                isFront,
                SurfaceIndex: isFront ? 0 : sheet.Element.Surfaces.Count - 1,
                SurfaceCount: sheet.Element.Surfaces.Count,
                Component: null,
                Material: null));
        var lineHeight = Math.Min(11.5f, (rowHeight - 9) / lines.Count);
        for (var index = 0; index < lines.Count; index++)
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

    private static IReadOnlyList<string> GbMaterialSpecificationLines(
        OpticalDrawingTemplate template,
        OpticalDrawingSheet sheet) =>
        ResolveFields(
            template.Specification.Gb2009MaterialFields,
            new OpticalDrawingFieldContext(
                sheet,
                Surface: null,
                IsFront: false,
                SurfaceIndex: -1,
                SurfaceCount: sheet.Element.Surfaces.Count,
                Component: null,
                Material: sheet.MaterialData));

    private static IReadOnlyList<(string Item, string Value)> Gb1991MaterialSpecificationRows(
        OpticalDrawingSheet sheet)
    {
        var template = OpticalDrawingTemplateCatalog.For(sheet.Standard);
        var context = new OpticalDrawingFieldContext(
            sheet,
            Surface: null,
            IsFront: false,
            SurfaceIndex: -1,
            SurfaceCount: sheet.Element.Surfaces.Count,
            Component: null,
            Material: sheet.MaterialData);
        return template.Specification.Gb1991MaterialRows
            .Select(row => (row.Item, ResolveField(row.ValueBinding, context)))
            .ToArray();
    }

    private static IReadOnlyList<(string Surface, string Aperture, string Radius, string Requirement)> Gb1991PartSpecificationRows(
        OpticalDrawingSheet sheet)
    {
        var template = OpticalDrawingTemplateCatalog.For(sheet.Standard);
        var surfaces = sheet.Element.Surfaces;
        var rows = new List<(string Surface, string Aperture, string Radius, string Requirement)>(surfaces.Count);
        for (var index = 0; index < surfaces.Count; index++)
        {
            var surface = surfaces[index];
            var context = new OpticalDrawingFieldContext(
                sheet,
                surface,
                IsFront: index == 0,
                SurfaceIndex: index,
                SurfaceCount: surfaces.Count,
                Component: null,
                Material: null);
            rows.Add((
                $"S{index + 1}",
                $"⌀{sheet.Element.ClearSemiDiameter * 2:0.###}",
                RadiusText(surface.Radius),
                string.Join("；", template.Specification.Gb1991PartRequirementFields
                    .Select(binding => ResolveField(binding, context)))));
        }

        return rows;
    }

    internal static IReadOnlyList<string> SurfaceSpecificationLines(
        OpticalDrawingSheet sheet,
        SurfaceRowDto surface,
        bool isFront)
    {
        var template = OpticalDrawingTemplateCatalog.For(sheet.Standard);
        return ResolveFields(
            template.Specification.SurfaceFields,
            new OpticalDrawingFieldContext(
                sheet,
                surface,
                isFront,
                SurfaceIndex: isFront ? 0 : sheet.Element.Surfaces.Count - 1,
                SurfaceCount: sheet.Element.Surfaces.Count,
                Component: null,
                Material: null));
    }

    internal static string LaserDamageThresholdIndication(OpticalDrawingSheet sheet) =>
        OpticalDrawingSheet.LaserDamageThresholdIndication(sheet.LaserDamageThreshold);

    internal static IReadOnlyList<string> ValidateTemplateLayout(OpticalDrawingSheet sheet)
    {
        var template = OpticalDrawingTemplateCatalog.For(sheet.Standard);
        var issues = new List<string>();
        if (template.Page.TitleTop <= template.Page.SpecificationTop)
        {
            issues.Add($"模板 {template.Id} 的标题栏不能位于规格表之前。");
        }

        foreach (var binding in template.FieldBindings)
        {
            if (!CanResolveTemplateField(binding))
            {
                issues.Add($"模板 {template.Id} 包含未注册字段绑定：{binding}");
            }
        }

        var specificationHeight = template.Page.TitleTop - template.Page.SpecificationTop;
        if (sheet.Element.IsCemented && template.Specification.Kind != "gb1991")
        {
            var bodyHeight = specificationHeight - template.Specification.HeaderHeight - 14;
            for (var index = 0; index < sheet.Element.Surfaces.Count; index++)
            {
                CheckColumn(
                    SurfaceSpecificationLines(
                        sheet,
                        sheet.Element.Surfaces[index],
                        isFront: index == 0),
                    bodyHeight,
                    $"模板 {template.Id} 胶合面 S{index + 1}");
            }

            for (var index = 0; index < sheet.Element.Components.Count; index++)
            {
                var material = sheet.ComponentMaterialData?.ElementAtOrDefault(index)
                    ?? (index == 0 ? sheet.MaterialData : null);
                CheckColumn(
                    ComponentMaterialSpecificationLines(sheet, sheet.Element.Components[index], material),
                    bodyHeight,
                    $"模板 {template.Id} 胶合材料 L{index + 1}");
            }

            return issues;
        }

        switch (template.Specification.Kind)
        {
            case "iso-three-column":
                {
                    var bodyHeight = specificationHeight - template.Specification.HeaderHeight - 14;
                    CheckColumn(SurfaceSpecificationLines(sheet, sheet.Element.FrontSurface, isFront: true), bodyHeight, $"{template.Id} 左表面");
                    CheckColumn(MaterialSpecificationLines(sheet), bodyHeight, $"{template.Id} 材料");
                    CheckColumn(SurfaceSpecificationLines(sheet, sheet.Element.BackSurface, isFront: false), bodyHeight, $"{template.Id} 右表面");
                    break;
                }

            case "gb2009":
                {
                    var materialTopOffset = template.Specification.HeaderHeight + 17;
                    CheckColumn(
                        GbMaterialSpecificationLines(template, sheet),
                        specificationHeight - materialTopOffset,
                        $"{template.Id} 材料");
                    if (template.Specification.Gb2009SurfaceRequirementFields.Count * 9f
                        > (specificationHeight - template.Specification.HeaderHeight - template.Specification.SubheaderHeight) / 2)
                    {
                        issues.Add($"模板 {template.Id} 的 2009 零件要求行数过多，可能溢出。");
                    }

                    break;
                }

            case "gb1991":
                if (template.Specification.Gb1991MaterialRows.Count == 0
                    || template.Specification.Gb1991PartRequirementFields.Count == 0)
                {
                    issues.Add($"模板 {template.Id} 缺少 1991 材料行或零件要求字段。");
                }

                break;
        }

        return issues;

        void CheckColumn(IReadOnlyList<string> lines, float availableHeight, string label)
        {
            if (!ColumnLinesFit(lines.Count, availableHeight))
            {
                issues.Add($"{label} 的字段行无法放入规格表单元格。");
            }
        }
    }

    private static IReadOnlyList<string> MaterialSpecificationLines(OpticalDrawingSheet sheet)
    {
        var template = OpticalDrawingTemplateCatalog.For(sheet.Standard);
        return ResolveFields(
            template.Specification.MaterialFields,
            new OpticalDrawingFieldContext(
                sheet,
                Surface: null,
                IsFront: false,
                SurfaceIndex: -1,
                SurfaceCount: sheet.Element.Surfaces.Count,
                Component: null,
                Material: sheet.MaterialData));
    }

    internal static bool CanResolveTemplateField(string binding) =>
        KnownTemplateFields.Contains(binding);

    private static IReadOnlyList<string> ResolveFields(
        IReadOnlyList<string> bindings,
        OpticalDrawingFieldContext context) =>
        bindings.Select(binding => ResolveField(binding, context)).ToArray();

    private static string ResolveField(string binding, OpticalDrawingFieldContext context)
    {
        var sheet = context.Sheet;
        var surface = context.Surface;
        var material = context.Material;
        var component = context.Component;
        return binding switch
        {
            "title.standardDesignation" => StandardDesignation(sheet.Standard),
            "title.partName" => sheet.PartName,
            "title.revision" => sheet.Revision,
            "title.drawingNumber" => sheet.DrawingNumber,
            "title.pageSize" => sheet.PageSize == OpticalDrawingPageSize.A3 ? "A3" : "A4",
            "title.designer" => sheet.Designer,
            "title.reviewer" => sheet.Reviewer,
            "title.scale" => ScaleDesignation(sheet),

            "surface.radius" => $"R  {RadiusText(RequireSurface(surface, binding).Radius)}",
            "surface.radius.value" => RadiusText(RequireSurface(surface, binding).Radius),
            "element.clearAperture" => $"⌀e  {sheet.Element.ClearSemiDiameter * 2:0.###}",
            "element.clearAperture.value" => $"⌀{sheet.Element.ClearSemiDiameter * 2:0.###}",
            "surface.edgeTreatment" => $"边缘  {sheet.EdgeTreatment}",
            "surface.coating" => $"λ  {CoatingText(sheet, RequireSurface(surface, binding))}",

            "iso.surface.3.form" => $"3/  {SurfaceFormNanometers(context):0.#} nm",
            "iso.surface.4.centering" => $"4/  {sheet.CenteringToleranceArcMinutes:0.###}′",
            "iso.surface.5.imperfection" => $"5/  {sheet.SurfaceImperfection}",
            "iso.surface.6.laserDamageThreshold" => LaserDamageThresholdIndication(sheet),
            "iso.material.0.stressBirefringence" => $"0/  {sheet.StressBirefringence}",
            "iso.material.1.bubblesAndInclusions" => $"1/  {sheet.BubblesAndInclusions}",
            "iso.material.2.homogeneityAndStriae" => $"2/  {sheet.HomogeneityAndStriae}",

            "material.manufacturer" => $"制造商  {material?.Manufacturer ?? "当前玻璃库"}",
            "material.name" => $"玻璃牌号  {sheet.Element.Material}",
            "material.refractiveIndexD" => material is null
                ? "n[d]  由玻璃库解析"
                : $"n[d]  {material.RefractiveIndexD:0.000000} ±{sheet.RefractiveIndexTolerance:0.000000}",
            "material.abbeNumber" => material is null
                ? "V[d]  由玻璃库解析"
                : $"V[d]  {material.AbbeNumber:0.###} ±{sheet.AbbeNumberTolerance:0.###}",

            "component.material.name" => $"GLASS  {RequireComponent(component, binding).Material}",
            "component.material.manufacturer" => $"MAKER  {material?.Manufacturer ?? "CATALOG"}",
            "component.material.refractiveIndexD" => material is null
                ? "n[d]  CATALOG"
                : $"n[d]  {material.RefractiveIndexD:0.000000} +/-{sheet.RefractiveIndexTolerance:0.000000}",
            "component.material.abbeNumber" => material is null
                ? "V[d]  CATALOG"
                : $"V[d]  {material.AbbeNumber:0.###} +/-{sheet.AbbeNumberTolerance:0.###}",
            "component.centerThickness" => $"CT  {RequireComponent(component, binding).CenterThickness:0.###} mm",

            "gb.material.name" => $"光学材料  {sheet.Element.Material}",
            "gb.material.manufacturer" => $"制造商  {material?.Manufacturer ?? "当前玻璃库"}",
            "gb.material.refractiveIndexD" => material is null
                ? "n[d]  折射率由玻璃库解析"
                : $"n[d]  折射率 {material.RefractiveIndexD:0.000000} ±{sheet.RefractiveIndexTolerance:0.000000}",
            "gb.material.abbeNumber" => material is null
                ? "V[d]  阿贝数由玻璃库解析"
                : $"V[d]  阿贝数 {material.AbbeNumber:0.###} ±{sheet.AbbeNumberTolerance:0.###}",
            "gb.material.stressBirefringence" => $"应力双折射  {sheet.StressBirefringence}",
            "gb.material.bubblesAndInclusions" => $"气泡和夹杂  {sheet.BubblesAndInclusions}",
            "gb.material.homogeneityAndStriae" => $"均匀性和条纹  {sheet.HomogeneityAndStriae}",
            "gb.surface.radius" => $"R {RadiusText(RequireSurface(surface, binding).Radius)}",
            "gb.surface.formAndCentering" => $"面形偏差 {SurfaceFormNanometers(context):0.#} nm；偏心/倾斜 {sheet.CenteringToleranceArcMinutes:0.###}′",
            "gb.surface.imperfection" => $"表面缺陷 {sheet.SurfaceImperfection}",
            "gb.surface.texture" => $"表面纹理 Rq {sheet.SurfaceTextureNanometers:0.###} nm",
            "gb.surface.coating" => $"膜层 {CoatingText(sheet, RequireSurface(surface, binding))}",
            "gb.surface.edgeTreatment" => $"边缘 {sheet.EdgeTreatment}",

            "gb1991.material.glassAndMaker" => $"{sheet.Element.Material}；{material?.Manufacturer ?? "当前玻璃库"}",
            "gb1991.material.indexAndAbbe" => material is null
                ? "n[d]、V[d] 由玻璃库解析"
                : $"n[d] {material.RefractiveIndexD:0.000000} ±{sheet.RefractiveIndexTolerance:0.000000}；V[d] {material.AbbeNumber:0.###} ±{sheet.AbbeNumberTolerance:0.###}",
            "gb1991.material.homogeneityAndStriae" => sheet.HomogeneityAndStriae,
            "gb1991.material.stressAndBubbles" => $"{sheet.StressBirefringence}；{sheet.BubblesAndInclusions}",
            "gb1991.part.form" => $"面形 {SurfaceFormNanometers(context):0.#} nm",
            "gb1991.part.centering" => $"偏心 {sheet.CenteringToleranceArcMinutes:0.###}′",
            "gb1991.part.imperfection" => $"B {sheet.SurfaceImperfection}",
            "gb1991.part.texture" => $"Rq {sheet.SurfaceTextureNanometers:0.###} nm",
            "gb1991.part.coating" => $"膜 {CoatingText(sheet, RequireSurface(surface, binding))}",
            "gb1991.part.edgeTreatment" => $"边 {sheet.EdgeTreatment}",

            _ => $"[missing:{binding}]"
        };
    }

    private static SurfaceRowDto RequireSurface(SurfaceRowDto? surface, string binding) =>
        surface ?? throw new InvalidDataException($"Template field '{binding}' requires a surface context.");

    private static OpticalElementDefinition RequireComponent(OpticalElementDefinition? component, string binding) =>
        component ?? throw new InvalidDataException($"Template field '{binding}' requires a component context.");

    private static string CoatingText(OpticalDrawingSheet sheet, SurfaceRowDto surface) =>
        string.IsNullOrWhiteSpace(surface.Coating)
        || surface.Coating.Equals("None", StringComparison.OrdinalIgnoreCase)
            ? sheet.Coating
            : surface.Coating;

    private static double SurfaceFormNanometers(OpticalDrawingFieldContext context)
    {
        var sheet = context.Sheet;
        if (context.SurfaceIndex == 0 || context.IsFront)
        {
            return sheet.FrontSurfaceFormNanometers;
        }

        if (context.SurfaceIndex == context.SurfaceCount - 1)
        {
            return sheet.BackSurfaceFormNanometers;
        }

        return Math.Max(sheet.FrontSurfaceFormNanometers, sheet.BackSurfaceFormNanometers);
    }

    private static readonly IReadOnlySet<string> KnownTemplateFields = new HashSet<string>(
        new[]
        {
            "title.standardDesignation",
            "title.partName",
            "title.revision",
            "title.drawingNumber",
            "title.pageSize",
            "title.designer",
            "title.reviewer",
            "title.scale",
            "surface.radius",
            "surface.radius.value",
            "element.clearAperture",
            "element.clearAperture.value",
            "surface.edgeTreatment",
            "surface.coating",
            "iso.surface.3.form",
            "iso.surface.4.centering",
            "iso.surface.5.imperfection",
            "iso.surface.6.laserDamageThreshold",
            "iso.material.0.stressBirefringence",
            "iso.material.1.bubblesAndInclusions",
            "iso.material.2.homogeneityAndStriae",
            "material.manufacturer",
            "material.name",
            "material.refractiveIndexD",
            "material.abbeNumber",
            "component.material.name",
            "component.material.manufacturer",
            "component.material.refractiveIndexD",
            "component.material.abbeNumber",
            "component.centerThickness",
            "gb.material.name",
            "gb.material.manufacturer",
            "gb.material.refractiveIndexD",
            "gb.material.abbeNumber",
            "gb.material.stressBirefringence",
            "gb.material.bubblesAndInclusions",
            "gb.material.homogeneityAndStriae",
            "gb.surface.radius",
            "gb.surface.formAndCentering",
            "gb.surface.imperfection",
            "gb.surface.texture",
            "gb.surface.coating",
            "gb.surface.edgeTreatment",
            "gb1991.material.glassAndMaker",
            "gb1991.material.indexAndAbbe",
            "gb1991.material.homogeneityAndStriae",
            "gb1991.material.stressAndBubbles",
            "gb1991.part.form",
            "gb1991.part.centering",
            "gb1991.part.imperfection",
            "gb1991.part.texture",
            "gb1991.part.coating",
            "gb1991.part.edgeTreatment"
        },
        StringComparer.Ordinal);

    private readonly record struct OpticalDrawingFieldContext(
        OpticalDrawingSheet Sheet,
        SurfaceRowDto? Surface,
        bool IsFront,
        int SurfaceIndex,
        int SurfaceCount,
        OpticalElementDefinition? Component,
        GlassMaterialDto? Material);

    private static void DrawColumnLines(
        SKCanvas canvas,
        IReadOnlyList<string> lines,
        float x,
        float y,
        float maxWidth,
        float? availableHeight = null)
    {
        if (availableHeight is { } height && !ColumnLinesFit(lines.Count, height))
        {
            throw new InvalidDataException("Optical drawing template column lines exceed the available cell height.");
        }

        var lineHeight = ColumnLineHeight(lines.Count, availableHeight);
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

    private static float ColumnLineHeight(int lineCount, float? availableHeight) =>
        availableHeight is { } height && lineCount > 1
            ? Math.Min(19f, Math.Max(9f, (height - 8f) / (lineCount - 1)))
            : 19f;

    private static bool ColumnLinesFit(int lineCount, float availableHeight)
    {
        if (lineCount <= 0)
        {
            return true;
        }

        if (availableHeight <= 0 || !float.IsFinite(availableHeight))
        {
            return false;
        }

        return ((lineCount - 1) * ColumnLineHeight(lineCount, availableHeight)) <= availableHeight - 2;
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
