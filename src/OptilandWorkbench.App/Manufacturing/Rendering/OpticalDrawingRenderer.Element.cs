using System.Collections.Concurrent;
using OptilandWorkbench.Application.Contracts;
using SkiaSharp;

namespace OptilandWorkbench.App.Manufacturing;

internal static partial class OpticalDrawingRendererCore
{
    internal static void Render(
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
        axis.PathEffect = SKPathEffect.CreateDash(OpticalAxisDashPattern(sheet.Standard), 0);
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

        var scaleDesignation = DrawElementGeometry(
            canvas,
            sheet,
            new SKRect(inner + 12, inner + 12, right - 12, specificationTop - 7),
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
        var scaleRatio = DrawingScaleRatio(element);
        var drawingScale = (float)scaleRatio * MillimetersToPoints;
        var yScale = drawingScale;
        var xScale = drawingScale;
        var componentProfiles = new List<ManufacturingComponentProfile>();
        var cursorZ = 0.0;
        foreach (var component in element.Components)
        {
            var profile = BuildManufacturingComponentProfile(component, cursorZ);
            componentProfiles.Add(profile);
            cursorZ = profile.BackVertexZ;
        }

        var profilePoints = componentProfiles
            .SelectMany(profile => profile.Boundary)
            .ToArray();
        var zCenter = (profilePoints.Min(point => point.Z) + profilePoints.Max(point => point.Z)) / 2;
        var semiDiameter = Math.Max(0.1, profilePoints.Max(point => Math.Abs(point.Y)));

        SKPoint MapProfilePoint(ManufacturingProfilePoint point) => new(
            centerX + ((float)(point.Z - zCenter) * xScale),
            centerY + ((float)point.Y * yScale));

        float MapZ(double z) => centerX + ((float)(z - zCenter) * xScale);
        float MapY(double y) => centerY + ((float)y * yScale);

        var frontVertexX = MapZ(componentProfiles[0].FrontVertexZ);
        var backVertexX = MapZ(componentProfiles[^1].BackVertexZ);
        var componentGeometry = new List<(
            OpticalElementDefinition Component,
            IReadOnlyList<SKPoint> Front,
            IReadOnlyList<SKPoint> Back,
            float FrontVertex,
            float BackVertex)>();
        foreach (var profile in componentProfiles)
        {
            var componentFront = profile.Front.Select(MapProfilePoint).ToArray();
            var componentBack = profile.Back.Select(MapProfilePoint).ToArray();
            var componentBoundary = profile.Boundary.Select(MapProfilePoint).ToArray();

            using var lens = new SKPath();
            lens.MoveTo(componentBoundary[0]);
            foreach (var point in componentBoundary.Skip(1))
            {
                lens.LineTo(point);
            }

            lens.Close();
            DrawOpticalGlassHatch(canvas, lens, hatch, sheet.Standard);
            canvas.DrawPath(lens, outline);
            componentGeometry.Add((
                profile.Component,
                componentFront,
                componentBack,
                MapZ(profile.FrontVertexZ),
                MapZ(profile.BackVertexZ)));
        }
        canvas.DrawLine(area.Left + 8, centerY, area.Right - 8, centerY, axis);
        DrawText(canvas, "光轴", area.Right - 9, centerY - 5, 6.2f, SKTextAlign.Right);
        foreach (var geometry in componentGeometry)
        {
            var width = Math.Max(24, geometry.BackVertex - geometry.FrontVertex - 4);
            var componentCenterX = (geometry.FrontVertex + geometry.BackVertex) / 2;
            DrawFittedText(
                canvas,
                $"CT {geometry.Component.CenterThickness:0.###}",
                componentCenterX,
                centerY + 17,
                width,
                6.2f,
                SKTextAlign.Center);
        }


        var topY = MapY(-semiDiameter);
        var bottomY = MapY(semiDiameter);
        var allPoints = componentGeometry
            .SelectMany(geometry => geometry.Front.Concat(geometry.Back))
            .ToArray();
        var leftEdge = allPoints.Min(point => point.X);

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

        var surfaceGeometry = new List<(SurfaceRowDto Surface, IReadOnlyList<SKPoint> Points, float Vertex)>
        {
            (componentGeometry[0].Component.FrontSurface, componentGeometry[0].Front, frontVertexX)
        };
        surfaceGeometry.AddRange(componentGeometry.Select(geometry =>
            (geometry.Component.BackSurface, geometry.Back, geometry.BackVertex)));
        for (var index = 0; index < surfaceGeometry.Count; index++)
        {
            var geometry = surfaceGeometry[index];
            var tolerance = index == 0
                ? sheet.FrontRadiusTolerance
                : index == surfaceGeometry.Count - 1
                    ? sheet.BackRadiusTolerance
                    : Math.Max(sheet.FrontRadiusTolerance, sheet.BackRadiusTolerance);
            DrawSurfaceRadiusDimension(
                canvas,
                geometry.Points,
                geometry.Vertex,
                centerY,
                geometry.Surface.Radius,
                tolerance,
                xScale,
                area,
                dimension,
                upperSurface: index % 2 == 0);
            DrawText(canvas, $"S{index + 1}", geometry.Vertex, centerY - 7, 7.2f, SKTextAlign.Center, true);
        }
        return ScaleDesignation(scaleRatio);

    }
}
